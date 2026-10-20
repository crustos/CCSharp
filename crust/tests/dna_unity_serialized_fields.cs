// dna
// unity-engine
// What Unity's serializer gives a script before Awake: a [Serializable] class field gets an object (and its own fields theirs), a private
// [SerializeField] one too, an array an empty array, a List<T> an empty list, a string ""; a value already set is kept; a field that is not
// serialized (private, [NonSerialized], of a class that is not [Serializable]) stays null; a self-referencing class stops at Unity's depth.
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public class Range { public float min; public float max = 1f; }
[Serializable] public class Clamped { public float value; public Range range; public string label; }
[Serializable] public class Chain { public Chain next; }
public class Plain { public int x; }

public class Holder : MonoBehaviour
{
    public Clamped clamped;
    [SerializeField] Clamped hidden;
    Clamped notSerialized;
    [NonSerialized] public Clamped skipped;
    public Plain plain;
    public int[] numbers;
    public List<Range> ranges;
    public string title;
    public Clamped preset;
    public Chain chain;

    public string Report()
    {
        int depth = 0;
        for (Chain c = chain; c != null; c = c.next) depth++;
        return (clamped != null) + " " + (clamped.range != null) + " " + clamped.range.max + " [" + clamped.label + "] " + (hidden != null && hidden.range != null)
            + " " + (notSerialized == null) + " " + (skipped == null) + " " + (plain == null) + " " + numbers.Length + " " + ranges.Count
            + " [" + title + "] " + preset.value + " " + depth;
    }
}

class Program
{
    static int Main()
    {
        GameObject g = new GameObject("g");
        Holder h = new Holder();
        h.preset = new Clamped();
        h.preset.value = 7f;
        g.Attach(h);
        Console.WriteLine(h.Report());            // True True 1 [] True True True True 0 0 [] 7 11
        Holder added = g.AddComponent<Holder>();
        Console.WriteLine(added.clamped != null && added.ranges != null);   // True
        return 0;
    }
}
