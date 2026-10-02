"""unit -- turn a generated CC# program into one Crust translation unit and lower it to C.

A CC# program is C++-subset text that `#include`s corelib/coost headers.  Crust consumes C++ as ONE unit
(no link boundary), so the coost sources the program's headers reach are spliced in beside it -- the same
rule coost's own build.py uses for its tests (src/X.cc joins when include/co/X.h is in the include closure).
coost's plain-C objects (sockets, http) are linked in.

    CRUST_HOME / COOST_HOME   default to ../crust and ../coost, side by side with this repo.
"""
import hashlib
import importlib.util
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SIBLINGS = os.path.dirname(REPO)


def crust_home():
    return os.environ.get("CRUST_HOME") or os.path.join(SIBLINGS, "crust")


def coost_home():
    return os.environ.get("COOST_HOME") or os.path.join(SIBLINGS, "coost")


def native_include():
    return os.path.join(REPO, "corelib", "native", "include")


def require():
    for name, p, probe in (("crust", crust_home(), os.path.join("tools", "cpprust.py")),
                           ("coost", coost_home(), os.path.join("include", "co", "fastring.h"))):
        if not os.path.exists(os.path.join(p, probe)):
            sys.exit("%s not found at %s -- run `python3 build.py deps` (it clones crust and coost beside "
                     "this repo), or set %s_HOME" % (name, p, name.upper()))


_cb = None


def coost_build():
    """coost's own build.py, loaded as a module (its helpers know its source list and its C objects)."""
    global _cb
    if _cb is None:
        require()
        os.environ.setdefault("CRUST", crust_home())
        spec = importlib.util.spec_from_file_location("coost_build", os.path.join(coost_home(), "build.py"))
        _cb = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(_cb)
    return _cb


INC_RE = re.compile(r'^[ \t]*#[ \t]*include[ \t]*([<"])([^">]+)[">]', re.M)


def closure(paths, incdirs):
    """Every file reachable from `paths` through #include (quoted: includer's dir first, then incdirs)."""
    seen = set()
    stack = [os.path.normpath(p) for p in paths]
    while stack:
        p = stack.pop()
        if p in seen:
            continue
        seen.add(p)
        with open(p, errors="replace") as f:
            text = f.read()
        for kind, name in INC_RE.findall(text):
            cands = [os.path.join(os.path.dirname(p), name)] if kind == '"' else []
            cands += [os.path.join(d, name) for d in incdirs]
            for c in cands:
                if os.path.isfile(c):
                    stack.append(os.path.normpath(c))
                    break
    return seen


def incdirs():
    return [os.path.join(coost_home(), "include"), native_include()]


def select_sources(program):
    """coost sources a program needs: src/X.cc joins when include/co/X.h is in the closure (to a fixpoint)."""
    cb = coost_build()
    inc = incdirs()
    files = closure([program], inc)
    chosen = set()
    changed = True
    while changed:
        changed = False
        for s in cb.SOURCES:
            if s in chosen:
                continue
            h = cb.header_of(s)
            if h is None or h in files:
                chosen.add(s)
                files |= closure([os.path.join(cb.ROOT, s)], inc)
                changed = True
    return [s for s in cb.SOURCES if s in chosen], files


def assemble(program, outdir, name="unit"):
    """Write outdir/name.cc = coost sources + the program; returns (path, include closure)."""
    cb = coost_build()
    os.makedirs(outdir, exist_ok=True)
    sources, files = select_sources(program)
    text = "/* generated -- coost sources + the CC# program */\n"
    text += "".join('#include "%s"\n' % os.path.join(cb.ROOT, s) for s in sources)
    text += '#include "%s"\n' % os.path.abspath(program)
    path = os.path.join(outdir, name + ".cc")
    if not os.path.exists(path) or open(path).read() != text:
        with open(path, "w") as f:
            f.write(text)
    return path, files


def lower(unit_cc, out_c):
    """cpprust: C++ subset -> C.  Called in-process (not the CLI) because C# allows a type to use one declared
    below it, which is cpprust's `any_order`; that is API-only.  Everything else is coost's own build.py setting.
    Raises RuntimeError with cpprust's diagnostic."""
    require()
    home = crust_home()
    if home not in sys.path:
        sys.path.insert(0, home)
    import tools.cpprust as cpprust
    with open(unit_cc) as f:
        text = f.read()
    try:
        c = cpprust.translate(text, path=unit_cc, basedir=os.path.dirname(unit_cc), incdirs=incdirs(),
                              defines=("CO_CRUST",), clang=False, any_order=True)
    except Exception as e:                      # CppError
        raise RuntimeError(getattr(e, "message", None) and str(e) or str(e))
    c = cpprust._sub_code(r"(?<![\w])NULL(?![\w])", lambda m: "((void *)0)", c)
    c = c.replace("#pragma once\n", "")        # headers are spliced into one unit; their guards are noise
    with open(out_c, "w") as f:
        f.write(c)
    return c


def c_objects():
    """coost's plain-C objects (built on demand by coost's own rules)."""
    return coost_build().c_objects()
