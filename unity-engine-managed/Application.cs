// UnityEngine.Application and PlayerPrefs.  The data path is Unity's on Linux, $HOME/.config/unity3d/<company>/<product> (the packer sets the two names
// from ProjectSettings); PlayerPrefs live in a file there, written at the end of a frame that changed them (and on Save), so a game that exits
// without calling Save keeps them, as Unity's do.

using System;
using System.Collections.Generic;
using System.IO;

namespace UnityEngine
{
    public enum RuntimePlatform { OSXPlayer = 1, WindowsPlayer = 2, LinuxPlayer = 13, WebGLPlayer = 17, Android = 11, IPhonePlayer = 8 }

    public static class Application
    {
        public static string companyName = "DefaultCompany";
        public static string productName = "Player";
        public static string version = "1.0";
        public static string unityVersion = "2022.3.0f1";
        public static int targetFrameRate = -1;
        public static bool runInBackground;

        public static bool isPlaying { get { return true; } }
        public static bool isEditor { get { return false; } }
        public static bool isFocused { get { return true; } }
        public static bool isMobilePlatform { get { return false; } }
        public static RuntimePlatform platform { get { return RuntimePlatform.LinuxPlayer; } }

        static string madePersistent, madeTemporary;

        /// <summary>Unity's place for a game's files, made if it is not there (Unity makes it at startup).</summary>
        public static string persistentDataPath
        {
            get
            {
                string home = Environment.GetEnvironmentVariable("HOME");
                if (string.IsNullOrEmpty(home)) home = ".";
                string path = home + "/.config/unity3d/" + companyName + "/" + productName;
                if (madePersistent != path) { System.IO.Directory.CreateDirectory(path); madePersistent = path; }
                return path;
            }
        }

        public static string dataPath { get { return "."; } }
        public static string streamingAssetsPath { get { return "./StreamingAssets"; } }
        public static string temporaryCachePath
        {
            get
            {
                string path = "/tmp/" + companyName + "/" + productName;
                if (madeTemporary != path) { System.IO.Directory.CreateDirectory(path); madeTemporary = path; }
                return path;
            }
        }

        /// <summary>Asks the host to end the program (after this frame).</summary>
        public static void Quit() { PlayerPrefs.Save(); Engine.quitRequested = true; }
        public static void Quit(int exitCode) { Quit(); }
        public static void OpenURL(string url) { }

        // the API before SceneManager
        public static int loadedLevel { get { return Engine.activeScene; } }
        public static void LoadLevel(int index) { Engine.LoadScene(index); }
        public static void LoadLevel(string name) { SceneManagement.SceneManager.LoadScene(name); }
    }

    public static class PlayerPrefs
    {
        // a value is an int, a float (kept as its bits, so it reads back exactly) or a string
        sealed class Entry
        {
            public char kind;
            public int i;
            public string s;
        }

        static Dictionary<string, Entry> prefs;
        static bool dirty;

        static string FilePath { get { return Application.persistentDataPath + "/prefs.txt"; } }

        static Dictionary<string, Entry> Prefs
        {
            get
            {
                if (prefs == null) Load();
                return prefs;
            }
        }

        static void Load()
        {
            prefs = new Dictionary<string, Entry>();
            string path = FilePath;
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllText(path).Split('\n'))
            {
                int t1 = line.IndexOf('\t');
                int t2 = t1 < 0 ? -1 : line.IndexOf('\t', t1 + 1);
                if (t1 != 1 || t2 < 0) continue;
                Entry e = new Entry();
                e.kind = line[0];
                string key = Unescape(line.Substring(t1 + 1, t2 - t1 - 1));
                string value = line.Substring(t2 + 1);
                if (e.kind == 's') e.s = Unescape(value);
                else e.i = int.Parse(value);
                prefs[key] = e;
            }
        }

        static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n"); }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    b.Append(n == 't' ? '\t' : n == 'n' ? '\n' : n);
                }
                else b.Append(c);
            }
            return b.ToString();
        }

        /// <summary>Writes the file if anything changed (the engine calls this at the end of each frame).</summary>
        public static void Flush() { if (dirty) Save(); }

        public static void Save()
        {
            if (prefs == null) return;
            dirty = false;
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.Text.StringBuilder b = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, Entry> kv in prefs)
            {
                b.Append(kv.Value.kind).Append('\t').Append(Escape(kv.Key)).Append('\t');
                b.Append(kv.Value.kind == 's' ? Escape(kv.Value.s) : kv.Value.i.ToString()).Append('\n');
            }
            File.WriteAllText(path, b.ToString());
        }

        static void Set(string key, char kind, int i, string s)
        {
            Entry e = new Entry();
            e.kind = kind;
            e.i = i;
            e.s = s;
            Prefs[key] = e;
            dirty = true;
        }

        static Entry Get(string key, char kind)
        {
            Entry e;
            if (Prefs.TryGetValue(key, out e) && e.kind == kind) return e;
            return null;
        }

        public static void SetInt(string key, int value) { Set(key, 'i', value, null); }
        public static void SetFloat(string key, float value) { Set(key, 'f', BitConverter.SingleToInt32Bits(value), null); }
        public static void SetString(string key, string value) { Set(key, 's', 0, value); }

        public static int GetInt(string key) { return GetInt(key, 0); }
        public static int GetInt(string key, int defaultValue) { Entry e = Get(key, 'i'); return e == null ? defaultValue : e.i; }
        public static float GetFloat(string key) { return GetFloat(key, 0f); }
        public static float GetFloat(string key, float defaultValue) { Entry e = Get(key, 'f'); return e == null ? defaultValue : BitConverter.Int32BitsToSingle(e.i); }
        public static string GetString(string key) { return GetString(key, ""); }
        public static string GetString(string key, string defaultValue) { Entry e = Get(key, 's'); return e == null ? defaultValue : e.s; }

        public static bool HasKey(string key) { return Prefs.ContainsKey(key); }
        public static void DeleteKey(string key) { if (Prefs.Remove(key)) dirty = true; }
        public static void DeleteAll() { Prefs.Clear(); dirty = true; }
    }
}
