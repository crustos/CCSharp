// refuse: derives from the arena class
using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
[MaxInstances(4)] class Base { public int X; }
class Derived : Base { public int Y; }
class Program { static int Main() { Derived d = new Derived(); return d.Y; } }
