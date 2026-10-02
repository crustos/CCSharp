#!/usr/bin/env python3
"""build.py -- build CC# (C# -> the C++ subset of Crust) and the two things it requires, offline.

CC# needs two sibling checkouts, beside this repository:

    ../crust    https://github.com/brentharts/crust   cpprust (C++ subset -> C) and shivyc (the C compiler)
    ../coost    https://github.com/crustos/coost      the string / fs / path / time library the corelib sits on

    python3 build.py                  deps, compiler, coost, corelib, test, examples
    python3 build.py deps             clone crust and coost beside this repo (the only step that needs a network)
    python3 build.py compiler         build build/compiler/ccs.dll with the Roslyn inside the .NET SDK (no NuGet)
    python3 build.py coost            build coost with its own build.py (and `--test` runs its tests)
    python3 build.py corelib          check the corelib: C# compiles, native helpers lower and compile
    python3 build.py test [names..]   crust/run_tests.py: each case vs real .NET, via cpprust+gcc and shivyc
    python3 build.py examples         build and run examples/*
    python3 build.py run SRC_DIR      compile a C# folder to a native executable and run it
    python3 build.py compile SRC_DIR -o EXE [--shivyc]
    python3 build.py status           what was found, and where
    python3 build.py clean

Options:
    --offline        never touch the network: a missing dependency is an error that says what to clone
    --update         `git pull --ff-only` the dependencies (deps)
    --shivyc         compile with Crust's own compiler instead of gcc (compile / run)
    --no-test        with no command: skip the test step
    --no-shivyc      test: skip the shivyc stage

Environment:  CRUST_HOME, COOST_HOME (dependency locations), DOTNET (the dotnet executable), CC.
Needs:        python3, git (deps only), a C compiler, and the .NET SDK 8+ (its Roslyn compiles the compiler).
"""
import glob
import hashlib
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
SIBLINGS = os.path.dirname(ROOT)
BUILD = os.path.join(ROOT, "build")
COMPILER_DIR = os.path.join(BUILD, "compiler")
COMPILER_DLL = os.path.join(COMPILER_DIR, "ccs.dll")

DEPS = {
    "crust": ("https://github.com/brentharts/crust.git", os.path.join("tools", "cpprust.py")),
    "coost": ("https://github.com/crustos/coost.git", os.path.join("include", "co", "fastring.h")),
}


def say(msg):
    print(msg, flush=True)


def step(name):
    say("\n== %s " % name + "=" * max(2, 66 - len(name)))


def die(msg, code=1):
    sys.stderr.write("build.py: %s\n" % msg)
    sys.exit(code)


def run(cmd, cwd=None, env=None, quiet=False):
    if not quiet:
        say("  $ " + " ".join(cmd))
    r = subprocess.run(cmd, cwd=cwd, env=env)
    if r.returncode != 0:
        die("failed (%d): %s" % (r.returncode, " ".join(cmd)), r.returncode)


def capture(cmd, cwd=None):
    r = subprocess.run(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, universal_newlines=True)
    return r.returncode, r.stdout


# ---- dependencies ---------------------------------------------------------------------------------------------

def dep_path(name):
    return os.environ.get(name.upper() + "_HOME") or os.path.join(SIBLINGS, name)


def dep_ok(name):
    return os.path.exists(os.path.join(dep_path(name), DEPS[name][1]))


def dep_rev(name):
    rc, out = capture(["git", "-C", dep_path(name), "log", "-1", "--format=%h %s"])
    return out.strip() if rc == 0 else "(not a git checkout)"


def cmd_deps(opts):
    step("dependencies (crust and coost, beside this repository)")
    for name, (url, probe) in DEPS.items():
        path = dep_path(name)
        if dep_ok(name):
            if opts["update"] and not opts["offline"] and os.path.isdir(os.path.join(path, ".git")):
                run(["git", "-C", path, "pull", "--ff-only"])
            say("  %-6s %s   %s" % (name, path, dep_rev(name)))
            continue
        if opts["offline"]:
            die("%s is not at %s and --offline forbids cloning it.\n  git clone %s %s" % (name, path, url, path))
        if os.path.exists(path) and os.listdir(path):
            die("%s exists but does not look like %s (no %s)" % (path, name, probe))
        if not shutil.which("git"):
            die("git is needed to clone %s (or put a checkout at %s)" % (name, path))
        run(["git", "clone", url, path])
        if not dep_ok(name):
            die("cloned %s but %s is missing" % (url, probe))
        say("  %-6s %s   %s" % (name, path, dep_rev(name)))


def require_deps():
    for name in DEPS:
        if not dep_ok(name):
            die("%s not found at %s.  Run `python3 build.py deps` (clones it beside this repo)." % (name, dep_path(name)))


def env_for_children():
    env = dict(os.environ)
    env["CRUST_HOME"] = dep_path("crust")
    env["COOST_HOME"] = dep_path("coost")
    env["CRUST"] = dep_path("crust")                      # coost's build.py reads CRUST
    if os.path.exists(COMPILER_DLL):
        env["CCS"] = "%s %s" % (dotnet(), COMPILER_DLL)
    return env


# ---- the compiler -----------------------------------------------------------------------------------------------

def dotnet():
    exe = os.environ.get("DOTNET") or shutil.which("dotnet")
    if not exe:
        die("the .NET SDK (dotnet) was not found.  Install .NET SDK 8 or later (its Roslyn compiles the C# compiler).")
    return exe


def sdk_roslyn():
    """(sdk major version, directory holding Microsoft.CodeAnalysis*.dll) of the newest installed SDK."""
    rc, out = capture([dotnet(), "--list-sdks"])
    best = None
    for line in out.splitlines():
        m = re.match(r"(\d+)\.(\d+)\.(\d+)\S*\s+\[(.+)\]", line.strip())
        if not m:
            continue
        ver = tuple(int(x) for x in m.groups()[:3])
        d = os.path.join(m.group(4), "%s" % line.split()[0], "Roslyn", "bincore")
        if os.path.exists(os.path.join(d, "Microsoft.CodeAnalysis.CSharp.dll")) and (best is None or ver > best[0]):
            best = (ver, d)
    if best is None:
        die("no .NET SDK with Roslyn found (`dotnet --list-sdks` printed:\n%s)\n  Install .NET SDK 8 or later." % out.strip())
    if best[0][0] < 8:
        die(".NET SDK %s is too old: 8 or later is needed." % ".".join(map(str, best[0])))
    return best[0][0], best[1]


def compiler_sources():
    return sorted(glob.glob(os.path.join(ROOT, "compiler", "src", "*.cs")))


def compiler_stamp(roslyn):
    h = hashlib.sha256()
    h.update(roslyn.encode())
    for f in compiler_sources():
        h.update(f.encode())
        with open(f, "rb") as fh:
            h.update(fh.read())
    return h.hexdigest()


CSPROJ = """<Project Sdk="Microsoft.NET.Sdk">
  <!-- generated by build.py.  The compiler is built against the Roslyn that ships inside the .NET SDK, so
       nothing is downloaded: compiler/src/CCSharpCompiler.csproj (NuGet) is the normal developer build. -->
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net{major}.0</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <UseAppHost>false</UseAppHost>
    <AssemblyName>ccs</AssemblyName>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RestoreSources>{empty}</RestoreSources>
    <NoWarn>CS8632;CS0168;CS0219;CS0162;CS0649;CS0414</NoWarn>
    <OutDir>{out}/</OutDir>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{src}/*.cs" />
    <Reference Include="Microsoft.CodeAnalysis"><HintPath>{roslyn}/Microsoft.CodeAnalysis.dll</HintPath></Reference>
    <Reference Include="Microsoft.CodeAnalysis.CSharp"><HintPath>{roslyn}/Microsoft.CodeAnalysis.CSharp.dll</HintPath></Reference>
  </ItemGroup>
</Project>
"""


def cmd_compiler(opts):
    step("compiler (Roslyn from the .NET SDK, offline)")
    major, roslyn = sdk_roslyn()
    stamp_file = os.path.join(COMPILER_DIR, "ccs.stamp")
    stamp = compiler_stamp(roslyn)
    if os.path.exists(COMPILER_DLL) and os.path.exists(stamp_file) and open(stamp_file).read() == stamp:
        say("  (current) " + os.path.relpath(COMPILER_DLL, ROOT))
        return
    proj = os.path.join(COMPILER_DIR, "proj")
    empty = os.path.join(BUILD, "empty-nuget-source")
    os.makedirs(proj, exist_ok=True)
    os.makedirs(empty, exist_ok=True)
    with open(os.path.join(proj, "ccs.csproj"), "w") as f:
        f.write(CSPROJ.format(major=major, empty=empty, out=COMPILER_DIR, src=os.path.join(ROOT, "compiler", "src"), roslyn=roslyn))
    run([dotnet(), "build", "-c", "Release", "-nologo", "-v", "q", os.path.join(proj, "ccs.csproj")])
    if not os.path.exists(COMPILER_DLL):
        die("the build reported success but %s is missing" % COMPILER_DLL)
    with open(stamp_file, "w") as f:
        f.write(stamp)
    say("  built " + os.path.relpath(COMPILER_DLL, ROOT))


def require_compiler():
    if not os.path.exists(COMPILER_DLL):
        die("the compiler is not built.  Run `python3 build.py compiler`.")


# ---- coost -------------------------------------------------------------------------------------------------------

def cmd_coost(opts):
    step("coost (built through crust's cpprust, as coost's own build.py does)")
    require_deps()
    env = env_for_children()
    run([sys.executable, "build.py"], cwd=dep_path("coost"), env=env)
    if opts["coost_test"]:
        run([sys.executable, "build.py", "test"], cwd=dep_path("coost"), env=env)


# ---- the corelib --------------------------------------------------------------------------------------------------

def cmd_corelib(opts):
    step("corelib (corelib/src compiles; corelib/native lowers and compiles)")
    require_deps()
    require_compiler()
    env = env_for_children()
    tmp = os.path.join(BUILD, "corelib-check")
    shutil.rmtree(tmp, ignore_errors=True)
    os.makedirs(os.path.join(tmp, "src"))
    with open(os.path.join(tmp, "src", "Check.cs"), "w") as f:
        f.write("class Check { static int Main() { return 0; } }\n")
    # 1. the C# corelib is the only thing a program can name: it must compile on its own
    rc, out = capture([dotnet(), COMPILER_DLL, os.path.join(tmp, "src"), "Check", "--home=" + ROOT], cwd=tmp)
    if rc != 0 or "generated" not in out:
        die("the corelib does not compile:\n" + out.strip())
    say("  corelib/src: %d files compile" % len(glob.glob(os.path.join(ROOT, "corelib", "src", "**", "*.cs"), recursive=True)))
    # 2. the native helpers: every header lowers through cpprust and compiles as C
    sys.path.insert(0, os.path.join(ROOT, "crust"))
    os.environ.update({"CRUST_HOME": dep_path("crust"), "COOST_HOME": dep_path("coost")})
    import unit                                          # noqa: E402
    heads = sorted(os.path.basename(h) for h in glob.glob(os.path.join(ROOT, "corelib", "native", "include", "cs", "*.h")))
    prog = os.path.join(tmp, "native_check.cc")
    with open(prog, "w") as f:
        f.write("".join('#include "cs/%s"\n' % h for h in heads) + "int main() { return 0; }\n")
    unit_cc, _ = unit.assemble(prog, tmp, "native_unit")
    try:
        unit.lower(unit_cc, os.path.join(tmp, "native_unit.c"))
    except RuntimeError as e:
        die("corelib/native does not lower:\n" + str(e))
    cc = os.environ.get("CC") or shutil.which("cc") or shutil.which("gcc")
    run([cc, "-w", "-o", os.path.join(tmp, "native_check"), os.path.join(tmp, "native_unit.c"), "-lm"], quiet=True)
    say("  corelib/native: %s lower and compile" % ", ".join(heads))


# ---- tests and examples -------------------------------------------------------------------------------------------

def cmd_test(opts):
    step("tests (each case vs real .NET; cpprust+gcc and shivyc)")
    require_deps()
    require_compiler()
    env = env_for_children()
    if opts["no_shivyc"]:
        env["NO_SHIVYC"] = "1"
    if not shutil.which("dotnet"):
        say("  note: no dotnet, so there is no .NET reference to compare with")
    run([sys.executable, os.path.join(ROOT, "crust", "run_tests.py")] + opts["names"], env=env)


def sources_of(d):
    return os.path.join(d, "src") if os.path.isdir(os.path.join(d, "src")) else d


def label_of(d):
    """examples/example1/src -> example1"""
    d = os.path.abspath(d)
    return os.path.basename(os.path.dirname(d)) if os.path.basename(d) == "src" else os.path.basename(d)


def cmd_examples(opts):
    step("examples")
    require_deps()
    require_compiler()
    env = env_for_children()
    os.makedirs(os.path.join(BUILD, "examples"), exist_ok=True)
    for ex in sorted(glob.glob(os.path.join(ROOT, "examples", "*"))):
        name = os.path.basename(ex)
        exe = os.path.join(BUILD, "examples", name)
        run([sys.executable, os.path.join(ROOT, "crust", "ccs2c.py"), sources_of(ex), "--exe", exe], env=env, quiet=True)
        r = subprocess.run([exe], stdout=subprocess.PIPE, universal_newlines=True)
        say("  %-10s rc=%d  %s" % (name, r.returncode, " | ".join(r.stdout.strip().splitlines())[:90]))


def cmd_run(opts):
    require_deps()
    require_compiler()
    if not opts["names"]:
        die("run needs a source folder")
    exe = os.path.join(BUILD, "run", label_of(opts["names"][0]))
    cmd = [sys.executable, os.path.join(ROOT, "crust", "ccs2c.py"), sources_of(opts["names"][0]), "--exe", exe]
    if opts["shivyc"]:
        cmd.append("--shivyc")
    run(cmd, env=env_for_children(), quiet=True)
    sys.exit(subprocess.run([exe]).returncode)


def cmd_compile(opts):
    require_deps()
    require_compiler()
    if not opts["names"] or not opts["out"]:
        die("usage: build.py compile SRC_DIR -o EXE [--shivyc]")
    cmd = [sys.executable, os.path.join(ROOT, "crust", "ccs2c.py"), sources_of(opts["names"][0]), "--exe", os.path.abspath(opts["out"])]
    if opts["shivyc"]:
        cmd.append("--shivyc")
    run(cmd, env=env_for_children(), quiet=True)
    say("  built " + opts["out"])


# ---- status / clean -----------------------------------------------------------------------------------------------

def cmd_status(opts):
    step("status")
    say("  repo      %s" % ROOT)
    for name in DEPS:
        say("  %-9s %s  %s" % (name, dep_path(name) if dep_ok(name) else dep_path(name) + "  (MISSING: python3 build.py deps)",
                              dep_rev(name) if dep_ok(name) else ""))
    d = os.environ.get("DOTNET") or shutil.which("dotnet")
    say("  dotnet    %s" % (d or "(not found)"))
    if d:
        try:
            major, roslyn = sdk_roslyn()
            say("  roslyn    %s  (SDK %d)" % (roslyn, major))
        except SystemExit:
            pass
    say("  compiler  %s" % (COMPILER_DLL if os.path.exists(COMPILER_DLL) else "(not built: python3 build.py compiler)"))
    say("  cc        %s" % (os.environ.get("CC") or shutil.which("cc") or shutil.which("gcc") or "(not found)"))
    say("  git       %s" % (shutil.which("git") or "(not found)"))


def cmd_clean(opts):
    step("clean")
    shutil.rmtree(BUILD, ignore_errors=True)
    shutil.rmtree(os.path.join(ROOT, "crust", "out"), ignore_errors=True)
    if dep_ok("coost") and dep_ok("crust"):
        subprocess.run([sys.executable, "build.py", "clean"], cwd=dep_path("coost"), env=env_for_children())
    say("  removed build/ (the dependencies themselves are left alone)")


def cmd_all(opts):
    cmd_deps(opts)
    cmd_compiler(opts)
    cmd_coost(opts)
    cmd_corelib(opts)
    if not opts["no_test"]:
        cmd_test(opts)
    cmd_examples(opts)
    step("done")
    say("  python3 build.py run examples/example1/src")


COMMANDS = {
    "all": cmd_all, "deps": cmd_deps, "compiler": cmd_compiler, "coost": cmd_coost, "corelib": cmd_corelib,
    "test": cmd_test, "examples": cmd_examples, "run": cmd_run, "compile": cmd_compile, "status": cmd_status,
    "clean": cmd_clean,
}


def main(argv):
    opts = {"offline": False, "update": False, "shivyc": False, "no_test": False, "no_shivyc": False,
            "coost_test": False, "out": None, "names": []}
    cmd = None
    i = 0
    while i < len(argv):
        a = argv[i]
        if a in ("-h", "--help", "help"):
            print(__doc__)
            return 0
        if a == "--offline":
            opts["offline"] = True
        elif a == "--update":
            opts["update"] = True
        elif a == "--shivyc":
            opts["shivyc"] = True
        elif a == "--no-test":
            opts["no_test"] = True
        elif a == "--no-shivyc":
            opts["no_shivyc"] = True
        elif a == "--test":
            opts["coost_test"] = True
        elif a == "-o":
            i += 1
            opts["out"] = argv[i] if i < len(argv) else None
        elif cmd is None and a in COMMANDS:
            cmd = a
        elif a.startswith("-"):
            die("unknown option %s (see --help)" % a)
        else:
            opts["names"].append(a)
        i += 1
    COMMANDS[cmd or "all"](opts)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
