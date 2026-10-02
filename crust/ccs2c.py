#!/usr/bin/env python3
"""ccs2c -- CC# (Roslyn) C# -> Crust C++ subset -> C, and optionally build + run.

    python3 crust/ccs2c.py src_dir [--main=Class] [-o out.c] [--exe path [--shivyc]] [--run [--shivyc]]

Pipeline:
    CCSharpCompiler src_dir Project --crust     (Roslyn; writes cpp/*.cpp)
    tools/cpprust.py                            (Crust; C++ subset -> C)
    cc                                          (only with --run)

Environment (all default to what `python3 build.py` sets up):
    CRUST_HOME   a checkout of https://github.com/brentharts/crust   (default: ../crust)
    COOST_HOME   a checkout of https://github.com/crustos/coost      (default: ../coost)
    CCS          command that runs the compiler, e.g. "dotnet build/compiler/ccs.dll"
"""
import os
import shlex
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import unit                                             # noqa: E402


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


class Refused(Exception):
    pass


class ShivycUnavailable(Exception):
    """shivyc cannot compile this program for a reason that is shivyc's (a glibc header it does not bundle)."""


def to_cpp(src_dir, main=None, workdir=None):
    """C# folder in; returns (cpp_text, cpp_dir). Raises Refused with the diagnostics."""
    work = workdir or tempfile.mkdtemp(prefix="ccs2c-")
    cmd = compiler_cmd() + [os.path.abspath(src_dir), "Program", "--crust", "--home=" + REPO]
    if main:
        cmd.append("--main=" + main)
    p = subprocess.run(cmd, cwd=work, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    out = p.stdout.decode("utf-8", "replace")
    if p.returncode != 0:
        raise Refused(out.strip())
    cpp_dir = os.path.join(work, "cpp")
    with open(os.path.join(cpp_dir, "Program.cpp")) as f:
        return f.read(), cpp_dir


def to_c(cpp_text, cpp_dir, path="Program.cpp"):
    """The program's translation unit (coost sources spliced in), lowered to C by cpprust."""
    prog = os.path.join(cpp_dir, "Program.cpp")
    unit_cc, _ = unit.assemble(prog, cpp_dir, "unit")
    try:
        return unit.lower(unit_cc, os.path.join(cpp_dir, "unit.c"))
    except RuntimeError as e:
        raise Refused("cpprust: " + str(e))


def build_run(c_text, args=(), timeout=30):
    tmp = tempfile.mkdtemp(prefix="ccs2c-run-")
    try:
        cfile = os.path.join(tmp, "p.c")
        with open(cfile, "w") as f:
            f.write(c_text)
        exe = os.path.join(tmp, "p")
        cc = shutil.which("gcc") or shutil.which("cc")
        p = subprocess.run([cc, "-w", "-o", exe, cfile, "-lm"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if p.returncode != 0:
            raise Refused("generated C did not compile:\n" + p.stdout.decode("utf-8", "replace"))
        r = subprocess.run([exe] + list(args), stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=timeout)
        return r.returncode, r.stdout.decode("utf-8", "replace")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def build_run_shivyc(c_text, args=(), timeout=60):
    """Compile with Crust's own compiler (shivyc) -- the real target -- and run."""
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
            import re
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


def build_exe(c_text, exe, shivyc=False):
    """Compile lowered C to a native executable with gcc, or with Crust's own compiler (shivyc)."""
    d = os.path.dirname(os.path.abspath(exe))
    os.makedirs(d, exist_ok=True)
    cfile = exe + ".c"
    with open(cfile, "w") as f:
        f.write(c_text)
    if shivyc:
        cmd = [sys.executable, os.path.join(crust_home(), "crust"), cfile, "-o", exe]
    else:
        cmd = [shutil.which("gcc") or shutil.which("cc"), "-O2", "-w", "-o", exe, cfile, "-lm"]
    p = subprocess.run(cmd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if p.returncode != 0:
        import re
        raise Refused("C compiler failed:\n" + re.sub(r"\x1b\[[0-9;]*m", "", p.stdout.decode("utf-8", "replace")).strip()[-800:])


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    src = argv[1]
    mainc = None
    out = None
    exe = None
    run = False
    i = 2
    while i < len(argv):
        a = argv[i]
        if a.startswith("--main="):
            mainc = a[7:]
        elif a == "-o":
            i += 1
            out = argv[i]
        elif a == "--run":
            run = True
        elif a == "--exe":
            i += 1
            exe = argv[i]
        i += 1
    try:
        cpp, cpp_dir = to_cpp(src, mainc)
        c = to_c(cpp, cpp_dir)
    except Refused as e:
        print(e)
        return 1
    except Exception as e:                              # CppError from cpprust
        print("cpprust: %s" % e)
        return 1
    if exe:
        try:
            build_exe(c, exe, shivyc="--shivyc" in argv)
        except Refused as e:
            print(e)
            return 1
        print("built " + exe)
        return 0
    if out:
        with open(out, "w") as f:
            f.write(c)
    if run:
        rc, so = (build_run_shivyc(c) if "--shivyc" in argv else build_run(c))
        sys.stdout.write(so)
        return rc
    if not out:
        sys.stdout.write(c)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
