using Crust;

namespace System.Collections.Generic {

  /* List<T> is Crust's vector<T>.  Indexing is unchecked, as in C. */
  [Cpp("std::vector<{T0}>")]
  public sealed class List<T> : IEnumerable<T> {
    public List() {}
    [Cpp("(int){this}.size()")]                public extern int Count { get; }
    public extern T this[int index] { get; set; }
    [Cpp("{this}.push_back({0})")]             public extern void Add(T item);
    [Cpp("{this}.clear()")]                    public extern void Clear();

    // `foreach` is lowered to a range-for by the emitter; the enumerator exists so that the C# compiler binds it.
    public extern Enumerator GetEnumerator();
    extern IEnumerator<T> IEnumerable<T>.GetEnumerator();
    extern System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator();
    public struct Enumerator : IEnumerator<T> {
      public extern T Current { get; }
      extern object System.Collections.IEnumerator.Current { get; }
      public extern bool MoveNext();
      public extern void Reset();
      public extern void Dispose();
    }

    public extern bool Contains(T item);                  // not implemented yet
    public extern bool Remove(T item);
    public extern void RemoveAt(int index);
    public extern void Insert(int index, T item);
    public extern int IndexOf(T item);
    public extern void Sort();
    public extern T[] ToArray();
  }

  /* Dictionary<K,V> is Crust's ordered map<K,V>.  Reading a missing key is unchecked (it inserts a default). */
  [Cpp("std::map<{T0}, {T1}>")]
  public sealed class Dictionary<TKey, TValue> {
    public Dictionary() {}
    [Cpp("(int){this}.size()")]                       public extern int Count { get; }
    public extern TValue this[TKey key] { get; set; }
    [Cpp("({this}.count({0}) > 0)")]                  public extern bool ContainsKey(TKey key);
    [Cpp("({this}.erase({0}) > 0)")]                  public extern bool Remove(TKey key);
    [Cpp("{this}.clear()")]                           public extern void Clear();

    public extern void Add(TKey key, TValue value);       // not implemented yet
    public extern bool TryGetValue(TKey key, out TValue value);
  }
}
