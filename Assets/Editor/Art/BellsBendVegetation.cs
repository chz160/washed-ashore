using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using T = BellsBendArtTargets;

// Technical-artist tool: Bells Bend trees and detail cover (broomsedge, briar, shrub, kudzu, fern,
// crack-grass) from the Stylized Nature MegaKit. Trees hash world-space grid cells, so the result is the
// same whatever the tile layout or build order. Called by BellsBendGround.Build().
public static class BellsBendVegetation
{
    public const string PlantRoot = "Assets/World/BellsBend/Art/Plants";
    const string GreenLeafAtlas = "Assets/ThirdParty/Quaternius/NatureMegaKit/Textures/Leaves_NormalTree_C.png";

    // Species -> MegaKit meshes and instance height range (m); scale is fitted to each mesh's bounds.
    public static readonly (int species, string[] names, float minH, float maxH)[] TreeSpecs =
    {
        (T.Hardwood, new[] { "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5" }, 12f, 20f),
        (T.Pine, new[] { "Pine_1", "Pine_3" }, 11f, 17f),
        (T.Twisted, new[] { "TwistedTree_1" }, 9f, 14f),
        (T.Dead, new[] { "DeadTree_1", "DeadTree_2", "DeadTree_3" }, 7f, 12f),
        (T.FieldCedar, new[] { "Pine_1", "Pine_3" }, 5.5f, 8f),   // 20-30 y eastern redcedar (FEIS): 5.5-8 m
    };

    // Detail layers: key, MegaKit mesh, target height range (m), leaf tint (clear = the MegaKit material as is), counts as shrub cover.
    // Instanced details draw with the prefab's own material, so colour comes from a tinted material copy, not healthy/dry colour.
    public static readonly (string key, string mesh, float minH, float maxH, Color tint, bool shrub)[] DetailSpecs =
    {
        ("Broomsedge", "Grass_Wispy_Tall", 0.7f, 1.1f, new Color(0.95f, 0.72f, 0.42f), false),  // copper-tan bunchgrass
        ("Briar", "Bush_Common", 0.9f, 1.6f, new Color(0.62f, 0.66f, 0.42f), true),           // blackberry/sumac, dusty
        ("ShrubThicket", "Bush_Common", 1.5f, 2.6f, new Color(0.42f, 0.6f, 0.34f), true),         // bush honeysuckle/privet: dark, dense
        ("KudzuMound", "Bush_Common", 1.6f, 3.2f, new Color(0.45f, 0.68f, 0.32f), true),      // vine-smothered scrub
        ("KudzuLeaf", "Clover_1", 0.5f, 0.9f, new Color(0.62f, 0.85f, 0.5f), true),               // trifoliate, like kudzu
        ("Fern", "Fern_1", 0.5f, 0.9f, Color.clear, false),
        ("CrackGrass", "Grass_Common_Short", 0.25f, 0.5f, new Color(0.85f, 0.85f, 0.6f), false),
        ("BankGrass", "Grass_Common_Tall", 0.6f, 1.1f, Color.clear, false),
    };
    public const int Broomsedge = 0, Briar = 1, ShrubThicket = 2, KudzuMound = 3, KudzuLeaf = 4, Fern = 5, CrackGrass = 6, BankGrass = 7;

    public class Protos
    {
        public List<GameObject> trees = new List<GameObject>();
        public List<float> unitHeight = new List<float>();
        public Dictionary<int, int[]> bySpecies = new Dictionary<int, int[]>();
        public GameObject[] details;
        public float[] detailUnitHeight;
    }

    public static Protos Prototypes()
    {
        var p = new Protos();
        var missing = new List<string>();
        var protoIndex = new Dictionary<string, int>();
        foreach (var (species, names, _, _) in TreeSpecs)
        {
            var idx = new List<int>();
            foreach (var n in names)
            {
                // Field cedar shares the Pine prototypes; only the instance scale differs.
                if (!protoIndex.TryGetValue(n, out int i))
                {
                    // LOD chain (MegaKit mesh + impostor cards) instead of the kit's one-level LODGroup: R2 budget.
                    var go = BellsBendTreeLods.Ensure(n);
                    if (!go) { missing.Add(n); continue; }
                    i = protoIndex[n] = p.trees.Count;
                    p.trees.Add(go);
                    p.unitHeight.Add(Mathf.Max(0.1f, MeshBounds(go).size.y));
                }
                idx.Add(i);
            }
            p.bySpecies[species] = idx.ToArray();
        }
        p.details = DetailSpecs.Select(s => s.tint == Color.clear ? Find(s.mesh, missing) : Tinted(s.mesh, s.key, s.tint, missing)).ToArray();
        p.detailUnitHeight = p.details.Select(g => g ? Mathf.Max(0.05f, MeshBounds(g).size.y) : 1f).ToArray();
        if (missing.Count > 0) throw new FileNotFoundException("MegaKit meshes missing: " + string.Join(",", missing));
        return p;
    }

    public static int PlaceTrees(Terrain t, BellsBendMaps maps, Protos protos)
    {
        var d = t.terrainData;
        d.treePrototypes = protos.trees.Select(g => new TreePrototype { prefab = g, bendFactor = 0f }).ToArray();
        d.RefreshPrototypes();
        var band = BarrierBand();
        var list = new List<TreeInstance>();
        var o = t.transform.position;
        // One candidate per jittered cell at the densest in-clump density; each zone keeps its share.
        float top = T.Zones.Where(z => z != null).Max(z => z.treesPerHa / z.clumpCover);
        Scatter(o, d.size, Mathf.Sqrt(10000f / top), 1, (p, rng) =>
        {
            byte zone = maps.Zone(p);
            var z = T.Get(zone);
            if (z == null || z.treesPerHa <= 0f || maps.RoadWeight(p) > 0f) return;
            if (p.z > band.x && p.z < band.y) return;
            if (maps.IsCliff(p) && maps.GameSlope(p) > T.CliffMaxTreeSlope) return;
            if (zone == T.RoadBuffer && maps.RoadCentreDistance(p) < T.RoadBufferOuter) return; // trees at the outer edge only
            float density = z.treesPerHa;
            if (zone == T.Fields) density = BellsBendGround.Clump(p, z.clumpCover) > 0.5f ? z.treesPerHa / z.clumpCover : 0f;
            if (zone == T.RoadBuffer) density = z.treesPerHa * 20f / 8f; // the whole quota sits in the outer 8 m of 20 m
            if (rng.NextDouble() >= density / top) return;
            list.Add(Instance(protos, Pick(rng, z.mix), rng, o, d.size, p));
        });
        d.SetTreeInstances(list.ToArray(), true);
        return list.Count;
    }

    public static long PaintDetails(Terrain t, BellsBendMaps maps, Protos protos, float cellMetres)
    {
        var d = t.terrainData;
        int res = Mathf.Clamp(Mathf.RoundToInt(d.size.x / cellMetres), 128, 2048);
        d.SetDetailResolution(res, 32);
        d.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        d.detailPrototypes = DetailSpecs.Select((s, i) => new DetailPrototype
        {
            prototype = protos.details[i], usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
            minWidth = s.minH / protos.detailUnitHeight[i], maxWidth = s.maxH / protos.detailUnitHeight[i],
            minHeight = s.minH / protos.detailUnitHeight[i], maxHeight = s.maxH / protos.detailUnitHeight[i],
            noiseSpread = 0.4f, healthyColor = Color.white, dryColor = Color.white, alignToGround = 0.3f, positionJitter = 0.9f,
        }).ToArray();
        int n = DetailSpecs.Length;
        var layers = new int[n][,];
        for (int i = 0; i < n; i++) layers[i] = new int[res, res];
        var o = t.transform.position;
        var band = BarrierBand();
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                var p = new Vector3(o.x + (x + 0.5f) / res * d.size.x, 0f, o.z + (y + 0.5f) / res * d.size.z);
                byte zone = maps.Zone(p);
                var z = T.Get(zone);
                if (z == null || zone == T.NorthVista || maps.ShoreDistance(p) < 0.5f) continue; // vista is never walked: no detail cost
                if (p.z > band.x && p.z < band.y) continue; // keep the barrier readable
                if (maps.IsCliff(p) && maps.GameSlope(p) > T.CliffMaxTreeSlope) continue;
                var rng = new System.Random(Hash(Mathf.FloorToInt(p.x * 4f), Mathf.FloorToInt(p.z * 4f), 3));
                float n1 = BellsBendGround.Noise(p, 0.15f, 53f);
                int Count(float mean) => mean <= 0f ? 0 : Mathf.FloorToInt(mean + (float)rng.NextDouble());
                bool Chance(float c) => rng.NextDouble() < c;
                float road = maps.RoadWeight(p);
                if (road > 0f)
                {
                    int id = maps.Road(p);
                    float crack = (id == BellsBendMaps.CleecesFerry ? 2f : 1f) * (BellsBendMaps.IsGravel(id) ? 1.5f : 1f);
                    layers[CrackGrass][y, x] = Count(0.35f * crack * Mathf.Clamp01(n1 * 1.5f) + (1f - road) * 1.2f);
                    if (road < 0.5f) layers[KudzuLeaf][y, x] = Count(1.2f * (1f - road));
                    continue;
                }
                switch (zone)
                {
                    case T.Fields:
                        bool gap = BellsBendGround.Clump(p, z.clumpCover) < 0.5f;
                        layers[Broomsedge][y, x] = Count(gap ? 1.6f * Mathf.Clamp01(0.4f + n1) : 0.2f);
                        if (Chance(gap ? z.shrubCell : z.shrubCell * 0.3f)) layers[n1 > 0.5f ? Briar : ShrubThicket][y, x] = 1;
                        break;
                    case T.Clearing:
                        layers[Broomsedge][y, x] = Count(1.8f * Mathf.Clamp01(0.4f + n1));
                        if (Chance(z.shrubCell)) layers[Briar][y, x] = 1;
                        break;
                    case T.RoadBuffer:
                        float fade = Mathf.Clamp01(1.1f - Mathf.Max(0f, maps.RoadCentreDistance(p) - 8f) / 16f);
                        if (Chance(z.shrubCell * fade)) layers[KudzuMound][y, x] = 1;
                        layers[KudzuLeaf][y, x] = Count(2.5f * fade);
                        break;
                    case T.Bank:
                        if (Chance(z.shrubCell)) layers[n1 > 0.45f ? ShrubThicket : Briar][y, x] = 1;
                        layers[BankGrass][y, x] = Count(1.2f * n1);
                        break;
                    case T.Ridge:
                    case T.Hollows:
                        layers[Fern][y, x] = Count((zone == T.Hollows ? 0.9f : 0.4f) * n1);
                        if (Chance(z.shrubCell)) layers[ShrubThicket][y, x] = 1;  // bush honeysuckle understorey
                        break;
                }
            }
        long total = 0;
        for (int i = 0; i < n; i++)
        {
            d.SetDetailLayer(0, 0, i, layers[i]);
            foreach (var v in layers[i]) total += v;
        }
        return total;
    }

    /// <summary>Z range kept free of trees under the north barrier: MapConfig.northLineZ -30 m .. +8 m (level-2's band).</summary>
    static Vector2 BarrierBand()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<WashedAshore.World.MapConfig>("Assets/World/MapConfig.asset");
        if (!cfg) throw new FileNotFoundException("MapConfig missing", "Assets/World/MapConfig.asset");
        return new Vector2(cfg.northLineZ - 30f, cfg.northLineZ + 8f);
    }

    static int Pick(System.Random rng, float[] mix)
    {
        double r = rng.NextDouble() * mix.Sum();
        for (int i = 0; i < mix.Length; i++) { if (r < mix[i]) return i; r -= mix[i]; }
        return System.Array.FindLastIndex(mix, m => m > 0f);
    }

    static TreeInstance Instance(Protos protos, int species, System.Random rng, Vector3 o, Vector3 size, Vector3 p)
    {
        var options = protos.bySpecies[species];
        int proto = options[rng.Next(options.Length)];
        var spec = TreeSpecs.First(s => s.species == species);
        float s = Mathf.Lerp(spec.minH, spec.maxH, (float)rng.NextDouble()) / protos.unitHeight[proto];
        return new TreeInstance
        {
            prototypeIndex = proto,
            position = new Vector3((p.x - o.x) / size.x, 0f, (p.z - o.z) / size.z),
            widthScale = s * Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble()), heightScale = s,
            rotation = (float)rng.NextDouble() * Mathf.PI * 2f, color = Color.white, lightmapColor = Color.white,
        };
    }

    /// <summary>Jittered world-space grid; each cell's RNG depends only on its world cell and salt.</summary>
    static void Scatter(Vector3 o, Vector3 size, float cell, int salt, System.Action<Vector3, System.Random> visit)
    {
        // A cell belongs to the tile its jittered point falls in, so tile seams get neither gaps nor doubles.
        int x0 = Mathf.FloorToInt(o.x / cell), x1 = Mathf.FloorToInt((o.x + size.x) / cell);
        int z0 = Mathf.FloorToInt(o.z / cell), z1 = Mathf.FloorToInt((o.z + size.z) / cell);
        for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                var rng = new System.Random(Hash(cx, cz, salt));
                var p = new Vector3((cx + (float)rng.NextDouble()) * cell, 0f, (cz + (float)rng.NextDouble()) * cell);
                if (p.x < o.x || p.x >= o.x + size.x || p.z < o.z || p.z >= o.z + size.z) continue;
                visit(p, rng);
            }
    }

    static int Hash(int x, int z, int salt)
    {
        unchecked
        {
            uint h = (uint)T.Seed * 2654435761u ^ (uint)x * 73856093u ^ (uint)z * 19349663u ^ (uint)salt * 83492791u;
            h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            return (int)h;
        }
    }

    /// <summary>A MegaKit prefab copy whose non-bark materials are tinted: PlantRoot/{key}.prefab + M_{key}_*.mat.</summary>
    static GameObject Tinted(string mesh, string key, Color tint, List<string> missing)
    {
        var src = Find(mesh, missing);
        if (!src) return null;
        Directory.CreateDirectory(PlantRoot);
        var copies = new Dictionary<Material, Material>();
        foreach (var m in src.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct())
        {
            if (!m || m.name.Contains("Bark")) continue;
            var path = $"{PlantRoot}/M_{key}_{m.name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(m); AssetDatabase.CreateAsset(mat, path); }
            mat.CopyPropertiesFromMaterial(m);
            // Bush_Common's leaf atlas is the red TwistedTree one; the same-layout green NormalTree atlas makes it read as summer scrub.
            if (m.mainTexture && m.mainTexture.name.StartsWith("Leaves_TwistedTree"))
                mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(GreenLeafAtlas);
            mat.SetColor("_BaseColor", tint);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            copies[m] = mat;
        }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
        if (PrefabUtility.IsPartOfPrefabInstance(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => m && copies.TryGetValue(m, out var c) ? c : m).ToArray();
        // Detail prototypes need one mesh and one material and reject LODGroups ("LOD Group component is not supported"); the MegaKit bushes carry a 1-level one.
        foreach (var lod in go.GetComponentsInChildren<LODGroup>()) Object.DestroyImmediate(lod);
        go.name = key;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PlantRoot}/{key}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject Find(string name, List<string> missing)
    {
        var hit = AssetDatabase.FindAssets($"{name} t:GameObject", new[] { "Assets/ThirdParty/Quaternius/NatureMegaKit" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileNameWithoutExtension(p) == name)
            .OrderBy(p => p.EndsWith(".prefab") ? 0 : 1)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .FirstOrDefault();
        if (!hit) missing.Add(name);
        return hit;
    }

    public static Bounds MeshBounds(GameObject go)
    {
        var fs = go.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh).ToArray();
        if (fs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        Bounds b = default;
        bool first = true;
        foreach (var f in fs)
        {
            var m = f.sharedMesh.bounds;
            var mtx = go.transform.worldToLocalMatrix * f.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? m.min.x : m.max.x, (i & 2) == 0 ? m.min.y : m.max.y, (i & 4) == 0 ? m.min.z : m.max.z);
                var w = mtx.MultiplyPoint3x4(c);
                if (first) { b = new Bounds(w, Vector3.zero); first = false; } else b.Encapsulate(w);
            }
        }
        return b;
    }
}
