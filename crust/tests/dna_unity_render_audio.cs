// dna
// unity-engine
// Rendering, audio and tilemaps with Unity's meanings: a sprite sets its renderer's size and bounds (scale and pivot taken in); an AudioSource
// plays for its clip's length at its pitch (in real time), pauses and loops; a tilemap's cells and world positions, its bounds and a filled block;
// a line's bounds; HashSet's set operations; an Animator's state by name.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

class Program
{
    static string V(Vector3 v) { return (int)Math.Round(v.x * 100) + "," + (int)Math.Round(v.y * 100); }

    static int Main()
    {
        Time.unscaledDeltaTime = 0.25f;
        GameObject g = new GameObject("Hero");
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        Texture2D tex = new Texture2D(64, 32);
        tex.hostId = 3;
        sr.sprite = Sprite.Create(tex, new Rect(0f, 0f, 64f, 32f), new Vector2(0f, 0f), 32f);   // pivot at the bottom left
        g.transform.position = new Vector3(1f, 1f, 0f);
        g.transform.localScale = new Vector3(2f, 1f, 1f);
        Console.WriteLine(sr.spriteId + " " + V(sr.size) + " " + V(sr.bounds.center) + " " + V(sr.bounds.size));   // 3 200,100 300,150 400,100

        AudioSource source = g.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.clip = AudioClip.Create("beep", 44100, 1, 44100, false);                   // one second
        source.pitch = 2f;
        source.Play();
        Engine.Tick();
        Console.Write((source.isPlaying ? 1 : 0) + " " + (int)(source.time * 100) + " ");   // 1 50 (a quarter second at double speed)
        Engine.Tick();
        Console.WriteLine(source.isPlaying ? 1 : 0);                                       // 0 (played its second)
        source.loop = true;
        source.Play();
        Engine.Tick();
        source.Pause();
        Engine.Tick();
        Engine.Tick();
        Console.Write((int)(source.time * 100) + " ");                                     // 50 (paused)
        source.UnPause();
        Engine.Tick();
        Engine.Tick();
        Console.WriteLine((source.isPlaying ? 1 : 0) + " " + (int)(source.time * 100));   // 1 50 (looped: 1.5 seconds in)

        GameObject gridObject = new GameObject("Grid");
        Grid grid = gridObject.AddComponent<Grid>();
        grid.SetCellSize(new Vector3(0.5f, 0.5f, 0f));
        gridObject.transform.position = new Vector3(10f, 0f, 0f);
        GameObject mapObject = new GameObject("Map");
        mapObject.transform.SetParent(gridObject.transform, false);
        Tilemap map = mapObject.AddComponent<Tilemap>();
        Tile tile = ScriptableObject.CreateInstance<Tile>();
        map.SetTile(new Vector3Int(2, -1, 0), tile);
        map.SetTilesBlock(new BoundsInt(-1, 0, 0, 2, 2, 1), new TileBase[] { tile, null, tile, tile });
        Console.WriteLine(map.cellBounds.position + " " + map.cellBounds.size + " " + (map.HasTile(new Vector3Int(0, 0, 0)) ? 1 : 0)
            + " " + map.WorldToCell(new Vector3(11.3f, -0.2f, 0f)) + " " + V(map.GetCellCenterWorld(new Vector3Int(2, -1, 0))));
        // (-1, -1, 0) (4, 3, 1) 0 (2, -1, 0) 1125,-25 (the tiles span y -1 to 1)

        LineRenderer line = new GameObject("Line").AddComponent<LineRenderer>();
        line.positionCount = 3;
        line.SetPositions(new Vector3[] { new Vector3(-1f, 0f, 0f), new Vector3(2f, 3f, 0f), new Vector3(0f, -2f, 0f) });
        Console.WriteLine(V(line.bounds.min) + " " + V(line.bounds.max));                 // -100,-200 200,300

        HashSet<int> a = new HashSet<int>(new int[] { 1, 2, 3, 4 });
        a.IntersectWith(new int[] { 2, 4, 6 });
        a.UnionWith(new int[] { 9 });
        int removed = a.RemoveWhere(x => x > 8);
        Console.WriteLine(a.Count + " " + (a.Contains(4) ? 1 : 0) + " " + removed + " " + (a.SetEquals(new int[] { 4, 2 }) ? 1 : 0));   // 2 1 1 1

        Animator animator = g.AddComponent<Animator>();
        animator.Play("Jump");
        animator.speed = 2f;
        Engine.Tick();
        AnimatorStateInfo info = animator.GetCurrentAnimatorStateInfo(0);
        Console.WriteLine((info.IsName("Jump") ? 1 : 0) + " " + (info.IsName("Idle") ? 1 : 0) + " " + (Animator.StringToHash("Jump") == info.shortNameHash ? 1 : 0));   // 1 0 1
        return 0;
    }
}
