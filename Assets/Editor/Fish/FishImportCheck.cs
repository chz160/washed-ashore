using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WashedAshore.Wildlife.Editor;

namespace WashedAshore.Fish.Editor
{
    /// <summary>
    /// F1 evidence for the fish import: the Model Facing Report (each fish must be Flip or Keep), the Swim loop seam
    /// (the wrap step from the last frame back to frame 0 against the clip's median frame step), the licence copy, and
    /// a check that no excluded marine model is anywhere under Assets/. Read-only on assets.
    /// Run: unity command eval "return WashedAshore.Fish.Editor.FishImportCheck.Run();"
    /// Output: TestResults/model-facing-report.json and TestResults/fish-import-check.txt.
    /// </summary>
    public static class FishImportCheck
    {
        public const string OutPath = "TestResults/fish-import-check.txt";
        public const float MaxWrapRatio = 1.5f; // f-qa rev 1.6: wrap <= 1.5x each neighbouring step (no pop)
        public const float StaticDeg = 0.1f, StaticMm = 0.1f; // static floor: both neighbours below it -> wrap <= it
        public const float MinNeighbour = 0.5f; // f-qa rev 1.6: wrap >= 0.5x each neighbouring step (no stall)
        public const float WorldFloorMm = 1f;   // f-qa rev 2.7: a position step within 1 mm (world, max species scale) of each neighbour is continuous
        static readonly string[] Excluded = { "Fish3", "Shark", "Whale", "Dolphin", "Manta ray", "Manta" };

        [MenuItem("Washed Ashore/Art/Fish Import Check")]
        static void Menu() => Debug.Log(Run());

        // Headless: unity run <project> -- -executeMethod WashedAshore.Fish.Editor.FishImportCheck.RunBatch
        public static void RunBatch()
        {
            try { Run(); EditorApplication.Exit(0); }
            catch (System.Exception e)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutPath)));
                File.WriteAllText(OutPath, "FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        public static string Run()
        {
            var sb = new StringBuilder("fish import check (F1)\n");
            bool ok = true;

            ModelFacingPostprocessor.FacingReport(ModelFacingPostprocessor.ReportPath);
            var report = JsonUtility.FromJson<ReportFile>(File.ReadAllText(ModelFacingPostprocessor.ReportPath));
            foreach (var path in FishImportSettings.Models)
            {
                var e = report.models.FirstOrDefault(m => m.path == path);
                bool pass = e != null && (e.decision == "Flip" || e.decision == "Keep");
                ok &= pass;
                sb.AppendLine(e == null ? $"facing {path}: MISSING from report"
                    : $"facing {path}: {e.decision} by {e.decidedBy} (L/R {e.lrForm} {e.lrMeasure:F4} vs eps {e.epsilon:F4}; head '{e.headBone}' x {e.headX:F2} z {e.headZ:F2}; now {e.currentDecision}) {(pass ? "PASS" : "FAIL")}");
            }

            foreach (var path in FishImportSettings.Models)
            {
                var line = Seam(path, out bool pass);
                ok &= pass;
                sb.AppendLine(line);
                sb.AppendLine(Measure(path, out bool faceAtNose));
                ok &= faceAtNose;
            }

            var licence = FishImportSettings.Folder + "/License.txt";
            bool lic = File.Exists(licence) && File.ReadAllText(licence).Contains("CC0");
            ok &= lic;
            sb.AppendLine($"licence {licence}: {(lic ? "present, CC0" : "MISSING")}");

            var marine = AssetDatabase.FindAssets("t:Model", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => Excluded.Contains(Path.GetFileNameWithoutExtension(p))).ToList();
            ok &= marine.Count == 0;
            sb.AppendLine(marine.Count == 0 ? "excluded marine models under Assets/: none" : "EXCLUDED MODELS PRESENT: " + string.Join(", ", marine));

            sb.AppendLine(ok ? "F1 import check: PASS" : "F1 import check: FAIL");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutPath)));
            File.WriteAllText(OutPath, sb.ToString());
            return sb.ToString();
        }

        // Samples every key of the Swim clip and checks the loop seam per bone, three ways (FishSwimLoop): the raw vendor clip as
        // Unity loops it (last key and frame 0 are one instant, so last -> 0 should be ~0); as baked v1 (linear loop correction,
        // last key dropped, wrap (n-1) -> 0 over one frame); as baked v2 (v1 plus a Hermite rebuild of the frames round the seam).
        // Graded by f-qa plan rev 1.6 (Table). Per-frame steps go to TestResults/fish-seam-steps-<model>.csv.
        /// <summary>The loop the VAT bake must use for this model: 1 (linear correction) or 2 (+ seam rebuild); 0 = neither passes.</summary>
        public static int LoopVersion(string path) { Seam(path, out _, out int v); return v; }

        static string Seam(string path, out bool pass) => Seam(path, out pass, out _);

        static string Seam(string path, out bool pass, out int version)
        {
            pass = false;
            version = 0;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == FishImportSettings.ClipName);
            if (!model || !clip) return $"seam {path}: model or '{FishImportSettings.ClipName}' clip MISSING";
            var go = Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var bones = go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform).ToArray();
                // f-qa rev 2.7 floor in model units: 1 mm in the world at the largest species drawn with this model.
                string modelName = Path.GetFileNameWithoutExtension(path);
                float maxScale = FishLooks.MaxLength(modelName) / RestLength(go);
                float floorModelMm = WorldFloorMm / maxScale;
                var (lr, lp) = FishSwimLoop.Sample(go, bones, clip, out int n);
                var rawPos = FishSwimLoop.RootPositions(go, bones, lr, lp);
                var (cr, cp) = FishSwimLoop.Linear(lr, lp, n);
                var cpos = FishSwimLoop.RootPositions(go, bones, cr, cp);
                var (sr, sp) = FishSwimLoop.Linear(lr, lp, n);
                FishSwimLoop.SeamSmooth(sr, sp, n);
                var spos = FishSwimLoop.RootPositions(go, bones, sr, sp);
                WriteSteps(path, bones, lr, rawPos, sr, spos);

                var sb = new StringBuilder($"seam {path}: clip '{clip.name}' {clip.length:F3} s at {clip.frameRate} fps ({n + 1} keys), loopTime {clip.isLooping}; " +
                    $"graded by f-qa plan rev 1.6: per bone, rotation and position, the wrap step within {MinNeighbour}-{MaxWrapRatio}x EACH neighbouring step; " +
                    $"where both neighbours are under {StaticDeg} deg / {StaticMm} mm (model units / 1000) the static rule applies (wrap <= that floor). " +
                    $"Rev 2.7 floor: a position wrap within {WorldFloorMm} mm (world) of each neighbour at the max species scale {maxScale:F4} ({floorModelMm:F1} model-mm) also passes. Median ratio reported only.\n");
                bool rawPass = Table(sb, "raw vendor clip, Unity loop (last key and frame 0 coincide: wrap " + n + "->0 is instantaneous)", bones, lr, rawPos, n + 1, true, floorModelMm);
                bool linPass = Table(sb, $"as baked v1 (linear loop correction, {n} frames, wrap {n - 1}->0 over one frame)", bones, cr, cpos, n, false, floorModelMm);
                bool smPass = Table(sb, $"as baked v2 (linear correction + Hermite rebuild of the {2 * FishSwimLoop.SeamWindow - 1} frames round the wrap)", bones, sr, spos, n, false, floorModelMm);
                pass = clip.isLooping && (linPass || smPass);
                version = linPass ? 1 : smPass ? 2 : 0;
                sb.Append($"  seam: raw {(rawPass ? "PASS" : "FAIL")}; as baked v1 {(linPass ? "PASS" : "FAIL")}; v2 {(smPass ? "PASS" : "FAIL")} " +
                          $"(the bake uses {(linPass ? "v1" : smPass ? "v2" : "neither: FAIL")}; no Animator plays this clip)");
                return sb.ToString();
            }
            finally { Object.DestroyImmediate(go); }
        }

        // Per-frame, per-bone steps (raw and as baked v2) for the seam evidence: TestResults/fish-seam-steps-<model>.csv.
        static void WriteSteps(string path, Transform[] bones, Quaternion[][] lr, Vector3[][] rawPos, Quaternion[][] sr, Vector3[][] spos)
        {
            var csv = new StringBuilder("bone,series,from,to,rot_deg,pos_mm\n");
            void Rows(string series, Quaternion[][] r, Vector3[][] q, bool cyclic)
            {
                int count = r.Length;
                for (int i = 0; i < bones.Length; i++)
                    for (int f = 0; f < (cyclic ? count : count - 1); f++)
                    {
                        int g = (f + 1) % count;
                        csv.Append($"{bones[i].name},{series},{f},{g},{Quaternion.Angle(r[f][i], r[g][i]):F4},{Vector3.Distance(q[f][i], q[g][i]) * 1000f:F4}\n");
                    }
            }
            Rows("raw", lr, rawPos, false);
            Rows("baked_v2", sr, spos, true);
            var file = $"TestResults/fish-seam-steps-{Path.GetFileNameWithoutExtension(path)}.csv";
            File.WriteAllText(file, csv.ToString());
        }

        // One per-bone seam table over frames 0..count-1. instantaneous: the wrap (count-1 -> 0) takes no time, so its ideal is 0.
        // Otherwise graded by f-qa rev 1.6 (local continuity against each neighbouring step; static rule when both are tiny).
        static bool Table(StringBuilder sb, string label, Transform[] bones, Quaternion[][] rot, Vector3[][] pos, int count, bool instantaneous, float floorModelMm)
        {
            int last = count - 1;
            float RotStep(int a, int b, int i) => Quaternion.Angle(rot[a][i], rot[b][i]);
            float PosStep(int a, int b, int i) => Vector3.Distance(pos[a][i], pos[b][i]) * 1000f;
            float Median(IEnumerable<float> s) { var l = s.OrderBy(x => x).ToList(); return l[l.Count / 2]; }
            // Both neighbours under the floor: static rule. Otherwise the wrap must sit within 0.5-1.5x of each neighbour
            // (a neighbour under the floor counts as the floor, so a turning point can't demand a sub-floor wrap).
            bool Local(float wrap, float before, float after, float floor)
            {
                if (before < floor && after < floor) return wrap <= floor;
                float b = Mathf.Max(before, floor), a = Mathf.Max(after, floor);
                return wrap >= MinNeighbour * b && wrap <= MaxWrapRatio * b && wrap >= MinNeighbour * a && wrap <= MaxWrapRatio * a;
            }
            sb.AppendLine($"  {label}");
            sb.AppendLine("  bone | rot wrap deg | rot before/after deg | rot ratios | pos wrap mm | pos before/after mm | pos ratios | rot/pos median ratio (info) | result");
            bool all = true;
            for (int i = 0; i < bones.Length; i++)
            {
                float rw = RotStep(last, 0, i), pw = PosStep(last, 0, i);
                float rb = RotStep(last - 1, last, i), ra = RotStep(0, 1, i), pb = PosStep(last - 1, last, i), pa = PosStep(0, 1, i);
                float rm = Median(Enumerable.Range(0, last).Select(f => RotStep(f, f + 1, i)));
                float pm = Median(Enumerable.Range(0, last).Select(f => PosStep(f, f + 1, i)));
                bool posRatio = Local(pw, pb, pa, StaticMm);
                bool posFloor = !instantaneous && Mathf.Abs(pw - pb) < floorModelMm && Mathf.Abs(pw - pa) < floorModelMm;
                bool ok = instantaneous ? rw <= StaticDeg && pw <= StaticMm : Local(rw, rb, ra, StaticDeg) && (posRatio || posFloor);
                all &= ok;
                string R(float w, float x) => x < 1e-4f ? "-" : (w / x).ToString("F2");
                sb.AppendLine($"  {bones[i].name} | {rw:F3} | {rb:F3}/{ra:F3} | {R(rw, rb)}/{R(rw, ra)} | {pw:F3} | {pb:F3}/{pa:F3} | {R(pw, pb)}/{R(pw, pa)} | " +
                              $"{R(rw, rm)}/{R(pw, pm)} | {(ok ? (!instantaneous && !posRatio ? "PASS (rev 2.7 floor)" : "PASS") : "FAIL")}");
            }
            sb.AppendLine($"  {(all ? "PASS" : "FAIL")}");
            return all;
        }

        // Nose-to-tail of the model's rest pose (model units, the instance as imported).
        static float RestLength(GameObject go)
        {
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var mesh = new Mesh();
            smr.BakeMesh(mesh, true);
            var b = GeometryUtility.CalculateBounds(mesh.vertices, go.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix);
            Object.DestroyImmediate(mesh);
            return b.size.z;
        }

        // Model-level numbers at scale 1 for the sim (f-engineer): mesh bounds unioned over every Swim frame (model root space),
        // nose-to-tail length at frame 0, root bone travel over one loop, and whether the Face bone (the head rule's bone)
        // sits at the +Z end of the body after the facing fix (f-td: the head rule must point at the nose).
        static string Measure(string path, out bool faceAtNose)
        {
            faceAtNose = false;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == FishImportSettings.ClipName);
            if (!model || !clip) return $"measure {path}: MISSING";
            var go = Object.Instantiate(model);
            go.hideFlags = HideFlags.HideAndDontSave;
            var mesh = new Mesh();
            try
            {
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var face = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Face");
                var root = smr.rootBone;
                int frames = Mathf.RoundToInt(clip.length * clip.frameRate);
                Bounds? union = null, first = null;
                Vector3 root0 = Vector3.zero, rootEnd = Vector3.zero;
                for (int f = 0; f <= frames; f++)
                {
                    clip.SampleAnimation(go, f / clip.frameRate);
                    smr.BakeMesh(mesh, true);
                    var toRoot = go.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                    var b = GeometryUtility.CalculateBounds(mesh.vertices, toRoot);
                    if (union == null) { union = b; first = b; } else { var u = union.Value; u.Encapsulate(b); union = u; }
                    var rp = go.transform.InverseTransformPoint(root.position);
                    if (f == 0) root0 = rp;
                    if (f == frames) rootEnd = rp;
                }
                var fb = first.Value;
                float faceZ = face ? go.transform.InverseTransformPoint(face.position).z : float.NaN;
                faceAtNose = face && faceZ > fb.center.z;
                return $"measure {path}: clipBounds centre {union.Value.center:F3} size {union.Value.size:F3}; frame-0 nose-to-tail {fb.size.z:F3} " +
                       $"(z {fb.min.z:F3}..{fb.max.z:F3}); root '{root.name}' travel per loop {(rootEnd - root0).magnitude:F4}; " +
                       $"Face bone z {faceZ:F3} vs body centre z {fb.center.z:F3}: {(faceAtNose ? "Face at the +Z (nose) end PASS" : "Face NOT at the nose FAIL")}";
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(mesh); }
        }

        [System.Serializable] class ReportFile { public ModelFacingPostprocessor.ReportEntry[] models; }
    }
}
