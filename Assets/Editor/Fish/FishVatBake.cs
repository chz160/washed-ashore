using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WashedAshore.Fish.Rendering;

namespace WashedAshore.Fish.Editor
{
    /// <summary>
    /// Phase 1 bake (spec F1, F2, F9; f-td gate/fish-technique 5.4): from the imported, facing-corrected Fish1/Fish2 Swim clips
    /// to everything the runtime draws and the sim measures. Per model: a single-submesh VAT mesh (rest pose in model-root
    /// space, vertex colour r = fin region, uv1.x = VAT column), a VAT texture (row 0 = rest/bind pose, rows 1..N = the Swim
    /// loop as FishSwimLoop plays it, the version FishImportCheck grades as passing) and an instanced FishUnderwater material.
    /// Then FishBodies.asset (f-engineer's type: one body per brief variant, in fish-targets.json order) and
    /// Art/FishRenderSet.asset. No yaw anywhere: the clip is baked after the import facing fix.
    /// Format: RGBAHalf (xyz position, w = two 5-bit octahedral normal components), or with <see cref="Rgba32"/> the WebGL2
    /// fallback (positions quantised to the clip box + an RGBA32 normal texture); f-td 5.4(e).
    /// Run: unity command eval "return WashedAshore.Fish.Editor.FishVatBake.Bake();"
    /// </summary>
    public static class FishVatBake
    {
        /// <summary>The one import setting for the VAT format (f-td 5.4 e): false = RGBAHalf, true = RGBA32 fallback.</summary>
        public static readonly bool Rgba32 = false;
        public const int MaxBytesPerModel = 256 * 1024; // f-td: <= 256 KB per model
        public const string ArtDir = "Assets/World/Fish/Art";
        public const string BodiesPath = "Assets/World/Fish/FishBodies.asset";
        public const string RenderSetPath = ArtDir + "/FishRenderSet.asset";
        public const string WaterMaterialPath = "Assets/World/BellsBend/Water/BellsBendWater.mat";
        const string FishShader = "WashedAshore/FishUnderwater", RingShader = "WashedAshore/FishRing";

        public class ModelBake
        {
            public string name, path;
            public Mesh mesh; public Material material;
            public Bounds clipBounds; public float noseToTail, rootTravel, tailHalfSwing;
            public int vertices, frames, loopVersion, bytes; public float rawSeamMm, faceZ, tailZ; public string executed, tipWrap, vatHash;
        }

        [MenuItem("Washed Ashore/Art/Bake Fish VAT")]
        static void Menu() => Debug.Log(Bake());

        // Swatch-only refresh (no VAT, mesh, material or FishBodies writes): re-reads FishLooks into FishRenderSet.variants.
        public static string UpdateLooks()
        {
            var set = AssetDatabase.LoadAssetAtPath<FishRenderSet>(RenderSetPath);
            var variants = FishLooks.Variants();
            var sb = new StringBuilder("FishRenderSet swatch refresh (VAT untouched):\n");
            for (int i = 0; i < set.variants.Length; i++)
            {
                if (set.variants[i].id != variants[i].variant) throw new InvalidDataException($"FishRenderSet[{i}] is {set.variants[i].id}, brief says {variants[i].variant}: full rebake needed");
                FishLooks.For(variants[i].variant, out var back, out var belly, out var fins);
                var v = set.variants[i];
                if (v.back != back || v.belly != belly || v.fins != fins)
                    sb.AppendLine($"  {v.id}: back #{ColorUtility.ToHtmlStringRGB(v.back)}->#{ColorUtility.ToHtmlStringRGB(back)} belly #{ColorUtility.ToHtmlStringRGB(v.belly)}->#{ColorUtility.ToHtmlStringRGB(belly)} fins #{ColorUtility.ToHtmlStringRGB(v.fins)}->#{ColorUtility.ToHtmlStringRGB(fins)}");
                v.back = back; v.belly = belly; v.fins = fins;
                set.variants[i] = v;
            }
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        public const string FootprintDraftPath = "_bmad-output/poc/fish-targets-F17-draft.json";

        /// <summary>
        /// Imports surfaceSigns.footprints[] (ruling/fish-sign-footprint-physical) into FishRenderSet.footprints: from the brief
        /// of record if it has them, else the F17 draft (logged). JSON kind names map to FishSurfaceKind (VWake -> Wake and
        /// FleeWake, GarGulp -> GarGulpOrBask, RollOrTail -> Roll and RollOrTail; GarBask has no ring and is skipped). Scalars
        /// or [min, max] pairs are both accepted.
        /// </summary>
        public static string UpdateFootprints()
        {
            var list = ReadFootprints(out string text, out string source);
            var set = AssetDatabase.LoadAssetAtPath<FishRenderSet>(RenderSetPath);
            set.footprints = list.ToArray();
            set.footprintsSource = source;
            bool record = RecordHasFootprints(out string rev);
            set.footprintsSha = FishFootprintHash.Of(record ? FishLooks.TargetsPath : FootprintDraftPath);
            set.footprintsRev = record ? rev : "draft";
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return text;
        }

        // Headless: unity run <project> -- -executeMethod WashedAshore.Fish.Editor.FishVatBake.RunFootprintsBatch
        public static void RunFootprintsBatch()
        {
            int code = 0;
            try { File.WriteAllText("TestResults/fish-footprints.txt", UpdateFootprints() + "done" + System.Environment.NewLine); }
            catch (System.Exception e) { File.WriteAllText("TestResults/fish-footprints.txt", "FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        /// <summary>True if the brief of record (fish-targets.json) carries surfaceSigns.footprints; revision is its briefRevision.</summary>
        public static bool RecordHasFootprints(out string revision)
        {
            var j = (Dictionary<string, object>)BellsBendData.Json.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), FishLooks.TargetsPath)));
            revision = j.TryGetValue("briefRevision", out var r) ? r as string : "?";
            return j.TryGetValue("surfaceSigns", out var ss) && ss is Dictionary<string, object> d && d.ContainsKey("footprints");
        }

        /// <summary>
        /// The footprints a fresh import of the current targets would produce (pure; nothing saved). source names the file
        /// read, its briefRevision and sha256 (the record if it has footprints, else the draft).
        /// </summary>
        public static List<FishRenderSet.Footprint> ReadFootprints(out string report, out string source)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string path = RecordHasFootprints(out _) ? FishLooks.TargetsPath : FootprintDraftPath;
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, path));
            var j = (Dictionary<string, object>)BellsBendData.Json.Parse(File.ReadAllText(Path.Combine(root, path)));
            string sha;
            using (var h = System.Security.Cryptography.SHA256.Create())
                sha = System.BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            source = $"{path} ({j["briefRevision"]}) sha256 {sha}";
            var rows = (List<object>)((Dictionary<string, object>)j["surfaceSigns"])["footprints"];
            Vector2 V2(Dictionary<string, object> r, string key)
            {
                if (!r.TryGetValue(key, out var o) || o == null) return Vector2.zero;
                if (o is List<object> l) return new Vector2(BellsBendData.F(l[0]), BellsBendData.F(l[l.Count - 1]));
                float v = BellsBendData.F(o); return new Vector2(v, v);
            }
            float F1(Dictionary<string, object> r, string key) => r.TryGetValue(key, out var o) && o != null && !(o is List<object>) ? BellsBendData.F(o) : V2(r, key).x;
            var map = new Dictionary<string, FishSurfaceKind[]>
            {
                ["Dimple"] = new[] { FishSurfaceKind.Dimple }, ["Rise"] = new[] { FishSurfaceKind.Rise }, ["Swirl"] = new[] { FishSurfaceKind.Swirl },
                ["RollOrTail"] = new[] { FishSurfaceKind.Roll, FishSurfaceKind.RollOrTail }, ["GarGulp"] = new[] { FishSurfaceKind.GarGulpOrBask },
                ["VWake"] = new[] { FishSurfaceKind.Wake, FishSurfaceKind.FleeWake }, ["NervousWaterOrFlip"] = new[] { FishSurfaceKind.NervousWaterOrFlip },
                ["Jump"] = new[] { FishSurfaceKind.Jump }, ["Busting"] = new[] { FishSurfaceKind.Busting },
            };
            var list = new List<FishRenderSet.Footprint>();
            var sb = new StringBuilder($"footprints from {source}:\n");
            foreach (Dictionary<string, object> r in rows)
            {
                string name = (string)r["kind"];
                if (!map.TryGetValue(name, out var kinds)) { sb.AppendLine($"  {name}: no ring sign, skipped"); continue; }
                float arm = 0f, armSec = 0f;
                if (r.TryGetValue("armLengthRule", out var rule) && rule is string ruleText)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(ruleText, @"~\s*([0-9.]+)\s*m");
                    if (m.Success) arm = float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var ms = System.Text.RegularExpressions.Regex.Match(ruleText, @"x\s*([0-9.]+)\s*s");
                    if (ms.Success) armSec = float.Parse(ms.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
                foreach (var k in kinds)
                {
                    var f = new FishRenderSet.Footprint
                    {
                        kind = k, coreRadius = F1(r, "coreRadius"), ringSpeed = F1(r, "ringSpeed"), maxRingRadius = F1(r, "maxRingRadius"),
                        armLength = arm, armSeconds = armSec, lifeSec = V2(r, "lifeSec"), patchDiameter = V2(r, "patchDiameter"),
                    };
                    list.Add(f);
                    sb.AppendLine($"  {k}: core {f.coreRadius} m, speed {f.ringSpeed} m/s, max ring {f.maxRingRadius} m, arm {f.armLength} m / {f.armSeconds} s x burst, life {f.lifeSec.x}-{f.lifeSec.y} s, patch {f.patchDiameter.x}-{f.patchDiameter.y} m");
                }
            }
            report = sb.ToString();
            return list;
        }

        // Slot #8: everything except the bake. VatPos hashes are logged before and after to show they didn't move.
        public static void RunBatchNoVat()
        {
            var path = "TestResults/fish-art-batch-novat.txt";
            int code = 0;
            try
            {
                var sb = new StringBuilder();
                string before = string.Join(", ", new[] { "Fish1", "Fish2" }.Select(n => $"{n} {VatHash(n)}"));
                sb.AppendLine("VatPos before: " + before);
                sb.AppendLine(FishImportCheck.Run());
                sb.AppendLine(UpdateLooks());
                sb.AppendLine(FishPrefabBuilder.Build());
                WashedAshore.Wildlife.Editor.ModelFacingPostprocessor.FacingReport(WashedAshore.Wildlife.Editor.ModelFacingPostprocessor.ReportPath);
                string after = string.Join(", ", new[] { "Fish1", "Fish2" }.Select(n => $"{n} {VatHash(n)}"));
                sb.AppendLine("VatPos after:  " + after + (after == before ? "  (unchanged)" : "  CHANGED"));
                File.WriteAllText(path, sb + "\ndone\n");
            }
            catch (System.Exception e) { File.WriteAllText(path, "FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        // Headless, the whole art pass in order: import check (seam grading), bake, prefab variants, facing report.
        // unity run <project> -- -executeMethod WashedAshore.Fish.Editor.FishVatBake.RunBatch  -> TestResults/fish-art-batch.txt
        public static void RunBatch()
        {
            var path = "TestResults/fish-art-batch.txt";
            int code = 0;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("VatPos before this bake: " + string.Join(", ", new[] { "Fish1", "Fish2" }.Select(n => $"{n} {VatHash(n)}")));
                sb.AppendLine(FishImportCheck.Run());
                sb.AppendLine(Bake());
                sb.AppendLine("VatPos after this bake: " + string.Join(", ", new[] { "Fish1", "Fish2" }.Select(n => $"{n} {VatHash(n)}")));
                sb.AppendLine(FishPrefabBuilder.Build());
                WashedAshore.Wildlife.Editor.ModelFacingPostprocessor.FacingReport(WashedAshore.Wildlife.Editor.ModelFacingPostprocessor.ReportPath);
                sb.AppendLine("facing report rewritten: " + WashedAshore.Wildlife.Editor.ModelFacingPostprocessor.ReportPath);
                File.WriteAllText(path, sb + "\ndone\n");
            }
            catch (System.Exception e) { File.WriteAllText(path, "FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static string Bake()
        {
            var log = new StringBuilder($"FishVatBake {System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ} format {(Rgba32 ? "RGBA32 fallback" : "RGBAHalf")}\n");
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(Application.dataPath), ArtDir));
            var models = new Dictionary<string, ModelBake>();
            foreach (var (name, path) in new[] { ("Fish1", FishImportSettings.Fish1Fbx), ("Fish2", FishImportSettings.Fish2Fbx) })
            {
                var m = BakeModel(name, path);
                models[name] = m;
                log.AppendLine($"{name}: {m.vertices} vertices x {m.frames} frames (+ rest row), baked loop {(m.loopVersion == 2 ? "v2" : "v1")} (seam grading: {(m.loopVersion == 0 ? "neither version passes" : $"v{m.loopVersion} passes")}), VAT {m.bytes / 1024f:F0} KB " +
                               $"(cap {MaxBytesPerModel / 1024} KB); raw last-key vs frame 0 max {m.rawSeamMm:F2} model-mm; clip box {m.clipBounds.size:F3}; " +
                               $"nose-to-tail {m.noseToTail:F3}; root travel/loop {m.rootTravel:F4}; tail half-swing {m.tailHalfSwing:F3}");
                log.AppendLine($"  loop EXECUTED by the bake: {m.executed}; {m.tipWrap}; VatPos sha256_16 {m.vatHash}");
                log.AppendLine($"  facing (f-td 2a): bind-pose Face z {m.faceZ:F3} > Tail_end z {m.tailZ:F3} in prefab-root space: PASS");
                log.AppendLine($"  loop correction (addendum-2): each bone's key-0 minus last-key difference is spread linearly over all {m.frames} frames " +
                               $"(weight f/{m.frames}, so frame f gets f/{m.frames} of it, max per-frame change 1/{m.frames}); last key dropped; " +
                               (m.loopVersion != 2 ? (m.loopVersion == 1 ? "v1 passes the continuity check, so frames are otherwise untouched" : "NEITHER version passes the continuity check; baked v1 (frames otherwise untouched), pending f-qa") : $"v2: the {2 * FishSwimLoop.SeamWindow - 1} frames round the wrap ({m.frames - FishSwimLoop.SeamWindow + 1}..{m.frames - 1}, 0..{FishSwimLoop.SeamWindow - 1}) also rebuilt by a Hermite matching position and velocity at frames {m.frames - FishSwimLoop.SeamWindow} and {FishSwimLoop.SeamWindow}"));
            }

            var variants = FishLooks.Variants();
            var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(BodiesPath);
            if (!bodies) { bodies = ScriptableObject.CreateInstance<FishBodies>(); AssetDatabase.CreateAsset(bodies, BodiesPath); }
            var set = AssetDatabase.LoadAssetAtPath<FishRenderSet>(RenderSetPath);
            if (!set) { set = ScriptableObject.CreateInstance<FishRenderSet>(); AssetDatabase.CreateAsset(set, RenderSetPath); }
            var modelNames = new[] { "Fish1", "Fish2" };
            set.models = modelNames.Select(n => new FishRenderSet.Model { name = n, mesh = models[n].mesh, material = models[n].material }).ToArray();
            var bodyList = new List<FishBody>();
            var looks = new List<FishRenderSet.Variant>();
            foreach (var (variant, species, length) in variants)
            {
                var look = FishLooks.For(variant, out var back, out var belly, out var fins);
                var m = models[look.model];
                var axis = new Vector3(look.x, look.y, 1f);
                var b = m.clipBounds;
                bodyList.Add(new FishBody
                {
                    variant = variant,
                    clipBounds = new Bounds(Vector3.Scale(b.center, axis), Vector3.Scale(b.size, axis)),
                    noseToTail = m.noseToTail,
                    rootTravelPerLoop = m.rootTravel,
                    tailBeatAmplitude = m.tailHalfSwing * look.x / m.noseToTail,
                });
                looks.Add(new FishRenderSet.Variant { id = variant, model = System.Array.IndexOf(modelNames, look.model), axisScale = axis, back = back, belly = belly, fins = fins, mottle = look.mottle });
                log.AppendLine($"  variant {bodyList.Count - 1} {variant} ({species}): {look.model} axis ({look.x:F2}, {look.y:F2}, 1); adult {length.x:F2}-{length.y:F2} m -> size scale " +
                               $"{length.x / m.noseToTail:F4}-{length.y / m.noseToTail:F4}; back #{ColorUtility.ToHtmlStringRGB(back)} belly #{ColorUtility.ToHtmlStringRGB(belly)} fins #{ColorUtility.ToHtmlStringRGB(fins)}");
            }
            bodies.bodies = bodyList.ToArray();
            set.variants = looks.ToArray();
            set.water = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (!set.water) throw new FileNotFoundException("water material missing", WaterMaterialPath);
            set.ring = RingMaterial();
            set.ringMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            EditorUtility.SetDirty(bodies);
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            log.AppendLine($"{BodiesPath}: {bodies.bodies.Length} bodies; {RenderSetPath}: {set.models.Length} models, {set.variants.Length} variants, water {set.water.name}, ring {set.ring.name}");
            if (models.Values.Any(m => m.loopVersion == 0)) log.AppendLine("WARNING: a model's loop passes neither seam version (see fish-import-check.txt); baked with v1, pending f-qa's ruling");
            File.WriteAllText("TestResults/fish-vat-bake.txt", log.ToString());
            return log.ToString();
        }

        static ModelBake BakeModel(string name, string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => c.name == FishImportSettings.ClipName);
            var r = new ModelBake { name = name, path = path, loopVersion = FishImportCheck.LoopVersion(path) };
            var go = Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            var baked = new Mesh();
            try
            {
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var src = smr.sharedMesh;
                int V = src.vertexCount;
                var bones = go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform).ToArray();
                Matrix4x4 ToRoot() => go.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;

                // Rest (bind) pose: the instance as imported, before any sampling.
                smr.BakeMesh(baked, true);
                var toRoot = ToRoot();
                var restP = baked.vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToArray();
                var restN = baked.normals.Select(n => toRoot.MultiplyVector(n).normalized).ToArray();
                // f-td review 2a: positions are in the prefab root's space after the import facing fix, so the nose must be +Z.
                var faceBone = bones.First(b => b.name == "Face");
                var tailBone = bones.First(b => b.name == "Tail_end");
                r.faceZ = go.transform.InverseTransformPoint(faceBone.position).z;
                r.tailZ = go.transform.InverseTransformPoint(tailBone.position).z;
                if (!(r.faceZ > r.tailZ)) throw new System.InvalidOperationException($"{name}: bind pose Face z {r.faceZ:F3} is not ahead of Tail_end z {r.tailZ:F3}: facing is wrong, fix the import, never rotate in code");

                var (lr, lp) = FishSwimLoop.Sample(go, bones, clip, out int n);
                var rawPos = FishSwimLoop.RootPositions(go, bones, lr, lp);
                r.rawSeamMm = Enumerable.Range(0, bones.Length).Max(i => Vector3.Distance(rawPos[0][i], rawPos[n][i])) * 1000f;
                var (cr, cp) = FishSwimLoop.Linear(lr, lp, n);
                r.executed = "v1 (FishSwimLoop.Linear)";
                if (r.loopVersion == 2) { FishSwimLoop.SeamSmooth(cr, cp, n); r.executed = "v2 (FishSwimLoop.Linear + SeamSmooth)"; } // neither passing: v1
                r.frames = n;
                r.vertices = V;

                var pos = new Vector3[n + 1][];
                var nrm = new Vector3[n + 1][];
                pos[0] = restP; nrm[0] = restN;
                var root = smr.rootBone;
                var tail = bones.FirstOrDefault(b => b.name == "Tail_end");
                float tailMin = float.MaxValue, tailMax = float.MinValue;
                Vector3 root0 = Vector3.zero;
                var clip0 = new Bounds();
                for (int f = 0; f < n; f++)
                {
                    for (int i = 0; i < bones.Length; i++) { bones[i].localRotation = cr[f][i]; bones[i].localPosition = cp[f][i]; }
                    smr.BakeMesh(baked, true);
                    toRoot = ToRoot();
                    pos[f + 1] = baked.vertices.Select(v => toRoot.MultiplyPoint3x4(v)).ToArray();
                    nrm[f + 1] = baked.normals.Select(v => toRoot.MultiplyVector(v).normalized).ToArray();
                    var fb = GeometryUtility.CalculateBounds(pos[f + 1], Matrix4x4.identity);
                    if (f == 0) clip0 = fb; else clip0.Encapsulate(fb);
                    if (tail) { float x = go.transform.InverseTransformPoint(tail.position).x; tailMin = Mathf.Min(tailMin, x); tailMax = Mathf.Max(tailMax, x); }
                    var rp = go.transform.InverseTransformPoint(root.position);
                    if (f == 0) root0 = rp; else r.rootTravel = Mathf.Max(r.rootTravel, Vector3.Distance(rp, root0));
                }
                var restBounds = GeometryUtility.CalculateBounds(restP, Matrix4x4.identity);
                clip0.Encapsulate(restBounds);
                r.clipBounds = clip0;
                r.noseToTail = restBounds.size.z;
                r.tailHalfSwing = tail ? 0.5f * (tailMax - tailMin) : 0f;

                // Per-vertex wrap at the tail tip (the rest pose's rearmost vertex), straight from the rows written to the VAT:
                // row 31 -> row 1 against rows 30 -> 31 and 1 -> 2, model-mm and world mm at the model's max species scale.
                int tip = 0;
                for (int v = 1; v < V; v++) if (restP[v].z < restP[tip].z) tip = v;
                float D(int a, int b) => Vector3.Distance(pos[a][tip], pos[b][tip]) * 1000f;
                float maxScale = FishLooks.MaxLength(name) / restBounds.size.z;
                float wrap = D(n, 1), before = D(n - 1, n), after = D(1, 2);
                r.tipWrap = $"tail-tip vertex {tip}: wrap row {n}->1 {wrap:F2} model-mm vs neighbours {before:F2} / {after:F2}; " +
                            $"world at max species scale {maxScale:F4}: {wrap * maxScale:F2} vs {before * maxScale:F2} / {after * maxScale:F2} mm";
                r.mesh = SaveMesh(name, src, smr, restP, restN, clip0);
                r.material = SaveVat(name, pos, nrm, clip0, restBounds, V, n, out r.bytes);
                var vat = (Texture2D)r.material.GetTexture("_VatPos");
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    r.vatHash = System.BitConverter.ToString(sha.ComputeHash(vat.GetRawTextureData())).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                return r;
            }
            finally { Object.DestroyImmediate(baked); Object.DestroyImmediate(go); }
        }

        // One submesh; vertex colour r = 1 on the vendor "Fins" submesh; uv1.x = the vertex's VAT column.
        static Mesh SaveMesh(string name, Mesh src, SkinnedMeshRenderer smr, Vector3[] restP, Vector3[] restN, Bounds clip)
        {
            string path = $"{ArtDir}/{name}_VAT.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.name = name + "_VAT";
            mesh.vertices = restP;
            mesh.normals = restN;
            var colors = new Color[restP.Length];
            var tris = new List<int>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var mat = s < smr.sharedMaterials.Length ? smr.sharedMaterials[s] : null;
                bool fin = mat && mat.name.StartsWith("Fins");
                var t = src.GetTriangles(s);
                foreach (var v in t) colors[v] = fin ? Color.red : Color.black;
                tris.AddRange(t);
            }
            mesh.colors = colors;
            mesh.SetUVs(1, Enumerable.Range(0, restP.Length).Select(i => new Vector2(i, 0f)).ToList());
            mesh.SetTriangles(tris, 0);
            mesh.bounds = clip; // the whole Swim, so per-instance culling never clips a tail
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        static Material SaveVat(string name, Vector3[][] pos, Vector3[][] nrm, Bounds box, Bounds rest, int V, int n, out int bytes)
        {
            int H = n + 1;
            var posTex = new Texture2D(V, H, Rgba32 ? TextureFormat.RGBA32 : TextureFormat.RGBAHalf, false, true) { name = name + "_VatPos", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            Texture2D nrmTex = null;
            if (Rgba32)
            {
                nrmTex = new Texture2D(V, H, TextureFormat.RGBA32, false, true) { name = name + "_VatNrm", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var pc = new Color32[V * H]; var nc = new Color32[V * H];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < V; x++)
                    {
                        var q = Vector3.Scale(pos[y][x] - box.min, new Vector3(1f / box.size.x, 1f / box.size.y, 1f / box.size.z));
                        pc[y * V + x] = new Color32(B(q.x), B(q.y), B(q.z), 255);
                        var o = Oct(nrm[y][x]);
                        nc[y * V + x] = new Color32(B(o.x), B(o.y), 0, 255);
                    }
                posTex.SetPixels32(pc); nrmTex.SetPixels32(nc);
                nrmTex.Apply(false, false);
                bytes = V * H * 8;
            }
            else
            {
                var data = new ushort[V * H * 4];
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < V; x++)
                    {
                        int k = (y * V + x) * 4;
                        var p = pos[y][x];
                        var o = Oct(nrm[y][x]);
                        float packed = Mathf.Round(Mathf.Clamp01(o.x) * 31f) * 32f + Mathf.Round(Mathf.Clamp01(o.y) * 31f);
                        data[k] = Mathf.FloatToHalf(p.x); data[k + 1] = Mathf.FloatToHalf(p.y); data[k + 2] = Mathf.FloatToHalf(p.z); data[k + 3] = Mathf.FloatToHalf(packed);
                    }
                posTex.SetPixelData(data, 0);
                bytes = V * H * 8;
            }
            posTex.Apply(false, false);
            if (bytes > MaxBytesPerModel) throw new System.InvalidOperationException($"{name}: VAT {bytes} bytes over the {MaxBytesPerModel} cap (f-td)");
            posTex = SaveTexture(posTex, $"{ArtDir}/{name}_VatPos.asset");
            if (nrmTex) nrmTex = SaveTexture(nrmTex, $"{ArtDir}/{name}_VatNrm.asset");

            string matPath = $"{ArtDir}/{name}_VAT.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find(FishShader)); AssetDatabase.CreateAsset(mat, matPath); }
            mat.shader = Shader.Find(FishShader);
            mat.enableInstancing = true;
            mat.SetFloat("_UseVat", 1f);
            mat.SetTexture("_VatPos", posTex);
            mat.SetTexture("_VatNrm", nrmTex);
            mat.SetVector("_VatInfo", new Vector4(V, n, Rgba32 ? 1f : 0f, 0f));
            mat.SetVector("_VatBoxMin", box.min);
            mat.SetVector("_VatBoxSize", box.size);
            // Back/belly split at the body centre's height, a 3% band; positions are model-root space (+Y up, +Z nose).
            mat.SetVector("_BodyFrame", new Vector4(rest.center.y, rest.size.z, 0f, 0.03f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // sha256 (first 16 hex) of a model's VatPos texture data as stored, or "none".
        static string VatHash(string name)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArtDir}/{name}_VatPos.asset");
            if (!tex) return "none";
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(tex.GetRawTextureData())).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }

        static Texture2D SaveTexture(Texture2D tex, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static Material RingMaterial()
        {
            string path = $"{ArtDir}/FishRing.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find(RingShader)); AssetDatabase.CreateAsset(mat, path); }
            mat.enableInstancing = true;
            // The look lives in the shader's property defaults (one diffable source): copy them onto the material.
            var sh = mat.shader;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                var pn = sh.GetPropertyName(i);
                switch (sh.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range: mat.SetFloat(pn, sh.GetPropertyDefaultFloatValue(i)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color: mat.SetColor(pn, sh.GetPropertyDefaultVectorValue(i)); break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector: mat.SetVector(pn, sh.GetPropertyDefaultVectorValue(i)); break;
                }
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static byte B(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        // Octahedral encoding of a unit normal into [0, 1]^2 (decoded by FishUnderwater.shader OctDecode).
        static Vector2 Oct(Vector3 n)
        {
            n /= Mathf.Abs(n.x) + Mathf.Abs(n.y) + Mathf.Abs(n.z);
            var e = new Vector2(n.x, n.y);
            if (n.z < 0f) e = new Vector2((1f - Mathf.Abs(n.y)) * (n.x >= 0f ? 1f : -1f), (1f - Mathf.Abs(n.x)) * (n.y >= 0f ? 1f : -1f));
            return e * 0.5f + new Vector2(0.5f, 0.5f);
        }
    }
}
