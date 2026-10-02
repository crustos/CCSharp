#!/usr/bin/env python3
"""ccs2c -- CC# (Roslyn) C# -> Crust C++ subset -> C, and optionally build + run.

    python3 crust/ccs2c.py INPUT... [options]

INPUT is any mix of C# files, folders, .csproj and .sln (see crust/inputs.py); together they are ONE program.

    --main=Class      the class whose static Main is the entry point (needed when there are several)
    --name=Name       the program's name (default: the first input's name)
    --convert=DIR     write the generated C++ into DIR            (add --c for the lowered, self-contained C too)
    -o FILE.c         write the lowered C to FILE.c
    --exe=PATH        build a native executable               (gcc by default; --shivyc uses Crust's own compiler)
    --run             build and run it; arguments after `--` go to the program
    --cc=CC           the C compiler (default: $CC, else cc / gcc)
    --debug           -g -O0 instead of -O2
    --shivyc          compile with Crust's own compiler (shivyc) instead of gcc

Pipeline:
    CCSharpCompiler --srclist=...       Roslyn: the C# files -> cpp/*.cpp
    unit.py + tools/cpprust.py          the coost sources they reach are spliced in, then lowered to C
    cc                                  the executable (only with --exe / --run)

Environment (all default to what `python3 build.py` sets up):
    CRUST_HOME   a checkout of https://github.com/brentharts/crust   (default: ../crust)
    COOST_HOME   a checkout of https://github.com/crustos/coost      (default: ../coost)
    CCS          command that runs the compiler, e.g. "dotnet build/compiler/ccs.dll"
    CC           the C compiler
"""
import os
import re
import shlex
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import inputs as inputs_mod                              # noqa: E402
import unit                                              # noqa: E402


def crust_home():
    unit.require()
    return unit.crust_home()


def compiler_cmd():
    """CCS, else the compiler build.py makes (build/compiler/ccs.dll), else the offline-build output."""
    c = os.environ.get("CCS")
    if c:
        return shlex.split(c)
    for cand in (os.path.join(REPO, "build", "compiler", "ccs.dll"), os.path.join(HERE, "out", "ccs.dll")):
        if os.path.exists(cand):
            return ["dotnet", cand]
    sys.exit("ccs2c: no compiler: run `python3 build.py compiler` (or set CCS)")


def c_compiler(cc=None):
    c = cc or os.environ.get("CC") or shutil.which("cc") or shutil.which("gcc")
    if not c:
        sys.exit("ccs2c: no C compiler found (install gcc, or pass --cc / set CC)")
    return c


class Refused(Exception):
    pass


class ShivycUnavailable(Exception):
    """shivyc cannot compile this program for a reason that is shivyc's (a glibc header it does not bundle)."""


def to_cpp(inputs, main=None, workdir=None, name=None):
    """C# inputs (files / folders / .csproj / .sln, one program) in; returns (cpp_text, cpp_dir).
    Raises Refused with the diagnostics."""
    if isinstance(inputs, str):
        inputs = [inputs]
    try:
        files = inputs_mod.expand(inputs)
    except inputs_mod.InputError as e:
        raise Refused(str(e))
    name = name or "Program"
    work = workdir or tempfile.mkdtemp(prefix="ccs2c-")
    listing = os.path.join(work, "sources.txt")
    with open(listing, "w") as f:
        f.write("\n".join(files) + "\n")
    cmd = compiler_cmd() + [files[0], name, "--crust", "--home=" + REPO, "--srclist=" + listing]
    if main:
        cmd.append("--main=" + main)
    p = subprocess.run(cmd, cwd=work, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    out = p.stdout.decode("utf-8", "replace")
    if p.returncode != 0:
        raise Refused("\n".join(l for l in out.strip().splitlines() if not l.startswith("build:")))
    cpp_dir = os.path.join(work, "cpp")
    with open(os.path.join(cpp_dir, name + ".main.cpp")) as f:
        return f.read(), cpp_dir


def to_c(cpp_text, cpp_dir, name="Program"):
    """The program's translation unit (coost sources spliced in), lowered to C by cpprust."""
    prog = os.path.join(cpp_dir, name + ".main.cpp")
    unit_cc, _ = unit.assemble(prog, cpp_dir, "unit")
    try:
        return unit.lower(unit_cc, os.path.join(cpp_dir, "unit.c"))
    except RuntimeError as e:
        raise Refused("cpprust: " + str(e))


def _cflags(debug):
    return ["-g", "-O0", "-w"] if debug else ["-O2", "-w"]


def build_run(c_text, args=(), timeout=30, cc=None):
    """Compile lowered C with the C compiler (gcc) and run it: (exit status, stdout)."""
    tmp = tempfile.mkdtemp(prefix="ccs2c-run-")
    try:
        cfile = os.path.join(tmp, "p.c")
        with open(cfile, "w") as f:
            f.write(c_text)
        exe = os.path.join(tmp, "p")
        p = subprocess.run([c_compiler(cc), "-w", "-o", exe, cfile, "-lm"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if p.returncode != 0:
            raise Refused("generated C did not compile:\n" + p.stdout.decode("utf-8", "replace"))
        r = subprocess.run([exe] + list(args), stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
        return r.returncode, r.stdout.decode("utf-8", "replace")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def build_run_shivyc(c_text, args=(), timeout=60):
    """Compile with Crust's own compiler (shivyc) and run.  Experimental: see README."""
    home = crust_home()
    tmp = tempfile.mkdtemp(prefix="ccs2c-shivyc-")
    try:
        cfile = os.path.join(tmp, "p.c")
        with open(cfile, "w") as f:
            f.write(c_text)
        exe = os.path.join(tmp, "p")
        p = subprocess.run([sys.executable, os.path.join(home, "crust"), cfile, "-o", exe],
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=300)
        if p.returncode != 0:
            msg = re.sub(r"\x1b\[[0-9;]*m", "", p.stdout.decode("utf-8", "replace"))
            #the only excuse accepted: a system header shivyc does not bundle (coost's fs / time need <errno.h>)
            m = re.search(r"error: unable to read included file\s*\n\s*#include <([\w./]+)>", msg)
            if m:
                raise ShivycUnavailable("<%s> is not bundled with shivyc" % m.group(1))
            raise Refused("shivyc rejected the generated C:\n" + msg.strip()[-600:])
        r = subprocess.run([exe] + list(args), stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
        return r.returncode, r.stdout.decode("utf-8", "replace")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def build_exe(c_text, exe, shivyc=False, cc=None, debug=False):
    """Compile lowered C to a native executable: gcc (or $CC / --cc), or Crust's own compiler (shivyc)."""
    d = os.path.dirname(os.path.abspath(exe))
    os.makedirs(d, exist_ok=True)
    cfile = exe + ".c"
    with open(cfile, "w") as f:
        f.write(c_text)
    if shivyc:
        cmd = [sys.executable, os.path.join(crust_home(), "crust"), cfile, "-o", exe]
    else:
        cmd = [c_compiler(cc)] + _cflags(debug) + ["-o", exe, cfile, "-lm"]
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if p.returncode != 0:
        raise Refused("C compiler failed:\n" + re.sub(r"\x1b\[[0-9;]*m", "", p.stdout.decode("utf-8", "replace")).strip()[-800:])


def convert(cpp_dir, dest, name, c_text=None):
    """Copy the generated C++ (and, if given, the lowered C) into dest.  Returns the files written."""
    os.makedirs(dest, exist_ok=True)
    written = []
    for f in sorted(os.listdir(cpp_dir)):
        if f.endswith(".cpp") and f != "unit.cc":
            shutil.copy(os.path.join(cpp_dir, f), os.path.join(dest, f))
            written.append(os.path.join(dest, f))
    if c_text is not None:
        p = os.path.join(dest, name + ".c")
        with open(p, "w") as f:
            f.write(c_text)
        written.append(p)
    return written


def parse_args(argv):
    o = {"inputs": [], "main": None, "name": None, "convert": None, "c": False, "out": None, "exe": None,
         "run": False, "cc": None, "debug": False, "shivyc": False, "args": []}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a == "--":
            o["args"] = argv[i + 1:]
            break
        if a.startswith("--main="):
            o["main"] = a[7:]
        elif a.startswith("--name="):
            o["name"] = a[7:]
        elif a.startswith("--convert="):
            o["convert"] = a[10:]
        elif a.startswith("--exe="):
            o["exe"] = a[6:]
        elif a.startswith("--cc="):
            o["cc"] = a[5:]
        elif a == "--exe":
            i += 1
            o["exe"] = argv[i]
        elif a == "-o":
            i += 1
            o["out"] = argv[i]
        elif a == "--c":
            o["c"] = True
        elif a == "--run":
            o["run"] = True
        elif a == "--debug":
            o["debug"] = True
        elif a == "--shivyc":
            o["shivyc"] = True
        elif a.startswith("-"):
            sys.exit("ccs2c: unknown option %s" % a)
        else:
            o["inputs"].append(a)
        i += 1
    return o


def main(argv):
    if not argv or argv[0] in ("-h", "--help"):
        print(__doc__)
        return 2
    o = parse_args(argv)
    if not o["inputs"]:
        print("ccs2c: no input (a .cs file, a folder, a .csproj or a .sln)")
        return 2
    name = o["name"] or inputs_mod.project_name(o["inputs"])
    need_c = o["c"] or o["out"] or o["exe"] or o["run"] or not o["convert"]
    try:
        cpp, cpp_dir = to_cpp(o["inputs"], o["main"], name=name)
        c = to_c(cpp, cpp_dir, name) if need_c else None
        if o["convert"]:
            for f in convert(cpp_dir, o["convert"], name, c if o["c"] else None):
                print("wrote " + f)
        if o["exe"]:
            build_exe(c, o["exe"], shivyc=o["shivyc"], cc=o["cc"], debug=o["debug"])
            print("built " + o["exe"])
        if o["out"]:
            with open(o["out"], "w") as f:
                f.write(c)
            print("wrote " + o["out"])
        if o["run"]:
            tmp = tempfile.mkdtemp(prefix="ccs2c-exe-")
            exe = os.path.join(tmp, name)
            build_exe(c, exe, shivyc=o["shivyc"], cc=o["cc"], debug=o["debug"])
            r = subprocess.run([exe] + o["args"])
            shutil.rmtree(tmp, ignore_errors=True)
            return r.returncode
        if not (o["convert"] or o["exe"] or o["out"]):
            sys.stdout.write(c)
    except Refused as e:
        print(e)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
