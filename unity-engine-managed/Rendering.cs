// UnityEngine's rendering assets: Color32, Texture / Texture2D (pixels held here; a texture the host draws is named by the host's index),
// Sprite (a rectangle of a texture, with its pivot and pixels per unit), Material and Shader (properties kept by name; there are no shaders to run).

using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Color32 : IEquatable<Color32>
    {
        public byte r, g, b, a;

        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }

        public static implicit operator Color32(Color c)
        {
            return new Color32((byte)Mathf.Round(Mathf.Clamp01(c.r) * 255f), (byte)Mathf.Round(Mathf.Clamp01(c.g) * 255f),
                (byte)Mathf.Round(Mathf.Clamp01(c.b) * 255f), (byte)Mathf.Round(Mathf.Clamp01(c.a) * 255f));
        }
        public static implicit operator Color(Color32 c) { return new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f); }

        public static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32((byte)(a.r + (b.r - a.r) * t), (byte)(a.g + (b.g - a.g) * t), (byte)(a.b + (b.b - a.b) * t), (byte)(a.a + (b.a - a.a) * t));
        }

        public bool Equals(Color32 o) { return r == o.r && g == o.g && b == o.b && a == o.a; }
        public override bool Equals(object other) { return other is Color32 && Equals((Color32)other); }
        public override int GetHashCode() { return r | (g << 8) | (b << 16) | (a << 24); }
        public override string ToString() { return "RGBA(" + r + ", " + g + ", " + b + ", " + a + ")"; }
    }

    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, RGBAFloat = 20 }

    public class Texture : Object
    {
        /// <summary>The host's index of this texture (-1 for one made at run time).</summary>
        public int hostId = -1;
        protected int w, h;

        public virtual int width { get { return w; } set { w = value; } }
        public virtual int height { get { return h; } set { h = value; } }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; }
        public Vector2 texelSize { get { return new Vector2(1f / Math.Max(1, w), 1f / Math.Max(1, h)); } }
    }

    public class Texture2D : Texture
    {
        Color32[] pixels;

        public TextureFormat format { get; private set; }
        public bool isReadable { get { return true; } }

        public Texture2D(int width, int height) : this(width, height, TextureFormat.RGBA32, true) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain)
        {
            w = width;
            h = height;
            format = textureFormat;
            pixels = new Color32[Math.Max(0, width * height)];
            filterMode = FilterMode.Bilinear;
        }

        public static Texture2D whiteTexture { get { Texture2D t = new Texture2D(4, 4); t.SetPixels32(Fill(16, new Color32(255, 255, 255, 255))); return t; } }
        public static Texture2D blackTexture { get { Texture2D t = new Texture2D(4, 4); t.SetPixels32(Fill(16, new Color32(0, 0, 0, 255))); return t; } }

        static Color32[] Fill(int n, Color32 c)
        {
            Color32[] a = new Color32[n];
            for (int i = 0; i < n; i++) a[i] = c;
            return a;
        }

        public Color GetPixel(int x, int y)
        {
            x = Mathf.Clamp(x, 0, w - 1);
            y = Mathf.Clamp(y, 0, h - 1);
            return pixels[y * w + x];
        }
        public Color GetPixelBilinear(float u, float v) { return GetPixel(Mathf.FloorToInt(u * w), Mathf.FloorToInt(v * h)); }
        public void SetPixel(int x, int y, Color color) { if (x >= 0 && y >= 0 && x < w && y < h) pixels[y * w + x] = color; }
        public Color[] GetPixels()
        {
            Color[] a = new Color[pixels.Length];
            for (int i = 0; i < a.Length; i++) a[i] = pixels[i];
            return a;
        }
        public Color[] GetPixels(int x, int y, int blockWidth, int blockHeight)
        {
            Color[] a = new Color[blockWidth * blockHeight];
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++) a[j * blockWidth + i] = GetPixel(x + i, y + j);
            return a;
        }
        public void SetPixels(Color[] colors)
        {
            for (int i = 0; i < colors.Length && i < pixels.Length; i++) pixels[i] = colors[i];
        }
        public void SetPixels(int x, int y, int blockWidth, int blockHeight, Color[] colors)
        {
            for (int j = 0; j < blockHeight; j++)
                for (int i = 0; i < blockWidth; i++) SetPixel(x + i, y + j, colors[j * blockWidth + i]);
        }
        public Color32[] GetPixels32() { return (Color32[])pixels.Clone(); }
        public void SetPixels32(Color32[] colors)
        {
            for (int i = 0; i < colors.Length && i < pixels.Length; i++) pixels[i] = colors[i];
        }
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
        public bool Reinitialize(int width, int height)
        {
            w = width;
            h = height;
            pixels = new Color32[width * height];
            return true;
        }
    }

    public enum SpriteMeshType { FullRect = 0, Tight = 1 }
    public enum SpritePackingMode { Tight = 0, Rectangle = 1 }
    public enum SpritePackingRotation { None = 0, FlipHorizontal = 1, FlipVertical = 2, Rotate180 = 3, Any = 15 }

    /// <summary>A rectangle of a texture (in pixels), with its pivot (pixels from the rectangle's bottom left) and how many pixels make a unit.</summary>
    public class Sprite : Object
    {
        public Texture2D texture { get; private set; }
        public Rect rect { get; private set; }
        public Rect textureRect { get { return rect; } }
        public Vector2 pivot { get; private set; }
        public float pixelsPerUnit { get; private set; }
        public Vector4 border { get; private set; }
        public bool packed { get { return false; } }
        public SpritePackingMode packingMode { get { return SpritePackingMode.Rectangle; } }
        public SpritePackingRotation packingRotation { get { return SpritePackingRotation.None; } }

        /// <summary>Its size in world units, centred on the pivot.</summary>
        public Bounds bounds
        {
            get
            {
                Vector3 size = new Vector3(rect.width / pixelsPerUnit, rect.height / pixelsPerUnit, 0.2f);
                Vector3 center = new Vector3((rect.width / 2f - pivot.x) / pixelsPerUnit, (rect.height / 2f - pivot.y) / pixelsPerUnit, 0f);
                return new Bounds(center, size);
            }
        }

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot) { return Create(texture, rect, pivot, 100f); }
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit)
        {
            return Create(texture, rect, pivot, pixelsPerUnit, 0, SpriteMeshType.Tight, Vector4.zero);
        }
        public Vector2 textureRectOffset { get { return Vector2.zero; } }
        public Vector2[] vertices { get; private set; }
        public ushort[] triangles { get; private set; }
        public Vector2[] uv { get { return vertices; } }

        /// <summary>Gives the sprite its own outline (vertices in pixels from the rectangle's bottom left); the host draws the rectangle still.</summary>
        public void OverrideGeometry(Vector2[] vertices, ushort[] triangles) { this.vertices = vertices; this.triangles = triangles; }

        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude)
        {
            return Create(texture, rect, pivot, pixelsPerUnit, extrude, SpriteMeshType.Tight, Vector4.zero);
        }
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType)
        {
            return Create(texture, rect, pivot, pixelsPerUnit, extrude, meshType, Vector4.zero);
        }
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit, uint extrude, SpriteMeshType meshType, Vector4 border)
        {
            Sprite s = new Sprite();
            s.texture = texture;
            s.rect = rect;
            s.pivot = new Vector2(pivot.x * rect.width, pivot.y * rect.height);   // given normalised, kept in pixels as Unity reports it
            s.pixelsPerUnit = pixelsPerUnit;
            s.border = border;
            s.name = "";
            return s;
        }
    }

    public class Shader : Object
    {
        static readonly Dictionary<string, int> ids = new Dictionary<string, int>();

        public static Shader Find(string name) { Shader s = new Shader(); s.name = name; return s; }

        public static int PropertyToID(string name)
        {
            int id;
            if (!ids.TryGetValue(name, out id)) { id = ids.Count + 1; ids[name] = id; }
            return id;
        }
    }

    /// <summary>A material: its properties by name (or the id of the name), kept; nothing draws with them yet but its color and main texture.</summary>
    public class Material : Object
    {
        readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();
        readonly Dictionary<string, Vector4> vectors = new Dictionary<string, Vector4>();

        public Shader shader { get; set; }
        public int renderQueue { get; set; }

        public Material(Shader shader) { this.shader = shader; name = shader == null ? "" : shader.name; colors["_Color"] = Color.white; }
        public Material(Material source)
        {
            shader = source.shader;
            name = source.name;
            foreach (KeyValuePair<string, float> kv in source.floats) floats[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, Color> kv in source.colors) colors[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, Texture> kv in source.textures) textures[kv.Key] = kv.Value;
            foreach (KeyValuePair<string, Vector4> kv in source.vectors) vectors[kv.Key] = kv.Value;
        }

        public Color color { get { return GetColor("_Color"); } set { SetColor("_Color", value); } }
        public Texture mainTexture { get { return GetTexture("_MainTex"); } set { SetTexture("_MainTex", value); } }

        static string Named(int id)
        {
            return "#" + id;
        }

        public bool HasProperty(string name) { return floats.ContainsKey(name) || colors.ContainsKey(name) || textures.ContainsKey(name) || vectors.ContainsKey(name); }
        public bool HasProperty(int id) { return HasProperty(Named(id)); }
        public float GetFloat(string name) { float v; return floats.TryGetValue(name, out v) ? v : 0f; }
        public float GetFloat(int id) { return GetFloat(Named(id)); }
        public void SetFloat(string name, float value) { floats[name] = value; }
        public void SetFloat(int id, float value) { SetFloat(Named(id), value); }
        public int GetInt(string name) { return (int)GetFloat(name); }
        public void SetInt(string name, int value) { SetFloat(name, value); }
        public Color GetColor(string name) { Color v; return colors.TryGetValue(name, out v) ? v : Color.white; }
        public Color GetColor(int id) { return GetColor(Named(id)); }
        public void SetColor(string name, Color value) { colors[name] = value; }
        public void SetColor(int id, Color value) { SetColor(Named(id), value); }
        public Vector4 GetVector(string name) { Vector4 v; return vectors.TryGetValue(name, out v) ? v : Vector4.zero; }
        public void SetVector(string name, Vector4 value) { vectors[name] = value; }
        public void SetVector(int id, Vector4 value) { SetVector(Named(id), value); }
        public Texture GetTexture(string name) { Texture v; return textures.TryGetValue(name, out v) ? v : null; }
        public Texture GetTexture(int id) { return GetTexture(Named(id)); }
        public void SetTexture(string name, Texture value) { textures[name] = value; }
        public void SetTexture(int id, Texture value) { SetTexture(Named(id), value); }
        readonly Dictionary<string, string> tags = new Dictionary<string, string>();
        readonly Dictionary<string, bool> passes = new Dictionary<string, bool>();
        public void SetOverrideTag(string tag, string val) { tags[tag] = val; }
        public string GetTag(string tag, bool searchFallbacks) { string v; return tags.TryGetValue(tag, out v) ? v : ""; }
        public void SetShaderPassEnabled(string passName, bool enabled) { passes[passName] = enabled; }
        public bool GetShaderPassEnabled(string passName) { bool v; return !passes.TryGetValue(passName, out v) || v; }
        public void EnableKeyword(string keyword) { }
        public void DisableKeyword(string keyword) { }
        public bool IsKeywordEnabled(string keyword) { return false; }
    }

    /// <summary>The project's sorting layers, in draw order (ProjectSettings/TagManager.asset: the packer sets the names and ids).</summary>
    public struct SortingLayer
    {
        public static string[] names = { "Default" };
        public static int[] ids = { 0 };

        int index;

        public int id { get { return IndexToID(index); } }
        public string name { get { return IDToName(id); } }
        public int value { get { return index; } }

        public static SortingLayer[] layers
        {
            get
            {
                SortingLayer[] a = new SortingLayer[names.Length];
                for (int i = 0; i < a.Length; i++) a[i].index = i;
                return a;
            }
        }

        public static int IndexToID(int i) { return i >= 0 && i < ids.Length ? ids[i] : 0; }
        public static int IDToIndex(int id)
        {
            for (int i = 0; i < ids.Length; i++) if (ids[i] == id) return i;
            return 0;
        }
        public static string IDToName(int id) { int i = IDToIndex(id); return i < names.Length ? names[i] : ""; }
        public static int NameToID(string name)
        {
            for (int i = 0; i < names.Length; i++) if (names[i] == name) return IndexToID(i);
            return 0;
        }
        public static int GetLayerValueFromID(int id) { return IDToIndex(id); }
        public static int GetLayerValueFromName(string name) { return IDToIndex(NameToID(name)); }
        public static bool IsValid(int id) { for (int i = 0; i < ids.Length; i++) if (ids[i] == id) return true; return false; }
    }

    public enum MotionVectorGenerationMode { Camera = 0, Object = 1, ForceNoMotion = 2 }
    public enum SpriteDrawMode { Simple = 0, Sliced = 1, Tiled = 2 }
    public enum SpriteMaskInteraction { None = 0, VisibleInsideMask = 1, VisibleOutsideMask = 2 }
    public enum SpriteSortPoint { Center = 0, Pivot = 1 }
    public enum LineAlignment { View = 0, TransformZ = 1 }
    public enum LineTextureMode { Stretch = 0, Tile = 1, DistributePerSegment = 2, RepeatPerSegment = 3 }

    /// <summary>A line through points (drawn by nothing yet: the positions, colours and widths are kept).</summary>
    public class LineRenderer : Renderer
    {
        Vector3[] positions = new Vector3[2];

        public bool useWorldSpace = true;
        public bool loop;
        public Color startColor = Color.white;
        public Color endColor = Color.white;
        public float startWidth = 1f;
        public float endWidth = 1f;
        public float widthMultiplier = 1f;
        public int numCornerVertices;
        public int numCapVertices;
        public LineAlignment alignment;
        public LineTextureMode textureMode;

        public override void OnAttach() { enabled = true; }

        public int positionCount
        {
            get { return positions.Length; }
            set { Vector3[] p = new Vector3[Math.Max(0, value)]; Array.Copy(positions, p, Math.Min(positions.Length, p.Length)); positions = p; }
        }

        public void SetPosition(int index, Vector3 position) { positions[index] = position; }
        public Vector3 GetPosition(int index) { return positions[index]; }
        public void SetPositions(Vector3[] newPositions)
        {
            for (int i = 0; i < newPositions.Length && i < positions.Length; i++) positions[i] = newPositions[i];
        }
        public int GetPositions(Vector3[] result)
        {
            int n = Math.Min(result.Length, positions.Length);
            Array.Copy(positions, result, n);
            return n;
        }

        public override Bounds bounds
        {
            get
            {
                if (positions.Length == 0) return new Bounds(transform.position, Vector3.zero);
                Vector3 first = useWorldSpace ? positions[0] : transform.TransformPoint(positions[0]);
                Bounds b = new Bounds(first, Vector3.zero);
                foreach (Vector3 p in positions) b.Encapsulate(useWorldSpace ? p : transform.TransformPoint(p));
                return b;
            }
        }
    }

    public class Mesh : Object
    {
        public Vector3[] vertices = new Vector3[0];
        public int[] triangles = new int[0];
        public Vector2[] uv = new Vector2[0];
        public Vector3[] normals = new Vector3[0];
        public Color[] colors = new Color[0];
        public int subMeshCount = 1;
        public Bounds bounds;

        public Mesh() { name = ""; }

        public int vertexCount { get { return vertices.Length; } }
        public void Clear() { vertices = new Vector3[0]; triangles = new int[0]; uv = new Vector2[0]; normals = new Vector3[0]; colors = new Color[0]; }
        public void Clear(bool keepVertexLayout) { Clear(); }
        public void MarkDynamic() { }
        public void UploadMeshData(bool markNoLongerReadable) { }
        public void SetVertices(List<Vector3> inVertices) { vertices = inVertices.ToArray(); }
        public void SetTriangles(int[] tris, int submesh) { triangles = tris; }
        public void SetTriangles(List<int> tris, int submesh) { triangles = tris.ToArray(); }
        public int[] GetTriangles(int submesh) { return triangles; }
        public void SetUVs(int channel, List<Vector2> uvs) { if (channel == 0) uv = uvs.ToArray(); }
        public void SetColors(List<Color> inColors) { colors = inColors.ToArray(); }
        public void SetNormals(List<Vector3> inNormals) { normals = inNormals.ToArray(); }

        public void RecalculateBounds()
        {
            if (vertices.Length == 0) { bounds = new Bounds(Vector3.zero, Vector3.zero); return; }
            Bounds b = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 v in vertices) b.Encapsulate(v);
            bounds = b;
        }

        /// <summary>Each vertex's normal: the sum of its triangles' face normals, normalised.</summary>
        public void RecalculateNormals()
        {
            Vector3[] n = new Vector3[vertices.Length];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                Vector3 face = Vector3.Cross(b - a, c - a);
                n[triangles[i]] += face; n[triangles[i + 1]] += face; n[triangles[i + 2]] += face;
            }
            for (int i = 0; i < n.Length; i++) n[i] = n[i].normalized;
            normals = n;
        }

        public void RecalculateTangents() { }
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh;
        public Mesh mesh { get { if (sharedMesh == null) sharedMesh = new Mesh(); return sharedMesh; } set { sharedMesh = value; } }
    }

    public class MeshRenderer : Renderer
    {
        public override void OnAttach() { enabled = true; }
    }

    /// <summary>The editor's drawing aids: nothing draws them in a player.</summary>
    public static class Gizmos
    {
        public static Color color = Color.white;
        public static Matrix4x4 matrix = Matrix4x4.identity;
        public static void DrawLine(Vector3 from, Vector3 to) { }
        public static void DrawRay(Vector3 from, Vector3 direction) { }
        public static void DrawRay(Ray r) { }
        public static void DrawWireCube(Vector3 center, Vector3 size) { }
        public static void DrawCube(Vector3 center, Vector3 size) { }
        public static void DrawWireSphere(Vector3 center, float radius) { }
        public static void DrawSphere(Vector3 center, float radius) { }
        public static void DrawIcon(Vector3 center, string name) { }
        public static void DrawIcon(Vector3 center, string name, bool allowScaling) { }
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale) { }
        public static void DrawWireMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale) { }
    }

    /// <summary>Assets loaded by path.  The packer copies no Resources folder yet, so Load finds nothing (null, as Unity's does for a missing path).</summary>
    public static class Resources
    {
        public static Object Load(string path) { return null; }
        public static T Load<T>(string path) where T : Object { return null; }
        public static T[] LoadAll<T>(string path) where T : Object { return new T[0]; }
        public static void UnloadAsset(Object assetToUnload) { }
        public static AsyncOperation UnloadUnusedAssets() { AsyncOperation op = new AsyncOperation(); op.Finish(); return op; }
        public static T[] FindObjectsOfTypeAll<T>() where T : Object { return Object.FindObjectsOfType<T>(true); }
    }

    public enum CursorLockMode { None = 0, Locked = 1, Confined = 2 }
    public enum CursorMode { Auto = 0, ForceSoftware = 1 }

    public static class Cursor
    {
        public static bool visible = true;
        public static CursorLockMode lockState;
        public static void SetCursor(Texture2D texture, Vector2 hotspot, CursorMode cursorMode) { }
    }

    public static class QualitySettings
    {
        public static int globalTextureMipmapLimit;
        public static int vSyncCount = 1;
        public static int antiAliasing;
        public static int pixelLightCount = 4;
        public static float shadowDistance = 150f;
        public static float lodBias = 1f;
        public static string[] names = { "Default" };
        static int level;
        public static int GetQualityLevel() { return level; }
        public static void SetQualityLevel(int index) { level = index; }
        public static void SetQualityLevel(int index, bool applyExpensiveChanges) { level = index; }
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
    public enum BlendMode { Zero = 0, One = 1, DstColor = 2, SrcColor = 3, OneMinusDstColor = 4, SrcAlpha = 5, OneMinusSrcColor = 6, DstAlpha = 7,
        OneMinusDstAlpha = 8, SrcAlphaSaturate = 9, OneMinusSrcAlpha = 10 }
    public enum RenderQueue { Background = 1000, Geometry = 2000, AlphaTest = 2450, GeometryLast = 2500, Transparent = 3000, Overlay = 4000 }
    public enum CompareFunction { Disabled = 0, Never = 1, Less = 2, Equal = 3, LessEqual = 4, Greater = 5, NotEqual = 6, GreaterEqual = 7, Always = 8 }

    public class SortingGroup : Behaviour
    {
        public int sortingOrder;
        public int sortingLayerID;
        public string sortingLayerName = "Default";
    }
}

namespace UnityEngine.Rendering.Universal
{
    /// <summary>URP's 2D light (lighting is not drawn: the fields are kept).</summary>
    public class Light2D : MonoBehaviour
    {
        public enum LightType { Parametric = 0, Freeform = 1, Sprite = 2, Point = 3, Global = 4 }

        public LightType lightType = LightType.Point;
        public Color color = Color.white;
        public float intensity = 1f;
        public float falloffIntensity = 0.5f;
        public float shapeLightFalloffSize = 0.5f;
        public float pointLightInnerRadius;
        public float pointLightOuterRadius = 1f;
        public float pointLightInnerAngle = 360f;
        public float pointLightOuterAngle = 360f;
        public int blendStyleIndex;
        public bool shadowsEnabled { get; set; }
        public float shadowIntensity { get; set; }
    }

    public class ShadowCaster2D : MonoBehaviour
    {
        public bool castsShadows = true;
        public bool selfShadows;
        public bool useRendererSilhouette = true;
    }
}
