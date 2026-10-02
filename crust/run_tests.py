#!/usr/bin/env python3
"""run_tests -- every case in crust/tests runs through CC# --crust -> cpprust -> C -> native,
and its stdout + exit status are compared with the same C# run on real .NET.

    CRUST_HOME=~/crust CCS="dotnet CCSharpCompiler.dll" python3 crust/run_tests.py [name ...]

A case is  tests/NAME.cs  (or a folder tests/NAME/ of .cs files).  The first lines may carry:

    // refuse: some text     the compile must be REFUSED, and the message must contain the text
    // expect-rc: 3          override the reference exit status (when there is no .NET to ask)

Refusals are tested as pinned behaviour: for this subset a refusal is the deliverable.
"""
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ccs2c                                            # noqa: E402

TESTS = os.path.join(HERE, "tests")


def dotnet_reference(src_dir):
    """Run the case on real .NET.  None if there is no SDK."""
    dotnet = shutil.which("dotnet")
    if not dotnet:
        return None
    roots = glob.glob("/usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll") + glob.glob("/usr/share/dotnet/sdk/*/Roslyn/bincore/csc.dll")
    shared = (glob.glob("/usr/lib/dotnet/shared/Microsoft.NETCore.App/8.*") + glob.glob("/usr/share/dotnet/shared/Microsoft.NETCore.App/8.*"))
    if not roots or not shared:
        return None
    csc, rt = roots[0], shared[0]
    tmp = tempfile.mkdtemp(prefix="ccs-ref-")
    try:
        files = []
        for root, _, fs in os.walk(src_dir):
            files += [os.path.join(root, f) for f in fs if f.endswith(".cs")]
        refs = ["-r:" + os.path.join(rt, n) for n in
                ("System.Private.CoreLib.dll", "System.Runtime.dll", "System.Console.dll", "System.Collections.dll")]
        exe = os.path.join(tmp, "ref.dll")
        p = subprocess.run([dotnet, csc, "-nologo", "-nowarn:0626,0219,0168,0414,0649,0169", "-unsafe", "-out:" + exe,
                            "-t:exe", "-noconfig", "-nostdlib"] + refs + sorted(files),
                           stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if p.returncode != 0:
            return ("compile-error", p.stdout.decode("utf-8", "replace"))
        with open(os.path.join(tmp, "ref.runtimeconfig.json"), "w") as f:
            f.write('{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"%s"}}}'
                    % os.path.basename(rt))
        r = subprocess.run([dotnet, exe], stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=60)
        return (r.returncode & 0xFF, r.stdout.decode("utf-8", "replace").replace("\r\n", "\n"))
    finally:
        shutil.rmtree(tmp, ignore_errors=True)


def cases(names):
    out = []
    for p in sorted(glob.glob(os.path.join(TESTS, "*"))):
        base = os.path.basename(p)
        name = base[:-3] if base.endswith(".cs") else base
        if names and name not in names:
            continue
        if os.path.isdir(p) or base.endswith(".cs"):
            out.append((name, p))
    return out


def header(p):
    first = p
    if os.path.isdir(p):
        fs = sorted(glob.glob(os.path.join(p, "*.cs")))
        first = fs[0]
    meta = {}
    with open(first) as f:
        for line in f:
            m = re.match(r"//\s*(refuse|expect-rc):\s*(.*)$", line)
            if m:
                meta[m.group(1)] = m.group(2).strip()
            elif line.strip() and not line.startswith("//"):
                break
    return meta


def check_lines(src_dir, cpp_dir):
    """Line numbers are preserved: a `return` on C# line N is on (virtual) line N of the emitted C++.
    This is what lets a Crust diagnostic point at the .cs line.  `#line` resyncs a type that was moved."""
    problems = []
    for cs in sorted(glob.glob(os.path.join(src_dir, "**", "*.cs"), recursive=True)):
        out = os.path.join(cpp_dir, os.path.basename(cs)[:-3] + ".cpp")
        if not os.path.exists(out):
            continue
        virt = {}
        cur = 0
        for i, line in enumerate(open(out).read().split("\n"), 1):
            m = re.match(r"#line (\d+)", line)
            if m:
                cur = int(m.group(1)) - 1
                continue
            cur = cur + 1 if cur else i
            virt.setdefault(cur, []).append(line)
        for n, line in enumerate(open(cs).read().split("\n"), 1):
            if line.strip().startswith("//"):
                continue
            if re.search(r"\breturn\b", line) and not any("return" in l for l in virt.get(n, [])):
                problems.append("%s:%d `return` is not on line %d of the output" % (os.path.basename(cs), n, n))
    return problems


def run_case(name, path):
    meta = header(path)
    src = path
    tmpdir = None
    if not os.path.isdir(path):
        tmpdir = tempfile.mkdtemp(prefix="ccs-case-")
        shutil.copy(path, os.path.join(tmpdir, "Case.cs"))
        src = tmpdir
    try:
        if "refuse" in meta:
            try:
                cpp, d = ccs2c.to_cpp(src)
                ccs2c.to_c(cpp, d)
            except ccs2c.Refused as e:
                return (meta["refuse"] in str(e), "refused: " + str(e).splitlines()[-2][:150] if str(e).strip() else str(e))
            except Exception as e:
                return (meta["refuse"] in str(e), "cpprust refused: " + str(e)[:150])
            return (False, "expected a refusal containing %r, got a translation" % meta["refuse"])
        ref = dotnet_reference(src)
        try:
            cpp, d = ccs2c.to_cpp(src)
            lp = check_lines(src, d)
            if lp:
                return (False, "line numbers moved: " + "; ".join(lp[:3]))
            c = ccs2c.to_c(cpp, d)
            rc, out = ccs2c.build_run(c)
            note = ""
            if os.environ.get("NO_SHIVYC") != "1":
                try:
                    src_rc, src_out = ccs2c.build_run_shivyc(c)   # Crust's own compiler must agree with gcc
                    if (src_rc & 0xFF, src_out) != (rc & 0xFF, out):
                        return (False, "shivyc and gcc disagree: shivyc rc=%s out=%r, gcc rc=%s out=%r"
                                % (src_rc & 0xFF, src_out[:200], rc & 0xFF, out[:200]))
                except ccs2c.ShivycUnavailable as e:
                    note = "  [gcc only: %s]" % e
        except ccs2c.Refused as e:
            return (False, str(e)[-600:])
        except Exception as e:
            return (False, "cpprust: " + str(e)[:600])
        if ref is None:
            exp = int(meta.get("expect-rc", "0"))
            return (rc == exp, "no .NET reference; rc=%d%s" % (rc, note))
        if ref[0] == "compile-error":
            return (False, "reference does not compile:\n" + ref[1][-500:])
        if (rc & 0xFF, out) != (ref[0], ref[1]):
            return (False, "MISMATCH\n  .NET : rc=%s out=%r\n  crust: rc=%s out=%r" % (ref[0], ref[1][:300], rc & 0xFF, out[:300]))
        return (True, "rc=%d%s" % (rc, note))
    finally:
        if tmpdir:
            shutil.rmtree(tmpdir, ignore_errors=True)


def main(argv):
    names = set(argv[1:])
    bad = 0
    cs = cases(names)
    for name, path in cs:
        ok, msg = run_case(name, path)
        print("%-4s %-28s %s" % ("ok" if ok else "FAIL", name, msg if not ok or msg.startswith("refused") else msg))
        bad += 0 if ok else 1
    print("\n%d/%d passed" % (len(cs) - bad, len(cs)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
