using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Level-designer: before/after evidence for the lookout tower's site dressing (spec P2). Snapshot the tree and detail
// instances of every tile before art repaints, again after, then diff: trees may only leave the tower clearing and
// nothing else may change.
//   unity command eval "return BellsBendTowerDiff.Snapshot(\"before\");"
//   unity command eval "return BellsBendGround.Build();"                      (art)
//   unity command eval "return BellsBendTowerDiff.Snapshot(\"after\");"
//   unity command eval "return BellsBendTowerDiff.Compare(\"before\", \"after\");"   -> TestResults/tower-level/p2-trees.json
public static class BellsBendTowerDiff
{
    static string Dir(string label) => Path.Combine(BellsBendTowerSite.EvidenceDir, "snapshot-" + label);

    static IEnumerable<(string name, TerrainData data, Vector3 origin)> Tiles()
    {
        var root = GameObject.Find(BellsBendTiles.RootName);
        if (!root) yield break;
        foreach (var t in root.GetComponentsInChildren<Terrain>(true).OrderBy(t => t.name))
            yield return (t.name, t.terrainData, t.transform.position);
    }

    /// <summary>Per tile: tree instances (world x, z, prototype name, height and width scale) and every detail layer, as binary files.</summary>
    public static string Snapshot(string label)
    {
        var dir = Dir(label);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        long trees = 0, details = 0; int tiles = 0;
        foreach (var (name, d, o) in Tiles())
        {
            using (var w = new BinaryWriter(File.Create(Path.Combine(dir, name + ".trees"))))
            {
                var protos = d.treePrototypes.Select(p => p.prefab ? p.prefab.name : "").ToArray();
                var inst = d.treeInstances;
                w.Write(inst.Length);
                foreach (var i in inst)
                {
                    w.Write(o.x + i.position.x * d.size.x); w.Write(o.z + i.position.z * d.size.z);
                    w.Write(protos[i.prototypeIndex]); w.Write(i.heightScale); w.Write(i.widthScale); w.Write(i.rotation);
                }
                trees += inst.Length;
            }
            using (var w = new BinaryWriter(File.Create(Path.Combine(dir, name + ".details"))))
            {
                int res = d.detailResolution, layers = d.detailPrototypes.Length;
                w.Write(res); w.Write(layers); w.Write(o.x); w.Write(o.z); w.Write(d.size.x);
                for (int l = 0; l < layers; l++)
                {
                    var a = d.GetDetailLayer(0, 0, res, res, l);
                    var b = new byte[res * res];
                    for (int y = 0; y < res; y++)
                        for (int x = 0; x < res; x++) { int v = a[y, x]; b[y * res + x] = (byte)Mathf.Min(255, v); details += v; }
                    var proto = d.detailPrototypes[l];
                    w.Write(proto.prototype ? proto.prototype.name : "");
                    w.Write(proto.maxHeight * UnitHeight(proto.prototype)); // tallest instance of this layer, metres
                    w.Write(b);
                }
            }
            using (var w = new BinaryWriter(File.Create(Path.Combine(dir, name + ".splat"))))
            {
                int res = d.alphamapResolution, layers = d.alphamapLayers;
                var a = d.GetAlphamaps(0, 0, res, res);
                var b = new byte[res * res * layers];
                for (int y = 0; y < res; y++)
                    for (int x = 0; x < res; x++)
                        for (int l = 0; l < layers; l++) b[(y * res + x) * layers + l] = (byte)Mathf.RoundToInt(a[y, x, l] * 255f);
                w.Write(res); w.Write(layers); w.Write(o.x); w.Write(o.z); w.Write(d.size.x); w.Write(b);
            }
            tiles++;
        }
        return $"snapshot '{label}': tiles={tiles} trees={trees} details={details} -> {dir}";
    }

    static float UnitHeight(GameObject go)
    {
        if (!go) return 0f;
        float h = 0f;
        foreach (var f in go.GetComponentsInChildren<MeshFilter>())
            if (f.sharedMesh) h = Mathf.Max(h, f.sharedMesh.bounds.size.y * f.transform.lossyScale.y);
        return h;
    }

    struct Tree { public float x, z, h, w, rot; public string proto; public string Key => $"{x:R}|{z:R}|{proto}|{h:R}|{w:R}|{rot:R}"; }

    static List<Tree> ReadTrees(string path)
    {
        var list = new List<Tree>();
        using (var r = new BinaryReader(File.OpenRead(path)))
        {
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                list.Add(new Tree { x = r.ReadSingle(), z = r.ReadSingle(), proto = r.ReadString(), h = r.ReadSingle(), w = r.ReadSingle(), rot = r.ReadSingle() });
        }
        return list;
    }

    /// <summary>
    /// Diffs two snapshots. Pass = no tree added; every removed tree inside the clearing radius of the tower centre;
    /// no tree left inside it; detail cells changed only inside it. (t-qa's band: none within 17.3 m, none removed beyond 21.3 m.)
    /// </summary>
    public static string Compare(string before, string after)
    {
        var maps = AssetDatabase.LoadAssetAtPath<WashedAshore.Level.BellsBendLevelMaps>(WashedAshore.Level.BellsBendLevelMaps.AssetPath);
        if (!maps || maps.towerClearRadius <= 0f) return "no tower site in LevelMaps (run BellsBendTowerSite.Build())";
        var centre = maps.towerCentre;
        float limit = maps.towerClearRadius, ringRadius = maps.towerClearRadius;
        int removed = 0, added = 0, removedOutside = 0, insideRing = 0, treesBefore = 0, treesAfter = 0;
        float removedMaxDist = 0f, nearestAfter = float.MaxValue;
        const float SplatSlack = 1.5f, TrackSplatHalf = 4f; // one splat texel past the ring; the track's ruts and verge
        long splatChanged = 0, splatChangedOutside = 0;
        long detailChanged = 0, detailChangedOutside = 0; float detailMaxDist = 0f, tallestRingDetail = 0f;
        var changedTiles = new List<string>();
        foreach (var fa in Directory.GetFiles(Dir(before), "*.trees").OrderBy(f => f))
        {
            string name = Path.GetFileNameWithoutExtension(fa), fb = Path.Combine(Dir(after), name + ".trees");
            var a = ReadTrees(fa); var b = ReadTrees(fb);
            treesBefore += a.Count; treesAfter += b.Count;
            foreach (var t in b)
            {
                float d = Vector2.Distance(new Vector2(t.x, t.z), centre);
                nearestAfter = Mathf.Min(nearestAfter, d);
                if (d < ringRadius) insideRing++;
            }
            bool tileChanged = false;
            if (!File.ReadAllBytes(fa).SequenceEqual(File.ReadAllBytes(fb)))
            {
                var sa = new HashSet<string>(a.Select(t => t.Key)); var sb = new HashSet<string>(b.Select(t => t.Key));
                foreach (var t in a.Where(t => !sb.Contains(t.Key)))
                {
                    removed++; tileChanged = true;
                    float d = Vector2.Distance(new Vector2(t.x, t.z), centre);
                    removedMaxDist = Mathf.Max(removedMaxDist, d);
                    if (d > limit) removedOutside++;
                }
                int add = b.Count(t => !sa.Contains(t.Key));
                added += add; tileChanged |= add > 0;
            }
            // details
            using (var ra = new BinaryReader(File.OpenRead(Path.Combine(Dir(before), name + ".details"))))
            using (var rb = new BinaryReader(File.OpenRead(Path.Combine(Dir(after), name + ".details"))))
            {
                int res = ra.ReadInt32(), layers = ra.ReadInt32(); float ox = ra.ReadSingle(), oz = ra.ReadSingle(), size = ra.ReadSingle();
                int resB = rb.ReadInt32(), layersB = rb.ReadInt32(); rb.ReadSingle(); rb.ReadSingle(); rb.ReadSingle();
                if (res != resB || layers != layersB) { detailChangedOutside += (long)res * res; tileChanged = true; }
                else
                    for (int l = 0; l < layers; l++)
                    {
                        ra.ReadString(); rb.ReadString(); ra.ReadSingle(); float spec = rb.ReadSingle();
                        var da = ra.ReadBytes(res * res); var db = rb.ReadBytes(res * res);
                        for (int i = 0; i < da.Length; i++)
                        {
                            bool diff = da[i] != db[i];
                            if (!diff && db[i] == 0) continue;
                            var p = new Vector2(ox + (i % res + 0.5f) / res * size, oz + (i / res + 0.5f) / res * size);
                            float d = Vector2.Distance(p, centre);
                            if (db[i] > 0 && d <= maps.towerClearRadius) tallestRingDetail = Mathf.Max(tallestRingDetail, spec);
                            if (!diff) continue;
                            detailChanged++; tileChanged = true; detailMaxDist = Mathf.Max(detailMaxDist, d);
                            if (d > limit + size / res) detailChangedOutside++;
                        }
                    }
            }
            // ground splat (8-bit per layer): changes are allowed inside the ring and along the track only
            using (var ra = new BinaryReader(File.OpenRead(Path.Combine(Dir(before), name + ".splat"))))
            using (var rb = new BinaryReader(File.OpenRead(Path.Combine(Dir(after), name + ".splat"))))
            {
                int res = ra.ReadInt32(), layers = ra.ReadInt32(); float ox = ra.ReadSingle(), oz = ra.ReadSingle(), size = ra.ReadSingle();
                int resB = rb.ReadInt32(), layersB = rb.ReadInt32(); rb.ReadSingle(); rb.ReadSingle(); rb.ReadSingle();
                if (res != resB || layers != layersB) { splatChangedOutside += (long)res * res; tileChanged = true; }
                else
                {
                    var da = ra.ReadBytes(res * res * layers); var db = rb.ReadBytes(res * res * layers);
                    for (int i = 0; i < res * res; i++)
                    {
                        bool diff = false;
                        for (int l = 0; l < layers && !diff; l++) diff = da[i * layers + l] != db[i * layers + l];
                        if (!diff) continue;
                        splatChanged++; tileChanged = true;
                        var p = new Vector2(ox + (i % res + 0.5f) / res * size, oz + (i / res + 0.5f) / res * size);
                        if (Vector2.Distance(p, centre) <= limit + SplatSlack) continue;
                        float td = float.MaxValue;
                        for (int k = 1; k < maps.towerTrack.Length; k++)
                        {
                            Vector2 a0 = maps.towerTrack[k - 1], ab = maps.towerTrack[k] - a0;
                            float u = Mathf.Clamp01(Vector2.Dot(p - a0, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                            td = Mathf.Min(td, Vector2.Distance(p, a0 + ab * u));
                        }
                        if (td > TrackSplatHalf) splatChangedOutside++;
                    }
                }
            }
            if (tileChanged) changedTiles.Add(name);
        }
        bool pass = added == 0 && removedOutside == 0 && insideRing == 0 && detailChangedOutside == 0 && splatChangedOutside == 0;
        var sbj = new StringBuilder("{\n");
        sbj.Append($" \"before\": \"{before}\", \"after\": \"{after}\",\n \"towerCentre\": [{centre.x:F2}, {centre.y:F2}], \"clearRadius\": {maps.towerClearRadius}, \"removedLimit\": {limit:F2}, \"ringRadius\": {ringRadius},\n");
        sbj.Append($" \"treesBefore\": {treesBefore}, \"treesAfter\": {treesAfter}, \"removed\": {removed}, \"added\": {added},\n");
        sbj.Append($" \"removedMaxDistFromCentre\": {removedMaxDist:F2}, \"removedBeyondLimit\": {removedOutside},\n");
        sbj.Append($" \"treesInsideRing\": {insideRing}, \"nearestTreeToCentre\": {nearestAfter:F2},\n");
        sbj.Append($" \"detailCellsChanged\": {detailChanged}, \"detailCellsChangedBeyondLimit\": {detailChangedOutside}, \"detailChangeMaxDist\": {detailMaxDist:F2},\n");
        sbj.Append($" \"tallestDetailSpecInClearing\": {tallestRingDetail:F2},\n");
        sbj.Append($" \"splatTexelsChanged\": {splatChanged}, \"splatTexelsChangedOutsideRingAndTrack\": {splatChangedOutside}, \"splatRingSlack\": {SplatSlack}, \"splatTrackHalfWidth\": {TrackSplatHalf},\n");
        sbj.Append($" \"changedTiles\": [{string.Join(", ", changedTiles.Select(t => "\"" + t + "\""))}],\n \"pass\": {(pass ? "true" : "false")}\n" + "}\n");
        File.WriteAllText(Path.Combine(BellsBendTowerSite.EvidenceDir, "p2-trees.json"), sbj.ToString());
        return sbj.ToString();
    }
}
