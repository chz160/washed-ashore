using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// S-A9, the player-stances.md section 3a range ends, proved by script at BOTH ends (director amend1):
    /// eye ends (crouch 1.10 / 1.30, squat 0.90 / 1.10, sit 0.75 / 0.90, sleep 0.25): the -85..+85 sweep on the view body with no near-slab
    /// triangle and no back face, the hidden-head eye match within 3 cm (camera minus the SHADOW body head eye point, world y), and S-A4
    /// headroom under a 1.65 m ceiling. Sleep pitch -45 and -85: a still each and the sleep sweep. Crouch speed 1.25 and 2.50: foot slide
    /// per plant on CrouchWalk, and the playback rate within 0.7 to 1.4. The clip variants come from the artist's RangeLow / RangeHigh
    /// override controllers. Each end prints PASS or FAIL in TestResults/player-character/range/range-ends.txt; a failed end shrinks its range.
    /// </summary>
    public class RangeEndTests : StanceFixture
    {
        static string OutDir => RunOutput.Dir("range");
        // the hidden-head eye point is the Survivor's: eye-point-Survivor.json via StanceFixture.SurvivorEyeLocal (td-49, pod 49)
        const string RangeLow = "Assets/Art/Character/CharacterAnimator_Men_RangeLow.overrideController";
        const string RangeHigh = "Assets/Art/Character/CharacterAnimator_Men_RangeHigh.overrideController";

        struct Tri { public Vector3 a, b, c; public string rend, bone, region; public int index; public bool cap; }   // region: BarOneRule.TriRegion of the three corners' dominant bones (td-49 B2)

        // PRINT-ONLY attribution of every back-face ray (td-49 follow-up to baton 8): on when the environment variable RANGEEND_ATTRIB is "1", off otherwise.
        static StringBuilder attribLog;
        static string attribLabel = "";
        static float attribPitch, attribFloorTop;

        /// <summary>The hidden-setting echo (td-49): the raw value of RANGEEND_ATTRIB and whether attribution is on.</summary>
        static string AttribSwitch => "RANGEEND_ATTRIB=" + (System.Environment.GetEnvironmentVariable("RANGEEND_ATTRIB") ?? "unset") + " (attribution " + (attribLog != null ? "ON" : "OFF") + ")";

        PlayerCharacterView view;
        FirstPersonBody fp;
        Transform head;
        Vector3 headLocalEye;
        readonly StringBuilder log = new StringBuilder();
        string eyeFigures = "";

        IEnumerator Start(bool high)
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.Body, "no body on the Player");
            fp = view.FirstPerson;
            head = view.Body.Bone("Head");
            headLocalEye = SurvivorEyeLocal();
            Directory.CreateDirectory(OutDir);
            // The range ends run on the artist's range controllers: the SHADOW on its variant (the capped Jog and Sprint arms stay mapped to _Full) and the VIEW on the base-based one.
            ApplyRangeControllers(high);
            RangeGuard(high);
            yield return null;
        }

        /// <summary>Assigns the range controllers of the given end to both Animators, from the appearance, as the -bbStance launch argument does.</summary>
        void ApplyRangeControllers(bool high)
        {
            var app = view.Body.Appearance;
            var shadowCtl = app.RangeController(high, true);
            var viewCtl = app.RangeController(high, false);
            Assert.IsNotNull(shadowCtl, $"the appearance has no shadow range controller for {(high ? "High" : "Low")}");
            Assert.IsNotNull(viewCtl, $"the appearance has no view range controller for {(high ? "High" : "Low")}");
            view.Body.Animator.runtimeAnimatorController = shadowCtl;
            if (view.ViewCopy != null && view.ViewCopy.ViewAnimator != null) view.ViewCopy.ViewAnimator.runtimeAnimatorController = viewCtl;
        }

        /// <summary>
        /// td's guard: these tests run on the range-end controllers, so their arm and gait readings are NOT the base clips' (the proofs for those are in LookDownTests and
        /// FootSlideTests). Fails unless both Animators really run the range controllers of this end; prints their names.
        /// </summary>
        void RangeGuard(bool high)
        {
            var app = view.Body.Appearance;
            Assert.AreSame(app.RangeController(high, true), view.Body.Animator.runtimeAnimatorController, "the shadow Animator is not on its range controller: do not read range-end results from this run");
            if (view.ViewCopy != null && view.ViewCopy.ViewAnimator != null)
                Assert.AreSame(app.RangeController(high, false), view.ViewCopy.ViewAnimator.runtimeAnimatorController, "the view Animator is not on its range controller");
            Assert.IsTrue(view.Body.Animator.runtimeAnimatorController.name.Contains("Range"), "not a range controller");
            log.AppendLine($"range guard ({(high ? "High" : "Low")}): shadow Animator on '{view.Body.Animator.runtimeAnimatorController.name}', view Animator on '{(view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator.runtimeAnimatorController.name : "none")}'");
        }

        static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

        /// <summary>Reaches the stance, settles, runs the -85..+85 sweep, the eye match and (for crouch) the headroom case. Returns the failure text or null.</summary>
        IEnumerator EyeEnd(string name, StanceKey key, Stance stance, float eye, bool high, System.Action<string> done)
        {
            var fails = new List<string>();
            yield return Go(key, 6f);
            if (player.Stance != stance) { done($"{name}: could not reach {stance}: {player.EntryBlockedBy(stance)}"); yield break; }
            yield return new WaitForSeconds(2f);
            float floorTop = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
            float camY = player.ViewCamera.transform.position.y - floorTop;
            float headY = head.TransformPoint(headLocalEye).y - floorTop;
            if (Mathf.Abs(camY - headY) > 0.03f) fails.Add($"eye match {camY:F3} vs {headY:F3} (diff {camY - headY:F3})");
            if (Mathf.Abs((player.ViewCamera.transform.position.y - player.transform.position.y) - eye) > 0.011f) fails.Add("pivot eye is not the tuned value");
            int near = 0, back = 0;
            BarOneRule.Tally bar1 = default;
            for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
            {
                player.SetLook(player.Yaw, pitch);
                yield return null;
                yield return null;
                attribLabel = name; attribPitch = pitch; attribFloorTop = floorTop;
                Check(player.ViewCamera, Bake(), out int n, out int b, out BarOneRule.Tally t1);
                near += n; back += b; bar1.Merge(t1);
            }
            if (near > 0 || bar1.Defects > 0) fails.Add($"sweep near-slab {near}, BAR 1 defects {bar1.Defects} ({bar1.Clauses}; max gap {bar1.maxGap * 1000f:F1} mm, {BarOneRule.GText}; back-first {back}, print only)");
            eyeFigures = $"camera {camY:F3} m, hidden-head eye point {headY:F3} m above the floor, error {camY - headY:+0.000;-0.000} m (limit 0.030); pivot eye {player.ViewCamera.transform.position.y - player.transform.position.y:F3} m (tuned {eye:F2}); sweep -85..+85 step 5: near-slab {near}, back faces {back}";
            done(fails.Count == 0 ? null : $"{name}: " + string.Join("; ", fails));
        }

        [UnityTest, Timeout(3000000)]
        public IEnumerator EyeEnds_LowAndHigh()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            MirrorTie.ResetCounters();
            attribLog = System.Environment.GetEnvironmentVariable("RANGEEND_ATTRIB") == "1" ? new StringBuilder("# " + MirrorTie.StateLine + "; " + "RANGEEND_ATTRIB=" + System.Environment.GetEnvironmentVariable("RANGEEND_ATTRIB") + " (attribution ON)" + System.Environment.NewLine + "case,pitch,gridX,gridY,t,renderer,triIndex,neckCapOrShell,hitBone,hitHeightAboveFloor,nearestOpenEdge_m,hitPoint,ruleState,frontT,frontRegion,verdict" + System.Environment.NewLine) : null;
            var results = new List<string>();
            var cases = new[]
            {
                ("crouch 1.10 (Low)", StanceKey.Crouch, Stance.Crouch, 1.10f, false),
                ("crouch 1.30 (High)", StanceKey.Crouch, Stance.Crouch, 1.30f, true),
                ("squat 0.90 (Low)", StanceKey.Squat, Stance.Squat, 0.90f, false),
                ("squat 1.10 (High)", StanceKey.Squat, Stance.Squat, 1.10f, true),
                ("sit 0.75 (Low)", StanceKey.Sit, Stance.Sit, 0.75f, false),
                ("sit 0.90 (High)", StanceKey.Sit, Stance.Sit, 0.90f, true),
                ("sleep 0.25 (High)", StanceKey.Sleep, Stance.Sleep, 0.25f, true),
            };
            foreach (var c in cases)
            {
                yield return Start(c.Item5);
                var t = player.StanceTuning;
                switch (c.Item3)
                {
                    case Stance.Crouch: t.crouchEye = c.Item4; break;
                    case Stance.Squat: t.squatEye = c.Item4; break;
                    case Stance.Sit: t.sitEye = c.Item4; break;
                    case Stance.Sleep: t.sleepEye = c.Item4; break;
                }
                string failure = null;
                yield return EyeEnd(c.Item1, c.Item2, c.Item3, c.Item4, c.Item5, s => failure = s);
                // S-A4 headroom at this end (crouch only): stand-up under a 1.65 m ceiling is refused.
                if (failure == null && c.Item3 == Stance.Crouch)
                {
                    var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ceiling.transform.localScale = new Vector3(6f, 0.2f, 6f);
                    ceiling.transform.position = player.transform.position + new Vector3(0f, 1.65f + 0.1f, 0f);
                    yield return Frames(3);
                    yield return Go(StanceKey.Crouch, 1f);
                    if (player.Stance != Stance.Crouch) failure = $"{c.Item1}: stood up under a 1.65 m ceiling (S-A4)";
                    Object.Destroy(ceiling);
                }
                string line = $"{Verdict(failure == null)} {c.Item1} :: {eyeFigures}{(c.Item3 == Stance.Crouch ? $"; headroom under a 1.65 m ceiling: {(failure != null && failure.Contains("S-A4") ? "stood up (FAIL)" : "refused (ok)")}" : "")}" + (failure == null ? "" : " :: FAILURE " + failure);
                results.Add(line);
                log.AppendLine(line);
                Debug.Log("RangeEndTests: " + line);
            }
            Debug.Log("RangeEndTests eye ends: tie-resolved hits: " + MirrorTie.Resolved + " (near ties not mirrored: " + MirrorTie.NearTiesNotMirrored + "; rule " + (MirrorTie.Enabled ? "ON" : "OFF") + ")");
            if (attribLog != null) File.WriteAllText($"{OutDir}/range-backface-attrib-eyes.csv", attribLog.ToString());
            log.Insert(0, MirrorTie.StateLine + System.Environment.NewLine + AttribSwitch + System.Environment.NewLine);
            File.WriteAllText($"{OutDir}/range-eyes.txt", log.ToString());
            Assert.IsFalse(results.Exists(r => r.StartsWith("FAIL")), string.Join("\n", results));
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator SleepPitchEnds()
        {
            MirrorTie.ResetCounters();
            var results = new List<string>();
            foreach (float pitchEnd in new[] { -45f, -85f })
            {
                yield return Start(true);
                player.StanceTuning.sleepPitch = pitchEnd;
                yield return Go(StanceKey.Sleep, 6f);
                yield return new WaitForSeconds(2f);
                float reachedPitch = player.Pitch;
                bool reached = Mathf.Abs(reachedPitch - pitchEnd) < 1f;
                yield return Still($"{OutDir}/sleep-pitch{pitchEnd:F0}.png");
                int near = 0, back = 0;
                BarOneRule.Tally bar1 = default;
                for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
                {
                    player.SetLook(player.Yaw, pitch);
                    yield return null;
                    yield return null;
                    attribLabel = "sleep pitch " + pitchEnd.ToString("F0"); attribPitch = pitch; attribFloorTop = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
                    Check(player.ViewCamera, Bake(), out int n, out int b, out BarOneRule.Tally t1);
                    near += n; back += b; bar1.Merge(t1);
                }
                string line = $"{Verdict(reached && near == 0 && bar1.Defects == 0)} sleep pitch {pitchEnd:F0}: pitch reached {reachedPitch:F1}, sweep near-slab {near}, BAR 1 defects {bar1.Defects} ({bar1.Clauses}; max gap {bar1.maxGap * 1000f:F1} mm, {BarOneRule.GText}; back-first {back}, print only)";
                results.Add(line);
                Debug.Log("RangeEndTests: " + line);
            }
            Debug.Log("RangeEndTests sleep pitch ends: tie-resolved hits: " + MirrorTie.Resolved + " (near ties not mirrored: " + MirrorTie.NearTiesNotMirrored + "; rule " + (MirrorTie.Enabled ? "ON" : "OFF") + ")");
            File.AppendAllText($"{OutDir}/range-sleep-pitch.txt", MirrorTie.StateLine + "\n" + string.Join("\n", results) + "\n");
            Assert.IsFalse(results.Exists(r => r.StartsWith("FAIL")), string.Join("\n", results));
        }

        List<Tri> Bake()
        {
            var tris = new List<Tri>(40000);
            var mesh = new Mesh();
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer smr)) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                BoneWeight1[] w = null; byte[] perVertex = null; Transform[] bones = null; int[] firstIndex = null;
                {
                    w = smr.sharedMesh.GetAllBoneWeights().ToArray();   // always read now: the region of every triangle is needed by the render-faithful Bar 1 rule
                    perVertex = smr.sharedMesh.GetBonesPerVertex().ToArray();
                    bones = smr.bones;
                    firstIndex = new int[v.Length];
                    int run = 0;
                    for (int i = 0; i < v.Length; i++) { firstIndex[i] = run; run += perVertex[i]; }
                }
                string Dom(int vi)
                {
                    int bi = -1; float best = -1f;
                    for (int k = 0; k < perVertex[vi]; k++) { var bw = w[firstIndex[vi] + k]; if (bw.weight > best) { best = bw.weight; bi = bw.boneIndex; } }
                    return bi >= 0 && bi < bones.Length && bones[bi] != null ? bones[bi].name : "?";
                }
                string Reg(int vi)
                {
                    int bi = -1; float best = -1f;
                    for (int k = 0; k < perVertex[vi]; k++) { var bw = w[firstIndex[vi] + k]; if (bw.weight > best) { best = bw.weight; bi = bw.boneIndex; } }
                    return bi >= 0 && bi < bones.Length && bones[bi] != null ? BarOneRule.Region(bones[bi]) : "UNKNOWN";
                }
                // neck cap = the fan around the centre vertex: the baked position shared by the most triangle corners (at least 12), as in PitchSweepTests.BakeHits
                Vector3Int capCentre = default; int capValence = 0;
                if (attribLog != null)
                {
                    var valence = new Dictionary<Vector3Int, int>();
                    for (int i = 0; i < t.Length; i++)
                    {
                        var key = QPos(v[t[i]]);
                        int n1 = valence.TryGetValue(key, out int c0) ? c0 + 1 : 1;
                        valence[key] = n1;
                        if (n1 > capValence) { capValence = n1; capCentre = key; }
                    }
                }
                for (int i = 0; i < t.Length; i += 3)
                {
                    string bone = null;
                    bool isCap = attribLog != null && capValence >= 12 && (QPos(v[t[i]]) == capCentre || QPos(v[t[i + 1]]) == capCentre || QPos(v[t[i + 2]]) == capCentre);
                    if (attribLog != null) { string b0 = Dom(t[i]), b1 = Dom(t[i + 1]), b2 = Dom(t[i + 2]); bone = b0 == b1 || b0 == b2 ? b0 : (b1 == b2 ? b1 : b0); }
                    tris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], rend = smr.name, bone = bone, region = BarOneRule.TriRegion(Reg(t[i]), Reg(t[i + 1]), Reg(t[i + 2])), index = i / 3, cap = isCap });
                }
            }
            Object.Destroy(mesh);
            return tris;
        }

        struct OpenEdge { public Vector3 a, b; }

        static Vector3Int QPos(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));

        /// <summary>Edges used by exactly one triangle once vertices at the same position are welded at 0.1 mm: the open boundary of the baked view mesh (attribution print only).</summary>
        static List<OpenEdge> FindOpenEdges(List<Tri> tris)
        {
            var count = new Dictionary<(Vector3Int, Vector3Int), int>();
            var ends = new Dictionary<(Vector3Int, Vector3Int), OpenEdge>();
            void Add(Vector3 p, Vector3 q)
            {
                var kp = QPos(p); var kq = QPos(q);
                if (kp == kq) return;
                var key = (kp.x, kp.y, kp.z).CompareTo((kq.x, kq.y, kq.z)) <= 0 ? (kp, kq) : (kq, kp);
                count[key] = count.TryGetValue(key, out int n) ? n + 1 : 1;
                if (!ends.ContainsKey(key)) ends[key] = new OpenEdge { a = p, b = q };
            }
            foreach (var t in tris) { Add(t.a, t.b); Add(t.b, t.c); Add(t.c, t.a); }
            var open = new List<OpenEdge>();
            foreach (var kv in count) if (kv.Value == 1) open.Add(ends[kv.Key]);
            return open;
        }

        static float NearestOpenEdge(List<OpenEdge> edges, Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var e in edges)
            {
                Vector3 ab = e.b - e.a;
                float t = Mathf.Clamp01(Vector3.Dot(p - e.a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector3.Distance(p, e.a + ab * t));
            }
            return best;
        }

        static void Check(Camera cam, List<Tri> tris, out int nearSlab, out int backRays, out BarOneRule.Tally bar1)
        {
            bar1 = default;
            Transform ct = cam.transform;
            Vector3 o = ct.position;
            float n = cam.nearClipPlane;
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfH = halfV * (16f / 9f);
            nearSlab = backRays = 0;
            var openEdges = attribLog != null ? FindOpenEdges(tris) : null;
            for (int iy = 0; iy < 9; iy++)
                for (int ix = 0; ix < 16; ix++)
                {
                    float x = (ix / 15f * 2f - 1f) * halfH, y = (iy / 8f * 2f - 1f) * halfV;
                    Vector3 d = (ct.forward + ct.right * x + ct.up * y).normalized;
                    float nearLimit = n / Vector3.Dot(d, ct.forward), best = float.MaxValue;
                    bool backFace = false;
                    Tri bestTri = default;
                    var front = new BarOneRule.RayState();   // nearest FRONT-facing hit of this ray; the winner below is chosen exactly as before (td-49 B2)
                    foreach (var tri in tris)
                    {
                        Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a;
                        Vector3 p = Vector3.Cross(d, e2);
                        float det = Vector3.Dot(e1, p);
                        if (Mathf.Abs(det) < 1e-9f) continue;
                        float inv = 1f / det;
                        Vector3 s = o - tri.a;
                        float u = Vector3.Dot(s, p) * inv;
                        if (u < 0f || u > 1f) continue;
                        Vector3 q = Vector3.Cross(s, e1);
                        float v = Vector3.Dot(d, q) * inv;
                        if (v < 0f || u + v > 1f) continue;
                        float t = Vector3.Dot(e2, q) * inv;
                        if (t < 0f) continue;
                        if (t <= nearLimit) nearSlab++;
                        bool backNow = Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f;
                        front.Note(backNow, t, tri.region);
                        if (MirrorTie.Replaces(best < float.MaxValue, best, backFace, bestTri.a, bestTri.b, bestTri.c, t, backNow, tri.a, tri.b, tri.c)) { best = t; backFace = backNow; bestTri = tri; }
                    }
                    var verdict = best < float.MaxValue ? BarOneRule.Judge(backFace, best, bestTri.region, front.Found, front.T, front.Region) : BarOneRule.Verdict.Ok;
                    if (best < float.MaxValue) bar1.Add(verdict, backFace, backFace && front.Found ? front.T - best : float.NaN, bestTri.region);
                    if (best < float.MaxValue && backFace)
                    {
                        backRays++;
                        if (attribLog != null)
                        {
                            Vector3 hit = o + d * best;
                            attribLog.AppendLine($"{attribLabel},{attribPitch:F0},{ix},{iy},{best:F3},{bestTri.rend},{bestTri.index},{(bestTri.cap ? "NECK-CAP" : "shell")},{bestTri.bone},{hit.y - attribFloorTop:F3},{NearestOpenEdge(openEdges, hit):F4},{hit.x:F3};{hit.y:F3};{hit.z:F3},{MirrorTie.StateLine.Replace(",", ";")},{(front.Found ? (front.T).ToString("F4") : "none")},{(front.Found ? front.Region : "none")},{verdict}");
                        }
                    }
                }
        }

        IEnumerator Still(string path)
        {
            var cam = player.ViewCamera;
            var rt = new RenderTexture(960, 540, 24);
            var old = cam.targetTexture;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = old;
                RenderTexture.active = null;
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
            yield return null;
        }
    }
}
