// UnityEngine's audio: AudioClip, AudioSource and AudioListener.  Nothing is heard yet (the host has no sound), but playback is timed as Unity's
// is -- in real time, at the source's pitch -- so isPlaying and time read as a game expects: a clip that is not looped stops when it has played
// its length.  playOnAwake plays it when the source is attached to an active object.

using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum AudioDataLoadState { Unloaded = 0, Loading = 1, Loaded = 2, Failed = 3 }
    public enum AudioRolloffMode { Logarithmic = 0, Linear = 1, Custom = 2 }

    public class AudioClip : Object
    {
        /// <summary>The host's index of the clip's sound (-1 for none).</summary>
        public int hostId = -1;
        float m_Length;
        int m_Samples, m_Channels = 1, m_Frequency = 44100;
        float[] data;

        public float length { get { return m_Length; } }
        public int samples { get { return m_Samples; } }
        public int channels { get { return m_Channels; } }
        public int frequency { get { return m_Frequency; } }
        public AudioDataLoadState loadState { get { return AudioDataLoadState.Loaded; } }
        public bool preloadAudioData { get { return true; } }
        public bool LoadAudioData() { return true; }
        public bool UnloadAudioData() { return true; }

        /// <summary>A clip of that many samples per channel (its length is samples / frequency).</summary>
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream)
        {
            AudioClip c = new AudioClip();
            c.name = name;
            c.m_Samples = lengthSamples;
            c.m_Channels = channels;
            c.m_Frequency = frequency;
            c.m_Length = frequency > 0 ? (float)lengthSamples / frequency : 0f;
            c.data = new float[lengthSamples * channels];
            return c;
        }

        /// <summary>A clip the packer knows from the project (its host sound and its length in seconds).</summary>
        public static AudioClip FromHost(string name, int hostId, float lengthSeconds, int frequency, int channels)
        {
            AudioClip c = Create(name, (int)(lengthSeconds * frequency), channels, frequency, false);
            c.hostId = hostId;
            return c;
        }

        public bool GetData(float[] buffer, int offsetSamples)
        {
            if (data == null) return false;
            for (int i = 0; i < buffer.Length && offsetSamples * m_Channels + i < data.Length; i++) buffer[i] = data[offsetSamples * m_Channels + i];
            return true;
        }
        public bool SetData(float[] buffer, int offsetSamples)
        {
            if (data == null) return false;
            for (int i = 0; i < buffer.Length && offsetSamples * m_Channels + i < data.Length; i++) data[offsetSamples * m_Channels + i] = buffer[i];
            return true;
        }
    }

    public class AudioSource : Behaviour
    {
        public AudioClip clip;
        public float volume = 1f;
        public float pitch = 1f;
        public bool loop;
        public bool mute;
        public bool playOnAwake = true;
        public float spatialBlend;
        public float panStereo;
        public float minDistance = 1f;
        public float maxDistance = 500f;
        public float dopplerLevel = 1f;
        public float spread;
        public float reverbZoneMix = 1f;
        public int priority = 128;
        public AudioRolloffMode rolloffMode;
        public bool ignoreListenerPause;
        public bool ignoreListenerVolume;
        public bool bypassEffects;
        public bool bypassListenerEffects;
        public bool bypassReverbZones;

        bool playing, paused;
        float startedAt, pausedAt, startOffset;

        public override void OnAttach()
        {
            if (playOnAwake && clip != null && gameObject.activeInHierarchy) Play();
        }

        static float Now { get { return Time.realtimeSinceStartup; } }

        float Elapsed
        {
            get
            {
                float t = (paused ? pausedAt : Now) - startedAt;
                return startOffset + t * Mathf.Abs(pitch);
            }
        }

        public bool isPlaying
        {
            get
            {
                if (!playing || paused || clip == null || !isActiveAndEnabled) return false;
                if (AudioListener.pause && !ignoreListenerPause) return false;
                return loop || Elapsed < clip.length;
            }
        }

        public float time
        {
            get
            {
                if (clip == null || clip.length <= 0f || !playing) return 0f;
                float e = Elapsed;
                return loop ? e % clip.length : Mathf.Min(e, clip.length);
            }
            set { startOffset = Mathf.Max(0f, value); startedAt = paused ? pausedAt : Now; }
        }

        public int timeSamples
        {
            get { return clip == null ? 0 : (int)(time * clip.frequency); }
            set { time = clip == null || clip.frequency == 0 ? 0f : (float)value / clip.frequency; }
        }

        public void Play()
        {
            playing = true;
            paused = false;
            startOffset = 0f;
            startedAt = Now;
        }

        public void PlayDelayed(float delay)
        {
            Play();
            startOffset = -delay * Mathf.Abs(pitch);
        }

        public void PlayScheduled(double time) { Play(); }
        public void Stop() { playing = false; paused = false; }
        public void Pause() { if (playing && !paused) { paused = true; pausedAt = Now; } }
        public void UnPause() { if (paused) { startedAt += Now - pausedAt; paused = false; } }

        /// <summary>Plays the clip over whatever plays (not heard yet; nothing to time either: a one-shot does not change isPlaying).</summary>
        public void PlayOneShot(AudioClip clip) { }
        public void PlayOneShot(AudioClip clip, float volumeScale) { }

        /// <summary>A source made at the point for the clip's length (as Unity's "One shot audio" object), then destroyed.</summary>
        public static void PlayClipAtPoint(AudioClip clip, Vector3 position) { PlayClipAtPoint(clip, position, 1f); }
        public static void PlayClipAtPoint(AudioClip clip, Vector3 position, float volume)
        {
            if (clip == null) return;
            GameObject g = new GameObject("One shot audio");
            g.transform.position = position;
            AudioSource s = g.AddComponent<AudioSource>();
            s.clip = clip;
            s.volume = volume;
            s.spatialBlend = 1f;
            s.Play();
            Destroy(g, clip.length * (Time.timeScale < 0.01f ? 0.01f : Time.timeScale));
        }
    }

    public class AudioListener : Behaviour
    {
        public static float volume = 1f;
        public static bool pause;
        public static void GetOutputData(float[] samples, int channel) { Array.Clear(samples, 0, samples.Length); }
        public static void GetSpectrumData(float[] samples, int channel, FFTWindow window) { Array.Clear(samples, 0, samples.Length); }
    }

    public enum FFTWindow { Rectangular = 0, Triangle = 1, Hamming = 2, Hanning = 3, Blackman = 4, BlackmanHarris = 5 }
}
