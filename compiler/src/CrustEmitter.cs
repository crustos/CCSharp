/**

  CC# --crust back end

  Emits the C++ subset accepted by Crust's `cpprust` (https://github.com/brentharts/crust)
  instead of the garbage-collected C++ that the classic CC# generator produces.

  Conventions follow Crust's own C# lowering (tools/cs2cpp.py, CSRUST.md) so that
  CC# and `csrust` agree on what a C# program means:

    * `class` is single ownership: a lowered object is a value, destroyed at scope exit.
    * class / array / List / Dictionary parameters are borrowed (C++ references).
    * enum Kind : byte { A }       -> enum Kind_values { Kind_A }; typedef unsigned char Kind;
    * properties                   -> field + get_P() / set_P(v)
    * T[] / List<T>                -> std::vector<T>
    * Console.Write/WriteLine      -> printf
    * line numbers are preserved, so a diagnostic in the generated C++ points at the .cs line.

  Anything outside the subset is REFUSED, at the C# line, with the reason and the
  replacement -- never approximated.  (Crust: "a refusal is the deliverable".)

*/

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CCSharpCompiler;

/** A construct that is outside the Crust C# subset. */
class CrustRefusal : Exception
{
  public SyntaxNode Node;
  public CrustRefusal(SyntaxNode node, string msg) : base(msg) { Node = node; }
}

/** Output buffer that tracks its own line number so source lines can be preserved. */
class CrustOut
{
  StringBuilder sb = new StringBuilder();
  public int Line = 1;
  public override string ToString() { return sb.ToString(); }
  public bool AtLineStart { get { return sb.Length == 0 || sb[sb.Length - 1] == '\n'; } }

  public void Raw(string s)
  {
    sb.Append(s);
    foreach (char c in s) if (c == '\n') Line++;
  }

  /** Advance (never rewind) to the given source line. */
  public void PadTo(int srcLine)
  {
    while (Line < srcLine) { sb.Append('\n'); Line++; }
  }

  /** Write text, separated from what precedes it on the same line. */
  public void Text(string s)
  {
    if (s.Length == 0) return;
    if (!AtLineStart && sb[sb.Length - 1] != ' ') sb.Append(' ');
    Raw(s);
  }

  public void At(int srcLine, string s) { PadTo(srcLine); Text(s); }
}

class CrustEmitter
{
  public const string WRAPV_PRAGMA = "_Pragma(\"GCC optimize(\\\"wrapv\\\")\") ";

  public List<string> errors = new List<string>();

  Source src;
  IMethodSymbol currentMethod;                   //the method whose body is being emitted
  INamedTypeSymbol currentType;                  //the class whose members are being emitted
  SemanticModel model;
  CrustOut o;
  string mainClass;                              //--main=Class (flat name) or null
  public static string entryCall;                //"Cls::Main" once found
  public static bool entryReturnsInt;
  public static bool entryTakesArgs;
  bool preambleDone;
  HashSet<string> arrayHelpers = new HashSet<string>();
  StringBuilder preamble = new StringBuilder();
  int tmp;
  List<string> hoisted = new List<string>();     //statements emitted just before the current statement, in order
  bool canHoist;
  ExpressionSyntax hoistRoot;                    //a composite whose parts are hoisted strictly in order
  public bool UsesString;                        //aggregate file needs <string>
  public bool UsesStrcmp;                        //... and <string.h>
  IMethodSymbol entryMethod;                     //Main, whose string[] args are `const char *` elements

  static readonly HashSet<string> reserved = new HashSet<string> {
    "auto","register","template","typename","namespace","operator","new","delete","friend","virtual",
    "inline","static_cast","dynamic_cast","reinterpret_cast","const_cast","typeid","typedef","union",
    "signed","unsigned","long","short","extern","export","explicit","mutable","private","protected",
    "public","throw","try","catch","using","this","main","and","or","not","xor","asm","bool","char",
    "double","float","int","void","wchar_t","goto","default","enum","struct","class","volatile",
    "restrict","class_","NULL","errno","stdin","stdout","stderr","printf","abort","size_t"
  };

  public CrustEmitter(string mainClass) { this.mainClass = mainClass; }

  // ------------------------------------------------------------------ entry points

  /** Emit one source file.  Returns the C++ text, or null when the file was refused. */
  public string EmitFile(Source file)
  {
    src = file;
    model = file.model;
    o = new CrustOut();
    preambleDone = false;
    firstOutput = true;
    preamble.Clear();
    int before = errors.Count;
    var root = (CompilationUnitSyntax)file.tree.GetRoot();
    var decls = new List<MemberDeclarationSyntax>();
    CollectTypes(root.Members, decls);
    foreach (var d in DependencyOrder(decls)) TopLevel(d);
    if (errors.Count > before) return null;
    return FinishPreamble(o.ToString());
  }

  public string EmitMain()
  {
    if (entryCall == null) return "";
    var sb = new StringBuilder();
    sb.Append("int main(int argc, char **argv) {\n");
    if (entryTakesArgs) {
      sb.Append("  std::vector<const char *> args;\n");
      sb.Append("  for (int i = 1; i < argc; i++) { args.push_back(argv[i]); }\n");
    }
    string call = entryCall + "(" + (entryTakesArgs ? "args" : "") + ")";
    if (entryReturnsInt) sb.Append("  return " + call + ";\n");
    else sb.Append("  " + call + ";\n  return 0;\n");
    sb.Append("}\n");
    return sb.ToString();
  }

  void Refuse(SyntaxNode node, string msg) { throw new CrustRefusal(node, msg); }

  int LineOf(SyntaxNode n) { return n.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }
  int LineOf(SyntaxToken t) { return t.GetLocation().GetLineSpan().StartLinePosition.Line + 1; }

  void Report(CrustRefusal r)
  {
    int line = r.Node == null ? 0 : LineOf(r.Node);
    errors.Add(src.csFile + ":" + line + ": " + r.Message);
  }

  /** Called before the first token of real output: include + helpers on the way in. */
  void Begin(int srcLine)
  {
    preambleDone = true;
    Resync(srcLine);          //no include here: it lives in the aggregate file, so line 1 stays line 1
  }

  /** Put the next output line on source line `srcLine`: pad forward, or `#line` back when a type was moved. */
  void Resync(int srcLine)
  {
    if (o.Line <= srcLine) { o.PadTo(srcLine); return; }
    if (!o.AtLineStart) o.Raw("\n");
    o.Raw("#line " + srcLine + "\n");
    o.Line = srcLine;
  }

  // ------------------------------------------------------------------ declarations

  /** Namespaces are flattened; what is left is the list of declarations in source order. */
  void CollectTypes(SyntaxList<MemberDeclarationSyntax> members, List<MemberDeclarationSyntax> into)
  {
    foreach (var m in members) {
      if (m is NamespaceDeclarationSyntax ns) CollectTypes(ns.Members, into);
      else if (m is FileScopedNamespaceDeclarationSyntax fs) CollectTypes(fs.Members, into);
      else into.Add(m);
    }
  }

  /** C# lets a derived type come before its base; C++ does not.  Bases and interfaces first, otherwise source order. */
  List<MemberDeclarationSyntax> DependencyOrder(List<MemberDeclarationSyntax> decls)
  {
    var bySym = new Dictionary<ISymbol, MemberDeclarationSyntax>(SymbolEqualityComparer.Default);
    foreach (var d in decls)
      if (d is BaseTypeDeclarationSyntax) { var s = model.GetDeclaredSymbol(d); if (s != null) bySym[s.OriginalDefinition] = d; }
    var result = new List<MemberDeclarationSyntax>();
    var seen = new HashSet<MemberDeclarationSyntax>();
    void Visit(MemberDeclarationSyntax d)
    {
      if (!seen.Add(d)) return;
      if (d is TypeDeclarationSyntax td && td.BaseList != null) {
        foreach (var b in td.BaseList.Types) {
          var bs = model.GetSymbolInfo(b.Type).Symbol as INamedTypeSymbol;
          if (bs != null && bySym.TryGetValue(bs.OriginalDefinition, out var bd)) Visit(bd);
        }
      }
      result.Add(d);
    }
    foreach (var d in decls) Visit(d);
    return result;
  }

  void TopLevel(MemberDeclarationSyntax d)
  {
    try {
      switch (d) {
        case NamespaceDeclarationSyntax ns:
          foreach (var m in ns.Members) TopLevel(m);
          break;
        case FileScopedNamespaceDeclarationSyntax fs:
          foreach (var m in fs.Members) TopLevel(m);
          break;
        case EnumDeclarationSyntax e: Enum(e); break;
        case ClassDeclarationSyntax c: Class(c, "class"); break;
        case StructDeclarationSyntax s: Class(s, "class"); break;
        case InterfaceDeclarationSyntax i: Class(i, "class"); break;
        case DelegateDeclarationSyntax dg:
          Refuse(dg, "`delegate` is not in the Crust C# subset: delegates need a captured-state representation. Use a static method, or an interface with one method.");
          break;
        case GlobalStatementSyntax gs:
          Refuse(gs, "top-level statements are not in the Crust C# subset: C# requires them before every type declaration and C needs them after. Put the code in a class with a `static Main`.");
          break;
        case RecordDeclarationSyntax rd:
          Refuse(rd, "`record` is not in the Crust C# subset. Write a class with fields.");
          break;
        default:
          Refuse(d, "`" + d.Kind() + "` is not in the Crust C# subset.");
          break;
      }
    } catch (CrustRefusal r) { Report(r); }
  }

  string FlatName(INamedTypeSymbol t)
  {
    string n = Ident(t.Name);
    if (t.ContainingType != null) n = FlatName(t.ContainingType) + "_" + n;
    else if (t.ContainingNamespace != null && !t.ContainingNamespace.IsGlobalNamespace)
      n = t.ContainingNamespace.ToDisplayString().Replace(".", "_") + "_" + n;
    return n;
  }

  string Ident(string name)
  {
    if (reserved.Contains(name)) return name + "_";
    return name;
  }

  string Underlying(INamedTypeSymbol e)
  {
    switch (e.EnumUnderlyingType.SpecialType) {
      case SpecialType.System_Byte: return "unsigned char";
      case SpecialType.System_SByte: return "signed char";
      case SpecialType.System_Int16: return "short";
      case SpecialType.System_UInt16: return "unsigned short";
      case SpecialType.System_UInt32: return "unsigned";
      case SpecialType.System_Int64: return "long long";
      case SpecialType.System_UInt64: return "unsigned long long";
      default: return "int";
    }
  }

  void Enum(EnumDeclarationSyntax e)
  {
    var sym = (INamedTypeSymbol)model.GetDeclaredSymbol(e);
    string name = FlatName(sym);
    Begin(LineOf(e));
    var sb = new StringBuilder();
    sb.Append("enum " + name + "_values { ");
    bool first = true;
    foreach (var m in e.Members) {
      var ms = (IFieldSymbol)model.GetDeclaredSymbol(m);
      if (!first) sb.Append(", ");
      first = false;
      sb.Append(name + "_" + Ident(m.Identifier.Text));
      if (ms.HasConstantValue) sb.Append(" = " + Convert.ToInt64(ms.ConstantValue));
    }
    sb.Append(" }; typedef " + Underlying(sym) + " " + name + ";");
    FirstLinePrefix(sb);
    o.Text(sb.ToString());
    o.PadTo(LineOf(e.CloseBraceToken));
  }

  /** helpers + wrapv pragma ride on the first line of output. */
  void FirstLinePrefix(StringBuilder sb)
  {
    if (firstOutput) {
      firstOutput = false;
      sb.Insert(0, WRAPV_PRAGMA + "_CS_PREAMBLE_ ");
    }
  }
  bool firstOutput = true;

  bool IsOwnedTypeDecl(TypeDeclarationSyntax t) { return true; }

  void Class(TypeDeclarationSyntax c, string kw)
  {
    var sym = (INamedTypeSymbol)model.GetDeclaredSymbol(c);
    string name = FlatName(sym);
    CheckClassAttributes(c, sym);
    if (sym.ContainingType != null)
      Refuse(c, "nested type `" + sym.Name + "` is not in the Crust C# subset yet. Move it to the top level of the file.");
    if (c.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
      Refuse(c, "`partial` is not in the Crust C# subset. Put the whole class in one declaration.");
    if (c.Modifiers.Any(m => m.IsKind(SyntaxKind.UnsafeKeyword)))
      Refuse(c, "`unsafe` is not in the Crust C# subset.");
    //attribute classes are markers the C# compiler needs; Crust drops them
    if (sym.BaseType != null && sym.BaseType.ToDisplayString() == "System.Attribute") return;

    Begin(LineOf(c));
    var head = new StringBuilder();
    if (c.TypeParameterList != null) {
      head.Append("template<");
      head.Append(string.Join(", ", c.TypeParameterList.Parameters.Select(p => "typename " + Ident(p.Identifier.Text))));
      head.Append("> ");
    }
    head.Append(kw + " " + name);
    var bases = new List<string>();
    if (c.BaseList != null) {
      foreach (var b in c.BaseList.Types) {
        var bs = model.GetSymbolInfo(b.Type).Symbol as INamedTypeSymbol;
        if (bs == null) continue;
        if (bs.SpecialType == SpecialType.System_Object || bs.SpecialType == SpecialType.System_ValueType) continue;
        if (bs.Name == "ICloneable" || bs.Name == "IEquatable" || bs.Name == "IComparable")
          Refuse(b, "base type `" + bs.Name + "` is not in the Crust C# subset.");
        bases.Add("public " + TypeName(bs));
      }
    }
    if (bases.Count > 0) head.Append(" : " + string.Join(", ", bases));
    head.Append(" {");
    var sbh = new StringBuilder(head.ToString());
    FirstLinePrefix(sbh);
    o.Text(sbh.ToString());

    bool isInterface = c is InterfaceDeclarationSyntax;
    currentType = sym;
    foreach (var m in c.Members) {
      try { Member(m, sym, isInterface); }
      catch (CrustRefusal r) { Report(r); }
    }
    o.PadTo(LineOf(c.CloseBraceToken));
    o.Text("};");
    currentType = null;
  }

  void CheckClassAttributes(TypeDeclarationSyntax c, INamedTypeSymbol sym)
  {
    foreach (var al in c.AttributeLists)
      foreach (var a in al.Attributes) {
        string n = a.Name.ToString();
        if (n == "Shared" || n == "SharedAttribute")
          Refuse(a, "`[Shared]` (shared_ptr classes) is not supported by CC# --crust yet. Default single-owner classes are.");
        if (n == "MaxInstances" || n == "MaxInstancesAttribute")
          Refuse(a, "`[MaxInstances(N)]` (arena classes) is not supported by CC# --crust yet.");
      }
  }

  void Member(MemberDeclarationSyntax m, INamedTypeSymbol owner, bool isInterface)
  {
    switch (m) {
      case FieldDeclarationSyntax f: Field(f); break;
      case PropertyDeclarationSyntax p: Property(p, owner, isInterface); break;
      case MethodDeclarationSyntax md: Method(md, owner, isInterface); break;
      case ConstructorDeclarationSyntax cd: Ctor(cd, owner); break;
      case DestructorDeclarationSyntax dd: Dtor(dd, owner); break;
      case OperatorDeclarationSyntax od:
        Refuse(od, "operator overloading is not in the Crust C# subset yet. Write a named method.");
        break;
      case IndexerDeclarationSyntax ix:
        Refuse(ix, "indexers are not in the Crust C# subset. Write `Get(i)` / `Set(i, v)` methods.");
        break;
      case EventFieldDeclarationSyntax ev:
        Refuse(ev, "`event` is not in the Crust C# subset. Call the handler through an interface.");
        break;
      case BaseTypeDeclarationSyntax _:
      case DelegateDeclarationSyntax _:
        Refuse(m, "nested types are not in the Crust C# subset yet. Move it to the top level of the file.");
        break;
      default:
        Refuse(m, "`" + m.Kind() + "` is not in the Crust C# subset.");
        break;
    }
  }

  bool Has(SyntaxTokenList mods, SyntaxKind k) { return mods.Any(x => x.IsKind(k)); }

  void RefuseExtern(MemberDeclarationSyntax m, SyntaxTokenList mods)
  {
    if (Has(mods, SyntaxKind.ExternKeyword))
      Refuse(m, "`extern` methods are not in the Crust C# subset: the CC# corelib is not available under --crust. Declare the function in C and call it from a .c file.");
    if (Has(mods, SyntaxKind.UnsafeKeyword))
      Refuse(m, "`unsafe` is not in the Crust C# subset.");
  }

  string DefaultInit(ITypeSymbol t)
  {
    switch (t.SpecialType) {
      case SpecialType.System_Boolean: return "false";
      case SpecialType.System_Single: return "0.0f";
      case SpecialType.System_Double: return "0.0";
      case SpecialType.System_Byte: case SpecialType.System_SByte:
      case SpecialType.System_Int16: case SpecialType.System_UInt16:
      case SpecialType.System_Int32: case SpecialType.System_UInt32:
      case SpecialType.System_Int64: case SpecialType.System_UInt64:
        return "0";
    }
    if (t.TypeKind == TypeKind.Enum) return "0";
    return null;   //aggregates: default-constructed (C# would have null for a class: see Refuse in TypeName)
  }

  void Field(FieldDeclarationSyntax f)
  {
    RefuseExtern(f, f.Modifiers);
    string prefix = "";
    bool isConst = Has(f.Modifiers, SyntaxKind.ConstKeyword);
    bool isStatic = Has(f.Modifiers, SyntaxKind.StaticKeyword) || isConst;
    if (isStatic) prefix = "static ";
    if (isConst) prefix += "const ";
    foreach (var v in f.Declaration.Variables) {
      var fs = (IFieldSymbol)model.GetDeclaredSymbol(v);
      string t = TypeName(fs.Type, v);
      var sb = new StringBuilder();
      sb.Append(prefix + t + " " + Ident(v.Identifier.Text));
      if (v.Initializer != null) {
        var init = v.Initializer.Value;
        if (fs.Type.SpecialType == SpecialType.System_String)
          Refuse(init, "a string field initialiser is not in the Crust C# subset yet. Assign it in the constructor; until then the field is the empty string.");
        if (init is ObjectCreationExpressionSyntax oc && IsOwnedClass(fs.Type)) {
          if (oc.ArgumentList != null && oc.ArgumentList.Arguments.Count > 0)
            Refuse(init, "a class-typed field initialised with constructor arguments is not supported by CC# --crust yet. Assign it in the constructor.");
          //default constructor runs when the owner is built
        } else if (init is ArrayCreationExpressionSyntax || init is ImplicitArrayCreationExpressionSyntax
                   || init is InitializerExpressionSyntax) {
          Refuse(init, "a field initialised with an array is not supported by CC# --crust yet. Assign it in the constructor.");
        } else {
          sb.Append(" = " + Expr(init));
        }
      } else {
        string d = DefaultInit(fs.Type);
        if (d != null) sb.Append(" = " + d);
      }
      sb.Append(";");
      o.At(LineOf(v), sb.ToString());
    }
  }

  bool IsOwnedClass(ITypeSymbol t)
  {
    return t.TypeKind == TypeKind.Class && t.SpecialType == SpecialType.None
           && !(t is IArrayTypeSymbol) && !IsList(t) && !IsDictionary(t);
  }

  /** A method returning its own class that does `return this;` is a fluent API.  C# returns the same
      object; a by-value return would silently copy it, so it returns `T *` and the chain uses `->`. */
  Dictionary<IMethodSymbol, bool> fluentCache = new Dictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
  bool IsFluent(IMethodSymbol ms)
  {
    if (ms == null || !IsOwnedClass(ms.ReturnType)) return false;
    ms = ms.OriginalDefinition;
    if (fluentCache.TryGetValue(ms, out bool v)) return v;
    bool r = false;
    foreach (var sr in ms.DeclaringSyntaxReferences) {
      if (sr.GetSyntax() is MethodDeclarationSyntax md && md.Body != null)
        r = ReturnsOf(md).Any(x => x.Expression is ThisExpressionSyntax);
    }
    fluentCache[ms] = r;
    return r;
  }
  IEnumerable<ReturnStatementSyntax> ReturnsOf(MethodDeclarationSyntax md)
  {
    return md.Body.DescendantNodes(n => !(n is LambdaExpressionSyntax || n is LocalFunctionStatementSyntax)).OfType<ReturnStatementSyntax>();
  }
  bool IsFluentCall(ExpressionSyntax e)
  {
    return e is InvocationExpressionSyntax inv && IsFluent(model.GetSymbolInfo(inv).Symbol as IMethodSymbol);
  }

  /** an rvalue object: a construction or a call result -- a new value, so no alias is created. */
  bool IsFresh(ExpressionSyntax e)
  {
    if (e is ObjectCreationExpressionSyntax || e is ImplicitObjectCreationExpressionSyntax || e is ArrayCreationExpressionSyntax) return true;
    if (e is InvocationExpressionSyntax inv && !IsFluentCall(inv)) return true;
    if (e is ParenthesizedExpressionSyntax p) return IsFresh(p.Expression);
    return false;
  }

  bool IsList(ITypeSymbol t)
  {
    return t is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>";
  }
  bool IsDictionary(ITypeSymbol t)
  {
    return t is INamedTypeSymbol n && n.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>";
  }

  void Property(PropertyDeclarationSyntax p, INamedTypeSymbol owner, bool isInterface)
  {
    RefuseExtern(p, p.Modifiers);
    var ps = (IPropertySymbol)model.GetDeclaredSymbol(p);
    string t = TypeName(ps.Type, p);
    string name = Ident(p.Identifier.Text);
    bool isStatic = Has(p.Modifiers, SyntaxKind.StaticKeyword);
    string st = isStatic ? "static " : "";
    string virt = "";
    if (!isStatic && !isInterface) {
      if (Has(p.Modifiers, SyntaxKind.VirtualKeyword) || Has(p.Modifiers, SyntaxKind.AbstractKeyword)) virt = "virtual ";
    }
    bool abs = Has(p.Modifiers, SyntaxKind.AbstractKeyword);
    int line = LineOf(p);

    if (isInterface || abs) {
      var sb0 = new StringBuilder();
      foreach (var a in p.AccessorList.Accessors) {
        if (a.IsKind(SyntaxKind.GetAccessorDeclaration)) sb0.Append("virtual " + t + " get_" + name + "() = 0; ");
        else sb0.Append("virtual void set_" + name + "(" + t + " value) = 0; ");
      }
      o.At(line, sb0.ToString().TrimEnd());
      return;
    }

    if (p.ExpressionBody != null) {
      BeginHoist();
      var gm = ps.GetMethod;
      currentMethod = gm;
      string gb;
      try { gb = Return(new ReturnStatementData(p.ExpressionBody.Expression)); } finally { currentMethod = null; }
      string gpre = string.Join(" ", hoisted);
      canHoist = false; hoisted.Clear();
      o.At(line, st + virt + t + " get_" + name + "() { " + (gpre.Length > 0 ? gpre + " " : "") + gb + " }");
      return;
    }
    var accs = p.AccessorList.Accessors;
    bool auto = accs.All(a => a.Body == null && a.ExpressionBody == null);
    if (auto) {
      var sb = new StringBuilder();
      string d = DefaultInit(ps.Type);
      string init = p.Initializer != null ? " = " + Expr(p.Initializer.Value) : (d != null ? " = " + d : "");
      sb.Append(st + t + " _" + name + init + ";");
      bool strProp = ps.Type.SpecialType == SpecialType.System_String;
      if (strProp && p.Initializer != null)
        Refuse(p.Initializer, "a string property initialiser is not in the Crust C# subset yet. Assign it in the constructor.");
      foreach (var a in accs) {
        if (a.IsKind(SyntaxKind.GetAccessorDeclaration) && strProp)
          sb.Append(" " + st + virt + t + " get_" + name + "() { std::string _r(" + (isStatic ? "" : "this->") + "_" + name + "); return _r; }");
        else if (a.IsKind(SyntaxKind.GetAccessorDeclaration))
          sb.Append(" " + st + virt + t + " get_" + name + "() { return " + (isStatic ? "" : "this->") + "_" + name + "; }");
        else if (a.IsKind(SyntaxKind.SetAccessorDeclaration))
          sb.Append(" " + st + virt + "void set_" + name + "(" + t + " v) { " + (isStatic ? "" : "this->") + "_" + name + " = v; }");
        else Refuse(a, "`init` accessors are not in the Crust C# subset. Use `set`.");
      }
      o.At(line, sb.ToString());
      return;
    }
    foreach (var a in accs) {
      if (a.IsKind(SyntaxKind.InitAccessorDeclaration))
        Refuse(a, "`init` accessors are not in the Crust C# subset. Use `set`.");
      bool get = a.IsKind(SyntaxKind.GetAccessorDeclaration);
      string head = st + virt + (get ? t + " get_" + name + "()" : "void set_" + name + "(" + t + " value)");
      if (a.ExpressionBody != null) {
        string e = Expr(a.ExpressionBody.Expression);
        o.At(LineOf(a), head + (get ? " { return " + e + "; }" : " { " + e + "; }"));
      } else if (a.Body != null) {
        o.At(LineOf(a), head);
        Block(a.Body);
      } else {
        Refuse(a, "an accessor mixing auto and bodied forms is not in the Crust C# subset.");
      }
    }
  }

  string ParamList(ParameterListSyntax pl, bool isMain = false)
  {
    var parts = new List<string>();
    foreach (var p in pl.Parameters) {
      var ps = (IParameterSymbol)model.GetDeclaredSymbol(p);
      if (p.Default != null) Refuse(p, "default parameter values are not in the Crust C# subset. Write an overload.");
      if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.ParamsKeyword)))
        Refuse(p, "`params` is not in the Crust C# subset. Pass an array.");
      if (p.Modifiers.Any(m => m.IsKind(SyntaxKind.InKeyword)))
        Refuse(p, "`in` parameters are not in the Crust C# subset.");
      string t;
      if (isMain && ps.Type is IArrayTypeSymbol at && at.ElementType.SpecialType == SpecialType.System_String)
        t = "std::vector<const char *> &";
      else {
        t = TypeName(ps.Type, p);
        bool byRef = ps.RefKind == RefKind.Ref || ps.RefKind == RefKind.Out;
        if (byRef || Borrowed(ps.Type)) t += " &";
      }
      parts.Add(t + " " + Ident(p.Identifier.Text));
    }
    return string.Join(", ", parts);
  }

  /** class / array / List / Dictionary / interface: C# passes a reference; here it is borrowed. */
  bool Borrowed(ITypeSymbol t)
  {
    if (t is IArrayTypeSymbol || IsList(t) || IsDictionary(t)) return true;
    if (t.TypeKind == TypeKind.Interface) return true;
    return IsOwnedClass(t);
  }

  void CheckTypeParams(MethodDeclarationSyntax md)
  {
    if (md.TypeParameterList != null)
      Refuse(md, "generic methods are not in the Crust C# subset. Make the enclosing class generic, or write one method per type.");
  }

  void Method(MethodDeclarationSyntax md, INamedTypeSymbol owner, bool isInterface)
  {
    RefuseExtern(md, md.Modifiers);
    CheckTypeParams(md);
    if (md.ExplicitInterfaceSpecifier != null)
      Refuse(md, "explicit interface implementations are not in the Crust C# subset. Name the method as the interface does.");
    var ms = (IMethodSymbol)model.GetDeclaredSymbol(md);
    bool isStatic = Has(md.Modifiers, SyntaxKind.StaticKeyword);
    bool isAbstract = Has(md.Modifiers, SyntaxKind.AbstractKeyword) || (isInterface && md.Body == null && md.ExpressionBody == null);
    bool isVirtual = Has(md.Modifiers, SyntaxKind.VirtualKeyword) || Has(md.Modifiers, SyntaxKind.AbstractKeyword) || (isInterface && !isStatic);
    bool isMain = isStatic && md.Identifier.Text == "Main" && IsEntryCandidate(owner, md);

    string ret = ms.ReturnsVoid ? "void" : TypeName(ms.ReturnType, md.ReturnType);
    if (IsFluent(ms)) {
      if (ReturnsOf(md).Any(x => !(x.Expression is ThisExpressionSyntax)))
        Refuse(md, "a method that returns `this` and something else is not in the Crust C# subset: `this` is returned as a pointer, a new object as a value. Split it into two methods.");
      if (isVirtual)
        Refuse(md, "a virtual method returning `this` is not in the Crust C# subset: Crust cannot chain through a virtual call. Return void.");
      ret += " *";
    }
    string pl = ParamList(md.ParameterList, isMain);
    string name = Ident(md.Identifier.Text);
    var head = (isStatic ? "static " : "") + (isVirtual ? "virtual " : "") + ret + " " + name + "(" + pl + ")";

    if (isMain) { RegisterEntry(owner, ms); entryMethod = ms; }

    if (isAbstract) { o.At(LineOf(md), head + " = 0;"); return; }
    if (md.ExpressionBody != null) {
      BeginHoist();
      currentMethod = ms;
      string body;
      try {
        if (ms.ReturnsVoid) body = Expr(md.ExpressionBody.Expression, statement: true) + ";";
        else body = Return(new ReturnStatementData(md.ExpressionBody.Expression));
      } finally { currentMethod = null; }
      string pre = string.Join(" ", hoisted);
      canHoist = false; hoisted.Clear();
      o.At(LineOf(md), head + " { " + (pre.Length > 0 ? pre + " " : "") + body + " }");
      return;
    }
    o.At(LineOf(md), head);
    currentMethod = ms;
    try { Block(md.Body); } finally { currentMethod = null; }
  }

  bool IsEntryCandidate(INamedTypeSymbol owner, MethodDeclarationSyntax md)
  {
    if (mainClass != null) return FlatName(owner) == mainClass.Replace("::", "_").Replace(".", "_");
    return true;
  }

  void RegisterEntry(INamedTypeSymbol owner, IMethodSymbol ms)
  {
    if (entryCall != null) return;                      //first Main wins (or --main=Class)
    if (ms.ReturnType.SpecialType != SpecialType.System_Int32 && !ms.ReturnsVoid) return;
    if (ms.Parameters.Length > 1) return;
    if (ms.Parameters.Length == 1 && !(ms.Parameters[0].Type is IArrayTypeSymbol)) return;
    entryCall = FlatName(owner) + "::Main";
    entryReturnsInt = !ms.ReturnsVoid;
    entryTakesArgs = ms.Parameters.Length == 1;
  }

  void Ctor(ConstructorDeclarationSyntax cd, INamedTypeSymbol owner)
  {
    RefuseExtern(cd, cd.Modifiers);
    string name = FlatName(owner);
    string head = name + "(" + ParamList(cd.ParameterList) + ")";
    if (Has(cd.Modifiers, SyntaxKind.StaticKeyword))
      Refuse(cd, "static constructors are not supported by CC# --crust yet. Initialise the static field where it is declared.");
    if (cd.Initializer != null) {
      var args = cd.Initializer.ArgumentList.Arguments.Select(a => Arg(a)).ToList();
      if (cd.Initializer.IsKind(SyntaxKind.ThisConstructorInitializer))
        Refuse(cd.Initializer, "`: this(..)` constructor chaining is not in the Crust C# subset. Call a shared method from each constructor.");
      var bt = owner.BaseType;
      head += " : " + TypeName(bt) + "(" + string.Join(", ", args) + ")";
    }
    o.At(LineOf(cd), head);
    Block(cd.Body);
  }

  void Dtor(DestructorDeclarationSyntax dd, INamedTypeSymbol owner)
  {
    o.At(LineOf(dd), "~" + FlatName(owner) + "()");
    Block(dd.Body);
  }

  // ------------------------------------------------------------------ types

  string TypeName(ITypeSymbol t, SyntaxNode at = null)
  {
    switch (t.SpecialType) {
      case SpecialType.System_Boolean: return "bool";
      case SpecialType.System_Byte: return "unsigned char";
      case SpecialType.System_SByte: return "signed char";
      case SpecialType.System_Int16: return "short";
      case SpecialType.System_UInt16: return "unsigned short";
      case SpecialType.System_Int32: return "int";
      case SpecialType.System_UInt32: return "unsigned";
      case SpecialType.System_Int64: return "long long";
      case SpecialType.System_UInt64: return "unsigned long long";
      case SpecialType.System_Single: return "float";
      case SpecialType.System_Double: return "double";
      case SpecialType.System_Void: return "void";
      case SpecialType.System_Char:
        Refuse(at, "`char` is not in the Crust C# subset. Use `byte` (or `int` for a code point).");
        break;
      case SpecialType.System_String:
        UsesString = true;
        return "std::string";      //immutable in C#, so a by-value copy is indistinguishable from sharing
      case SpecialType.System_Object:
        Refuse(at, "`object` is not in the Crust C# subset: there is no common base class and no boxing. Use an interface or a generic class.");
        break;
      case SpecialType.System_Decimal:
        Refuse(at, "`decimal` is not in the Crust C# subset.");
        break;
      case SpecialType.System_IntPtr: case SpecialType.System_UIntPtr:
        Refuse(at, "`IntPtr` is not in the Crust C# subset.");
        break;
    }
    if (t is IArrayTypeSymbol arr) {
      if (arr.Rank > 1) Refuse(at, "multidimensional arrays are not in the Crust C# subset. Use a jagged array `T[][]`.");
      return "std::vector<" + TypeName(arr.ElementType, at) + ">";
    }
    if (t is IPointerTypeSymbol) Refuse(at, "pointers are not in the Crust C# subset.");
    if (t is ITypeParameterSymbol tp) return Ident(tp.Name);
    if (t is INamedTypeSymbol n) {
      if (n.TypeKind == TypeKind.Delegate)
        Refuse(at, "delegates are not in the Crust C# subset. Use an interface with one method.");
      if (n.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T || n.Name == "Nullable")
        Refuse(at, "`T?` (nullable value types) is not in the Crust C# subset. Use a bool flag beside the value.");
      string full = n.OriginalDefinition.ToDisplayString();
      if (full == "System.Collections.Generic.List<T>") return "std::vector<" + TypeName(n.TypeArguments[0], at) + ">";
      if (full == "System.Collections.Generic.Dictionary<TKey, TValue>")
        return "std::map<" + TypeName(n.TypeArguments[0], at) + ", " + TypeName(n.TypeArguments[1], at) + ">";
      if (n.ContainingNamespace != null && n.ContainingNamespace.ToDisplayString().StartsWith("System"))
        Refuse(at, "`" + n.ToDisplayString() + "` is not in the Crust C# subset: the .NET class library is not available. Crust provides List<T>, Dictionary<K,V> and Console.Write/WriteLine.");
      string name = FlatName(n);
      if (n.TypeArguments.Length > 0)
        name += "<" + string.Join(", ", n.TypeArguments.Select(a => TypeName(a, at))) + ">";
      return name;
    }
    Refuse(at, "type `" + t.ToDisplayString() + "` is not in the Crust C# subset.");
    return "";
  }

  // ------------------------------------------------------------------ statements

  void Block(BlockSyntax b)
  {
    o.Text("{");
    foreach (var s in b.Statements) Stmt(s);
    o.At(LineOf(b.CloseBraceToken), "}");
  }

  /** a statement used as a loop / if body: always braced. */
  void Body(StatementSyntax s)
  {
    if (s is BlockSyntax b) { Block(b); return; }
    o.Text("{");
    Stmt(s);
    o.At(LineOf(s.GetLastToken()), "}");
  }

  void Stmt(StatementSyntax s)
  {
    try { StmtInner(s); }
    catch (CrustRefusal r) { Report(r); }
  }

  void StmtInner(StatementSyntax s)
  {
    canHoist = false;
    int line = LineOf(s);
    switch (s) {
      case BlockSyntax b:
        o.At(line, "");
        Block(b);
        return;
      case LocalDeclarationStatementSyntax ld:
        if (ld.UsingKeyword.RawKind != 0) Refuse(ld, "`using` declarations are not in the Crust C# subset. Use a class with a destructor and scope exit.");
        BeginHoist();
        var decls = LocalDecl(ld.Declaration, ld.IsConst);
        o.PadTo(line);
        FlushHoisted();
        foreach (var d in decls) o.Text(d);
        return;
      case ExpressionStatementSyntax es:
        BeginHoist();
        string est = ExprStmt(es.Expression);
        est = est.Length == 0 ? "" : est + ";";
        o.PadTo(line);
        FlushHoisted();
        o.Text(est);
        return;
      case ReturnStatementSyntax rs:
        BeginHoist();
        string rst = Return(rs);
        o.PadTo(line);
        FlushHoisted();
        o.Text(rst);
        return;
      case IfStatementSyntax ifs:
        IfChain(ifs, line, false);
        return;
      case WhileStatementSyntax ws:
        o.At(line, "while (" + Expr(ws.Condition) + ")");
        Body(ws.Statement);
        return;
      case DoStatementSyntax dos:
        o.At(line, "do");
        Body(dos.Statement);
        o.Text("while (" + Expr(dos.Condition) + ");");
        return;
      case ForStatementSyntax fs:
        For(fs, line);
        return;
      case ForEachStatementSyntax fe:
        ForEach(fe, line);
        return;
      case BreakStatementSyntax _: o.At(line, "break;"); return;
      case ContinueStatementSyntax _: o.At(line, "continue;"); return;
      case EmptyStatementSyntax _: return;
      case SwitchStatementSyntax sw: Switch(sw, line); return;
      case ThrowStatementSyntax ts:
        Refuse(ts, "`throw` is not supported by CC# --crust yet (Crust lowers it to the checked `raise` model). Return an error code.");
        return;
      case TryStatementSyntax tr:
        Refuse(tr, "`try` / `catch` / `finally` is not supported by CC# --crust yet (Crust has the checked `except` model, not an unwinder). Return an error code.");
        return;
      case LockStatementSyntax lk:
        Refuse(lk, "`lock` is not in the Crust C# subset. Threads are declared to the compiler, not locked.");
        return;
      case UsingStatementSyntax us:
        Refuse(us, "`using (..)` is not in the Crust C# subset. Use a class with a destructor and scope exit.");
        return;
      case GotoStatementSyntax gt:
        Refuse(gt, "`goto` is not in the Crust C# subset.");
        return;
      case YieldStatementSyntax ys:
        Refuse(ys, "`yield` (iterators) is not in the Crust C# subset.");
        return;
      case CheckedStatementSyntax cs:
        Refuse(cs, "`checked` is not in the Crust C# subset: wrapping is the arithmetic here.");
        return;
      case LocalFunctionStatementSyntax lf:
        Refuse(lf, "local functions are not in the Crust C# subset. Make it a static method.");
        return;
      case UnsafeStatementSyntax us2:
        Refuse(us2, "`unsafe` is not in the Crust C# subset.");
        return;
      case FixedStatementSyntax fx:
        Refuse(fx, "`fixed` is not in the Crust C# subset.");
        return;
      default:
        Refuse(s, "statement `" + s.Kind() + "` is not in the Crust C# subset.");
        return;
    }
  }

  void BeginHoist() { hoisted.Clear(); canHoist = true; }
  void FlushHoisted()
  {
    canHoist = false;
    foreach (var h in hoisted) o.Text(h);
    hoisted.Clear();
  }

  class ReturnStatementData { public ExpressionSyntax Expression; public ReturnStatementData(ExpressionSyntax e) { Expression = e; } }
  string Return(ReturnStatementData rd) { return ReturnCore(rd.Expression); }
  string Return(ReturnStatementSyntax rs) { return ReturnCore(rs.Expression); }

  string ReturnCore(ExpressionSyntax rexpr)
  {
    if (rexpr == null) return "return;";
    var m = currentMethod;
    if (m != null && m.ReturnType.SpecialType == SpecialType.System_String) return ReturnString(rexpr);
    if (m != null && IsFluent(m)) return "return this;";
    if (m != null && Borrowed(m.ReturnType) && m.ReturnType.TypeKind != TypeKind.Interface && !IsFreshOrLocal(rexpr))
      Refuse(rexpr, "returning an existing object would copy it where C# returns the same one: a class is single-owner here. Return a new object, or give the caller a method that does the work.");
    return "return " + Expr(rexpr) + ";";
  }

  /** `return <string>;` -- cpprust moves a bare local out and refuses an expression, so go through a local. */
  string ReturnString(ExpressionSyntax e)
  {
    UsesString = true;
    var inner = e;
    while (inner is ParenthesizedExpressionSyntax pp) inner = pp.Expression;
    string n = Expr(inner);          //a named std::string: a local, a hoisted temporary, or a field / parameter
    if (inner is IdentifierNameSyntax id && model.GetSymbolInfo(id).Symbol is ILocalSymbol) return "return " + n + ";";
    if (n.StartsWith("_s") && n.All(ch => char.IsLetterOrDigit(ch) || ch == '_')) return "return " + n + ";";
    if (!canHoist) Refuse(e, "returning this string needs a temporary, which is not supported here.");
    string r = NewTemp("_r");
    hoisted.Add("std::string " + r + "(" + n + ");");
    return "return " + r + ";";
  }

  /** a fresh value, or a local variable (moved out); not a field, parameter or element. */
  bool IsFreshOrLocal(ExpressionSyntax e)
  {
    if (IsFresh(e)) return true;
    if (e is ParenthesizedExpressionSyntax p) return IsFreshOrLocal(p.Expression);
    if (e is IdentifierNameSyntax id) return model.GetSymbolInfo(id).Symbol is ILocalSymbol;
    return false;
  }

  void IfChain(IfStatementSyntax ifs, int line, bool isElseIf)
  {
    //the first condition is evaluated once, first: a string it needs can be built just before the `if`.
    //An `else if` condition runs only when the earlier ones failed, so it cannot be hoisted.
    if (!isElseIf) BeginHoist(); else canHoist = false;
    string cond = Expr(ifs.Condition);
    if (!isElseIf) { o.PadTo(line); FlushHoisted(); }
    canHoist = false;
    if (isElseIf) o.Text("if (" + cond + ")");
    else o.At(line, "if (" + cond + ")");
    Body(ifs.Statement);
    if (ifs.Else != null) {
      o.At(LineOf(ifs.Else.ElseKeyword), "else");
      if (ifs.Else.Statement is IfStatementSyntax inner) IfChain(inner, LineOf(inner), true);
      else Body(ifs.Else.Statement);
    }
  }

  void For(ForStatementSyntax fs, int line)
  {
    string init = "";
    if (fs.Declaration != null) {
      var parts = LocalDecl(fs.Declaration, false, forInit: true);
      init = parts[0];
    } else {
      init = string.Join(", ", fs.Initializers.Select(e => ExprStmt(e)));
    }
    string cond = fs.Condition == null ? "" : Expr(fs.Condition);
    string inc = string.Join(", ", fs.Incrementors.Select(e => ExprStmt(e)));
    o.At(line, "for (" + init + "; " + cond + "; " + inc + ")");
    Body(fs.Statement);
  }

  void ForEach(ForEachStatementSyntax fe, int line)
  {
    var coll = model.GetTypeInfo(fe.Expression).Type;
    if (!(coll is IArrayTypeSymbol) && !IsList(coll))
      Refuse(fe.Expression, "`foreach` over `" + coll.ToDisplayString() + "` is not in the Crust C# subset. Iterate an array or a List<T>.");
    var info = model.GetDeclaredSymbol(fe) as ILocalSymbol;
    string t = TypeName(info.Type, fe.Type);
    string e = Expr(fe.Expression);
    // a class element is borrowed: iterate by reference so nothing is copied
    string amp = IsOwnedClass(info.Type) ? "& " : " ";
    o.At(line, "for (" + t + amp + Ident(fe.Identifier.Text) + " : " + e + ")");
    Body(fe.Statement);
  }

  void Switch(SwitchStatementSyntax sw, int line)
  {
    var tt = model.GetTypeInfo(sw.Expression).Type;
    if (tt.SpecialType == SpecialType.System_String)
      Refuse(sw.Expression, "`switch` on a string is not in the Crust C# subset (no string). Switch on an int or an enum.");
    BeginHoist();
    string subject = Expr(sw.Expression);
    o.PadTo(line); FlushHoisted();
    o.At(line, "switch (" + subject + ") {");
    foreach (var sec in sw.Sections) {
      foreach (var lab in sec.Labels) {
        if (lab is CaseSwitchLabelSyntax cl) o.At(LineOf(lab), "case " + Expr(cl.Value) + ":");
        else if (lab is DefaultSwitchLabelSyntax) o.At(LineOf(lab), "default:");
        else Refuse(lab, "pattern `case` labels are not in the Crust C# subset. Use constant labels.");
      }
      o.Text("{");
      foreach (var st in sec.Statements) Stmt(st);
      o.At(LineOf(sec.GetLastToken()), "}");
    }
    o.At(LineOf(sw.CloseBraceToken), "}");
  }

  List<string> LocalDecl(VariableDeclarationSyntax decl, bool isConst, bool forInit = false)
  {
    var outp = new List<string>();
    var first = true;
    var common = new StringBuilder();
    foreach (var v in decl.Variables) {
      var ls = (ILocalSymbol)model.GetDeclaredSymbol(v);
      //`object o = new object();` -- an opaque value with identity and nothing else
      if (ls.Type.SpecialType == SpecialType.System_Object && v.Initializer != null
          && v.Initializer.Value is ObjectCreationExpressionSyntax oo && (oo.ArgumentList == null || oo.ArgumentList.Arguments.Count == 0)
          && oo.Initializer == null) {
        if (arrayHelpers.Add("_cs_object")) preamble.Append("class _cs_object { public: char _unused; }; ");
        string piece0 = "_cs_object " + Ident(v.Identifier.Text);
        if (forInit) { outp.Add(piece0); first = false; common.Append(piece0); continue; }
        outp.Add(piece0 + ";");
        continue;
      }
      if (v.Initializer != null && v.Initializer.Value is QueryExpressionSyntax)
        Refuse(v.Initializer.Value, "LINQ is not in the Crust C# subset. Write a loop.");
      string t = TypeName(ls.Type, decl.Type);
      string name = Ident(v.Identifier.Text);
      string piece;
      if (v.Initializer == null) {
        piece = t + " " + name;
      } else {
        var init = v.Initializer.Value;
        if (ls.Type.SpecialType == SpecialType.System_String) {
          if (forInit) Refuse(init, "a string declared in a `for` header is not in the Crust C# subset. Declare it before the loop.");
          UsesString = true;
          var icv = model.GetConstantValue(init);
          if (init is LiteralExpressionSyntax nlit && nlit.IsKind(SyntaxKind.NullLiteralExpression)) {
            Refuse(init, "`null` is not in the Crust C# subset: a string is a value here, empty at worst. Use \"\".");
            piece = "";
          } else if (icv.HasValue && icv.Value is string cs0) {
            piece = (isConst ? "const " : "") + "std::string " + name + "(\"" + CLit(cs0) + "\")";
          } else if (IsStringLvalue(init) || IsStringCall(init)) {
            piece = "std::string " + name + " = " + CallOrName(init);
          } else {
            //a concatenation / interpolation: build straight into the variable
            RequireHoist(init, "building this string");
            hoisted.Add("std::string " + name + ";");
            var sp = new List<object>();
            StringParts(init, sp);
            var saveRoot2 = hoistRoot; hoistRoot = init;
            try { foreach (var part in sp) AppendPart(name, part); } finally { hoistRoot = saveRoot2; }
            piece = "";
          }
        } else if (init is ObjectCreationExpressionSyntax || init is ImplicitObjectCreationExpressionSyntax) {
          piece = NewLocal(ls.Type, init, t, name);
        } else if (init is ArrayCreationExpressionSyntax ac) {
          piece = t + " " + name + " = " + NewArray(ac);
        } else if (init is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression)) {
          Refuse(init, "`null` is not in the Crust C# subset for owned classes: a class is a value, not a reference. Construct it, or use an arena class.");
          piece = "";
        } else if ((IsOwnedClass(ls.Type) || IsList(ls.Type) || ls.Type is IArrayTypeSymbol) && IsFresh(init)) {
          piece = t + " " + name + " = " + Expr(init);
        } else if (IsFluentCall(init)) {
          Refuse(init, "the result of a method that returns `this` can only start a chain or be dropped: storing it would alias the object, and a class is single-owner here. Call the methods on the original variable.");
          piece = "";
        } else if (IsOwnedClass(ls.Type) || IsList(ls.Type) || ls.Type is IArrayTypeSymbol) {
          Refuse(init, "initialising `" + v.Identifier.Text + "` from another object would alias it: a class is single-owner here, so there is one owner and no copy. Construct a new one with `new`, or pass the original by reference.");
          piece = "";
        } else {
          piece = (isConst ? "const " : "") + t + " " + name + " = " + Expr(init);
        }
      }
      if (forInit) {
        //`int i = 0, j = 0` shares one type; only valid when the types agree
        if (first) { common.Append(piece); first = false; }
        else {
          int sp = piece.IndexOf(' ');
          common.Append(", " + piece.Substring(t.Length + 1));
        }
      } else {
        if (piece.Length > 0) outp.Add(piece + ";");
      }
    }
    if (forInit) { outp.Add(common.ToString()); }
    return outp;
  }

  string NewLocal(ITypeSymbol type, ExpressionSyntax init, string t, string name)
  {
    ArgumentListSyntax al = null;
    if (init is ObjectCreationExpressionSyntax oc) {
      al = oc.ArgumentList;
      if (oc.Initializer != null)
        Refuse(oc.Initializer, "object / collection initializers are not in the Crust C# subset. Assign the fields on the next lines.");
    } else if (init is ImplicitObjectCreationExpressionSyntax ioc) {
      al = ioc.ArgumentList;
    }
    var args = al == null ? new List<string>() : al.Arguments.Select(a => Arg(a)).ToList();
    if (IsList(type) || IsDictionary(type)) {
      if (args.Count > 0) Refuse(init, "`new " + type.Name + "(..)` with arguments is not in the Crust C# subset. Construct it empty and Add.");
      return t + " " + name;
    }
    if (args.Count == 0) return t + " " + name;
    return t + " " + name + "(" + string.Join(", ", args) + ")";
  }

  // ------------------------------------------------------------------ expressions

  string Arg(ArgumentSyntax a)
  {
    if (a.NameColon != null) Refuse(a, "named arguments are not in the Crust C# subset. Pass them in order.");
    if (a.RefKindKeyword.RawKind != 0) {
      if (a.Expression is DeclarationExpressionSyntax)
        Refuse(a, "`out var x` is not in the Crust C# subset. Declare the variable first, then pass `out x`.");
      return Expr(a.Expression);                              //a C++ reference: the variable itself
    }
    var at = model.GetTypeInfo(a.Expression).Type;
    string text;
    if (at != null && (IsOwnedClass(at) || IsList(at) || at is IArrayTypeSymbol)
        && IsFresh(a.Expression) && !(a.Expression is ArrayCreationExpressionSyntax))
      text = Hoist(a, at);
    else
      text = Expr(a.Expression);
    var pt = ParamTypeOf(a);
    if (pt != null && at != null && NeedsUpcast(at, pt)) return Upcast(a, text, at, pt);
    return text;
  }

  /** the declared type of the parameter this argument binds to. */
  ITypeSymbol ParamTypeOf(ArgumentSyntax a)
  {
    var list = a.Parent as ArgumentListSyntax;
    if (list == null) return null;
    var ms = model.GetSymbolInfo(list.Parent).Symbol as IMethodSymbol;
    if (ms == null || ms.Parameters.Length == 0) return null;
    int i = list.Arguments.IndexOf(a);
    return ms.Parameters[Math.Min(i, ms.Parameters.Length - 1)].Type;
  }

  /** a class (or interface) value bound to a parameter of one of its base types */
  bool NeedsUpcast(ITypeSymbol from, ITypeSymbol to)
  {
    if (!(IsOwnedClass(from) || from.IsValueType == false && from.TypeKind == TypeKind.Class)) return false;
    if (!(IsOwnedClass(to) || to.TypeKind == TypeKind.Interface)) return false;
    if (SymbolEqualityComparer.Default.Equals(from.OriginalDefinition, to.OriginalDefinition)) return false;
    for (var b = from.BaseType; b != null; b = b.BaseType)
      if (SymbolEqualityComparer.Default.Equals(b.OriginalDefinition, to.OriginalDefinition)) return true;
    return from.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, to.OriginalDefinition));
  }

  /** Is `to` reachable from `from` along primary bases only?  Crust lays out one class base first and
      then interface bases, so only the primary chain is a pure cast (address unchanged). */
  bool IsPrimaryBase(ITypeSymbol from, ITypeSymbol to)
  {
    for (var t = from as INamedTypeSymbol; t != null; t = t.BaseType) {
      if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, to.OriginalDefinition)) return true;
      //the first base listed in the declaration is the layout base, whether class or interface
      var decl = t.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().FirstOrDefault();
      if (decl == null || decl.BaseList == null || decl.BaseList.Types.Count == 0) return false;
      var first = model.GetSymbolInfo(decl.BaseList.Types[0].Type).Symbol as INamedTypeSymbol;
      if (first == null) return false;
      if (first.TypeKind == TypeKind.Interface) return SymbolEqualityComparer.Default.Equals(first.OriginalDefinition, to.OriginalDefinition);
      t = first;
      if (SymbolEqualityComparer.Default.Equals(t.OriginalDefinition, to.OriginalDefinition)) return true;
      //continue from `first`, whose own BaseType is the next link
      var next = first;
      while (true) {
        var d2 = next.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (d2 == null || d2.BaseList == null || d2.BaseList.Types.Count == 0) return false;
        var f2 = model.GetSymbolInfo(d2.BaseList.Types[0].Type).Symbol as INamedTypeSymbol;
        if (f2 == null) return false;
        if (SymbolEqualityComparer.Default.Equals(f2.OriginalDefinition, to.OriginalDefinition)) return true;
        if (f2.TypeKind == TypeKind.Interface) return false;
        next = f2;
      }
    }
    return false;
  }

  /** `Call(derived)` where Call takes `Base &`: cpprust lowers it to `Call(&derived)`, a B* -> A* conversion
      that shivyc rejects.  A base reference bound through an explicit pointer cast lowers to
      `A *r = &(*(A *)&derived)`, which it accepts and which dispatches virtually as before. */
  string Upcast(ArgumentSyntax a, string text, ITypeSymbol from, ITypeSymbol to)
  {
    if (!IsPrimaryBase(from, to))
      Refuse(a, "`" + to.Name + "` is a secondary base of `" + from.Name + "`: Crust lays out one class base first and interface bases after it, so converting needs an address adjustment that is not supported yet. Make `" + to.Name + "` the first base, or pass the object through a method of its own class.");
    var e = a.Expression;
    while (e is ParenthesizedExpressionSyntax pp) e = pp.Expression;
    bool lvalue = e is IdentifierNameSyntax || e is MemberAccessExpressionSyntax || e is ElementAccessExpressionSyntax
                  || text.StartsWith("_t");
    if (!lvalue || (model.GetSymbolInfo(e).Symbol is IPropertySymbol ps && !ps.IsIndexer))
      Refuse(a, "passing a `" + from.Name + "` where a `" + to.Name + "` is expected needs a named variable, field or element. Assign the object to a local first.");
    if (!canHoist)
      Refuse(a, "passing a `" + from.Name + "` where a `" + to.Name + "` is expected is only supported in a plain statement, not inside a condition or initialiser. Do the call on its own line and keep the result in a local.");
    var list = (ArgumentListSyntax)a.Parent;
    var parts = new List<ExpressionSyntax>();
    foreach (var sib in list.Arguments) parts.Add(sib.Expression);
    if (list.Parent is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax rma) parts.Add(rma.Expression);
    if (!parts.All(x => x == a.Expression || IsPure(x) || text.StartsWith("_t")))
      Refuse(a, "the upcast of `" + from.Name + "` to `" + to.Name + "` would be evaluated before the other arguments, which changes what C# does. Assign it to a local first.");
    string name = "_u" + (++tmp);
    hoisted.Add(TypeName(to, a) + " &" + name + " = *(" + TypeName(to, a) + " *)&" + text + ";");
    return name;
  }

  /** `f(new T(x))`: Crust passes a reference as a pointer, so the argument needs an address.
      Declare a named local first -- only when that cannot change what C# evaluates, and in what order. */
  string Hoist(ArgumentSyntax a, ITypeSymbol at)
  {
    var call = a.Parent.Parent;
    string why = "a temporary object passed by reference has no address in Crust. Assign it to a local first and pass that.";
    if (!canHoist)
      Refuse(a, why);
    var parts = new List<ExpressionSyntax>();
    ArgumentListSyntax list = (ArgumentListSyntax)a.Parent;
    foreach (var sib in list.Arguments) if (sib != a) parts.Add(sib.Expression);
    if (call is InvocationExpressionSyntax inv && inv.Expression is MemberAccessExpressionSyntax rma) parts.Add(rma.Expression);
    var inner = a.Expression;
    while (inner is ParenthesizedExpressionSyntax pp) inner = pp.Expression;
    ArgumentListSyntax innerArgs = (inner as ObjectCreationExpressionSyntax)?.ArgumentList
                                   ?? (inner as ImplicitObjectCreationExpressionSyntax)?.ArgumentList
                                   ?? (inner as InvocationExpressionSyntax)?.ArgumentList;
    if (innerArgs != null) foreach (var ia in innerArgs.Arguments) parts.Add(ia.Expression);
    if (inner is InvocationExpressionSyntax) Refuse(a, "the result of a call passed by reference is not supported by CC# --crust yet. Assign it to a local first and pass that.");
    if (!parts.All(IsPure))
      Refuse(a, "a temporary object with side effects in this call would be built before the other arguments are evaluated, which changes what C# does. Assign it to a local first and pass that.");
    string name = "_t" + (++tmp);
    string t = TypeName(at, a);
    var args = innerArgs == null ? new List<string>() : innerArgs.Arguments.Select(x => Arg(x)).ToList();
    hoisted.Add(args.Count == 0 ? t + " " + name + ";" : t + " " + name + "(" + string.Join(", ", args) + ");");
    return name;
  }

  /** no calls, assignments, ++/--, or constructions: evaluating it earlier or later gives the same value. */
  bool IsPure(ExpressionSyntax e)
  {
    return !e.DescendantNodesAndSelf().Any(n =>
      n is InvocationExpressionSyntax || n is AssignmentExpressionSyntax || n is ObjectCreationExpressionSyntax
      || n is ImplicitObjectCreationExpressionSyntax || n is AwaitExpressionSyntax
      || (n is PostfixUnaryExpressionSyntax pu && (pu.IsKind(SyntaxKind.PostIncrementExpression) || pu.IsKind(SyntaxKind.PostDecrementExpression)))
      || (n is PrefixUnaryExpressionSyntax pr && (pr.IsKind(SyntaxKind.PreIncrementExpression) || pr.IsKind(SyntaxKind.PreDecrementExpression))));
  }

  /** an expression used as a statement: property ++/--/op= become a setter call. */
  string ExprStmt(ExpressionSyntax e)
  {
    return Expr(e, statement: true);
  }

  string Expr(ExpressionSyntax e, bool statement = false, bool recv = false)
  {
    if (!statement && !recv && IsFluentCall(e))
      Refuse(e, "the result of a method that returns `this` can only start a chain or be dropped: storing it would alias the object, and a class is single-owner here. Call the methods on the original variable.");
    //string-typed expressions: a named std::string (building it first when it is a composite)
    if (!(e is LiteralExpressionSyntax nl && nl.IsKind(SyntaxKind.NullLiteralExpression)) && !(e is AssignmentExpressionSyntax)) {
      var sti = model.GetTypeInfo(e).Type;
      if (sti != null && sti.SpecialType == SpecialType.System_String && !IsStringLvalue(e))
        return StrNamed(e);
    }
    //constants fold exactly as C# folds them
    var cv = model.GetConstantValue(e);
    if (cv.HasValue && !(e is LiteralExpressionSyntax le0 && le0.IsKind(SyntaxKind.NullLiteralExpression)))
      return Constant(e, cv.Value);

    switch (e) {
      case ParenthesizedExpressionSyntax p: return "(" + Expr(p.Expression) + ")";
      case LiteralExpressionSyntax l:
        if (l.IsKind(SyntaxKind.NullLiteralExpression))
          Refuse(l, "`null` is not in the Crust C# subset for owned classes: a class is a value, not a reference. Use an arena class `[MaxInstances(N)]` for references.");
        return l.Token.Text;
      case IdentifierNameSyntax id: return IdentifierExpr(id);
      case GenericNameSyntax gn: return IdentifierExpr(gn);
      case ThisExpressionSyntax _: return "(*this)";
      case BaseExpressionSyntax b:
        Refuse(b, "`base.M()` is not in the Crust C# subset. Call a shared protected method, or restructure.");
        return "";
      case MemberAccessExpressionSyntax ma: return MemberAccess(ma);
      case InvocationExpressionSyntax inv: return Invocation(inv);
      case ObjectCreationExpressionSyntax oc: return NewExpr(oc);
      case ImplicitObjectCreationExpressionSyntax ioc: return NewExpr(ioc);
      case ArrayCreationExpressionSyntax ac: return NewArray(ac);
      case ElementAccessExpressionSyntax ea:
        if (ea.ArgumentList.Arguments.Count != 1) Refuse(ea, "multi-index element access is not in the Crust C# subset.");
        return Expr(ea.Expression) + "[" + Expr(ea.ArgumentList.Arguments[0].Expression) + "]";
      case BinaryExpressionSyntax b: return Binary(b);
      case PrefixUnaryExpressionSyntax pu: return Prefix(pu, statement);
      case PostfixUnaryExpressionSyntax po: return Postfix(po, statement);
      case AssignmentExpressionSyntax asg: return Assign(asg, statement);
      case ConditionalExpressionSyntax c:
        return "(" + Expr(c.Condition) + " ? " + Expr(c.WhenTrue) + " : " + Expr(c.WhenFalse) + ")";
      case CastExpressionSyntax ce: return Cast(ce);
      case DefaultExpressionSyntax de: return DefaultOf(model.GetTypeInfo(de.Type).Type, de);
      case InterpolatedStringExpressionSyntax isx:
        Refuse(isx, "`$\"..\"` is only supported as the argument of Console.Write/WriteLine: Crust has no managed string.");
        break;
      case IsPatternExpressionSyntax ip:
        Refuse(ip, "`is` patterns are not in the Crust C# subset. Compare values with `==`.");
        break;
      case TypeOfExpressionSyntax to:
        Refuse(to, "`typeof` is not in the Crust C# subset: there is no reflection.");
        break;
      case AwaitExpressionSyntax aw:
        Refuse(aw, "`async` / `await` is not in the Crust C# subset.");
        break;
      case SimpleLambdaExpressionSyntax sl:
        Refuse(sl, "lambdas are not supported by CC# --crust yet.");
        break;
      case ParenthesizedLambdaExpressionSyntax pl:
        Refuse(pl, "lambdas are not supported by CC# --crust yet.");
        break;
      case QueryExpressionSyntax q:
        Refuse(q, "LINQ is not in the Crust C# subset. Write a loop.");
        break;
      case ThrowExpressionSyntax te:
        Refuse(te, "`throw` is not supported by CC# --crust yet.");
        break;
      case SwitchExpressionSyntax sx:
        Refuse(sx, "`switch` expressions are not in the Crust C# subset. Use a `switch` statement.");
        break;
      case StackAllocArrayCreationExpressionSyntax sa:
        Refuse(sa, "`stackalloc` is not in the Crust C# subset.");
        break;
      case ImplicitArrayCreationExpressionSyntax ia:
        Refuse(ia, "array initializers are not in the Crust C# subset yet. Allocate with `new T[n]` and assign the elements.");
        break;
      case InitializerExpressionSyntax ie:
        Refuse(ie, "array / collection initializers are not in the Crust C# subset yet. Allocate with `new T[n]` and assign the elements.");
        break;
    }
    Refuse(e, "expression `" + e.Kind() + "` is not in the Crust C# subset.");
    return "";
  }

  // ------------------------------------------------------------------ strings

  /** a string already in a variable: reading it needs no temporary. */
  bool IsStringLvalue(ExpressionSyntax e)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    var cv = model.GetConstantValue(e);
    if (cv.HasValue) return false;                                   //literals and const strings
    var sym = model.GetSymbolInfo(e).Symbol;
    if (e is IdentifierNameSyntax || e is MemberAccessExpressionSyntax) {
      if (sym is ILocalSymbol) return true;
      if (sym is IParameterSymbol) return true;
      if (sym is IFieldSymbol fs) return fs.ContainingType.SpecialType != SpecialType.System_String;   //string.Empty is not
      return false;                                                   //a property is a call
    }
    if (e is ElementAccessExpressionSyntax ea) {
      var rt = model.GetTypeInfo(ea.Expression).Type;
      if (IsMainArgs(ea.Expression)) return false;                    //`const char *` element: copied into a std::string
      return rt is IArrayTypeSymbol || IsList(rt);
    }
    return false;
  }

  bool IsMainArgs(ExpressionSyntax e)
  {
    var ps = model.GetSymbolInfo(e).Symbol as IParameterSymbol;
    return ps != null && entryMethod != null && SymbolEqualityComparer.Default.Equals(ps.ContainingSymbol, entryMethod)
           && ps.Type is IArrayTypeSymbol;
  }

  /** a call / property read returning a string, which cpprust accepts directly as an initialiser. */
  bool IsStringCall(ExpressionSyntax e)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    if (model.GetConstantValue(e).HasValue) return false;
    if (e is InvocationExpressionSyntax) return true;
    if (e is MemberAccessExpressionSyntax ma && model.GetSymbolInfo(ma).Symbol is IPropertySymbol) return true;
    return false;
  }
  string CallOrName(ExpressionSyntax e)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    if (e is InvocationExpressionSyntax inv) return Invocation(inv);
    if (e is MemberAccessExpressionSyntax ma) return MemberAccess(ma);
    return Expr(e);
  }

  string NewTemp(string prefix) { return prefix + (++tmp); }

  /** Hoisting a statement ahead of the current one is only sound when nothing else in the statement is
      evaluated before it and could be affected: every impure node must enclose it or be inside it. */
  void RequireHoist(ExpressionSyntax e, string what)
  {
    if (!canHoist)
      Refuse(e, what + " needs a temporary, which is only supported in a plain statement, not inside a condition or loop header. Build it on its own line first.");
    SyntaxNode scope = e.FirstAncestorOrSelf<StatementSyntax>();
    if (scope == null) return;
    //an `if` condition or a `switch` subject is evaluated first and once; its bodies are not part of it
    if (scope is IfStatementSyntax ifs && ifs.Condition.Span.Contains(e.Span)) scope = ifs.Condition;
    else if (scope is SwitchStatementSyntax sw && sw.Expression.Span.Contains(e.Span)) scope = sw.Expression;
    foreach (var n in scope.DescendantNodesAndSelf()) {
      if (!IsImpureNode(n)) continue;
      if (e.Span.Contains(n.Span) || n.Span.Contains(e.Span)) continue;
      if (hoistRoot != null && hoistRoot.Span.Contains(n.Span)) continue;
      Refuse(e, what + " would be built before the rest of this statement is evaluated, which changes what C# does (" + n.ToString().Split('\n')[0].Trim() + " has a side effect). Build it on its own line first.");
    }
  }

  bool IsImpureNode(SyntaxNode n)
  {
    return n is InvocationExpressionSyntax || n is AssignmentExpressionSyntax
      || n is ObjectCreationExpressionSyntax || n is ImplicitObjectCreationExpressionSyntax
      || (n is PostfixUnaryExpressionSyntax pu && (pu.IsKind(SyntaxKind.PostIncrementExpression) || pu.IsKind(SyntaxKind.PostDecrementExpression)))
      || (n is PrefixUnaryExpressionSyntax pr && (pr.IsKind(SyntaxKind.PreIncrementExpression) || pr.IsKind(SyntaxKind.PreDecrementExpression)));
  }

  static string CLit(string s) { return CEscape(s).Replace("%%", "%"); }

  /** flatten `a + b + (c + d)` and `$"..{x}.."` into its operands, in evaluation order. */
  void StringParts(ExpressionSyntax e, List<object> parts)
  {
    while (e is ParenthesizedExpressionSyntax p) e = p.Expression;
    var cv = model.GetConstantValue(e);
    if (cv.HasValue && cv.Value is string cs) { parts.Add(cs); return; }
    if (e is BinaryExpressionSyntax b && b.IsKind(SyntaxKind.AddExpression)
        && model.GetTypeInfo(b).Type.SpecialType == SpecialType.System_String) {
      StringParts(b.Left, parts); StringParts(b.Right, parts); return;
    }
    if (e is InterpolatedStringExpressionSyntax isx) {
      foreach (var c in isx.Contents) {
        if (c is InterpolatedStringTextSyntax tx) parts.Add(tx.TextToken.ValueText);
        else if (c is InterpolationSyntax ip) {
          if (ip.AlignmentClause != null || ip.FormatClause != null)
            Refuse(ip, "alignment / format specifiers in `$\"..\"` are not in the Crust C# subset.");
          StringParts(ip.Expression, parts);
        }
      }
      return;
    }
    parts.Add(e);
  }

  /** Append one operand of a concatenation to the std::string `t`. */
  void AppendPart(string t, object part)
  {
    if (part is string lit) { if (lit.Length > 0) hoisted.Add(t + ".append(\"" + CLit(lit) + "\");"); return; }
    var e = (ExpressionSyntax)part;
    var ty = model.GetTypeInfo(e).Type;
    if (ty != null && ty.TypeKind == TypeKind.Enum)
      Refuse(e, "a string made from an enum is not in the Crust C# subset (no `ToString`). Use `(int)value`.");
    switch (ty == null ? SpecialType.None : ty.SpecialType) {
      case SpecialType.System_String:
        hoisted.Add(t + " += " + Expr(e) + ";");           //Expr yields a named std::string
        return;
      case SpecialType.System_Boolean:
        NeedHelper("_cs_app_bool"); hoisted.Add("_cs_app_bool(" + t + ", " + Expr(e) + ");"); return;
      case SpecialType.System_Byte: case SpecialType.System_SByte: case SpecialType.System_Int16:
      case SpecialType.System_UInt16: case SpecialType.System_Int32: case SpecialType.System_Int64:
        NeedHelper("_cs_app_ll"); hoisted.Add("_cs_app_ll(" + t + ", (long long)(" + Expr(e) + "));"); return;
      case SpecialType.System_UInt32: case SpecialType.System_UInt64:
        NeedHelper("_cs_app_ull"); hoisted.Add("_cs_app_ull(" + t + ", (unsigned long long)(" + Expr(e) + "));"); return;
      case SpecialType.System_Single: case SpecialType.System_Double:
        Refuse(e, "a string made from a float / double is not exact in the Crust C# subset: C# prints the shortest round-trip form. Use scaled integers.");
        return;
      case SpecialType.System_Char:
        Refuse(e, "`char` is not in the Crust C# subset.");
        return;
    }
    Refuse(e, "a string made from `" + (ty == null ? "?" : ty.ToDisplayString()) + "` is not in the Crust C# subset. Use ints, bools and strings.");
  }

  void NeedHelper(string name)
  {
    if (!arrayHelpers.Add(name)) return;
    switch (name) {
      case "_cs_app_ull":
        preamble.Append("static void _cs_app_ull(std::string &s, unsigned long long u) { char b[24]; int n = 0; if (u == 0) { b[n] = '0'; n = n + 1; } while (u > 0) { b[n] = (char)('0' + (int)(u % 10)); n = n + 1; u = u / 10; } while (n > 0) { n = n - 1; s.push_back(b[n]); } } ");
        break;
      case "_cs_app_ll":
        NeedHelper("_cs_app_ull");
        preamble.Append("static void _cs_app_ll(std::string &s, long long v) { if (v < 0) { s.push_back('-'); _cs_app_ull(s, 0ULL - (unsigned long long)v); } else { _cs_app_ull(s, (unsigned long long)v); } } ");
        break;
      case "_cs_app_bool":
        preamble.Append("static void _cs_app_bool(std::string &s, bool v) { if (v) { s.append(\"True\"); } else { s.append(\"False\"); } } ");
        break;
    }
  }

  /** The value of a string expression as a *named* std::string, built into a temporary when it is a composite. */
  string StrNamed(ExpressionSyntax e)
  {
    UsesString = true;
    var inner = e;
    while (inner is ParenthesizedExpressionSyntax pp) inner = pp.Expression;
    //Main's string[] args hold `const char *`
    if (inner is ElementAccessExpressionSyntax ea && IsMainArgs(ea.Expression)) {
      RequireHoist(e, "an element of `args`");
      string a = NewTemp("_s");
      hoisted.Add("std::string " + a + "(" + Expr(ea.Expression) + "[" + Expr(ea.ArgumentList.Arguments[0].Expression) + "]);");
      return a;
    }
    var parts = new List<object>();
    StringParts(inner, parts);
    bool concat = inner is InterpolatedStringExpressionSyntax || (inner is BinaryExpressionSyntax)
                  || (model.GetConstantValue(inner).HasValue);
    RequireHoist(e, "building this string");
    string t = NewTemp("_s");
    if (concat) {
      hoisted.Add("std::string " + t + ";");
      var saveRoot = hoistRoot; hoistRoot = inner;
      try { foreach (var part in parts) AppendPart(t, part); } finally { hoistRoot = saveRoot; }
      return t;
    }
    //a call, a property read, string.Empty ...
    if (inner is MemberAccessExpressionSyntax ma0 && model.GetSymbolInfo(ma0).Symbol is IFieldSymbol f0 && f0.ContainingType.SpecialType == SpecialType.System_String) {
      hoisted.Add("std::string " + t + ";");           //string.Empty
      return t;
    }
    string call;
    switch (inner) {
      case InvocationExpressionSyntax inv: call = Invocation(inv); break;
      case MemberAccessExpressionSyntax ma: call = MemberAccess(ma); break;
      case IdentifierNameSyntax id: call = IdentifierExpr(id); break;
      case ConditionalExpressionSyntax _:
        Refuse(e, "`?:` producing a string is not in the Crust C# subset. Use an `if` that assigns the string.");
        return "";
      default:
        Refuse(e, "this string expression (`" + inner.Kind() + "`) is not in the Crust C# subset.");
        return "";
    }
    hoisted.Add("std::string " + t + " = " + call + ";");
    return t;
  }

  /** a `const char *` for printf / strcmp: a literal as written, a variable's c_str(), else a built temporary. */
  string StrCText(ExpressionSyntax e)
  {
    var inner = e;
    while (inner is ParenthesizedExpressionSyntax pp) inner = pp.Expression;
    var cv = model.GetConstantValue(inner);
    if (cv.HasValue && cv.Value is string s) return "\"" + CLit(s) + "\"";
    UsesString = true;
    return (IsStringLvalue(inner) ? Expr(inner) : StrNamed(inner)) + ".c_str()";
  }

  string StrCompare(BinaryExpressionSyntax b)
  {
    UsesStrcmp = true;
    string l = StrCText(b.Left);
    string r = StrCText(b.Right);
    return "(strcmp(" + l + ", " + r + ") " + (b.OperatorToken.Text == "==" ? "==" : "!=") + " 0)";
  }

  string Constant(ExpressionSyntax e, object v)
  {
    var ti = model.GetTypeInfo(e);
    var t = ti.ConvertedType ?? ti.Type;
    //an enum member keeps its name
    var sym = model.GetSymbolInfo(e).Symbol;
    if (sym is IFieldSymbol fs && fs.ContainingType != null && fs.ContainingType.TypeKind == TypeKind.Enum)
      return FlatName(fs.ContainingType) + "_" + Ident(fs.Name);
    if (t != null && t.TypeKind == TypeKind.Enum)
      return "(" + FlatName((INamedTypeSymbol)t) + ")" + Convert.ToInt64(v);
    if (v is bool b) return b ? "true" : "false";
    if (v is string) Refuse(e, "internal: a string constant must go through StrNamed.");
    if (v is char) Refuse(e, "`char` is not in the Crust C# subset. Use `byte` (or `int` for a code point).");
    if (v is int i) return i == int.MinValue ? "(-2147483647 - 1)" : i.ToString();
    if (v is uint u) return u + "U";
    if (v is long l) return l == long.MinValue ? "(-9223372036854775807LL - 1)" : l + "LL";
    if (v is ulong ul) return ul + "ULL";
    if (v is short sh) return sh.ToString();
    if (v is ushort us) return us.ToString();
    if (v is byte by) return by.ToString();
    if (v is sbyte sb) return sb.ToString();
    if (v is double d) return Dbl(d);
    if (v is float f) return Flt(f);
    Refuse(e, "constant of type `" + v.GetType().Name + "` is not in the Crust C# subset.");
    return "";
  }

  static string Dbl(double d)
  {
    if (double.IsNaN(d)) return "(0.0 / 0.0)";
    if (double.IsPositiveInfinity(d)) return "(1.0 / 0.0)";
    if (double.IsNegativeInfinity(d)) return "(-1.0 / 0.0)";
    string s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
    return s;
  }
  static string Flt(float f)
  {
    string s = f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0) s += ".0";
    return s + "f";
  }

  string DefaultOf(ITypeSymbol t, SyntaxNode at)
  {
    string d = DefaultInit(t);
    if (d != null) return d;
    if (t.IsValueType) return TypeName(t, at) + "()";
    Refuse(at, "`default(" + t.Name + ")` is `null` for a class, which is not in the Crust C# subset.");
    return "";
  }

  string IdentifierExpr(SimpleNameSyntax id)
  {
    var sym = model.GetSymbolInfo(id).Symbol;
    if (sym is IPropertySymbol ps) {
      string recv = ps.IsStatic ? FlatName(ps.ContainingType) + "::" : "";
      return recv + "get_" + Ident(ps.Name) + "()";
    }
    if (sym is IFieldSymbol fs && fs.IsStatic && fs.ContainingType.TypeKind != TypeKind.Enum)
      return StaticField(fs);
    if (sym is IMethodSymbol ms && ms.IsStatic)
      return FlatName(ms.ContainingType) + "::" + Ident(ms.Name);
    if (sym is INamedTypeSymbol nt) return FlatName(nt);
    return Ident(id.Identifier.Text);
  }

  string MemberAccess(MemberAccessExpressionSyntax ma)
  {
    var sym = model.GetSymbolInfo(ma).Symbol;
    var recvType = model.GetTypeInfo(ma.Expression).Type;

    if (recvType != null && recvType.SpecialType == SpecialType.System_String && ma.Name.Identifier.Text == "Length")
      return Expr(ma.Expression) + ".size()";
    //arrays and lists
    if (recvType is IArrayTypeSymbol && ma.Name.Identifier.Text == "Length")
      return Expr(ma.Expression) + ".size()";
    if (IsList(recvType) && ma.Name.Identifier.Text == "Count")
      return Expr(ma.Expression) + ".size()";
    if (IsDictionary(recvType) && ma.Name.Identifier.Text == "Count")
      return Expr(ma.Expression) + ".size()";

    if (sym is IPropertySymbol ps) {
      if (ps.IsStatic) return FlatName(ps.ContainingType) + "::get_" + Ident(ps.Name) + "()";
      return Receiver(ma.Expression) + "get_" + Ident(ps.Name) + "()";
    }
    if (sym is IFieldSymbol fs) {
      if (fs.ContainingType.TypeKind == TypeKind.Enum)
        return FlatName(fs.ContainingType) + "_" + Ident(fs.Name);
      if (fs.IsStatic) return StaticField(fs);
      return Receiver(ma.Expression) + Ident(fs.Name);
    }
    if (sym is IMethodSymbol ms) {
      if (ms.IsStatic) return FlatName(ms.ContainingType) + "::" + Ident(ms.Name);
      return Receiver(ma.Expression) + Ident(ms.Name);
    }
    if (sym is INamedTypeSymbol nt) return FlatName(nt);
    if (sym is INamespaceSymbol) return "";
    Refuse(ma, "member access `" + ma + "` is not in the Crust C# subset.");
    return "";
  }

  /** Inside its own class a static field is a bare name; elsewhere it is `Cls::Field`. */
  string StaticField(IFieldSymbol fs)
  {
    if (currentType != null && SymbolEqualityComparer.Default.Equals(currentType, fs.ContainingType))
      return Ident(fs.Name);
    return FlatName(fs.ContainingType) + "::" + Ident(fs.Name);
  }

  string Receiver(ExpressionSyntax e)
  {
    if (e is ThisExpressionSyntax) return "this->";
    if (IsFluentCall(e)) return Expr(e, recv: true) + "->";
    return Expr(e) + ".";
  }

  string Invocation(InvocationExpressionSyntax inv)
  {
    var sym = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
    if (sym == null) Refuse(inv, "call `" + inv + "` could not be resolved.");

    // Console
    if (sym.ContainingType != null && sym.ContainingType.ToDisplayString() == "System.Console")
      return ConsoleCall(inv, sym);

    // List<T> / Dictionary<K,V> members
    var ma = inv.Expression as MemberAccessExpressionSyntax;
    if (ma != null) {
      var rt = model.GetTypeInfo(ma.Expression).Type;
      string n = ma.Name.Identifier.Text;
      if (IsList(rt)) {
        string r = Expr(ma.Expression);
        if (n == "Add" && inv.ArgumentList.Arguments.Count == 1) return r + ".push_back(" + Arg(inv.ArgumentList.Arguments[0]) + ")";
        if (n == "Clear" && inv.ArgumentList.Arguments.Count == 0) return r + ".clear()";
        Refuse(inv, "`List<T>." + n + "` is not supported by CC# --crust yet. Available: Add, Clear, Count, the indexer, foreach.");
      }
      if (IsDictionary(rt))
        Refuse(inv, "`Dictionary<K,V>." + n + "` is not supported by CC# --crust yet. Available: Count.");
      if (rt != null && rt is INamedTypeSymbol nts && nts.ToDisplayString().StartsWith("System."))
        Refuse(inv, "`" + nts.ToDisplayString() + "." + n + "` is not in the Crust C# subset: the .NET class library is not available.");
    }
    if (sym.ContainingType != null && sym.ContainingType.ContainingNamespace != null
        && sym.ContainingType.ContainingNamespace.ToDisplayString().StartsWith("System")
        && sym.ContainingType.SpecialType != SpecialType.System_Object)
      Refuse(inv, "`" + sym.ContainingType.Name + "." + sym.Name + "` is not in the Crust C# subset: the .NET class library is not available.");
    if (sym.IsGenericMethod)
      Refuse(inv, "generic methods are not in the Crust C# subset.");

    string callee = Expr(inv.Expression);
    var args = inv.ArgumentList.Arguments.Select(a => Arg(a));
    return callee + "(" + string.Join(", ", args) + ")";
  }

  // ---- Console.Write / WriteLine -> printf

  string ConsoleCall(InvocationExpressionSyntax inv, IMethodSymbol sym)
  {
    string n = sym.Name;
    if (n != "Write" && n != "WriteLine")
      Refuse(inv, "`Console." + n + "` is not in the Crust C# subset. Crust provides Console.Write and Console.WriteLine.");
    string nl = n == "WriteLine" ? "\\n" : "";
    var args = inv.ArgumentList.Arguments;
    if (args.Count == 0) return "printf(\"" + nl + "\")";
    if (args.Count > 1)
      Refuse(inv, "`Console." + n + "` with a format string is not in the Crust C# subset. Use `$\"..{x}..\"` or several Write calls.");
    var fmt = new StringBuilder();
    var vals = new List<PVal>();
    PrintfPart(args[0].Expression, fmt, vals);
    fmt.Append(nl);
    //C leaves the order in which printf's arguments are evaluated unspecified, C# does not: when any value
    //has a side effect, snapshot every value into a temporary, left to right.  Strings that need building
    //are built at their own position in that order.
    bool ordered = vals.Count >= 2 && vals.Any(v => v.Impure);
    if (ordered || vals.Any(v => v.Deferred != null)) {
      var saveRoot = hoistRoot; hoistRoot = inv;
      try {
        foreach (var v in vals) {
          if (v.Deferred != null) { v.Text = StrNamed(v.Deferred) + ".c_str()"; v.Deferred = null; }
          if (ordered) {
            string tn = NewTemp("_v");
            hoisted.Add(v.CType + " " + tn + " = " + v.Text + ";");
            v.Text = tn;
          }
        }
      } finally { hoistRoot = saveRoot; }
    }
    string call = "printf(\"" + fmt + "\"";
    foreach (var v in vals) call += ", " + v.Text;
    return call + ")";
  }

  class PVal { public string Text, CType; public bool Impure; public ExpressionSyntax Deferred; }

  PVal Val(ExpressionSyntax node, string text, string ctype)
  {
    return new PVal { Text = text, CType = ctype, Impure = node.DescendantNodesAndSelf().Any(IsImpureNode) };
  }

  static string CEscape(string s)
  {
    var sb = new StringBuilder();
    foreach (char c in s) {
      switch (c) {
        case '%': sb.Append("%%"); break;
        case '\\': sb.Append("\\\\"); break;
        case '"': sb.Append("\\\""); break;
        case '\n': sb.Append("\\n"); break;
        case '\r': sb.Append("\\r"); break;
        case '\t': sb.Append("\\t"); break;
        case '\0': sb.Append("\\0"); break;
        default:
          if (c < 32 || c > 126) {
            foreach (byte b in Encoding.UTF8.GetBytes(c.ToString())) sb.Append("\\" + Convert.ToString(b, 8).PadLeft(3, '0'));
          } else sb.Append(c);
          break;
      }
    }
    return sb.ToString();
  }

  void PrintfPart(ExpressionSyntax e, StringBuilder fmt, List<PVal> vals)
  {
    while (e is ParenthesizedExpressionSyntax pe) e = pe.Expression;
    if (e is InterpolatedStringExpressionSyntax isx) {
      foreach (var part in isx.Contents) {
        if (part is InterpolatedStringTextSyntax tx) fmt.Append(CEscape(tx.TextToken.ValueText));
        else if (part is InterpolationSyntax ip) {
          if (ip.AlignmentClause != null || ip.FormatClause != null)
            Refuse(ip, "alignment / format specifiers in `$\"..\"` are not in the Crust C# subset.");
          PrintfPart(ip.Expression, fmt, vals);
        }
      }
      return;
    }
    var cv = model.GetConstantValue(e);
    if (cv.HasValue && cv.Value is string s) { fmt.Append(CEscape(s)); return; }
    var t = model.GetTypeInfo(e).Type;
    if (t != null && t.SpecialType == SpecialType.System_String) {
      if (e is BinaryExpressionSyntax sb && sb.IsKind(SyntaxKind.AddExpression)) {      //"a" + x: print the pieces in order
        PrintfPart(sb.Left, fmt, vals); PrintfPart(sb.Right, fmt, vals); return;
      }
      fmt.Append("%s");
      if (IsStringLvalue(e)) { UsesString = true; vals.Add(Val(e, Expr(e) + ".c_str()", "const char *")); }
      else vals.Add(new PVal { CType = "const char *", Impure = e.DescendantNodesAndSelf().Any(IsImpureNode), Deferred = e });
      return;
    }
    if (t != null && t.TypeKind == TypeKind.Enum)
      Refuse(e, "printing an enum is not in the Crust C# subset (no `ToString`). Print `(int)value`.");
    switch (t == null ? SpecialType.None : t.SpecialType) {
      case SpecialType.System_Boolean:
        fmt.Append("%s"); vals.Add(Val(e, "(" + Expr(e) + ") ? \"True\" : \"False\"", "const char *")); return;
      case SpecialType.System_Byte: case SpecialType.System_SByte:
      case SpecialType.System_Int16: case SpecialType.System_UInt16:
      case SpecialType.System_Int32:
        fmt.Append("%d"); vals.Add(Val(e, Expr(e), "int")); return;
      case SpecialType.System_UInt32: fmt.Append("%u"); vals.Add(Val(e, Expr(e), "unsigned")); return;
      case SpecialType.System_Int64: fmt.Append("%lld"); vals.Add(Val(e, Expr(e), "long long")); return;
      case SpecialType.System_UInt64: fmt.Append("%llu"); vals.Add(Val(e, Expr(e), "unsigned long long")); return;
      case SpecialType.System_Single: case SpecialType.System_Double:
        Refuse(e, "printing a float / double is not exact in the Crust C# subset: C# prints the shortest round-trip form, printf cannot. Print scaled integers.");
        return;
      case SpecialType.System_Char:
        Refuse(e, "`char` is not in the Crust C# subset.");
        return;
    }
    Refuse(e, "Console output of `" + (t == null ? "?" : t.ToDisplayString()) + "` is not in the Crust C# subset. Print ints, bools and string literals.");
  }

  // ---- object / array creation

  string NewExpr(ExpressionSyntax e)
  {
    var type = model.GetTypeInfo(e).Type;
    ArgumentListSyntax al = null;
    if (e is ObjectCreationExpressionSyntax oc) {
      al = oc.ArgumentList;
      if (oc.Initializer != null)
        Refuse(oc.Initializer, "object / collection initializers are not in the Crust C# subset. Assign the fields on the next lines.");
    } else if (e is ImplicitObjectCreationExpressionSyntax ioc) al = ioc.ArgumentList;
    var args = al == null ? new List<string>() : al.Arguments.Select(a => Arg(a)).ToList();
    return TypeName(type, e) + "(" + string.Join(", ", args) + ")";
  }

  string NewArray(ArrayCreationExpressionSyntax ac)
  {
    if (ac.Initializer != null)
      Refuse(ac, "array initializers are not in the Crust C# subset yet. Allocate with `new T[n]` and assign the elements.");
    var at = (IArrayTypeSymbol)model.GetTypeInfo(ac).Type;
    if (at.Rank > 1) Refuse(ac, "multidimensional arrays are not in the Crust C# subset. Use a jagged array `T[][]`.");
    var elem = at.ElementType;
    string d = DefaultInit(elem);
    if (d == null)
      Refuse(ac, "`new T[n]` of a class or struct is not in the Crust C# subset: the elements would be null. Use a List<T> and Add, or an arena class.");
    string et = TypeName(elem, ac);
    string helper = "_cs_new_array_" + Mangle(et);
    if (arrayHelpers.Add(helper)) {
      string def = "static std::vector<" + et + "> " + helper + "(int n) { std::vector<" + et + "> v; int i = 0; if (n < 0) { abort(); } while (i < n) { v.push_back(" + d + "); i = i + 1; } return v; } ";
      preamble.Append(def);
    }
    return helper + "(" + Expr(ac.Type.RankSpecifiers[0].Sizes[0]) + ")";
  }

  static string Mangle(string s)
  {
    var sb = new StringBuilder();
    foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
    return sb.ToString();
  }

  // ---- operators

  string Binary(BinaryExpressionSyntax b)
  {
    string op = b.OperatorToken.Text;
    switch (b.Kind()) {
      case SyntaxKind.AsExpression:
        Refuse(b, "`as` is not in the Crust C# subset: there is no runtime type information. Use a virtual method.");
        break;
      case SyntaxKind.IsExpression:
        Refuse(b, "`is` is not in the Crust C# subset: there is no runtime type information. Use a virtual method.");
        break;
      case SyntaxKind.CoalesceExpression:
        Refuse(b, "`??` is not in the Crust C# subset: there is no null for a value. ");
        break;
    }
    var lt = model.GetTypeInfo(b.Left).Type;
    var rt = model.GetTypeInfo(b.Right).Type;
    if ((lt != null && lt.SpecialType == SpecialType.System_String) || (rt != null && rt.SpecialType == SpecialType.System_String)) {
      if (op == "==" || op == "!=") return StrCompare(b);
      Refuse(b, "the string operator `" + op + "` is not in the Crust C# subset. Compare with `==` / `!=`, build with `+`.");
    }
    if ((op == "==" || op == "!=") && lt != null && (IsOwnedClass(lt) || lt.TypeKind == TypeKind.Interface))
      Refuse(b, "`==` on class references is not in the Crust C# subset: a class is a value, there is no identity. Compare a field.");
    if (op == "<<" || op == ">>") {
      int bits = BitWidth(model.GetTypeInfo(b).Type);
      return "(" + Expr(b.Left) + " " + op + " (" + Expr(b.Right) + " & " + (bits - 1) + "))";
    }
    if (op == ">>>")
      Refuse(b, "`>>>` is not in the Crust C# subset.");
    return Expr(b.Left) + " " + op + " " + Expr(b.Right);
  }

  int BitWidth(ITypeSymbol t)
  {
    if (t == null) return 32;
    switch (t.SpecialType) {
      case SpecialType.System_Int64: case SpecialType.System_UInt64: return 64;
      default: return 32;
    }
  }

  string Prefix(PrefixUnaryExpressionSyntax pu, bool statement)
  {
    string op = pu.OperatorToken.Text;
    if (op == "++" || op == "--") {
      var prop = PropertyTarget(pu.Operand);
      if (prop != null) {
        if (!statement) Refuse(pu, "`" + op + "` on a property inside a larger expression is not in the Crust C# subset. Make it a statement.");
        return PropSet(pu.Operand, prop, PropGet(pu.Operand, prop) + " " + op[0] + " 1");
      }
    }
    if (op == "^") op = "~";
    return op + Expr(pu.Operand);
  }

  string Postfix(PostfixUnaryExpressionSyntax po, bool statement)
  {
    string op = po.OperatorToken.Text;
    var prop = PropertyTarget(po.Operand);
    if (prop != null) {
      if (!statement) Refuse(po, "`" + op + "` on a property inside a larger expression is not in the Crust C# subset. Make it a statement.");
      return PropSet(po.Operand, prop, PropGet(po.Operand, prop) + " " + op[0] + " 1");
    }
    if (op == "!") Refuse(po, "the null-forgiving operator `!` is not in the Crust C# subset.");
    return Expr(po.Operand) + op;
  }

  IPropertySymbol PropertyTarget(ExpressionSyntax e)
  {
    var ps = model.GetSymbolInfo(e).Symbol as IPropertySymbol;
    return ps != null && !ps.IsIndexer ? ps : null;   //xs[i] on a List<T> is an indexer, not a get_/set_ pair
  }

  string PropRecv(ExpressionSyntax e, IPropertySymbol ps)
  {
    if (ps.IsStatic) return FlatName(ps.ContainingType) + "::";
    if (e is MemberAccessExpressionSyntax ma) return Receiver(ma.Expression);
    return "";
  }
  string PropGet(ExpressionSyntax e, IPropertySymbol ps) { return PropRecv(e, ps) + "get_" + Ident(ps.Name) + "()"; }
  string PropSet(ExpressionSyntax e, IPropertySymbol ps, string value)
  {
    return PropRecv(e, ps) + "set_" + Ident(ps.Name) + "(" + value + ")";
  }

  string Assign(AssignmentExpressionSyntax a, bool statement)
  {
    string op = a.OperatorToken.Text;
    if (a.IsKind(SyntaxKind.CoalesceAssignmentExpression))
      Refuse(a, "`??=` is not in the Crust C# subset.");
    var prop = PropertyTarget(a.Left);
    if (prop != null) {
      if (!statement) Refuse(a, "assigning a property inside a larger expression is not in the Crust C# subset. Make it a statement.");
      if (op == "=") return PropSet(a.Left, prop, Expr(a.Right));
      string bop = op.Substring(0, op.Length - 1);
      return PropSet(a.Left, prop, PropGet(a.Left, prop) + " " + bop + " " + Expr(a.Right));
    }
    var lt = model.GetTypeInfo(a.Left).Type;
    if (lt != null && lt.SpecialType == SpecialType.System_String) {
      if (!statement) Refuse(a, "assigning a string inside a larger expression is not in the Crust C# subset. Make it a statement.");
      if (op == "+=") {
        var cvr = model.GetConstantValue(a.Right);
        if (cvr.HasValue && cvr.Value is string lit) return Expr(a.Left) + ".append(\"" + CLit(lit) + "\")";
        var rty = model.GetTypeInfo(a.Right).Type;
        if (rty != null && rty.SpecialType == SpecialType.System_String) return Expr(a.Left) + " += " + Expr(a.Right);
        //s += 5  ->  append the digits
        RequireHoist(a.Right, "appending a number to a string");
        string lhs = Expr(a.Left);
        AppendPart(lhs, a.Right);
        return "";
      }
      if (op != "=") Refuse(a, "the string operator `" + op + "` is not in the Crust C# subset.");
      return Expr(a.Left) + " = " + Expr(a.Right);
    }
    if (op == "=" && lt != null && (IsOwnedClass(lt) || IsList(lt)) && !IsFresh(a.Right))
      Refuse(a, "assigning one object to another would alias it: a class is single-owner here. Construct with `new`, or copy the fields.");
    if (op == "<<=" || op == ">>=") {
      int bits = BitWidth(lt);
      return Expr(a.Left) + " " + op + " (" + Expr(a.Right) + " & " + (bits - 1) + ")";
    }
    if (op == ">>>=") Refuse(a, "`>>>=` is not in the Crust C# subset.");
    return Expr(a.Left) + " " + op + " " + Expr(a.Right);
  }

  string Cast(CastExpressionSyntax ce)
  {
    var from = model.GetTypeInfo(ce.Expression).Type;
    var to = model.GetTypeInfo(ce).Type;
    if (to != null && to.SpecialType == SpecialType.System_Char) Refuse(ce, "`char` is not in the Crust C# subset.");
    if (to != null && !to.IsValueType) Refuse(ce, "reference casts are not in the Crust C# subset: there is no runtime type information.");
    return "((" + TypeName(to, ce) + ")" + Expr(ce.Expression) + ")";
  }

  // ------------------------------------------------------------------ finish

  public string FinishPreamble(string text)
  {
    return text.Replace("_CS_PREAMBLE_ ", preamble.ToString());
  }
}
