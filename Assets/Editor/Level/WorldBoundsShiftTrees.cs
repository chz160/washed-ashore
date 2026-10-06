using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// B6 helper for WorldBoundsShiftCheck. The art build keeps trees out of a clear band at
// northLineZ -30..+8 (BellsBendGround reads the real MapConfig), so a shifted line would run
// through vista forest and tree colliders could stop the sweep before the barrier. Instead of
// repainting every tile, clear only the tree instances inside the shifted band and back them up
// to a file; Restore puts the exact arrays back and checks a hash against the pre-shift state.
public static class WorldBoundsShiftTrees
{
    public const float BandSouth = 30f, BandNorth = 8f;
    static string BackupPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults", "b6-trees-backup.json");

    [System.Serializable]
    class TileTrees
    {
        public string tile;
        public string hash;
        public int[] proto;
        public float[] data;       // px, py, pz, width, height, rotation per instance
        public uint[] color, lightmap;
    }

    [System.Serializable]
    class Backup { public List<TileTrees> tiles = new List<TileTrees>(); }

    /// <summary>Removes tree instances with lineZ - BandSouth &lt;= z &lt;= lineZ + BandNorth; backs up affected tiles.</summary>
    public static string ClearBand(float lineZ)
    {
        if (File.Exists(BackupPath)) throw new IOException($"{BackupPath} exists: restore the previous B6 shift first");
        var backup = new Backup();
        int removed = 0;
        foreach (var t in Terrain.activeTerrains)
        {
            var d = t.terrainData;
            float oz = t.transform.position.z;
            if (oz > lineZ + BandNorth || oz + d.size.z < lineZ - BandSouth) continue;
            var all = d.treeInstances;
            var keep = new List<TreeInstance>(all.Length);
            foreach (var i in all)
            {
                float z = oz + i.position.z * d.size.z;
                if (z >= lineZ - BandSouth && z <= lineZ + BandNorth) removed++;
                else keep.Add(i);
            }
            if (keep.Count == all.Length) continue;
            backup.tiles.Add(Save(t.name, all));
            d.SetTreeInstances(keep.ToArray(), false);
            EditorUtility.SetDirty(d);
        }
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Path.GetDirectoryName(BackupPath));
        File.WriteAllText(BackupPath, JsonUtility.ToJson(backup));
        return $"trees cleared in z=[{lineZ - BandSouth:F1},{lineZ + BandNorth:F1}]: removed={removed} tiles={backup.tiles.Count}";
    }

    /// <summary>Puts the backed-up tree arrays back and verifies each tile's hash.</summary>
    public static string RestoreBand()
    {
        if (!File.Exists(BackupPath)) return "trees: no backup (nothing to restore)";
        var backup = JsonUtility.FromJson<Backup>(File.ReadAllText(BackupPath));
        var byName = new Dictionary<string, Terrain>();
        foreach (var t in Terrain.activeTerrains) byName[t.name] = t;
        var bad = new List<string>();
        int restored = 0;
        foreach (var b in backup.tiles)
        {
            if (!byName.TryGetValue(b.tile, out var t)) { bad.Add($"{b.tile} missing"); continue; }
            var arr = Load(b);
            t.terrainData.SetTreeInstances(arr, false);
            EditorUtility.SetDirty(t.terrainData);
            restored += arr.Length;
            string now = Hash(t.terrainData.treeInstances);
            if (now != b.hash) bad.Add($"{b.tile} hash {now} != {b.hash}");
        }
        AssetDatabase.SaveAssets();
        if (bad.Count == 0) File.Delete(BackupPath);
        return $"trees restored: tiles={backup.tiles.Count} instances={restored} hashOk={bad.Count == 0}" +
               (bad.Count > 0 ? $" problems=[{string.Join("; ", bad)}] (backup kept at {BackupPath})" : "");
    }

    static TileTrees Save(string tile, TreeInstance[] all)
    {
        var s = new TileTrees { tile = tile, hash = Hash(all), proto = new int[all.Length], data = new float[all.Length * 6],
                                color = new uint[all.Length], lightmap = new uint[all.Length] };
        for (int k = 0; k < all.Length; k++)
        {
            var i = all[k];
            s.proto[k] = i.prototypeIndex;
            s.data[k * 6] = i.position.x; s.data[k * 6 + 1] = i.position.y; s.data[k * 6 + 2] = i.position.z;
            s.data[k * 6 + 3] = i.widthScale; s.data[k * 6 + 4] = i.heightScale; s.data[k * 6 + 5] = i.rotation;
            s.color[k] = Pack(i.color); s.lightmap[k] = Pack(i.lightmapColor);
        }
        return s;
    }

    static TreeInstance[] Load(TileTrees s)
    {
        var arr = new TreeInstance[s.proto.Length];
        for (int k = 0; k < arr.Length; k++)
            arr[k] = new TreeInstance
            {
                prototypeIndex = s.proto[k],
                position = new Vector3(s.data[k * 6], s.data[k * 6 + 1], s.data[k * 6 + 2]),
                widthScale = s.data[k * 6 + 3], heightScale = s.data[k * 6 + 4], rotation = s.data[k * 6 + 5],
                color = Unpack(s.color[k]), lightmapColor = Unpack(s.lightmap[k])
            };
        return arr;
    }

    static uint Pack(Color32 c) => (uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24);
    static Color32 Unpack(uint v) => new Color32((byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24));

    /// <summary>FNV-1a over prototype, position, scales and rotation of every instance, in order.</summary>
    public static string Hash(TreeInstance[] all)
    {
        ulong h = 1469598103934665603UL;
        void Mix(uint v) { for (int b = 0; b < 4; b++) { h ^= (v >> (8 * b)) & 0xff; h *= 1099511628211UL; } }
        uint F(float f) => System.BitConverter.ToUInt32(System.BitConverter.GetBytes(f), 0);
        foreach (var i in all)
        {
            Mix((uint)i.prototypeIndex);
            Mix(F(i.position.x)); Mix(F(i.position.y)); Mix(F(i.position.z));
            Mix(F(i.widthScale)); Mix(F(i.heightScale)); Mix(F(i.rotation));
        }
        return $"{all.Length}:{h:x16}";
    }
}
