// dna
// refuse: through their static methods only
using System;
class ManagedAttribute : Attribute { }
class NativeAttribute : Attribute { }
[Native] class Vec { public int x; public Vec(int a) { x = a; } }
[Managed] class Script { public static int Go(Vec v) { return 1; } }
class Program { static int Main() { Vec v = new Vec(3); return Script.Go(v); } }
