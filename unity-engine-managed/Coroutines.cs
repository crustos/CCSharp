// named-by: StartCoroutine StopCoroutine StopAllCoroutines yield Invoke InvokeRepeating CancelInvoke IsInvoking print
//
// UnityEngine coroutines, with Unity's timing: StartCoroutine runs the routine at once, up to its first yield; `yield return null` (or anything the
// engine does not know) resumes it the next frame after Update; WaitForSeconds after that many seconds of scaled time, also after Update;
// WaitForEndOfFrame at the end of the frame (after LateUpdate); WaitForFixedUpdate after FixedUpdate; an IEnumerator is run as a nested routine and
// a Coroutine (another StartCoroutine) is waited for.  Deactivating or destroying the object stops its coroutines (disabling the script does not).

using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class YieldInstruction { }

    public sealed class Coroutine : YieldInstruction
    {
        internal MonoBehaviour owner;
        internal Stack<IEnumerator> stack = new Stack<IEnumerator>();
        internal IEnumerator routine;
        internal bool done;
        internal CoroutinePhase phase;
        internal int readyFrame;            // CoroutinePhase.Update: not before this frame
        internal float readyTime;           // ... nor before this time (WaitForSeconds)
        internal Coroutine waitingFor;
        internal AsyncOperation waitingOp;
    }

    public sealed class WaitForSeconds : YieldInstruction
    {
        internal float seconds;
        public WaitForSeconds(float seconds) { this.seconds = seconds; }
    }

    public sealed class WaitForEndOfFrame : YieldInstruction { }

    public sealed class WaitForFixedUpdate : YieldInstruction { }

    /// <summary>A yield instruction that waits while keepWaiting is true (it is an IEnumerator, run as a nested routine).</summary>
    public abstract class CustomYieldInstruction : IEnumerator
    {
        public abstract bool keepWaiting { get; }
        public object Current { get { return null; } }
        public bool MoveNext() { return keepWaiting; }
        public virtual void Reset() { }
    }

    public sealed class WaitForSecondsRealtime : CustomYieldInstruction
    {
        float until;
        public float waitTime { get; set; }
        public WaitForSecondsRealtime(float time) { waitTime = time; until = -1f; }
        public override bool keepWaiting
        {
            get
            {
                if (until < 0f) until = Time.realtimeSinceStartup + waitTime;
                if (Time.realtimeSinceStartup < until) return true;
                until = -1f;
                return false;
            }
        }
        public override void Reset() { until = -1f; }
    }

    public sealed class WaitUntil : CustomYieldInstruction
    {
        readonly Func<bool> predicate;
        public WaitUntil(Func<bool> predicate) { this.predicate = predicate; }
        public override bool keepWaiting { get { return !predicate(); } }
    }

    public sealed class WaitWhile : CustomYieldInstruction
    {
        readonly Func<bool> predicate;
        public WaitWhile(Func<bool> predicate) { this.predicate = predicate; }
        public override bool keepWaiting { get { return predicate(); } }
    }

    /// <summary>An operation that finishes later (LoadSceneAsync); a coroutine that yields it waits until isDone.</summary>
    public class AsyncOperation : YieldInstruction
    {
        public bool isDone { get; internal set; }
        public float progress { get { return isDone ? 1f : 0.9f; } }
        public bool allowSceneActivation { get; set; }
        public int priority { get; set; }
        public event Action<AsyncOperation> completed;

        public AsyncOperation() { allowSceneActivation = true; }

        internal void Finish()
        {
            isDone = true;
            if (completed != null) completed(this);
        }
    }

    public enum CoroutinePhase { Update, EndOfFrame, FixedUpdate }

    /// <summary>The running coroutines and the Invoke timers; Engine.Tick steps them.</summary>
    public static class Coroutines
    {
        static readonly List<Coroutine> running = new List<Coroutine>();

        public static Coroutine Start(MonoBehaviour owner, IEnumerator routine)
        {
            if (routine == null) return null;
            Coroutine c = new Coroutine();
            c.owner = owner;
            c.routine = routine;
            c.stack.Push(routine);
            if (owner == null || owner.destroyed || !owner.gameObject.activeInHierarchy)
            {
                Debug.LogError("Coroutine couldn't be started because the game object is inactive!");
                c.done = true;
                return c;
            }
            Advance(c);
            if (!c.done) running.Add(c);
            return c;
        }

        /// <summary>Runs the routine to its next yield, and notes what it waits for.</summary>
        static void Advance(Coroutine c)
        {
            while (c.stack.Count > 0)
            {
                IEnumerator top = c.stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    c.stack.Clear();
                    break;
                }
                if (!more)
                {
                    c.stack.Pop();
                    continue;
                }
                object y = top.Current;
                c.waitingFor = null;
                c.waitingOp = null;
                c.readyTime = 0f;
                c.phase = CoroutinePhase.Update;
                c.readyFrame = Time.frameCount + 1;
                if (y is WaitForSeconds) c.readyTime = Time.time + ((WaitForSeconds)y).seconds;
                else if (y is WaitForEndOfFrame) { c.phase = CoroutinePhase.EndOfFrame; c.readyFrame = Time.frameCount; }
                else if (y is WaitForFixedUpdate) c.phase = CoroutinePhase.FixedUpdate;
                else if (y is Coroutine) c.waitingFor = (Coroutine)y;
                else if (y is AsyncOperation) c.waitingOp = (AsyncOperation)y;
                else if (y is IEnumerator)
                {
                    c.stack.Push((IEnumerator)y);       // a nested routine runs now, in this frame
                    continue;
                }
                return;
            }
            c.done = true;
        }

        static bool Ready(Coroutine c, CoroutinePhase phase)
        {
            if (c.waitingFor != null) return c.waitingFor.done && phase == CoroutinePhase.Update;
            if (c.waitingOp != null) return c.waitingOp.isDone && phase == CoroutinePhase.Update;
            if (c.phase != phase) return false;
            if (phase == CoroutinePhase.FixedUpdate) return true;
            return Time.frameCount >= c.readyFrame && Time.time >= c.readyTime;
        }

        public static void Step(CoroutinePhase phase)
        {
            if (phase == CoroutinePhase.Update) StepInvokes();
            // by index over a snapshot's length: a routine may start others, which join at the end and wait for a later step
            int n = running.Count;
            for (int i = 0; i < n && i < running.Count; i++)
            {
                Coroutine c = running[i];
                if (c.done || !Ready(c, phase)) continue;
                Advance(c);
            }
            for (int i = running.Count - 1; i >= 0; i--)
                if (running[i].done) running.RemoveAt(i);
        }

        public static void Stop(Coroutine c)
        {
            if (c == null) return;
            c.done = true;
            c.stack.Clear();
        }

        public static void Stop(MonoBehaviour owner, IEnumerator routine)
        {
            foreach (Coroutine c in running)
                if (c.owner == owner && c.routine == routine) Stop(c);
        }

        public static void StopAll(MonoBehaviour owner)
        {
            foreach (Coroutine c in running)
                if (c.owner == owner) Stop(c);
            for (int i = invokes.Count - 1; i >= 0; i--)
                if (invokes[i].owner == owner) invokes.RemoveAt(i);
        }

        // ---- Invoke / InvokeRepeating: a method of the script found by name (reflection), called later

        sealed class Pending
        {
            public MonoBehaviour owner;
            public string method;
            public float at, repeat;
        }

        static readonly List<Pending> invokes = new List<Pending>();

        public static void Invoke(MonoBehaviour owner, string method, float time, float repeat)
        {
            Pending p = new Pending();
            p.owner = owner;
            p.method = method;
            p.at = Time.time + time;
            p.repeat = repeat;
            invokes.Add(p);
        }

        public static void CancelInvoke(MonoBehaviour owner, string method)
        {
            for (int i = invokes.Count - 1; i >= 0; i--)
                if (invokes[i].owner == owner && (method == null || invokes[i].method == method)) invokes.RemoveAt(i);
        }

        public static bool IsInvoking(MonoBehaviour owner, string method)
        {
            foreach (Pending p in invokes)
                if (p.owner == owner && p.method == method) return true;
            return false;
        }

        static void StepInvokes()
        {
            int n = invokes.Count;
            for (int i = 0; i < n && i < invokes.Count; i++)
            {
                Pending p = invokes[i];
                if (Time.time < p.at) continue;
                if (p.owner == null || p.owner.destroyed) { p.repeat = -2f; continue; }
                if (p.owner.isActiveAndEnabled)
                {
                    System.Reflection.MethodInfo m = p.owner.GetType().GetMethod(p.method);
                    if (m != null) m.Invoke(p.owner, null);
                    else Debug.LogError("Trying to Invoke method: " + p.owner.GetType().Name + "." + p.method + " couldn't be called.");
                }
                if (p.repeat > 0f) p.at += p.repeat;
                else p.repeat = -2f;
            }
            for (int i = invokes.Count - 1; i >= 0; i--)
                if (invokes[i].repeat == -2f) invokes.RemoveAt(i);
        }
    }
}
