// dna
// A native class that managed code uses in ways that cannot cross (its objects, an array it returns) is not refused: it follows the managed class.
using System;
class ManagedAttribute : Attribute { }
class Vec { public int x; public Vec(int a) { x = a; } public int Get() { return x; } public static int[] Make(int n) { return new int[n]; } }
[Managed] class Script { public static int Go(Vec w) { Vec v = new Vec(3); return v.Get() + Vec.Make(4).Length + w.Get(); } }
class Program { static int Main() { Vec v = new Vec(2); int r = Script.Go(v); Console.WriteLine(r); return r; } }
