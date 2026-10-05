/*
  CC# --dna : the managed side to CIL.

  The classes that stay C# (and the C# proxies of the native classes they call) are compiled by Roslyn, here, in the compiler, against
  DotNetAnywhere's own class library: `--dna-corlib=PATH` is its corlib.dll, and nothing else is referenced, which is what
  `csc -nostdlib -r:corlib.dll` does.  The result runs on DNA (native/src/Host.h); corlib.dll must sit beside it.
*/
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CCSharpCompiler;

static class ManagedBuild
{
  /** Compile `files` to `outPath`.  Returns the diagnostics that are errors (empty on success). */
  public static List<string> Compile(IEnumerable<string> files, string corlibPath, string outPath, bool exe, string mainType)
  {
    var errors = new List<string>();
    var trees = files.Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f)).ToList();
    var options = new CSharpCompilationOptions(exe ? OutputKind.ConsoleApplication : OutputKind.DynamicallyLinkedLibrary,
      mainTypeName: exe ? mainType : null, allowUnsafe: true, optimizationLevel: OptimizationLevel.Release);
    var comp = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(outPath), trees, new[] { MetadataReference.CreateFromFile(corlibPath) }, options);
    using (var ms = new MemoryStream()) {
      var result = comp.Emit(ms);
      foreach (var d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)) errors.Add("managed: " + d);
      if (result.Success) File.WriteAllBytes(outPath, ms.ToArray());
      else if (errors.Count == 0) errors.Add("managed: the managed assembly did not compile");
    }
    return errors;
  }
}
