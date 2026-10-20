// UnityEngine's grids and tilemaps: Grid and GridLayout (a rectangular layout's cell <-> world conversions: cell size and gap, through the
// object's transform), Tilemap (its tiles by cell; cellBounds is the bounds of the tiles it holds, as after CompressBounds), TileBase and Tile.
// A tilemap's tiles are drawn as the packer bakes them (sprites); one changed at run time is not redrawn yet.

using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class GridLayout : Behaviour
    {
        public enum CellLayout { Rectangle = 0, Hexagon = 1, Isometric = 2, IsometricZAsY = 3 }
        public enum CellSwizzle { XYZ = 0, XZY = 1, YXZ = 2, YZX = 3, ZXY = 4, ZYX = 5 }

        public virtual Vector3 cellSize { get { return new Vector3(1f, 1f, 0f); } }
        public virtual Vector3 cellGap { get { return Vector3.zero; } }
        public CellLayout cellLayout { get { return CellLayout.Rectangle; } }
        public CellSwizzle cellSwizzle { get { return CellSwizzle.XYZ; } }

        Vector3 Pitch { get { Vector3 s = cellSize, g = cellGap; return new Vector3(s.x + g.x, s.y + g.y, s.z + g.z); } }

        public Vector3 CellToLocal(Vector3Int cellPosition)
        {
            Vector3 p = Pitch;
            return new Vector3(cellPosition.x * p.x, cellPosition.y * p.y, cellPosition.z * p.z);
        }

        public Vector3Int LocalToCell(Vector3 localPosition)
        {
            Vector3 p = Pitch;
            return new Vector3Int(Mathf.FloorToInt(localPosition.x / p.x), Mathf.FloorToInt(localPosition.y / p.y),
                p.z == 0f ? 0 : Mathf.FloorToInt(localPosition.z / p.z));
        }

        public Vector3 CellToLocalInterpolated(Vector3 cellPosition)
        {
            Vector3 p = Pitch;
            return new Vector3(cellPosition.x * p.x, cellPosition.y * p.y, cellPosition.z * p.z);
        }

        public Vector3 LocalToCellInterpolated(Vector3 localPosition)
        {
            Vector3 p = Pitch;
            return new Vector3(localPosition.x / p.x, localPosition.y / p.y, p.z == 0f ? 0f : localPosition.z / p.z);
        }

        public Vector3 CellToWorld(Vector3Int cellPosition) { return transform.TransformPoint(CellToLocal(cellPosition)); }
        public Vector3Int WorldToCell(Vector3 worldPosition) { return LocalToCell(transform.InverseTransformPoint(worldPosition)); }
        public Vector3 LocalToWorld(Vector3 localPosition) { return transform.TransformPoint(localPosition); }
        public Vector3 WorldToLocal(Vector3 worldPosition) { return transform.InverseTransformPoint(worldPosition); }

        public Vector3 GetCellCenterLocal(Vector3Int position)
        {
            Vector3 c = CellToLocal(position), s = cellSize;
            return new Vector3(c.x + s.x * 0.5f, c.y + s.y * 0.5f, c.z + s.z * 0.5f);
        }

        public Vector3 GetCellCenterWorld(Vector3Int position) { return transform.TransformPoint(GetCellCenterLocal(position)); }

        public Bounds GetBoundsLocal(Vector3Int cellPosition) { return new Bounds(GetCellCenterLocal(cellPosition), cellSize); }
    }

    public class Grid : GridLayout
    {
        Vector3 m_CellSize = new Vector3(1f, 1f, 0f);
        Vector3 m_CellGap = Vector3.zero;

        public override Vector3 cellSize { get { return m_CellSize; } }
        public override Vector3 cellGap { get { return m_CellGap; } }
        public void SetCellSize(Vector3 size) { m_CellSize = size; }
        public void SetCellGap(Vector3 gap) { m_CellGap = gap; }
    }
}

namespace UnityEngine.Tilemaps
{
    public enum TileFlags { None = 0, LockColor = 1, LockTransform = 2, InstantiateGameObjectRuntimeOnly = 4, LockAll = 3 }

    public abstract class TileBase : ScriptableObject { }

    public class Tile : TileBase
    {
        public enum ColliderType { None = 0, Sprite = 1, Grid = 2 }

        public Sprite sprite;
        public Color color = Color.white;
        public Matrix4x4 transform = Matrix4x4.identity;
        public GameObject gameObject;
        public TileFlags flags = TileFlags.LockColor;
        public ColliderType colliderType = ColliderType.Sprite;
    }

    /// <summary>Tiles by cell.  Its cells are the parent Grid's (a Tilemap is a child of one).</summary>
    public class Tilemap : GridLayout
    {
        sealed class Cell
        {
            public TileBase tile;
            public Color color = Color.white;
            public Matrix4x4 transform = Matrix4x4.identity;
        }

        readonly Dictionary<Vector3Int, Cell> cells = new Dictionary<Vector3Int, Cell>();

        public Color color = Color.white;
        public Vector3 tileAnchor = new Vector3(0.5f, 0.5f, 0f);
        public Matrix4x4 orientationMatrix = Matrix4x4.identity;
        public float animationFrameRate = 1f;

        public Grid layoutGrid { get { return GetComponentInParent<Grid>(); } }
        public override Vector3 cellSize { get { Grid g = layoutGrid; return g != null ? g.cellSize : new Vector3(1f, 1f, 0f); } }
        public override Vector3 cellGap { get { Grid g = layoutGrid; return g != null ? g.cellGap : Vector3.zero; } }

        /// <summary>The bounds of the cells that hold a tile (empty: size 0 at the origin).</summary>
        public BoundsInt cellBounds
        {
            get
            {
                if (cells.Count == 0) return new BoundsInt(Vector3Int.zero, Vector3Int.zero);
                bool first = true;
                Vector3Int lo = Vector3Int.zero, hi = Vector3Int.zero;
                foreach (Vector3Int p in cells.Keys)
                {
                    if (first) { lo = p; hi = p; first = false; continue; }
                    lo = Vector3Int.Min(lo, p);
                    hi = Vector3Int.Max(hi, p);
                }
                return new BoundsInt(lo, hi - lo + Vector3Int.one);
            }
        }

        public Vector3Int origin { get { return cellBounds.position; } }
        public Vector3Int size { get { return cellBounds.size; } }
        public Bounds localBounds
        {
            get
            {
                BoundsInt b = cellBounds;
                Vector3 lo = CellToLocal(b.min), hi = CellToLocal(b.max);
                Bounds r = new Bounds(lo, Vector3.zero);
                r.Encapsulate(hi);
                return r;
            }
        }

        public bool HasTile(Vector3Int position) { return cells.ContainsKey(position); }
        public TileBase GetTile(Vector3Int position) { Cell c; return cells.TryGetValue(position, out c) ? c.tile : null; }
        public T GetTile<T>(Vector3Int position) where T : TileBase { return GetTile(position) as T; }
        public Sprite GetSprite(Vector3Int position) { Tile t = GetTile(position) as Tile; return t == null ? null : t.sprite; }

        public void SetTile(Vector3Int position, TileBase tile)
        {
            if (tile == null) { cells.Remove(position); return; }
            Cell c;
            if (!cells.TryGetValue(position, out c)) { c = new Cell(); cells[position] = c; }
            c.tile = tile;
            Tile t = tile as Tile;
            if (t != null) { c.color = t.color; c.transform = t.transform; }
        }

        public void SetTiles(Vector3Int[] positionArray, TileBase[] tileArray)
        {
            for (int i = 0; i < positionArray.Length && i < tileArray.Length; i++) SetTile(positionArray[i], tileArray[i]);
        }

        /// <summary>The block's cells take the tiles in x, then y, then z order (as Unity fills a block).</summary>
        public void SetTilesBlock(BoundsInt position, TileBase[] tileArray)
        {
            int i = 0;
            for (int z = position.zMin; z < position.zMax; z++)
                for (int y = position.yMin; y < position.yMax; y++)
                    for (int x = position.xMin; x < position.xMax; x++)
                        SetTile(new Vector3Int(x, y, z), i < tileArray.Length ? tileArray[i++] : null);
        }

        public TileBase[] GetTilesBlock(BoundsInt bounds)
        {
            List<TileBase> found = new List<TileBase>();
            for (int z = bounds.zMin; z < bounds.zMax; z++)
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                    for (int x = bounds.xMin; x < bounds.xMax; x++)
                        found.Add(GetTile(new Vector3Int(x, y, z)));
            return found.ToArray();
        }

        public int GetUsedTilesCount()
        {
            HashSet<TileBase> used = new HashSet<TileBase>();
            foreach (Cell c in cells.Values) used.Add(c.tile);
            return used.Count;
        }

        public void ClearAllTiles() { cells.Clear(); }
        public void CompressBounds() { }
        public void ResizeBounds() { }
        public void RefreshAllTiles() { }
        public void RefreshTile(Vector3Int position) { }
        public void SwapTile(TileBase changeTile, TileBase newTile)
        {
            foreach (Cell c in cells.Values) if (c.tile == changeTile) c.tile = newTile;
        }

        public Color GetColor(Vector3Int position) { Cell c; return cells.TryGetValue(position, out c) ? c.color : Color.white; }
        public void SetColor(Vector3Int position, Color color) { Cell c; if (cells.TryGetValue(position, out c)) c.color = color; }
        public Matrix4x4 GetTransformMatrix(Vector3Int position) { Cell c; return cells.TryGetValue(position, out c) ? c.transform : Matrix4x4.identity; }
        public void SetTransformMatrix(Vector3Int position, Matrix4x4 transform) { Cell c; if (cells.TryGetValue(position, out c)) c.transform = transform; }
        public TileFlags GetTileFlags(Vector3Int position) { return TileFlags.None; }
        public void SetTileFlags(Vector3Int position, TileFlags flags) { }
        public Vector3 GetCellCenterLocalAnchored(Vector3Int position)
        {
            Vector3 c = CellToLocal(position), s = cellSize;
            return new Vector3(c.x + s.x * tileAnchor.x, c.y + s.y * tileAnchor.y, c.z);
        }
    }

    public class TilemapRenderer : Renderer
    {
        public enum Mode { Chunk = 0, Individual = 1, SRPBatch = 2 }
        public enum SortOrder { BottomLeft = 0, BottomRight = 1, TopLeft = 2, TopRight = 3 }
        public Mode mode;
        public SortOrder sortOrder;
        public override void OnAttach() { enabled = true; }
    }
}
