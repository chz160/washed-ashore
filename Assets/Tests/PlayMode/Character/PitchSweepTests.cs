using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// H5: the camera never shows the inside of the head or body at any stance or pitch from -85 to +85, and the stance eye matches
    /// the research within 3 cm. Per stance and pitch step, with the real animated pose baked to triangles:
    /// (1) no triangle lies between the camera and its near plane, inside the frustum;
    /// (2) no view ray's nearest hit is a back face (the camera would be looking at the inside of the shell, e.g. the neck opening).
    /// Result per stance: TestResults/player-character/sweep/sweep-STANCE.csv and stills at -85, 0 and +85.
    /// </summary>
    public class PitchSweepTests : StanceFixture
    {
        static string OutDir => RunOutput.Dir("sweep");
        const float EyeTolerance = 0.03f;
        // The hidden-head eye point is the Survivor's, in the Head bone frame: eye-point-Survivor.json via StanceFixture.SurvivorEyeLocal (td-49, pod 49).
        const int GridX = 9, GridY = 5;

        static readonly StanceKey[] Keys = { StanceKey.None, StanceKey.Crouch, StanceKey.Squat, StanceKey.Sit, StanceKey.Sleep };
        static readonly float[] StandEyes = { 1.65f, 1.20f, 1.00f, 0.80f, 0.20f };

        struct Tri { public Vector3 a, b, c; public string region; }   // region: BarOneRule.TriRegion of the three corners' dominant bones (td-49 B2)

        [UnityTest, Timeout(1800000)]
        public IEnumerator EveryStance_PitchSweep_NoClipping()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view, "no PlayerCharacterView on the Player: run PlayerCharacterSetup");
            Assert.IsNotNull(view.Body, "the Player has no assembled body");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var head = view.Body.Bone("Head");
            Assert.IsNotNull(head, "no Head bone");
            Assert.AreEqual(0, fp.ShadowRoot.GetComponentsInChildren<Collider>(true).Length, "the shadow body has a Collider");
            if (fp.ViewRoot != null) Assert.AreEqual(0, fp.ViewRoot.GetComponentsInChildren<Collider>(true).Length, "the view body has a Collider");
            var summary = new StringBuilder();
            var failures = new List<string>();
            Vector3 headLocalEye = default;
            MirrorTie.ResetCounters();

            for (int s = 0; s < Keys.Length; s++)
            {
                var stance = (Stance)s;
                if (Keys[s] != StanceKey.None) yield return Go(Keys[s], 6f);
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}: {player.EntryBlockedBy(stance)}");
                yield return new WaitForSeconds(2f);   // let the held pose and its loop settle
                if (s == 0)
                {
                    headLocalEye = SurvivorEyeLocal();
                }
                if (s == 0)
                {
                    // Controller hover h: capsule bottom minus the ground it rests on (flat test floor, grounded, idle 2 s). td ruling: body drop = h.
                    float floorTop = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
                    float capBottom = player.transform.position.y + body.center.y - body.height * 0.5f;
                    float lowest = float.MaxValue;
                    foreach (var tri in Bake(fp)) lowest = Mathf.Min(lowest, Mathf.Min(tri.a.y, Mathf.Min(tri.b.y, tri.c.y)));
                    Assert.AreEqual(0f, lowest - floorTop, 0.01f, "Stand idle: the lowest foot vertex is not on the floor");
                    summary.AppendLine($"Scene World, run {System.DateTime.Now:s}: controller hover h = {capBottom - floorTop:F4} m (capsule bottom minus floor top); player origin above floor {player.transform.position.y - floorTop:F4} m; body drop in use {fp.FootDrop:F4} m; lowest foot vertex above ground (Stand idle) {lowest - floorTop:F4} m");
                }
                float expectedEye = StandEyes[s];
                var csv = new StringBuilder("pitch,nearSlabTriangles,backFaceRays,minBackFaceDistance," + BarOneRule.CsvColumns + "\n");   // backFaceRays and minBackFaceDistance are PRINT columns under R3
                int worstNear = 0, worstBack = 0, worstBar1 = 0;
                float worstBar1Pitch = 0f;
                BarOneRule.Tally worstBar1Tally = default;
                for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
                {
                    player.SetLook(player.Yaw, pitch);
                    yield return null;
                    yield return null;
                    var tris = Bake(fp);
                    Check(player.ViewCamera, tris, out int near, out int back, out float minBack, out BarOneRule.Tally bar1);
                    worstNear = Mathf.Max(worstNear, near);
                    worstBack = Mathf.Max(worstBack, back);
                    if (bar1.Defects > worstBar1) { worstBar1 = bar1.Defects; worstBar1Pitch = pitch; worstBar1Tally = bar1; }
                    csv.AppendLine($"{pitch:F0},{near},{back},{(back > 0 ? minBack.ToString("F3") : "-")},{BarOneRule.CsvValues(bar1)}");
                    if (Mathf.Approximately(pitch, -85f) || Mathf.Approximately(pitch, 0f) || Mathf.Approximately(pitch, 85f))
                        yield return Still($"{OutDir}/{stance}_{pitch:+0;-0;0}.png");
                }
                File.WriteAllText($"{OutDir}/sweep-{stance}.csv", csv.ToString());

                Vector3 eyePoint = head.TransformPoint(headLocalEye);
                float floorTopY = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
                float headAboveFloor = eyePoint.y - floorTopY, camAboveFloor = player.ViewCamera.transform.position.y - floorTopY;
                float horiz = new Vector2(eyePoint.x - player.ViewCamera.transform.position.x, eyePoint.z - player.ViewCamera.transform.position.z).magnitude;
                summary.AppendLine($"{stance}: camera {camAboveFloor:F3} and hidden-head eye point {headAboveFloor:F3} above the floor top (difference {camAboveFloor - headAboveFloor:F3}); pivot-local eye {Eye:F3} (record {expectedEye:F2}); horizontal gap {horiz:F3}; worst near-slab {worstNear}; worst back-face rays {worstBack}");
                if (Mathf.Abs(Eye - expectedEye) > 0.011f) failures.Add($"{stance}: pivot eye {Eye:F3} vs {expectedEye:F2}");
                if (Mathf.Abs(camAboveFloor - headAboveFloor) > EyeTolerance) failures.Add($"{stance}: camera {camAboveFloor:F3} vs hidden-head eye point {headAboveFloor:F3} above the floor (> 3 cm)");
                if (worstNear > 0) failures.Add($"{stance}: {worstNear} triangles in the near slab");
                if (worstBar1 > 0) failures.Add($"{stance}: BAR 1 (render-faithful, {BarOneRule.GText}): up to {worstBar1} rays per frame, worst pitch {worstBar1Pitch:+0;-0;0}: {worstBar1Tally.Clauses}");
                summary.AppendLine($"{stance}: back-first rays up to {worstBack} per frame (print only under R3)");
            }
            File.WriteAllText($"{OutDir}/sweep-summary.txt", summary.ToString());
            Debug.Log("PitchSweepTests:\n" + summary + "tie-resolved hits: " + MirrorTie.Resolved + " (exact mirror pairs only; near ties left to the plain rule: " + MirrorTie.NearTiesNotMirrored + "); " + MirrorTie.StateLine);
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        struct HitTri { public Vector3 a, b, c; public string rend, bone; public int index; public bool cap; }

        /// <summary>
        /// PRINT ONLY (pod 49, td-49 PlayMode-5 triage): every view ray whose nearest hit is a back face, per stance and pitch, with the hit renderer, the dominant skin bone of the
        /// hit triangle (majority of its three vertices), the hit point and its position relative to the Neck bone, and the camera position relative to the Neck and Head bones.
        /// Writes backface-hits.csv and backface-summary.txt. Asserts nothing.
        /// </summary>
        [UnityTest, Explicit("print only: back-face hit attribution"), Timeout(1800000)]
        public IEnumerator BackFaceHits_Print()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.Body, "no body on the Player");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var csv = new StringBuilder("stance,pitch,gridX,gridY,t,renderer,triIndex,hitBone,rayDotNormal,hitPoint,hitMinusNeck,camMinusNeck,camMinusHead,triBonesAllNeckOrHead,neckCap,hitHeightAboveFloor,nearestOpenEdge_m,openEdgeY\n");
            var counts = new Dictionary<string, int>();
            MirrorTie.ResetCounters();
            int resolvedRays = 0, resolvedNonCap = 0;
            var capLog = new StringBuilder("stance,pitch,cameraSignedDistanceToCapPlane_m,capTriangles,capNormal,cameraMinusCapCentre,playerForwardDotNormal,upDotNormal,backShiftFor1cm_m,downShiftFor1cm_m\n");
            var viewBody = view.ViewCopy != null ? view.ViewCopy.Body : view.Body;
            var neck = viewBody.Bone("Neck");
            var headB = viewBody.Bone("Head");
            for (int s = 0; s < Keys.Length; s++)
            {
                var stance = (Stance)s;
                if (Keys[s] != StanceKey.None) yield return Go(Keys[s], 6f);
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}");
                yield return new WaitForSeconds(2f);
                for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
                {
                    player.SetLook(player.Yaw, pitch);
                    yield return null;
                    yield return null;
                    var tris = BakeHits(fp);
                    var openEdges = FindOpenEdges(tris);
                    CapPlane(stance, pitch, tris, player.ViewCamera.transform, player.transform, capLog);
                    var cam = player.ViewCamera;
                    Transform ct = cam.transform;
                    Vector3 o = ct.position;
                    float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfH = halfV * (16f / 9f);
                    for (int iy = 0; iy < GridY; iy++)
                        for (int ix = 0; ix < GridX; ix++)
                        {
                            float x = (ix / (float)(GridX - 1) * 2f - 1f) * halfH, y = (iy / (float)(GridY - 1) * 2f - 1f) * halfV;
                            Vector3 d = (ct.forward + ct.right * x + ct.up * y).normalized;
                            float nearestT = float.MaxValue; bool nearestBack = false; int idx = -1; Tri nearestTri = default;
                            int resolvedBefore = MirrorTie.Resolved;
                            for (int i = 0; i < tris.Count; i++)
                            {
                                var tri = new Tri { a = tris[i].a, b = tris[i].b, c = tris[i].c };
                                if (!RayTriangle(o, d, tri, out float t, out bool back)) continue;
                                if (MirrorTie.Replaces(idx >= 0, nearestT, nearestBack, nearestTri.a, nearestTri.b, nearestTri.c, t, back, tri.a, tri.b, tri.c)) { nearestT = t; nearestBack = back; idx = i; nearestTri = tri; }
                            }
                            if (MirrorTie.Resolved != resolvedBefore) { resolvedRays++; if (idx < 0 || !tris[idx].cap) resolvedNonCap++; }   // qa-49 T3: the tie rule may only act on the neck cap
                            if (idx < 0 || !nearestBack) continue;
                            Vector3 hit = o + d * nearestT;
                            float edgeDist = NearestOpenEdge(openEdges, hit, out float edgeY);
                            float floorTopY = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
                            string key = $"{stance}|{tris[idx].rend}|{(tris[idx].cap ? "NECK-CAP" : "shell")}|{tris[idx].bone}";
                            counts[key] = counts.TryGetValue(key, out int n0) ? n0 + 1 : 1;
                            csv.AppendLine($"{stance},{pitch:F0},{ix},{iy},{nearestT:F3},{tris[idx].rend},{tris[idx].index},{tris[idx].bone},{Vector3.Dot(Vector3.Cross(tris[idx].b - tris[idx].a, tris[idx].c - tris[idx].a).normalized, d):F3},\"{hit.ToString("F3")}\",\"{(hit - neck.position).ToString("F3")}\",\"{(o - neck.position).ToString("F3")}\",\"{(o - headB.position).ToString("F3")}\",{(tris[idx].bone.Contains("Neck") || tris[idx].bone.Contains("Head"))},{tris[idx].cap},{hit.y - floorTopY:F3},{edgeDist:F4},{edgeY - floorTopY:F3}");
                        }
                }
            }
            Debug.Log("BackFaceHits_Print tie-resolved hits: " + MirrorTie.Resolved + " (near ties not mirrored: " + MirrorTie.NearTiesNotMirrored + "); rays resolved: " + resolvedRays + ", of them on non-cap triangles: " + resolvedNonCap + "; rule " + (MirrorTie.Enabled ? "ON" : "OFF"));
            var summary = new StringBuilder(MirrorTie.StateLine + System.Environment.NewLine + "back-face hits (rays whose nearest hit is a back face), grid 9x5, pitch -85..85 step 5, by stance | renderer | dominant bone of the hit triangle:\n");
            foreach (var kv in new SortedDictionary<string, int>(counts)) summary.AppendLine($"{kv.Key}: {kv.Value}");
            File.WriteAllText($"{OutDir}/backface-hits.csv", csv.ToString());
            File.WriteAllText($"{OutDir}/backface-summary.txt", summary.ToString());
            File.WriteAllText($"{OutDir}/cap-plane.csv", capLog.ToString());
            Assert.AreEqual(0, resolvedNonCap, "the mirror tie rule resolved a hit on a triangle that is not part of the neck cap");
            Debug.Log("PitchSweepTests back-face attribution:\n" + summary);
        }

        struct OpenEdge { public Vector3 a, b; }

        static Vector3Int QPos(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e4f), Mathf.RoundToInt(p.y * 1e4f), Mathf.RoundToInt(p.z * 1e4f));

        /// <summary>Edges used by exactly one triangle once vertices at the same position (UV and normal splits) are welded at 0.1 mm: the open boundary of the baked view mesh.</summary>
        static List<OpenEdge> FindOpenEdges(List<HitTri> tris)
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

        static float NearestOpenEdge(List<OpenEdge> edges, Vector3 p, out float edgeY)
        {
            float best = float.MaxValue; edgeY = float.NaN;
            foreach (var e in edges)
            {
                Vector3 ab = e.b - e.a;
                float t = Mathf.Clamp01(Vector3.Dot(p - e.a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
                float d = Vector3.Distance(p, e.a + ab * t);
                if (d < best) { best = d; edgeY = (e.a.y + e.b.y) * 0.5f; }
            }
            return best;
        }

        /// <summary>The neck cap plane of this frame (centre vertex shared by every cap triangle, outward normal from the triangle winding) and the camera's signed distance to it (positive = on the cap's front, the head side).</summary>
        static void CapPlane(Stance stance, float pitch, List<HitTri> tris, Transform cam, Transform playerT, StringBuilder log)
        {
            Vector3 n = Vector3.zero; HitTri? first = null; int count = 0;
            foreach (var t in tris) if (t.cap) { n += Vector3.Cross(t.b - t.a, t.c - t.a); count++; first ??= t; }
            if (count == 0 || first == null) { log.AppendLine($"{stance},{pitch:F0},no cap triangles found"); return; }
            n.Normalize();
            Vector3 centre = default; bool found = false;
            foreach (var c in new[] { first.Value.a, first.Value.b, first.Value.c })
            {
                bool all = true;
                foreach (var t in tris) if (t.cap && QPos(t.a) != QPos(c) && QPos(t.b) != QPos(c) && QPos(t.c) != QPos(c)) { all = false; break; }
                if (all) { centre = c; found = true; break; }
            }
            if (!found) { log.AppendLine($"{stance},{pitch:F0},cap triangles share no common vertex ({count} triangles)"); return; }
            float d = Vector3.Dot(cam.position - centre, n);
            float fwd = Vector3.Dot(playerT.forward, n), up = Vector3.Dot(Vector3.up, n);
            string back = d >= 0.01f ? "0.000" : (fwd > 0.05f ? ((0.01f - d) / fwd).ToString("F3") : "n/a (back shift does not move the camera toward the head side)");
            string down = d >= 0.01f ? "0.000" : (up > 0.05f ? ((0.01f - d) / up).ToString("F3") : "n/a");
            log.AppendLine($"{stance},{pitch:F0},{d:F4},{count},\"{n.ToString("F3")}\",\"{(cam.position - centre).ToString("F3")}\",{fwd:F3},{up:F3},{back},{down}");
        }


        static List<HitTri> BakeHits(FirstPersonBody fp)
        {
            var tris = new List<HitTri>(40000);
            var mesh = new Mesh();
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled) continue;
                if (!(r is SkinnedMeshRenderer smr)) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                var w = smr.sharedMesh.GetAllBoneWeights();
                var perVertex = smr.sharedMesh.GetBonesPerVertex();
                var bones = smr.bones;
                var firstIndex = new int[v.Length];
                int run = 0;
                for (int i = 0; i < v.Length; i++) { firstIndex[i] = run; run += perVertex[i]; }
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                string Dom(int vi)
                {
                    int bi = -1; float best = -1f;
                    for (int k = 0; k < perVertex[vi]; k++)
                    {
                        var bw = w[firstIndex[vi] + k];
                        if (bw.weight > best) { best = bw.weight; bi = bw.boneIndex; }
                    }
                    return bi >= 0 && bi < bones.Length && bones[bi] != null ? bones[bi].name : "?";
                }
                // The neck cap is a fan around one centre vertex that only the cap has: the baked position shared by the most triangle corners (split vertices share a position).
                var valence = new Dictionary<Vector3Int, int>();
                Vector3Int Q5(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
                var centre = default(Vector3Int); int centreValence = 0;
                for (int i = 0; i < t.Length; i++)
                {
                    var key = Q5(v[t[i]]);
                    int n1 = valence.TryGetValue(key, out int c0) ? c0 + 1 : 1;
                    valence[key] = n1;
                    if (n1 > centreValence) { centreValence = n1; centre = key; }
                }
                Debug.Log($"BakeHits: {smr.name} cap-centre candidate: {centreValence} triangle corners share baked position {centre}, dominant bone {Dom(System.Array.IndexOf(t, 0) >= 0 ? t[0] : 0)}");
                for (int i = 0; i < t.Length; i += 3)
                {
                    bool isCap = centreValence >= 12 && (Q5(v[t[i]]) == centre || Q5(v[t[i + 1]]) == centre || Q5(v[t[i + 2]]) == centre);
                    string b0 = Dom(t[i]), b1 = Dom(t[i + 1]), b2 = Dom(t[i + 2]);
                    string bone = b0 == b1 || b0 == b2 ? b0 : (b1 == b2 ? b1 : b0);
                    tris.Add(new HitTri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], rend = smr.name, bone = bone, index = i / 3, cap = isCap });
                }
            }
            Object.Destroy(mesh);
            return tris;
        }

        /// <summary>World-space triangles of every body renderer the camera can see (the head is shadow-only and left out).</summary>
        static List<Tri> Bake(FirstPersonBody fp)
        {
            var tris = new List<Tri>(40000);
            var mesh = new Mesh();
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled) continue;
                if (!(r is SkinnedMeshRenderer smr)) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                var weights = smr.sharedMesh.boneWeights;
                var bones = smr.bones;
                string Reg(int vi) => weights.Length > vi && weights[vi].boneIndex0 >= 0 && weights[vi].boneIndex0 < bones.Length ? BarOneRule.Region(bones[weights[vi].boneIndex0]) : "UNKNOWN";
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < t.Length; i += 3)
                    tris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], region = BarOneRule.TriRegion(Reg(t[i]), Reg(t[i + 1]), Reg(t[i + 2])) });
            }
            Object.Destroy(mesh);
            return tris;
        }

        static void Check(Camera cam, List<Tri> tris, out int nearSlab, out int backRays, out float minBackDistance, out BarOneRule.Tally bar1)
        {
            bar1 = default;
            Transform ct = cam.transform;
            Vector3 o = ct.position;
            float n = cam.nearClipPlane;
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfH = halfV * (16f / 9f);
            nearSlab = backRays = 0;
            minBackDistance = float.MaxValue;

            // (1) Segments from the camera to a grid on the near plane: any triangle crossing one is inside the clipped slab.
            // (2) Rays through the same grid, to the far end of the body: the nearest hit must not be a back face.
            var dirs = new List<Vector3>();
            for (int iy = 0; iy < GridY; iy++)
                for (int ix = 0; ix < GridX; ix++)
                {
                    float x = (ix / (float)(GridX - 1) * 2f - 1f) * halfH, y = (iy / (float)(GridY - 1) * 2f - 1f) * halfV;
                    dirs.Add((ct.forward + ct.right * x + ct.up * y).normalized);
                }
            foreach (var d in dirs)
            {
                float nearestT = float.MaxValue;
                bool nearestBack = false;
                Tri nearestTri = default;
                var front = new BarOneRule.RayState();   // nearest FRONT-facing hit of this ray; the winner below is chosen exactly as before (td-49 B2)
                float nearLimit = n / Vector3.Dot(d, ct.forward);
                foreach (var tri in tris)
                {
                    if (!RayTriangle(o, d, tri, out float t, out bool backFacing)) continue;
                    front.Note(backFacing, t, tri.region);
                    if (MirrorTie.Replaces(nearestT < float.MaxValue, nearestT, nearestBack, nearestTri.a, nearestTri.b, nearestTri.c, t, backFacing, tri.a, tri.b, tri.c)) { nearestT = t; nearestBack = backFacing; nearestTri = tri; }
                    if (t >= 0f && t <= nearLimit) nearSlab++;
                }
                if (nearestT < float.MaxValue && nearestBack) { backRays++; minBackDistance = Mathf.Min(minBackDistance, nearestT); }
                if (nearestT < float.MaxValue) bar1.Add(BarOneRule.Judge(nearestBack, nearestT, nearestTri.region, front.Found, front.T, front.Region), nearestBack, nearestBack && front.Found ? front.T - nearestT : float.NaN, nearestTri.region);
            }
        }

        // Moller-Trumbore, two-sided; reports whether the ray hit the triangle's back (the ray runs along the geometric normal).
        static bool RayTriangle(Vector3 o, Vector3 d, Tri tri, out float t, out bool backFacing)
        {
            t = 0f;
            backFacing = false;
            Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a;
            Vector3 p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = o - tri.a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            if (t < 0f) return false;
            // Unity front faces: Cross(b - a, c - a) points out of the front. A ray that travels along it hits the back.
            Vector3 normal = Vector3.Cross(e1, e2);
            backFacing = Vector3.Dot(normal, d) > 0f;
            return true;
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
