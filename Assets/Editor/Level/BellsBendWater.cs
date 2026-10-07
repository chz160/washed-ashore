using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using WashedAshore.Level;
using WashedAshore.World;

/// <summary>A water build guard tripped: terrain changed under the water, or the north line moved.</summary>
public class WaterBuildException : Exception
{
    public WaterBuildException(string message) : base(message) { }
    public WaterBuildException(string message, Exception inner) : base(message, inner) { }
}

// Level-designer: per-tile water quads for Bells Bend (water pod W1-W3). Rerunnable; rebuilds only the "Water" root.
//   unity command eval "return BellsBendWater.Build();"
// Order: guards (lake-mask hash, north line) -> mesh + placeholder material -> one quad per 1024 m tile at
//        MapConfig.WaterLevelY on layer 4 -> WaterBody (w-engineer) and WaterMaps bake (w-artist) if present -> save.
// BellsBendLevel.BuildAll runs Guard first and Build (no save) after the barrier and backstop.
public static class BellsBendWater
{
    /// <summary>Lake mask (report.json lakeMaskSha256, first 16) the water was approved against: terrain rc4.
    /// A terrain rebuild that changes the lake must re-check the water and bump this on purpose.</summary>
    public const string ApprovedLakeMask = "8f8a637cd4657d82";
    /// <summary>Same tolerance as tools/terrain LINE_MOVE_TOLERANCE_M (config vs data north line).</summary>
    public const float LineMoveTolerance = 1f;

    public const string RootName = "Water";
    public const string Dir = BellsBendData.WorldRoot + "/Water";
    public const string MeshPath = Dir + "/WaterTile.asset";
    public const string MaterialPath = Dir + "/BellsBendWater.mat"; // w-artist owns it; created here only if missing
    public const string MotionPath = "Assets/World/Water/WaterMotion.asset"; // w-engineer
    const string WaterBodyType = "WashedAshore.World.WaterBody, WashedAshore.World";
    const string WaterMapsType = "BellsBendWaterMaps, Assembly-CSharp-Editor";

    [MenuItem("Washed Ashore/Level/Build Bells Bend water")]
    static void BuildMenu() => Debug.Log(Build());

    public static string Build() => Build(BellsBendData.LoadConfig());

    /// <summary>Batchmode entry: unity run &lt;project&gt; -- -executeMethod BellsBendWater.BuildBatch.
    /// Builds, takes the seam shots, writes Logs/bells-bend-water-build.txt, exits 1 on failure.</summary>
    public static void BuildBatch()
    {
        var path = Path.Combine(BellsBendData.ProjectRoot, "Logs", "bells-bend-water-build.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try
        {
            File.WriteAllText(path, Build() + SeamShots() + "\ndone\n");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText(path, "FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    public static string Build(MapConfig cfg)
    {
        if (EditorSceneManager.GetActiveScene().path != BellsBendLevel.ScenePath) EditorSceneManager.OpenScene(BellsBendLevel.ScenePath);
        return Build(cfg, BellsBendData.LoadManifest(cfg), LoadReport(cfg), true);
    }

    public static Dictionary<string, object> LoadReport(MapConfig cfg)
    {
        var path = Path.Combine(BellsBendData.ProjectRoot, Path.GetDirectoryName(cfg.manifestPath), "report.json");
        if (!File.Exists(path)) throw new WaterBuildException($"water: no tools report at {path}; run tools/terrain/build_terrain.py");
        return (Dictionary<string, object>)BellsBendData.Json.Parse(File.ReadAllText(path));
    }

    public static string Guard(MapConfig cfg, BellsBendData.Manifest m) => Guard(cfg, m, LoadReport(cfg));

    /// <summary>W3: throws WaterBuildException unless the terrain lake mask is the approved one and the north line has not moved.</summary>
    public static string Guard(MapConfig cfg, BellsBendData.Manifest m, Dictionary<string, object> report)
    {
        if (!cfg) throw new WaterBuildException("water: no MapConfig");
        var sha16 = ReportLakeMask(report);
        if (sha16 != ApprovedLakeMask)
            throw new WaterBuildException($"water: lake mask {sha16 ?? "missing"} != approved {ApprovedLakeMask}. The terrain changed under the water; " +
                "re-check banks and coverage, then update BellsBendWater.ApprovedLakeMask on purpose.");
        float delta = cfg.northLineZ - m.northLineZData;
        bool reportMoved = report.TryGetValue("northLineMoved", out var nl) && nl is Dictionary<string, object> d && d.TryGetValue("moved", out var mv) && mv is bool b && b;
        if (reportMoved || Mathf.Abs(delta) > LineMoveTolerance)
            throw new WaterBuildException($"water: north line moved (MapConfig.northLineZ {cfg.northLineZ} vs data {m.northLineZData:F2}, delta {delta:+0.0;-0.0} m, " +
                $"report moved={reportMoved}). Neck crossings are not re-derived for a moved line, so water would flood the cut (td-note §3 risk 2). Build error until they are.");
        return $"water guard ok: lakeMask={sha16} northLineZ={cfg.northLineZ} data={m.northLineZData:F2}";
    }

    /// <summary>First 16 hex chars (lower case) of the report's lakeMaskSha256; null if missing or too short.</summary>
    public static string ReportLakeMask(Dictionary<string, object> report)
    {
        var sha = report.TryGetValue("lakeMaskSha256", out var s) ? s as string : null;
        return sha != null && sha.Length >= 16 ? sha.Substring(0, 16).ToLowerInvariant() : null;
    }

    /// <summary>Guard, then build. Any failure after the guard (incl. WaterBody/WaterMaps hooks) is a WaterBuildException,
    /// so BuildAll rethrows it as a build error instead of logging it.</summary>
    public static string Build(MapConfig cfg, BellsBendData.Manifest m, Dictionary<string, object> report, bool save)
    {
        var log = new StringBuilder(Guard(cfg, m, report)).AppendLine();
        try { return BuildGuarded(cfg, m, report, save, log); }
        catch (Exception e) when (!(e is WaterBuildException)) { throw new WaterBuildException("water: build step failed: " + e.Message, e); }
    }

    static string BuildGuarded(MapConfig cfg, BellsBendData.Manifest m, Dictionary<string, object> report, bool save, StringBuilder log)
    {
        float w = cfg.WaterLevelY;
        int layer = WorldLayers.Water; // throws if the layer is missing

        Directory.CreateDirectory(Path.Combine(BellsBendData.ProjectRoot, Dir));
        var mesh = TileMesh(m.tileSize);
        var mat = PlaceholderMaterial(log);

        foreach (var old in EditorSceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == RootName).ToArray())
            UnityEngine.Object.DestroyImmediate(old);
        var root = new GameObject(RootName) { layer = layer };
        var info = root.AddComponent<BellsBendWaterInfo>();
        info.lakeMaskSha16 = ReportLakeMask(report); info.waterLevelY = w; info.tileSize = m.tileSize;
        info.gridOrigin = m.gridOrigin; info.tilesX = m.tilesX; info.tilesZ = m.tilesZ;

        foreach (var t in m.tiles.OrderBy(t => t.iz).ThenBy(t => t.ix))
        {
            var go = new GameObject($"Water_x{t.ix}_z{t.iz}") { layer = layer };
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(t.position.x, w, t.position.z);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        log.AppendLine($"water: {m.tiles.Count} quads {m.tileSize} m at y=WaterLevelY={w} layer={layer} extent X[{m.gridOrigin.x},{m.gridOrigin.x + m.tilesX * m.tileSize}] " +
            $"Z[{m.gridOrigin.y},{m.gridOrigin.y + m.tilesZ * m.tileSize}] lakeMask={ApprovedLakeMask}");

        log.AppendLine(AddWaterBody(root, cfg));
        log.AppendLine(BakeWaterMaps(cfg));

        if (save)
        {
            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            log.AppendLine($"water: scene saved {scene.path}");
        }
        return log.ToString();
    }

    /// <summary>One flat quad, 0..tileSize in local XZ, +Y normals, 0..1 UVs. Rewritten in place so its GUID survives rebuilds.</summary>
    static Mesh TileMesh(float s)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        bool created = !mesh;
        if (created) mesh = new Mesh();
        mesh.Clear();
        mesh.name = "WaterTile";
        mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(s, 0, 0), new Vector3(0, 0, s), new Vector3(s, 0, s) };
        mesh.normals = Enumerable.Repeat(Vector3.up, 4).ToArray();
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateBounds();
        if (created) AssetDatabase.CreateAsset(mesh, MeshPath);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    static Material PlaceholderMaterial(StringBuilder log)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "BellsBendWater" };
        mat.SetColor("_BaseColor", new Color(0.33f, 0.35f, 0.21f));
        mat.SetFloat("_Smoothness", 0.6f);
        AssetDatabase.CreateAsset(mat, MaterialPath);
        log.AppendLine($"water: created placeholder material {MaterialPath} (w-artist replaces it in place)");
        return mat;
    }

    static Type FindType(string assemblyQualified)
    {
        var t = Type.GetType(assemblyQualified);
        if (t != null) return t;
        var name = assemblyQualified.Split(',')[0].Trim();
        return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(x => x != null);
    }

    /// <summary>w-engineer's WaterBody on the root, configured from MapConfig and WaterMotion.asset; skipped (logged) until both exist.</summary>
    static string AddWaterBody(GameObject root, MapConfig cfg)
    {
        var type = FindType(WaterBodyType);
        if (type == null) return "water: WaterBody not present, skipped";
        var motionType = FindType("WashedAshore.World.WaterMotionSettings, WashedAshore.World");
        var motion = motionType != null ? AssetDatabase.LoadAssetAtPath(MotionPath, motionType) : null;
        if (!motion) return $"water: WARNING WaterBody skipped: {MotionPath} missing";
        var body = root.AddComponent(type);
        Invoke(type.GetMethod("Configure", new[] { typeof(MapConfig), motionType }), body, cfg, motion);
        return $"water: WaterBody configured with {MotionPath}";
    }

    /// <summary>w-artist's WaterMaps bake (sets _WaterMaps on the material itself); skipped (logged) if the baker is not present.</summary>
    static string BakeWaterMaps(MapConfig cfg)
    {
        var type = FindType(WaterMapsType);
        if (type == null) return "water: WaterMaps baker not present, skipped";
        return "water: WaterMaps " + Invoke(type.GetMethod("Bake", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(MapConfig) }, null), null, cfg);
    }

    /// <summary>Calls a WaterBody/WaterMaps hook; any failure inside it becomes a WaterBuildException (inner kept).</summary>
    public static object Invoke(MethodInfo mi, object target, params object[] args)
    {
        if (mi == null) throw new WaterBuildException("water: hook method not found (contract changed?)");
        try { return mi.Invoke(target, args); }
        catch (TargetInvocationException e)
        {
            var inner = e.InnerException;
            if (inner is WaterBuildException) { ExceptionDispatchInfo.Capture(inner).Throw(); throw; }
            throw new WaterBuildException($"water hook {mi.DeclaringType.Name}.{mi.Name} failed: {inner.Message}", inner);
        }
    }

    /// <summary>W2 seam evidence: oblique and top-down shots of a four-tile water corner (fog off), PNGs in _bmad-output/poc.</summary>
    public static string SeamShots()
    {
        var root = GameObject.Find(RootName);
        if (!root) return "no Water root; run BellsBendWater.Build()";
        var info = root.GetComponent<BellsBendWaterInfo>();
        // Corner shared by x0/x1 and z1/z2: x0 tiles are all-water, so a gap would show the bed 10 m down.
        var c = new Vector3(info.gridOrigin.x + info.tileSize, info.waterLevelY, info.gridOrigin.y + 2 * info.tileSize);
        var sb = new StringBuilder($"seam corner ({c.x},{c.z})");
        sb.Append(" top=" + Shot("water-seam-top.png", c + Vector3.up * 60f, Quaternion.Euler(90f, 0f, 0f)));
        sb.Append(" oblique=" + Shot("water-seam-oblique.png", c + new Vector3(-40f, 25f, -40f), Quaternion.LookRotation(c - (c + new Vector3(-40f, 25f, -40f)))));
        return sb.ToString();
    }

    static string Shot(string file, Vector3 pos, Quaternion rot)
    {
        const int W = 1920, H = 1080;
        bool fog = RenderSettings.fog; RenderSettings.fog = false;
        var go = new GameObject("_WaterSeamCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(pos, rot);
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 3000f;
        var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
        Directory.CreateDirectory(BellsBendData.PocDir);
        var path = Path.Combine(BellsBendData.PocDir, file);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(go);
        RenderSettings.fog = fog;
        return path;
    }
}
