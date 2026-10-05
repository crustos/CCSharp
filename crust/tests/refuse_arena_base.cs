// refuse: an arena class with a base class or an interface is not supported
using System;
class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }
class Base { public int X; }
[MaxInstances(4)] class Derived : Base { public int Y; }
class Program { static int Main() { Derived d = new Derived(); return d.Y; } }
