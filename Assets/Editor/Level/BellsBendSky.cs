using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer: horizon haze for Bells Bend (ruling/water-look-scum-horizon fix 2 + addendum; w-td: procedural sky). Rerunnable.
//   unity command eval "return BellsBendSky.Build();"     (batch: -executeMethod BellsBendSky.BuildBatch)
// The open water met a blue sky in a crisp line. Fix, colour only: BellsBendSky.mat on the built-in Skybox/Procedural
// shader with a greyer tint, thicker atmosphere and a warm grey ground; the fog colour is then measured from that sky
// just above the horizon, so the fogged far water meets the sky without a band. Fog distances stay 120-520 m, so the
// near water (0-120 m) and the bluff view get no more haze. The default environment reflection (what the water
// reflects) and ambient are regenerated from the new sky with DynamicGI.UpdateEnvironment (no baked lighting data).
public static class BellsBendSky
{
    /// <summary>Warm humid grey (sRGB): the sky's ground colour, below the horizon.</summary>
    public static readonly Color Haze = new Color(0.78f, 0.76f, 0.70f);
    /// <summary>Built-in Default-Skybox is SkyTint 0.5 grey, AtmosphereThickness 1, Exposure 1.3.</summary>
    public static readonly Color SkyTint = new Color(0.53f, 0.49f, 0.47f);
    public const float Atmosphere = 1.1f, Exposure = 1.0f;
    public const float FogStart = 120f, FogEnd = 520f;

    public const string MaterialPath = BellsBendData.WorldRoot + "/Sky/BellsBendSky.mat";
    public const string RootName = "Sky";
    const int Face = 512;
    static string ShotDir => Path.Combine(BellsBendData.PocDir, "water-horizon");

    [MenuItem("Washed Ashore/Level/Build Bells Bend sky haze")]
    static void BuildMenu() => Debug.Log(Build());

    public static void BuildBatch()
    {
        var path = Path.Combine(BellsBendData.ProjectRoot, "Logs", "bells-bend-sky-build.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        try { File.WriteAllText(path, Build() + "\ndone\n"); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(path, "FAILED: " + e); EditorApplication.Exit(1); }
    }

    public static string Build()
    {
        if (EditorSceneManager.GetActiveScene().path != BellsBendLevel.ScenePath) EditorSceneManager.OpenScene(BellsBendLevel.ScenePath);
        var cfg = BellsBendData.LoadConfig();
        var log = new StringBuilder($"BellsBendSky.Build {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
        Directory.CreateDirectory(ShotDir);
        var poses = Poses(cfg);
        var before = poses.Select(p => Shot(p.name + "-before.png", p.eye, p.look)).ToArray();
        var upperBefore = SkyOnly(Quaternion.Euler(-45f, 30f, 0f));
        var sky = RenderSettings.skybox;
        log.AppendLine($"before: skybox={(sky ? sky.name + " " + AssetDatabase.GetAssetPath(sky) : "none")} fog={RenderSettings.fogColor} {RenderSettings.fogMode} {RenderSettings.fogStartDistance}-{RenderSettings.fogEndDistance}");

        log.AppendLine(Apply(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat")));
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray()) UnityEngine.Object.DestroyImmediate(old);
        new GameObject(RootName).AddComponent<BellsBendSkyInit>();
        log.AppendLine($"root '{RootName}' with BellsBendSkyInit (DynamicGI.UpdateEnvironment at load)");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        var refl = ReflectionProbe.defaultTexture;
        log.AppendLine($"after: skybox={MaterialPath} fog=sRGB({RenderSettings.fogColor.r:F3},{RenderSettings.fogColor.g:F3},{RenderSettings.fogColor.b:F3}) " +
            $"Linear {FogStart}-{FogEnd}; default reflection {(refl ? $"{refl.name} {refl.width}px {refl.dimension}" : "none")} (generated, no asset); scene saved");

        var upperAfter = SkyOnly(Quaternion.Euler(-45f, 30f, 0f));
        log.AppendLine($"upper sky (45 deg up, sky only, linear) mean channel diff before/after = {MeanDiff(upperBefore, upperAfter):F4}");
        for (int i = 0; i < poses.Length; i++)
        {
            var after = Shot(poses[i].name + "-after.png", poses[i].eye, poses[i].look);
            log.AppendLine($"{poses[i].name}: horizon row step before={HorizonStep(before[i].px):F3} after={HorizonStep(after.px):F3} (max mean adjacent-row step, centre third); " +
                $"lower third mean diff={MeanDiff(before[i].px, after.px, 0, ShotH / 3):F4}; water-area blue excess before={BlueExcess(before[i].px):F3} after={BlueExcess(after.px):F3}; " +
                $"{Path.GetFileName(before[i].path)} {Path.GetFileName(after.path)}");
        }
        return log.ToString();
    }

    /// <summary>Material on the built-in Skybox/Procedural shader (no shader copy), fog colour from that sky's horizon,
    /// then ambient and default reflection regenerated from it.</summary>
    static string Apply(Material source)
    {
        Directory.CreateDirectory(Path.Combine(BellsBendData.ProjectRoot, Path.GetDirectoryName(MaterialPath)));
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!mat) { mat = new Material(source) { name = "BellsBendSky" }; AssetDatabase.CreateAsset(mat, MaterialPath); }
        mat.shader = source.shader;
        mat.CopyPropertiesFromMaterial(source);
        mat.SetColor("_SkyTint", SkyTint);
        mat.SetFloat("_AtmosphereThickness", Atmosphere);
        mat.SetColor("_GroundColor", Haze);
        mat.SetFloat("_Exposure", Exposure);
        EditorUtility.SetDirty(mat);
        RenderSettings.skybox = mat;

        // Mean sky colour 0.5-1.5 deg above the horizon over 8 headings (linear), from sky-only renders (centre half of each row).
        var sum = Color.clear; int n = 0;
        for (int h = 0; h < 8; h++)
        {
            var px = SkyOnly(Quaternion.Euler(0f, h * 45f, 0f));
            for (int row = 0; row < Face; row++)
            {
                float elev = Mathf.Atan((row + 0.5f) / Face * 2f - 1f) * Mathf.Rad2Deg;
                if (elev < 0.5f || elev > 1.5f) continue;
                for (int x = Face / 4; x < 3 * Face / 4; x += 8) { sum += px[row * Face + x]; n++; }
            }
        }
        var horizon = sum / n;
        var fog = horizon.gamma; fog.a = 1f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = fog;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;
        DynamicGI.UpdateEnvironment();
        return $"sky: {MaterialPath} on {source.shader.name} (built-in), SkyTint={SkyTint} Atmosphere={Atmosphere} Exposure={Exposure} " +
            $"Ground=sRGB({Haze.r:F2},{Haze.g:F2},{Haze.b:F2}); sky at horizon (0.5-1.5 deg, 8 headings) sRGB=({fog.r:F3},{fog.g:F3},{fog.b:F3}) -> fog colour; " +
            "DynamicGI.UpdateEnvironment (ambient + default reflection)";
    }

    /// <summary>Skybox only (no geometry), 90 deg square HDR render in linear colour.</summary>
    static Color[] SkyOnly(Quaternion rot)
    {
        var go = new GameObject("_SkyCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(Vector3.zero, rot);
        cam.fieldOfView = 90f; cam.aspect = 1f; cam.cullingMask = 0; cam.clearFlags = CameraClearFlags.Skybox; cam.allowHDR = true;
        var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(Face, Face, RenderTextureFormat.ARGBHalf, 24) { sRGB = false });
        var px = Read(cam, rt, Face, Face, TextureFormat.RGBAHalf, true).GetPixels();
        RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(go);
        return px;
    }

    static Texture2D lastTex;
    static Texture2D Read(Camera cam, RenderTexture rt, int w, int h, TextureFormat fmt, bool linear)
    {
        cam.targetTexture = rt; cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        if (lastTex) UnityEngine.Object.DestroyImmediate(lastTex);
        lastTex = new Texture2D(w, h, fmt, false, linear);
        lastTex.ReadPixels(new Rect(0, 0, w, h), 0, 0); lastTex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null;
        return lastTex;
    }

    // ---------------- evidence shots ----------------

    const int ShotW = 1920, ShotH = 1080;

    static (string path, Color32[] px) Shot(string file, Vector3 eye, Vector3 look)
    {
        var go = new GameObject("_SkyShotCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        cam.fieldOfView = 60f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 6000f;
        var rt = RenderTexture.GetTemporary(ShotW, ShotH, 24, RenderTextureFormat.ARGB32);
        var tex = Read(cam, rt, ShotW, ShotH, TextureFormat.RGBA32, false);
        var path = Path.Combine(ShotDir, file);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        var px = tex.GetPixels32();
        RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(go);
        return (path, px);
    }

    /// <summary>Bank (Cleeces Ferry landing, 6 m inland), north line across the east channel, Buzzard Bluff top
    /// (same framings as w-artist's W5 look shots).</summary>
    static (string name, Vector3 eye, Vector3 look)[] Poses(MapConfig cfg)
    {
        var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        var v = BellsBendData.LoadVectors(cfg);
        Vector3 L(string id) { var l = v.landmarks.First(x => x.id == id); return new Vector3(l.xz.x, 0f, l.xz.y); }
        Vector3 Eye(Vector3 p, float up) { p.y = WashedAshore.Gameplay.TerrainQuery.Height(p) + up; return p; }
        float W = cfg.WaterLevelY;
        var ferry = NearestShore(maps, L("CleecesFerryLanding"), -1f, 0f); var ferryOut = Outward(maps, ferry);
        var ec = new Vector3(v.eastCrossing.x, 0f, v.eastCrossing.y);
        var ecEye = NearestShore(maps, ec + new Vector3(0f, 0f, -20f), 4f, 8f);
        var bluff = L("BuzzardBluff"); var bluffEdge = NearestShore(maps, bluff, -1f, 0f);
        return new[]
        {
            ("bank", Eye(ferry - ferryOut * 6f, 1.7f), new Vector3(ferry.x, W, ferry.z) + ferryOut * 40f),
            ("north-line-east-channel", Eye(ecEye, 1.7f), new Vector3(ec.x + 90f, W, cfg.northLineZ)),
            ("bluff-top", Eye(bluff, 1.7f), new Vector3(bluffEdge.x, W, bluffEdge.z) + Outward(maps, bluffEdge) * 30f),
        };
    }

    static Vector3 NearestShore(BellsBendLevelMaps maps, Vector3 p, float lo, float hi)
    {
        for (float r = 0f; r <= 400f; r += 2f)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.PI * r));
            for (int k = 0; k < steps; k++)
            {
                float a = k * 2f * Mathf.PI / steps;
                var q = p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float sd = maps.ShoreDistance(q);
                if (sd >= lo && sd <= hi) return q;
            }
        }
        throw new InvalidOperationException($"no shore cell near ({p.x},{p.z})");
    }

    static Vector3 Outward(BellsBendLevelMaps maps, Vector3 p)
    {
        float sd = maps.ShoreDistance(p);
        var g = new Vector3(maps.ShoreDistance(p + new Vector3(2f, 0f, 0f)) - sd, 0f, maps.ShoreDistance(p + new Vector3(0f, 0f, 2f)) - sd);
        return g.sqrMagnitude < 1e-6f ? Vector3.forward : -g.normalized;
    }

    // ---------------- metrics ----------------

    /// <summary>Largest mean colour step between adjacent rows in the centre third of the image (the crisp-horizon measure).</summary>
    static float HorizonStep(Color32[] px)
    {
        float worst = 0f;
        for (int y = 1; y < ShotH; y++)
        {
            float rowStep = 0f; int n = 0;
            for (int x = ShotW / 3; x < 2 * ShotW / 3; x += 4)
            {
                Color a = px[y * ShotW + x], b = px[(y - 1) * ShotW + x];
                rowStep += Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)); n++;
            }
            worst = Mathf.Max(worst, rowStep / n);
        }
        return worst;
    }

    /// <summary>Mean (b - max(r, g)) over the lower half of the image (water in these framings): above 0 = blue cast.</summary>
    static float BlueExcess(Color32[] px)
    {
        double s = 0; long n = 0;
        for (int y = 0; y < ShotH / 2; y += 2) for (int x = 0; x < ShotW; x += 2)
            {
                Color c = px[y * ShotW + x];
                s += c.b - Mathf.Max(c.r, c.g); n++;
            }
        return (float)(s / n);
    }

    static float MeanDiff(Color32[] a, Color32[] b, int y0, int y1)
    {
        double s = 0; long n = 0;
        for (int y = y0; y < y1; y++) for (int x = 0; x < ShotW; x += 2)
            {
                Color p = a[y * ShotW + x], q = b[y * ShotW + x];
                s += (Mathf.Abs(p.r - q.r) + Mathf.Abs(p.g - q.g) + Mathf.Abs(p.b - q.b)) / 3f; n++;
            }
        return (float)(s / n);
    }

    static float MeanDiff(Color[] a, Color[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) s += (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b)) / 3f;
        return (float)(s / a.Length);
    }
}
