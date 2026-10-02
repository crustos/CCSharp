CC#
===

C# -> C++ = CC#

  CC# is a C# cross compiler written in C# on top of Roslyn.  It turns a C# program into the C++ subset that
Crust (https://github.com/brentharts/crust) accepts; Crust's cpprust lowers that to C, and a C compiler (gcc, or
Crust's own shivyc) makes the executable.

      C# --Roslyn--> Crust C++ subset --cpprust--> C --shivyc / cc--> native
       (CC#, this repo)                (crust)           (crust)

  This is the next evolution in C# to C++ compilers, based on the Q# project, which tried to use attributes to
inject C++ code directly into the C# source, which was a mess.  CC# instead declares members in a C# corelib and
implements them in C++ files, which keeps the C# clean.

  Crust and coost are REQUIRED.  There is no garbage collector, no .NET class library, and no Qt.

      ../crust   https://github.com/brentharts/crust   cpprust (C++ subset -> C) and shivyc (the C compiler)
      ../coost   https://github.com/crustos/coost      strings, files, paths and clocks, in the same C++ subset

  They are checked out beside this repository.  build.py clones them for you.


Quick start
===========

  python3 build.py                  clone crust + coost, build the compiler, build coost, check the corelib,
                                    run the tests, build and run the examples
  python3 build.py run examples/example1/src
  python3 build.py compile examples/example1/src -o hello          (add --shivyc to use Crust's own C compiler)

  build.py works offline once crust and coost are present (python3 build.py --offline).  See `python3 build.py -h`.

  Requirements:  python3, git (to clone the two dependencies), a C compiler, and the .NET SDK 8 or later.
  The compiler is built with the Roslyn that ships inside the .NET SDK, so nothing is downloaded from NuGet.
  Windows and Linux were both supported by the old back end; the Crust back end has only been run on Linux.


What it means to be a C# program here
=====================================

  The semantics are Crust's, not .NET's (see crust/README.md for the whole mapping):

    * A class is single ownership: a value, destroyed at scope exit.  There is no garbage collector.
      Class, array and List<T> parameters are borrowed.  `Node b = a;` would alias, so it is refused.
    * string is coost's fastring, held by value.  C# strings are immutable, so a copy cannot be told from sharing.
      There is no null string, and no char (a C# char is UTF-16).
    * There is no catch.  An exception can never be observed, so a failure that .NET would throw (int.Parse("x"),
      Substring out of range) is an unhandled exception here too: the process ends, with the same exit status.
    * Arithmetic wraps, as in C#'s default unchecked context.

  Anything outside the subset is REFUSED, at its C# line, with the reason and what to write instead.  Nothing is
approximated: a refusal is the deliverable.  Where C# would alias an object and a single-owner value would silently
copy it, CC# refuses instead of miscompiling.


Corelib
=======

  corelib/src      The C# class library, compiled by Roslyn INSTEAD of the .NET reference assemblies, so it is also
                   the whole of what a program can name.  Members with no body are implemented in C++:

                       [Cpp("{this}.substr({0})")]  public extern string Substring(int startIndex);

                   A member declared with no [Cpp] is part of the .NET surface that is not implemented yet: using
                   it is refused, naming the member.

  corelib/native   The C++ side (headers in the Crust C++ subset, on top of coost): cs/core.h (strings, numbers,
                   parsing), cs/io.h (files), cs/math.h, cs/time.h.

  What exists: string (Length, Substring, IndexOf, LastIndexOf, Contains, StartsWith, EndsWith, Replace, ToUpper,
  ToLower, Trim, Equals, IsNullOrEmpty), StringBuilder, Console.Write/WriteLine, Math, Convert, int/long
  Parse and TryParse, List<T>, Dictionary<int-like, V>, File, Directory, Path, Stopwatch, Environment.

  The old corelib was written around a garbage collector (System.Object as a root every class derives from, UTF-16
chars, reflection, threads).  None of that exists in Crust.  What survived is its shape: the same namespaces and
type names, and Console, File and Environment.  The rest is kept, unmaintained, in legacy/corelib-gc.


Layout
======

  build.py          builds and tests everything
  compiler/src      the compiler: CCSharpCompiler.cs (driver) and CrustEmitter.cs (the Crust back end)
  corelib/          the Crust corelib (src = C#, native = C++)
  crust/            test harness and docs: ccs2c.py, unit.py, run_tests.py, tests/ (see crust/README.md)
  examples/         example1 (hello), example2 (a List<string> benchmark)
  legacy/           the old garbage-collected back end's corelib and scripts.  Unmaintained; `--gc` selects the back end.
  build/            what build.py makes (the compiler, examples).  Not checked in.


Tests
=====

  python3 build.py test

  Every case in crust/tests runs through CC# -> cpprust -> gcc AND Crust's own shivyc, and its stdout and exit status
are compared with the same C# on real .NET.  Programs that use coost's fs / time are compiled by gcc only: shivyc
does not bundle <errno.h>; the test run says so for each one.  Refusal cases pin the diagnostic text.


History
=======

  This was the Q# successor written by Peter Quiring.  The original notes on the compiler, kept because they are
still true:

  The C# to C++ compiler is written in C# and uses the amazing Roslyn project to analyze code.
I've tried MANY times to use Java's compiler to build a similar project but it just doesn't expose enough symbol
information, and converting Java to C++ or C is impossible (even tried x64 assembly).  The Java language is just
too abstract to implement.  Just look at the problems Oracle is having with Graal.

  Extern functions do not require a [DllImport ...] attribute.  To avoid compiler warnings, include
<NoWarn>0626</NoWarn> in your .csproj files.

Author : Peter Quiring (pquiring at gmail dot com)
Crust back end : https://github.com/brentharts/crust

Version : 0.1
