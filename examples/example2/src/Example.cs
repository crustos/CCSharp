using System;
using System.Collections.Generic;
using System.Diagnostics;

/*
  A small benchmark in the style of the original CC# example, on the Crust corelib:
  a growing List<string> scanned on every insert.  Strings are coost fastrings held by value.
*/
public class Example {
  public static int Count = 64;

  public static int Main(String[] args) {
    String s1 = "--";
    String s2 = "++";
    Stopwatch sw = Stopwatch.StartNew();
    List<String> al = new List<String>();
    int hits = 0;
    for (int x = 0; x < Count * 32; x++) {
      al.Add(s1);
      int cnt = al.Count;
      for (int y = 0; y < cnt; y++) {
        if (al[y].Equals(s2)) {
          hits++;
        }
      }
    }
    Console.WriteLine("entries=" + al.Count);
    Console.WriteLine("hits=" + hits);
    Console.WriteLine("test4=" + sw.ElapsedMilliseconds + "ms");
    return hits;
  }
}
