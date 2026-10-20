// UnityEngine.Events: UnityAction and UnityEvent.  A UnityEvent calls its persistent calls first -- the ones wired in the scene, which the packer
// adds with AddPersistentCall: a method of a target object found by name (reflection), with the argument stored in the scene or with the event's
// own (a "dynamic" call) -- then the listeners added at run time, as Unity's does.

using System;
using System.Collections.Generic;

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
    public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);
    public delegate void UnityAction<T0, T1, T2>(T0 arg0, T1 arg1, T2 arg2);
    public delegate void UnityAction<T0, T1, T2, T3>(T0 arg0, T1 arg1, T2 arg2, T3 arg3);

    public enum UnityEventCallState { Off = 0, EditorAndRuntime = 1, RuntimeOnly = 2 }

    public enum PersistentListenerMode { EventDefined = 0, Void = 1, Object = 2, Int = 3, Float = 4, String = 5, Bool = 6 }

    public abstract class UnityEventBase
    {
        sealed class PersistentCall
        {
            public Object target;
            public string method;
            public PersistentListenerMode mode;
            public object argument;
            public UnityEventCallState state = UnityEventCallState.RuntimeOnly;
        }

        readonly List<PersistentCall> persistent = new List<PersistentCall>();

        /// <summary>A call wired in the scene (the packer writes these): `mode` EventDefined passes the event's own arguments, the others the stored one.</summary>
        public void AddPersistentCall(Object target, string methodName, PersistentListenerMode mode, object argument)
        {
            PersistentCall c = new PersistentCall();
            c.target = target;
            c.method = methodName;
            c.mode = mode;
            c.argument = argument;
            persistent.Add(c);
        }

        public int GetPersistentEventCount() { return persistent.Count; }
        public Object GetPersistentTarget(int index) { return persistent[index].target; }
        public string GetPersistentMethodName(int index) { return persistent[index].method; }
        public void SetPersistentListenerState(int index, UnityEventCallState state) { persistent[index].state = state; }
        public UnityEventCallState GetPersistentListenerState(int index) { return persistent[index].state; }

        protected void InvokePersistent(object[] eventArgs)
        {
            foreach (PersistentCall c in persistent.ToArray())
            {
                if (c.state == UnityEventCallState.Off || c.target == null || c.target.destroyed) continue;
                System.Reflection.MethodInfo m = c.target.GetType().GetMethod(c.method);
                if (m == null)
                {
                    Debug.LogError("Invalid method " + c.method + " on " + c.target.GetType().Name);
                    continue;
                }
                object[] args = c.mode == PersistentListenerMode.Void ? new object[0]
                    : c.mode == PersistentListenerMode.EventDefined ? eventArgs
                    : new object[] { c.argument };
                m.Invoke(c.target, args);
            }
        }

        public abstract void RemoveAllListeners();
    }

    [Serializable]
    public class UnityEvent : UnityEventBase
    {
        readonly List<UnityAction> calls = new List<UnityAction>();

        public void AddListener(UnityAction call) { calls.Add(call); }
        public void RemoveListener(UnityAction call) { calls.Remove(call); }
        public override void RemoveAllListeners() { calls.Clear(); }

        public void Invoke()
        {
            InvokePersistent(new object[0]);
            foreach (UnityAction c in calls.ToArray()) c();
        }
    }

    [Serializable]
    public class UnityEvent<T0> : UnityEventBase
    {
        readonly List<UnityAction<T0>> calls = new List<UnityAction<T0>>();

        public void AddListener(UnityAction<T0> call) { calls.Add(call); }
        public void RemoveListener(UnityAction<T0> call) { calls.Remove(call); }
        public override void RemoveAllListeners() { calls.Clear(); }

        public void Invoke(T0 arg0)
        {
            InvokePersistent(new object[] { arg0 });
            foreach (UnityAction<T0> c in calls.ToArray()) c(arg0);
        }
    }

    [Serializable]
    public class UnityEvent<T0, T1> : UnityEventBase
    {
        readonly List<UnityAction<T0, T1>> calls = new List<UnityAction<T0, T1>>();

        public void AddListener(UnityAction<T0, T1> call) { calls.Add(call); }
        public void RemoveListener(UnityAction<T0, T1> call) { calls.Remove(call); }
        public override void RemoveAllListeners() { calls.Clear(); }

        public void Invoke(T0 arg0, T1 arg1)
        {
            InvokePersistent(new object[] { arg0, arg1 });
            foreach (UnityAction<T0, T1> c in calls.ToArray()) c(arg0, arg1);
        }
    }

    [Serializable]
    public class UnityEvent<T0, T1, T2> : UnityEventBase
    {
        readonly List<UnityAction<T0, T1, T2>> calls = new List<UnityAction<T0, T1, T2>>();

        public void AddListener(UnityAction<T0, T1, T2> call) { calls.Add(call); }
        public void RemoveListener(UnityAction<T0, T1, T2> call) { calls.Remove(call); }
        public override void RemoveAllListeners() { calls.Clear(); }

        public void Invoke(T0 arg0, T1 arg1, T2 arg2)
        {
            InvokePersistent(new object[] { arg0, arg1, arg2 });
            foreach (UnityAction<T0, T1, T2> c in calls.ToArray()) c(arg0, arg1, arg2);
        }
    }
}
