using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using UnityEditor;
using UnityEngine;
using WashedAshore.World;

// Technical-artist tool: fetches the Bells Bend ground materials from ambientCG (CC0) and builds
// their TerrainLayers. Re-runnable: a texture already under Assets/ThirdParty/ambientCG is reused,
// a zip already in _staging/ambientcg is not downloaded again.
// Run via: unity command eval "return AmbientCgLayers.Build();"
public static class AmbientCgLayers
{
    public const string TexRoot = "Assets/ThirdParty/ambientCG";
    public const string LayerRoot = "Assets/World/BellsBend/Art/Layers";
    static string StagingDir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "_staging", "ambientcg");

    // Canonical ground IDs (Specs order 0-6, equal to WashedAshore.World.GroundType). A tile's alphamap channel
    // order is NOT fixed any more: BellsBendGround keeps only the layers a tile needs, and runtime code finds a
    // layer by asset name through TerrainGroundMap. The names come from GroundTypes.LayerNames so the two cannot drift.
    public const int Grass = (int)GroundType.Grass, ForestFloor = (int)GroundType.ForestFloor, Asphalt = (int)GroundType.Asphalt,
        Gravel = (int)GroundType.Gravel, Bank = (int)GroundType.Bank, Rock = (int)GroundType.Rock, Kudzu = (int)GroundType.Kudzu;
    // Tile sizes must divide the 1024 m terrain tile: layer UVs restart at each tile's corner, so any
    // other size shows a straight seam at every tile border. Build() refuses sizes that don't.
    public static readonly (string key, string id, Vector2 tile, float smooth)[] Specs =
    {
        (GroundTypes.LayerNames[0], "Ground037", new Vector2(4f, 4f), 0.05f),   // moss, broomsedge stubble, twigs: not lawn
        (GroundTypes.LayerNames[1], "Ground023", new Vector2(4f, 4f), 0.05f),     // leaf litter (ridge, hollows, bank)
        (GroundTypes.LayerNames[2], "Asphalt026C", new Vector2(8f, 8f), 0.15f),
        (GroundTypes.LayerNames[3], "Ground078", new Vector2(8f, 4f), 0.05f),  // 2:1 source image
        (GroundTypes.LayerNames[4], "Ground054", new Vector2(4f, 4f), 0.2f),        // silt/mud
        (GroundTypes.LayerNames[5], "Rock051", new Vector2(16f, 16f), 0.1f),        // grey limestone with moss
        (GroundTypes.LayerNames[6], "Grass001", new Vector2(4f, 4f), 0.1f),        // dense dark broadleaf cover on the verges
    };

    const float TerrainTileMeters = 1024f;

    static void CheckTileSizes()
    {
        foreach (var s in Specs)
            foreach (var v in new[] { s.tile.x, s.tile.y })
                if (v <= 0f || Mathf.Abs(TerrainTileMeters / v - Mathf.Round(TerrainTileMeters / v)) > 1e-4f)
                    throw new System.InvalidOperationException($"{s.key}: tile size {v} m doesn't divide {TerrainTileMeters} m; it would seam at tile borders.");
    }

    public static string Build()
    {
        Directory.CreateDirectory(LayerRoot);
        var names = new System.Collections.Generic.List<string>();
        foreach (var layer in Load(true)) names.Add(layer.name);
        AssetDatabase.SaveAssets();
        return $"layers=[{string.Join(",", names)}] texRoot={TexRoot}";
    }

    /// <summary>The layers whose canonical ID bit is set in <paramref name="mask"/>, in ID order (a tile's terrainLayers list).</summary>
    public static TerrainLayer[] Subset(TerrainLayer[] all, uint mask) =>
        all.Where((l, i) => (mask & (1u << i)) != 0).ToArray();

    /// <summary>The layers in canonical ID order; with create, (re)builds any that are missing or stale.</summary>
    public static TerrainLayer[] Load(bool create)
    {
        if (create) CheckTileSizes();
        return Specs.Select(s =>
        {
            var path = $"{LayerRoot}/{s.key}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (!create) return layer;
            var color = ImportTex(s.id, "Color", false);
            var normal = ImportTex(s.id, "NormalGL", true);
            if (!layer)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = color;
            layer.normalMapTexture = normal;
            layer.tileSize = s.tile;
            layer.normalScale = 1f;
            layer.smoothness = s.smooth;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }).ToArray();
    }

    static Texture2D ImportTex(string id, string map, bool normal)
    {
        var file = $"{id}_1K-JPG_{map}.jpg";
        var dst = $"{TexRoot}/{id}/{file}";
        if (!File.Exists(dst) || IsLfsPointer(dst))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            File.Copy(Staged(id, file), dst, true);
            AssetDatabase.ImportAsset(dst, ImportAssetOptions.ForceSynchronousImport);
        }
        var imp = (TextureImporter)AssetImporter.GetAtPath(dst);
        var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        // 1K, BC7/DXT5nm via Normal quality, mips on: about 1.3 MB per map on Windows.
        if (imp.textureType != type || imp.sRGBTexture == normal || imp.maxTextureSize != 1024 || !imp.mipmapEnabled)
        {
            imp.textureType = type;
            imp.sRGBTexture = !normal;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 1024;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.anisoLevel = 4;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
    }

    static string Staged(string id, string file)
    {
        var src = Path.Combine(StagingDir, id, file);
        if (File.Exists(src) && !IsLfsPointer(src)) return src;
        var zip = Path.Combine(StagingDir, $"{id}_1K-JPG.zip");
        if (!File.Exists(zip) || new FileInfo(zip).Length < 1024)
        {
            Directory.CreateDirectory(StagingDir);
            using var http = new HttpClient { Timeout = System.TimeSpan.FromMinutes(2) };
            var bytes = http.GetByteArrayAsync($"https://ambientcg.com/get?file={id}_1K-JPG.zip").Result;
            File.WriteAllBytes(zip, bytes);
        }
        using (var archive = ZipFile.OpenRead(zip))
        {
            var entry = archive.GetEntry(file) ?? throw new FileNotFoundException($"{file} is not in {zip}");
            Directory.CreateDirectory(Path.GetDirectoryName(src));
            entry.ExtractToFile(src, true);
        }
        return src;
    }

    static bool IsLfsPointer(string path)
    {
        if (new FileInfo(path).Length > 512) return false;
        return File.ReadAllText(path).StartsWith("version https://git-lfs");
    }
}
