using Crust;

namespace System.Collections.Generic {

  /* List<T> is Crust's vector<T>.  Indexing is unchecked, as in C. */
  [Cpp("std::vector<{T0}>")]
  public sealed class List<T> : IEnumerable<T>, System.Collections.IList {
    public List() {}
    public List(int capacity) {}                          // (a hint: the list is built empty -- see CrustEmitter.CollectionCtor)
    public List(List<T> collection) {}                    // (a copy of another List<T> whose elements are not owned classes)
    [Cpp("(int){this}.size()")]                public extern int Count { get; }
    public extern T this[int index] { get; set; }
    // set-only on purpose: assigning is a real `reserve` (a hint a program cannot see), and there is no capacity to read back that .NET and C would agree on
    [CppSet("{this}.reserve({0})")]            public extern int Capacity { set; }
    [Cpp("{this}.push_back({0})")]             public extern void Add(T item);
    [Cpp("{this}.clear()")]                    public extern void Clear();

    // `foreach` is lowered to a range-for by the emitter; the enumerator exists so that the C# compiler binds it.
    public extern Enumerator GetEnumerator();
    extern IEnumerator<T> IEnumerable<T>.GetEnumerator();
    extern System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator();
    // the non-generic IList: not implemented natively (managed code has it, on DotNetAnywhere)
    extern object System.Collections.IList.this[int index] { get; set; }
    extern int System.Collections.IList.Add(object value);
    extern void System.Collections.IList.Clear();
    extern bool System.Collections.IList.Contains(object value);
    extern int System.Collections.IList.IndexOf(object value);
    extern void System.Collections.IList.Insert(int index, object value);
    extern void System.Collections.IList.Remove(object value);
    extern void System.Collections.IList.RemoveAt(int index);
    extern int System.Collections.ICollection.Count { get; }
    extern void System.Collections.ICollection.CopyTo(Array array, int index);
    public struct Enumerator : IEnumerator<T> {
      public extern T Current { get; }
      extern object System.Collections.IEnumerator.Current { get; }
      public extern bool MoveNext();
      public extern void Reset();
      public extern void Dispose();
    }

    // (an index out of range is unchecked, as indexing is: C# throws, here it is the C vector's behaviour)
    [Cpp("{this}.erase({this}.begin() + {0})")]                        public extern void RemoveAt(int index);
    [Cpp("{this}.insert({this}.begin() + {0}, {1})")]                  public extern void Insert(int index, T item);

    [Cpp("__cs_reverse_{M0}({this})", Helper = "static void __cs_reverse_{M0}(std::vector<{T0}> &v) { int i = 0; int j = (int)v.size() - 1; while (i < j) { {T0} t = v[i]; v[i] = v[j]; v[j] = t; i = i + 1; j = j - 1; } }")]
    public extern void Reverse();
    public extern bool Contains(T item);                  // not implemented yet
    public extern bool Remove(T item);
    public extern int IndexOf(T item);
    public extern void Sort();
    public extern void Sort(Comparison<T> comparison);                // not implemented: it calls a delegate
    public extern void Sort(IComparer<T> comparer);                   // not implemented: it calls through an interface
    public extern T[] ToArray();
    // not implemented natively (managed code has them, on DotNetAnywhere)
    public List(IEnumerable<T> collection) {}
    public extern void AddRange(IEnumerable<T> collection);
    public extern void InsertRange(int index, IEnumerable<T> collection);
    public extern void RemoveRange(int index, int count);
    public extern List<T> GetRange(int index, int count);
    public extern void Sort(int index, int count, IComparer<T> comparer);
    public extern void Reverse(int index, int count);
    public extern T Find(Predicate<T> match);
    public extern T FindLast(Predicate<T> match);
    public extern List<T> FindAll(Predicate<T> match);
    public extern int FindIndex(Predicate<T> match);
    public extern int FindIndex(int startIndex, Predicate<T> match);
    public extern int FindIndex(int startIndex, int count, Predicate<T> match);
    public extern int FindLastIndex(Predicate<T> match);
    public extern bool Exists(Predicate<T> match);
    public extern bool TrueForAll(Predicate<T> match);
    public extern int RemoveAll(Predicate<T> match);
    public extern void ForEach(Action<T> action);
    public extern List<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter);
    public extern int LastIndexOf(T item);
    public extern void CopyTo(T[] array);
    public extern void CopyTo(T[] array, int arrayIndex);
  }

  /* Stack<T> is Crust's vector<T> used from the back.  Peek and Pop of an empty stack are unchecked, as indexing is: C# throws, here it is the C vector's behaviour.
     There is no enumerator: .NET enumerates a stack from the top, a range-for over a vector goes from the bottom, so `foreach` is not offered (it does not bind). */
  [Cpp("std::vector<{T0}>")]
  public sealed class Stack<T> {
    public Stack() {}
    [Cpp("(int){this}.size()")]                public extern int Count { get; }
    [Cpp("{this}.push_back({0})")]             public extern void Push(T item);
    [Cpp("{this}[(int){this}.size() - 1]")]    public extern T Peek();
    [Cpp("__cs_stack_pop_{M0}({this})", Helper = "static {T0} __cs_stack_pop_{M0}(std::vector<{T0}> &v) { {T0} x = v[(int)v.size() - 1]; v.pop_back(); return x; }")]
    public extern T Pop();
    [Cpp("{this}.clear()")]                    public extern void Clear();
  }

  /* Dictionary<K,V> is Crust's ordered map<K,V>.  Reading a missing key is unchecked (it inserts a default). */
  [Cpp("std::map<{T0}, {T1}>")]
  public class Dictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> {
    public Dictionary() {}

    // enumerating is not implemented natively (managed code has it); the enumerator exists so that `foreach (var kv in d)` binds
    public extern Enumerator GetEnumerator();
    extern IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator();
    extern System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator();
    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>> {
      public extern KeyValuePair<TKey, TValue> Current { get; }
      extern object System.Collections.IEnumerator.Current { get; }
      public extern bool MoveNext();
      public extern void Reset();
      public extern void Dispose();
    }
    [Cpp("(int){this}.size()")]                       public extern int Count { get; }
    public extern TValue this[TKey key] { get; set; }
    [Cpp("({this}.count({0}) > 0)")]                  public extern bool ContainsKey(TKey key);
    [Cpp("({this}.erase({0}) > 0)")]                  public extern bool Remove(TKey key);
    [Cpp("{this}.clear()")]                           public extern void Clear();

    public extern ICollection<TValue> Values { get; }     // not implemented yet
    public extern ICollection<TKey> Keys { get; }
    public extern void Add(TKey key, TValue value);       // not implemented yet
    public extern bool TryGetValue(TKey key, out TValue value);
  }

  // not implemented natively (managed code has them, on DotNetAnywhere)
  public class HashSet<T> : ICollection<T> {
    public HashSet() {}
    public HashSet(IEnumerable<T> collection) {}
    public HashSet(IEqualityComparer<T> comparer) {}
    public extern int Count { get; }
    public extern bool Add(T item);
    public extern bool Remove(T item);
    public extern bool Contains(T item);
    public extern void Clear();
    public extern void CopyTo(T[] array);
    public extern void UnionWith(IEnumerable<T> other);
    public extern void ExceptWith(IEnumerable<T> other);
    public extern void IntersectWith(IEnumerable<T> other);
    public extern void SymmetricExceptWith(IEnumerable<T> other);
    public extern bool Overlaps(IEnumerable<T> other);
    public extern bool IsSubsetOf(IEnumerable<T> other);
    public extern bool IsSupersetOf(IEnumerable<T> other);
    public extern bool SetEquals(IEnumerable<T> other);
    public extern int RemoveWhere(Predicate<T> match);
    public extern bool TryGetValue(T equalValue, out T actualValue);
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
  }

  public abstract class EqualityComparer<T> : IEqualityComparer<T> {
    public static extern EqualityComparer<T> Default { get; }
    public abstract bool Equals(T x, T y);
    public abstract int GetHashCode(T obj);
  }
  public class KeyNotFoundException : SystemException { public KeyNotFoundException() {} public KeyNotFoundException(string message) {} }

  public struct KeyValuePair<TKey, TValue> {
    public KeyValuePair(TKey key, TValue value) {}                // not implemented natively (managed code has it)
    public extern TKey Key { get; }
    public extern TValue Value { get; }
  }

  /* Not implemented natively (managed code has it, on DotNetAnywhere). */
  public class SortedList<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>> {
    public SortedList() {}
    public SortedList(IComparer<TKey> comparer) {}
    public extern int Count { get; }
    public extern TValue this[TKey key] { get; set; }
    public extern IList<TKey> Keys { get; }
    public extern IList<TValue> Values { get; }
    public extern void Add(TKey key, TValue value);
    public extern bool Remove(TKey key);
    public extern void RemoveAt(int index);
    public extern void Clear();
    public extern bool ContainsKey(TKey key);
    public extern bool ContainsValue(TValue value);
    public extern int IndexOfKey(TKey key);
    public extern int IndexOfValue(TValue value);
    public extern TKey GetKeyAtIndex(int index);
    public extern TValue GetValueAtIndex(int index);
    public extern bool TryGetValue(TKey key, out TValue value);
    public extern IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator();
    extern System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator();
  }
}
