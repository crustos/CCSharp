# C# -> C++ -> C = CC#

CC# is a C# cross compiler written in C# on top of Roslyn. It turns a C# program, or a whole C# project, into the
C++ subset that [Crust](https://github.com/brentharts/crust) accepts. Crust's `cpprust` lowers that to plain C, and
a C compiler (gcc by default) makes the executable.

```
C# --Roslyn--> Crust C++ subset --cpprust--> C --gcc--> native
 (CC#, this repo)                (crust)             (or Crust's shivyc: experimental)
```

There is no garbage collector, no .NET class library and no Qt. The C# standard library is replaced by a small
corelib (`corelib/`) written for Crust and [coost](https://github.com/crustos/coost). **Crust and coost are
required.** They live beside this repository, and `build.py` clones them for you:

```
parent/
  CCSharp/    this repository
  crust/      https://github.com/brentharts/crust    cpprust: C++ subset -> C
  coost/      https://github.com/crustos/coost       strings, files, paths and clocks
```

## Quick start

```sh
python3 build.py                              # clone crust + coost, build everything, test, run the examples
python3 build.py run examples/example1/src    # compile and run a program
```

You need `python3`, `git` (only to clone the two dependencies), a C compiler (`cc` or `gcc`), and the **.NET SDK 8 or
later**. The compiler is built with the Roslyn that ships inside the SDK, so nothing is downloaded from NuGet and
everything works offline once the two dependencies are present. It has only been run on Linux.

## Using build.py

`python3 build.py -h` prints this reference. Commands come first, then options; the order of options does not matter.

### Commands

| command | what it does |
|---|---|
| *(none)* / `all` | `deps`, `compiler`, `coost`, `corelib`, `test`, `examples` |
| `deps` | clone `../crust` and `../coost` if they are missing. The only step that needs a network. |
| `compiler` | build `build/compiler/ccs.dll` from `compiler/src`. Skipped when nothing changed. |
| `coost` | build coost with its own `build.py` (`--test` also runs coost's tests) |
| `corelib` | check the corelib: the C# compiles, the native helpers lower through cpprust and compile |
| `test [names..]` | the input tests, then every case in `crust/tests` compared with real .NET |
| `examples` | build and run everything in `examples/` |
| `convert INPUT.. [-o DIR] [--c]` | write the generated C++ (and with `--c` one self-contained C file) |
| `compile INPUT.. [-o EXE]` | build a native executable. Default: `build/bin/NAME` |
| `run INPUT.. [-- ARGS..]` | compile and run; everything after `--` goes to the program. Exits with the program's status. |
| `status` | where the dependencies, the .NET SDK, the compiler and the C compiler were found |
| `clean` | remove `build/` (the dependencies are left alone) |

### Inputs: files, folders, projects, solutions

`convert`, `compile` and `run` take **any number of inputs, in any mix**. Together they are **one program**, so you
can convert an entire C# project (or several) in a single command:

```sh
python3 build.py run     examples/example1/src                  # a folder
python3 build.py run     Hello.cs                               # one file
python3 build.py compile src/ extra/Helpers.cs tools/Util.cs    # folders and files together
python3 build.py compile 'src/**/*.cs'                          # a wildcard (when your shell leaves it alone)
python3 build.py compile MyApp/MyApp.csproj -o myapp            # a project
python3 build.py convert Everything.sln -o generated --c        # a whole solution
```

| input | what it contributes |
|---|---|
| a `.cs` file | itself |
| a folder | every `.cs` under it, recursively, except `bin/`, `obj/`, `.git/` and `.vs/` |
| a `.csproj` (SDK-style) | every `.cs` under the project's folder, except `bin/`, `obj/` and folders that hold another project; then `<Compile Include>` adds, `<Compile Remove>` removes (wildcards and `..\` paths work), and every `<ProjectReference>` is followed, transitively |
| a `.csproj` (old-style) | only its `<Compile Include>` items |
| a `.csproj` with `<EnableDefaultCompileItems>false` | only its `<Compile Include>` items |
| a `.sln` / `.slnx` | the `.csproj` projects it lists, merged into one program |
| a wildcard | the `.cs` files it matches |

The files are sorted and de-duplicated. Two files with the same name in different folders (`App/Util.cs`,
`Lib/Util.cs`) are fine: each is named by its path under the common folder, and diagnostics always show your original
paths and line numbers. `Condition="..."` attributes in a project are not evaluated; every item is taken. A solution
merges **all** of its projects into one program, so give a `.csproj` instead if you want only one of them (its
references still come along).

### Choosing the entry point

The program's entry point is a `static int Main()`, `static void Main()` or `Main(string[] args)`. If a program has
more than one (typical for a solution with a test project), CC# refuses to guess:

```
Error: 2 entry points: App.Program.Main, Tests.Runner.Main
  Say which one with --main=Class.
```

```sh
python3 build.py run Everything.sln --main App.Program
```

### Options

| option | meaning |
|---|---|
| `-o PATH` | the output: the executable (`compile`) or the folder (`convert`) |
| `--main CLASS` | the class whose `static Main` is the entry point. Use the dotted name, namespace included. |
| `--name NAME` | the program's name (default: from the first input: `App.csproj` gives `App`, `src/` gives its parent's name) |
| `--c` | `convert`: also write the lowered C |
| `--cc CC` | the C compiler (default: `$CC`, else `cc`, else `gcc`) |
| `--debug` | compile with `-g -O0` instead of `-O2` |
| `--shivyc` | use Crust's own compiler instead of gcc (experimental, see below). With `test`: also run every case through shivyc and require agreement. |
| `--offline` | never touch the network. A missing dependency is an error that tells you what to clone. |
| `--update` | `deps`: `git pull --ff-only` the dependencies |
| `--no-test` | with no command: skip the test step |
| `--test` | `coost`: also run coost's own tests |
| `--` | everything after it is passed to the program (`run`) |

Environment: `CRUST_HOME` and `COOST_HOME` (where the dependencies are; default `../crust` and `../coost`),
`DOTNET` (the `dotnet` executable), `CC` (the C compiler).

### What you get

`compile` writes the executable (default `build/bin/NAME`) and, beside it, the generated C (`NAME.c`).

`convert` writes a folder (default `build/convert/NAME`):

```
generated/
  App_Program.cpp      one C++ file per C# file, named by its path under the common folder
  App_Util.cpp         (App/Program.cs, App/Util.cs, Lib/Cart.cs ... : slashes become underscores)
  Lib_Cart.cpp
  Shop.main.cpp        includes them in dependency order and defines main()
  Shop.c               with --c: the whole program as ONE C file
```

* The `.cpp` files are the C++ subset for **Crust**. A statement on C# line N is on line N of its file (a `#line`
  puts a moved type back), so a Crust diagnostic points at your `.cs`.
* `NAME.c` is **self-contained**: the coost sources the program needs are already spliced in. It needs no headers
  from this repository, so any C compiler builds it: `cc -O2 NAME.c -lm -o app`. That is the file to ship, or to feed
  to a different toolchain.

### Examples

```sh
# everything in a solution, converted, then built with plain cc
python3 build.py convert Shop.sln --main App.Program -o gen --c
cc -O2 -w gen/Shop.c -lm -o shop && ./shop

# a program that takes arguments
python3 build.py run Greeter.cs -- Ada Lovelace

# a debuggable build with another compiler
python3 build.py compile MyApp.csproj --cc clang --debug -o myapp

# offline, in CI: dependencies must already be there
python3 build.py --offline
```

### Errors

A program outside the supported subset is **refused**, naming the C# file and line, the reason, and what to write
instead. All refusals are reported, not just the first, and the exit status is 1:

```
Calc.cs:12: initialising `b` from another object would alias it: a class is single-owner here, so there is one
owner and no copy. Construct a new one with `new`, or pass the original by reference.
Calc.cs:30: `System.String.PadLeft` is declared in the CC# corelib (the .NET surface) but has no Crust implementation yet.
CCSharp --crust: 2 construct(s) outside the Crust C# subset
```

Ordinary C# errors (a typo, a missing type) are reported first, in Roslyn's own words. A bad input
(`no such file or folder: x.cs`, `... lists no .csproj projects`) says so and exits 1.

## gcc, and shivyc

**gcc is the default** for `compile`, `run` and `test`; use `--cc` or `$CC` for another C compiler. Crust has its own
C compiler, `shivyc`, and CC# output can go through it with `--shivyc`:

```sh
python3 build.py run examples/example1/src --shivyc
python3 build.py test --shivyc          # every case must give the same output under shivyc as under gcc
```

It is **experimental and off by default**. Every test case, and both examples, produce identical output under shivyc
and gcc except two: shivyc does not bundle glibc's `<errno.h>`, which coost's file and time code needs, so a program
that uses `File`, `Directory`, `Path`, `Stopwatch` or `Environment.TickCount64` cannot be compiled by it. With
`test --shivyc` those cases are reported as `gcc only`.

## What a C# program means here

The semantics are Crust's, not .NET's. `crust/README.md` has the full mapping.

* **A class is single ownership**: a value, destroyed at scope exit. Class, array and `List<T>` parameters are
  borrowed. `Node b = a;` would alias, so it is refused.
* **`string` is coost's `fastring`**, held by value. C# strings are immutable, so a copy cannot be told from sharing.
  There is no `null` string and no `char` (a C# `char` is UTF-16).
* **There is no `catch`.** An exception can never be observed, so a failure that .NET would throw
  (`int.Parse("x")`, `Substring` out of range) is an unhandled exception here too: the process ends, with the same
  exit status and the output printed so far.
* **Arithmetic wraps**, as in C#'s default unchecked context. Evaluation order is C#'s (left to right); where C
  could differ, CC# evaluates into temporaries in order, or refuses.
* Array and `Dictionary` indexing is unchecked, as in C.

## The corelib

`corelib/src` is C# declarations compiled by Roslyn **instead of** the .NET reference assemblies, so it is the whole
of what a program can name. A member without a body is implemented in C++ (`corelib/native`, on coost):

```csharp
[Cpp("cs_s_substr1({this}, {0})")]  public extern string Substring(int startIndex);
```

A member declared without `[Cpp]` is part of .NET that is not implemented yet: using it is refused, by name.

| area | implemented |
|---|---|
| `string` | `Length`, `Substring`, `IndexOf`, `LastIndexOf`, `Contains`, `StartsWith`, `EndsWith`, `Replace`, `ToUpper`, `ToLower`, `Trim`, `Equals`, `IsNullOrEmpty`, `+`, `==`, `$"..."` |
| text | `StringBuilder` (`Append`, `AppendLine`, `Clear`, `Length`, `ToString`; chains work) |
| numbers | `Math` (`Abs`, `Min`, `Max`, `Sign`, `Sqrt`, `Pow`, `Floor`, `Ceiling`, `Sin`, `Cos`, `PI`, `E`), `int` / `long` `Parse`, `TryParse`, `ToString`, `Convert` |
| collections | `T[]`, `List<T>`, `Dictionary<K,V>` with an integer, bool or enum key |
| I/O | `Console.Write` / `WriteLine`, `File`, `Directory`, `Path` |
| other | `Stopwatch`, `Environment.TickCount64` / `NewLine` / `Exit` |

Refused today, with a message: `char`, `null`, `throw` / `try`, lambdas and delegates, generic methods, `is` / `as`,
LINQ, `goto`, `params`, named and optional arguments, nested types, operator overloading, array initialisers, printing
a `float` / `double`, and `Dictionary<string, ...>`.

## Tests

```sh
python3 build.py test                  # the input tests, then every case, with gcc
python3 build.py test corelib_string   # just the cases whose names are given
python3 build.py test --shivyc         # ... and with Crust's own compiler too
```

* `crust/test_inputs.py` tests what counts as a program: files, folders, `.csproj` and `.sln`, and converting several
  inputs as one.
* `crust/tests` holds a case per file or folder. Each runs through CC# and gcc, and its **stdout and exit status are
  compared with the same C# run on real .NET** (when `dotnet` is available). A case that is a folder holding a `.sln` or
  `.csproj` is converted *as that project*. `refuse_*.cs` cases pin a refusal's message. `// main: Class` in a case's
  first file selects its entry point.
* A line check asserts that every `return` is on its C# line in the generated C++.

## Layout

```
build.py            builds, tests, and runs everything (this file's commands)
compiler/src        the compiler: CCSharpCompiler.cs (driver) and CrustEmitter.cs (the Crust back end)
corelib/src         the C# class library      corelib/native   its C++ side, on coost
crust/              ccs2c.py (the pipeline), inputs.py (files/projects), unit.py (coost splicing), tests, docs
examples/           example1 (hello), example2 (a List<string> benchmark)
build/              what build.py makes (the compiler, executables, converted output). Not checked in.
```

`crust/ccs2c.py` is the pipeline `build.py` drives. It can be used directly (`python3 crust/ccs2c.py -h`).

## History

This was the Q# successor written by Peter Quiring. The original notes on the compiler are still true:

The C# to C++ compiler is written in C# and uses the amazing Roslyn project to analyze code. I've tried MANY times to
use Java's compiler to build a similar project but it just doesn't expose enough symbol information, and converting
Java to C++ or C is impossible (even tried x64 assembly). The Java language is just too abstract to implement. Just
look at the problems Oracle is having with Graal.

The old back end emitted garbage-collected C++ over a corelib built around `System.Object`, UTF-16 strings, reflection
and threads. None of that exists in Crust, and it was removed (it remains in the git history). What survived is the
shape: the same namespaces and type names, and `Console`, `File` and `Environment`.

Author : Peter Quiring (pquiring at gmail dot com)
Crust back end : https://github.com/brentharts/crust
