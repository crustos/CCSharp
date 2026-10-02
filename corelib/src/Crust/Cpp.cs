/*
  The mapping from the C# corelib to C++ (the subset Crust lowers) and coost.

  A member with no body is declared here and implemented in C++.  [Cpp] says how a use of it is spelled:

      [Cpp("{this}.substr({0})")]          instance method / property getter
      [Cpp("cs_abs_i32({0})")]             static method
      [Cpp("std::vector<{T0}>")]           on a type: how the type is spelled; {T0} {T1} are its type arguments

  Placeholders:  {this}  the receiver          {0} {1} ..  the arguments
                 {0:c}   argument 0 as a `const char *` (a literal as written, a string's c_str())

  A string-typed receiver or argument is always a *named* fastring (Crust passes references as pointers, so
  a temporary has no address); the emitter builds it into a local first.

  A member declared without [Cpp] is part of the .NET surface that is not implemented in Crust yet: using it
  is refused, at the C# line, naming the member.  That is the contract of this corelib, and it is why
  everything .NET has is not simply deleted from here: the C# compiler can then say "not implemented" rather
  than "no such member".
*/
namespace Crust {

  /** How a member (or a whole type) is written in C++. */
  [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = false)]
  public sealed class CppAttribute : System.Attribute {
    public CppAttribute(string template) {}
  }

  /** A header the generated code needs: "\"cs/core.h\"" or "<math.h>".  coost sources are spliced in by header. */
  [System.AttributeUsage(System.AttributeTargets.All, AllowMultiple = true)]
  public sealed class CppIncludeAttribute : System.Attribute {
    public CppIncludeAttribute(string header) {}
  }

  /** The method returns its receiver (StringBuilder.Append): a chain `sb.Append(a).Append(b)` is one statement per call. */
  [System.AttributeUsage(System.AttributeTargets.Method)]
  public sealed class CppFluentAttribute : System.Attribute {
  }
}
