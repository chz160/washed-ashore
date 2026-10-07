using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

// Technical-artist checks for the Bells Bend water look (spec W5, W10 evidence; no player window):
//   1. the water shader compiles for PC (D3D11) and web (GLES3x / WebGL), every keyword combination it uses;
//   2. the downstream sign: the shader's flow direction, mirrored on the CPU, runs clockwise round the bend;
//   3. offscreen editor-camera look shots (bank, bluff top, water level, shallow edge, west neck step, lip, north line).
// Run headless: unity run <project> -- -executeMethod BellsBendWaterLook.RunBatch
// Output: _bmad-output/poc/water-look/ (look-log.txt + PNGs). Read-only on assets; the scene is not saved.
public static class BellsBendWaterLook
{
    static string OutDir => Path.Combine(BellsBendData.PocDir, "water-look");

    public static void RunBatch()
    {
        Directory.CreateDirectory(OutDir);
        var path = Path.Combine(OutDir, "look-log.txt");
        try
        {
            File.WriteAllText(path, Run() + "\ndone\n");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            File.WriteAllText(path, "FAILED: " + e);
            EditorApplication.Exit(1);
        }
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine(CheckShader());
        if (EditorSceneManager.GetActiveScene().path != BellsBendLevel.ScenePath) EditorSceneManager.OpenScene(BellsBendLevel.ScenePath);
        var cfg = BellsBendData.LoadConfig();
        sb.AppendLine(ApplyShaderDefaults());
        sb.AppendLine(RippleDeterminism());
        sb.AppendLine(FlowSignCheck(cfg));
        sb.AppendLine(Shots(cfg));
        return sb.ToString();
    }

    // ---------------- 1. shader compile, PC and web ----------------

    public static string CheckShader()
    {
        var shader = Shader.Find(BellsBendWaterMaps.ShaderName);
        if (!shader) throw new FileNotFoundException("shader missing: " + BellsBendWaterMaps.ShaderName);
        var sb = new StringBuilder($"shader {shader.name}: hasError={ShaderUtil.ShaderHasError(shader)}");
        foreach (var m in ShaderUtil.GetShaderMessages(shader)) sb.Append($"\n  {m.severity} {m.platform} line {m.line}: {m.message}");
        var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
        var targets = new[] { (ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64), (ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL) };
        var sets = new[] { new string[0], new[] { "FOG_LINEAR" }, new[] { "FOG_EXP2" }, new[] { "_WATER_DEBUG_HEIGHT" } };
        int bad = 0;
        foreach (var (platform, target) in targets)
            foreach (var kw in sets)
                foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                {
                    var r = pass.CompileVariant(stage, kw, platform, target);
                    if (r.Success) continue;
                    bad++;
                    sb.Append($"\n  FAIL {platform} {stage} [{string.Join(",", kw)}]: " + string.Join(" | ", r.Messages.Select(m => $"line {m.line}: {m.message}")));
                }
        sb.Append($"\n  variants compiled: {targets.Length * sets.Length * 2 - bad}/{targets.Length * sets.Length * 2} (D3D + GLES3x/WebGL)");
        if (bad > 0 || ShaderUtil.ShaderHasError(shader)) throw new System.InvalidOperationException(sb.ToString());
        return sb.ToString();
    }

    // The look values live in the shader's property defaults (one diffable source). Copies them onto the material,
    // leaving the per-build values Bake sets (maps, transform, north line, neck, ripple texture).
    public static string ApplyShaderDefaults()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BellsBendWaterMaps.MaterialPath);
        var shader = mat.shader;
        var keep = new HashSet<string> { "_WaterMaps", "_WaterMapsST", "_NorthLineZ", "_NeckX", "_RippleTex", "_DebugHeight" };
        int n = 0;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            var name = shader.GetPropertyName(i);
            if (keep.Contains(name)) continue;
            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: mat.SetFloat(name, shader.GetPropertyDefaultFloatValue(i)); n++; break;
                case ShaderPropertyType.Vector: mat.SetVector(name, shader.GetPropertyDefaultVectorValue(i)); n++; break;
                case ShaderPropertyType.Color: mat.SetColor(name, shader.GetPropertyDefaultVectorValue(i)); n++; break;
            }
        }
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return $"material: {n} look values set from {shader.name} defaults";
    }

    // AF1: the ripple texture Bake rewrites every build must be byte-identical run to run (fixed seed, no clock).
    public static string RippleDeterminism()
    {
        string Hash(byte[] b) { using (var h = System.Security.Cryptography.SHA256.Create()) return System.BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").Substring(0, 16).ToLowerInvariant(); }
        string a = Hash(BellsBendWaterMaps.RippleBytes()), b = Hash(BellsBendWaterMaps.RippleBytes());
        string onDisk = Hash(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), BellsBendWaterMaps.RipplePath)));
        return $"ripple determinism: generator run1={a} run2={b} {(a == b ? "identical" : "DIFFERENT")}; WaterRipple.png on disk sha256_16={onDisk}";
    }

    // ---------------- 2. downstream sign ----------------

    // Mirrors the shader's flow direction (BellsBendWater.shader, "Flow along the bank tangent") on the CPU, 10 m out from
    // every inner-bank vertex south of the blend band, and checks it against the local downstream direction of the bank.
    // innerBank runs from the west crossing down the west side and up the east side (counter-clockwise), and the
    // Cumberland runs clockwise round the bend, so downstream at vertex i is bank[i-1] - bank[i+1].
    public static string FlowSignCheck(MapConfig cfg)
    {
        var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BellsBendWaterMaps.MaterialPath);
        float sign = mat.GetFloat("_FlowSign");
        var bank = BellsBendData.LoadVectors(cfg).innerBank;
        int n = 0, ok = 0;
        var against = new StringBuilder();
        for (int i = 1; i < bank.Count - 1; i++)
        {
            var b = bank[i];
            if (b.y > cfg.northLineZ - 60f) continue; // the blend band and the north channels use the channel rule
            var p = OutTo(maps, new Vector3(b.x, 0f, b.y), -10f);
            if (p == null) continue;
            float sd = maps.ShoreDistance(p.Value);
            // Same differences as the shader: +-4 m central.
            var g = new Vector2(maps.ShoreDistance(p.Value + new Vector3(4f, 0f, 0f)) - maps.ShoreDistance(p.Value - new Vector3(4f, 0f, 0f)),
                                maps.ShoreDistance(p.Value + new Vector3(0f, 0f, 4f)) - maps.ShoreDistance(p.Value - new Vector3(0f, 0f, 4f))) * 0.125f;
            if (g.magnitude < 0.25f) continue;
            var dir = new Vector2(-g.y, g.x).normalized * sign;
            var downstream = (bank[i - 1] - bank[i + 1]).normalized;
            n++;
            if (Vector2.Dot(dir, downstream) > 0f) ok++;
            else against.Append($"\n  against: ({p.Value.x:F0},{p.Value.z:F0}) flow ({dir.x:F2},{dir.y:F2}) bank downstream ({downstream.x:F2},{downstream.y:F2})");
        }
        return $"flow sign: _FlowSign={sign}; {ok}/{n} inner-bank stations (10 m out, south of the line) drift downstream along the bank " +
               "(Cumberland clockwise round Bells Bend: south down the east side, north up the west; OSM way 40806685, USGS 03431500)" + against;
    }

    // Walks from p down the shore-distance gradient until sd <= target (water side), or null.
    static Vector3? OutTo(BellsBendLevelMaps maps, Vector3 p, float target)
    {
        for (int i = 0; i < 80; i++)
        {
            float sd = maps.ShoreDistance(p);
            if (sd <= target) return p;
            var g = new Vector3(maps.ShoreDistance(p + new Vector3(2f, 0f, 0f)) - sd, 0f, maps.ShoreDistance(p + new Vector3(0f, 0f, 2f)) - sd);
            if (g.sqrMagnitude < 1e-4f) return null;
            p -= g.normalized * 1f;
        }
        return null;
    }

    // ---------------- 3. look shots ----------------

    public static string Shots(MapConfig cfg)
    {
        var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        var v = BellsBendData.LoadVectors(cfg);
        var lm = v.landmarks.ToDictionary(l => l.id, l => new Vector3(l.xz.x, 0f, l.xz.y));
        float W = cfg.WaterLevelY;
        PushDemoWaves();
        var sb = new StringBuilder("look shots (fog on, editor camera, no player):");

        // Bank: stand 6 m inland of the Cleeces Ferry landing waterline, look out over the water.
        var ferryEdge = NearestShore(maps, lm["CleecesFerryLanding"], -1f, 0f);
        var ferryOut = Outward(maps, ferryEdge);
        var bankEye = Ground(ferryEdge - ferryOut * 6f) + 1.7f;
        sb.Append(Shot("w5-bank.png", WithY(ferryEdge - ferryOut * 6f, bankEye), WithY(ferryEdge + ferryOut * 40f, W)));

        // Bluff top: Buzzard Bluff, look down to the water at its foot (bluff base scum, no blue cast).
        var bluff = lm["BuzzardBluff"];
        var bluffEdge = NearestShore(maps, bluff, -1f, 0f);
        sb.Append(Shot("w5-bluff-top.png", WithY(bluff, Ground(bluff) + 1.7f), WithY(bluffEdge + Outward(maps, bluffEdge) * 30f, W)));

        // Water level: a swimmer's eye 15 m off the ferry bank, looking along the bank.
        var swim = ferryEdge + ferryOut * 15f;
        var along = new Vector3(ferryOut.z, 0f, -ferryOut.x);
        sb.Append(Shot("w5-water-level.png", WithY(swim, W + 0.15f), WithY(swim + along * 60f - ferryOut * 8f, W + 0.3f)));

        // Shallow edge: look 50 degrees down onto a low bank waterline (the boat slipway). Bank visible through the shallows.
        var slip = NearestShore(maps, lm["BoatSlipway"], -1f, 0f);
        var slipOut = Outward(maps, slip);
        sb.Append(Shot("w5-shallow-edge.png", WithY(slip - slipOut * 4f, W + 5f), WithY(slip + slipOut * 2f, W)));
        // w-director's side-by-side check (ruling water-scum-aspect-metric): same pose at scum stretch 3.6 and 4.2,
        // set temporarily on the material and restored to the shader default afterwards.
        var mat = AssetDatabase.LoadAssetAtPath<Material>(BellsBendWaterMaps.MaterialPath);
        float stretch = mat.GetFloat("_FoamStretch");
        foreach (var s in new[] { 3.6f, 4.2f })
        {
            mat.SetFloat("_FoamStretch", s);
            sb.Append(Shot($"w5-shallow-edge-stretch{s:F1}.png", WithY(slip - slipOut * 4f, W + 5f), WithY(slip + slipOut * 2f, W)));
        }
        mat.SetFloat("_FoamStretch", stretch);
        sb.Append($"\n  _FoamStretch restored to {mat.GetFloat("_FoamStretch")}");

        // 0.31 m lip at the waterline: from the water, 8 m off the low slipway bank, looking at it (no vegetation in front).
        sb.Append(Shot("w5-lip.png", WithY(slip + slipOut * 8f, W + 0.6f), WithY(slip, W + 0.2f)));

        // West neck corner step (3.41 m, ruled exception) at the west north-line crossing.
        var wc = new Vector3(v.westCrossing.x, 0f, v.westCrossing.y);
        // From the water 30 m off the crossing, at 2 m, so the step and the waterline are both in frame.
        var wcOut = Outward(maps, NearestShore(maps, wc + new Vector3(0f, 0f, -10f), -1f, 0f));
        var wcEye = wc + wcOut * 30f + new Vector3(0f, 0f, -10f);
        sb.Append(Shot("w5-west-neck-step.png", WithY(wcEye, W + 2f), WithY(wc, W + 1f)));

        // North line across the east channel: no seam where flow and foam blend at the fence.
        var ec = new Vector3(v.eastCrossing.x, 0f, v.eastCrossing.y);
        var ecEye = NearestShore(maps, ec + new Vector3(0f, 0f, -20f), 4f, 8f);
        sb.Append(Shot("w5-north-line-east-channel.png", WithY(ecEye, Ground(ecEye) + 1.7f), WithY(new Vector3(ec.x + 90f, 0f, cfg.northLineZ), W)));


        // Haze continuity (w-director's check 1): climbing the Buzzard bluff face at its XZ, eye from W + 2 to the top,
        // looking at the same far-water point; then a pan at the bank pose (pitch -12 / -4 / +4 degrees). The haze is a
        // function of the water point's distance and view angle, so it must change smoothly, without a step.
        var bluffFar = WithY(bluffEdge + Outward(maps, bluffEdge) * 600f, W);
        float top = Ground(bluff) + 1.7f;
        foreach (var hgt in new[] { W + 2f, W + 8f, W + 16f, top })
            sb.Append(Shot($"w5-haze-climb-{hgt - W:F0}m.png", WithY(bluff + (bluffEdge - bluff) * Mathf.InverseLerp(top, W + 2f, hgt), hgt), bluffFar));
        var bankEyePos = WithY(ferryEdge - ferryOut * 6f, bankEye);
        foreach (var pitch in new[] { -12f, -4f, 4f })
        {
            var flat = new Vector3(ferryOut.x, 0f, ferryOut.z).normalized;
            var dir = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, flat)) * flat;
            sb.Append(Shot($"w5-haze-pan{pitch:+0;-0}.png", bankEyePos, bankEyePos + dir * 50f));
        }

        // Drift in motion (director note 3), without a window: the same near-top-down view (14 degrees off vertical, north up) of the east bank scum band
        // below Buzzard (flow runs south there) at flow phases 0.10 / 0.25 / 0.40. The patches should step south frame to frame.
        var east = NearestShore(maps, new Vector3(318f, 0f, 1561f), -2.5f, -1.5f);
        var period = AssetDatabase.LoadAssetAtPath<WaterMotionSettings>(MotionPath).flowCyclePeriod;
        foreach (var ph in new[] { 0.10f, 0.25f, 0.40f })
        {
            PushMotion(ph * period); // flow phase ph at water time ph x flowCyclePeriod
            sb.Append(Shot($"w5-drift-phase{ph:F2}.png", WithY(east, W + 4f), WithY(east + new Vector3(0f, 0f, 1f), W), 60f));
        }

        ClearDemoWaves();
        return sb.ToString();
    }

    // WaterBody pushes only in Play, so in this headless edit-mode run the same globals are pushed here from
    // w-engineer's WaterMotion.asset with WaterMotion's own Phase / FlowPhase at water time t (no hand-typed waves).
    const string MotionPath = "Assets/World/Water/WaterMotion.asset";

    static void PushDemoWaves() => PushMotion(0.6);

    static void PushMotion(double t)
    {
        var motion = AssetDatabase.LoadAssetAtPath<WaterMotionSettings>(MotionPath);
        if (!motion) throw new FileNotFoundException("WaterMotion settings missing", MotionPath);
        for (int i = 0; i < WaterMotion.MaxWaves; i++)
        {
            bool on = motion.waves != null && i < motion.waves.Length && motion.waves[i].wavelength > 0f;
            var w = on ? motion.waves[i] : default;
            var d = on ? w.Direction : Vector2.zero;
            Shader.SetGlobalVector("_WaterWave" + i, on ? new Vector4(w.amplitude, w.K, d.x, d.y) : Vector4.zero);
            Shader.SetGlobalVector("_WaterWaveSpeedPhase" + i, on ? new Vector4(w.speed, WaterMotion.Phase(w, t), 0f, 0f) : Vector4.zero);
        }
        Shader.SetGlobalFloat("_WaterFlowPhase", WaterMotion.FlowPhase(motion.flowCyclePeriod, t));
    }

    static void ClearDemoWaves()
    {
        for (int i = 0; i < 4; i++) { Shader.SetGlobalVector("_WaterWave" + i, Vector4.zero); Shader.SetGlobalVector("_WaterWaveSpeedPhase" + i, Vector4.zero); }
        Shader.SetGlobalFloat("_WaterFlowPhase", 0f);
    }

    static Vector3 WithY(Vector3 p, float y) => new Vector3(p.x, y, p.z);
    static float Ground(Vector3 p) => TerrainQuery.Height(p);

    // Nearest 2 m cell to p whose shore distance lies in [lo, hi] (searching rings out to 400 m).
    static Vector3 NearestShore(BellsBendLevelMaps maps, Vector3 p, float lo, float hi)
    {
        for (float r = 0f; r <= 400f; r += 2f)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(2f * Mathf.PI * r / 2f));
            for (int k = 0; k < steps; k++)
            {
                float a = k * 2f * Mathf.PI / steps;
                var q = p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                float sd = maps.ShoreDistance(q);
                if (sd >= lo && sd <= hi) return q;
            }
        }
        throw new System.InvalidOperationException($"no shore cell near ({p.x},{p.z})");
    }

    // Unit XZ direction from land toward water (down the shore-distance gradient).
    static Vector3 Outward(BellsBendLevelMaps maps, Vector3 p)
    {
        var g = Vector3.zero;
        for (float r = 4f; r <= 12f; r += 4f)
            g += new Vector3(maps.ShoreDistance(p + new Vector3(r, 0f, 0f)) - maps.ShoreDistance(p - new Vector3(r, 0f, 0f)), 0f,
                             maps.ShoreDistance(p + new Vector3(0f, 0f, r)) - maps.ShoreDistance(p - new Vector3(0f, 0f, r)));
        return g.sqrMagnitude > 1e-6f ? -g.normalized : Vector3.forward;
    }

    static string Shot(string file, Vector3 pos, Vector3 target, float fov = 60f)
    {
        const int Wd = 1920, H = 1080;
        var go = new GameObject("_WaterLookCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
        cam.fieldOfView = fov; cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000f;
        var rt = RenderTexture.GetTemporary(Wd, H, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(Wd, H, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, Wd, H), 0, 0); tex.Apply();
        RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
        var path = Path.Combine(OutDir, file);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
        return $"\n  {file}: eye ({pos.x:F1},{pos.y:F2},{pos.z:F1}) -> ({target.x:F1},{target.y:F2},{target.z:F1})";
    }
}
