/*
  CC# --dna : the managed side to CIL.

  The classes that stay C# (and the C# proxies of the native classes they call) are compiled by Roslyn, here, in the compiler, against
  DotNetAnywhere's own class library: `--dna-corlib=PATH` is its corlib.dll, and nothing else is referenced, which is what
  `csc -nostdlib -r:corlib.dll` does.  The result runs on DNA (native/src/Host.h); corlib.dll must sit beside it.
*/
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CCSharpCompiler;

static class ManagedBuild
{
  /** Compile `files` to `outPath`.  Returns the diagnostics that are errors (empty on success). */
  public static List<string> Compile(IEnumerable<string> files, string corlibPath, string outPath, bool exe, string mainType)
  {
    var errors = new List<string>();
    var texts = files.Select(f => (path: f, text: File.ReadAllText(f))).ToList();
    var options = new CSharpCompilationOptions(exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
      mainTypeName: exe ? mainType : null, allowUnsafe: true, optimizationLevel: OptimizationLevel.Release);
    for (int attempt = 0; attempt < 2; attempt++) {
      errors.Clear();
      var trees = texts.Select(t => MdArrays.Rewrite(CSharpSyntaxTree.ParseText(t.text, Program.ManagedParse, path: t.path))).ToList();
      var comp = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(outPath), trees, new[] { MetadataReference.CreateFromFile(corlibPath) }, options);
      using (var ms = new MemoryStream()) {
        var result = comp.Emit(ms);
        foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) errors.Add("managed: " + d);
        if (result.Success) { File.WriteAllBytes(outPath, ms.ToArray()); return errors; }
        // A `using` of a namespace that has no managed side (the bindings of a native library, say) in a file that kept some managed classes: the
        // directive cannot be satisfied, and nothing managed uses it. Blank those directives out (line for line) and compile once more.
        var bad = new Dictionary<SyntaxTree, List<Microsoft.CodeAnalysis.Text.TextSpan>>();
        foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error && (d.Id == "CS0234" || d.Id == "CS0246") && d.Location.SourceTree != null)) {
          var u = d.Location.SourceTree.GetRoot().FindToken(d.Location.SourceSpan.Start).Parent?.AncestorsAndSelf().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax>().FirstOrDefault();
          if (u == null) continue;
          if (!bad.TryGetValue(d.Location.SourceTree, out var l)) bad[d.Location.SourceTree] = l = new List<Microsoft.CodeAnalysis.Text.TextSpan>();
          if (!l.Contains(u.Span)) l.Add(u.Span);
        }
        if (bad.Count == 0 || attempt == 1) { if (errors.Count == 0) errors.Add("managed: the managed assembly did not compile"); break; }
        for (int i = 0; i < texts.Count; i++) {
          var tree = trees[i];
          if (!bad.TryGetValue(tree, out var spans)) continue;
          var chars = texts[i].text.ToCharArray();
          foreach (var sp in spans) for (int k = sp.Start; k < sp.End; k++) if (chars[k] != '\n' && chars[k] != '\r') chars[k] = ' ';
          texts[i] = (texts[i].path, new string(chars));
        }
      }
    }
    return errors;
  }
}

/** DotNetAnywhere has no arrays of rank 2 or 3: their types and creations become the corlib's MdArray2 / MdArray3 (an element access, GetLength, Length
    and foreach read the same).  `T[,] a = { { .. } }` and `new T[,] { { .. } }` become MdArray2<T>.Of(rows); a rank 3 initializer stays (and fails). */
sealed class MdArrays : CSharpSyntaxRewriter
{
  public static SyntaxTree Rewrite(SyntaxTree tree)
  {
    if (!tree.GetRoot().DescendantNodes().OfType<ArrayRankSpecifierSyntax>().Any(r => r.Rank > 1)) return tree;
    var root = new MdArrays().Visit(tree.GetRoot());
    return tree.WithRootAndOptions(root, tree.Options);
  }

  static TypeSyntax Md(int rank, TypeSyntax elem)
  {
    return SyntaxFactory.ParseTypeName("global::System.Runtime.CompilerServices.MdArray" + rank + "<" + elem.ToString() + ">");
  }

  /** The type of `specs` (outermost first) over `elem`: each rank-1 specifier an array, each higher one an MdArray. */
  TypeSyntax Build(TypeSyntax elem, IEnumerable<ArrayRankSpecifierSyntax> specs)
  {
    TypeSyntax t = (TypeSyntax)Visit(elem);
    foreach (var spec in specs.Reverse())
      t = spec.Rank == 1 ? SyntaxFactory.ArrayType(t, SyntaxFactory.SingletonList(SyntaxFactory.ArrayRankSpecifier(
            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(SyntaxFactory.OmittedArraySizeExpression())))) : Md(spec.Rank, t);
    return t;
  }

  public override SyntaxNode VisitArrayType(ArrayTypeSyntax node)
  {
    if (node.RankSpecifiers.All(r => r.Rank == 1)) return base.VisitArrayType(node);
    return Build(node.ElementType, node.RankSpecifiers).WithTriviaFrom(node);
  }

  ExpressionSyntax Rows(TypeSyntax elem, InitializerExpressionSyntax init)
  {
    var e = elem.ToString();
    var rows = init.Expressions.Select(r => r is InitializerExpressionSyntax ri ? "new " + e + "[] { " + string.Join(", ", ri.Expressions.Select(x => Visit(x).ToString())) + " }" : Visit(r).ToString());
    return SyntaxFactory.ParseExpression("global::System.Runtime.CompilerServices.MdArray2<" + e + ">.Of(new " + e + "[][] { " + string.Join(", ", rows) + " })");
  }

  public override SyntaxNode VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
  {
    var specs = node.Type.RankSpecifiers;
    if (specs.All(r => r.Rank == 1)) return base.VisitArrayCreationExpression(node);
    var first = specs[0];
    var elem = Build(node.Type.ElementType, specs.Skip(1));
    if (first.Rank == 1) {
      // `new MdArray2<T>[n]`: an array of them
      var arr = SyntaxFactory.ArrayType(elem, SyntaxFactory.SingletonList((ArrayRankSpecifierSyntax)Visit(first)));
      return node.WithType(arr).WithInitializer(node.Initializer == null ? null : (InitializerExpressionSyntax)Visit(node.Initializer));
    }
    if (node.Initializer != null && first.Rank == 2) return Rows(elem, node.Initializer).WithTriviaFrom(node);
    var args = first.Sizes.Select(x => SyntaxFactory.Argument((ExpressionSyntax)Visit(x)));
    return SyntaxFactory.ObjectCreationExpression(Md(first.Rank, elem), SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args)), null).WithTriviaFrom(node);
  }

  public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
  {
    if (node.Type is ArrayTypeSyntax at && at.RankSpecifiers[0].Rank == 2 && node.Variables.Any(v => v.Initializer?.Value is InitializerExpressionSyntax)) {
      var elem = Build(at.ElementType, at.RankSpecifiers.Skip(1));
      var vars = node.Variables.Select(v => v.Initializer?.Value is InitializerExpressionSyntax ie
        ? v.WithInitializer(v.Initializer.WithValue(Rows(elem, ie))) : (VariableDeclaratorSyntax)Visit(v));
      return node.WithType(((TypeSyntax)Visit(node.Type)).WithTriviaFrom(node.Type)).WithVariables(SyntaxFactory.SeparatedList(vars));
    }
    return base.VisitVariableDeclaration(node);
  }
}
