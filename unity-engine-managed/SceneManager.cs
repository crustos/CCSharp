// UnityEngine.SceneManagement: the build's scenes (their names, in build order: the packer sets SceneManager.sceneNames), loaded the way the engine
// loads a scene: at the end of the frame every root object that is not DontDestroyOnLoad goes and the scene's loader builds the new one.
// Additive loading is not supported: LoadSceneMode.Additive loads as Single.

using System;

namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single = 0, Additive = 1 }

    public struct Scene
    {
        internal int index;

        public int buildIndex { get { return index; } }
        public string name { get { return index >= 0 && index < SceneManager.sceneNames.Length ? SceneManager.sceneNames[index] : ""; } }
        public string path { get { return "Assets/" + name + ".unity"; } }
        public bool isLoaded { get { return index == Engine.activeScene; } }
        public bool IsValid() { return index >= 0 && index < SceneManager.sceneNames.Length; }
        public int rootCount { get { return GetRootGameObjects().Length; } }

        public GameObject[] GetRootGameObjects()
        {
            System.Collections.Generic.List<GameObject> roots = new System.Collections.Generic.List<GameObject>();
            foreach (GameObject g in Engine.objects)
                if (!g.destroyed && g.transform.parent == null) roots.Add(g);
            return roots.ToArray();
        }

        public static bool operator ==(Scene a, Scene b) { return a.index == b.index; }
        public static bool operator !=(Scene a, Scene b) { return a.index != b.index; }
        public override bool Equals(object other) { return other is Scene && ((Scene)other).index == index; }
        public override int GetHashCode() { return index; }
    }

    public static class SceneManager
    {
        public static string[] sceneNames = { "" };

        public static event Events.UnityAction<Scene, LoadSceneMode> sceneLoaded;
        public static event Events.UnityAction<Scene> sceneUnloaded;
        public static event Events.UnityAction<Scene, Scene> activeSceneChanged;

        static AsyncOperation pending;

        public static int sceneCount { get { return 1; } }
        public static int sceneCountInBuildSettings { get { return sceneNames.Length; } }

        static Scene At(int index) { Scene s; s.index = index; return s; }

        public static Scene GetActiveScene() { return At(Engine.activeScene); }
        public static Scene GetSceneAt(int index) { return At(Engine.activeScene); }
        public static Scene GetSceneByBuildIndex(int buildIndex) { return At(buildIndex); }
        public static Scene GetSceneByName(string name) { return At(IndexOf(name)); }
        public static bool SetActiveScene(Scene scene) { return scene.index == Engine.activeScene; }

        static int IndexOf(string name)
        {
            for (int i = 0; i < sceneNames.Length; i++)
                if (sceneNames[i] == name || "Assets/" + sceneNames[i] + ".unity" == name) return i;
            return -1;
        }

        public static void LoadScene(int sceneBuildIndex) { LoadScene(sceneBuildIndex, LoadSceneMode.Single); }
        public static void LoadScene(string sceneName) { LoadScene(sceneName, LoadSceneMode.Single); }
        public static void LoadScene(string sceneName, LoadSceneMode mode)
        {
            int i = IndexOf(sceneName);
            if (i < 0) { Debug.LogError("Scene '" + sceneName + "' couldn't be loaded because it has not been added to the build settings"); return; }
            LoadScene(i, mode);
        }
        public static void LoadScene(int sceneBuildIndex, LoadSceneMode mode)
        {
            if (sceneBuildIndex < 0 || sceneBuildIndex >= sceneNames.Length)
            {
                Debug.LogError("Scene with build index " + sceneBuildIndex + " couldn't be loaded because it has not been added to the build settings");
                return;
            }
            if (sceneUnloaded != null) sceneUnloaded(At(Engine.activeScene));
            Engine.LoadScene(sceneBuildIndex);
        }

        /// <summary>Loads at the end of this frame (there is no streaming here): the operation is done once the scene is built.</summary>
        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex) { return LoadSceneAsync(sceneBuildIndex, LoadSceneMode.Single); }
        public static AsyncOperation LoadSceneAsync(string sceneName) { return LoadSceneAsync(IndexOf(sceneName), LoadSceneMode.Single); }
        public static AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode) { return LoadSceneAsync(IndexOf(sceneName), mode); }
        public static AsyncOperation LoadSceneAsync(int sceneBuildIndex, LoadSceneMode mode)
        {
            pending = new AsyncOperation();
            LoadScene(sceneBuildIndex, mode);
            return pending;
        }

        /// <summary>The scene is single here, so unloading one is a no-op that finishes at once.</summary>
        public static AsyncOperation UnloadSceneAsync(int sceneBuildIndex) { AsyncOperation op = new AsyncOperation(); op.Finish(); return op; }
        public static AsyncOperation UnloadSceneAsync(string sceneName) { return UnloadSceneAsync(IndexOf(sceneName)); }
        public static AsyncOperation UnloadSceneAsync(Scene scene) { return UnloadSceneAsync(scene.index); }

        /// <summary>The engine has built scene `index` (after LoadScene).</summary>
        public static void Loaded(int index)
        {
            Scene s = At(index);
            if (sceneLoaded != null) sceneLoaded(s, LoadSceneMode.Single);
            if (activeSceneChanged != null) activeSceneChanged(s, s);
            if (pending != null) { AsyncOperation op = pending; pending = null; op.Finish(); }
        }
    }
}
