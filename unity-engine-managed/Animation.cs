// UnityEngine's Animator.  The packer loads no animation clips yet, so nothing moves; the state machine's bookkeeping is Unity's: Play goes to a
// state (by name or hash, Animator.StringToHash's), normalizedTime advances with Time.deltaTime and speed (over a 1-second state: the clips'
// lengths are not known), and the parameters keep what is set.

using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum AnimatorUpdateMode { Normal = 0, AnimatePhysics = 1, UnscaledTime = 2 }
    public enum AnimatorCullingMode { AlwaysAnimate = 0, CullUpdateTransforms = 1, CullCompletely = 2 }

    public class AnimationClip : Object
    {
        public float length = 1f;
        public float frameRate = 60f;
        public bool isLooping;
    }

    public class RuntimeAnimatorController : Object
    {
        public AnimationClip[] animationClips = new AnimationClip[0];
    }

    public struct AnimatorStateInfo
    {
        internal int nameHash;
        internal float time;
        internal float stateSpeed;

        public int shortNameHash { get { return nameHash; } }
        public int fullPathHash { get { return nameHash; } }
        public float normalizedTime { get { return time; } }
        public float length { get { return 1f; } }
        public float speed { get { return stateSpeed; } }
        public float speedMultiplier { get { return 1f; } }
        public bool loop { get { return false; } }
        public bool IsName(string name) { return Animator.StringToHash(name) == nameHash || Animator.StringToHash("Base Layer." + name) == nameHash; }
        public bool IsTag(string tag) { return false; }
    }

    public class Animator : Behaviour
    {
        public RuntimeAnimatorController runtimeAnimatorController;
        public float speed = 1f;
        public bool applyRootMotion;
        public AnimatorUpdateMode updateMode;
        public AnimatorCullingMode cullingMode;
        public bool keepAnimatorStateOnDisable;

        readonly Dictionary<int, float> floats = new Dictionary<int, float>();
        readonly Dictionary<int, int> ints = new Dictionary<int, int>();
        readonly Dictionary<int, bool> bools = new Dictionary<int, bool>();
        readonly HashSet<int> triggers = new HashSet<int>();
        int state;
        float stateTime;
        float lastFrame = -1f;

        /// <summary>Unity's hash of a state or parameter name (CRC-32 of its UTF-8 bytes, as an int).</summary>
        public static int StringToHash(string name)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (char ch in name ?? "")
            {
                int c = ch;
                int[] bytes = c < 0x80 ? new int[] { c } : c < 0x800 ? new int[] { 0xC0 | (c >> 6), 0x80 | (c & 0x3F) }
                    : new int[] { 0xE0 | (c >> 12), 0x80 | ((c >> 6) & 0x3F), 0x80 | (c & 0x3F) };
                foreach (int b in bytes)
                {
                    crc ^= (uint)b;
                    for (int k = 0; k < 8; k++) crc = (crc & 1u) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }
            return (int)(crc ^ 0xFFFFFFFFu);
        }

        public bool isInitialized { get { return true; } }
        public int layerCount { get { return 1; } }
        public bool hasBoundPlayables { get { return runtimeAnimatorController != null; } }

        void Advance()
        {
            float now = Time.time;
            if (lastFrame >= 0f && now > lastFrame) stateTime += (now - lastFrame) * speed;
            lastFrame = now;
        }

        public void Play(string stateName) { Play(StringToHash(stateName), -1, float.NegativeInfinity); }
        public void Play(string stateName, int layer) { Play(StringToHash(stateName), layer, float.NegativeInfinity); }
        public void Play(string stateName, int layer, float normalizedTime) { Play(StringToHash(stateName), layer, normalizedTime); }
        public void Play(int stateNameHash) { Play(stateNameHash, -1, float.NegativeInfinity); }
        public void Play(int stateNameHash, int layer) { Play(stateNameHash, layer, float.NegativeInfinity); }

        /// <summary>Goes to the state (normalizedTime -infinity: from its start if it is another state, where it is if it is this one).</summary>
        public void Play(int stateNameHash, int layer, float normalizedTime)
        {
            Advance();
            if (stateNameHash != state || !float.IsNegativeInfinity(normalizedTime)) stateTime = float.IsNegativeInfinity(normalizedTime) ? 0f : normalizedTime;
            state = stateNameHash;
        }

        public void PlayInFixedTime(string stateName, int layer, float fixedTime) { Play(StringToHash(stateName), layer, fixedTime); }
        public void CrossFade(string stateName, float normalizedTransitionDuration) { Play(stateName); }
        public void CrossFade(string stateName, float normalizedTransitionDuration, int layer) { Play(stateName, layer); }
        public void CrossFade(int stateHashName, float normalizedTransitionDuration) { Play(stateHashName); }
        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration) { Play(stateName); }

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layerIndex)
        {
            Advance();
            AnimatorStateInfo i = new AnimatorStateInfo();
            i.nameHash = state;
            i.time = stateTime;
            i.stateSpeed = speed;
            return i;
        }

        public AnimatorStateInfo GetNextAnimatorStateInfo(int layerIndex) { return new AnimatorStateInfo(); }
        public bool IsInTransition(int layerIndex) { return false; }
        public float GetLayerWeight(int layerIndex) { return layerIndex == 0 ? 1f : 0f; }
        public void SetLayerWeight(int layerIndex, float weight) { }
        public int GetLayerIndex(string layerName) { return layerName == "Base Layer" ? 0 : -1; }
        public string GetLayerName(int layerIndex) { return layerIndex == 0 ? "Base Layer" : ""; }

        public void SetFloat(string name, float value) { floats[StringToHash(name)] = value; }
        public void SetFloat(int id, float value) { floats[id] = value; }
        public void SetFloat(string name, float value, float dampTime, float deltaTime) { SetFloat(name, value); }
        public float GetFloat(string name) { return GetFloat(StringToHash(name)); }
        public float GetFloat(int id) { float v; return floats.TryGetValue(id, out v) ? v : 0f; }
        public void SetInteger(string name, int value) { ints[StringToHash(name)] = value; }
        public void SetInteger(int id, int value) { ints[id] = value; }
        public int GetInteger(string name) { return GetInteger(StringToHash(name)); }
        public int GetInteger(int id) { int v; return ints.TryGetValue(id, out v) ? v : 0; }
        public void SetBool(string name, bool value) { bools[StringToHash(name)] = value; }
        public void SetBool(int id, bool value) { bools[id] = value; }
        public bool GetBool(string name) { return GetBool(StringToHash(name)); }
        public bool GetBool(int id) { bool v; return bools.TryGetValue(id, out v) && v; }
        public void SetTrigger(string name) { triggers.Add(StringToHash(name)); }
        public void SetTrigger(int id) { triggers.Add(id); }
        public void ResetTrigger(string name) { triggers.Remove(StringToHash(name)); }
        public void ResetTrigger(int id) { triggers.Remove(id); }

        public void Rebind() { state = 0; stateTime = 0f; floats.Clear(); ints.Clear(); bools.Clear(); triggers.Clear(); }
        public void Update(float deltaTime) { stateTime += deltaTime * speed; }
        public void StopPlayback() { }
        public void StartPlayback() { }
    }

    /// <summary>A behaviour of an animator state (its callbacks are never called: there is no state machine running).</summary>
    public abstract class StateMachineBehaviour : ScriptableObject
    {
        public virtual void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
        public virtual void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) { }
    }
}
