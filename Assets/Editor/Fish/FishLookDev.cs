using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

namespace WashedAshore.Fish.Editor
{
    /// <summary>
    /// G4 look-dev stills for f-director (pre-gate, approved by f-td in gate/fish-technique-addendum-1). Builds an isolated
    /// scene, Assets/Scenes/LookDev/FishLookDev.unity (never in the build list), from copies of World.unity's terrain tiles,
    /// water quads, sun and sky near the Cleeces Ferry shelf and Buzzard Bluff. World.unity is opened additively, read and
    /// closed without saving. Fish are temporary (DontSave) skinned instances of the imported Fish1/Fish2 posed from Swim,
    /// drawn with FishUnderwater.shader, whose water values are copied at run time from the water quads' material.
    /// Shots (opaque RGB PNG, TestResults/fish-lookdev/), under f-director's tier rules and rule V2: the V1 and V2 rows with the
    /// top of the body at 0.3 / 0.6 / 1.0 m on the shelf from the 1.7 m shore eye (60 and 20 deg) and from bluff-top height
    /// (26.6 m above W, 20 m inland); a V2 flash; a rise and a dimple; gar basking; dry reference sheets of every variant; a gar
    /// Swim strip; the real Buzzard Bluff view. "record" shots switch a rule off, for the record only.
    /// Run headless: unity run <project> -- -executeMethod WashedAshore.Fish.Editor.FishLookDev.RunBatch, then read lookdev-log.txt.
    /// </summary>
    public static partial class FishLookDev
    {
        public const string ScenePath = "Assets/Scenes/LookDev/FishLookDev.unity";
        const string FishShader = "WashedAshore/FishUnderwater", RingShader = "WashedAshore/FishRing", WaterShader = "WashedAshore/BellsBendWater";
        const string MotionPath = "Assets/World/Water/WaterMotion.asset";
        const float ShoreEye = 1.7f, BluffEyeAboveW = 26.6f, BluffInland = 20f, RSub = 25f;
        static readonly float[] Depths = { 0.3f, 0.6f, 1.0f };
        static string OutDir => Path.Combine(BellsBendData.ProjectRoot, "TestResults", "fish-lookdev");

        // G3 (artist-items-3-5.md §4): model, adult TL range (cm, G1), Y and X scale, back / belly / fin swatches (sRGB), mottle.
        struct Look
        {
            public string id, model, back, belly, fins, tier; public float tlMin, tlMax, y, x, mottle;
            public Look(string id, string model, float tlMin, float tlMax, float y, float x, string back, string belly, string fins, float mottle, string tier = "V3")
            { this.tier = tier; this.id = id; this.model = model; this.tlMin = tlMin; this.tlMax = tlMax; this.y = y; this.x = x; this.back = back; this.belly = belly; this.fins = fins; this.mottle = mottle; }
            public float TL => (tlMin + tlMax) * 0.5f / 100f;
        }

        static readonly Look[] Deep =
        {
            new Look("bluegill", "Fish2", 13, 24, 0.94f, 1.0f, "4F5A48", "A08F60", "4A4E40", 0.10f, "V1"),
            new Look("crappie", "Fish2", 23, 38, 0.83f, 0.9f, "5E6656", "B9BCB2", "5A5E52", 0.30f, "V2"),
            new Look("gizzard-shad", "Fish2", 23, 36, 0.75f, 0.8f, "646C68", "C6C8C0", "60645E", 0.00f),
            new Look("buffalo-alt-Fish2", "Fish2", 38, 76, 0.70f, 1.0f, "5E5A48", "B0AA92", "56524A", 0.05f, "V2"),
        };
        static readonly Look[] Bass =
        {
            new Look("largemouth", "Fish1", 25, 50, 1.20f, 1.1f, "5C6440", "C9C4A0", "6A6A48", 0.20f, "V1"),
            new Look("spotted-bass", "Fish1", 25, 43, 1.15f, 1.05f, "646040", "C8C0A0", "64604A", 0.25f, "V2"),
            new Look("smallmouth", "Fish1", 25, 50, 1.15f, 1.05f, "6E6042", "C2B48E", "6A5A40", 0.25f, "V2"),
            new Look("white-bass", "Fish1", 23, 38, 1.40f, 1.1f, "6A706A", "C4C6BE", "646862", 0.00f),
            new Look("sauger", "Fish1", 30, 38, 0.80f, 0.9f, "66603E", "CFC8AA", "6A6448", 0.35f),
            new Look("skipjack", "Fish1", 30, 41, 1.15f, 0.85f, "566A66", "C8CCC4", "5E6462", 0.00f),
        };
        static readonly Look[] Big =
        {
            new Look("channel-cat", "Fish1", 30, 80, 0.86f, 1.2f, "5A6052", "B8B49C", "4E5048", 0.10f),
            new Look("flathead", "Fish1", 38, 114, 0.80f, 1.4f, "6B5B40", "B5A783", "5A4C36", 0.35f),
            new Look("blue-cat", "Fish1", 50, 112, 0.95f, 1.2f, "5E6670", "C2C4BE", "565C62", 0.00f),
            new Look("drum", "Fish1", 30, 50, 1.40f, 1.15f, "6E7270", "BEBEB4", "6A6C68", 0.00f),
            new Look("buffalo", "Fish1", 38, 76, 1.55f, 1.2f, "5E5A48", "B0AA92", "56524A", 0.05f, "V2"),
            new Look("common-carp", "Fish1", 30, 64, 1.40f, 1.2f, "6C6241", "B8A570", "6A5640", 0.10f, "V1"),
            new Look("gar", "Fish1", 60, 120, 0.43f, 0.6f, "5E5A3E", "B3A986", "5A5438", 0.40f, "V1"),
        };
        static readonly (string name, Look[] looks)[] Groups = { ("deep", Deep), ("bass", Bass), ("big", Big) };
        // f-director (ruling on G3): only V1 shows a body, and only within 0.6 m; V2 is a dark silhouette or a dull flash, never
        // a clear body; V3 never draws a body (signs only). V3 variants appear only on the dry reference sheets.
        static Look[] Tier(string t) => Deep.Concat(Bass).Concat(Big).Where(l => l.tier == t).ToArray();
        const float V1MaxDepth = 0.6f;

        static readonly List<Object> temp = new List<Object>();
        static readonly List<string> placedIds = new List<string>(); // ids of the last PlaceRow, aligned with its centres
        static Shader fishShader;

        public static void RunToFile() => RunToFile(false);

        // Headless: unity run <project> -- -executeMethod WashedAshore.Fish.Editor.FishLookDev.RunBatch
        public static void RunBatch() => RunToFile(true);

        static void RunToFile(bool exit)
        {
            Directory.CreateDirectory(OutDir);
            var path = Path.Combine(OutDir, "lookdev-log.txt");
            int code = 0;
            try { File.WriteAllText(path, Run() + "\ndone\n"); }
            catch (System.Exception e) { File.WriteAllText(path, "FAILED: " + e); code = 1; }
            if (exit) EditorApplication.Exit(code);
        }

        public static string Run()
        {
            var log = new StringBuilder($"FishLookDev {System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new System.InvalidOperationException($"open scene '{SceneManager.GetSceneAt(i).path}' has unsaved changes; not touching it");
            fishShader = Shader.Find(FishShader);
            var ringShader = Shader.Find(RingShader);
            if (!fishShader || !ringShader) throw new FileNotFoundException("fish shaders missing");
            log.AppendLine(CheckShaders(fishShader, ringShader));
            Directory.CreateDirectory(OutDir);

            var cfg = BellsBendData.LoadConfig();
            float W = cfg.WaterLevelY;
            var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
            var lm = BellsBendData.LoadVectors(cfg).landmarks.ToDictionary(l => l.id, l => new Vector3(l.xz.x, 0f, l.xz.y));
            var ferry = lm["CleecesFerryLanding"];
            var bluff = lm["BuzzardBluff"];

            var scene = BuildScene(new[] { ferry, bluff }, log);
            try
            {
                var waterMat = PushWaterGlobals(scene, W, log);
                var models = new Dictionary<string, (GameObject go, AnimationClip clip, Bounds b0)>();
                foreach (var m in new[] { "Fish1", "Fish2" }) models[m] = LoadModel(m, log);

                var edge = NearestShore(maps, ferry, -1f, 0f);
                var outw = Outward(maps, edge);
                var along = new Vector3(outw.z, 0f, -outw.x);
                var shoreEye = WithY(edge - outw * 1f, TerrainQuery.Height(edge - outw * 1f) + ShoreEye);
                var bluffEye = WithY(edge - outw * BluffInland, W + BluffEyeAboveW);
                log.AppendLine($"shelf site: waterline ({edge.x:F1},{edge.z:F1}) out ({outw.x:F2},{outw.z:F2}); shore eye y {shoreEye.y:F2} " +
                               $"({shoreEye.y - W:F2} above W); bluff-height eye ({bluffEye.x:F1},{bluffEye.y:F2},{bluffEye.z:F1}), W={W:F2}");

                // 1. V1 and V2 rows at each depth, from the shore eye (60 and 20 deg). V1 deeper than 0.6 m is not drawn (tier
                // rule); that row is also shot once with the rule off, for the record of what the murk alone does.
                foreach (var tier in new[] { "V1", "V2" })
                    foreach (var d in Depths)
                    {
                        var placed = PlaceRow(models, Tier(tier), edge, outw, along, d, W, 0.25f, log, $"{tier} d{d:F1}");
                        var centre = placed.Aggregate(Vector3.zero, (s, p) => s + p) / placed.Count;
                        if (tier == "V1" && d > V1MaxDepth)
                        {
                            log.AppendLine(Shot($"g4-{tier}-d{d:F1}-shore-fov40-record-tierRuleOff.png", shoreEye, centre, 40f));
                            SetFishActive(false);
                            log.AppendLine($"  tier rule: V1 top at {d:F1} m > {V1MaxDepth} m, not drawn");
                        }
                        log.AppendLine(Shot($"g4-{tier}-d{d:F1}-shore-fov60.png", shoreEye, centre, 60f));
                        for (int k = 0; k < placed.Count; k++)
                            log.AppendLine(Shot($"g4-{tier}-d{d:F1}-shore-fov20-{placedIds[k]}.png", shoreEye, placed[k], 20f));
                        // Bluff-top height: every fish is > 25 m from that eye, so rule V2 (approved) hides them all.
                        if (d == Depths[0])
                        {
                            log.AppendLine(Shot($"g4-{tier}-d{d:F1}-bluffheight-fov20-record-V2off.png", bluffEye, centre, 20f));
                            log.AppendLine(Shot($"g4-{tier}-d{d:F1}-bluffheight-fov60-record-V2off.png", bluffEye, centre, 60f));
                            log.AppendLine($"  rule V2 from bluff height: {ApplyRSub(bluffEye)} hidden");
                            log.AppendLine(Shot($"g4-{tier}-d{d:F1}-bluffheight-fov20.png", bluffEye, centre, 20f));
                        }
                        ClearTemp();
                    }

                // 1b. V2 flash: the V2 row at 0.3 m with a full flank flash (lerp to the dull belly), shore eye.
                var flashRow = PlaceRow(models, Tier("V2"), edge, outw, along, 0.3f, W, 0.25f, log, "V2 flash d0.3", flash: 1f);
                log.AppendLine(Shot("g4-V2-d0.3-flash-shore-fov20.png", shoreEye, flashRow.Aggregate(Vector3.zero, (s, p) => s + p) / flashRow.Count, 20f));
                ClearTemp();

                // 2. Surface signs: a dimple 3 m out and a rise 5 m out, mid-age; gar basking (back 2 cm down).
                Ring(edge + outw * 3f + along * -1f, W, 0.12f, 1, 0.6f, ringShader);
                Ring(edge + outw * 5f + along * 1.5f, W, 0.6f, 3, 0.8f, ringShader);
                var signCentre = edge + outw * 4f;
                log.AppendLine(Shot("g4-rise-dimple-shore-fov60.png", shoreEye, WithY(signCentre, W), 60f));
                log.AppendLine(Shot("g4-rise-dimple-shore-fov20.png", shoreEye, WithY(signCentre, W), 20f));
                log.AppendLine(Shot("g4-rise-dimple-bluffheight-fov60.png", bluffEye, WithY(signCentre, W), 60f));
                ClearTemp();
                var gar = Big.First(l => l.id == "gar");
                var bask = PlaceRow(models, new[] { gar }, edge + along * 2f, outw, along, 0.02f, W, 0.25f, log, "gar bask");
                log.AppendLine(Shot("g4-gar-bask-shore-fov60.png", shoreEye, bask[0], 60f));
                log.AppendLine(Shot("g4-gar-bask-shore-fov20.png", shoreEye, bask[0], 20f));
                log.AppendLine(Shot("g4-gar-bask-bluffheight-fov20.png", bluffEye, bask[0], 20f));
                ClearTemp();

                // 3. Dry reference: every variant 0.5 m above the water (opaque, T = 1), side-on from the water at 5 m.
                foreach (var (gname, looks) in Groups)
                {
                    var row = PlaceRow(models, looks, edge, outw, along, -0.5f, W, 0.25f, log, $"{gname} dry", dry: true);
                    foreach (var m in temp.OfType<Material>()) m.SetFloat("_Silhouette", 0f); // reference sheet: the census tints
                    var c = row.Aggregate(Vector3.zero, (s, p) => s + p) / row.Count;
                    float span = row.Count > 1 ? Vector3.Distance(row[0], row[row.Count - 1]) + 1f : 1.5f;
                    float back = 0.6f * span / Mathf.Tan(22.5f * Mathf.Deg2Rad);
                    log.AppendLine(Shot($"g4-dry-{gname}.png", c + outw * back + Vector3.up * 0.3f, c, 45f));
                    log.AppendLine("  dry order left to right from the camera: see the row line above (" + string.Join(", ", placedIds) + ")");
                    ClearTemp();
                }

                // 4. Gar Swim strip: four phases, dry, from above, to judge whether Swim still reads at Y 0.43 / X 0.6.
                var strip = new List<Vector3>();
                for (int k = 0; k < 4; k++)
                    strip.AddRange(PlaceRow(models, new[] { gar }, edge + outw * (k * 0.3f - 0.45f), outw, along, -0.5f, W, k * 0.25f, log, $"gar phase {k * 0.25f:F2}", dry: true));
                var sc = strip.Aggregate(Vector3.zero, (s, p) => s + p) / strip.Count;
                log.AppendLine(Shot("g4-gar-swim-strip-top.png", sc + Vector3.up * 1.6f + outw * 0.01f, sc, 60f));
                ClearTemp();

                // 4b. f-td 5.4(f): the hand-written shader through Graphics.RenderMeshInstanced (INSTANCING_ON) under URP with the
                // SRP Batcher state as set. Four baked Fish1 poses 0.5 m above the water, side-on; pixels compared with an empty frame.
                log.AppendLine(InstancingCheck(models["Fish1"], Bass[0], edge, outw, along, W));

                // 5. The real Buzzard Bluff top: three fish 0.3 m down, 25-30 m out from its foot. Without and with rule V2.
                var bEdge = NearestShore(maps, bluff, -1f, 0f);
                var bOut = Outward(maps, bEdge);
                var realEye = WithY(bluff, TerrainQuery.Height(bluff) + ShoreEye);
                var bFish = PlaceRow(models, Tier("V1").Where(l => l.id != "gar").ToArray(), bEdge + bOut * 25f, bOut, new Vector3(bOut.z, 0f, -bOut.x), 0.3f, W, 0.25f, log, "bluff 0.3", maxOut: 10f);
                var bc = bFish.Aggregate(Vector3.zero, (s, p) => s + p) / bFish.Count;
                log.AppendLine($"bluff-top eye ({realEye.x:F1},{realEye.y:F2},{realEye.z:F1}) is {realEye.y - W:F2} above W; nearest fish {bFish.Min(p => Vector3.Distance(p, realEye)):F1} m from the eye");
                log.AppendLine(Shot("g4-bluff-real-fov20-record-V2off.png", realEye, bc, 20f));
                log.AppendLine($"rule V2 (R_sub {RSub} m): {ApplyRSub(realEye)} sub-surface fish hidden");
                foreach (var fov in new[] { 60f, 20f }) log.AppendLine(Shot($"g4-bluff-real-V2-fov{fov:F0}.png", realEye, bc, fov));
                ClearTemp();
                log.AppendLine($"water material: {AssetDatabase.GetAssetPath(waterMat)}");
            }
            finally
            {
                ClearTemp();
                ClearWaterGlobals();
            }
            bool inBuild = EditorBuildSettings.scenes.Any(s => s.path == ScenePath);
            log.AppendLine($"{ScenePath} in build list: {inBuild}" + (inBuild ? " FAIL" : " (correct: not in the build list)"));
            if (inBuild) throw new System.InvalidOperationException(log.ToString());
            return log.ToString();
        }

        // ---------------- scene ----------------

        // New look-dev scene with copies of World's terrain tiles, water quads, directional lights and sky root near the sites,
        // plus its RenderSettings. World is opened additively and closed without saving. Saves the look-dev scene (no fish).
        static Scene BuildScene(Vector3[] sites, StringBuilder log)
        {
            Directory.CreateDirectory(Path.Combine(BellsBendData.ProjectRoot, Path.GetDirectoryName(ScenePath)));
            var look = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = EditorSceneManager.OpenScene(BellsBendLevel.ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(world);
            var rs = (sky: RenderSettings.skybox, fog: RenderSettings.fog, mode: RenderSettings.fogMode, fogColor: RenderSettings.fogColor,
                      start: RenderSettings.fogStartDistance, end: RenderSettings.fogEndDistance, density: RenderSettings.fogDensity,
                      amb: RenderSettings.ambientMode, ambSky: RenderSettings.ambientSkyColor, ambEq: RenderSettings.ambientEquatorColor,
                      ambGround: RenderSettings.ambientGroundColor, ambI: RenderSettings.ambientIntensity, sun: RenderSettings.sun,
                      refl: RenderSettings.defaultReflectionMode, reflI: RenderSettings.reflectionIntensity);
            bool Near(Vector3 p, float r) => sites.Any(s => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.x, s.z)) < r);
            int terrains = 0, water = 0, lights = 0, sky = 0;
            Light sunCopy = null;
            void Copy(GameObject src)
            {
                var c = Object.Instantiate(src);
                c.name = src.name;
                SceneManager.MoveGameObjectToScene(c, look);
                c.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                c.transform.localScale = src.transform.lossyScale;
                if (src.GetComponent<Light>() == rs.sun) sunCopy = c.GetComponent<Light>();
            }
            foreach (var root in world.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Terrain>(true))
                {
                    var size = t.terrainData.size;
                    if (!Near(t.transform.position + new Vector3(size.x, 0f, size.z) * 0.5f, 1400f)) continue;
                    Copy(t.gameObject); terrains++;
                }
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.sharedMaterial && r.sharedMaterial.shader.name == WaterShader && Near(r.transform.position, 1400f)) { Copy(r.gameObject); water++; }
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                    if (l.type == LightType.Directional && l.isActiveAndEnabled) { Copy(l.gameObject); lights++; }
                foreach (var s in root.GetComponentsInChildren<BellsBendSkyInit>(true)) { Copy(s.gameObject); sky++; }
            }
            EditorSceneManager.CloseScene(world, true); // discard: World.unity is never saved here
            SceneManager.SetActiveScene(look);
            RenderSettings.skybox = rs.sky; RenderSettings.fog = rs.fog; RenderSettings.fogMode = rs.mode; RenderSettings.fogColor = rs.fogColor;
            RenderSettings.fogStartDistance = rs.start; RenderSettings.fogEndDistance = rs.end; RenderSettings.fogDensity = rs.density;
            RenderSettings.ambientMode = rs.amb; RenderSettings.ambientSkyColor = rs.ambSky; RenderSettings.ambientEquatorColor = rs.ambEq;
            RenderSettings.ambientGroundColor = rs.ambGround; RenderSettings.ambientIntensity = rs.ambI; RenderSettings.defaultReflectionMode = rs.refl;
            RenderSettings.reflectionIntensity = rs.reflI; RenderSettings.sun = sunCopy;
            DynamicGI.UpdateEnvironment();
            EditorSceneManager.SaveScene(look, ScenePath);
            log.AppendLine($"scene {ScenePath}: copied {terrains} terrain tiles, {water} water quads, {lights} directional lights, {sky} sky roots from " +
                           $"{BellsBendLevel.ScenePath} (closed unsaved); fog {rs.mode} {rs.start}-{rs.end}, skybox {(rs.sky ? rs.sky.name : "none")}, sun {(sunCopy ? sunCopy.name : "none")}");
            return look;
        }

        // Water motion globals as BellsBendWaterLook does (WaterBody only pushes in Play), plus the fish shader's copies of the
        // water material's look values (a material can't read another material's properties; no literal copies).
        static Material PushWaterGlobals(Scene scene, float W, StringBuilder log)
        {
            var motion = AssetDatabase.LoadAssetAtPath<WaterMotionSettings>(MotionPath);
            if (!motion) throw new FileNotFoundException("WaterMotion settings missing", MotionPath);
            const double t = 0.6;
            for (int i = 0; i < WaterMotion.MaxWaves; i++)
            {
                bool on = motion.waves != null && i < motion.waves.Length && motion.waves[i].wavelength > 0f;
                var w = on ? motion.waves[i] : default;
                var d = on ? w.Direction : Vector2.zero;
                Shader.SetGlobalVector("_WaterWave" + i, on ? new Vector4(w.amplitude, w.K, d.x, d.y) : Vector4.zero);
                Shader.SetGlobalVector("_WaterWaveSpeedPhase" + i, on ? new Vector4(w.speed, WaterMotion.Phase(w, t), 0f, 0f) : Vector4.zero);
            }
            Shader.SetGlobalFloat("_WaterFlowPhase", WaterMotion.FlowPhase(motion.flowCyclePeriod, t));
            Shader.SetGlobalFloat("_WaterLevelY", W);
            var mat = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshRenderer>(true))
                .Select(r => r.sharedMaterial).First(m => m && m.shader.name == WaterShader);
            var ext = mat.GetVector("_Extinction");
            Shader.SetGlobalVector("_FishWaterExtinction", ext);
            Shader.SetGlobalColor("_FishWaterScatter", mat.GetColor("_ScatterColor"));
            Shader.SetGlobalFloat("_FishWaterMinCos", mat.GetFloat("_MinCosView"));
            Shader.SetGlobalFloat("_FishWaterF0", mat.GetFloat("_F0"));
            log.AppendLine($"water globals at t={t}: W={W:F3}; copied from {mat.name}: _Extinction {ext}, _ScatterColor {mat.GetColor("_ScatterColor")}, " +
                           $"_MinCosView {mat.GetFloat("_MinCosView")}, _F0 {mat.GetFloat("_F0")}");
            return mat;
        }

        static void ClearWaterGlobals()
        {
            for (int i = 0; i < WaterMotion.MaxWaves; i++) { Shader.SetGlobalVector("_WaterWave" + i, Vector4.zero); Shader.SetGlobalVector("_WaterWaveSpeedPhase" + i, Vector4.zero); }
            Shader.SetGlobalFloat("_WaterFlowPhase", 0f);
        }

        // ---------------- fish ----------------

        static (GameObject, AnimationClip, Bounds) LoadModel(string name, StringBuilder log)
        {
            var path = name == "Fish1" ? FishImportSettings.Fish1Fbx : FishImportSettings.Fish2Fbx;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => c.name == FishImportSettings.ClipName);
            // Rest-of-clip bounds at scale 1, model root space, heading +Z (the facing fix makes +Z the nose).
            var inst = Object.Instantiate(go);
            inst.hideFlags = HideFlags.HideAndDontSave;
            var mesh = new Mesh();
            clip.SampleAnimation(inst, 0f);
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            smr.BakeMesh(mesh, true);
            var b = GeometryUtility.CalculateBounds(mesh.vertices, inst.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix);
            log.AppendLine($"{name}: frame-0 bounds centre {b.center:F3} size {b.size:F3}; submeshes " +
                           string.Join(", ", smr.sharedMaterials.Select((m, i) => $"{i}:{(m ? m.name : "none")}")));
            Object.DestroyImmediate(mesh); Object.DestroyImmediate(inst);
            return (go, clip, b);
        }

        // Places one fish per look in a row along the bank, each with the top of its body at depth d below W (negative = above,
        // dry), at the first point out from the waterline where the bed leaves 5 cm under the belly. Returns body centres.
        static List<Vector3> PlaceRow(Dictionary<string, (GameObject go, AnimationClip clip, Bounds b0)> models, Look[] looks, Vector3 edge,
            Vector3 outw, Vector3 along, float d, float W, float phase, StringBuilder log, string label, bool dry = false, float maxOut = 15f, float flash = 0f)
        {
            var centres = new List<Vector3>();
            placedIds.Clear();
            var line = new StringBuilder($"  row {label}:");
            for (int i = 0; i < looks.Length; i++)
            {
                var look = looks[i];
                var (model, clip, b0) = models[look.model];
                float u = look.TL / b0.size.z;
                var scale = new Vector3(look.x * u, look.y * u, u);
                float h = b0.size.y * scale.y, len = b0.size.z * u;
                float lateral = (i - (looks.Length - 1) * 0.5f) * Mathf.Max(1.6f, len * 1.3f);
                var basePt = edge + along * lateral;
                Vector3 p = basePt;
                float bedNeed = d + h + 0.05f, bed = 0f;
                bool found = dry;
                for (float s = 0f; !dry && s <= maxOut; s += 0.1f)
                {
                    p = basePt + outw * s;
                    bed = W - TerrainQuery.Height(p);
                    if (bed >= bedNeed) { found = true; break; }
                }
                if (!found) { line.Append($" {look.id}: no bed >= {bedNeed:F2} m within {maxOut} m, skipped;"); continue; }
                if (dry) p = basePt + outw * 3f;
                var centre = new Vector3(p.x, W - d - h * 0.5f, p.z);
                var rot = Quaternion.LookRotation(i % 2 == 0 ? along : -along, Vector3.up);
                var go = Object.Instantiate(model);
                go.hideFlags = HideFlags.DontSave;
                temp.Add(go);
                go.transform.localScale = scale;
                go.transform.rotation = rot;
                go.transform.position = centre - rot * Vector3.Scale(b0.center, scale);
                clip.SampleAnimation(go, phase * clip.length);
                Dress(go, look, centre, len, flash);
                centres.Add(centre);
                placedIds.Add(look.id);
                line.Append($" {look.id} TL {look.TL:F2} m len {len:F2} h {h:F2} at {(dry ? "dry" : $"{Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(edge.x, 0f, edge.z)):F1} m out, bed {bed:F2}")};");
            }
            log.AppendLine(line.ToString());
            return centres;
        }

        // Fish shader materials by vendor submesh name: "Fins" -> fin colour, every other submesh -> countershaded body.
        static void Dress(GameObject go, Look look, Vector3 centre, float length, float flash)
        {
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            Material Make(bool fin)
            {
                var m = new Material(fishShader) { hideFlags = HideFlags.DontSave, name = $"{look.id}-{(fin ? "fins" : "body")}" };
                m.SetColor("_BackColor", Hex(look.back)); m.SetColor("_BellyColor", Hex(look.belly)); m.SetColor("_FinColor", Hex(look.fins));
                m.SetFloat("_IsFin", fin ? 1f : 0f); m.SetFloat("_Mottle", look.mottle);
                m.SetFloat("_Silhouette", look.tier == "V2" ? 1f : 0f); m.SetFloat("_Flash", flash);
                temp.Add(m);
                return m;
            }
            Material body = Make(false), fins = Make(true);
            smr.sharedMaterials = smr.sharedMaterials.Select(m => m && m.name.StartsWith("Fins") ? fins : body).ToArray();
            var mpb = new MaterialPropertyBlock();
            mpb.SetVector("_FishCentre", new Vector4(centre.x, centre.y, centre.z, length));
            smr.SetPropertyBlock(mpb);
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.updateWhenOffscreen = true;
        }

        static void Ring(Vector3 at, float W, float radius, int count, float age, Shader shader)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.hideFlags = HideFlags.DontSave;
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.transform.SetPositionAndRotation(new Vector3(at.x, W + 0.004f, at.z), Quaternion.Euler(90f, 0f, 0f));
            q.transform.localScale = Vector3.one * radius * 2.2f;
            var m = new Material(shader) { hideFlags = HideFlags.DontSave };
            m.SetFloat("_Radius", 0.9f); m.SetFloat("_RingCount", count); m.SetFloat("_Strength", 1f - 0.5f * age);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
            temp.Add(q); temp.Add(m);
        }

        static void SetFishActive(bool on) { foreach (var g in temp.OfType<GameObject>()) if (g) g.SetActive(on); }

        // Rule V2: a fish below the surface is drawn only within R_sub of the camera.
        static int ApplyRSub(Vector3 eye)
        {
            int n = 0;
            foreach (var g in temp.OfType<GameObject>())
                if (g && g.activeSelf && g.GetComponentInChildren<SkinnedMeshRenderer>(true) && Vector3.Distance(g.transform.position, eye) > RSub) { g.SetActive(false); n++; }
            return n;
        }

        static void ClearTemp()
        {
            foreach (var o in temp) if (o) Object.DestroyImmediate(o);
            temp.Clear();
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
    }
}
