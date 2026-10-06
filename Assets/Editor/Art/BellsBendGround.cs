using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using A = AmbientCgLayers;
using T = BellsBendArtTargets;

// Technical-artist tool: paints the Bells Bend tiles 25 years after the collapse. Owns
// terrainLayers, alphamaps, tree and detail prototypes and instances; never touches heights.
// Run after level-2's BellsBendLevel.BuildAll(), and again whenever the heights change.
// Deterministic: same heights + level maps give the same output.
// Run via: unity command eval "return BellsBendGround.Build();"
public static class BellsBendGround
{
    public const string TerrainRoot = "Terrain";
    const float TexelMetres = 1f;   // alphamap target spacing
    const float DetailMetres = 2f;  // detail cell spacing

    public static string Build()
    {
        var maps = BellsBendMaps.Load();
        var tiles = Tiles();
        if (tiles.Length == 0) return "error: no Terrain_<col>_<row> tiles under '" + TerrainRoot + "'";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var layers = A.Load(true);
        var veg = BellsBendVegetation.Prototypes();
        int trees = 0; long details = 0;
        foreach (var t in tiles)
        {
            var d = t.terrainData;
            d.terrainLayers = layers;
            PaintSplat(t, maps);
            trees += BellsBendVegetation.PlaceTrees(t, maps, veg);
            details += BellsBendVegetation.PaintDetails(t, maps, veg, DetailMetres);
            ApplyRenderSettings(t);
            EditorUtility.SetDirty(d);
        }
        var invalid = tiles[0].terrainData.detailPrototypes.Select(p => p.Validate(out string e) ? null : $"{p.prototype.name}: {e}").Where(e => e != null).ToList();
        if (invalid.Count > 0) throw new System.InvalidOperationException("Invalid detail prototypes: " + string.Join("; ", invalid));
        AssetDatabase.SaveAssets();
        var scene = tiles[0].gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return $"tiles={tiles.Length} layers={layers.Length} treeInstances={trees} detailInstances={details} seconds={sw.Elapsed.TotalSeconds:F0} " +
               $"treeProtos=[{string.Join(",", veg.trees.Select(p => p.name))}] detailProtos=[{string.Join(",", BellsBendVegetation.DetailSpecs.Select(s => s.key))}]";
    }

    public static Terrain[] Tiles()
    {
        var root = GameObject.Find(TerrainRoot);
        if (!root) return new Terrain[0];
        return root.GetComponentsInChildren<Terrain>(true).Where(t => t.name.StartsWith("Terrain_")).OrderBy(t => t.name).ToArray();
    }

    /// <summary>Render budget knobs (R2). Kept in one place so the FPS pass tunes only BellsBendPerf.</summary>
    public static void ApplyRenderSettings(Terrain t)
    {
        t.drawInstanced = true;
        t.heightmapPixelError = BellsBendPerf.PixelError;
        t.basemapDistance = BellsBendPerf.BasemapDistance;
        t.treeDistance = BellsBendPerf.TreeDistance;
        t.treeBillboardDistance = BellsBendPerf.TreeBillboardDistance;
        t.treeCrossFadeLength = 10f;
        t.treeMaximumFullLODCount = BellsBendPerf.MaxFullLodTrees;
        t.detailObjectDistance = BellsBendPerf.DetailDistance;
        t.detailObjectDensity = BellsBendPerf.DetailDensity;
        t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        t.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        if (rp && rp.defaultTerrainMaterial) t.materialTemplate = rp.defaultTerrainMaterial;
    }

    static void PaintSplat(Terrain t, BellsBendMaps maps)
    {
        var d = t.terrainData;
        int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(d.size.x / TexelMetres)), 512, 2048);
        if (d.alphamapResolution != res) d.alphamapResolution = res;
        int n = A.Specs.Length;
        var a = new float[res, res, n];
        var w = new float[n];
        var o = t.transform.position;
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float nx = (x + 0.5f) / res, nz = (y + 0.5f) / res;
                var p = new Vector3(o.x + nx * d.size.x, 0f, o.z + nz * d.size.z);
                Weights(maps, p, w);
                for (int l = 0; l < n; l++) a[y, x, l] = w[l];
            }
        d.SetAlphamaps(0, 0, a);
    }

    /// <summary>Layer weights at a world point, normalised into w.</summary>
    public static void Weights(BellsBendMaps maps, Vector3 p, float[] w)
    {
        System.Array.Clear(w, 0, w.Length);
        byte zone = maps.Zone(p);
        float n1 = Noise(p, 0.045f, 11f), n2 = Noise(p, 0.18f, 37f), n3 = Noise(p, 0.6f, 71f);
        var z = T.Get(zone);

        // Base ground per zone.
        switch (zone)
        {
            case T.Fields:
                // Needle duff under the cedar clumps, old-field grass (layer 0, habitat grass) in the gaps.
                float c = Clump(p, z.clumpCover);
                w[A.Grass] = 1f - c * 0.75f;
                w[A.ForestFloor] = c * 0.75f;
                break;
            case T.Clearing:
                w[A.Grass] = 1f;
                break;
            case T.RoadBuffer:
                // Kudzu blanket thinning toward the outer edge of the 20 m buffer, old field showing through.
                float k = Mathf.Clamp01(0.95f - Mathf.Max(0f, maps.RoadCentreDistance(p) - 8f) / 24f + (n2 - 0.5f) * 0.5f);
                w[A.Kudzu] = k;
                w[A.Grass] = 1f - k;
                break;
            case T.Ridge: case T.Hollows: case T.NorthVista: case T.Bank:
                w[A.ForestFloor] = 1f;
                w[A.Grass] = Mathf.Clamp01((n1 - 0.62f) * 3f) * 0.6f; // mossy openings
                break;
            case T.Road:
                w[A.Grass] = 1f;  // the road layer is mixed on top below
                break;
            default:
                w[A.Bank] = 1f;   // outside the land: the future riverbed
                break;
        }

        // River bank silt on the lowest strip of land.
        float shore = maps.ShoreDistance(p);
        // (The vista lies outside the playable polygon, so its shore distance is negative: no silt there.)
        if (zone != T.Outside && zone != T.NorthVista) Mix(w, A.Bank, Mathf.Clamp01(1f - (shore - 1f) / T.BankMud + (n2 - 0.5f) * 0.6f));

        // Cliffs: rock on every overlay cell (brief: rock >= 0.5 on >= 80%), feathered onto the slopes just below 40 deg.
        float steep = maps.GameSlope(p);
        float rock = maps.IsCliff(p) ? 0.75f + n3 * 0.25f : Mathf.Clamp01((steep - 34f + (n3 - 0.5f) * 6f) / 12f) * 0.6f;
        Mix(w, A.Rock, rock);

        // Roads: the carriageway weight with grass in the cracks; the verges eaten by the old field.
        float road = maps.RoadWeight(p);
        // Z0 is classified on the 5 m grid, so a Z0 texel can sit just past the 2 m road weight: paint it as road too.
        int id = road > 0f || zone == T.Road ? maps.NearestRoad(p) : 0;
        if (id != 0)
        {
            int layer = BellsBendMaps.IsGravel(id) ? A.Gravel : A.Asphalt;
            float crackDensity = id == BellsBendMaps.CleecesFerry ? 2f : 1f;
            float cracks = Mathf.Min(0.3f, Mathf.Clamp01((n3 - 0.72f) * 3f) * 0.2f * crackDensity); // grass in the cracks; road stays >= 0.7 in Z0
            float r = zone == T.Road ? 1f : road * (1f - (n2 - 0.3f) * 0.5f);                       // verges eaten by the old field
            Mix(w, layer, Mathf.Clamp01(r) * (1f - cracks));
            Mix(w, A.Grass, Mathf.Clamp01(r) * cracks);
        }

        float s = 0f;
        for (int i = 0; i < w.Length; i++) s += w[i];
        if (s <= 0f) { w[A.Bank] = 1f; return; }
        for (int i = 0; i < w.Length; i++) w[i] /= s;
    }

    static void Mix(float[] w, int layer, float amount)
    {
        if (amount <= 0f) return;
        for (int i = 0; i < w.Length; i++) w[i] *= 1f - amount;
        w[layer] += amount;
    }

    public static float Noise(Vector3 p, float freq, float salt) =>
        Mathf.PerlinNoise(p.x * freq + salt * 13.1f + 5000f, p.z * freq + salt * 7.7f + 5000f);

    /// <summary>Z5 cedar clump mask, 0..1 with a soft 0..1 edge; 'cover' is the share of the field inside clumps.</summary>
    public static float Clump(Vector3 p, float cover)
    {
        float v = ClumpNoise(p);
        return Mathf.Clamp01((v - ClumpThreshold(cover)) * 25f + 0.5f);
    }

    public static float ClumpNoise(Vector3 p) =>
        Noise(p, 1f / T.FieldClumpMetres, 3f) * 0.8f + Noise(p, 4f / T.FieldClumpMetres, 19f) * 0.2f;

    static float[] clumpCdf;
    /// <summary>ClumpNoise value above which a share 'cover' of the land lies (Perlin values are not uniform).</summary>
    public static float ClumpThreshold(float cover)
    {
        if (cover >= 1f) return -1f;
        if (clumpCdf == null)
        {
            var rng = new System.Random(T.Seed);
            clumpCdf = new float[20000];
            for (int i = 0; i < clumpCdf.Length; i++)
                clumpCdf[i] = ClumpNoise(new Vector3((float)rng.NextDouble() * 8000f - 4000f, 0f, (float)rng.NextDouble() * 8000f - 4000f));
            System.Array.Sort(clumpCdf);
        }
        return clumpCdf[Mathf.Clamp(Mathf.RoundToInt((1f - cover) * (clumpCdf.Length - 1)), 0, clumpCdf.Length - 1)];
    }
}
