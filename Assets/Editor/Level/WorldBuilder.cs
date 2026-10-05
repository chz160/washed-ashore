using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Level-designer tool: generates the World terrain (heights, layers, trees, details,
// light/sky, PlayerSpawn). Re-runnable; overwrites its own assets.
// Run via: unity command eval "return WorldBuilder.Build();"
public static class WorldBuilder
{
    const string Root = "Assets/World/Terrain";
    const string StagingDir = @"E:\GitHub\washed-ashore\_staging\ambientcg";
    const float Size = 512f;
    const float MaxHeight = 80f;
    const int HeightRes = 513;
    const int SplatRes = 512;
    const int DetailRes = 512;
    const int Seed = 1337;

    // Spawn per designer guidance: 40%/40% of size, facing y=45 toward centre, flat 25 m, blend to 40 m.
    public static Vector2 SpawnXZ = new Vector2(205f, 205f);
    public static float SpawnFlatRadius = 25f;
    public static float SpawnBlendRadius = 40f;
    public static float SpawnClearRadius = 22f;
    public static float SpawnYaw = 45f;

    public static string[] TreeNames = { "CommonTree_1", "CommonTree_3", "Pine_1", "Pine_3", "TwistedTree_1", "DeadTree_1" };
    public static string[] RockBushNames = { "Rock_Medium_1", "Rock_Medium_2", "Bush_Common", "Bush_Common_Flowers" };
    // Per-prototype instance count and scale range (Twisted is 16-19 m tall, ~10 m wide: sparse and small).
    public static (int count, float min, float max)[] TreeSpecs = { (120, 0.8f, 1.2f), (100, 0.8f, 1.2f), (120, 0.8f, 1.25f), (100, 0.8f, 1.25f), (20, 0.5f, 0.7f), (35, 0.6f, 0.9f) };
    public static (int count, float min, float max)[] RockSpecs = { (60, 0.7f, 1.5f), (50, 0.7f, 1.5f), (80, 0.8f, 1.2f), (50, 0.8f, 1.2f) };
    public static string[] GrassNames = { "Grass_Common_Short", "Grass_Wispy_Short", "Clover_1" };

    public static string Build()
    {
        Random.InitState(Seed);
        Directory.CreateDirectory(Root + "/Textures");
        Directory.CreateDirectory(Root + "/Layers");

        // Texture imports first: an import refresh mid-build reloaded the terrain asset from disk
        // and wiped heights/splat. Then reuse the persisted TerrainData (writes made before it
        // was an asset were lost too).
        var layers = CreateLayers();
        var dataPath = Root + "/WorldTerrain.asset";
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
        if (!data)
        {
            data = new TerrainData();
            AssetDatabase.CreateAsset(data, dataPath);
        }
        data.heightmapResolution = HeightRes;
        data.alphamapResolution = SplatRes;
        data.size = new Vector3(Size, MaxHeight, Size);
        data.SetHeights(0, 0, GenerateHeights());

        data.terrainLayers = layers;
        PaintSplat(data);

        var missing = new List<string>();
        var trees = TreeNames.Select(n => FindPrefab(n, missing)).Where(p => p).ToList();
        var rocks = RockBushNames.Select(n => FindPrefab(n, missing)).Where(p => p).ToList();
        data.treePrototypes = trees.Concat(rocks).Select(p => new TreePrototype { prefab = p, bendFactor = 0.3f }).ToArray();
        data.RefreshPrototypes();
        PlaceTrees(data, trees.Count, rocks.Count);

        var grass = GrassNames.Select(n => FindPrefab(n, missing)).Where(p => p).ToList();
        PaintDetails(data, grass);

        var old = GameObject.Find("Terrain");
        if (old) Object.DestroyImmediate(old);
        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.isStatic = true;
        var terrain = go.GetComponent<Terrain>();
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp && rp.defaultTerrainMaterial) terrain.materialTemplate = rp.defaultTerrainMaterial;
        terrain.treeDistance = 600f;
        terrain.treeBillboardDistance = 120f;
        terrain.detailObjectDistance = 90f;
        terrain.detailObjectDensity = 1f;
        terrain.heightmapPixelError = 3f;

        var spawn = CreateSpawn(terrain);
        SetupLightAndSky();

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        var scene = go.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Report(terrain, spawn, missing);
    }

    static float[,] GenerateHeights()
    {
        var h = new float[HeightRes, HeightRes];
        float ox = Random.Range(0f, 1000f), oy = Random.Range(0f, 1000f);
        float min = float.MaxValue, max = float.MinValue;
        for (int y = 0; y < HeightRes; y++)
            for (int x = 0; x < HeightRes; x++)
            {
                float u = x / (float)(HeightRes - 1) * Size, v = y / (float)(HeightRes - 1) * Size;
                float n = 0f, amp = 1f, freq = 1f / 180f;
                for (int o = 0; o < 5; o++)
                {
                    n += amp * Mathf.PerlinNoise(ox + u * freq, oy + v * freq);
                    amp *= 0.5f; freq *= 2f;
                }
                // Ridged band for a few hills, plus a gentle rise toward the edges.
                float ridge = 1f - Mathf.Abs(Mathf.PerlinNoise(oy + u / 260f, ox + v / 260f) * 2f - 1f);
                float edge = Mathf.Clamp01((Vector2.Distance(new Vector2(u, v), new Vector2(Size, Size) * 0.5f) - 150f) / 110f);
                h[y, x] = n * 0.55f + ridge * ridge * 0.35f + edge * edge * 0.5f;
                min = Mathf.Min(min, h[y, x]); max = Mathf.Max(max, h[y, x]);
            }
        // Normalise to 0.05..0.9 of MaxHeight, then flatten the spawn clearing.
        float spawnH = 0f; int cnt = 0;
        for (int y = 0; y < HeightRes; y++)
            for (int x = 0; x < HeightRes; x++)
            {
                h[y, x] = Mathf.Lerp(0.05f, 0.9f, (h[y, x] - min) / (max - min));
                if (DistToSpawn(x, y) < SpawnFlatRadius) { spawnH += h[y, x]; cnt++; }
            }
        spawnH /= Mathf.Max(1, cnt);
        for (int y = 0; y < HeightRes; y++)
            for (int x = 0; x < HeightRes; x++)
            {
                float t = Mathf.InverseLerp(SpawnFlatRadius, SpawnBlendRadius, DistToSpawn(x, y));
                h[y, x] = Mathf.Lerp(spawnH, h[y, x], Mathf.SmoothStep(0f, 1f, t));
            }
        return h;
    }

    static float DistToSpawn(int x, int y)
    {
        var p = new Vector2(x / (float)(HeightRes - 1) * Size, y / (float)(HeightRes - 1) * Size);
        return Vector2.Distance(p, SpawnXZ);
    }

    static TerrainLayer[] CreateLayers()
    {
        // grass, dirt, rock (ambientCG CC0)
        var specs = new[] { ("Grass004", 6f), ("Ground037", 5f), ("Rock030", 10f) };
        var layers = new List<TerrainLayer>();
        foreach (var (id, tile) in specs)
        {
            var color = ImportTex(id, "Color", false);
            var normal = ImportTex(id, "NormalGL", true);
            var layer = new TerrainLayer { diffuseTexture = color, normalMapTexture = normal, tileSize = new Vector2(tile, tile), normalScale = 1f, smoothness = 0.05f };
            var path = $"{Root}/Layers/{id}.terrainlayer";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(layer, path);
            layers.Add(layer);
        }
        return layers.ToArray();
    }

    static Texture2D ImportTex(string id, string map, bool normal)
    {
        var file = $"{id}_1K-JPG_{map}.jpg";
        var dst = $"{Root}/Textures/{file}";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
        if (existing) return existing;
        File.Copy(Path.Combine(StagingDir, id, file), dst, true);
        AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
        var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
        imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = !normal;
        imp.mipmapEnabled = true;
        imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }

    static void PaintSplat(TerrainData data)
    {
        var a = new float[SplatRes, SplatRes, 3];
        for (int y = 0; y < SplatRes; y++)
            for (int x = 0; x < SplatRes; x++)
            {
                float nx = x / (float)(SplatRes - 1), ny = y / (float)(SplatRes - 1);
                float steep = data.GetSteepness(nx, ny);
                float hgt = data.GetInterpolatedHeight(nx, ny) / MaxHeight;
                float noise = Mathf.PerlinNoise(nx * 40f, ny * 40f);
                float rock = Mathf.Clamp01((steep - 22f) / 12f) + Mathf.Clamp01((hgt - 0.78f) / 0.1f);
                float dirt = Mathf.Clamp01((steep - 12f) / 10f) * 0.8f + Mathf.Clamp01((noise - 0.62f) * 4f);
                float g = 1f;
                rock = Mathf.Clamp01(rock); dirt = Mathf.Clamp01(dirt) * (1f - rock); g = Mathf.Clamp01(g - rock - dirt);
                float s = g + dirt + rock;
                a[y, x, 0] = g / s; a[y, x, 1] = dirt / s; a[y, x, 2] = rock / s;
            }
        data.SetAlphamaps(0, 0, a);
    }

    static GameObject FindPrefab(string name, List<string> missing)
    {
        var hits = AssetDatabase.FindAssets($"{name} t:GameObject")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileNameWithoutExtension(p) == name)
            .Select(p => (path: p, go: AssetDatabase.LoadAssetAtPath<GameObject>(p)))
            .OrderBy(t => PrefabUtility.GetPrefabAssetType(t.go) == PrefabAssetType.Model ? 1 : 0)
            .ToList();
        if (hits.Count == 0) { missing.Add(name); return null; }
        if (PrefabUtility.GetPrefabAssetType(hits[0].go) == PrefabAssetType.Model) missing.Add(name + "(model-only)");
        return hits[0].go;
    }

    static bool Clear(TerrainData d, float nx, float nz, float maxSteep)
    {
        var rel = new Vector2(nx * Size, nz * Size) - SpawnXZ;
        if (rel.magnitude <= SpawnClearRadius) return false;
        // Clear lane along spawn forward (6 m x 30 m, padded to 10 m x 36 m).
        float yaw = SpawnYaw * Mathf.Deg2Rad;
        var fwd = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
        float along = Vector2.Dot(rel, fwd), side = Mathf.Abs(rel.x * fwd.y - rel.y * fwd.x);
        if (along > -2f && along < 36f && side < 5f) return false;
        return d.GetSteepness(nx, nz) < maxSteep;
    }

    static void PlaceTrees(TerrainData d, int treeTypes, int rockTypes)
    {
        var list = new List<TreeInstance>();
        void Add(int proto, int count, float maxSteep, float minScale, float maxScale)
        {
            int guard = 0;
            for (int i = 0; i < count && guard < count * 20; guard++)
            {
                float nx = Random.Range(0.02f, 0.98f), nz = Random.Range(0.02f, 0.98f);
                // Cluster trees using noise so there are forests and meadows.
                if (proto < treeTypes && Mathf.PerlinNoise(nx * 6f + 50f, nz * 6f + 50f) < 0.42f) continue;
                if (!Clear(d, nx, nz, maxSteep)) continue;
                float s = Random.Range(minScale, maxScale);
                list.Add(new TreeInstance
                {
                    prototypeIndex = proto, position = new Vector3(nx, 0f, nz),
                    widthScale = s, heightScale = s, rotation = Random.Range(0f, Mathf.PI * 2f),
                    color = Color.white, lightmapColor = Color.white
                });
                i++;
            }
        }
        for (int t = 0; t < treeTypes; t++) { var s = TreeSpecs[t]; Add(t, s.count, 30f, s.min, s.max); }
        for (int r = 0; r < rockTypes; r++) { var s = RockSpecs[r]; Add(treeTypes + r, s.count, 40f, s.min, s.max); }
        d.SetTreeInstances(list.ToArray(), true);
    }

    static void PaintDetails(TerrainData d, List<GameObject> grass)
    {
        d.SetDetailResolution(DetailRes, 32);
        d.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
        d.detailPrototypes = grass.Select(g => new DetailPrototype
        {
            prototype = g, usePrototypeMesh = true, useInstancing = true, renderMode = DetailRenderMode.VertexLit,
            minWidth = 0.4f, maxWidth = 0.65f, minHeight = 0.35f, maxHeight = 0.6f, noiseSpread = 0.3f
        }).ToArray();
        var splat = d.GetAlphamaps(0, 0, SplatRes, SplatRes);
        for (int layer = 0; layer < grass.Count; layer++)
        {
            var map = new int[DetailRes, DetailRes];
            bool flowers = grass[layer].name.StartsWith("Clover");
            for (int y = 0; y < DetailRes; y++)
                for (int x = 0; x < DetailRes; x++)
                {
                    float g = splat[y * SplatRes / DetailRes, x * SplatRes / DetailRes, 0];
                    float n = Mathf.PerlinNoise(x * 0.05f + layer * 31f, y * 0.05f + layer * 17f);
                    if (g < 0.6f) continue;
                    map[y, x] = flowers ? (n > 0.7f && Random.value < 0.15f ? 1 : 0) : (n > 0.35f ? (Random.value < 0.5f ? 1 : 2) : 0);
                }
            d.SetDetailLayer(0, 0, layer, map);
        }
    }

    static GameObject CreateSpawn(Terrain t)
    {
        var old = GameObject.Find("PlayerSpawn");
        if (old) Object.DestroyImmediate(old);
        var go = new GameObject("PlayerSpawn");
        float y = t.SampleHeight(new Vector3(SpawnXZ.x, 0, SpawnXZ.y)) + t.transform.position.y;
        go.transform.SetPositionAndRotation(new Vector3(SpawnXZ.x, y, SpawnXZ.y), Quaternion.Euler(0f, SpawnYaw, 0f));
        return go;
    }

    static void SetupLightAndSky()
    {
        var sun = Object.FindObjectsByType<Light>().FirstOrDefault(l => l.type == LightType.Directional);
        if (!sun) sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.name = "Sun";
        sun.intensity = 1.4f;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

        // Built-in procedural skybox: no .mat under Assets/ (qa P5.1 requires every Assets material to be URP).
        RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.88f);
        RenderSettings.fogStartDistance = 120f;
        RenderSettings.fogEndDistance = 520f;
        DynamicGI.UpdateEnvironment();
    }

    public static string Report(Terrain t, GameObject spawn, List<string> missing)
    {
        var d = t.terrainData;
        float[,] h = d.GetHeights(0, 0, d.heightmapResolution, d.heightmapResolution);
        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in h) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
        var a = d.GetAlphamaps(0, 0, d.alphamapWidth, d.alphamapHeight);
        var cover = new float[d.alphamapLayers];
        for (int y = 0; y < d.alphamapHeight; y++)
            for (int x = 0; x < d.alphamapWidth; x++)
                for (int l = 0; l < cover.Length; l++) cover[l] += a[y, x, l];
        int near = 0, offTerrain = 0;
        if (spawn)
            foreach (var inst in d.treeInstances)
            {
                var w = Vector3.Scale(inst.position, d.size) + t.transform.position;
                if (Vector2.Distance(new Vector2(w.x, w.z), new Vector2(spawn.transform.position.x, spawn.transform.position.z)) < 20f) near++;
                if (inst.position.x < 0 || inst.position.x > 1 || inst.position.z < 0 || inst.position.z > 1) offTerrain++;
            }
        float flatMin = float.MaxValue, flatMax = float.MinValue, flatSteep = 0f;
        if (spawn)
            for (float dz = -25f; dz <= 25f; dz += 1f)
                for (float dx = -25f; dx <= 25f; dx += 1f)
                {
                    if (dx * dx + dz * dz > 625f) continue;
                    var wp = spawn.transform.position + new Vector3(dx, 0, dz);
                    float hh = t.SampleHeight(wp);
                    flatMin = Mathf.Min(flatMin, hh); flatMax = Mathf.Max(flatMax, hh);
                    flatSteep = Mathf.Max(flatSteep, d.GetSteepness((wp.x - t.transform.position.x) / d.size.x, (wp.z - t.transform.position.z) / d.size.z));
                }
        int detailInstances = 0;
        for (int l = 0; l < d.detailPrototypes.Length; l++)
            foreach (var c in d.GetDetailLayer(0, 0, d.detailWidth, d.detailHeight, l)) detailInstances += c;
        return $"terrainSize={d.size} minHeight={min * d.size.y:F2}m maxHeight={max * d.size.y:F2}m delta={(max - min) * d.size.y:F2}m " +
               $"layers={d.terrainLayers.Length}[{string.Join(",", d.terrainLayers.Select(x => x.name))}] " +
               $"treePrototypes={d.treePrototypes.Length}[{string.Join(",", d.treePrototypes.Select(p => p.prefab ? p.prefab.name : "null"))}] " +
               $"treeInstances={d.treeInstanceCount} detailLayers={d.detailPrototypes.Length}[{string.Join(",", d.detailPrototypes.Select(p => p.prototype ? p.prototype.name : "null"))}] " +
               $"detailInstances={detailInstances} spawn={(spawn ? spawn.transform.position.ToString("F2") : "none")} " +
               $"layerCoverage=[{string.Join(",", cover.Select(c => (c / (d.alphamapWidth * d.alphamapHeight)).ToString("F3")))}] " +
               $"treesWithin20mOfSpawn={near} offTerrain={offTerrain} spawnFlat25m: heightRange={flatMax - flatMin:F2}m maxSlope={flatSteep:F1}deg " +
               $"spawnRotY={(spawn ? spawn.transform.eulerAngles.y : 0):F0} " +
               $"material={(t.materialTemplate ? t.materialTemplate.shader.name : "none")} missing=[{string.Join(",", missing)}]";
    }
}
