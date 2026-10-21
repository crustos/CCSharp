/**

  CC# direct C back end, step 1: the C++ subset -> C, in C#, without Crust's cpprust.

  `ccs --lower-cpp P.main.cpp` reads the aggregate C++ that the --crust back end writes (its `#include "Type.cpp"` pieces are
  expanded in place) and prints the C for the PROGRAM part: its forward declarations, its prototypes, and its definitions
  (types, then `main`).  What goes around that -- the standard-library instances and the coost sources the program's headers
  reach -- is still cpprust's, spliced in by crust/test_direct_c.py today (see its direct_c()); lowering coost is a later step.

  The output is meant to be THE SAME C as cpprust's (crust/test_direct_c.py compares them after normalize_c), so this follows
  cpprust's rules, including its naming:   class Cls { int M(int a) }  ->  static int Cls_M(Cls *this, int a)
                                           Counter c(5);              ->  Counter c; Counter_new(&c, 5);
                                           a ref parameter            ->  a pointer, and `&arg` at the call.

  Anything not handled yet raises LowerUnsupported (exit status 3, the reason on stderr): never a guess.

*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CCSharpCompiler;

/** C++ the direct lowerer does not handle yet.  The caller falls back to cpprust. */
class LowerUnsupported : Exception
{
  public LowerUnsupported(string msg) : base(msg) { }
}

class CppLowerer
{
  public class Sections
  {
    public string Skeleton = "";     //the aggregate with the program's types replaced by one empty slot class: lowered by cpprust, it is the program's surroundings
    public string Fwd = "";          //struct X; typedef struct X X;  for each type
    public string Proto = "";        //prototypes of the types' functions
    public string Enums = "";        //each enum's definition and `typedef enum X X;`: cpprust hoists them above everything it writes for the program's classes
    public string Structs = "";      //the struct definitions (with the static fields), each class's followed by the `;` of its C++ class except the last
    public bool Behavioral;          //the C is not the text cpprust writes (inheritance): the test compares what it does
    public string Defs = "";         //the function bodies, then the last class's `;`, then main
  }

  public const string Slot = "CcsSlot";

  /** `ccs --lower-cpp FILE.cpp`: the sections, each after a line `@@name`.  Exit status 3: not handled yet (the reason on stderr). */
  public static int Run(string path, string foreignPath)
  {
    try {
      var s = new CppLowerer().Lower(path, foreignPath);
      if (foreignPath == null) Console.Out.Write("@@skeleton\n" + s.Skeleton + "\n");          // phase 1: only the surroundings' source
      else Console.Out.Write("@@skeleton\n" + s.Skeleton + "\n@@enums\n" + s.Enums + "@@fwd\n" + s.Fwd + "@@proto\n" + s.Proto + "@@structs\n" + s.Structs + "@@defs\n" + s.Defs + (s.Behavioral ? "\n@@behavioral\n1\n" : ""));
      return 0;
    } catch (LowerUnsupported e) {
      Console.Error.WriteLine("not lowered directly yet: " + e.Message);
      return 3;
    }
  }

  // ------------------------------------------------------------------ tokens

  enum K { Ws, Ident, Num, Str, Chr, Punct, Pre, Comment }

  class Tok
  {
    public K Kind;
    public string S;
    public override string ToString() { return S; }
  }

  static readonly string[] Puncts3 = { "<<=", ">>=", "...", "->*" };
  static readonly string[] Puncts2 = { "::", "->", "++", "--", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=" };

  static bool IsIdStart(char c) { return char.IsLetter(c) || c == '_'; }
  static bool IsIdChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }
  static bool IsSpace(char c) { return c == ' ' || c == '\t' || c == '\r' || c == '\n'; }

  static List<Tok> Lex(string s)
  {
    var r = new List<Tok>();
    int i = 0, n = s.Length;
    bool lineStart = true;
    while (i < n) {
      char c = s[i];
      if (IsSpace(c)) {
        int j = i;
        while (j < n && IsSpace(s[j])) j++;
        string ws = s.Substring(i, j - i);
        r.Add(new Tok { Kind = K.Ws, S = ws });
        if (ws.Contains('\n')) lineStart = true;
        i = j;
        continue;
      }
      if (c == '#' && lineStart) {
        int j = i;
        while (j < n && s[j] != '\n') j++;
        r.Add(new Tok { Kind = K.Pre, S = s.Substring(i, j - i).TrimEnd('\r') });
        i = j;
        continue;
      }
      lineStart = false;
      if (c == '/' && i + 1 < n && s[i + 1] == '/') {
        int j = i;
        while (j < n && s[j] != '\n') j++;
        r.Add(new Tok { Kind = K.Comment, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      if (c == '/' && i + 1 < n && s[i + 1] == '*') {
        int j = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
        j = j < 0 ? n : j + 2;
        r.Add(new Tok { Kind = K.Comment, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      if (IsIdStart(c)) {
        int j = i;
        while (j < n && IsIdChar(s[j])) j++;
        r.Add(new Tok { Kind = K.Ident, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1]))) {
        int j = i;
        bool hex = c == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X');
        while (j < n) {
          char d = s[j];
          if (IsIdChar(d) || d == '.') { j++; continue; }
          if ((d == '+' || d == '-') && !hex && j > i && (s[j - 1] == 'e' || s[j - 1] == 'E')) { j++; continue; }
          break;
        }
        r.Add(new Tok { Kind = K.Num, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      if (c == '"' || c == '\'') {
        int j = i + 1;
        while (j < n && s[j] != c) { if (s[j] == '\\') j++; j++; }
        j = Math.Min(n, j + 1);
        r.Add(new Tok { Kind = c == '"' ? K.Str : K.Chr, S = s.Substring(i, j - i) });
        i = j;
        continue;
      }
      string p = null;
      foreach (var q in Puncts3) if (string.CompareOrdinal(s, i, q, 0, 3) == 0) { p = q; break; }
      if (p == null) foreach (var q in Puncts2) if (string.CompareOrdinal(s, i, q, 0, 2) == 0) { p = q; break; }
      if (p == null) p = c.ToString();
      r.Add(new Tok { Kind = K.Punct, S = p });
      i += p.Length;
    }
    return r;
  }

  static bool IsSig(Tok t) { return t.Kind != K.Ws && t.Kind != K.Comment; }
  static bool Is(Tok t, string s) { return (t.Kind == K.Punct || t.Kind == K.Ident) && t.S == s; }

  static int NextSig(List<Tok> t, int p)
  {
    while (p < t.Count && !IsSig(t[p])) p++;
    return p < t.Count ? p : -1;
  }

  static string Join(List<Tok> t, int a, int b)
  {
    var sb = new StringBuilder();
    for (int i = a; i < b; i++) sb.Append(t[i].S);
    return sb.ToString();
  }

  static int MatchParen(List<Tok> t, int open)
  {
    int d = 0;
    for (int i = open; i < t.Count; i++) {
      if (Is(t[i], "(")) d++;
      else if (Is(t[i], ")")) { d--; if (d == 0) return i; }
    }
    throw new LowerUnsupported("unbalanced parenthesis");
  }

  static int MatchBrace(List<Tok> t, int open)
  {
    int d = 0;
    for (int i = open; i < t.Count; i++) {
      if (Is(t[i], "{")) d++;
      else if (Is(t[i], "}")) { d--; if (d == 0) return i; }
    }
    throw new LowerUnsupported("unbalanced brace");
  }

  /** The `;` that ends the statement starting at t[from], skipping anything nested. */
  static int StmtEnd(List<Tok> t, int from)
  {
    int depth = 0;
    for (int i = from; i < t.Count; i++) {
      if (Is(t[i], "(") || Is(t[i], "{") || Is(t[i], "[")) depth++;
      else if (Is(t[i], ")") || Is(t[i], "}") || Is(t[i], "]")) depth--;
      else if (depth == 0 && Is(t[i], ";")) return i;
    }
    throw new LowerUnsupported("a statement without its `;`");
  }

  // ------------------------------------------------------------------ the program's declarations

  class Param
  {
    public string Type;        //without a trailing `&`
    public string Name;
    public bool IsRef;
  }

  class Member
  {
    public bool IsMethod;
    public bool IsCtor;
    public bool Synthesized;           //a constructor that only runs the field initialisers: the class wrote none
    public bool Static;
    public bool Virtual;               //declared `virtual`, or overrides one
    public bool Pure;                  //`= 0`
    public List<Tok> BaseCall;         //a constructor's `: Base(args)`, parentheses included
    public string Name;
    public string Ret;                 //methods
    public string Type;                //fields
    public string Init;                //fields: the initialiser text, or null
    public List<Param> Params = new List<Param>();
    public List<Tok> Body;             //the `{ ... }` of a function
  }

  class ClassInfo
  {
    public string Name;
    public string Base;                //`: public Base`
    public List<Member> Members = new List<Member>();
    public IEnumerable<Member> Fields { get { return Members.Where(m => !m.IsMethod && !m.Static); } }
    public IEnumerable<Member> Statics { get { return Members.Where(m => !m.IsMethod && m.Static); } }
    public IEnumerable<Member> Methods { get { return Members.Where(m => m.IsMethod && !m.IsCtor); } }
    public IEnumerable<Member> Ctors { get { return Members.Where(m => m.IsCtor); } }
  }

  class Local
  {
    public string Type;                //the lowered type
    public string Class;               //the class it is a value of, or null
    public string Vector;              //the lowered name of a std::vector<T> instance, or null
    public string Foreign;             //a library type (coost's fastring ...) it is a value of, or null
    public bool PtrRef;                //a reference to such a pointer (C: a pointer to the pointer)
    public bool Pointer;               //a pointer to a program class (an arena reference), not a by-reference parameter
    public string Elem;                //a vector's element class, or null
    public string Alias;               //a range-for variable that is a reference: its uses are the element expression itself
    public bool IsPtr;                 //a reference parameter: a pointer in C
    public bool Droppable;             //something to destroy at every exit from its scope
    public bool DropAfterWs;           //a temporary the statement hoisted: cpprust drops it on a line of its own, after the statement
  }

  class Proto
  {
    public string Ret, Name;
    public List<string> Params = new List<string>();
  }

  /** What cpprust knows about the library types a program uses, read from the prototypes in ITS C for the program's surroundings (see
      direct_c in crust/test_direct_c.py): which names are types, which functions are their methods (with cpprust's overload numbers),
      which parameters are references (a pointer to a type), and which types have a destructor.  Step 2 reads the coost headers itself. */
  class ForeignInfo
  {
    public HashSet<string> Types = new HashSet<string>();
    public Dictionary<string, List<Proto>> ByName = new Dictionary<string, List<Proto>>();

    static readonly Regex TypedefRe = new Regex("^typedef struct (\\w+) \\1;", RegexOptions.Multiline);
    static readonly Regex ProtoRe = new Regex("^(?:static\\s+)?(?:inline\\s+)?(?<ret>[A-Za-z_][^()\\n]*?)\\s*\\b(?<name>[A-Za-z_]\\w*)\\((?<params>[^()\\n]*)\\)[ \\t]*(?:;|\\{)", RegexOptions.Multiline);
    static readonly HashSet<string> NotRet = new HashSet<string> { "return", "else", "goto", "case", "typedef", "break", "continue", "do", "if", "while", "for" };

    public static ForeignInfo Parse(string c)
    {
      var f = new ForeignInfo();
      foreach (Match m in TypedefRe.Matches(c)) f.Types.Add(m.Groups[1].Value);
      foreach (Match m in ProtoRe.Matches(c)) {
        string ret = m.Groups["ret"].Value.Trim();
        if (NotRet.Contains(ret.Split(' ')[0])) continue;
        var pr = new Proto { Ret = ret, Name = m.Groups["name"].Value };
        string ps = m.Groups["params"].Value.Trim();
        if (ps.Length > 0 && ps != "void") pr.Params.AddRange(ps.Split(',').Select(x => x.Trim()));
        if (!f.ByName.TryGetValue(pr.Name, out var list)) f.ByName[pr.Name] = list = new List<Proto>();
        if (!list.Any(x => x.Ret == pr.Ret && string.Join(",", x.Params) == string.Join(",", pr.Params))) list.Add(pr);
      }
      return f;
    }

    /** Type_member and Type_member_N: cpprust numbers the overloads of a name. */
    public List<Proto> Members(string type, string member)
    {
      var re = new Regex("^" + Regex.Escape(type + "_" + member) + "(_\\d+)?$");
      var r = new List<Proto>();
      foreach (var kv in ByName) if (re.IsMatch(kv.Key)) r.AddRange(kv.Value);
      return r;
    }

    public bool IsInst(Proto p, string type)
    {
      return p.Params.Count > 0 && Regex.IsMatch(p.Params[0], "^(?:const\\s+)?" + Regex.Escape(type) + "\\s*\\*\\s*this$");
    }

    public bool HasDrop(string type) { return ByName.ContainsKey(type + "_drop"); }

    /** The parameters after `this`, as the caller sees them: a pointer to one of the types is a C++ reference, so the argument goes by address. */
    public List<Param> Declared(Proto p, bool inst)
    {
      var r = new List<Param>();
      foreach (var x in p.Params.Skip(inst ? 1 : 0)) {
        var m = Regex.Match(x, "^(?:const\\s+)?(\\w+)\\s*\\*\\s*\\w*$");
        r.Add(new Param { Type = x, Name = "", IsRef = (m.Success && Types.Contains(m.Groups[1].Value)) || ( Regex.IsMatch(x, "^(?:unsigned\\s+)?(?:int|long long|long|short|double|float|bool|unsigned|size_t|int64_t|uint64_t|int32_t|uint32_t)\\s*\\*\\s*\\w*$")) });
      }
      return r;
    }
  }

  class Ctx
  {
    public ClassInfo Cls;
    public bool IsStatic = true;
    public string Ret = "void";
    public List<Dictionary<string, Local>> Scopes = new List<Dictionary<string, Local>>();
    public List<KeyValuePair<int, bool>> Targets = new List<KeyValuePair<int, bool>>();      //what `break`/`continue` leave: the scope count at the loop's body, and whether it is a switch
    public Local Find(string n)
    {
      for (int i = Scopes.Count - 1; i >= 0; i--) if (Scopes[i].TryGetValue(n, out var l)) return l;
      return null;
    }
  }

  // ------------------------------------------------------------------ lowering

  /** `enum K_values { .. }; typedef int K;` (an enum of the C# program): cpprust lifts the definition to the top and leaves the typedef where it was, the
      lifted text replaced by as many spaces. */
  class EnumItem
  {
    public string Tag;                 //K_values
    public string Hoisted;             //enum K_values { .. };
    public string InPlace;             //what stays: spaces, then `typedef int K;`
  }

  readonly Dictionary<string, ClassInfo> classes = new Dictionary<string, ClassInfo>();
  readonly List<ClassInfo> order = new List<ClassInfo>();
  readonly Dictionary<string, Member> helpers = new Dictionary<string, Member>();
  readonly List<object> items = new List<object>();          //the program's classes and helper functions, in order
  int retCounter = 0;
  string entryClass;                   //the class whose Main main() calls

  static readonly HashSet<string> PrimWords = new HashSet<string> {
    "int", "long", "short", "char", "bool", "float", "double", "void", "unsigned", "signed", "size_t",
    "int8_t", "int16_t", "int32_t", "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t", "const"
  };

  static readonly Regex PieceInclude = new Regex("^[ \\t]*#[ \\t]*include[ \\t]*\"([^\"]+\\.cpp)\"[ \\t]*\\r?\\n?", RegexOptions.Multiline);

  readonly HashSet<string> declared = new HashSet<string>();     //every class the program names, so a class may mention itself or one below it

  ForeignInfo foreign;                 //null in phase 1 (only the skeleton is wanted)

  public Sections Lower(string path, string foreignPath)
  {
    if (foreignPath != null) foreign = ForeignInfo.Parse(File.ReadAllText(foreignPath));
    string dir = Path.GetDirectoryName(Path.GetFullPath(path));
    string raw = File.ReadAllText(path);
    var pieces = PieceInclude.Matches(raw);
    if (pieces.Count == 0) throw new LowerUnsupported("no `#include \"Type.cpp\"` in the aggregate: nothing to lower");
    string prefix = raw.Substring(0, pieces[0].Index);                                   //the headers, the wrapv pragma, the helpers: the surroundings' own
    var last = pieces[pieces.Count - 1];
    string mainRaw = raw.Substring(last.Index + last.Length);
    var toks = Expand(Lex(raw.Substring(pieces[0].Index)), dir, 0);
    ExpandTemplates(toks);
    if (Environment.GetEnvironmentVariable("CCS_DUMP") is string dump) File.WriteAllText(dump, string.Concat(toks.Select(x => x.S)));
    programText = string.Concat(toks.Select(x => x.S));
    rtti = Regex.IsMatch(programText, "\\bdynamic_cast\\s*<");
    {                                                                              // the method names the source calls on a call result (or an element): `f(..).M(`, `a[i]->M(`
      var sig = toks.Where(x => x.Kind != K.Ws && x.Kind != K.Comment).ToList();
      for (int k = 0; k + 3 < sig.Count; k++)
        if ((Is(sig[k], ")") || Is(sig[k], "]")) && (Is(sig[k + 1], ".") || Is(sig[k + 1], "->")) && sig[k + 2].Kind == K.Ident && Is(sig[k + 3], "(")) chained.Add(sig[k + 2].S);
    }
    foreach (Match dm in Regex.Matches(programText, "\\b(?:class|struct)\\s+(\\w+)\\s*(?::[^{;]*)?\\{")) declared.Add(dm.Groups[1].Value);               // (what comes before is the surroundings' own: it is in the skeleton)
    var defs = new StringBuilder();
    string mainText = null;
    int p = 0;
    bool seenType = false;
    while (p < toks.Count) {
      var t = toks[p];
      if (!IsSig(t)) { p++; continue; }
      if (t.Kind == K.Pre) {
        if (t.S.StartsWith("#line")) throw new LowerUnsupported("`#line` (a type that was moved above its source position)");
        if (seenType) throw new LowerUnsupported("a preprocessor line between types: " + t.S);
        p++;
        continue;
      }
      if (!seenType && t.Kind == K.Ident && t.S == "_Pragma") {
        while (p < toks.Count && !Is(toks[p], ")")) p++;
        p++;
        continue;
      }
      if (Is(t, "enum")) {
        var en = ParseEnum(toks, ref p);
        items.Add(en);
        continue;
      }
      if (Is(t, "class") || Is(t, "struct")) {
        seenType = true;
        var ci = ParseClass(toks, ref p);
        classes[ci.Name] = ci;
        order.Add(ci);
        items.Add(ci);
        continue;
      }
      if (seenType && Is(t, "static")) {                                           // a helper function between the types
        var fn = ParseFree(toks, ref p);
        items.Add(fn);
        helpers[fn.Name] = fn;
        continue;
      }
      if (!seenType) throw new LowerUnsupported("something before the first type that is not an include or the pragma (a helper?): " + t.S);
      mainText = ParseMain(toks, ref p);
    }
    if (mainText == null) throw new LowerUnsupported("no `main` in the aggregate");
    foreach (var ci in order) PrepareInheritance(ci);
    var sec = new Sections();
    bool boolText = toks.Any(x => x.Kind == K.Ident && (x.S == "bool" || x.S == "true" || x.S == "false"));
    var word = new Regex("\\bbool\\b");
    bool boolHead = order.Any(ci => ci.Members.Any(m => m.IsMethod && ((m.Ret != null && word.IsMatch(m.Ret)) || m.Params.Any(x => word.IsMatch(x.Type)))));   // a prototype says bool
    sec.Skeleton = Skeleton(prefix, mainRaw, boolText, boolHead);
    if (foreign == null) return sec;                                              // phase 1
    foreach (var ci in order) {
      sec.Fwd += "struct " + ci.Name + ";\ntypedef struct " + ci.Name + " " + ci.Name + ";\n";
      foreach (var m in ci.Members.Where(x => x.IsMethod && !x.Pure)) sec.Proto += Regex.Replace(Signature(ci, m), "\\(\\*(\\w+)\\)", "*$1") + ";\n";
      if (ClassHasCopy(ci)) sec.Proto += "static void " + ci.Name + "_copy(" + ci.Name + " *this, const " + ci.Name + " *o);\nstatic void " + ci.Name + "__assign(" + ci.Name + " *this, const " + ci.Name + " *o);\n";
      if (IsArena(ci)) sec.Proto += "static void " + ci.Name + "__free_slot(" + ci.Name + " *p);\n";
      if (IsArena(ci)) foreach (var c in ci.Ctors) sec.Proto += "static " + ci.Name + " *" + AllocName(ci, c) + "(" + (c.Params.Count == 0 ? "void" : string.Join(", ", c.Params.Select(LowerParam))) + ");\n";
      if (!ClassHasDrop(ci))
        foreach (var m in ci.Methods.Where(x => !x.Static && !x.Virtual && chained.Contains(x.Name)))
          sec.Proto += "static " + LowerType(m.Ret, out _, out _) + " " + ByValName(ci, m) + "(" + ci.Name + " self" + string.Concat(m.Params.Select(q => ", " + LowerParam(q))) + ");\n";
      foreach (var sl in SlotsOf(ci).Where(x => x.Value == ci && chained.Contains(x.Key.Name)))
        sec.Proto += "static " + LowerType(sl.Key.Ret, out _, out _) + " " + ci.Name + "__vcall_" + sl.Key.Name + "(" + ci.Name + " *this" + string.Concat(sl.Key.Params.Select(q => ", " + LowerParam(q))) + ");\n";
      foreach (var suffix in new[] { "", "_P" }) {
        if (!(suffix == "" ? vecClasses : ptrVecClasses).Contains(ci.Name)) continue;
        sec.Fwd += "/*CCS-VEC-FWD:" + ci.Name + suffix + "*/\n";
        sec.Proto += "/*CCS-VEC-PROTO:" + ci.Name + suffix + "*/\n";
      }
    }
    if (Regex.IsMatch(programText, "(?<![\\w.>])(?:new\\s+\\w+|delete\\b)")) defs.Append("/*CCS-HEAP*/\n");        // `new` and `delete` make cpprust declare malloc and free first thing
    // cpprust writes every class's struct first (each followed by the `;` that ended the C++ class, except the last one's, which comes
    // after the functions), then the library instances' bodies, then every function, then that last `;`, then main.
    var structs = new StringBuilder();
    var enums = new StringBuilder();
    var bodies = new Dictionary<ClassInfo, string>();
    foreach (var it in items) {
      if (it is ClassInfo cc) { bodies[cc] = EmitClass(cc, out var head); structs.Append(head); if (cc != order[order.Count - 1]) structs.Append("\n;\n"); }
      else if (it is EnumItem en) { structs.Append(en.InPlace); enums.Append(en.Hoisted).Append("\ntypedef enum ").Append(en.Tag).Append(' ').Append(en.Tag).Append(";\n"); }
      else structs.Append(EmitFree((Member)it));                                  // a helper the C# compiler wrote between the types stays where it is, among the structs
    }
    // A function used above where its body now is needs a prototype, and cpprust gives one, after all the classes' own, to each function it has not
    // declared yet (late_bodies in cpprust.py): `Bag_drop`, which the program never names, but which runs where a `Bag` local goes out of scope.
    var have = new HashSet<string>(Regex.Matches(sec.Proto, "(\\w+)\\s*\\(").Select(x => x.Groups[1].Value));
    foreach (var ci in order)
      foreach (Match fm in Regex.Matches(bodies[ci], "^(static\\s+(?:inline\\s+)?[^;={}()]*?\\b(\\w+)\\s*\\([^()]*\\))\\s*\\{", RegexOptions.Multiline))
        if (have.Add(fm.Groups[2].Value)) sec.Proto += fm.Groups[1].Value + ";\n";
    // `T__free(p)` of every arena class, after the classes' functions: a `Base *` may point into the arena of any class that derives from it, and the
    // slot goes back to the arena it came from, found by address.  (No class derives from another here yet, so each tests its own.)
    var arenas = order.Where(IsArena).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
    foreach (var a in arenas) sec.Proto += "static void " + a.Name + "__free(" + a.Name + " *p);\n";
    foreach (var it in items) if (it is ClassInfo cc) defs.Append(bodies[cc]);
    if (arenas.Count > 0) {
      defs.Append('\n');
      foreach (var a in arenas) {
        var family = new List<ClassInfo> { a };                                      // the class, its children, then theirs
        for (int fi = 0; fi < family.Count; fi++) family.AddRange(order.Where(x => x.Base == family[fi].Name && IsArena(x)));
        defs.Append("static void " + a.Name + "__free(" + a.Name + " *p) { ");
        foreach (var f in family) {
          string n = f.Statics.First(x => x.Name == "__max_instances").Init;
          defs.Append("if ((char *)p >= (char *)" + f.Name + "__arena && (char *)p < (char *)(" + f.Name + "__arena + " + n + ")) { " + f.Name + "__free_slot((" + f.Name + " *)p); return; } ");
        }
        defs.Append("}\n");
      }
    }
    defs.Append("\n;\n").Append(mainText);
    sec.Behavioral = selfHolders || order.Any(c => c.Base != null || HasVtable(c));
    sec.Enums = enums.ToString();
    sec.Structs = (rtti ? "typedef struct _CppTypeInfo { const struct _CppTypeInfo *base; } _CppTypeInfo;\nstatic inline void *_cpp_dyncast(void *o, const void *t) { const _CppTypeInfo *want = (const _CppTypeInfo *)t; const _CppTypeInfo *k = o ? *(const _CppTypeInfo *const *)o : 0; for (; k; k = k->base) if (k == want) return o; return 0; }\n" : "") + structs.ToString();
    sec.Defs = defs.ToString();
    return sec;
  }

  /** Class templates by instantiation: `template<typename T> class Box { .. };` is taken out, and each `Box<int>` the program names becomes the plain
      class `Box_int` (cpprust's name for it), written where the template was, in the order the program first uses them. */
  void ExpandTemplates(List<Tok> toks)
  {
    var templates = new Dictionary<string, TemplateInfo>();
    for (int p = 0; p < toks.Count; p++) {
      if (toks[p].Kind != K.Ident || toks[p].S != "template") continue;
      int lt = NextSig(toks, p + 1);
      if (lt < 0 || !Is(toks[lt], "<")) continue;
      int gt = lt + 1;
      while (gt < toks.Count && !Is(toks[gt], ">")) gt++;
      int cl = gt < toks.Count ? NextSig(toks, gt + 1) : -1;
      if (cl < 0 || !(Is(toks[cl], "class") || Is(toks[cl], "struct"))) continue;                  // a function template: not lowered here
      int nm = NextSig(toks, cl + 1), ob = nm < 0 ? -1 : NextSig(toks, nm + 1);
      if (ob < 0 || !Is(toks[ob], "{")) continue;
      int end = MatchBrace(toks, ob);
      int semi = NextSig(toks, end + 1);
      if (semi >= 0 && Is(toks[semi], ";")) end = semi;
      var ti = new TemplateInfo { Name = toks[nm].S, Body = toks.GetRange(cl, end - cl + 1), Slot = new Tok { Kind = K.Ws, S = "" } };
      for (int k = lt + 1; k < gt; k++) if (toks[k].Kind == K.Ident && toks[k].S != "typename" && toks[k].S != "class") ti.Params.Add(toks[k].S);
      templates[ti.Name] = ti;
      toks.RemoveRange(p, end - p + 1);
      toks.Insert(p, ti.Slot);
    }
    if (templates.Count > 0) InstantiateClassTemplates(toks, templates);
    ExpandMemberTemplates(toks);
  }

  void InstantiateClassTemplates(List<Tok> toks, Dictionary<string, TemplateInfo> templates)
  {
    bool changed = true;
    while (changed) {
      changed = false;
      for (int k = 0; k < toks.Count; k++) {
        if (toks[k].Kind != K.Ident || !templates.TryGetValue(toks[k].S, out var ti)) continue;
        int lt = NextSig(toks, k + 1);
        if (lt < 0 || !Is(toks[lt], "<")) continue;
        int gt = lt + 1;
        while (gt < toks.Count && !Is(toks[gt], ">") && !Is(toks[gt], "<")) gt++;
        if (gt >= toks.Count || Is(toks[gt], "<")) continue;                                          // a template argument that is itself one: the inner goes first
        var args = new List<List<Tok>>();
        var cur = new List<Tok>();
        for (int a = lt + 1; a < gt; a++) {
          if (Is(toks[a], ",")) { args.Add(cur); cur = new List<Tok>(); } else if (IsSig(toks[a])) cur.Add(toks[a]);
        }
        args.Add(cur);
        if (args.Count != ti.Params.Count) throw new LowerUnsupported("`" + ti.Name + "<..>` with " + args.Count + " arguments");
        var texts = args.Select(a => string.Join(" ", a.Select(x => x.S))).ToList();
        string mangled = ti.Name + "_" + string.Join("_", texts.Select(x => Regex.Replace(x.Replace("*", "P").Replace(" ", "_"), "_+", "_")));
        Tok useTok = toks[k], endTok = toks[gt];
        if (ti.Done.Add(mangled)) {
          instances.Add(mangled);
          var body = new List<Tok>();
          for (int i = 0; i < ti.Body.Count; i++) {
            var tk = ti.Body[i];
            int pi = tk.Kind == K.Ident ? ti.Params.IndexOf(tk.S) : -1;
            if (pi >= 0) {
              for (int q = 0; q < args[pi].Count; q++) {
                if (q > 0) body.Add(new Tok { Kind = K.Ws, S = " " });
                body.Add(new Tok { Kind = args[pi][q].Kind, S = args[pi][q].S });
              }
              continue;
            }
            if (tk.Kind == K.Ident && tk.S == ti.Name) {
              int nx = NextSig(ti.Body, i + 1);
              if (nx < 0 || !Is(ti.Body[nx], "<")) { body.Add(new Tok { Kind = K.Ident, S = mangled }); continue; }
            }
            body.Add(new Tok { Kind = tk.Kind, S = tk.S });
          }
          toks.InsertRange(toks.IndexOf(ti.Slot), body);
        }
        // (the instance went in ahead of the slot, which is after every use but possibly before this one: indexes shift, so start over)
        int at = toks.IndexOf(useTok), atEnd = toks.IndexOf(endTok);
        toks.RemoveRange(at, atEnd - at + 1);
        toks.Insert(at, new Tok { Kind = K.Ident, S = mangled });
        changed = true;
        break;
      }
    }
  }

  class MemberTemplate
  {
    public string Name;
    public List<string> Params = new List<string>();
    public List<Tok> Body;              //from after `template<..>` through the member's closing `}` / `;`
    public int ParamIndex = -1;         //the parameter whose type is `T *` / `T`: where a call without explicit arguments deduces T from
    public Tok Slot;
    public HashSet<string> Done = new HashSet<string>();
  }

  static string Mangle(string text) { return Regex.Replace(text.Replace("*", "P").Replace(" ", "_"), "_+", "_"); }

  /** Member templates (`template <class T> T * Get() { .. }` in a class) by instantiation: each call `Get<Wheel>(..)` (or `Get(w)`, with T deduced
      from a pointer argument) names `Get_Wheel`, a plain member written where the template was.  cpprust does the same: `Car_Get_Wheel`. */
  void ExpandMemberTemplates(List<Tok> toks)
  {
    var mts = new List<MemberTemplate>();
    for (int p = 0; p < toks.Count; p++) {
      if (toks[p].Kind != K.Ident || toks[p].S != "template") continue;
      int lt = NextSig(toks, p + 1);
      if (lt < 0 || !Is(toks[lt], "<")) continue;
      int gt = lt + 1;
      while (gt < toks.Count && !Is(toks[gt], ">")) gt++;
      int open = -1;
      for (int k = gt + 1; k < toks.Count; k++) if (Is(toks[k], "(")) { open = k; break; }
      if (open < 0) continue;
      int nm = open - 1;
      while (nm > gt && !IsSig(toks[nm])) nm--;
      int close = MatchParen(toks, open);
      int b = NextSig(toks, close + 1);
      while (b >= 0 && Is(toks[b], "const")) b = NextSig(toks, b + 1);
      if (b < 0) continue;
      int end = Is(toks[b], "{") ? MatchBrace(toks, b) : b;
      var mt = new MemberTemplate { Name = toks[nm].S, Body = toks.GetRange(gt + 1, end - gt), Slot = new Tok { Kind = K.Ws, S = "" } };
      for (int k = lt + 1; k < gt; k++) if (toks[k].Kind == K.Ident && toks[k].S != "typename" && toks[k].S != "class") mt.Params.Add(toks[k].S);
      // the parameter T is deduced from: the first whose type is `T *` or `T`
      int bo = mt.Body.IndexOf(mt.Body.First(x => Is(x, "("))), pi = 0;
      var cur = new List<string>();
      for (int k = bo + 1, d = 0; k < mt.Body.Count; k++) {
        var x = mt.Body[k];
        if (Is(x, "(")) d++;
        if (d == 0 && (Is(x, ",") || Is(x, ")"))) {
          if (mt.ParamIndex < 0 && mt.Params.Count == 1 && cur.Count >= 1 && cur[0] == mt.Params[0] && (cur.Count == 2 || (cur.Count == 3 && cur[1] == "*"))) mt.ParamIndex = pi;
          pi++; cur.Clear();
          if (Is(x, ")")) break;
          continue;
        }
        if (Is(x, ")")) d--;
        if (IsSig(x)) cur.Add(x.S);
      }
      mts.Add(mt);
      toks.RemoveRange(p, end - p + 1);
      toks.Insert(p, mt.Slot);
    }
    if (mts.Count == 0) return;
    var names = new HashSet<string>(mts.Select(x => x.Name));
    bool changed = true;
    while (changed) {
      changed = false;
      for (int k = 0; k < toks.Count && !changed; k++) {
        if (toks[k].Kind != K.Ident || !names.Contains(toks[k].S)) continue;
        int pk = k - 1;
        while (pk >= 0 && !IsSig(toks[pk])) pk--;
        int lt = NextSig(toks, k + 1);
        if (lt < 0) continue;
        var cands = mts.Where(x => x.Name == toks[k].S).ToList();
        string argText;
        int useEnd;
        if (Is(toks[lt], "<")) {                                                          // Name<Arg>(
          int gt = lt + 1;
          while (gt < toks.Count && !Is(toks[gt], ">")) gt++;
          int op = NextSig(toks, gt + 1);
          if (op < 0 || !Is(toks[op], "(")) continue;
          var args = toks.GetRange(lt + 1, gt - lt - 1).Where(IsSig).Select(x => x.S);
          argText = string.Join(" ", args);
          useEnd = gt;
        } else if (Is(toks[lt], "(") && !(pk >= 0 && (Is(toks[pk], "template") || toks[pk].Kind == K.Ident && !Is(toks[pk], "return") && !Is(toks[pk], "else") && false))) {   // Name(args): deduce
          var tm = cands.FirstOrDefault(x => x.ParamIndex >= 0);
          if (tm == null) continue;
          int close = MatchParen(toks, lt);
          argText = DeduceArg(toks, lt + 1, close, tm.ParamIndex, k);
          if (argText == null) continue;
          useEnd = k;
        } else continue;
        // instantiate every template of that name (arity overloads), then name the call
        string inst = toks[k].S + "_" + Mangle(argText);
        Tok useTok = toks[k], endTok = toks[useEnd];
        bool explicitArgs = Is(toks[lt], "<");
        foreach (var tm in cands) {
          if (!tm.Done.Add(inst)) continue;
          var body = new List<Tok>();
          bool renamed = false;
          for (int i = 0; i < tm.Body.Count; i++) {
            var tk = tm.Body[i];
            if (tk.Kind == K.Ident && tm.Params.Count == 1 && tk.S == tm.Params[0]) { foreach (var a in Lex(argText)) body.Add(a); continue; }
            if (tk.Kind == K.Ident && !renamed && tk.S == tm.Name && NextSig(tm.Body, i + 1) >= 0 && Is(tm.Body[NextSig(tm.Body, i + 1)], "(")) { body.Add(new Tok { Kind = K.Ident, S = inst }); renamed = true; continue; }
            body.Add(new Tok { Kind = tk.Kind, S = tk.S });
          }
          toks.InsertRange(toks.IndexOf(tm.Slot), body);
          changed = true;
        }
        if (explicitArgs) {                                                                 // Name<Arg>  ->  Name_Arg
          int at = toks.IndexOf(useTok), atEnd = toks.IndexOf(endTok);
          toks.RemoveRange(at, atEnd - at + 1);
          toks.Insert(at, new Tok { Kind = K.Ident, S = inst });
          changed = true;
        } else { useTok.S = inst; changed = true; }
      }
    }
  }

  /** The type a call's argument number `idx` has when it says so on its face: `new X(..)`, `(X *)e`, or a name declared as `X * name` earlier. */
  string DeduceArg(List<Tok> toks, int a, int b, int idx, int before)
  {
    var parts = new List<List<Tok>>();
    var cur = new List<Tok>();
    for (int i = a, d = 0; i < b; i++) {
      if (Is(toks[i], "(") || Is(toks[i], "[")) d++; else if (Is(toks[i], ")") || Is(toks[i], "]")) d--;
      if (d == 0 && Is(toks[i], ",")) { parts.Add(cur); cur = new List<Tok>(); continue; }
      if (IsSig(toks[i])) cur.Add(toks[i]);
    }
    parts.Add(cur);
    if (idx >= parts.Count) return null;
    var arg = parts[idx];
    if (arg.Count >= 2 && Is(arg[0], "new") && arg[1].Kind == K.Ident) return arg[1].S;
    int pc = 0;
    while (pc < arg.Count && Is(arg[pc], "(")) pc++;                                          // ((X *)(e))
    if (pc > 0 && pc + 2 < arg.Count && arg[pc].Kind == K.Ident && Is(arg[pc + 1], "*") && Is(arg[pc + 2], ")")) return arg[pc].S;
    if (arg.Count == 1 && arg[0].Kind == K.Ident) {
      for (int i = before - 1; i >= 2; i--)
        if (toks[i].Kind == K.Ident && toks[i].S == arg[0].S) {
          int q = i - 1;
          while (q >= 0 && !IsSig(toks[q])) q--;
          if (q >= 0 && Is(toks[q], "*")) {
            int q2 = q - 1;
            while (q2 >= 0 && !IsSig(toks[q2])) q2--;
            if (q2 >= 0 && toks[q2].Kind == K.Ident && !PrimWords.Contains(toks[q2].S)) return toks[q2].S;
          }
        }
    }
    return null;
  }

  class TemplateInfo
  {
    public string Name;
    public List<string> Params = new List<string>();
    public List<Tok> Body;
    public Tok Slot;
    public HashSet<string> Done = new HashSet<string>();
  }

  /** `#include "Type.cpp"` pieces are spliced in place, as cpprust does; every other include stays where it is. */
  List<Tok> Expand(List<Tok> toks, string dir, int depth)
  {
    if (depth > 32) throw new LowerUnsupported("includes nest too deep");
    var r = new List<Tok>();
    foreach (var t in toks) {
      if (t.Kind == K.Pre) {
        var m = Regex.Match(t.S, "^#\\s*include\\s*\"([^\"]+\\.cpp)\"");
        if (m.Success) {
          string f = Path.Combine(dir, m.Groups[1].Value);
          if (!File.Exists(f)) throw new LowerUnsupported("cannot read " + f);
          r.AddRange(Expand(Lex(File.ReadAllText(f)), Path.GetDirectoryName(f), depth + 1));
          continue;
        }
        if (Regex.IsMatch(t.S, "^#\\s*include\\s*\"")) throw new LowerUnsupported("a program that needs a library header yet: " + t.S);
      }
      r.Add(t);
    }
    return r;
  }

  // ---- types

  /** The type that starts at t[p]: [const] primitive words | a class name | std::vector<...>, then `*`s.  Returns its end (exclusive), or -1. */
  int TypeEnd(List<Tok> t, int p)
  {
    int q = NextSig(t, p);
    if (q < 0 || t[q].Kind != K.Ident) return -1;
    if (PrimWords.Contains(t[q].S)) {
      bool onlyConst = t[q].S == "const";
      q++;
      while (true) {
        int r = NextSig(t, q);
        if (r >= 0 && t[r].Kind == K.Ident && PrimWords.Contains(t[r].S)) { onlyConst = onlyConst && t[r].S == "const"; q = r + 1; } else break;
      }
      if (onlyConst) {                                                              // const fastring
        int r = NextSig(t, q);
        if (r >= 0 && t[r].Kind == K.Ident && IsTypeName(t[r].S)) q = r + 1;
      }
    } else if (t[q].S == "std") {
      int r = NextSig(t, q + 1);
      if (r < 0 || !Is(t[r], "::")) return -1;
      r = NextSig(t, r + 1);
      if (r < 0 || (!Is(t[r], "vector") && !Is(t[r], "map"))) return -1;
      r = NextSig(t, r + 1);
      if (r < 0 || !Is(t[r], "<")) return -1;
      int depth = 1;
      r++;
      while (r < t.Count && depth > 0) { if (Is(t[r], "<")) depth++; else if (Is(t[r], ">")) depth--; r++; }
      q = r;
    } else if (IsTypeName(t[q].S)) {
      q++;
    } else {
      int r1 = NextSig(t, q + 1);
      int r2 = r1 >= 0 && Is(t[r1], "::") ? NextSig(t, r1 + 1) : -1;
      if (r2 < 0 || t[r2].Kind != K.Ident || !IsTypeName(t[q].S + "_" + t[r2].S)) return -1;
      q = r2 + 1;
    }
    while (true) {
      int r = NextSig(t, q);
      if (r >= 0 && Is(t[r], "*")) q = r + 1; else break;
    }
    return q;
  }

  /** A class of the program, or (phase 2) a library type; in phase 1 any name: only the shape of the members is read then. */
  bool IsTypeName(string n)
  {
    return classes.ContainsKey(n) || declared.Contains(n) || (foreign != null ? foreign.Types.Contains(n) : true);
  }

  Local MakeLocal(string raw, bool isPtr)
  {
    string ty = LowerType(raw, out string cls, out string vec);
    string bare = Regex.Replace(ty, "^const\\s+", "");
    string fg = cls == null && vec == null && foreign != null && foreign.Types.Contains(bare) ? bare : null;
    var pm = Regex.Match(ty, "^(\\w+) ?\\*$");
    if (cls == null && vec == null && pm.Success && classes.ContainsKey(pm.Groups[1].Value))                         // Node *: a reference to an arena object
      return new Local { Type = ty, Class = pm.Groups[1].Value, IsPtr = true, Pointer = true, PtrRef = isPtr };
    string elem = null;
    if (vec != null) { var em = Regex.Match(raw, "^\\s*std\\s*::\\s*vector\\s*<\\s*(\\w+)\\s*>\\s*$"); if (em.Success && (classes.ContainsKey(em.Groups[1].Value) || (foreign != null && foreign.Types.Contains(em.Groups[1].Value)))) elem = em.Groups[1].Value; }
    return new Local { Type = ty, Class = cls, Vector = vec, Foreign = fg, IsPtr = isPtr, Elem = elem };
  }

  /** The C spelling of a type written in the subset: std::vector<const char *> is the instance cpprust names vector_const_char_P. */
  string LowerType(string raw, out string cls, out string vec)
  {
    cls = null; vec = null;
    string s = Regex.Replace(raw.Trim(), "\\s+", " ");
    if (!s.StartsWith("std")) s = Regex.Replace(s, "\\s*::\\s*", "_");
    var m = Regex.Match(s, "^std\\s*::\\s*vector\\s*<(.*)>$");
    if (m.Success) {
      string arg = m.Groups[1].Value.Trim();
      string ea = Regex.Replace(arg, "\\s+", " ");
      var pm = Regex.Match(ea, "^(\\w+) \\*$");
      bool ptrElem = pm.Success && classes.ContainsKey(pm.Groups[1].Value);
      bool classElem = classes.ContainsKey(ea) || (foreign != null && foreign.Types.Contains(ea));
      if (ea != "const char *" && !ptrElem && !classElem && !(IsPrimitive(ea) && !ea.Contains("const"))) throw new LowerUnsupported("`" + s + "`: instantiating the standard containers is not lowered here yet");
      vec = "vector_" + Regex.Replace(arg.Replace("*", "P").Replace(" ", "_"), "_+", "_");
      return vec;
    }
    var mm = Regex.Match(s, "^std\\s*::\\s*map\\s*<\\s*([^,<>]+?)\\s*,\\s*([^,<>]+?)\\s*>$");   // an ordered map of primitives: cpprust names it map_K_V
    if (mm.Success) {
      string k = mm.Groups[1].Value, v = mm.Groups[2].Value;
      if (!IsPrimitive(k) || !IsPrimitive(v) || k.Contains("*") || v.Contains("*") || k.Contains("const") || v.Contains("const")) throw new LowerUnsupported("`" + s + "`: instantiating the standard containers is not lowered here yet");
      vec = "map_" + k.Replace(" ", "_") + "_" + v.Replace(" ", "_");
      return vec;
    }
    if (classes.ContainsKey(s)) cls = s;
    return s;
  }

  static bool IsPrimitive(string type)
  {
    var ws = type.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    return ws.Length > 0 && ws.All(w => PrimWords.Contains(w) || w == "*");
  }

  // ---- classes

  // ---- inheritance: a base is the first member (`_base`), the root of a class with virtual methods holds the vtable pointer, every
  // class has a vtable struct with the slots of all the virtuals up its chain, and a thunk per method that gives the slot a root `this`.

  ClassInfo BaseOf(ClassInfo ci) { return ci.Base == null ? null : classes[ci.Base]; }
  ClassInfo RootOf(ClassInfo ci) { while (ci.Base != null) ci = classes[ci.Base]; return ci; }
  bool HasVtable(ClassInfo ci) { for (var c = ci; c != null; c = BaseOf(c)) if (c.Methods.Any(m => m.Virtual)) return true; return false; }
  bool IsInterface(ClassInfo ci) { return ci.Base == null && !ci.Fields.Any() && !ci.Ctors.Any() && ci.Methods.Any() && ci.Methods.All(m => m.Pure); }

  void PrepareInheritance(ClassInfo ci)
  {
    if (ci.Base != null) {
      if (!classes.ContainsKey(ci.Base)) throw new LowerUnsupported("class " + ci.Name + ": base " + ci.Base + " is not a program class");
      foreach (var m in ci.Methods.Where(x => !x.Virtual && !x.Static))
        for (var b = BaseOf(ci); b != null; b = BaseOf(b))
          if (b.Methods.Any(x => x.Virtual && x.Name == m.Name && x.Params.Count == m.Params.Count)) { m.Virtual = true; break; }
    }
    if (!ci.Ctors.Any() && (ci.Base != null || HasVtable(ci)) && !IsInterface(ci))
      ci.Members.Add(new Member { IsMethod = true, IsCtor = true, Synthesized = true, Name = ci.Name });
  }

  List<KeyValuePair<Member, ClassInfo>> SlotsOf(ClassInfo ci)
  {
    var list = ci.Base == null ? new List<KeyValuePair<Member, ClassInfo>>() : SlotsOf(classes[ci.Base]);
    foreach (var m in ci.Methods.Where(x => x.Virtual))
      if (!list.Any(x => x.Key.Name == m.Name && x.Key.Params.Count == m.Params.Count)) list.Add(new KeyValuePair<Member, ClassInfo>(m, ci));
    return list;
  }

  static string SlotName(Member m) { return m.Name + "_" + m.Params.Count; }

  /** The nearest definition of a slot's method at or above ci, or null (pure). */
  KeyValuePair<Member, ClassInfo>? ImplOf(ClassInfo ci, Member slot)
  {
    for (var c = ci; c != null; c = BaseOf(c)) {
      var m = c.Methods.FirstOrDefault(x => x.Virtual && !x.Pure && x.Name == slot.Name && x.Params.Count == slot.Params.Count);
      if (m != null) return new KeyValuePair<Member, ClassInfo>(m, c);
    }
    return null;
  }

  /** The path to the field `name` as seen from ci (`_base._base.`), "" if ci has it, null if no class up the chain does. */
  string FieldPath(ClassInfo ci, string name)
  {
    string path = "";
    for (var c = ci; c != null; c = BaseOf(c)) {
      if (c.Fields.Any(f => f.Name == name)) return path;
      path += "_base.";
    }
    return null;
  }

  /** The method `name` taking argc arguments, looked up from ci up through its bases: the method and the class that has it. */
  Member FindMethod(ClassInfo ci, string name, int argc, out ClassInfo owner)
  {
    for (var c = ci; c != null; c = BaseOf(c)) {
      var m = PickMethod(c, name, argc);
      if (m != null) { owner = c; return m; }
    }
    owner = null;
    return null;
  }

  /** A call of m (found in `owner`) on the object at pointer `recv` whose static class is `cls`: through the vtable if m is virtual. */
  string CallMethod(ClassInfo cls, Member m, ClassInfo owner, string recv, string args)
  {
    string a = args.Length > 0 ? ", " + args.TrimStart() : "";
    if (m.Virtual) {
      if (Regex.IsMatch(recv, "\\w\\s*\\(")) {                                  // a call result: dispatch through the helper that evaluates it once
        var decl = SlotsOf(cls).First(x => x.Key.Name == m.Name && x.Key.Params.Count == m.Params.Count).Value;
        return decl.Name + "__vcall_" + m.Name + "(" + (decl == cls ? recv : "(" + decl.Name + " *)(" + recv + ")") + a + ")";
      }
      var root = RootOf(cls).Name;
      return "((const struct " + cls.Name + "_vtable *)((" + root + " *)(" + recv + "))->_vptr)->" + SlotName(m) + "((" + root + " *)(" + recv + ")" + a + ")";
    }
    return FuncName(owner, m) + "(" + (owner == cls ? recv : "(" + owner.Name + " *)(" + recv + ")") + a + ")";
  }

  /** The object a path like `p`, `this->f`, `a->b.c` names (a local, or a field of this, then fields), or null. */
  Local ResolvePath(string path, Ctx cx)
  {
    var parts = Regex.Split(path.Trim(), "\\s*(?:\\.|->)\\s*");
    Local cur = null;
    if (parts[0] == "this") {
      if (cx.Cls == null || cx.IsStatic || parts.Length < 2) return null;
      var fld = FieldOfChain(cx.Cls, parts[1]);
      if (fld == null) return null;
      cur = MakeLocal(fld.Type, false);
      parts = parts.Skip(2).ToArray();
    } else {
      cur = cx.Find(parts[0]);
      if (cur == null && cx.Cls != null && !cx.IsStatic) {
        var fld = FieldOfChain(cx.Cls, parts[0]);
        if (fld != null) cur = MakeLocal(fld.Type, false);
      }
      if (cur == null && cx.Cls != null) {                                        // a static of this class (or of a base)
        for (var c = cx.Cls; c != null && cur == null; c = BaseOf(c)) {
          var st = c.Statics.FirstOrDefault(x => x.Name == parts[0]);
          if (st != null) cur = MakeLocal(st.Type, false);
        }
      }
      parts = parts.Skip(1).ToArray();
    }
    foreach (var f in parts) {
      if (cur == null || cur.Class == null) return null;
      var fld = FieldOfChain(classes[cur.Class], f);
      if (fld == null) return null;
      cur = MakeLocal(fld.Type, false);
    }
    return cur;
  }

  Member FieldOfChain(ClassInfo ci, string name)
  {
    for (var c = ci; c != null; c = BaseOf(c)) { var f = c.Fields.FirstOrDefault(x => x.Name == name); if (f != null) return f; }
    return null;
  }

  static int ArgCountText(string callText)
  {
    var tk = Lex(callText);
    int op = tk.FindIndex(x => Is(x, "("));
    return op < 0 ? 0 : ArgCount(tk, op + 1, MatchParen(tk, op));
  }

  /** `.M(..)` / `->M(..)` on the result of a call of `via`: a pointer result is the receiver itself; a class result goes in by value (the `__byval_` variant). */
  int Chain(List<Tok> t, int after, int b, string callText, Member via, Ctx cx, StringBuilder sb)
  {
    string rt = via.Ret.Trim();
    var pm = Regex.Match(rt, "^(\\w+)\\s*\\*$");
    if (pm.Success && classes.ContainsKey(pm.Groups[1].Value))
      return ObjectUse(t, after, b, new Local { Type = pm.Groups[1].Value + " *", Class = pm.Groups[1].Value, IsPtr = true, Pointer = true }, callText, cx, sb, callText);
    if (classes.ContainsKey(rt)) {
      int n = NextSig(t, after), mi = NextSig(t, n + 1), op = mi < 0 ? -1 : NextSig(t, mi + 1);
      if (mi >= 0 && (op < 0 || op >= b || !Is(t[op], "("))) {                                         // f(..).field: a member of the returned struct
        var rcl = classes[rt];
        var fm = FieldOfChain(rcl, t[mi].S);
        if (fm == null) throw new LowerUnsupported("a field of a call result");
        string ftext = callText + "." + FieldPath(rcl, t[mi].S) + t[mi].S;
        if (IsPrimitive(fm.Type)) { sb.Append(ftext); return mi + 1; }
        return ObjectUse(t, mi + 1, b, MakeLocal(fm.Type, false), ftext, cx, sb, ftext);
      }
      if (op < 0 || op >= b || !Is(t[op], "(")) throw new LowerUnsupported("a field of a call result");
      int close = MatchParen(t, op);
      var rc = classes[rt];
      var m2 = FindMethod(rc, t[mi].S, ArgCount(t, op + 1, close), out var o2);
      if (m2 == null || m2.Static || m2.Virtual || ClassHasDrop(o2)) throw new LowerUnsupported("`" + t[mi].S + "` on a call result");
      string a2 = Args(t, op + 1, close, cx, m2.Params);
      string next = ByValName(o2, m2) + "(" + callText + (a2.Length > 0 ? ", " + a2.TrimStart() : "") + ")";
      int nx = NextSig(t, close + 1);
      if (nx >= 0 && nx < b && (Is(t[nx], ".") || Is(t[nx], "->"))) return Chain(t, close + 1, b, next, m2, cx, sb);
      sb.Append(next);
      return close + 1;
    }
    throw new LowerUnsupported("a call on the result of `" + via.Name + "` returning " + rt);
  }

  string ByValName(ClassInfo ci, Member m) { return ci.Name + "__byval_" + m.Name + "_" + m.Params.Count; }

  /** What a constructor does first when the class has a base or virtual methods: run the base's constructor, set the vtable pointer. */
  string BasePre(ClassInfo ci, Member m, Ctx cx)
  {
    var sb = new StringBuilder();
    if (ci.Base != null) {
      var bc = classes[ci.Base];
      var bt = m.BaseCall;
      int argc = bt == null ? 0 : ArgCount(bt, 1, bt.Count - 1);
      if (bc.Ctors.Any()) {
        var pick = bc.Ctors.FirstOrDefault(c => c.Params.Count == argc);
        if (pick == null) throw new LowerUnsupported("class " + ci.Name + ": no constructor of " + bc.Name + " takes " + argc + " arguments");
        string a = argc == 0 ? "" : Args(bt, 1, bt.Count - 1, cx ?? new Ctx { Cls = ci, IsStatic = false }, pick.Params);
        sb.Append(FuncName(bc, pick)).Append("(&this->_base").Append(a.Length > 0 ? ", " + a.TrimStart() : "").Append("); ");
      } else if (argc > 0) throw new LowerUnsupported("class " + ci.Name + ": arguments for a base without constructors");
    }
    if (HasVtable(ci)) { string root = RootOf(ci).Name; sb.Append("((" + root + " *)this)->_vptr = (const struct " + root + "_vtable *)&" + ci.Name + "__vtable; "); }
    return sb.ToString();
  }

  ClassInfo ParseClass(List<Tok> t, ref int p)
  {
    p = NextSig(t, p + 1);
    var ci = new ClassInfo { Name = t[p].S };
    p = NextSig(t, p + 1);
    if (Is(t[p], ":")) {
      int q = NextSig(t, p + 1);
      if (q >= 0 && Is(t[q], "public")) q = NextSig(t, q + 1);
      if (q < 0 || t[q].Kind != K.Ident) throw new LowerUnsupported("class " + ci.Name + ": a base clause that is not `: public Base`");
      ci.Base = t[q].S;
      p = NextSig(t, q + 1);
      if (p >= 0 && Is(t[p], ",")) throw new LowerUnsupported("class " + ci.Name + ": more than one base class");
    }
    if (p < 0 || !Is(t[p], "{")) throw new LowerUnsupported("class " + ci.Name + ": a forward declaration");
    p++;
    while (true) {
      p = NextSig(t, p);
      if (p < 0) throw new LowerUnsupported("class " + ci.Name + ": no closing brace");
      if (Is(t[p], "}")) { p++; break; }
      if (Is(t[p], "public") || Is(t[p], "private") || Is(t[p], "protected")) throw new LowerUnsupported("class " + ci.Name + ": access specifiers");
      ParseMember(t, ref p, ci);
    }
    int semi = NextSig(t, p);
    if (semi >= 0 && Is(t[semi], ";")) p = semi + 1;
    if (!ci.Ctors.Any() && ci.Fields.Any(f => f.Init != null || !IsPrimitive(f.Type)))
      ci.Members.Add(new Member { IsMethod = true, IsCtor = true, Synthesized = true, Name = ci.Name });     // (cpprust writes it after the other members)
    return ci;
  }

  void ParseMember(List<Tok> t, ref int p, ClassInfo ci)
  {
    bool isStatic = false, isVirtual = false;
    if (Is(t[p], "static")) { isStatic = true; p = NextSig(t, p + 1); }
    if (Is(t[p], "virtual")) { isVirtual = true; p = NextSig(t, p + 1); }
    foreach (var bad in new[] { "template", "operator", "~", "enum", "typedef", "using" })
      if (Is(t[p], bad)) throw new LowerUnsupported("class " + ci.Name + ": `" + bad + "` member");
    int n1 = NextSig(t, p + 1);
    if (t[p].Kind == K.Ident && t[p].S == ci.Name && n1 >= 0 && Is(t[n1], "(")) {          // a constructor
      var m = new Member { IsMethod = true, IsCtor = true, Name = ci.Name };
      int close = MatchParen(t, n1);
      m.Params = ParseParams(t, n1 + 1, close);
      int b = NextSig(t, close + 1);
      if (b >= 0 && Is(t[b], ":")) {                                                       // : Base(args)
        int bn = NextSig(t, b + 1), bo = bn < 0 ? -1 : NextSig(t, bn + 1);
        if (bn < 0 || bo < 0 || !Is(t[bo], "(") || ci.Base == null || t[bn].S != ci.Base) throw new LowerUnsupported("class " + ci.Name + ": an initialiser list that is not `: Base(args)`");
        int bc = MatchParen(t, bo);
        m.BaseCall = t.GetRange(bo, bc - bo + 1);
        b = NextSig(t, bc + 1);
      }
      if (b < 0 || !Is(t[b], "{")) throw new LowerUnsupported("class " + ci.Name + ": a constructor without a body, or with an initialiser list");
      int e = MatchBrace(t, b);
      m.Body = t.GetRange(b, e - b + 1);
      p = e + 1;
      ci.Members.Add(m);
      return;
    }
    int te = TypeEnd(t, p);
    if (te < 0) throw new LowerUnsupported("class " + ci.Name + ": a member whose type is not understood yet: " + Join(t, p, Math.Min(t.Count, p + 6)).Trim());
    string rawType = Join(t, p, te).Trim();
    int nameAt = NextSig(t, te);
    if (nameAt < 0 || t[nameAt].Kind != K.Ident) throw new LowerUnsupported("class " + ci.Name + ": a member without a name");
    string name = t[nameAt].S;
    int after = NextSig(t, nameAt + 1);
    if (after < 0) throw new LowerUnsupported("class " + ci.Name + ": truncated member " + name);
    if (Is(t[after], "(")) {                                                                    // a method
      var m = new Member { IsMethod = true, Name = name, Static = isStatic, Ret = rawType };
      int close = MatchParen(t, after);
      m.Params = ParseParams(t, after + 1, close);
      int b = NextSig(t, close + 1);
      if (b >= 0 && Is(t[b], "const")) throw new LowerUnsupported("class " + ci.Name + ": a const method");
      if (b >= 0 && Is(t[b], "=") && isVirtual) {                                            // virtual M() = 0;
        int z = NextSig(t, b + 1), sc = z < 0 ? -1 : NextSig(t, z + 1);
        if (z < 0 || t[z].S != "0" || sc < 0 || !Is(t[sc], ";")) throw new LowerUnsupported("class " + ci.Name + ": method " + name + ": `= ...` that is not `= 0`");
        m.Virtual = true; m.Pure = true;
        p = sc + 1;
        ci.Members.Add(m);
        return;
      }
      if (b < 0 || !Is(t[b], "{")) throw new LowerUnsupported("class " + ci.Name + ": method " + name + " without a body");
      m.Virtual = isVirtual;
      int e = MatchBrace(t, b);
      m.Body = t.GetRange(b, e - b + 1);
      p = e + 1;
      if (ci.Methods.Any(x => x.Name == name && x.Params.Count == m.Params.Count)) throw new LowerUnsupported("class " + ci.Name + ": two overloads of " + name + " with " + m.Params.Count + " parameters");
      ci.Members.Add(m);
      return;
    }
    var f = new Member { IsMethod = false, Name = name, Type = rawType, Static = isStatic };
    if (!IsPrimitive(f.Type) && !Regex.IsMatch(f.Type, "^(?:std\\s*::\\s*(?:vector|map)\\s*<[^<>]*>|[A-Za-z_][\\w:]*)(?:\\s*\\*)?$")) throw new LowerUnsupported("class " + ci.Name + ": field " + name + " of type `" + f.Type + "`");
    if (Is(t[after], "=")) {
      int semi = StmtEnd(t, after + 1);
      f.Init = Join(t, after + 1, semi).Trim();
      p = semi + 1;
    } else if (Is(t[after], ";")) {
      p = after + 1;
    } else throw new LowerUnsupported("class " + ci.Name + ": field " + name + ": " + t[after].S);
    ci.Members.Add(f);
  }

  List<Param> ParseParams(List<Tok> t, int a, int b)
  {
    var r = new List<Param>();
    var parts = new List<(int, int)>();
    int start = a, depth = 0;
    for (int i = a; i <= b; i++) {
      if (i == b || (depth == 0 && Is(t[i], ","))) {
        if (Join(t, start, i).Trim().Length > 0) parts.Add((start, i));
        start = i + 1;
      } else if (Is(t[i], "<") || Is(t[i], "(")) depth++;
      else if (Is(t[i], ">") || Is(t[i], ")")) depth--;
    }
    foreach (var (s, e) in parts) {
      string text = Join(t, s, e).Trim();
      if (text.Contains("=")) throw new LowerUnsupported("default arguments");
      bool isRef = text.Contains("&");
      text = text.Replace("&", " ").Trim();
      int sp = text.LastIndexOfAny(new[] { ' ', '*', '\t', '\n' });
      if (sp < 0) throw new LowerUnsupported("a parameter without a name: " + text);
      r.Add(new Param { Type = text.Substring(0, sp + 1).Trim(), Name = text.Substring(sp + 1), IsRef = isRef });
    }
    return r;
  }

  string LowerParam(Param p)
  {
    string ty = LowerType(p.Type, out _, out _);
    return p.IsRef ? (ty.EndsWith("*") ? ty + " (*" + p.Name + ")" : ty + " *" + p.Name) : ty + " " + p.Name;
  }

  /** `T_new` for the only constructor and for the no-argument one; with several, the others are `T_new_<argument count>` (cpprust resolves by count). */
  /** A field that owns something: a vector, or a library object with a destructor.  Anything else that is not a primitive is refused. */
  bool Owning(ClassInfo ci, Member f)
  {
    if (IsPrimitive(f.Type)) return false;
    var l = MakeLocal(f.Type, false);
    if (l.Pointer) return false;
    if (l.Vector != null) return true;
    if (l.Foreign != null && foreign.HasDrop(l.Foreign)) return true;
    if (l.Class != null && classes.ContainsKey(l.Class)) return ClassHasDrop(classes[l.Class]);          // a class held by value: it owns what that class owns
    throw new LowerUnsupported("class " + ci.Name + ": field " + f.Name + " of type `" + f.Type + "`");
  }

  /** A member that is a value of a class or library type (not a number, not a pointer): the class constructs it, and cpprust writes the class a member-wise copy. */
  bool IsValueMember(Member f)
  {
    if (IsPrimitive(f.Type)) return false;
    return !MakeLocal(f.Type, false).Pointer;
  }

  /** The drop function of a member's type: a vector instance's, a library type's, or a program class's. */
  static string DropFn(Local l) { return (l.Vector ?? l.Class ?? l.Foreign) + "_drop"; }

  /** cpprust's std::map instance has no copy function, so a class holding one gets none either. */
  bool HasMapField(ClassInfo ci) { return ci.Fields.Any(f => !IsPrimitive(f.Type) && (MakeLocal(f.Type, false).Vector ?? "").StartsWith("map_")); }

  bool ClassHasCopy(ClassInfo ci) { return !HasMapField(ci) && ci.Fields.Any(f => Owning(ci, f) || IsValueMember(f)) || (ci.Base != null && ClassHasCopy(classes[ci.Base])); }

  /** An arena class: `[MaxInstances(N)]` in C#, a `static const int __max_instances` here.  A reference to it is a pointer into N static slots. */
  bool IsArena(ClassInfo ci) { return ci.Statics.Any(x => x.Name == "__max_instances"); }

  string AllocName(ClassInfo ci, Member ctor) { return FuncName(ci, ctor).Replace("_new", "__alloc"); }

  bool ClassHasDrop(ClassInfo ci) { return ci.Fields.Any(f => Owning(ci, f)) || (ci.Base != null && ClassHasDrop(classes[ci.Base])); }

  string FieldType(Member f) { return IsPrimitive(f.Type) ? f.Type : MakeLocal(f.Type, false).Type; }

  string FuncName(ClassInfo ci, Member m)
  {
    if (!m.IsCtor) return ci.Name + "_" + m.Name + (ci.Methods.Count(x => x.Name == m.Name) > 1 ? "_" + m.Params.Count : "");     // cpprust: overloads are told apart by argument count
    var ctors = ci.Ctors.ToList();
    if (ctors.Count(c => c.Params.Count == m.Params.Count) > 1) throw new LowerUnsupported("class " + ci.Name + ": two constructors with " + m.Params.Count + " parameters");
    return ci.Name + (ctors.Count == 1 || m.Params.Count == 0 ? "_new" : "_new_" + m.Params.Count);
  }

  Member PickMethod(ClassInfo ci, string name, int argc)
  {
    var all = ci.Methods.Where(x => x.Name == name).ToList();
    if (all.Count <= 1) return all.FirstOrDefault();
    return all.FirstOrDefault(x => x.Params.Count == argc);
  }

  string Signature(ClassInfo ci, Member m)
  {
    var ps = new List<string>();
    if (!m.Static) ps.Add(ci.Name + " *this");
    ps.AddRange(m.Params.Select(LowerParam));
    string ret = m.IsCtor ? "void" : LowerType(m.Ret, out _, out _);
    string name = FuncName(ci, m);
    return "static " + ret + " " + name + "(" + (ps.Count == 0 ? "void" : string.Join(", ", ps)) + ")";
  }

  Dictionary<string, Local> ParamScope(Member m)
  {
    var scope = new Dictionary<string, Local>();
    foreach (var pr in m.Params) {
      var l = MakeLocal(pr.Type, pr.IsRef);
      if (!pr.IsRef && l.Foreign != null) l.Droppable = foreign.HasDrop(l.Foreign);      // a by-value library object is the callee's to destroy
      scope[pr.Name] = l;
    }
    return scope;
  }

  /** One class as C, in the two places cpprust puts it: `head` is the static fields and the struct (cpprust writes every class's struct
      first, then the library instances' bodies the program reaches); the return value is the functions (written after those bodies). */
  string EmitClass(ClassInfo ci, out string head)
  {
    var hb = new StringBuilder();
    var sb = new StringBuilder();
    var fields = ci.Fields.ToList();
    // (a class that holds a vector of pointers to itself needs that instance's struct ahead of its own: where cpprust writes it then is not pinned down)
    bool selfVec = ptrVecClasses.Contains(ci.Name) && fields.Any(f => Regex.IsMatch(f.Type, "std\\s*::\\s*vector\\s*<\\s*" + Regex.Escape(ci.Name) + "\\s*\\*\\s*>"));
    if (selfVec) { selfHolders = true; hb.Append("/*CCS-VEC-STRUCT:" + ci.Name + "_P*/\n"); }      // its instance's struct goes ahead of the class's own (cpprust's place for it is not pinned down: such programs compare by behaviour)
    foreach (var st in ci.Statics) hb.Append("static ").Append(FieldType(st)).Append(' ').Append(ci.Name).Append('_').Append(st.Name).Append(st.Init != null ? " = " + st.Init : "").Append(";\n");
    var owning = fields.Where(f => Owning(ci, f)).ToList();
    foreach (var f in fields.Where(f => f.Init != null && !IsPrimitive(f.Type) && !f.Type.TrimEnd().EndsWith("*"))) throw new LowerUnsupported("class " + ci.Name + ": field " + f.Name + " with an initialiser");
    var layout = new List<string>();
    if (HasVtable(ci)) {
      var slots = SlotsOf(ci);
      string root = RootOf(ci).Name;
      hb.Append("struct " + ci.Name + "_vtable { " + (rtti ? "const struct _CppTypeInfo *base; " : "") + (slots.Count == 0 ? (rtti ? "" : "char _cpp_empty;") : string.Join(" ", slots.Select(x => LowerType(x.Key.Ret, out _, out _) + " (*" + SlotName(x.Key) + ")(" + root + " *this" + string.Concat(x.Key.Params.Select(q => ", " + LowerParam(q))) + ");"))) + " };\n");
      if (!IsInterface(ci)) hb.Append("static const struct " + ci.Name + "_vtable " + ci.Name + "__vtable;\n");
      if (ci.Base == null) layout.Add("const struct " + ci.Name + "_vtable *_vptr;");
    }
    if (ci.Base != null) layout.Add(ci.Base + " _base;");
    layout.AddRange(fields.Select(f => FieldType(f) + " " + f.Name + ";"));
    if (instances.Contains(ci.Name)) hb.Append(' ');
    if (layout.Count == 0) hb.Append("struct " + ci.Name + " { char _cpp_empty; };\n");
    else hb.Append("struct " + ci.Name + " { " + string.Join(" ", layout) + " };\n");
    foreach (var suffix in new[] { "", "_P" })                                                           // cpprust writes the struct of std::vector<Cls> (and of std::vector<Cls *>) right after the class's
      if ((suffix == "" ? vecClasses : ptrVecClasses).Contains(ci.Name) && !(selfVec && suffix == "_P")) hb.Append("/*CCS-VEC-STRUCT:" + ci.Name + suffix + "*/\n");
    head = hb.ToString();
    // owned fields are constructed first, then the field initialisers run (in every constructor)
    string news = string.Concat(fields.Where(IsValueMember).Select(f => {
      var l = MakeLocal(f.Type, false);
      if (l.Class == null) return (l.Vector ?? l.Foreign) + "_new(&this->" + f.Name + "); ";
      var mc = classes[l.Class];                                                  // a class held by value: its default constructor, if it has constructors
      var dc = mc.Ctors.FirstOrDefault(c => c.Params.Count == 0);
      if (dc == null && mc.Ctors.Any()) throw new LowerUnsupported("class " + ci.Name + ": member " + f.Name + " of class " + mc.Name + ", which has no default constructor");
      return dc == null ? "" : FuncName(mc, dc) + "(&this->" + f.Name + "); ";
    }));
    string inits = string.Concat(fields.Where(f => f.Init != null).Select(f => "this->" + f.Name + " = " + Regex.Replace(f.Init, "\\bNULL\\b", "((void *)0)") + "; "));
    if (HasVtable(ci)) {
      string root = RootOf(ci).Name;
      foreach (var m in ci.Methods.Where(x => x.Virtual && !x.Pure)) {
        string rt = LowerType(m.Ret, out _, out _);
        sb.Append("static " + rt + " " + ci.Name + "__thunk_" + SlotName(m) + "(" + root + " *this" + string.Concat(m.Params.Select(q => ", " + LowerParam(q))) + ") { " + (rt == "void" ? "" : "return ") + FuncName(ci, m) + "((" + ci.Name + " *)this" + string.Concat(m.Params.Select(q => ", " + q.Name)) + "); }\n");
      }
      if (!IsInterface(ci)) sb.Append("static const struct " + ci.Name + "_vtable " + ci.Name + "__vtable = { " + (rtti ? (ci.Base != null && HasVtable(classes[ci.Base]) && !IsInterface(classes[ci.Base]) ? "(const struct _CppTypeInfo *)&" + ci.Base + "__vtable" : "0") + (SlotsOf(ci).Count > 0 ? ", " : "") : "") + (SlotsOf(ci).Count == 0 ? (rtti ? "" : "0") : string.Join(", ", SlotsOf(ci).Select(x => { var im = ImplOf(ci, x.Key); return im == null ? "0" : "&" + im.Value.Value.Name + "__thunk_" + SlotName(x.Key); }))) + " };\n");
    }
    foreach (var m in ci.Members.Where(x => x.IsMethod && !x.Pure)) {
      string lead = news + (inits.Length > 0 ? " " + inits.TrimEnd() : "");
      if (m.Synthesized) {
        string pre0 = BasePre(ci, m, null);
        sb.Append(Signature(ci, m)).Append(" { ").Append(pre0).Append(inits.Length > 0 ? news + ' ' + inits.TrimEnd() : (pre0.Length > 0 && news.Length == 0 ? "" : news.TrimEnd() + ' ')).Append("}\n");
        continue;
      }
      var cx = new Ctx { Cls = ci, IsStatic = m.Static, Ret = m.IsCtor ? "void" : LowerType(m.Ret, out _, out _) };
      cx.Scopes.Add(ParamScope(m));
      if (m.IsCtor) lead = BasePre(ci, m, cx) + lead;
      int bp = 0;
      sb.Append(Signature(ci, m)).Append(' ').Append(Block(m.Body, ref bp, cx, "", true, m.IsCtor ? lead : null)).Append('\n');
    }
    bool baseCopy = ci.Base != null && ClassHasCopy(classes[ci.Base]), baseDrop = ci.Base != null && ClassHasDrop(classes[ci.Base]);
    if (fields.Any(IsValueMember) || baseCopy) {
      string c = ci.Name, drops = string.Join(" ", Enumerable.Reverse(owning).Select(f => { var l = MakeLocal(f.Type, false); return DropFn(l) + "(&this->" + f.Name + ");"; }).Concat(baseDrop ? new[] { ci.Base + "_drop(&this->_base);" } : new string[0]));
      string copies = (baseCopy ? ci.Base + "_copy(&this->_base, &o->_base); " : "") + string.Join(" ", fields.Select(f => {
        if (!IsValueMember(f)) return "this->" + f.Name + " = o->" + f.Name + ";";
        var l = MakeLocal(f.Type, false);
        if (l.Class != null && !ClassHasCopy(classes[l.Class])) return "(this->" + f.Name + ") = (*(&o->" + f.Name + "));";       // plain data: assignment is the copy
        return (l.Vector ?? l.Class ?? l.Foreign) + "_copy(&this->" + f.Name + ", &o->" + f.Name + ");";
      }));
      if (owning.Count > 0 || baseDrop) sb.Append("static void " + c + "_drop(" + c + " *this) { " + drops + " }\n");
      if (!HasMapField(ci)) {
        sb.Append("static void " + c + "_copy(" + c + " *this, const " + c + " *o) { " + copies + "}\n");
        sb.Append("static void " + c + "__assign(" + c + " *this, const " + c + " *o) { if (this != o) {" + (drops.Length > 0 ? " " + drops : "") + " " + copies + " }}\n");
      }
    }
    if (IsArena(ci)) {
      string dropAll = ClassHasDrop(ci) ? ci.Name + "_drop(&" + ci.Name + "__arena[k]); " : "";
      string dropOne = ClassHasDrop(ci) ? ci.Name + "_drop(p); " : "";
      var maxField = ci.Statics.First(x => x.Name == "__max_instances");
      string n = maxField.Init, c0 = ci.Name;
      // the slots: a stack of those given back (`Arena.Free`) is taken before the arena grows
      sb.Append("static " + c0 + " " + c0 + "__arena[" + n + "]; static int " + c0 + "__arena_n;\n");
      sb.Append("static int " + c0 + "__free_list[" + n + "]; static int " + c0 + "__free_n;\n");
      sb.Append("static void " + c0 + "__arena_reset(void) { int k; for (k = 0; k < " + c0 + "__arena_n; k = k + 1) { " + dropAll + "} " + c0 + "__arena_n = 0; " + c0 + "__free_n = 0; }\n");
      sb.Append("static void " + c0 + "__free_slot(" + c0 + " *p) { " + dropOne + "memset(p, 0, sizeof(" + c0 + ")); " + c0 + "__free_list[" + c0 + "__free_n] = (int)(p - " + c0 + "__arena); " + c0 + "__free_n = " + c0 + "__free_n + 1; }\n");
      foreach (var ct in ci.Ctors)
        sb.Append("static " + c0 + " *" + AllocName(ci, ct) + "(" + (ct.Params.Count == 0 ? "void" : string.Join(", ", ct.Params.Select(LowerParam))) + ") { " + c0 + " *__p; if (" + c0 + "__free_n > 0) { " + c0 + "__free_n = " + c0 + "__free_n - 1; __p = &" + c0 + "__arena[" + c0 + "__free_list[" + c0 + "__free_n]]; } else { if (" + c0 + "__arena_n >= " + n + ") { abort(); } __p = &" + c0 + "__arena[" + c0 + "__arena_n]; " + c0 + "__arena_n = " + c0 + "__arena_n + 1; } memset(__p, 0, sizeof(" + c0 + ")); if (__p) { " + FuncName(ci, ct) + "(__p" + string.Concat(ct.Params.Select(q => ", " + q.Name)) + "); } return __p; }\n");
    }
    // cpprust gives a receiver-by-value variant (`Cls__byval_M_<n>`) to every non-virtual method of a class that owns nothing whose name the
    // program calls on a call result; they come after the class's own functions.
    if (!ClassHasDrop(ci))
      foreach (var m in ci.Methods.GroupBy(x => x.Name).SelectMany(g => g).Where(x => !x.Static && !x.Virtual && chained.Contains(x.Name))) {
        string rt = LowerType(m.Ret, out _, out _);
        string call = FuncName(ci, m) + "(&self" + string.Concat(m.Params.Select(q => ", " + q.Name)) + ")";
        sb.Append("static " + rt + " " + ByValName(ci, m) + "(" + ci.Name + " self" + string.Concat(m.Params.Select(q => ", " + LowerParam(q))) + ") { " + (rt == "void" ? call + ";" : rt + " _cpp_bv = " + call + "; return _cpp_bv;") + " }\n");
      }
    foreach (var sl in SlotsOf(ci).Where(x => x.Value == ci && chained.Contains(x.Key.Name))) {       // `Cls__vcall_M`: a virtual call on a call result
      var m = sl.Key;
      string rt = LowerType(m.Ret, out _, out _), root = RootOf(ci).Name;
      string call = "((const struct " + ci.Name + "_vtable *)((" + root + " *)this)->_vptr)->" + SlotName(m) + "((" + root + " *)this" + string.Concat(m.Params.Select(q => ", " + q.Name)) + ")";
      sb.Append("static " + rt + " " + ci.Name + "__vcall_" + m.Name + "(" + ci.Name + " *this" + string.Concat(m.Params.Select(q => ", " + LowerParam(q))) + ") { " + (rt == "void" ? call + ";" : rt + " _cpp_vr = " + call + "; return _cpp_vr;") + " }\n");
    }
    foreach (var suffix in new[] { "", "_P" })
      if ((suffix == "" ? vecClasses : ptrVecClasses).Contains(ci.Name)) sb.Append("/*CCS-VEC-DEFS:" + ci.Name + suffix + "*/\n");
    return sb.ToString();
  }

  // ---- enums

  /** `enum K_values { .. }; typedef int K;` at t[p]. */
  EnumItem ParseEnum(List<Tok> t, ref int p)
  {
    int start = p;
    int tag = NextSig(t, p + 1);
    if (tag < 0 || t[tag].Kind != K.Ident) throw new LowerUnsupported("an anonymous enum");
    int open = NextSig(t, tag + 1);
    if (open < 0 || !Is(t[open], "{")) throw new LowerUnsupported("`enum " + t[tag].S + "` that is not a definition");
    int close = MatchBrace(t, open);
    int semi = NextSig(t, close + 1);
    if (semi < 0 || !Is(t[semi], ";")) throw new LowerUnsupported("an enum that is not a plain `enum X { .. };`");
    int td = NextSig(t, semi + 1);
    if (td < 0 || !Is(t[td], "typedef")) throw new LowerUnsupported("`enum " + t[tag].S + "` without the typedef the back end writes after it");
    int tdEnd = StmtEnd(t, td);
    int alias = tdEnd - 1;
    while (alias > td && !IsSig(t[alias])) alias--;
    if (t[alias].Kind != K.Ident) throw new LowerUnsupported("the typedef after `enum " + t[tag].S + "` is not understood");
    var en = new EnumItem { Tag = t[tag].S, Hoisted = Join(t, start, semi + 1) };
    var tail = new StringBuilder();                                                   // the rest of the typedef's line
    for (int e = tdEnd + 1; e < t.Count && !IsSig(t[e]); e++) {
      int nl = t[e].S.IndexOf('\n');
      if (nl < 0) { tail.Append(t[e].S); continue; }
      tail.Append(t[e].S, 0, nl + 1);
      break;
    }
    en.InPlace = new string(' ', en.Hoisted.Length) + Join(t, semi + 1, tdEnd + 1) + tail;
    // cpprust spells the enum's type as the integer type the typedef names, wherever the program writes it
    string alias_ = t[alias].S, underlying = Join(t, td + 1, alias).Trim();
    if (!IsPrimitive(underlying)) throw new LowerUnsupported("`enum " + t[tag].S + "`: the typedef's type `" + underlying + "` is not an integer type");
    p = tdEnd + 1;
    for (int k = p; k < t.Count; k++)
      if (t[k].Kind == K.Ident && t[k].S == alias_) { var under = Lex(underlying); t.RemoveAt(k); t.InsertRange(k, under); k += under.Count - 1; }
    return en;
  }

  // ---- helper functions

  /** `static RET name(params) { ... }` between the types: a free function the C# compiler wrote (an array helper). */
  Member ParseFree(List<Tok> t, ref int p)
  {
    p = NextSig(t, p + 1);                                                       // past `static`
    int te = TypeEnd(t, p);
    if (te < 0) throw new LowerUnsupported("a helper function with a return type not understood yet: " + Join(t, p, Math.Min(t.Count, p + 6)).Trim());
    var m = new Member { IsMethod = true, Static = true, Ret = Join(t, p, te).Trim() };
    int nameAt = NextSig(t, te);
    int open = nameAt < 0 ? -1 : NextSig(t, nameAt + 1);
    if (open < 0 || t[nameAt].Kind != K.Ident || !Is(t[open], "(")) throw new LowerUnsupported("a helper that is not a function");
    m.Name = t[nameAt].S;
    int close = MatchParen(t, open);
    m.Params = ParseParams(t, open + 1, close);
    int b = NextSig(t, close + 1);
    if (b < 0 || !Is(t[b], "{")) throw new LowerUnsupported("helper " + m.Name + " without a body");
    int e = MatchBrace(t, b);
    m.Body = t.GetRange(b, e - b + 1);
    p = e + 1;
    return m;
  }

  string EmitFree(Member m)
  {
    var cx = new Ctx { Cls = null, IsStatic = true, Ret = LowerType(m.Ret, out _, out _) };
    cx.Scopes.Add(ParamScope(m));
    int bp = 0;
    string ps = m.Params.Count == 0 ? "void" : string.Join(", ", m.Params.Select(LowerParam));
    return "static " + cx.Ret + " " + m.Name + "(" + ps + ") " + Block(m.Body, ref bp, cx, "", true) + "\n";
  }

  // ---- main

  /** int main(int argc, char **argv) { ... }, which the back end writes itself. */
  string ParseMain(List<Tok> t, ref int p)
  {
    int start = p;
    int open = start;
    while (open < t.Count && !Is(t[open], "(")) open++;
    string head = Join(t, start, open).Trim();
    if (head != "int main") throw new LowerUnsupported("a free function other than main: " + head);
    int close = MatchParen(t, open);
    int b = NextSig(t, close + 1);
    int e = MatchBrace(t, b);
    var body = t.GetRange(b, e - b + 1);
    p = e + 1;
    for (int i = 0; i + 2 < body.Count; i++) {                                     // Entry::Main(...)
      if (Is(body[i], "::")) {
        int m = NextSig(body, i + 1);
        int c = i - 1;
        while (c >= 0 && !IsSig(body[c])) c--;
        if (m >= 0 && Is(body[m], "Main") && c >= 0) { entryClass = body[c].S; break; }
      }
    }
    if (entryClass == null || !classes.ContainsKey(entryClass)) throw new LowerUnsupported("main does not call a Main of the program's classes");
    if (foreign == null) return "";                                                // phase 1: no bodies
    var cx = new Ctx { Ret = "int" };
    cx.Scopes.Add(new Dictionary<string, Local> {
      ["argc"] = new Local { Type = "int" },
      ["argv"] = new Local { Type = "char **" },
    });
    int bp = 0;
    return Join(t, start, b) + Block(body, ref bp, cx, "") + "\n";
  }

  /** The aggregate with the program's types replaced by one slot class, and main calling it. */
  string programText;
  readonly HashSet<string> chained = new HashSet<string>();
  bool selfHolders;                                               //a class holds a vector of pointers to itself
  bool rtti;                                                      //the program uses dynamic_cast: every vtable starts with a pointer to its base's
  readonly HashSet<string> instances = new HashSet<string>();     //the classes made from templates (cpprust leaves a space where the template header was)
  readonly List<string> ptrVecClasses = new List<string>();  //program classes some std::vector<Cls *> holds (arena classes)
  readonly List<string> vecClasses = new List<string>();     //program classes some std::vector holds: cpprust writes the instance right after the class

  string Skeleton(string prefix, string mainRaw, bool boolText, bool boolHead)
  {
    var main = classes[entryClass].Methods.First(m => m.Name == "Main");
    string ps = string.Join(", ", main.Params.Select(x => x.Type + (x.IsRef ? " & " : " ") + x.Name));
    // cpprust adds <stdbool.h> when the text says bool/true/false, and again at the top when `bool` is in a prototype: the slot says the same
    string use = boolText ? "bool CcsB = false; " : "";
    string body = LowerType(main.Ret, out _, out _) == "void" ? "{ " + use + "}" : "{ " + use + "return 0; }";
    string probe = boolHead ? " static bool CcsProbe() { return true; }" : "";
    // the containers the program names that the surroundings do not: used here so cpprust instantiates them, in the order the program first names them
    var seen = new List<string>();
    foreach (Match vm in Regex.Matches(programText, "std\\s*::\\s*vector\\s*<([^<>]*)>")) {
      string ea = Regex.Replace(vm.Groups[1].Value.Trim(), "\\s+", " ");
      if (ea == "const char *" || seen.Contains(ea) || Regex.IsMatch(prefix, "std\\s*::\\s*vector\\s*<\\s*" + Regex.Escape(ea).Replace("\\ ", "\\s+") + "\\s*>")) continue;
      seen.Add(ea);
    }
    // a vector of a program class is stamped from a stand-in class of the same name and shape (owning or not): the back end moves the text to the class
    var stubs = new StringBuilder();
    var stubbed = new HashSet<string>();
    for (int k = 0; k < seen.Count; k++) {
      var em = Regex.Match(seen[k], "^(\\w+)( \\*)?$");
      if (!em.Success || !classes.TryGetValue(em.Groups[1].Value, out var ec)) continue;
      bool ptr = em.Groups[2].Success;
      if (stubbed.Add(ec.Name)) {
        bool own = ec.Fields.Any(f => !IsPrimitive(f.Type) && !Regex.IsMatch(f.Type, "\\*$"));
        stubs.Append("class CcsE_" + ec.Name + " { int v; " + (own ? "std::vector<int> a; " : "") + "};\n");
      }
      if (ptr) ptrVecClasses.Add(ec.Name); else vecClasses.Add(ec.Name);
      seen[k] = "CcsE_" + ec.Name + (ptr ? " *" : "");
    }
    var maps = new List<string>();                                                           // std::map<K, V> of primitives: the same, without a stand-in
    foreach (Match mm in Regex.Matches(programText, "std\\s*::\\s*map\\s*<\\s*([^,<>]+?)\\s*,\\s*([^,<>]+?)\\s*>")) {
      string mt = mm.Groups[1].Value.Trim() + ", " + mm.Groups[2].Value.Trim();
      if (maps.Contains(mt) || Regex.IsMatch(prefix, "std\\s*::\\s*map\\s*<\\s*" + Regex.Escape(mm.Groups[1].Value.Trim()) + "\\s*,\\s*" + Regex.Escape(mm.Groups[2].Value.Trim()) + "\\s*>")) continue;
      maps.Add(mt);
    }
    var useParams = seen.Select((e, i) => "std::vector<" + e + "> & u" + i).Concat(maps.Select((e, i) => "std::map<" + e + "> & m" + i)).ToList();
    string uses = useParams.Count == 0 ? "" : "static void CcsUse(" + string.Join(", ", useParams) + ") { } ";
    string slot = "class " + Slot + " { " + uses + "static " + main.Ret + " Main(" + ps + ") " + body + probe + " };\n";
    return prefix + stubs + slot + Regex.Replace(mainRaw, "\\b" + Regex.Escape(entryClass) + "\\s*::\\s*Main\\b", Slot + "::Main");
  }

  // ---- bodies

  /** `{ ... }` at body[p]: its statements lowered.  `inits` is text that goes first inside (a constructor's field initialisers). */
  bool lastReturn;
  Dictionary<string, Local> pendingScope;     //locals the next block starts out with (a range-for's variable)

  string Block(List<Tok> body, ref int p, Ctx cx, string inits, bool fn = false, string raw = null)
  {
    var sb = new StringBuilder();
    sb.Append('{');
    if (raw != null) sb.Append(raw);
    else if (inits.Length > 0) sb.Append(' ').Append(inits.TrimEnd());                  // right after the brace; what followed it stays as it was
    p++;
    cx.Scopes.Add(pendingScope ?? new Dictionary<string, Local>());
    pendingScope = null;
    while (true) {
      if (p >= body.Count) throw new LowerUnsupported("unbalanced brace in a body");
      var t = body[p];
      if (Is(t, "}")) break;
      if (!IsSig(t)) { sb.Append(t.S); p++; continue; }
      lastReturn = Is(t, "return");
      Stmt(body, ref p, cx, sb);
    }
    var ends = new List<string>();                                                  // what dies at the closing brace: this scope, and a function's parameters
    bool afterWs = true;
    for (int k = cx.Scopes.Count - 1; k >= (fn ? 0 : cx.Scopes.Count - 1); k--)
      foreach (var kv in Enumerable.Reverse(cx.Scopes[k].ToList()))
        if (kv.Value.Droppable) { afterWs &= kv.Value.DropAfterWs; ends.Add((kv.Value.Vector ?? kv.Value.Class ?? kv.Value.Foreign) + "_drop(&" + kv.Key + ");"); }
    if (ends.Count > 0 && !lastReturn) {
      int n = sb.Length;
      while (n > 0 && char.IsWhiteSpace(sb[n - 1])) n--;
      string ws = sb.ToString(n, sb.Length - n);
      sb.Length = n;
      if (afterWs) { sb.Append(ws); sb.Append(string.Join(" ", ends)).Append(' '); }
      else { foreach (var d in ends) sb.Append(' ').Append(d); sb.Append(ws); }
    }
    lastReturn = false;
    cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
    sb.Append('}');
    p++;
    return sb.ToString();
  }

  /** The white space and comments at t[p..], copied. */
  static void CopyWs(List<Tok> t, ref int p, StringBuilder sb)
  {
    while (p < t.Count && !IsSig(t[p])) { sb.Append(t[p].S); p++; }
  }

  void Stmt(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    var tok = t[p];
    if (Is(tok, "{")) { sb.Append(Block(t, ref p, cx, "")); return; }
    if (Is(tok, "return")) { Return(t, ref p, cx, sb); return; }
    if (Is(tok, "if")) { If(t, ref p, cx, sb); return; }
    if (Is(tok, "while")) { While(t, ref p, cx, sb); return; }
    if (Is(tok, "do")) { Do(t, ref p, cx, sb); return; }
    if (Is(tok, "switch")) { Switch(t, ref p, cx, sb); return; }
    if (Is(tok, "for")) { For(t, ref p, cx, sb); return; }
    if ((Is(tok, "break") || Is(tok, "continue")) && cx.Targets.Count > 0) {                       // leaving scopes that hold objects with a destructor
      int semi0 = NextSig(t, p + 1);
      int tgt = -1;
      for (int k = cx.Targets.Count - 1; k >= 0; k--) if (!Is(tok, "continue") || !cx.Targets[k].Value) { tgt = cx.Targets[k].Key; break; }
      var outs = new List<string>();
      if (tgt >= 0 && semi0 >= 0 && Is(t[semi0], ";"))
        for (int k = cx.Scopes.Count - 1; k >= tgt; k--)
          foreach (var kv in Enumerable.Reverse(cx.Scopes[k].ToList()))
            if (kv.Value.Droppable) outs.Add((kv.Value.Vector ?? kv.Value.Class ?? kv.Value.Foreign) + "_drop(&" + kv.Key + ");");
      if (outs.Count > 0) { sb.Append("{ ").Append(string.Join(" ", outs)).Append(' ').Append(tok.S).Append("; }"); p = semi0 + 1; return; }
    }
    foreach (var bad in new[] { "goto", "try", "throw", "else", "case", "default" })
      if (Is(tok, bad)) throw new LowerUnsupported("`" + bad + "` statement");
    if (tok.Kind == K.Ident && classes.ContainsKey(tok.S)) {                                  // Cls &r = expr;  a reference local is a pointer
      int amp = NextSig(t, p + 1), rn = amp < 0 ? -1 : NextSig(t, amp + 1), eq = rn < 0 ? -1 : NextSig(t, rn + 1);
      if (amp >= 0 && Is(t[amp], "&") && rn >= 0 && t[rn].Kind == K.Ident && eq >= 0 && Is(t[eq], "=")) {
        int rend = StmtEnd(t, eq);
        sb.Append(tok.S).Append(" *").Append(t[rn].S).Append(" = &(").Append(Expr(t, eq + 1, rend, cx).Trim()).Append(");");
        cx.Scopes[cx.Scopes.Count - 1][t[rn].S] = new Local { Type = tok.S + " *", Class = tok.S, IsPtr = true };
        p = rend + 1;
        return;
      }
    }
    int te = TypeEnd(t, p);
    if (te >= 0) {
      int nm = NextSig(t, te);
      if (nm >= 0 && t[nm].Kind == K.Ident) {
        int after = NextSig(t, nm + 1);
        if (after >= 0 && (Is(t[after], ";") || Is(t[after], "(") || Is(t[after], "="))) { Decl(t, ref p, cx, sb, te, nm, after); return; }
      }
    }
    int end = StmtEnd(t, p);                                                    // an expression statement
    int p0 = p;
    {                                                                            // x = std::vector<T>();  builds a temporary first, ahead of the line it is on
      var vm = Regex.Match(Join(t, p, end).Trim(), "^(\\w+)\\s*=\\s*std\\s*::\\s*vector\\s*<([^<>]*)>\\s*\\(\\s*\\)$");
      if (vm.Success) {
        string tn = "std::vector<" + vm.Groups[2].Value + ">";
        string vname = LowerType(tn, out _, out string vv);
        string tmp = "__cpp_tmp" + (tmpCounter++);
        int n = sb.Length;
        while (n > 0 && char.IsWhiteSpace(sb[n - 1])) n--;
        string ws = sb.ToString(n, sb.Length - n);
        sb.Length = n;
        sb.Append(vv + " " + tmp + "; " + vv + "_new(&" + tmp + "); ").Append(ws);
        cx.Scopes[cx.Scopes.Count - 1][tmp] = new Local { Type = vv, Vector = vv, Droppable = true, DropAfterWs = true };
        sb.Append(Expr(t, p, NextSig(t, p + 1), cx).Trim()).Append(" = ").Append(tmp).Append(';');
        p = end + 1;
        return;
      }
    }
    {                                                                            // lhs = Cls(args);  builds a temporary first, ahead of the line it is on
      int eq = -1;
      for (int k = p, d = 0; k < end; k++) {
        if (Is(t[k], "(") || Is(t[k], "[")) d++; else if (Is(t[k], ")") || Is(t[k], "]")) d--;
        else if (d == 0 && Is(t[k], "=")) { eq = k; break; }
      }
      int cq = eq < 0 ? -1 : NextSig(t, eq + 1), co = cq < 0 ? -1 : NextSig(t, cq + 1);
      if (cq >= 0 && t[cq].Kind == K.Ident && classes.TryGetValue(t[cq].S, out var tc) && co >= 0 && Is(t[co], "(") && NextSig(t, MatchParen(t, co) + 1) == end) {
        if (ClassHasCopy(tc)) throw new LowerUnsupported("assigning a temporary of class " + tc.Name + ", which owns resources");
        int argc = ArgCount(t, co + 1, MatchParen(t, co));
        var pick = tc.Ctors.FirstOrDefault(c => c.Params.Count == argc);
        if (pick == null) throw new LowerUnsupported("class " + tc.Name + " has no constructor taking " + argc + " arguments");
        string tmp = "__cpp_tmp" + (tmpCounter++);
        string args = Args(t, co + 1, MatchParen(t, co), cx, pick.Params);
        int n = sb.Length;
        while (n > 0 && char.IsWhiteSpace(sb[n - 1])) n--;
        string ws = sb.ToString(n, sb.Length - n);
        sb.Length = n;
        sb.Append(tc.Name + " " + tmp + "; " + FuncName(tc, pick) + "(&" + tmp + (args.Length > 0 ? ", " + args.TrimStart() : "") + "); ").Append(ws);
        sb.Append(Expr(t, p, eq, cx).Trim()).Append(" = ").Append(tmp).Append(';');
        p = end + 1;
        return;
      }
    }
    if (t[p].Kind == K.Ident && foreign != null) {                              // dst = src; on library objects: an assignment function
      int ls = p;
      if (Is(t[p], "this")) { int a1 = NextSig(t, p + 1), a2 = a1 < 0 ? -1 : NextSig(t, a1 + 1); if (a2 >= 0 && Is(t[a1], "->") && t[a2].Kind == K.Ident) ls = a2; }
      int eq = -1;
      for (int k = p, d = 0; k < end; k++) {
        if (Is(t[k], "(") || Is(t[k], "[")) d++; else if (Is(t[k], ")") || Is(t[k], "]")) d--;
        else if (d == 0 && Is(t[k], "=")) { eq = k; break; }
        else if (d == 0 && (Is(t[k], "==") || Is(t[k], "+=") || Is(t[k], "-="))) break;
      }
      int rs = eq < 0 ? -1 : NextSig(t, eq + 1);
      if (eq >= 0 && Is(t[eq], "=") && rs >= 0 && t[rs].Kind == K.Ident && NextSig(t, rs + 1) == end) {
        Local dl = ls == p ? cx.Find(t[ls].S) : null;
        if (dl == null && cx.Cls != null && !cx.IsStatic) {
          string fname = t[ls].S;
          var fld = cx.Cls.Fields.FirstOrDefault(f => f.Name == fname && !IsPrimitive(f.Type));
          if (fld != null) dl = MakeLocal(fld.Type, false);
        }
        if (dl == null) {                                                          // Cls::static, a static or an inherited member, a.b->c
          string ltext = Join(t, p, eq).Trim();
          var qm = Regex.Match(ltext, "^(\\w+)\\s*::\\s*(\\w+)$");
          if (qm.Success && classes.TryGetValue(qm.Groups[1].Value, out var qcl)) {
            var st = qcl.Statics.FirstOrDefault(f => f.Name == qm.Groups[2].Value);
            if (st != null) dl = MakeLocal(st.Type, false);
          } else dl = ResolvePath(ltext, cx);
        }
        var sl = cx.Find(t[rs].S);
        if (dl != null && dl.Foreign != null && foreign.HasDrop(dl.Foreign)) {
          if (sl == null || sl.Foreign != dl.Foreign) throw new LowerUnsupported("assigning `" + t[rs].S + "` to a library object");
          sb.Append(dl.Foreign).Append("__assign(&").Append(Expr(t, p, eq, cx).Trim()).Append(", ").Append(sl.IsPtr ? t[rs].S : "&" + t[rs].S).Append(");");
          p = end + 1;
          return;
        }
      }
    }
    sb.Append(Expr(t, p, end, cx)).Append(';');
    p = end + 1;
  }

  /** The statement after a condition: a block, or one statement. */
  void Body(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    CopyWs(t, ref p, sb);
    if (p >= t.Count) throw new LowerUnsupported("a statement is missing its body");
    cx.Scopes.Add(new Dictionary<string, Local>());
    Stmt(t, ref p, cx, sb);
    cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
  }

  void If(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int open = NextSig(t, p + 1);
    int close = MatchParen(t, open);
    sb.Append("if").Append(Join(t, p + 1, open)).Append('(').Append(Expr(t, open + 1, close, cx)).Append(')');
    p = close + 1;
    Body(t, ref p, cx, sb);
    int e = NextSig(t, p);
    if (e >= 0 && Is(t[e], "else")) {
      while (p < e) { sb.Append(t[p].S); p++; }
      sb.Append("else");
      p = e + 1;
      CopyWs(t, ref p, sb);
      if (p < t.Count && Is(t[p], "if")) If(t, ref p, cx, sb);
      else Body(t, ref p, cx, sb);
    }
  }

  void While(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int open = NextSig(t, p + 1);
    int close = MatchParen(t, open);
    sb.Append("while").Append(Join(t, p + 1, open)).Append('(').Append(Expr(t, open + 1, close, cx)).Append(')');
    p = close + 1;
    cx.Targets.Add(new KeyValuePair<int, bool>(cx.Scopes.Count, false));
    Body(t, ref p, cx, sb);
    cx.Targets.RemoveAt(cx.Targets.Count - 1);
  }

  /** do body while (cond); */
  void Do(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    sb.Append("do");
    p++;
    cx.Targets.Add(new KeyValuePair<int, bool>(cx.Scopes.Count, false));
    Body(t, ref p, cx, sb);
    cx.Targets.RemoveAt(cx.Targets.Count - 1);
    int w = NextSig(t, p);
    if (w < 0 || !Is(t[w], "while")) throw new LowerUnsupported("a `do` without its `while`");
    while (p < w) { sb.Append(t[p].S); p++; }
    int open = NextSig(t, w + 1);
    if (open < 0 || !Is(t[open], "(")) throw new LowerUnsupported("a `do .. while` without a condition");
    int close = MatchParen(t, open);
    int semi = NextSig(t, close + 1);
    if (semi < 0 || !Is(t[semi], ";")) throw new LowerUnsupported("a `do .. while (..)` without its `;`");
    sb.Append("while").Append(Join(t, w + 1, open)).Append('(').Append(Expr(t, open + 1, close, cx)).Append(')').Append(Join(t, close + 1, semi)).Append(';');
    p = semi + 1;
  }

  /** switch (e) { case A: .. default: .. }: the labels are copied as written, the statements between them are lowered. */
  void Switch(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int open = NextSig(t, p + 1);
    int close = MatchParen(t, open);
    sb.Append("switch").Append(Join(t, p + 1, open)).Append('(').Append(Expr(t, open + 1, close, cx)).Append(')');
    p = close + 1;
    CopyWs(t, ref p, sb);
    if (p >= t.Count || !Is(t[p], "{")) throw new LowerUnsupported("a `switch` without braces");
    sb.Append('{');
    p++;
    var scope = new Dictionary<string, Local>();
    cx.Targets.Add(new KeyValuePair<int, bool>(cx.Scopes.Count, true));
    cx.Scopes.Add(scope);
    while (true) {
      if (p >= t.Count) throw new LowerUnsupported("unbalanced brace in a switch");
      var tk = t[p];
      if (Is(tk, "}")) break;
      if (!IsSig(tk)) { sb.Append(tk.S); p++; continue; }
      if (Is(tk, "case") || Is(tk, "default")) {                                      // up to the label's colon (not the one of a `?:`)
        int q = p + 1, ternary = 0;
        for (; q < t.Count; q++) {
          if (Is(t[q], "?")) ternary++;
          else if (Is(t[q], ":")) { if (ternary == 0) break; ternary--; }
          else if (Is(t[q], "{") || Is(t[q], ";")) throw new LowerUnsupported("a `case` label that is not understood");
        }
        if (q >= t.Count) throw new LowerUnsupported("a `case` label without its colon");
        sb.Append(Join(t, p, q + 1));
        p = q + 1;
        continue;
      }
      Stmt(t, ref p, cx, sb);
    }
    if (scope.Values.Any(v => v.Droppable)) throw new LowerUnsupported("an object with a destructor declared directly in a `switch` body (not inside braces)");
    cx.Targets.RemoveAt(cx.Targets.Count - 1);
    cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
    sb.Append('}');
    p++;
  }

  void For(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int open = NextSig(t, p + 1);
    int close = MatchParen(t, open);
    int colon = -1;
    for (int k = open + 1, d = 0; k < close; k++) {
      if (Is(t[k], "(") || Is(t[k], "[") || Is(t[k], "<")) d++; else if (Is(t[k], ")") || Is(t[k], "]") || Is(t[k], ">")) d--;
      else if (Is(t[k], ";")) break;
      else if (d == 0 && Is(t[k], ":")) { colon = k; break; }
    }
    if (colon >= 0) { RangeFor(t, ref p, open, colon, close, cx, sb); return; }
    int s1 = StmtEnd(t, open + 1);
    int s2 = StmtEnd(t, s1 + 1);
    cx.Scopes.Add(new Dictionary<string, Local>());                            // the loop variable lives in the loop
    int te = TypeEnd(t, open + 1);
    string init;
    if (te >= 0 && te < s1 && IsPrimitive(Join(t, open + 1, te).Trim())) {      // for (int i = 0; ...)
      int nm = NextSig(t, te);
      cx.Scopes[cx.Scopes.Count - 1][t[nm].S] = new Local { Type = Join(t, open + 1, te).Trim() };
      init = Join(t, open + 1, nm) + t[nm].S + Expr(t, nm + 1, s1, cx);
    } else if (te >= 0 && te < s1 && MakeLocal(Join(t, open + 1, te).Trim(), false).Pointer) {      // for (Node * n = first; ...)
      int nm = NextSig(t, te);
      cx.Scopes[cx.Scopes.Count - 1][t[nm].S] = MakeLocal(Join(t, open + 1, te).Trim(), false);
      init = Join(t, open + 1, nm) + t[nm].S + Expr(t, nm + 1, s1, cx);
    } else init = Expr(t, open + 1, s1, cx);
    sb.Append("for").Append(Join(t, p + 1, open)).Append('(').Append(init).Append(';').Append(Expr(t, s1 + 1, s2, cx)).Append(';').Append(Expr(t, s2 + 1, close, cx)).Append(')');
    p = close + 1;
    cx.Targets.Add(new KeyValuePair<int, bool>(cx.Scopes.Count, false));
    Body(t, ref p, cx, sb);
    cx.Targets.RemoveAt(cx.Targets.Count - 1);
    cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
  }

  /** `&x` for an object of library type `fg` that `text` names (a local, a field of this, `this->f`), or null if it names something else. */
  string CopySource(string text, string fg, Ctx cx)
  {
    if (Regex.IsMatch(text, "^[A-Za-z_]\\w*$")) {
      var l = cx.Find(text);
      if (l != null) return l.Foreign == fg ? (l.IsPtr ? text : "&" + text) : null;
      if (cx.Cls != null && !cx.IsStatic) {
        var f = cx.Cls.Fields.FirstOrDefault(x => x.Name == text && !IsPrimitive(x.Type));
        if (f != null && MakeLocal(f.Type, false).Foreign == fg) return "&this->" + text;
      }
      return null;
    }
    var m = Regex.Match(text, "^this\\s*->\\s*(\\w+)$");
    if (m.Success && cx.Cls != null) {
      var f = cx.Cls.Fields.FirstOrDefault(x => x.Name == m.Groups[1].Value && !IsPrimitive(x.Type));
      if (f != null && MakeLocal(f.Type, false).Foreign == fg) return "&this->" + m.Groups[1].Value;
    }
    return null;
  }

  int itCounter;

  /** for (T v : vec) body  ->  an index loop over the vector, `v` copied out first thing in the body (as cpprust writes it). */
  void RangeFor(List<Tok> t, ref int p, int open, int colon, int close, Ctx cx, StringBuilder sb)
  {
    int te = TypeEnd(t, open + 1);
    int nm = te < 0 ? -1 : NextSig(t, te);
    bool byRef = false;
    if (nm >= 0 && nm < colon && Is(t[nm], "&")) { byRef = true; nm = NextSig(t, nm + 1); }
    string elemTy = te < 0 ? "" : Join(t, open + 1, te).Trim();
    bool elemClass = classes.ContainsKey(elemTy);
    bool elemLib = foreign != null && foreign.Types.Contains(elemTy) && !byRef && foreign.HasDrop(elemTy);
    if (te < 0 || nm < 0 || nm >= colon || !(IsPrimitive(elemTy) || (elemClass && byRef) || elemLib)) throw new LowerUnsupported("a range-for over something other than primitives, or objects by reference");
    string over = Join(t, colon + 1, close).Trim();
    var vl = cx.Find(over);
    string otext = over;
    if (vl == null && cx.Cls != null && !cx.IsStatic) {                         // a field of this
      var fld = cx.Cls.Fields.FirstOrDefault(f => f.Name == over && !IsPrimitive(f.Type));
      if (fld != null) { vl = MakeLocal(fld.Type, false); otext = "this->" + over; }
    }
    if (vl == null || vl.Vector == null) throw new LowerUnsupported("a range-for over `" + over + "`");
    string ty = Join(t, open + 1, te).Trim(), v = t[nm].S, it = "_cpp_it" + (itCounter++);
    string recv = vl.IsPtr ? otext : "&" + otext;
    sb.Append("for").Append(Join(t, p + 1, open)).Append("(int ").Append(it).Append(" = 0; ").Append(it).Append(" < ").Append(vl.Vector).Append("_size(").Append(recv).Append("); ")
      .Append(it).Append(" = ").Append(it).Append(" + 1)");
    p = close + 1;
    CopyWs(t, ref p, sb);
    if (p >= t.Count || !Is(t[p], "{")) throw new LowerUnsupported("a range-for without braces");
    string element = "(*" + vl.Vector + "__index(" + recv + ", " + it + "))";
    int bp = p;
    cx.Targets.Add(new KeyValuePair<int, bool>(cx.Scopes.Count, false));
    if (elemLib) {                                                                  // a library object by value: a copy, destroyed at the end of each pass
      pendingScope = new Dictionary<string, Local> { [v] = new Local { Type = ty, Foreign = ty, Droppable = true } };
      string cinit = ty + " " + v + "; " + ty + "_copy(&" + v + ", &(*" + vl.Vector + "__index(" + (vl.IsPtr ? "(" + otext + ")" : "&(" + otext + ")") + ", " + it + ")));";
      sb.Append(Block(t, ref bp, cx, cinit));
    } else {
      var scope = new Dictionary<string, Local> { [v] = elemClass ? new Local { Type = ty, Class = ty, Alias = element } : new Local { Type = ty } };
      cx.Scopes.Add(scope);
      string init = ty + " " + v + " = " + element + ";";
      sb.Append(Block(t, ref bp, cx, elemClass ? "" : init));
      cx.Scopes.RemoveAt(cx.Scopes.Count - 1);
    }
    cx.Targets.RemoveAt(cx.Targets.Count - 1);
    p = bp;
  }

  /** `T x;` or `T x(args);` of a program class (`after` is the `;` or the `(`): the declaration, then the constructor called on it. */
  void CtorDecl(List<Tok> t, int after, ClassInfo ci, string ty, string name, Ctx cx, StringBuilder sb)
  {
    int argc = Is(t[after], "(") ? ArgCount(t, after + 1, MatchParen(t, after)) : 0;
    var ctors = ci.Ctors.ToList();
    var pick = ctors.FirstOrDefault(c => c.Params.Count == argc);
    if (pick == null && (ctors.Count > 0 || argc > 0)) throw new LowerUnsupported("class " + ci.Name + " has no constructor taking " + argc + " arguments");
    sb.Append(ty).Append(' ').Append(name).Append(';');
    if (pick != null) {
      string args = Is(t[after], "(") ? Args(t, after + 1, MatchParen(t, after), cx, pick.Params) : "";
      sb.Append(' ').Append(FuncName(ci, pick)).Append("(&").Append(name);
      if (args.Length > 0) sb.Append(", ").Append(args.TrimStart());
      sb.Append(");");
    }
  }

  void Decl(List<Tok> t, ref int p, Ctx cx, StringBuilder sb, int te, int nm, int after)
  {
    string rawType = Join(t, p, te).Trim();
    string name = t[nm].S;
    var local = MakeLocal(rawType, false);
    string ty = local.Type, cls = local.Class, vec = local.Vector, fg = local.Foreign;
    int semi = StmtEnd(t, after);
    if (local.Pointer) {                                                           // Node * n = ...;
      sb.Append(rawType).Append(' ').Append(name);
      if (Is(t[after], "=")) sb.Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim());
      else if (!Is(t[after], ";")) throw new LowerUnsupported("a declaration of `" + name + "`");
      sb.Append(';');
      cx.Scopes[cx.Scopes.Count - 1][name] = local;
      p = semi + 1;
      return;
    }
    if (fg != null) {                                                              // a library type: fastring s; fastring s = f(..); fastring s(..);
      sb.Append(ty).Append(' ').Append(name);
      if (Is(t[after], "=")) {
        string rhs = Expr(t, after + 1, semi, cx).Trim();
        string rt = Join(t, after + 1, semi).Trim();
        var src = Regex.IsMatch(rt, "^[A-Za-z_]\\w*$") ? cx.Find(rt) : null;
        if (src != null && src.Foreign == fg && !src.IsPtr) {                                          // a copy of another object
          sb.Append(';').Append(' ').Append(fg).Append("_copy(&").Append(name).Append(", &").Append(rt).Append(");");
          local.Droppable = foreign.HasDrop(fg);
          cx.Scopes[cx.Scopes.Count - 1][name] = local;
          p = semi + 1;
          return;
        }
        if (Regex.IsMatch(rt, "^\\w+(?:\\s*(?:\\.|->)\\s*\\w+)+$")) {                                   // a copy of a field: fastring s = h.Log;
          var fl = ResolvePath(rt, cx);
          if (fl != null && fl.Foreign == fg && !fl.IsPtr) {
            sb.Append(';').Append(' ').Append(fg).Append("_copy(&").Append(name).Append(", &").Append(rhs).Append(");");
            local.Droppable = foreign.HasDrop(fg);
            cx.Scopes[cx.Scopes.Count - 1][name] = local;
            p = semi + 1;
            return;
          }
        }
        if (!Regex.IsMatch(Join(t, after + 1, semi), "^\\s*(?:[A-Za-z_][\\w:.>\\-]*\\s*\\(|\\(.*\\)\\s*(?:\\.|->)\\s*\\w+\\s*\\()")) throw new LowerUnsupported("`" + rawType + " " + name + " = ...`: initialising a library object from something other than a call");
        sb.Append(" = ").Append(rhs).Append(';');
      } else {
        sb.Append(';');
        int argc = Is(t[after], "(") ? ArgCount(t, after + 1, MatchParen(t, after)) : 0;
        string copyOf = argc == 1 ? CopySource(Join(t, after + 1, MatchParen(t, after)).Trim(), fg, cx) : null;
        if (copyOf != null) {                                                       // T x(other): a copy
          sb.Append(' ').Append(fg).Append("_copy(&").Append(name).Append(", ").Append(copyOf).Append(");");
          local.Droppable = foreign.HasDrop(fg);
          cx.Scopes[cx.Scopes.Count - 1][name] = local;
          p = semi + 1;
          return;
        }
        var ctors = foreign.Members(fg, "new");
        if (ctors.Count > 0 || argc > 0) {
          var pr = Pick(ctors, true, argc, fg, "new");
          string args = Is(t[after], "(") ? Args(t, after + 1, MatchParen(t, after), cx, foreign.Declared(pr, true)) : "";
          sb.Append(' ').Append(pr.Name).Append("(&").Append(name).Append(args.Length > 0 ? ", " + args.TrimStart() : "").Append(");");
        }
      }
      local.Droppable = foreign.HasDrop(fg);
    } else if (cls != null) {
      var ci = classes[cls];
      if (Is(t[after], "=")) {                                                      // Node n = f(..): a value from a call
        string rt = Join(t, after + 1, semi).Trim();
        int cq = NextSig(t, after + 1), co = cq < 0 ? -1 : NextSig(t, cq + 1);
        if (cq >= 0 && t[cq].Kind == K.Ident && t[cq].S == cls && co >= 0 && Is(t[co], "(") && NextSig(t, MatchParen(t, co) + 1) == semi) {      // V v = V(..);  as  V v(..);
          CtorDecl(t, co, ci, ty, name, cx, sb);
          local.Droppable = ClassHasDrop(ci);
          cx.Scopes[cx.Scopes.Count - 1][name] = local;
          p = semi + 1;
          return;
        }
        var ixm = Regex.Match(rt, "^([A-Za-z_]\\w*)\\s*\\[(.+)\\]$");
        var ixv = ixm.Success ? cx.Find(ixm.Groups[1].Value) : null;
        if (ixv != null && ixv.Vector != null && ixv.Elem == cls) {                                         // Vec e = l[i];  a copy of an element
          string recv = ixv.IsPtr ? "&((*" + ixm.Groups[1].Value + "))" : "&(" + ixm.Groups[1].Value + ")";
          var idxToks = Lex(ixm.Groups[2].Value);
          string element = "*" + ixv.Vector + "__index(" + recv + ", " + Expr(idxToks, 0, idxToks.Count, cx).Trim() + ")";
          if (!ClassHasCopy(ci)) sb.Append(ty).Append(' ').Append(name).Append("; ").Append(name).Append(" = (").Append(element).Append(");");     // plain data: declared, then assigned
          else sb.Append(ty).Append(' ').Append(name).Append("; ").Append(cls).Append("_copy(&").Append(name).Append(", &(").Append(element).Append("));");
          local.Droppable = ClassHasDrop(ci);
          cx.Scopes[cx.Scopes.Count - 1][name] = local;
          p = semi + 1;
          return;
        }
        var cpSrc = Regex.IsMatch(rt, "^[A-Za-z_]\\w*$") ? cx.Find(rt) : null;
        if (cpSrc != null && cpSrc.Class == cls && !cpSrc.IsPtr && !cpSrc.Pointer && !ClassHasCopy(ci)) {          // Vec b = a;  a plain struct copy: declared, then assigned
          sb.Append(ty).Append(' ').Append(name).Append("; ").Append(name).Append(" = ").Append(rt).Append(';');
          cx.Scopes[cx.Scopes.Count - 1][name] = local;
          p = semi + 1;
          return;
        }
        if (cpSrc == null && Regex.IsMatch(rt, "^[A-Za-z_]\\w*$") && cx.Cls != null && !cx.IsStatic) {            // Vec b = field;  a plain struct copy of a member
          var ff = FieldOfChain(cx.Cls, rt);
          if (ff != null && MakeLocal(ff.Type, false).Class == cls && !ClassHasCopy(ci)) {
            sb.Append(ty).Append(' ').Append(name).Append("; ").Append(name).Append(" = this->").Append(FieldPath(cx.Cls, rt)).Append(rt).Append(';');
            cx.Scopes[cx.Scopes.Count - 1][name] = local;
            p = semi + 1;
            return;
          }
        }
        var rm = Regex.Match(rt, "^([A-Za-z_][\\w]*(?:\\s*(?:\\.|->)\\s*[A-Za-z_]\\w*)*?)\\s*(?:\\.|->)\\s*([A-Za-z_]\\w*)\\s*\\(.*\\)$");         // Vec b = a.f.M(..);  a method returning the object by value
        var rl = rm.Success ? ResolvePath(rm.Groups[1].Value, cx) : null;
        Member rmeth = null;
        if (rl != null && rl.Class != null) rmeth = FindMethod(classes[rl.Class], rm.Groups[2].Value, ArgCountText(rt.Substring(rm.Groups[2].Index + rm.Groups[2].Length)), out _);
        if (rmeth != null && rmeth.Ret.Trim() == cls && !ClassHasCopy(ci)) {
          sb.Append(ty).Append(' ').Append(name).Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim()).Append(';');
          local.Droppable = ClassHasDrop(ci);
          cx.Scopes[cx.Scopes.Count - 1][name] = local;
          p = semi + 1;
          return;
        }
        if (!Regex.IsMatch(rt, "^[A-Za-z_][\\w:]*\\s*\\(") || classes.ContainsKey(Regex.Match(rt, "^[A-Za-z_]\\w*(?=\\s*\\()").Value)) throw new LowerUnsupported("`" + rawType + " " + name + " = ...`: initialising an object from something other than a call");
        sb.Append(ty).Append(' ').Append(name).Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim()).Append(';');
      } else CtorDecl(t, after, ci, ty, name, cx, sb);
      local.Droppable = ClassHasDrop(ci);
    } else if (vec != null) {
      if (Is(t[after], "=")) {                                                      // a vector made by a library or helper function
        string rt = Join(t, after + 1, semi).Trim();
        if (!Regex.IsMatch(rt, "^[A-Za-z_][\\w:]*\\s*\\(")) throw new LowerUnsupported("a `std::vector` initialised from something other than a call");
        sb.Append(ty).Append(' ').Append(name).Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim()).Append(';');
      } else if (Is(t[after], "(")) {                                                // std::vector<T> b(a);  a copy of another vector
        string src = Join(t, after + 1, MatchParen(t, after)).Trim();
        var sv = Regex.IsMatch(src, "^[A-Za-z_]\\w*$") ? cx.Find(src) : null;
        if (sv == null || sv.Vector != vec) throw new LowerUnsupported("a `std::vector` constructed from something other than a vector of the same type");
        sb.Append(ty).Append(' ').Append(name).Append("; ").Append(vec).Append("_copy(&").Append(name).Append(", ").Append(sv.IsPtr ? src : "&" + src).Append(");");
      } else if (!Is(t[after], ";")) throw new LowerUnsupported("a `std::vector` with an initialiser");
      else sb.Append(ty).Append(' ').Append(name).Append("; ").Append(vec).Append("_new(&").Append(name).Append(");");
      local.Droppable = true;
    } else {
      sb.Append(Join(t, p, nm).Trim()).Append(' ').Append(name);
      if (Is(t[after], "=")) sb.Append(" = ").Append(Expr(t, after + 1, semi, cx).Trim());
      else if (!Is(t[after], ";")) throw new LowerUnsupported("a declaration of `" + name + "`");
      sb.Append(';');
    }
    cx.Scopes[cx.Scopes.Count - 1][name] = local;
    p = semi + 1;
  }

  void Return(List<Tok> t, ref int p, Ctx cx, StringBuilder sb)
  {
    int semi = StmtEnd(t, p + 1);
    var cm = Regex.Match(Join(t, p + 1, semi).Trim(), "^([A-Za-z_]\\w*)\\s*\\(");
    if (cm.Success && classes.TryGetValue(cm.Groups[1].Value, out var rc)) {            // return Node(args);
      var rt = Join(t, p + 1, semi).Trim();
      int po = rt.IndexOf('(');
      var ct = Lex(rt);
      int open0 = ct.FindIndex(x => Is(x, "(")), close0 = MatchParen(ct, open0);
      int argc = ArgCount(ct, open0 + 1, close0);
      var pick = rc.Ctors.FirstOrDefault(c => c.Params.Count == argc);
      if (pick == null) throw new LowerUnsupported("class " + rc.Name + " has no constructor taking " + argc + " arguments");
      if (ClassHasDrop(rc)) throw new LowerUnsupported("class " + rc.Name + " owns resources: returned by a constructor call");
      if (cx.Scopes.Any(sc => sc.Values.Any(v => v.Droppable))) throw new LowerUnsupported("returning a constructor call with live objects to destroy");
      string tmp0 = "__cpp_ret" + (ctorRetCounter++);
      string a0 = Args(ct, open0 + 1, close0, cx, pick.Params);
      sb.Append("{ ").Append(rc.Name).Append(' ').Append(tmp0).Append("; ").Append(FuncName(rc, pick)).Append("(&").Append(tmp0).Append(a0.Length > 0 ? ", " + a0 : "").Append("); return ").Append(tmp0).Append("; }");
      p = semi + 1;
      return;
    }
    string expr = Expr(t, p + 1, semi, cx).Trim();
    var drops = new List<string>();
    bool ownedRet = (foreign != null && foreign.Types.Contains(cx.Ret) && foreign.HasDrop(cx.Ret)) || cx.Ret.StartsWith("vector_") || (classes.TryGetValue(cx.Ret, out var rcls) && ClassHasCopy(rcls));
    bool place = ownedRet && Regex.IsMatch(expr, "^[A-Za-z_]\\w*(?:\\s*(?:->|\\.)\\s*[A-Za-z_]\\w*)+$");           // returning a member: the caller gets a copy
    string copyOf = null;
    if (place) { copyOf = expr; expr = "__CCS_COPY__"; }
    var moved = Regex.IsMatch(expr, "^[A-Za-z_]\\w*$") ? expr : null;                      // returning a local moves it out
    for (int i = cx.Scopes.Count - 1; i >= 0; i--)
      foreach (var kv in Enumerable.Reverse(cx.Scopes[i].ToList()))
        if (kv.Value.Droppable && kv.Key != moved) drops.Add((kv.Value.Vector ?? kv.Value.Class ?? kv.Value.Foreign) + "_drop(&" + kv.Key + ");");
    bool movedOwned = moved != null && cx.Find(moved) != null && cx.Find(moved).Droppable;       // an owning local that is returned still goes through a temporary
    if (drops.Count == 0 && !movedOwned && copyOf == null) {
      sb.Append("return");
      if (expr.Length > 0) sb.Append(' ').Append(expr);
      sb.Append(';');
    } else if (expr.Length == 0) {
      sb.Append("{ ").Append(string.Join(" ", drops)).Append(" return; }");
    } else {
      // cpprust numbers these across the whole unit, after everything it lowered before the program: the caller (see direct_c in
      // crust/test_direct_c.py) turns this placeholder into the next free number.  Step 2, which lowers the whole unit, counts them itself.
      string tmp = "__CCS_RET" + (++retCounter) + "__";
      if (copyOf != null) sb.Append("{ ").Append(cx.Ret).Append(' ').Append(tmp).Append("; ").Append(cx.Ret).Append("_copy(&").Append(tmp).Append(", &").Append(copyOf).Append("); ").Append(drops.Count > 0 ? string.Join(" ", drops) + " " : "").Append("return ").Append(tmp).Append("; }");
      else sb.Append("{ ").Append(cx.Ret).Append(' ').Append(tmp).Append(" = (").Append(expr).Append("); ").Append(drops.Count > 0 ? string.Join(" ", drops) + " " : "").Append("return ").Append(tmp).Append("; }");
    }
    p = semi + 1;
  }

  // ---- expressions

  static bool PrevIsAccess(List<Tok> t, int a, int i)
  {
    int j = i - 1;
    while (j >= a && !IsSig(t[j])) j--;
    return j >= a && (Is(t[j], ".") || Is(t[j], "->") || Is(t[j], "::"));
  }

  /** Tokens [a, b) of an expression, lowered: `this->` on fields, method calls as functions, `Cls::M` as `Cls_M`, `&` where a callee takes a reference. */
  string Expr(List<Tok> t, int a, int b, Ctx cx)
  {
    var sb = new StringBuilder();
    int i = a;
    while (i < b) {
      var tok = t[i];
      if (tok.Kind != K.Ident) { sb.Append(tok.S); i++; continue; }
      string id = tok.S;
      int n1 = NextSig(t, i + 1);
      bool afterAccess = PrevIsAccess(t, a, i);
      if (n1 > b) n1 = -1;
      if (id == "dynamic_cast" && n1 >= 0 && Is(t[n1], "<")) {                                          // dynamic_cast<T *>(e): null unless e's object is a T
        int gt = n1 + 1;
        while (gt < b && !Is(t[gt], ">")) gt++;
        string target = Join(t, n1 + 1, gt).Trim();
        var tm = Regex.Match(target, "^(\\w+)\\s*\\*$");
        int op = NextSig(t, gt + 1);
        if (!tm.Success || !classes.ContainsKey(tm.Groups[1].Value) || !HasVtable(classes[tm.Groups[1].Value]) || op < 0 || !Is(t[op], "(")) throw new LowerUnsupported("`dynamic_cast<" + target + ">`");
        int close = MatchParen(t, op);
        sb.Append("((" + tm.Groups[1].Value + " *)_cpp_dyncast((void *)(" + Expr(t, op + 1, close, cx).Trim() + "), (const struct _CppTypeInfo *)&" + tm.Groups[1].Value + "__vtable))");
        i = close + 1;
        continue;
      }
      if (!afterAccess && classes.TryGetValue(id, out var qc) && n1 >= 0 && Is(t[n1], "::")) {            // Cls::M(args)
        int mi = NextSig(t, n1 + 1);
        int op = mi < 0 ? -1 : NextSig(t, mi + 1);
        if (mi >= 0 && qc.Statics.Any(x => x.Name == t[mi].S) && (op < 0 || op >= b || !Is(t[op], "("))) {
          var sf = qc.Statics.First(x => x.Name == t[mi].S);
          string gn = id + "_" + t[mi].S;
          if (!IsPrimitive(sf.Type)) { i = ObjectUse(t, mi + 1, b, MakeLocal(sf.Type, false), gn, cx, sb, gn); continue; }
          sb.Append(gn); i = mi + 1; continue;
        }
        if (mi < 0 || op < 0 || !Is(t[op], "(")) throw new LowerUnsupported(id + "::" + (mi >= 0 ? t[mi].S : "") + " without a call");
        var meth = PickMethod(qc, t[mi].S, ArgCount(t, op + 1, MatchParen(t, op)));
        if (meth == null) throw new LowerUnsupported(id + "::" + t[mi].S + ": no such method");
        if (!meth.Static) throw new LowerUnsupported(id + "::" + t[mi].S + ": a qualified call of an instance method");
        int close = MatchParen(t, op);
        string qcall = FuncName(qc, meth) + "(" + Args(t, op + 1, close, cx, meth.Params) + ")";
        int qn = NextSig(t, close + 1);
        if (qn >= 0 && qn < b && (Is(t[qn], ".") || Is(t[qn], "->"))) { i = Chain(t, close + 1, b, qcall, meth, cx, sb); continue; }
        sb.Append(qcall);
        i = close + 1;
        continue;
      }
      if (!afterAccess && foreign != null && cx.Find(id) == null && foreign.Types.Contains(id) && n1 >= 0 && Is(t[n1], "::")) {        // Type::M(args), a library type's
        int mi = NextSig(t, n1 + 1);
        int op = mi < 0 ? -1 : NextSig(t, mi + 1);
        if (mi < 0 || op < 0 || !Is(t[op], "(")) throw new LowerUnsupported(id + "::" + (mi >= 0 ? t[mi].S : "") + " without a call");
        int close = MatchParen(t, op);
        var pr = Pick(foreign.Members(id, t[mi].S), false, ArgCount(t, op + 1, close), id, t[mi].S);
        sb.Append(pr.Name).Append('(').Append(Args(t, op + 1, close, cx, foreign.Declared(pr, false))).Append(')');
        i = close + 1;
        continue;
      }
      var loc = afterAccess ? null : cx.Find(id);
      if (loc != null && loc.Alias != null) {                                                            // a reference loop variable: the element itself
        int an = NextSig(t, i + 1);
        int am = an < 0 ? -1 : NextSig(t, an + 1);
        int ao = am < 0 ? -1 : NextSig(t, am + 1);
        if (an < 0 || an >= b || !Is(t[an], ".") || am < 0 || (ao >= 0 && ao < b && Is(t[ao], "("))) throw new LowerUnsupported("`" + id + "`, a reference loop variable, used other than as `" + id + ".field`");
        sb.Append(loc.Alias).Append('.');
        i = am;
        continue;
      }
      if (loc != null && (loc.Class != null || loc.Vector != null || loc.Foreign != null)) {
        string idt = loc.PtrRef ? "(*" + id + ")" : id;
        i = ObjectUse(t, i + 1, b, loc, idt, cx, sb, loc.IsPtr && loc.Class == null && loc.Vector == null ? "(*" + id + ")" : idt);
        continue;
      }
      if (loc != null) { sb.Append(loc.IsPtr && loc.Class == null && loc.Vector == null ? "(*" + id + ")" : id); i++; continue; }
      if (!afterAccess && cx.Cls != null) {                                                              // a member of this class, written bare
        bool call = n1 >= 0 && Is(t[n1], "(");
        if (!call) {
          var sfld = cx.Cls.Statics.FirstOrDefault(x => x.Name == id);
          if (sfld != null && !IsPrimitive(sfld.Type)) {
            string gname = cx.Cls.Name + "_" + id;
            i = ObjectUse(t, i + 1, b, MakeLocal(sfld.Type, false), gname, cx, sb, gname);
            continue;
          }
          if (sfld != null) { sb.Append(cx.Cls.Name).Append('_').Append(id); i++; continue; }
          var ofld = FieldOfChain(cx.Cls, id);
          if (ofld != null && IsPrimitive(ofld.Type)) ofld = null;
          if (ofld != null) {
            if (cx.IsStatic) throw new LowerUnsupported("field " + id + " used in a static method");
            string otx = "this->" + FieldPath(cx.Cls, id) + id;
            i = ObjectUse(t, i + 1, b, MakeLocal(ofld.Type, false), otx, cx, sb, otx);
            continue;
          }
          string fpath = FieldPath(cx.Cls, id);
          if (fpath != null) {
            if (cx.IsStatic) throw new LowerUnsupported("field " + id + " used in a static method");
            sb.Append("this->").Append(fpath).Append(id);
            i++;
            continue;
          }
        } else {
          var meth = FindMethod(cx.Cls, id, ArgCount(t, n1 + 1, MatchParen(t, n1)), out var mowner);
          if (meth != null) {
            int close = MatchParen(t, n1);
            string a2 = Args(t, n1 + 1, close, cx, meth.Params);
            if (meth.Static) sb.Append(FuncName(mowner, meth)).Append('(').Append(a2).Append(')');
            else {
              string bcall = CallMethod(cx.Cls, meth, mowner, "this", a2);
              int bn = NextSig(t, close + 1);
              if (bn >= 0 && bn < b && (Is(t[bn], ".") || Is(t[bn], "->"))) { i = Chain(t, close + 1, b, bcall, meth, cx, sb); continue; }
              sb.Append(bcall);
            }
            i = close + 1;
            continue;
          }
        }
      }
      if (!afterAccess && loc == null && n1 >= 0 && n1 < b && Is(t[n1], "(") && helpers.TryGetValue(id, out var hf) && (cx.Cls == null || !cx.Cls.Methods.Any(m => m.Name == id))) {     // a helper function
        int close = MatchParen(t, n1);
        sb.Append(id).Append('(').Append(Args(t, n1 + 1, close, cx, hf.Params)).Append(')');
        i = close + 1;
        continue;
      }
      if (!afterAccess && foreign != null && loc == null && n1 >= 0 && Is(t[n1], "(") && foreign.ByName.TryGetValue(id, out var fp) && !foreign.Types.Contains(id)) {   // a library function
        if (fp.Count != 1) throw new LowerUnsupported(id + ": several prototypes");
        int close = MatchParen(t, n1);
        sb.Append(id).Append('(').Append(Args(t, n1 + 1, close, cx, foreign.Declared(fp[0], false))).Append(')');
        i = close + 1;
        continue;
      }
      if (!afterAccess && cx.Find(id) == null && classes.ContainsKey(id) && n1 >= 0 && Is(t[n1], "(")) throw new LowerUnsupported("a constructor call `" + id + "(...)` inside an expression");
      if (!afterAccess && id == "NULL") { sb.Append("((void *)0)"); i++; continue; }
      if (!afterAccess && id == "new" && n1 >= 0 && n1 < b && t[n1].Kind == K.Ident && classes.TryGetValue(t[n1].S, out var nc) && IsArena(nc)) {      // new Cls(args): the next slot of its arena
        int op = NextSig(t, n1 + 1);
        if (op < 0 || op >= b || !Is(t[op], "(")) throw new LowerUnsupported("`new " + nc.Name + "` without a constructor call");
        int close = MatchParen(t, op);
        int argc = ArgCount(t, op + 1, close);
        var pick = nc.Ctors.FirstOrDefault(c => c.Params.Count == argc);
        if (pick == null) throw new LowerUnsupported("class " + nc.Name + " has no constructor taking " + argc + " arguments");
        string a3 = Args(t, op + 1, close, cx, pick.Params);
        sb.Append(AllocName(nc, pick)).Append('(').Append(a3.TrimStart()).Append(')');
        i = close + 1;
        continue;
      }
      if (!afterAccess && (id == "new" || id == "delete" || id == "template" || (id == "this" && (cx.Cls == null || cx.IsStatic)))) throw new LowerUnsupported("`" + id + "` in an expression");
      if (!afterAccess && id == "std") throw new LowerUnsupported("`std::` in an expression: the standard library is cpprust's here yet");
      sb.Append(id);
      i++;
    }
    return sb.ToString();
  }

  /** An object-typed expression (`text`: a local, `this->f`, `b.f`) and what may follow it: `[i]`, `.m(args)`, `.field`.  Returns the index after what it consumed. */
  int ObjectUse(List<Tok> t, int after, int b, Local l, string text, Ctx cx, StringBuilder sb, string bare)
  {
    int n = NextSig(t, after);
    if (n < 0 || n >= b) { sb.Append(bare); return after; }
    string recv = l.IsPtr ? text : "&" + text;
    if (l.Vector != null && Is(t[n], "[")) {
      int close = n, depth = 0;
      for (int k = n; k < b; k++) { if (Is(t[k], "[")) depth++; else if (Is(t[k], "]") && --depth == 0) { close = k; break; } }
      string el = "(*" + l.Vector + "__index(" + recv + ", " + Expr(t, n + 1, close, cx).Trim() + "))";
      if (l.Elem != null) {
        var el2 = new Local { Type = l.Elem, Class = classes.ContainsKey(l.Elem) ? l.Elem : null, Foreign = classes.ContainsKey(l.Elem) ? null : l.Elem };
        return ObjectUse(t, close + 1, b, el2, el, cx, sb, el);
      }
      var pe = Regex.Match(l.Vector, "^vector_(\\w+)_P$");
      if (pe.Success && classes.ContainsKey(pe.Groups[1].Value))                    // an element of a vector of pointers: an object reference
        return ObjectUse(t, close + 1, b, new Local { Type = pe.Groups[1].Value + " *", Class = pe.Groups[1].Value, IsPtr = true, Pointer = true }, el, cx, sb, el);
      sb.Append(el);
      return close + 1;
    }
    if (Is(t[n], ".") || Is(t[n], "->")) {
      int mi = NextSig(t, n + 1);
      int op = mi < 0 ? -1 : NextSig(t, mi + 1);
      if (op >= 0 && op < b && Is(t[op], "(")) {
        int close = MatchParen(t, op);
        if (l.Class != null) {
          var meth = FindMethod(classes[l.Class], t[mi].S, ArgCount(t, op + 1, close), out var mowner);
          if (meth == null) throw new LowerUnsupported(l.Class + "." + t[mi].S + ": no such method");
          string a2 = Args(t, op + 1, close, cx, meth.Params);
          string callText = CallMethod(classes[l.Class], meth, mowner, recv, a2);
          int nx = NextSig(t, close + 1);
          if (nx >= 0 && nx < b && (Is(t[nx], ".") || Is(t[nx], "->")) && !meth.Static) return Chain(t, close + 1, b, callText, meth, cx, sb);
          sb.Append(callText);
        } else if (l.Vector != null) {
          List<Param> vps = null;
          if (l.Elem != null) {
            if (t[mi].S != "push_back" && t[mi].S != "size" && t[mi].S != "clear" && t[mi].S != "empty" && t[mi].S != "reserve" && t[mi].S != "pop_back") throw new LowerUnsupported("`" + t[mi].S + "` on a vector of objects");
            if (t[mi].S == "push_back") vps = new List<Param> { new Param { Type = "const " + l.Elem + " *", Name = "v", IsRef = true } };
          }
          string a2 = Args(t, op + 1, close, cx, vps);
          sb.Append(l.Vector).Append('_').Append(t[mi].S == "erase" && l.Vector.StartsWith("vector_") ? "erase_" + ArgCount(t, op + 1, close) : t[mi].S).Append('(').Append(recv).Append(a2.Length > 0 ? ", " + a2.TrimStart() : "").Append(')');
        } else if (l.Foreign != null) {
          var pr = Pick(foreign.Members(l.Foreign, t[mi].S), true, ArgCount(t, op + 1, close), l.Foreign, t[mi].S);
          string a2 = Args(t, op + 1, close, cx, foreign.Declared(pr, true));
          sb.Append(pr.Name).Append('(').Append(recv).Append(a2.Length > 0 ? ", " + a2.TrimStart() : "").Append(')');
        } else throw new LowerUnsupported("a method call on `" + text + "` of type " + l.Type);
        return close + 1;
      }
      string acc = l.IsPtr ? "->" : ".";
      string fpath2 = "";
      if (l.Class != null && mi >= 0) {
        fpath2 = FieldPath(classes[l.Class], t[mi].S) ?? "";
        Member fld = null;
        for (var fc = classes[l.Class]; fc != null && fld == null; fc = BaseOf(fc)) fld = fc.Fields.FirstOrDefault(f => f.Name == t[mi].S);
        if (fld != null && !IsPrimitive(fld.Type)) {
          string ft = text + acc + fpath2 + t[mi].S;
          return ObjectUse(t, mi + 1, b, MakeLocal(fld.Type, false), ft, cx, sb, ft);
        }
      }
      sb.Append(text).Append(acc).Append(fpath2);
      return mi;
    }
    sb.Append(bare);
    return after;
  }

  static int ArgCount(List<Tok> t, int a, int b)
  {
    int n = 0, start = a, depth = 0;
    for (int i = a; i <= b; i++) {
      bool end = i == b;
      if (!end) {
        if (depth == 0 && Is(t[i], ",")) end = true;
        else if (Is(t[i], "(") || Is(t[i], "[") || Is(t[i], "{")) depth++;
        else if (Is(t[i], ")") || Is(t[i], "]") || Is(t[i], "}")) depth--;
      }
      if (!end) continue;
      if (Join(t, start, i).Trim().Length > 0) n++;
      start = i + 1;
    }
    return n;
  }

  /** The library function that takes this many arguments (cpprust numbers overloads; telling them apart by type is not lowered here yet). */
  Proto Pick(List<Proto> cands, bool inst, int argc, string type, string what)
  {
    var ok = cands.Where(x => foreign.IsInst(x, type) == inst && x.Params.Count - (inst ? 1 : 0) == argc).ToList();
    if (ok.Count == 1) return ok[0];
    throw new LowerUnsupported(type + (inst ? "." : "::") + what + (ok.Count == 0 ? ": no such function with " + argc + " argument(s) in the library" : ": several overloads take " + argc + " argument(s)"));
  }

  /** The arguments of a call, lowered, joined with ", ".  An argument for a reference parameter is passed by address. */
  int baCounter, ctorRetCounter, tmpCounter;

  string Args(List<Tok> t, int a, int b, Ctx cx, List<Param> ps)
  {
    var parts = new List<string>();
    int start = a, depth = 0;
    for (int i = a; i <= b; i++) {
      bool end = i == b;
      if (!end) {
        if (depth == 0 && Is(t[i], ",")) end = true;
        else if (Is(t[i], "(") || Is(t[i], "[") || Is(t[i], "{")) depth++;
        else if (Is(t[i], ")") || Is(t[i], "]") || Is(t[i], "}")) depth--;
      }
      if (!end) continue;
      string text = Join(t, start, i).Trim();
      if (text.Length > 0) {
        int k = parts.Count;
        string low;
        if (ps != null && k < ps.Count && ps[k].IsRef) {
          var l = cx.Find(text);
          if (l != null) low = l.IsPtr && !(l.Pointer && !l.PtrRef) ? text : "&" + text;
          else if (cx.Cls != null && !cx.IsStatic && FieldPath(cx.Cls, text) != null) low = "&this->" + FieldPath(cx.Cls, text) + text;                  // a field of this
          else if (cx.Cls != null && cx.Cls.Statics.Any(f => f.Name == text)) low = "&" + cx.Cls.Name + "_" + text;                                      // a static of this class
          else if (Regex.Match(text, "^(\\w+)\\s*::\\s*(\\w+)$") is Match sm && sm.Success && classes.TryGetValue(sm.Groups[1].Value, out var sc) && sc.Statics.Any(f => f.Name == sm.Groups[2].Value)) low = "&" + sm.Groups[1].Value + "_" + sm.Groups[2].Value;     // Cls::static
          else {
            var fm = Regex.Match(text, "^(\\w+)\\.(\\w+)$");                                                                         // local.field
            var ol = fm.Success ? cx.Find(fm.Groups[1].Value) : null;
            if (ol == null || ol.Class == null || !classes[ol.Class].Fields.Any(f => f.Name == fm.Groups[2].Value)) throw new LowerUnsupported("a reference argument that is not a plain variable: " + text);
            low = "&" + fm.Groups[1].Value + (ol.IsPtr ? "->" : ".") + fm.Groups[2].Value;
          }
        } else {
          low = Expr(t, start, i, cx).Trim();
          var l = ps != null && k < ps.Count && ps[k].Name != "" && !ps[k].IsRef && Regex.IsMatch(text, "^[A-Za-z_]\\w*$") ? cx.Find(text) : null;
          if (l != null && l.Foreign != null && l.Droppable && !l.IsPtr) {                      // a by-value argument is a copy
            string bn = "_cpp_ba" + (++baCounter);
            low = (parts.Count == 0 ? " " : "") + "({ " + l.Foreign + " " + bn + "; " + l.Foreign + "_copy(&" + bn + ", &(" + text + ")); " + bn + "; })";
          }
        }
        parts.Add(low);
      }
      start = i + 1;
    }
    return string.Join(", ", parts);
  }
}
