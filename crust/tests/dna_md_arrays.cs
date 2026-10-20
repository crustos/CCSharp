// dna
// Arrays of rank 2 and 3 on DotNetAnywhere (which has none: they compile as the corlib's MdArray2 / MdArray3): creation, element reads and writes,
// compound assignment, GetLength / Length / Rank, foreach in row-major order, both initializer forms, arrays of them and them of arrays, a field,
// and an index out of range.
using System;

class Grid
{
    public int[,] cells;
    public Grid(int w, int h) { cells = new int[w, h]; }
}

class Program
{
    static int Sum(int[,] a)
    {
        int s = 0;
        for (int i = 0; i < a.GetLength(0); i++)
            for (int j = 0; j < a.GetLength(1); j++) s += a[i, j] * (i + 1);
        return s;
    }

    static int Main()
    {
        int[,] a = new int[2, 3];
        a[1, 2] = 5;
        a[0, 1] += 7;
        a[1, 2]++;
        Console.WriteLine(a.Length + " " + a.Rank + " " + a.GetLength(0) + " " + a.GetLength(1) + " " + Sum(a));   // 6 2 2 3 19
        int[,] b = { { 1, 2 }, { 3, 4 }, { 5, 6 } };
        string order = "";
        foreach (int x in b) order += x;
        Console.WriteLine(order + " " + b[2, 1] + " " + b.GetUpperBound(0));                                      // 123456 6 2
        var c = new string[,] { { "a", "b" }, { "c", "d" } };
        Console.WriteLine(c[1, 0] + c[0, 1]);                                                                       // cb
        float[,,] d = new float[2, 2, 2];
        d[1, 0, 1] = 2.5f;
        Console.WriteLine(d.Length + " " + d.Rank + " " + d[1, 0, 1] + " " + d.GetLength(2));                     // 8 3 2.5 2
        int[][,] jag = new int[2][,];
        jag[1] = new int[1, 1];
        jag[1][0, 0] = 9;
        int[,][] rows = new int[1, 2][];
        rows[0, 1] = new int[] { 4, 4 };
        Console.WriteLine(jag[1][0, 0] + " " + rows[0, 1].Length + " " + (rows[0, 0] == null ? "null" : "set"));    // 9 2 null
        Grid g = new Grid(3, 2);
        g.cells[2, 1] = 1;
        Console.WriteLine(g.cells.GetLength(0) * 10 + g.cells.GetLength(1) + " " + g.cells[2, 1]);                  // 32 1
        try { a[2, 0] = 1; Console.WriteLine("no"); } catch (IndexOutOfRangeException) { Console.WriteLine("out of range"); }
        return 0;
    }
}
