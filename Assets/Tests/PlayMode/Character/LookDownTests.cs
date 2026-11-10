using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// Director amend2 and td method ruling: at pitch +60 and +85, Stand and Crouch, the camera must show (1) no neck, head or shoulder-top
    /// triangle and no back face, (2) chest or belly in the bottom 20% band of the frame with legs and feet beyond. The view body is
    /// moved by the pitch-driven look-down offset (a monotone-cubic point table per stance); the camera and the shadow body never move.
    /// The sweep forces each actual back offset (LookDownTuning.debugBackOverride) from -0.25 to 0.80 m at pitches 40 to 85 and prints a
    /// table (TestResults/player-character/lookdown/); then the configured tables are asserted, still and at four walk-cycle points.
    /// </summary>
    public class LookDownTests : StanceFixture
    {
        static string OutDir => RunOutput.Dir("lookdown");
        LookDownBfAttrib bf;   // G1: set only when LOOKDOWN_BF_ATTRIB=1, print only
        const int GridX = 32, GridY = 18;
        const float FloorHalf = 30f, FloorMarginMin = 2f, SpeedTolerance = 0.05f;
        readonly StringBuilder gaitMax = new StringBuilder("gait,pitch,maxNeckRays,cyclePoint\n");

        enum Cat { Other, NeckShoulder, Torso, Legs, Hand }

        struct Tri { public Vector3 a, b, c; public Cat cat; public string bone; public string weights; public string region; }   // region: BarOneRule.TriRegion of the three corners' dominant bones (td-49 B2)

        struct Result
        {
            public int rays, neckShoulder, backFaces, torsoBottomBand, torsoRays, legs, nearSlab, handRays;   // backFaces = back-first rays, PRINT ONLY under R3
            public BarOneRule.Tally bar1;   // the render-faithful Bar 1 clauses (BarOneRule, td-49 B1-B3)
            public List<(float nx, float ny, string bone, string weights)> hits;   // every counted neck/shoulder ray: frame position (16:9 normalised), dominant bone, top-2 weights
            public string neckBones;   // the dominant bone of every counted neck/shoulder ray, with counts, e.g. "Shoulder.L x3, Neck x1"
            public float lowestTorsoNy;   // lowest frame row (-1 bottom, +1 top) any torso ray reaches; 2 when none
            public override string ToString() => $"neck/shoulder {neckShoulder}{(neckShoulder > 0 ? " [" + neckBones + "]" : "")}, back-first {backFaces} (print only), {bar1}, torso in bottom band {torsoBottomBand} (torso rays {torsoRays}, lowest row {(lowestTorsoNy > 1.5f ? "none" : lowestTorsoNy.ToString("F2"))}), legs/feet {legs}, near-slab {nearSlab}";
            public bool Passes => neckShoulder == 0 && bar1.Defects == 0 && nearSlab == 0 && torsoBottomBand > 0 && legs > 0;
        }

        static Cat Classify(string bone)
        {
            if (bone.Contains("Neck") || bone.Contains("Head") || bone.Contains("Shoulder")) return Cat.NeckShoulder;
            if (bone.Contains("Hand") || bone.Contains("Index") || bone.Contains("Middle") || bone.Contains("Ring") || bone.Contains("Pinky") || bone.Contains("Thumb") || bone.Contains("LowerArm")) return Cat.Hand;
            if (bone.Contains("Chest") || bone.Contains("Abdomen")) return Cat.Torso;
            if (bone.Contains("Leg") || bone.Contains("Foot") || bone.StartsWith("PT")) return Cat.Legs;
            return Cat.Other;
        }

        static List<Tri> Bake(FirstPersonBody fp)
        {
            var tris = new List<Tri>(40000);
            var mesh = new Mesh();
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                var weights = smr.sharedMesh.boneWeights;
                var bones = smr.bones;
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < t.Length; i += 3)
                {
                    int bi = weights.Length > t[i] ? weights[t[i]].boneIndex0 : -1;
                    string bone = bi >= 0 && bi < bones.Length && bones[bi] != null ? bones[bi].name : "";
                    string wtext = "";
                    if (weights.Length > t[i])
                    {
                        var bw = weights[t[i]];
                        string N(int idx) => idx >= 0 && idx < bones.Length && bones[idx] != null ? bones[idx].name : "?";
                        wtext = $"{N(bw.boneIndex0)}: {bw.weight0:F2}, {N(bw.boneIndex1)}: {bw.weight1:F2}";
                    }
                    string Reg(int vi) => weights.Length > vi && weights[vi].boneIndex0 >= 0 && weights[vi].boneIndex0 < bones.Length ? BarOneRule.Region(bones[weights[vi].boneIndex0]) : "UNKNOWN";
                    tris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], cat = Classify(bone), bone = bone, weights = wtext, region = BarOneRule.TriRegion(Reg(t[i]), Reg(t[i + 1]), Reg(t[i + 2])) });
                }
            }
            Object.Destroy(mesh);
            return tris;
        }

        static Result Measure(Camera cam, List<Tri> tris)
        {
            var res = new Result { lowestTorsoNy = 2f };
            var neckCount = new Dictionary<string, int>();
            Transform ct = cam.transform;
            Vector3 o = ct.position;
            float n = cam.nearClipPlane;
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfH = halfV * (16f / 9f);
            for (int iy = 0; iy < GridY; iy++)
                for (int ix = 0; ix < GridX; ix++)
                {
                    float x = (ix / (float)(GridX - 1) * 2f - 1f) * halfH, ny = iy / (float)(GridY - 1) * 2f - 1f, y = ny * halfV;
                    Vector3 d = (ct.forward + ct.right * x + ct.up * y).normalized;
                    float nearLimit = n / Vector3.Dot(d, ct.forward);
                    float best = float.MaxValue;
                    bool backFace = false;
                    Cat cat = Cat.Other;
                    string hitBone = "", hitWeights = "";
                    Tri bestTri = default;
                    var front = new BarOneRule.RayState();   // nearest FRONT-facing hit of this ray (td-49 B2); the winner below is chosen exactly as before
                    foreach (var tri in tris)
                    {
                        if (!RayHit(o, d, tri, out float t, out bool back)) continue;
                        front.Note(back, t, tri.region);
                        if (t <= nearLimit) res.nearSlab++;
                        if (MirrorTie.Replaces(best < float.MaxValue, best, backFace, bestTri.a, bestTri.b, bestTri.c, t, back, tri.a, tri.b, tri.c)) { best = t; backFace = back; cat = tri.cat; hitBone = tri.bone; hitWeights = tri.weights; bestTri = tri; }
                    }
                    res.rays++;
                    if (best == float.MaxValue) continue;
                    if (backFace) res.backFaces++;
                    res.bar1.Add(BarOneRule.Judge(backFace, best, bestTri.region, front.Found, front.T, front.Region), backFace, backFace && front.Found ? front.T - best : float.NaN, bestTri.region);
                    if (cat == Cat.NeckShoulder) { res.neckShoulder++; neckCount[hitBone] = neckCount.TryGetValue(hitBone, out int nc) ? nc + 1 : 1; (res.hits ??= new List<(float, float, string, string)>()).Add((x / halfH, ny, hitBone, hitWeights)); }
                    if (cat == Cat.Legs) res.legs++;
                    if (cat == Cat.Hand) res.handRays++;
                    if (cat == Cat.Torso)
                    {
                        res.torsoRays++;
                        res.lowestTorsoNy = Mathf.Min(res.lowestTorsoNy, ny);
                        if (ny <= -0.6f) res.torsoBottomBand++;   // bottom 20% of the frame height
                    }
                }
            res.neckBones = string.Join(", ", neckCount.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} x{kv.Value}"));
            return res;
        }

        static bool RayHit(Vector3 o, Vector3 d, Tri tri, out float t, out bool backFacing)
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
            backFacing = Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f;
            return true;
        }

        /// <summary>The row check td asked for: the row's stance is the stance the player is in, and the applied offset is the table value for that stance and pitch.</summary>
        public static string CheckRow(Stance expected, Stance actual, float applied, float table)
        {
            if (expected != actual) return $"the player is {actual}, the row says {expected}";
            if (Mathf.Abs(applied - table) > 0.003f) return $"applied offset {applied:F3} m is not the {expected} table value {table:F3} m";
            return null;
        }

        [Test]
        public void CheckRow_FailsForAWrongStance_AndAWrongOffset()
        {
            Assert.IsNotNull(CheckRow(Stance.Crouch, Stance.Stand, 0.05f, 0.0f), "a Stand pose labelled Crouch must fail");
            Assert.IsNotNull(CheckRow(Stance.Crouch, Stance.Crouch, 0.05f, 0.0f), "the Stand offset on a Crouch row must fail");
            Assert.IsNull(CheckRow(Stance.Crouch, Stance.Crouch, 0.05f, 0.0504f));
        }

        /// <summary>Explicit, expected to go RED: expects Crouch while the player is Stand, proving the live check can fail.</summary>
        [UnityTest, Explicit("Forced-wrong-stance case: expected to FAIL, proving the stance assert can fail"), Timeout(600000)]
        public IEnumerator WrongStance_MustTurnRed()
        {
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            yield return new WaitForSeconds(0.5f);
            Assert.IsNull(CheckRow(Stance.Crouch, player.Stance, view.FirstPerson.LastLookDownOffset.x, 0f), "the player is Stand but the row claims Crouch (this assert is meant to fail)");
        }

        static string Row(Stance stance, float pitch, string label, Result r, string extra = ",,", string tail = "") =>
            $"{stance},{pitch:F0},{label},{r.neckShoulder},{r.backFaces},{r.torsoBottomBand},{r.torsoRays},{(r.lowestTorsoNy > 1.5f ? "none" : r.lowestTorsoNy.ToString("F2"))},{r.legs},{r.nearSlab},{r.Passes}{extra},\"{r.neckBones}\"{tail},{BarOneRule.CsvValues(r.bar1)}";

        /// <summary>
        /// PRINT ONLY (pod 49, td-49 PlayMode-5 triage): the look-down back-offset sweep at 0.01 m steps for the Survivor at the base back offset, Stand +55..+85 and Crouch +65..+85.
        /// Writes lookdown-fine.csv and prints, per stance and pitch, the windows of the forced actual back offset that meet bar 1 and bars 1+2. Asserts nothing.
        /// </summary>
        [UnityTest, Explicit("print only: fine look-down sweep at 0.01 m"), Timeout(3000000)]
        public IEnumerator FineSweep_Print()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var csv = new StringBuilder("stance,pitch,back,neckShoulder,backFaces,torsoBottomBand,torsoRays,lowestTorsoRow,legs,nearSlab,passes,grounded,speed,floorMargin,neckBones,settled,handRays," + BarOneRule.CsvColumns + "\n");
            var summary = new StringBuilder(MirrorTie.StateLine + System.Environment.NewLine);
            var tuning = player.LookDownTuning;
            foreach (var stance in new[] { Stance.Stand, Stance.Crouch })
            {
                if (stance == Stance.Crouch)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    player.WalkSpeedCap = 0f;
                    Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z), 0f);
                    yield return new WaitForSeconds(1.2f);
                    yield return Go(StanceKey.Crouch, 4f);
                }
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}");
                yield return new WaitForSeconds(1.5f);
                var pitches = stance == Stance.Stand ? new[] { 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f } : new[] { 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f };
                foreach (float pitch in pitches)
                {
                    player.SetLook(player.Yaw, pitch);
                    float lo1 = float.NaN, hi1 = float.NaN, lo2 = float.NaN, hi2 = float.NaN, loH = float.NaN, hiH = float.NaN, loA = float.NaN, hiA = float.NaN;
                    for (int i = -12; i <= 30; i++)
                    {
                        float back = i / 100f;
                        tuning.debugBackOverride = back;
                        yield return null;
                        yield return null;
                        var r = Measure(player.ViewCamera, Bake(fp));
                        csv.AppendLine(Row(stance, pitch, back.ToString("F2"), r, ",,", "," + r.handRays));
                        bool bar1 = r.neckShoulder == 0 && r.bar1.Defects == 0 && r.nearSlab == 0;
                        if (bar1) { if (float.IsNaN(lo1)) lo1 = back; hi1 = back; }
                        if (bar1 && r.torsoBottomBand > 0 && r.legs > 0) { if (float.IsNaN(lo2)) lo2 = back; hi2 = back; }
                        if (bar1 && r.handRays > 0) { if (float.IsNaN(loH)) loH = back; hiH = back; }
                        if (bar1 && r.handRays > 0 && r.torsoBottomBand > 0 && r.legs > 0) { if (float.IsNaN(loA)) loA = back; hiA = back; }
                    }
                    summary.AppendLine($"{stance} +{pitch:F0}: bar 1 met from {(float.IsNaN(lo1) ? "never" : lo1.ToString("F2"))} (up to {(float.IsNaN(hi1) ? "-" : hi1.ToString("F2"))}); bars 1+2 met {(float.IsNaN(lo2) ? "NEVER" : lo2.ToString("F2") + " to " + hi2.ToString("F2"))}; bar 1 + hands in frame (handRays > 0) {(float.IsNaN(loH) ? "NEVER" : loH.ToString("F2") + " to " + hiH.ToString("F2"))}; bar 1 + hands + bar 2 {(float.IsNaN(loA) ? "NEVER" : loA.ToString("F2") + " to " + hiA.ToString("F2"))}");
                }
            }
            tuning.debugBackOverride = float.NaN;
            File.WriteAllText($"{OutDir}/lookdown-fine.csv", csv.ToString());
            File.WriteAllText($"{OutDir}/lookdown-fine-windows.txt", summary.ToString());
            Debug.Log("LookDownTests fine sweep tie-resolved hits: " + MirrorTie.Resolved + " (near ties not mirrored: " + MirrorTie.NearTiesNotMirrored + ")");
            Debug.Log("LookDownTests fine sweep (forced actual view-body back offset on top of the base):\n" + summary);
        }

        /// <summary>
        /// PRINT ONLY (pod 49, td-49 hands finding): the hand-ray windows while WALKING. Holds Walk 1.15 and Walk 1.5, and at cycle points 0..7 of the gait and pitches +45..+85 forces the actual
        /// view-body back offset lb over a coarse set; per (speed, pitch, lb) prints how many of the 8 cycle points meet bar 1 and in how many hand rays are in frame. Writes lookdown-walk-hands.csv
        /// and lookdown-walk-hands-windows.txt. Asserts nothing.
        /// </summary>
        [UnityTest, Explicit("print only: walking hand-ray windows"), Timeout(3000000)]
        public IEnumerator FineSweep_Walk_Print()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var csv = new StringBuilder("speed,pitch,lb,cyclePoint,neckShoulder,backFaces,nearSlab,torsoBottomBand,legs,handRays," + BarOneRule.CsvColumns + "\n");
            var tuning = player.LookDownTuning;
            var lbs = new[] { -0.06f, -0.03f, 0f, 0.03f, 0.06f, 0.10f, 0.15f, 0.20f, 0.25f };
            var summary = new StringBuilder(MirrorTie.StateLine + System.Environment.NewLine + "speed,pitch: lb -> cycle points (of 8) with bar 1 / with hands / with bar 1 + hands\n");
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            foreach (float speed in new[] { 1.15f, 1.5f })
            {
                var table = new Dictionary<(float pitch, float lb), (int b1, int hands, int both)>();
                Place(floorCentre - Vector3.forward * 25f, 0f);
                yield return new WaitForSeconds(0.3f);
                player.ExternalMoveInput = Vector2.up * (speed / player.WalkSpeed);
                yield return new WaitForSeconds(0.9f);
                Assert.AreEqual(Stance.Stand, player.Stance, "the player is not standing");
                for (int k = 0; k < 8; k++)
                {
                    Place(floorCentre - Vector3.forward * 25f, 0f);
                    yield return new WaitForSeconds(0.1f + 0.09f * k);
                    foreach (float pitch in new[] { 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f })
                    {
                        player.SetLook(player.Yaw, pitch);
                        foreach (float lb in lbs)
                        {
                            tuning.debugBackOverride = lb;
                            Place(floorCentre - Vector3.forward * 25f, 0f);
                            yield return null;
                            yield return null;
                            var r = Measure(player.ViewCamera, Bake(fp));
                            bool b1 = r.neckShoulder == 0 && r.bar1.Defects == 0 && r.nearSlab == 0, h = r.handRays > 0;
                            table.TryGetValue((pitch, lb), out var cur);
                            table[(pitch, lb)] = (cur.b1 + (b1 ? 1 : 0), cur.hands + (h ? 1 : 0), cur.both + (b1 && h ? 1 : 0));
                            csv.AppendLine($"{speed:F2},{pitch:F0},{lb:F2},{k},{r.neckShoulder},{r.backFaces},{r.nearSlab},{r.torsoBottomBand},{r.legs},{r.handRays},{BarOneRule.CsvValues(r.bar1)}");
                        }
                    }
                }
                foreach (float pitch in new[] { 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f })
                {
                    var line = new StringBuilder($"Walk {speed:F2}, +{pitch:F0}:");
                    foreach (float lb in lbs) { var t = table[(pitch, lb)]; line.Append($"  {lb:+0.00;-0.00;0.00} -> {t.b1}/{t.hands}/{t.both}"); }
                    summary.AppendLine(line.ToString());
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                player.ExternalMoveInput = null;
                yield return new WaitForSeconds(0.6f);
            }
            tuning.debugBackOverride = float.NaN;
            File.WriteAllText($"{OutDir}/lookdown-walk-hands.csv", csv.ToString());
            File.WriteAllText($"{OutDir}/lookdown-walk-hands-windows.txt", summary.ToString());
            Debug.Log("LookDownTests walking hand-ray windows:\n" + summary);
        }

        /// <summary>
        /// PRINT ONLY (td-49 pre-approved, pod 49 table margins): the finer WALKING sweep. Walk 1.15 and 1.5, 8 cycle points; per pitch its own lb range at 0.01 m steps: +60 -0.05..0.03, +65 -0.02..0.12, +70 0.00..0.12,
        /// +75 0.04..0.16, +80 0.08..0.24, +85 0.08..0.26. Per (speed, pitch, lb): cycle points (of 8) with bar 1 / hands / both. Writes lookdown-walkfine.csv and lookdown-walkfine-windows.txt. Asserts nothing.
        /// </summary>
        [UnityTest, Explicit("print only: fine walking sweep"), Timeout(3000000)]
        public IEnumerator FineSweep_WalkFine_Print()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var csv = new StringBuilder("speed,pitch,lb,cyclePoint,neckShoulder,backFaces,nearSlab,torsoBottomBand,legs,handRays," + BarOneRule.CsvColumns + System.Environment.NewLine);
            var tuning = player.LookDownTuning;
            var ranges = new (float pitch, int from, int to)[] { (60f, -5, 3), (65f, -2, 12), (70f, 0, 12), (75f, 4, 16), (80f, 8, 24), (85f, 8, 26) };
            var summary = new StringBuilder(MirrorTie.StateLine + System.Environment.NewLine + "speed,pitch: lb -> cycle points (of 8) with bar 1 / with hands / with both" + System.Environment.NewLine);
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            foreach (float speed in new[] { 1.15f, 1.5f })
            {
                var table = new Dictionary<(float pitch, int lb), (int b1, int hands, int both)>();
                Place(floorCentre - Vector3.forward * 25f, 0f);
                yield return new WaitForSeconds(0.3f);
                player.ExternalMoveInput = Vector2.up * (speed / player.WalkSpeed);
                yield return new WaitForSeconds(0.9f);
                Assert.AreEqual(Stance.Stand, player.Stance, "the player is not standing");
                for (int k = 0; k < 8; k++)
                {
                    Place(floorCentre - Vector3.forward * 25f, 0f);
                    yield return new WaitForSeconds(0.1f + 0.09f * k);
                    foreach (var rg in ranges)
                    {
                        player.SetLook(player.Yaw, rg.pitch);
                        for (int i = rg.from; i <= rg.to; i++)
                        {
                            tuning.debugBackOverride = i / 100f;
                            Place(floorCentre - Vector3.forward * 25f, 0f);
                            yield return null;
                            yield return null;
                            var r = Measure(player.ViewCamera, Bake(fp));
                            bool b1 = r.neckShoulder == 0 && r.bar1.Defects == 0 && r.nearSlab == 0, h = r.handRays > 0;
                            table.TryGetValue((rg.pitch, i), out var cur);
                            table[(rg.pitch, i)] = (cur.b1 + (b1 ? 1 : 0), cur.hands + (h ? 1 : 0), cur.both + (b1 && h ? 1 : 0));
                            csv.AppendLine($"{speed:F2},{rg.pitch:F0},{i / 100f:F2},{k},{r.neckShoulder},{r.backFaces},{r.nearSlab},{r.torsoBottomBand},{r.legs},{r.handRays},{BarOneRule.CsvValues(r.bar1)}");
                        }
                    }
                }
                foreach (var rg in ranges)
                {
                    var line = new StringBuilder($"Walk {speed:F2}, +{rg.pitch:F0}:");
                    for (int i = rg.from; i <= rg.to; i++) { var t = table[(rg.pitch, i)]; line.Append($" {i / 100f:+0.00;-0.00;0.00}={t.b1}/{t.hands}/{t.both}"); }
                    summary.AppendLine(line.ToString());
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                player.ExternalMoveInput = null;
                yield return new WaitForSeconds(0.6f);
            }
            tuning.debugBackOverride = float.NaN;
            File.WriteAllText($"{OutDir}/lookdown-walkfine.csv", csv.ToString());
            File.WriteAllText($"{OutDir}/lookdown-walkfine-windows.txt", summary.ToString());
            Debug.Log("LookDownTests fine walking sweep:" + System.Environment.NewLine + summary + "tie-resolved hits: " + MirrorTie.Resolved + " (near ties not mirrored: " + MirrorTie.NearTiesNotMirrored + ")");
        }

        /// <summary>
        /// PRINT ONLY (td-49 pre-approved, pod 49 table margins): the idle static sweep at THREE breathing phases (passes start after 1.5, then 2.7 and 2.7 s more of waiting; the Animator state's normalized time at the
        /// start of each pass is printed). Stand +60..+85 with lb -0.06..0.26 and Crouch +60..+85 with lb -0.12..0.12 at 0.01 m steps. Per pass and pitch the windows for bar 1, bar 1 + hands, bars 1+2 and all three,
        /// and the intersection over the passes. Writes lookdown-phases.csv and lookdown-phases-windows.txt. Asserts nothing.
        /// </summary>
        [UnityTest, Explicit("print only: idle sweep at three breathing phases"), Timeout(3000000)]
        public IEnumerator FineSweep_IdlePhases_Print()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var csv = new StringBuilder("stance,phase,pitch,lb,neckShoulder,backFaces,nearSlab,torsoBottomBand,legs,handRays," + BarOneRule.CsvColumns + System.Environment.NewLine);
            var summary = new StringBuilder(MirrorTie.StateLine + System.Environment.NewLine);
            var tuning = player.LookDownTuning;
            string[] names = { "bar1", "bar1+hands", "bars1+2", "all3" };
            var passes = new Dictionary<string, List<(float lo, float hi)>>();
            foreach (var stance in new[] { Stance.Stand, Stance.Crouch })
            {
                if (stance == Stance.Crouch)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    player.WalkSpeedCap = 0f;
                    Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z), 0f);
                    yield return new WaitForSeconds(1.2f);
                    yield return Go(StanceKey.Crouch, 4f);
                }
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}");
                var pitches = stance == Stance.Stand ? new[] { 60f, 65f, 70f, 75f, 80f, 85f } : new[] { 60f, 65f, 70f, 75f, 80f, 85f };
                int from = stance == Stance.Stand ? -6 : -12, to = stance == Stance.Stand ? 26 : 12;
                for (int ph = 0; ph < 3; ph++)
                {
                    yield return new WaitForSeconds(ph == 0 ? 1.5f : 2.7f);
                    var st = view.Body.Animator.GetCurrentAnimatorStateInfo(0);
                    summary.AppendLine($"# {stance} pass {ph}: animator state normalized time {st.normalizedTime % 1f:F2} at the start");
                    foreach (float pitch in pitches)
                    {
                        player.SetLook(player.Yaw, pitch);
                        float[] lo = { float.NaN, float.NaN, float.NaN, float.NaN }, hi = { float.NaN, float.NaN, float.NaN, float.NaN };
                        for (int i = from; i <= to; i++)
                        {
                            tuning.debugBackOverride = i / 100f;
                            yield return null;
                            yield return null;
                            var r = Measure(player.ViewCamera, Bake(fp));
                            csv.AppendLine($"{stance},{ph},{pitch:F0},{i / 100f:F2},{r.neckShoulder},{r.backFaces},{r.nearSlab},{r.torsoBottomBand},{r.legs},{r.handRays},{BarOneRule.CsvValues(r.bar1)}");
                            bool b1 = r.neckShoulder == 0 && r.bar1.Defects == 0 && r.nearSlab == 0, h = r.handRays > 0, b2 = r.torsoBottomBand > 0 && r.legs > 0;
                            bool[] ok = { b1, b1 && h, b1 && b2, b1 && h && b2 };
                            for (int k = 0; k < 4; k++) if (ok[k]) { if (float.IsNaN(lo[k])) lo[k] = i / 100f; hi[k] = i / 100f; }
                        }
                        var line = new StringBuilder($"{stance} +{pitch:F0} pass {ph}:");
                        for (int k = 0; k < 4; k++)
                        {
                            line.Append($" {names[k]} {(float.IsNaN(lo[k]) ? "NEVER" : lo[k].ToString("F2") + ".." + hi[k].ToString("F2"))};");
                            string key = $"{stance} +{pitch:F0} {names[k]}";
                            if (!passes.TryGetValue(key, out var l)) passes[key] = l = new List<(float, float)>();
                            l.Add((lo[k], hi[k]));
                        }
                        summary.AppendLine(line.ToString());
                    }
                }
            }
            summary.AppendLine("# INTERSECTION over the three passes (largest lower bound .. smallest upper bound):");
            foreach (var kv in passes)
            {
                float lo = float.NegativeInfinity, hi = float.PositiveInfinity; bool never = false;
                foreach (var (a, b) in kv.Value) { if (float.IsNaN(a)) never = true; else { lo = Mathf.Max(lo, a); hi = Mathf.Min(hi, b); } }
                summary.AppendLine($"{kv.Key}: {(never || lo > hi ? "NEVER in all three" : lo.ToString("F2") + ".." + hi.ToString("F2"))}");
            }
            tuning.debugBackOverride = float.NaN;
            File.WriteAllText($"{OutDir}/lookdown-phases.csv", csv.ToString());
            File.WriteAllText($"{OutDir}/lookdown-phases-windows.txt", summary.ToString());
            Debug.Log("LookDownTests idle phases:" + System.Environment.NewLine + summary + "tie-resolved hits: " + MirrorTie.Resolved + " (near ties not mirrored: " + MirrorTie.NearTiesNotMirrored + ")");
        }

        /// <summary>
        /// THE TABLE GATE (td-49 "TABLE GATE LookDownTests", gate shape and director-49 W-49-1, note6): the CONFIGURED look-down tables of the Survivor (debugBackOverride = NaN), pitches taken from the table points >= +45.
        /// Stand idle, then the Stand gaits Walk 1.15, Walk 1.5, Jog 5, Sprint 8, then Crouch idle and CrouchWalk 1.75. Idle rows are measured at three breathing phases (the waits of FineSweep_IdlePhases_Print; the animator
        /// normalized time is a CSV column); each gait at 8 cycle points with a per-point proof (controller speed within 5 percent of its target, stance, dominant clip: GaitSample and Settled as in Gait()). ASSERTED:
        /// Bar 1 everywhere (no waiver exists); Bar 2 for Stand idle >= +60 and Crouch idle >= +80 except the waivers W-49-1 (idle Bar 2 at Stand +60) and W-49-2 (idle Bar 2 at Stand +65), Bar2IdleWaivers; hands for Stand idle >= +75 and Crouch idle >= +70 (director note6, gate/pod49-hands-in-lookdown-note6-11b-relaxations) at
        /// every phase; walk hands in >= 3 of 8 cycle points at EVERY pitch from +45 to +85, both speeds (WH-49-2; supersedes the 6 of 8 from +60), except Walk1.5 at +45 only: >= 2 of 8 (WH-49-3, director note6 with td's pick ruling "WALK1.5 +45 PICK": no +45 offset qualified, +45 stays -0.03). Jog, Sprint and CrouchWalk: this gate asserts Bar 1 only and prints their hand counts. Bar 2 scope of record (td B36): amend5b, with the torso clause ONLY waived in W-49-1/W-49-2 (Stand idle +60/+65), amend8g (1) carried over to the Survivor (Sprint8 Stand +60..+85, Jog5 Stand +60/+65) and W-49-4 (Walk1.15/Walk1.5 Stand +60/+65/+70, Jog5 Stand +70/+75/+80); the legs clause is waived nowhere. Gait Bar 2 is proven by StandAndCrouch_LookDown_BackOffsetSweepAndConfiguredTables (Bar2/Bar2Waiver), not by this gate.
        /// Failures are collected and asserted together; lookdown-gate.csv has every row, lookdown-gate-hands.txt the walk fractions; the first line of each names the MirrorTie rule state.
        /// </summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator ConfiguredTables_Gate_ThreeIdlePhasesAndGaits()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var tuning = player.LookDownTuning;
            tuning.debugBackOverride = float.NaN;
            bf = LookDownBfAttrib.Enabled ? new LookDownBfAttrib() : null;
            var csv = new StringBuilder(MirrorTie.StateLine + "; " + LookDownBfAttrib.StateLine + System.Environment.NewLine + "gait,phaseOrCyclePoint,stance,pitch,tableLb,neckShoulder,backFaces,nearSlab,torsoBottomBand,legs,handRays,animNormalizedTime,gaitDiag," + BarOneRule.CsvColumns + System.Environment.NewLine);
            var failures = new List<string>();
            var handsText = new StringBuilder(MirrorTie.StateLine + "; " + LookDownBfAttrib.StateLine + System.Environment.NewLine);
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            foreach (var stance in new[] { Stance.Stand, Stance.Crouch })
            {
                if (stance == Stance.Crouch)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    player.ExternalMoveInput = null;
                    player.WalkSpeedCap = 0f;
                    Place(floorCentre, 0f);
                    yield return new WaitForSeconds(1.2f);
                    yield return Go(StanceKey.Crouch, 4f);
                }
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}");
                var pitches = tuning.Of(stance).Select(p => p.x).Where(x => x >= 45f).ToArray();
                for (int ph = 0; ph < 3; ph++)
                {
                    yield return new WaitForSeconds(ph == 0 ? 1.5f : 2.7f);
                    float animT = view.Body.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
                    foreach (float pitch in pitches)
                    {
                        player.SetLook(player.Yaw, pitch);
                        yield return null;
                        yield return null;
                        var r = Measure(player.ViewCamera, Bake(fp));
                        csv.AppendLine($"idle,{ph},{stance},{pitch:F0},{fp.LastLookDownOffset.x:F3},{r.neckShoulder},{r.backFaces},{r.nearSlab},{r.torsoBottomBand},{r.legs},{r.handRays},{animT:F2},,{BarOneRule.CsvValues(r.bar1)}");
                        failures.AddRange(RowFailures(stance, $"{stance} idle phase {ph} (anim {animT:F2}) +{pitch:F0} (table {fp.LastLookDownOffset.x:F3})", pitch, r));
                        string badIdle = CheckRow(stance, player.Stance, fp.LastLookDownOffset.x, LookDownOffset.Back(tuning, stance, pitch));   // the applied offset must be the configured table's, not a stuck override
                        if (badIdle != null) failures.Add($"{stance} idle phase {ph} +{pitch:F0} CONFIGURED: {badIdle}");
                    }
                }
                var gaits = stance == Stance.Stand
                    ? new (string label, float target, float cap, Key[] keys)[] { ("Walk1.15", 1.15f, 1.15f, new Key[0]), ("Walk1.5", 1.5f, 1.5f, new Key[0]), ("Jog5", 5f, 0f, new[] { Key.W }), ("Sprint8", 8f, 0f, new[] { Key.W, Key.LeftShift }) }
                    : new (string label, float target, float cap, Key[] keys)[] { ("CrouchWalk1.75", 1.75f, 0f, new[] { Key.W }) };
                foreach (var g in gaits)
                {
                    var handsPoints = new Dictionary<float, int>();
                    yield return GateGait(g.label, stance, g.target, g.cap, fp, csv, failures, handsPoints, pitches, g.keys);
                    foreach (float pitch in pitches)
                    {
                        int n = handsPoints.TryGetValue(pitch, out int h) ? h : 0;
                        bool walk = g.label.StartsWith("Walk");
                        bool wh3 = g.label == "Walk1.5" && pitch == 45f;   // WH-49-3 (note6): Walk1.5 at +45 only
                        int need = wh3 ? 2 : 3;   // WH-49-2 (note6): >= 3 of 8 cycle points at every pitch, both walk speeds
                        handsText.AppendLine($"{g.label} +{pitch:F0}: hand rays in {n} of 8 cycle points{(walk ? $" (required >= {need})" : " (printed only)")}");
                        if (walk && n < need) failures.Add(wh3
                            ? $"{g.label} +{pitch:F0} WALK HANDS (WH-49-3: Walk1.5 +45 only, hands >= 2/8): {n} of 8"
                            : $"{g.label} +{pitch:F0} WALK HANDS (WH-49-2: walk hands >= 3/8 at every pitch +45..+85, both speeds): {n} of 8");
                    }
                }
                if (stance == Stance.Stand && bf != null) yield return Walk45Try(tuning, fp);   // note6: print-only, no table change
            }
            if (bf != null) bf.Write(OutDir);   // G1: lookdown-bf-attrib.csv and the classifier inputs in lookdown\\stills
            File.WriteAllText($"{OutDir}/lookdown-gate.csv", csv.ToString());
            File.WriteAllText($"{OutDir}/lookdown-gate-hands.txt", handsText.ToString());
            Debug.Log("LookDownTests table gate:" + System.Environment.NewLine + handsText + "tie-resolved hits: " + MirrorTie.Resolved);
            Assert.IsEmpty(failures, string.Join(System.Environment.NewLine, failures));
        }

        /// <summary>
        /// PRINT ONLY (director note6, gate/pod49-hands-in-lookdown-note6-11b-relaxations, riding the G1 switch): Walk 1.15 and Walk 1.5 at +45 with the view offset forced to lb -0.06 .. -0.12 (step 0.01), 8 cycle points each, as in the gate.
        /// One line per speed and lb in lookdown-walk45-try.txt: the number of BAR 1 rows, the cycle points with a hand ray, and the cycle points that are Bar 1 clean with a hand ray (td A5; the pick rule is the director's and td's). Failures of these rows are NOT added to the gate; the table does not change here.
        /// </summary>
        IEnumerator Walk45Try(LookDownTuning tuning, FirstPersonBody fp)
        {
            var text = new StringBuilder(MirrorTie.StateLine + "; " + LookDownBfAttrib.StateLine + System.Environment.NewLine + "gait,lb,bar1RowsOf8,handPointsOf8,bothPointsOf8" + System.Environment.NewLine);
            var keep = bf;
            bf = null;   // the forced rows are not gate rows: no attribution
            foreach (var (label, speed) in new[] { ("Walk1.15", 1.15f), ("Walk1.5", 1.5f) })
                for (int i = 6; i <= 12; i++)
                {
                    float lb = -i / 100f;
                    tuning.debugBackOverride = lb;
                    var f = new List<string>();
                    var hp = new Dictionary<float, int>();
                    var rows = new StringBuilder();
                    yield return GateGait(label, Stance.Stand, speed, speed, fp, rows, f, hp, new[] { 45f });
                    int both = 0;   // cycle points that are Bar 1 clean AND show a hand ray (csv columns: neckShoulder 5, nearSlab 7, handRays 10, bar1Defects 6th from the end)
                    foreach (var line in rows.ToString().Split('\n'))
                    {
                        if (line.StartsWith("#") || !line.StartsWith(label + ",")) continue;
                        var c = line.Split(',');
                        // columns: neckShoulder 5, nearSlab 7, handRays 10 from the front; bar1Defects is the 6th column from the END (the quoted gaitDiag before it can contain commas); backFaces (6) is print only under R3
                        if (c.Length > 16 && c[5] == "0" && c[c.Length - 6] == "0" && c[7] == "0" && c[10] != "0") both++;
                    }
                    text.AppendLine($"{label},{lb:F2},{f.Count(x => x.Contains("BAR 1"))},{(hp.TryGetValue(45f, out int h) ? h : 0)},{both}");
                }
            tuning.debugBackOverride = float.NaN;
            bf = keep;
            File.WriteAllText($"{OutDir}/lookdown-walk45-try.txt", text.ToString());
        }

        /// <summary>Named waivers of the idle Bar 2 clause, as (stance, pitch), each with its citation in the director's record. W-49-1: idle Bar 2 at Stand +60 only (studio:decisions gate/pod49-lookdown-waiver-w49-1-and-walk-fraction). W-49-2: idle Bar 2 at Stand +65 only (gate/pod49-lookdown-waiver-w49-2). Bar 1 is waived nowhere.</summary>
        static readonly HashSet<(Stance, float)> Bar2IdleWaivers = new HashSet<(Stance, float)> { (Stance.Stand, 60f), (Stance.Stand, 65f) };

        /// <summary>The per-row idle check of the gate, factored so the forced-failure test runs the SAME code: Bar 1 always, Bar 2 where it applies and is not waived, hands where required.</summary>
        static List<string> RowFailures(Stance stance, string where, float pitch, Result r, bool forceHandsCheck = false)
        {
            var f = new List<string>();
            if (r.neckShoulder > 0 || r.bar1.Defects > 0 || r.nearSlab > 0) f.Add($"{where} BAR 1 (render-faithful, {BarOneRule.GText}): {r}");
            // director-49 condition on retiring the amend4 crouch constants (gate/pod49-crouch-neck-check-condition): Crouch idle +65 and +70 must have no neck/collar ray. Named, separate from the Bar 1 total.
            if (stance == Stance.Crouch && (pitch == 65f || pitch == 70f) && r.neckShoulder > 0) f.Add($"{where} CROUCH NECK (amend4 rule, Survivor): {r.neckShoulder} neck/collar rays");
            bool bar2 = stance == Stance.Stand ? pitch >= 60f : pitch >= 80f;
            if (bar2 && !Bar2IdleWaivers.Contains((stance, pitch)) && !(r.torsoBottomBand > 0 && r.legs > 0)) f.Add($"{where} BAR 2 (idle Bar 2 applies at Stand >= +60 and Crouch >= +80; waived only by W-49-1: idle Bar 2 waived at Stand +60 only, and W-49-2: idle Bar 2 waived at Stand +65 only; Bar 1 is waived nowhere): {r}");
            bool hands = forceHandsCheck || (stance == Stance.Stand ? pitch >= 75f : pitch >= 70f);   // standing hands from +75 (gate/pod49-hands-in-lookdown-note3-stand75 and note4-margin-reading), crouch from +70 (note6: the +65 window is 1 cm)
            if (hands && r.handRays == 0) f.Add($"{where} " + (stance == Stance.Stand ? "HANDS (stand idle from +75: note3/note4)" : "HANDS (crouch idle from +70: note6)") + ": no hand ray in frame");
            return f;
        }

        /// <summary>One gait of the gate: 8 cycle points, every pitch; Bar 1 per point (asserted by the caller through failures), the per-point gait proof (speed, stance, dominant clip) and the hand count.</summary>
        IEnumerator GateGait(string label, Stance expected, float target, float cap, FirstPersonBody fp, StringBuilder csv, List<string> failures, Dictionary<float, int> handsPoints, float[] pitches, params Key[] keys)
        {
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            Place(floorCentre - Vector3.forward * 25f, 0f);
            yield return new WaitForSeconds(0.3f);
            if (cap > 0f) player.ExternalMoveInput = Vector2.up * (cap / player.WalkSpeed);
            else InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return new WaitForSeconds(0.9f);
            Assert.AreEqual(expected, player.Stance, $"{label}: the player is {player.Stance}, the rows would carry the wrong stance");
            string token = SweepToken(label);
            int advSamples = 0, advSkips = 0;
            for (int k = 0; k < 8; k++)
            {
                Place(floorCentre - Vector3.forward * 25f, 0f);
                yield return new WaitForSeconds(0.1f + 0.11f * k);
                foreach (float pitch in pitches)
                {
                    player.SetLook(player.Yaw, pitch);
                    Place(floorCentre - Vector3.forward * 25f, 0f);
                    yield return null;
                    yield return null;
                    string advGait = null;
                    yield return AdvancingCheck(a => advGait = a);   // as Gait(): the animator really advances; a skipped check is counted and printed
                    advSamples++;
                    if (advGait == Skipped) advSkips++;
                    else if (advGait != null) failures.Add($"{label} cycle point {k} at +{pitch:F0}: {advGait}");
                    var r = Measure(player.ViewCamera, Bake(fp));
                    if (bf != null && r.backFaces > 0)   // G1, print only: the same frame as the Measure above
                        bf.Add(label, k, pitch, fp.ViewRenderers, player.ViewCamera, player.transform.position.y, GridX, GridY,
                            view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator : view.Body.Animator, view.Body.Animator);
                    string where = $"{label} cycle point {k} +{pitch:F0} (table {fp.LastLookDownOffset.x:F3})";
                    string badCycle = CheckRow(expected, player.Stance, fp.LastLookDownOffset.x, LookDownOffset.Back(player.LookDownTuning, expected, pitch));
                    if (badCycle != null) failures.Add($"{where} CONFIGURED: {badCycle}");
                    string extra = GaitSample(label, target, floorCentre, failures, $"cycle point {k} at +{pitch:F0}");   // adds the failure when the speed is off target
                    string notSettled = Settled(expected, target, token, out string diag);
                    if (notSettled != null) failures.Add($"{where} gait proof: {notSettled}");
                    float animT = view.Body.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime % 1f;
                    csv.AppendLine($"{label},{k},{expected},{pitch:F0},{fp.LastLookDownOffset.x:F3},{r.neckShoulder},{r.backFaces},{r.nearSlab},{r.torsoBottomBand},{r.legs},{r.handRays},{animT:F2},\"{diag.Replace('\"', '\'')}\",{BarOneRule.CsvValues(r.bar1)}");
                    if (r.neckShoulder > 0 || r.bar1.Defects > 0 || r.nearSlab > 0) failures.Add($"{where} BAR 1 (render-faithful, {BarOneRule.GText}): {r}");
                    if (r.handRays > 0) handsPoints[pitch] = (handsPoints.TryGetValue(pitch, out int n) ? n : 0) + 1;
                }
            }
            csv.AppendLine($"# {label}: advancing checks skipped {advSkips} of {advSamples}");
            if (advSkips * 24 > advSamples * 3) failures.Add($"{label}: {advSkips} of {advSamples} advancing checks were skipped (more than 3 in 24)");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            player.ExternalMoveInput = null;
            yield return new WaitForSeconds(0.6f);
        }

        /// <summary>
        /// FORCED FAILURES (td-49 gate item 7): the gate's own per-row check must turn red. EXPECTED RED. (a) Stand +85 idle with the view body forced to lb 0.05, which the fine sweep of record
        /// (TestResults/player-character/runs/20261103-215346/lookdown/lookdown-fine.csv, rows "Stand,85,0.05,...") lists as failing Bar 1: at least one BAR 1 failure. (b) Stand +45 idle with the hands check forced on
        /// (idle has no hand ray at +45, lookdown-fine-windows.txt line 1 of that run): at least one HANDS failure.
        /// </summary>
        [UnityTest, Explicit("Forced-bad case: expected to FAIL, proving the gate's row check can fail"), Timeout(1200000)]
        public IEnumerator Gate_ForcedBadOffset_MustTurnRed()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            var fp = view.FirstPerson;
            yield return new WaitForSeconds(1.5f);
            var tuning = player.LookDownTuning;
            var failures = new List<string>();
            player.SetLook(player.Yaw, 85f);
            tuning.debugBackOverride = 0.05f;
            yield return null; yield return null;
            var fa = RowFailures(Stance.Stand, "FORCED Stand idle +85 at lb 0.05", 85f, Measure(player.ViewCamera, Bake(fp)));
            Assert.IsTrue(fa.Any(x => x.Contains("BAR 1")), "forced (a): no BAR 1 failure at Stand +85 with lb 0.05");
            failures.AddRange(fa);
            tuning.debugBackOverride = float.NaN;
            player.SetLook(player.Yaw, 45f);
            yield return null; yield return null;
            var fb = RowFailures(Stance.Stand, "FORCED Stand idle +45 hands", 45f, Measure(player.ViewCamera, Bake(fp)), true);
            Assert.IsTrue(fb.Any(x => x.Contains("HANDS")), "forced (b): no HANDS failure at Stand +45 idle");
            failures.AddRange(fb);
            // (d) F8' (td-49 B17): the real path under the CURRENT table, on Sprint8 at the GATE's nine pitches (the same pitch array and so the same cycle-point timing as the gate). Data of record: the baton 14b gate run 1
            // (TestResults/player-character/runs/b14-gate1/lookdown/lookdown-gate.csv, backFaces column) has back-first rays in 9 of 72 Sprint8 rows, with front hits at most 16.3 mm behind (a leg, 18.7 mm under the real G of 35 mm).
            // With the gap override at 0 the pass must print at least one BAR 1 line whose gap>G count is above 0; the CONTROL, the same pass under the real G, must print no gap>G count above 0. The override is reset in a finally.
            // A miss prints the row count, the back-first rows seen in that pass and the failure lines, so it is explained, not guessed. (Walk1.5 has back-first rays in only 3 of 72 rows and CrouchWalk1.75's knee cell has 0.79 mm of margin: neither is used.)
            var gatePitches = tuning.Of(Stance.Stand).Select(pt => pt.x).Where(x => x >= 45f).ToArray();
            var fdOn = new List<string>();
            var fdOff = new List<string>();
            var fdOnCsv = new StringBuilder();
            var fdOffCsv = new StringBuilder();
            try
            {
                BarOneRule.GapOverrideForTest = 0f;
                yield return GateGait("Sprint8", Stance.Stand, 8f, 0f, fp, fdOnCsv, fdOn, new Dictionary<float, int>(), gatePitches, Key.W, Key.LeftShift);
            }
            finally { BarOneRule.GapOverrideForTest = null; }
            string FdReport(StringBuilder csvText, List<string> fl)
            {
                var rows = csvText.ToString().Split('\n').Where(l => l.StartsWith("Sprint8,")).ToList();
                int backFirstRows = rows.Count(l => { var c = l.Split(','); return c.Length > 7 && c[6] != "0"; });
                return $"rows {rows.Count}, back-first rows {backFirstRows}, failure lines {fl.Count}: " + string.Join(" | ", fl.Take(6).Select(x => x.Length > 160 ? x.Substring(0, 160) : x));
            }
            Assert.IsTrue(fdOn.Any(x => x.Contains("BAR 1") && Regex.IsMatch(x, @"gap>G [1-9]")), "forced (d): no BAR 1 failure with a gap>G count above 0 at Sprint8 with G forced to 0; " + FdReport(fdOnCsv, fdOn));
            yield return GateGait("Sprint8", Stance.Stand, 8f, 0f, fp, fdOffCsv, fdOff, new Dictionary<float, int>(), gatePitches, Key.W, Key.LeftShift);
            Assert.IsFalse(fdOff.Any(x => Regex.IsMatch(x, @"gap>G [1-9]")), "forced (d) control: a gap>G failure was printed at Sprint8 under the real G; " + FdReport(fdOffCsv, fdOff));
            Assert.IsFalse(BarOneRule.GapOverrideForTest.HasValue, "forced (d): the G override was not reset");
            failures.AddRange(fdOn);
            // (c) CROUCH NECK: Crouch idle +70 forced to lb -0.10. The Survivor idle-phases sweep of record lists neckShoulder > 0 there in all three phases
            // (TestResults/player-character/runs/b9/lookdown/lookdown-phases.csv, rows "Crouch,0,70,-0.10,33", "Crouch,1,70,-0.10,32", "Crouch,2,70,-0.10,31"; header stance,phase,pitch,lb,neckShoulder,...).
            // Must produce the CROUCH NECK failure. (MIRRORTIE_OFF would not: it moves NECK-CAP back faces, which this clause does not count.)
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            player.ExternalMoveInput = null;
            player.WalkSpeedCap = 0f;
            yield return Go(StanceKey.Crouch, 4f);
            Assert.AreEqual(Stance.Crouch, player.Stance, "forced (c): could not reach Crouch");
            yield return new WaitForSeconds(1.5f);
            player.SetLook(player.Yaw, 70f);
            tuning.debugBackOverride = -0.10f;
            yield return null; yield return null;
            var fc = RowFailures(Stance.Crouch, "FORCED Crouch idle +70 at lb -0.10", 70f, Measure(player.ViewCamera, Bake(fp)));
            Assert.IsTrue(fc.Any(x => x.Contains("CROUCH NECK (amend4 rule, Survivor)")), "forced (c): no CROUCH NECK failure at Crouch +70 with lb -0.10");
            failures.AddRange(fc);
            tuning.debugBackOverride = float.NaN;
            Assert.IsEmpty(failures, "EXPECTED RED: the gate's row check failed the forced rows (this assert is meant to fail): " + string.Join(" | ", failures));
        }

        [UnityTest, Timeout(7200000)]
        public IEnumerator StandAndCrouch_LookDown_BackOffsetSweepAndConfiguredTables()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            Assert.IsTrue(view.FirstPerson.HasViewBody, "no view body: the look-down fix needs the view copy");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var report = new StringBuilder("stance,pitch,back,neckShoulder,backFaces,torsoBottomBand,torsoRays,lowestTorsoRow,legs,nearSlab,passes,grounded,speed,floorMargin,neckBones,settled," + BarOneRule.CsvColumns + "\n");
            var failures = new List<string>();
            var tuning = player.LookDownTuning;
            var pitches = new[] { 40f, 50f, 60f, 65f, 70f, 75f, 80f, 85f };

            foreach (var stance in new[] { Stance.Stand, Stance.Crouch })
            {
                if (stance == Stance.Crouch)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    player.WalkSpeedCap = 0f;
                    Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z), 0f);
                    yield return new WaitForSeconds(1.2f);
                    yield return Go(StanceKey.Crouch, 4f);
                }
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}: {player.EntryBlockedBy(stance)} (grounded {player.IsGrounded})");
                yield return new WaitForSeconds(1.5f);
                foreach (float pitch in pitches)
                {
                    player.SetLook(player.Yaw, pitch);
                    // Sweep: force each ACTUAL back offset (negative is forward).
                    float bestBack = 0f; int bestScore = -1;
                    for (float back = -0.25f; back <= 0.801f; back += 0.05f)
                    {
                        tuning.debugBackOverride = Mathf.Round(back * 100f) / 100f;
                        yield return null;
                        yield return null;
                        var r = Measure(player.ViewCamera, Bake(fp));
                        report.AppendLine(Row(stance, pitch, tuning.debugBackOverride.ToString("F2"), r));
                        int score = r.neckShoulder == 0 && r.bar1.Defects == 0 ? r.torsoBottomBand * 1000 + r.torsoRays : -1;
                        if (score > bestScore) { bestScore = score; bestBack = tuning.debugBackOverride; }
                    }
                    if (pitch == 60f || pitch == 85f)
                    {
                        tuning.debugBackOverride = 0f;
                        yield return null; yield return null;
                        yield return Still($"{OutDir}/{stance}_+{pitch:F0}_f0.00.png");
                        tuning.debugBackOverride = bestBack;
                        yield return null; yield return null;
                        yield return Still($"{OutDir}/{stance}_+{pitch:F0}_best{bestBack:+0.00;-0.00}.png");
                    }
                    // The configured table (the real function of stance and pitch).
                    tuning.debugBackOverride = float.NaN;
                    yield return null;
                    yield return null;
                    var cfg = Measure(player.ViewCamera, Bake(fp));
                    float applied = fp.LastLookDownOffset.x;
                    string bad = CheckRow(stance, player.Stance, applied, LookDownOffset.Back(tuning, stance, pitch));
                    if (bad != null) failures.Add($"CONFIGURED row {stance} +{pitch:F0}: {bad}");
                    report.AppendLine(Row(player.Stance, pitch, $"CONFIGURED {applied:F3}", cfg));
                    // Bar (1) is absolute at every pitch (director amend4). Bar (2) is asserted for Stand idle at +60 and +85 only; elsewhere it is reported (waived or unmeetable).
                    bool required = stance == Stance.Stand && pitch == 85f;   // idle Bar 2 at Stand +60 is waived by W-49-1 (gate/pod49-lookdown-waiver-w49-1-and-walk-fraction); +85 keeps the full bar
                    if (required ? !cfg.Passes : (cfg.neckShoulder > 0 || cfg.bar1.Defects > 0 || cfg.nearSlab > 0))
                        failures.Add($"{stance} at +{pitch:F0} with the configured offset {applied:F3}: {cfg}");
                }

                // Slow pitch pass: the view body must not pop between 0.5 degree pitch steps (director amend4 b).
                tuning.debugBackOverride = float.NaN;
                {
                    float lastZ = float.NaN, worstStep = 0f;
                    for (float pp = -85f; pp <= 85.01f; pp += 0.5f)
                    {
                        player.SetLook(player.Yaw, pp);
                        yield return null;
                        yield return null;
                        float z = fp.ViewRoot.localPosition.z;
                        if (!float.IsNaN(lastZ)) worstStep = Mathf.Max(worstStep, Mathf.Abs(z - lastZ));
                        lastZ = z;
                    }
                    report.AppendLine($"# {stance} slow pitch pass: worst view-body step per 0.5 degree {worstStep:F4} m");
                    if (worstStep > 0.012f) failures.Add($"{stance}: the view body popped {worstStep:F4} m on a slow pitch pass");
                }
                // The gaits that are actually played (td): Walk 1.15 and 1.5 (tower speeds), Jog 5 and Sprint 8 (Stand); CrouchWalk 1.75 (Crouch).
                if (stance == Stance.Stand)
                {
                    yield return Gait("Walk1.15", Stance.Stand, 1.15f, fp, report, failures, 1.15f, Key.W);
                    yield return Gait("Walk1.5", Stance.Stand, 1.5f, fp, report, failures, 1.5f, Key.W);
                    yield return Gait("Jog5", Stance.Stand, 5f, fp, report, failures, 0f, Key.W);
                    yield return Gait("Sprint8", Stance.Stand, 8f, fp, report, failures, 0f, Key.W, Key.LeftShift);
                }
                else yield return Gait("CrouchWalk1.75", Stance.Crouch, 1.75f, fp, report, failures, 0f, Key.W);
            }
            File.WriteAllText($"{OutDir}/lookdown-sweep.csv", report.ToString());
            File.WriteAllText($"{OutDir}/lookdown-gaitmax.csv", gaitMax.ToString());
            FlushAttribution();
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Holds a gait, then bars 1 and 2 at four cycle points at +60 and +85, and the -85 to +85 clipping check (bar 4) on moving frames.</summary>
        IEnumerator Gait(string label, Stance expected, float target, FirstPersonBody fp, StringBuilder report, List<string> failures, float cap, params Key[] keys)
        {
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            Place(floorCentre - Vector3.forward * 25f, 0f);
            yield return new WaitForSeconds(0.3f);
            if (cap > 0f) player.ExternalMoveInput = Vector2.up * (cap / player.WalkSpeed);   // TowerWalkZones rewrites WalkSpeedCap every frame, so a cap cannot hold a slow gait
            else InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return new WaitForSeconds(0.9f);
            var stance = player.Stance;
            Assert.AreEqual(expected, stance, $"{label}: the player is {stance}, the row would carry the wrong stance");
            var maxNeck = new Dictionary<float, (int neck, int phase)>();
            var bar2Rows = new Dictionary<float, int>();   // settled rows that bar 2 was applied to, per pitch
            var waivedRows = new Dictionary<float, (int n, int minLegs, int maxLegs)>();   // waived torso misses per pitch
            var bar2Failed = new Dictionary<float, int>();   // settled rows that FAILED bar 2, per pitch (so the summary line never says "met" over a failure)
            int advSamples = 0, advSkips = 0;
            for (int k = 0; k < 24; k++)   // 24 cycle points: a single instant can miss the phase where a shoulder rises into the frame (qa-2 found 25 neck rays at Sprint +55 that a 4-point sample missed)
            {
                // Back to the start of the floor for every cycle point: the measuring frames are long (frame time is clamped to 0.33 s), so a sprinter covers 25 m or more per point and would run off the floor into the airborne pose.
                Place(floorCentre - Vector3.forward * 25f, 0f);
                yield return new WaitForSeconds(0.1f + 0.037f * k);
                foreach (float pitch in new[] { 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f })
                {
                    player.SetLook(player.Yaw, pitch);
                    Place(floorCentre - Vector3.forward * 25f, 0f);   // the measuring frames are long; stay on the floor in the gait
                    yield return null;
                    yield return null;
                    string advGait = null;
                    yield return AdvancingCheck(r => advGait = r);
                    advSamples++;
                    if (advGait == Skipped) advSkips++;
                    else if (advGait != null) failures.Add($"{label} cycle point {k} at +{pitch:F0}: {advGait}");
                    var w = Measure(player.ViewCamera, Bake(fp));
                    LogHits("main-gait", $"{label}-cycle{k} +{pitch:F0}", w);
                    string cx = GaitSample(label, target, floorCentre, failures, $"cycle point {k} at +{pitch:F0}");
                    string token = SweepToken(label);
                    string notSettled = Settled(expected, target, token, out string settledDiag);
                    if (notSettled != null) failures.Add($"{label} cycle point {k} at +{pitch:F0} does not count: {notSettled}");
                    if (expected == Stance.Crouch && pitch == 75f)
                    {
                        var st = view.Body.Animator.GetCurrentAnimatorStateInfo(0);
                        report.AppendLine($"# DIAG {label}-cycle{k} stance {player.Stance} from {player.StanceFrom} blend {player.StanceBlend:F2} transitioning {player.StanceTransitioning}, eye {player.ViewCamera.transform.position.y - player.transform.position.y:F3}, pitch {player.Pitch:F1}, animator state hash {st.fullPathHash} t {st.normalizedTime:F2} speed {st.speed * st.speedMultiplier:F2}, LocoRate {view.Body.Animator.GetFloat("LocoRate"):F2}, controller speed {new Vector2(body.velocity.x, body.velocity.z).magnitude:F2}, view root {fp.ViewRoot.localPosition:F3}, shadow root {fp.ShadowRoot.localPosition:F3}");
                    }
                    report.AppendLine(Row(stance, pitch, $"{label}-cycle{k} {fp.LastLookDownOffset.x:F3}", w, cx, $",\"{(notSettled == null ? "yes" : "NO")} {settledDiag}\""));
                    if (!maxNeck.TryGetValue(pitch, out var mx) || w.neckShoulder > mx.neck) maxNeck[pitch] = (w.neckShoulder, k);
                    string badCycle = CheckRow(expected, player.Stance, fp.LastLookDownOffset.x, LookDownOffset.Back(player.LookDownTuning, expected, pitch));
                    if (badCycle != null) failures.Add($"{label} cycle point {k} at +{pitch:F0}: {badCycle}");
                    if (w.neckShoulder > 0 || w.bar1.Defects > 0 || w.nearSlab > 0) failures.Add($"{label} cycle point {k} at +{pitch:F0}: {w}");
                    if (notSettled == null && Bar2Applies(expected, pitch))
                    {
                        // Bar 2 on settled rows only (td line 276, amend5b full bar); the ruled cells (Bar2Waiver: amend8g (1) carry-over, W-49-4) print WAIVED with the citation and the figure instead of failing.
                        string bar2 = Bar2(expected, label, pitch, w, out bool torsoMissWaived);
                        if (bar2 != null) { failures.Add($"{label} cycle point {k} at +{pitch:F0} (bar 2): {bar2}"); bar2Failed[pitch] = bar2Failed.TryGetValue(pitch, out var bf) ? bf + 1 : 1; }
                        bar2Rows[pitch] = bar2Rows.TryGetValue(pitch, out var br) ? br + 1 : 1;
                        if (torsoMissWaived)
                        {
                            report.AppendLine($"# WAIVED bar 2 ({Bar2Waiver(expected, label, pitch)}) {label}-cycle{k} +{pitch:F0}: torso in bottom band {w.torsoBottomBand}, legs/feet {w.legs}, torso rays {w.torsoRays}");
                            waivedRows[pitch] = waivedRows.TryGetValue(pitch, out var wr) ? (wr.n + 1, Mathf.Min(wr.minLegs, w.legs), Mathf.Max(wr.maxLegs, w.legs)) : (1, w.legs, w.legs);
                        }
                    }
                }
                yield return new WaitForSeconds(0.12f);
            }
            // Per pitch: the largest neck/shoulder ray count over the 24 cycle points and the point where it occurs (qa-2 grades Sprint bar 1 from this).
            foreach (var kv in maxNeck.OrderBy(x => x.Key))
            {
                report.AppendLine($"# GAITMAX {label} pitch {kv.Key:F0}: max neck/shoulder rays {kv.Value.neck} at cycle point {kv.Value.phase}");
                gaitMax.AppendLine($"{label},{kv.Key:F0},{kv.Value.neck},{kv.Value.phase}");
            }
            foreach (var kv in bar2Rows.OrderBy(x => x.Key))
            {
                string cellWaiver = Bar2Waiver(expected, label, kv.Key);
                bool anyFailed = bar2Failed.TryGetValue(kv.Key, out int nf);
                string line = waivedRows.TryGetValue(kv.Key, out var wv)
                    ? $"WAIVED bar 2 ({cellWaiver}) {label} +{kv.Key:F0}: torso in bottom band 0 on {wv.n} of {kv.Value} settled rows (legs/feet {wv.minLegs} to {wv.maxLegs} on those rows); bar 1 asserted on every row"
                    : anyFailed ? $"bar 2 FAILED on {nf} of {kv.Value} settled rows of {label} +{kv.Key:F0} (listed as failures)"
                    : $"bar 2 {(cellWaiver != null ? $"(waived cell, {cellWaiver}) " : "")}met on all {kv.Value} settled rows of {label} +{kv.Key:F0}";
                if (anyFailed && wv.n > 0) line += $"; FAILED bar 2 on {nf} of {kv.Value} settled rows (listed as failures)";
                report.AppendLine("# " + line);
                Debug.Log("[LookDown] " + line);
            }
            report.AppendLine($"# ADVANCE {label}: {advSkips} of {advSamples} samples skipped (state change straddled three snapshot pairs)");
            if (advSkips * 24 > advSamples * 3) failures.Add($"{label}: {advSkips} of {advSamples} advancing checks were skipped (more than 3 in 24)");
            // Crouch-walk: no frame of the cycle at +60 may show the neck (director amend4 a).
            if (stance == Stance.Crouch)
            {
                player.SetLook(player.Yaw, 60f);
                Place(floorCentre - Vector3.forward * 25f, 0f);   // the measuring frames are long; stay on the floor in the gait
                for (int f = 0; f < 36; f++)
                {
                    yield return new WaitForSeconds(0.04f);
                    var cw = Measure(player.ViewCamera, Bake(fp));
                    report.AppendLine(Row(stance, 60f, $"{label}-frame{f} {fp.LastLookDownOffset.x:F3}", cw, GaitSample(label, target, floorCentre, failures, $"frame {f}")));
                    if (cw.neckShoulder > 0) failures.Add($"{label} frame {f} at +60 shows the neck: {cw}");
                }
            }
            // td line 277: the sweep rows below now take the cycle loop's path (Place, two frames, AdvancingCheck, Measure, Settled()). An unsettled row fails ("does not count").
            int sweepRows = 0, sweepSkips = 0;
            // Offset windows under this gait: force each actual offset at +60 (0 to 0.12) and +85 (0.10 to 0.40) while moving.
            foreach (var (gp, lo, hi, step) in new[] { (35f, 0f, 0.40f, 0.02f), (40f, 0f, 0.40f, 0.02f), (45f, 0f, 0.40f, 0.02f), (50f, 0f, 0.40f, 0.02f), (55f, 0f, 0.40f, 0.02f), (60f, 0f, 0.40f, 0.02f), (65f, 0f, 0.40f, 0.02f), (70f, 0f, 0.40f, 0.02f), (75f, 0f, 0.40f, 0.02f), (80f, 0f, 0.40f, 0.02f), (85f, 0f, 0.40f, 0.02f) })
            {
                player.SetLook(player.Yaw, gp);
                Place(floorCentre - Vector3.forward * 25f, 0f);   // the measuring frames are long; stay on the floor in the gait
                for (float b = lo; b <= hi + 1e-4f; b += step)
                {
                    player.LookDownTuning.debugBackOverride = b;
                    Place(floorCentre - Vector3.forward * 25f, 0f);
                    yield return null;
                    yield return null;
                    string advGs = null;
                    yield return AdvancingCheck(r => advGs = r);
                    sweepRows++;
                    if (advGs == Skipped) sweepSkips++;
                    else if (advGs != null) failures.Add($"{label} gait sweep {b:F3} at +{gp:F0}: {advGs}");
                    var gs = Measure(player.ViewCamera, Bake(fp));
                    string gsToken = SweepToken(label);
                    string gsNotSettled = Settled(expected, target, gsToken, out string gsDiag);
                    if (gsNotSettled != null) failures.Add($"{label} gait sweep {b:F3} at +{gp:F0} does not count: {gsNotSettled}");
                    report.AppendLine(Row(stance, gp, $"{label}-gaitsweep {b:F3}", gs, GaitSample(label, target, floorCentre, failures, $"gait sweep {b:F3} at +{gp:F0}"), $",\"{(gsNotSettled == null ? "yes" : "NO")} {gsDiag}\""));
                }
                player.LookDownTuning.debugBackOverride = float.NaN;
            }
            for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
            {
                player.SetLook(player.Yaw, pitch);
                Place(floorCentre - Vector3.forward * 25f, 0f);   // the measuring frames are long; stay on the floor in the gait
                yield return null;
                yield return null;
                string advCs = null;
                yield return AdvancingCheck(r => advCs = r);
                sweepRows++;
                if (advCs == Skipped) sweepSkips++;
                else if (advCs != null) failures.Add($"{label} clip sweep at {pitch:F0}: {advCs}");
                var m = Measure(player.ViewCamera, Bake(fp));
                string csNotSettled = Settled(expected, target, SweepToken(label), out string csDiag);
                report.AppendLine(Row(stance, pitch, $"{label}-clipsweep {fp.LastLookDownOffset.x:F3}", m, GaitSample(label, target, floorCentre, failures, $"clip sweep at {pitch:F0}"), $",\"{(csNotSettled == null ? "yes" : "NO")} {csDiag}\""));
                if (csNotSettled != null) failures.Add($"{label} clip sweep at {pitch:F0} does not count: {csNotSettled}");
                else if (m.neckShoulder > 0 || m.nearSlab > 0 || m.bar1.Defects > 0) failures.Add($"{label} moving clipping sweep at {pitch:F0} (bar 1): {m}");
            }
            report.AppendLine($"# SWEEP {label}: {sweepRows} sweep rows, {sweepSkips} advancing checks skipped");
            if (sweepSkips * 24 > sweepRows * 3) failures.Add($"{label}: {sweepSkips} of {sweepRows} sweep advancing checks were skipped (more than 3 in 24)");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            player.ExternalMoveInput = null;
            yield return new WaitForSeconds(0.6f);
        }

        static string SweepToken(string label) => label.StartsWith("Crouch") ? "CrouchWalk" : label.StartsWith("Walk") ? "Walk" : label.StartsWith("Jog") ? "Jog" : "Sprint";

        /// <summary>amend5b: bar 2 is the full bar at Stand +60..+85 and Crouch +80..+85 (Crouch +40..+75 and Stand +50..+55 have the torso clause waived).</summary>
        static bool Bar2Applies(Stance stance, float pitch) => stance == Stance.Stand ? pitch >= 60f : stance == Stance.Crouch && pitch >= 80f;

        /// <summary>
        /// The ruled gait cells of the bar 2 torso clause (the torso clause only; legs/feet and bar 1 are waived nowhere). Returns the citation, or null for a full-bar cell.
        /// (1) Director amend8g (1), word for word: "Stand, settled Sprint 8 at +60, +65, +70, +75, +80 and +85, and settled Jog 5 at +60 and +65, on the men's Farmer view body."
        ///     Carried over to the Survivor by Noah, 2026-11-10 (gate/pod49-walk-jog-bar2-to-noah, option 1, Q1).
        /// (2) W-49-4, Noah 2026-11-10 (gate/pod49-walk-jog-bar2-to-noah, option 1, Q2 as corrected by note2): Stand, Walk 1.15 and Walk 1.5 at +60, +65 and +70, and Jog 5 at +70, +75 and +80.
        /// Every other amend5b cell keeps the full bar: Walk 1.15/1.5 at +75..+85, Jog 5 at +85, CrouchWalk at +80/+85 (idle Stand +60/+65 is W-49-1/W-49-2, in the idle check).
        /// </summary>
        static string Bar2Waiver(Stance stance, string label, float pitch)
        {
            if (stance != Stance.Stand) return null;
            if ((label == "Sprint8" && pitch >= 60f && pitch <= 85f) || (label == "Jog5" && (pitch == 60f || pitch == 65f))) return "amend8g (1), carried over to the Survivor (Noah 2026-11-10)";
            if (((label == "Walk1.15" || label == "Walk1.5") && (pitch == 60f || pitch == 65f || pitch == 70f)) || (label == "Jog5" && (pitch == 70f || pitch == 75f || pitch == 80f))) return "W-49-4 (Noah 2026-11-10)";
            return null;
        }

        /// <summary>
        /// Bar 2 on one SETTLED gait row (call it for settled rows only). Returns the failure text, or null. torsoMissWaived is true when the only miss is the
        /// torso clause in a ruled cell (Bar2Waiver: amend8g (1) carry-over or W-49-4): the caller prints WAIVED with the citation and the figure. Legs/feet missing always fails.
        /// </summary>
        static string Bar2(Stance stance, string label, float pitch, Result r, out bool torsoMissWaived)
        {
            torsoMissWaived = false;
            if (!Bar2Applies(stance, pitch)) return null;
            var miss = new List<string>();
            if (r.legs == 0) miss.Add($"no legs/feet in view (legs {r.legs})");
            if (r.torsoBottomBand == 0)
            {
                if (Bar2Waiver(stance, label, pitch) != null) torsoMissWaived = true;
                else miss.Add($"no chest or belly in the bottom band (torso in bottom band {r.torsoBottomBand}, torso rays {r.torsoRays}, legs/feet {r.legs}); outside the ruled cells (W-49-1, W-49-2, amend8g (1) carried over, W-49-4)");
            }
            return miss.Count == 0 ? null : string.Join("; ", miss);
        }

        [Test]
        public void Bar2_HandWorked_AssertsOutsideTheWaiver_PrintsWaivedInside()
        {
            Result MakeRow(int band, int legs) => new Result { torsoBottomBand = band, legs = legs, torsoRays = 0 };
            string f;
            // Full-bar cells (amend5b cells outside amend8g (1) and W-49-4): a torso miss fails.
            foreach (var (label, p) in new[] { ("Walk1.15", 75f), ("Walk1.15", 80f), ("Walk1.15", 85f), ("Walk1.5", 75f), ("Walk1.5", 80f), ("Walk1.5", 85f), ("Jog5", 85f) })
            {
                Assert.IsNotNull(Bar2(Stance.Stand, label, p, MakeRow(0, 9), out bool w0), $"{label} +{p:F0} torso 0 must fail (outside amend8g (1) and W-49-4)");
                Assert.IsFalse(w0, $"{label} +{p:F0} is not a waived cell");
                Assert.IsNull(Bar2Waiver(Stance.Stand, label, p), $"{label} +{p:F0} must cite no waiver");
            }
            Assert.IsNotNull(Bar2(Stance.Crouch, "CrouchWalk1.75", 80f, MakeRow(0, 5), out _), "CrouchWalk +80 torso 0 must fail");
            Assert.IsNotNull(Bar2(Stance.Crouch, "CrouchWalk1.75", 85f, MakeRow(0, 5), out _), "CrouchWalk +85 torso 0 must fail");
            // Waived cells: a torso miss passes, is flagged for WAIVED and cites its record.
            foreach (var (label, p, cite) in new[] {
                ("Sprint8", 60f, "amend8g"), ("Sprint8", 65f, "amend8g"), ("Sprint8", 70f, "amend8g"), ("Sprint8", 75f, "amend8g"), ("Sprint8", 80f, "amend8g"), ("Sprint8", 85f, "amend8g"),
                ("Jog5", 60f, "amend8g"), ("Jog5", 65f, "amend8g"), ("Jog5", 70f, "W-49-4"), ("Jog5", 75f, "W-49-4"), ("Jog5", 80f, "W-49-4"),
                ("Walk1.15", 60f, "W-49-4"), ("Walk1.15", 65f, "W-49-4"), ("Walk1.15", 70f, "W-49-4"), ("Walk1.5", 60f, "W-49-4"), ("Walk1.5", 65f, "W-49-4"), ("Walk1.5", 70f, "W-49-4") })
            {
                f = Bar2(Stance.Stand, label, p, MakeRow(0, 17), out bool w);
                Assert.IsNull(f, $"{label} +{p:F0} torso 0 is waived: {f}");
                Assert.IsTrue(w, $"{label} +{p:F0} must print WAIVED");
                StringAssert.StartsWith(cite, Bar2Waiver(Stance.Stand, label, p), $"{label} +{p:F0} must cite {cite}");
            }
            // The ruled cells are Stand only.
            Assert.IsNull(Bar2Waiver(Stance.Crouch, "Walk1.15", 60f), "no gait waiver outside Stand");
            // The waiver covers the torso clause only: no legs still fails, in a waived cell too.
            Assert.IsNotNull(Bar2(Stance.Stand, "Sprint8", 60f, MakeRow(0, 0), out _), "Sprint 8 +60 with no legs must fail");
            Assert.IsNotNull(Bar2(Stance.Stand, "Sprint8", 60f, MakeRow(3, 0), out _), "Sprint 8 +60 with torso but no legs must fail");
            // A met row prints nothing; cells outside the full bar are not asserted.
            Assert.IsNull(Bar2(Stance.Stand, "Sprint8", 60f, MakeRow(3, 13), out bool met));
            Assert.IsFalse(met, "a met row is not WAIVED");
            Assert.IsNull(Bar2(Stance.Stand, "Walk1.5", 55f, MakeRow(0, 9), out _), "Stand +55 is not under the full bar");
            Assert.IsNull(Bar2(Stance.Crouch, "CrouchWalk1.75", 75f, MakeRow(0, 5), out _), "Crouch +75 has the torso clause waived (amend5b)");
        }

        /// <summary>
        /// Forced failure for td line 277: the OLD sweep pattern (Place, two frames, Measure, no settle frames) must read as NOT settled at Sprint 8 once the
        /// player is running. Executed in the class run, and green only while Settled() rejects such rows. It would go red if the old pattern ever counted.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator SweepPattern_WithoutSettleFrames_IsRejectedBySettled()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsTrue(view?.FirstPerson != null && view.FirstPerson.HasViewBody, "no view body");
            var fp = view.FirstPerson;
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            Place(floorCentre - Vector3.forward * 25f, 0f);
            yield return new WaitForSeconds(0.3f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
            yield return new WaitForSeconds(0.9f);
            int rejected = 0;
            var diag = new StringBuilder();
            for (int i = 0; i < 6; i++)
            {
                player.SetLook(player.Yaw, 65f);
                Place(floorCentre - Vector3.forward * 25f, 0f);
                yield return null;
                yield return null;
                Measure(player.ViewCamera, Bake(fp));   // as the old loop did, so the frame cost is the same
                string notSettled = Settled(Stance.Stand, 8f, "Sprint", out string d);
                if (notSettled != null) rejected++;
                diag.AppendLine($"old-pattern row {i}: {(notSettled == null ? "SETTLED" : "rejected: " + notSettled)} [{d}]");
            }
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Debug.Log("[LookDown] forced failure, old sweep pattern at Sprint 8:\n" + diag);
            Assert.Greater(rejected, 0, "the old sweep pattern (Place, two frames, no settle frames) was never rejected by Settled(), so the settle frames add nothing:\n" + diag);
        }

        /// <summary>
        /// td: every gait sample asserts (a) grounded (the controller and the Animator AirTime, not Jump_Air), (b) horizontal speed within 5% of the gait's
        /// target, (c) the player inside the test floor with at least 2 m of margin. Returns the three logged columns; a failed check is added to the failures.
        /// </summary>
        string GaitSample(string label, float target, Vector3 floorCentre, List<string> failures, string where)
        {
            string text = GaitCheck(target, out string extra);
            if (text != null) failures.Add($"{label} {where}: {text}");
            return extra;
        }

        string GaitCheck(float target, out string extra)
        {
            float v = new Vector2(body.velocity.x, body.velocity.z).magnitude;
            float air = view != null && view.Body != null ? view.Body.Animator.GetFloat("AirTime") : 0f;
            bool grounded = player.IsGrounded && air <= 0.05f;
            Vector3 p = player.transform.position, c = floor.transform.position;
            float margin = Mathf.Min(FloorHalf - Mathf.Abs(p.x - c.x), FloorHalf - Mathf.Abs(p.z - c.z));
            extra = $",{grounded},{v:F2},{margin:F1}";
            var bad = new List<string>();
            if (!grounded) bad.Add($"not grounded (controller {player.IsGrounded}, AirTime {air:F2})");
            if (target > 0f ? Mathf.Abs(v - target) > SpeedTolerance * target : v > 0.05f) bad.Add($"speed {v:F2} m/s is not within 5% of {target:F2}");
            if (margin < FloorMarginMin) bad.Add($"{margin:F1} m from the floor edge (need {FloorMarginMin:F0})");
            return bad.Count == 0 ? null : string.Join("; ", bad);
        }

        PlayerCharacterView view;

        /// <summary>
        /// Explicit, expected to go RED: the old path, no re-placement. A sprint held for 10 s of game time leaves the floor (or lands airborne), so the
        /// grounded, speed or floor-margin check must fail instead of producing a quiet bad row.
        /// </summary>
        [UnityTest, Explicit("Forced-bad case: the old no-re-place path at Sprint must fail the per-sample checks"), Timeout(600000)]
        public IEnumerator Gait_NoReplace_Sprint_MustTurnRed()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z - 25f), 0f);
            yield return new WaitForSeconds(0.3f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
            yield return new WaitForSeconds(10f);   // 80 m at 8 m/s from 25 m before the centre of a 60 m floor: the sprinter must be off the floor or falling
            string text = GaitCheck(8f, out string extra);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Debug.Log($"LookDownTests: {(text == null ? "EXPECTED RED: got GREEN (harness blind)" : "EXPECTED RED: got RED: " + text)} [{extra}]");
            Assert.IsNull(text, "EXPECTED RED check (this assert is meant to fail): " + text);
        }

        /// <summary>
        /// Arm-into-body interpenetration on the SHADOW body (the one with the Animator and the full mesh), so a hand passing through the thigh or torso is not judged from
        /// silhouette alone: every hand, finger and forearm vertex (dominant bone) is tested against the body triangles (torso, hips, legs, feet) by ray parity in two
        /// directions; a vertex counts as inside when both rays cross an odd number of body triangles. Counts only: the meshes are separate and not guaranteed closed,
        /// so a small count is expected noise and a large one is real contact.
        /// </summary>
        (int armVerts, int inside) ArmInsideBody(FirstPersonBody fp) => ArmInsideBody(fp, out _, out _);

        (int armVerts, int inside) ArmInsideBody(FirstPersonBody fp, out int controlPoints, out int controlInside)
        {
            var bodyTris = new List<Tri>(20000);
            var armPts = new List<Vector3>(2000);
            var controlPts = new List<Vector3>(500);
            var mesh = new Mesh();
            foreach (var r in fp.BodyRenderers)
            {
                if (!(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                var weights = smr.sharedMesh.boneWeights;
                var bones = smr.bones;
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                string BoneOf(int vi)
                {
                    int bi = weights.Length > vi ? weights[vi].boneIndex0 : -1;
                    return bi >= 0 && bi < bones.Length && bones[bi] != null ? bones[bi].name : "";
                }
                for (int i = 0; i < v.Length; i++)
                {
                    string n = BoneOf(i);
                    if (n.Contains("UpperLeg") && i % 7 == 0)
                    {
                        int bi = weights[i].boneIndex0;
                        if (bi >= 0 && bi < bones.Length && bones[bi] != null) controlPts.Add(Vector3.Lerp(pos + rot * v[i], bones[bi].position, 0.6f));   // 60% of the way from the thigh surface to its bone: well inside
                    }
                    if (n.Contains("Hand") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky") || n.Contains("Thumb") || n.Contains("LowerArm")) armPts.Add(pos + rot * v[i]);
                }
                for (int i = 0; i < t.Length; i += 3)
                {
                    string n = BoneOf(t[i]);
                    if (n.Contains("Hips") || n.Contains("Abdomen") || n.Contains("Chest") || n.Contains("Torso") || n.Contains("UpperLeg") || n.Contains("LowerLeg") || n.Contains("Foot") || n.StartsWith("PT"))
                        bodyTris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]] });
                }
            }
            Object.Destroy(mesh);
            int inside = 0;
            foreach (var pt in armPts)
                if (OddCrossings(pt, new Vector3(0.001f, 1f, 0.002f).normalized, bodyTris) && OddCrossings(pt, new Vector3(1f, 0.002f, 0.001f).normalized, bodyTris)) inside++;
            controlPoints = controlPts.Count;
            controlInside = 0;
            foreach (var pt in controlPts)
                if (OddCrossings(pt, new Vector3(0.001f, 1f, 0.002f).normalized, bodyTris) && OddCrossings(pt, new Vector3(1f, 0.002f, 0.001f).normalized, bodyTris)) controlInside++;
            return (armPts.Count, inside);
        }

        /// <summary>
        /// td: the arm-inside-body count needs a positive control. Standing idle, the baseline count is read; then both hand bones are moved into the middle of the chest after the
        /// animators have run (end of frame, before the bake) and the same count must come out NONZERO, with the hand's fingers following. A method that cannot read a hand
        /// inside the torso as inside would be blind to the table's zeros.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator ArmInsideBody_PositiveControl_ArmPushedIntoTorso_ReadsNonzero()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsTrue(view?.FirstPerson != null && view.FirstPerson.HasViewBody, "no view body");
            var fp = view.FirstPerson;
            yield return new WaitForSeconds(1.0f);
            yield return null;   // WaitForEndOfFrame is not evoked in batchmode; the baseline reads the previous frame's pose, and nothing runs between the push and the second read
            var baseline = ArmInsideBody(fp);
            Transform chest = null;
            foreach (var kv in view.Body.Bones) if (kv.Key.Contains("Chest") || kv.Key.Contains("Torso")) { chest = kv.Value; break; }
            if (chest == null) foreach (var kv in view.Body.Bones) if (kv.Key.Contains("Spine")) chest = kv.Value;
            Assert.IsNotNull(chest, "no chest or spine bone to push the hands into");
            // Both wrists must be found by exact name before any reads: a name that matches nothing must fail the test, not shrink it (td, QA report line 271).
            Assert.IsNotNull(view.Body.Bone("Wrist.L"), "no bone named exactly 'Wrist.L' on the shadow body");
            Assert.IsNotNull(view.Body.Bone("Wrist.R"), "no bone named exactly 'Wrist.R' on the shadow body");
            int moved = 0;
            foreach (var kv in view.Body.Bones)
                if (kv.Key == "Wrist.L" || kv.Key == "Wrist.R") { kv.Value.position = chest.position; moved++; }
            Assert.AreEqual(2, moved, "expected the two hand bones to be moved");
            var pushed = ArmInsideBody(fp);
            string line = $"arm-inside-body positive control: baseline {baseline.inside} of {baseline.armVerts}; hands pushed into the chest ({chest.name}): {pushed.inside} of {pushed.armVerts}";
            Debug.Log("LookDownTests: " + line);
            Directory.CreateDirectory(OutDir);
            File.AppendAllText($"{OutDir}/interpenetration-positive-control.txt", line + "\n");
            Assert.Greater(pushed.inside, baseline.inside, line);
            Assert.Greater(pushed.inside, 0, "the method read a hand inside the torso as zero: it is blind. " + line);
        }

        static bool OddCrossings(Vector3 origin, Vector3 dir, List<Tri> tris)
        {
            int n = 0;
            foreach (var tri in tris) if (RayHit(origin, dir, tri, out _, out _)) n++;
            return (n & 1) == 1;
        }

        static string StillDir => RunOutput.Dir("lookdown-stills");
        /// <summary>The Sprint and Jog watch stills, the hand-ray table and the first-person shadow stills (director, #19). Off for 17c; turned on for the run after the arm fix.</summary>
        const bool IncludeArmStills = true;
        readonly List<string> stillFailures = new List<string>();

        /// <summary>
        /// Stills at the CONFIGURED table for the waiver (td): view body, 16:9 and 21:9. Moving cases take 8 gait phases and keep the one with the fewest
        /// torso rays (the worst case for the band); idle cases are a single frame. Each kept pair is named for its case and phase.
        /// </summary>
        [UnityTest, Timeout(2400000)]
        public IEnumerator ConfiguredStills_ForTheWaiver()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsTrue(view?.FirstPerson != null && view.FirstPerson.HasViewBody, "no view body");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(StillDir);
            var summary = new StringBuilder();
            stillFailures.Clear();
            // Stand cases.
            yield return StillCase("Stand_+50_idle", Stance.Stand, 50f, 0f, fp, summary);
            yield return StillCase("Stand_+60_idle_reference", Stance.Stand, 60f, 0f, fp, summary);
            yield return StillCase("Stand_+50_Sprint8", Stance.Stand, 50f, 8f, fp, summary, Key.W, Key.LeftShift);
            yield return StillCase("Stand_+55_Jog5", Stance.Stand, 55f, 5f, fp, summary, Key.W);
            if (IncludeArmStills)   // false until #19: artist's arm fix changes the Sprint and Jog clips, so these are taken once, on the final arm pose
            {
                // Director watch item: does the swinging hand enter the normal view while sprinting (pitch 0 and +30)?
                yield return StillCase("Stand_+0_Sprint8_watch", Stance.Stand, 0f, 8f, fp, summary, Key.W, Key.LeftShift);
                yield return StillCase("Stand_+30_Sprint8_watch", Stance.Stand, 30f, 8f, fp, summary, Key.W, Key.LeftShift);
                yield return StillCase("Stand_+0_Jog5_watch", Stance.Stand, 0f, 5f, fp, summary, Key.W);
                yield return StillCase("Stand_+30_Jog5_watch", Stance.Stand, 30f, 5f, fp, summary, Key.W);
                // Director (#19): first-person stills of the SHADOW at +30, mid-stride, for Sprint and Jog; every phase is kept so the mid-stride one can be picked.
                yield return StillCase("Stand_+30_Sprint8_shadow", Stance.Stand, 30f, 8f, fp, summary, Key.W, Key.LeftShift);
                yield return StillCase("Stand_+30_Jog5_shadow", Stance.Stand, 30f, 5f, fp, summary, Key.W);
                // Hand rays for Jog 5 and Sprint 8 at -15, 0, +15, +30, every phase.
                yield return HandRays(fp);
            }
            // Crouch cases.
            yield return Go(StanceKey.Crouch, 4f);
            Assert.AreEqual(Stance.Crouch, player.Stance, "could not crouch for the stills");
            yield return StillCase("Crouch_+75_idle", Stance.Crouch, 75f, 0f, fp, summary);
            yield return StillCase("Crouch_+80_idle_reference", Stance.Crouch, 80f, 0f, fp, summary);
            yield return StillCase("Crouch_+75_CrouchWalk", Stance.Crouch, 75f, 1.75f, fp, summary, Key.W);
            File.WriteAllText($"{StillDir}/stills-summary.txt", summary.ToString());
            FlushAttribution();
            Assert.IsEmpty(stillFailures, string.Join("\n", stillFailures));
        }

        /// <summary>
        /// qa-2, GitHub #37 waiver stills (td line 276): Stand, settled Sprint 8 at +60 and +75 and Jog 5 at +60, view body, 16:9 and 21:9.
        /// 8 gait phases each; StillCase keeps the phase with the fewest torso rays. Test-only, no product change.
        /// </summary>
        [UnityTest, Timeout(1200000)]
        public IEnumerator WaiverStills_Sprint8_Jog5_GitHub37()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsTrue(view?.FirstPerson != null && view.FirstPerson.HasViewBody, "no view body");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(StillDir);
            var summary = new StringBuilder();
            stillFailures.Clear();
            yield return StillCase("Stand_+60_Sprint8_waiver", Stance.Stand, 60f, 8f, fp, summary, Key.W, Key.LeftShift);
            yield return StillCase("Stand_+75_Sprint8_waiver", Stance.Stand, 75f, 8f, fp, summary, Key.W, Key.LeftShift);
            yield return StillCase("Stand_+60_Jog5_waiver", Stance.Stand, 60f, 5f, fp, summary, Key.W);
            File.WriteAllText($"{StillDir}/waiver-stills-summary.txt", summary.ToString());
            FlushAttribution();
            Assert.IsEmpty(stillFailures, string.Join("\n", stillFailures));
        }

        /// <summary>Hand and lower-arm rays for Jog 5 and Sprint 8 at pitch -15, 0, +15 and +30, 8 phases each (one placement, walk at least 1 s, sample). Printed and written to lookdown\\handrays.csv.</summary>
        IEnumerator HandRays(FirstPersonBody fp)
        {
            var csv = new StringBuilder("gait,pitch,phase,handRays,neckShoulder,torsoRays,armVerts,armInsideBody,controlPoints,controlInside,settled\n");
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            foreach (var g in new[] { ("Jog5", 5f, new[] { Key.W }), ("Sprint8", 8f, new[] { Key.W, Key.LeftShift }) })
                foreach (float pitch in new[] { -15f, 0f, 15f, 30f })
                    for (int ph = 0; ph < 8; ph++)
                    {
                        Place(floorCentre - Vector3.forward * 27f, 0f);
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState(g.Item3));
                        yield return new WaitForSeconds(1.0f + 0.071f * ph);
                        player.SetLook(player.Yaw, pitch);
                        yield return null;
                        yield return null;
                        var m = Measure(player.ViewCamera, Bake(fp));
                        string notSettled = Settled(Stance.Stand, g.Item2, g.Item1.StartsWith("Jog") ? "Jog" : "Sprint", out string diag);
                        var arm = ArmInsideBody(fp, out int ctrlPts, out int ctrlIn);
                        csv.AppendLine($"{g.Item1},{pitch:F0},{ph},{m.handRays},{m.neckShoulder},{m.torsoRays},{arm.armVerts},{arm.inside},{ctrlPts},{ctrlIn},\"{(notSettled == null ? "yes" : "NO: " + notSettled)}\"");
                        if (notSettled != null) stillFailures.Add($"hand rays {g.Item1} +{pitch:F0} phase {ph}: {notSettled}");
                        if (m.handRays > 0) stillFailures.Add($"hand rays {m.handRays} on the VIEW body (amend7 requires 0): {g.Item1} +{pitch:F0} phase {ph}");
                        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    }
            File.WriteAllText($"{OutDir}/handrays.csv", csv.ToString());
            Debug.Log("LookDownTests: hand rays\n" + csv);
            yield return new WaitForSeconds(0.4f);
        }

        IEnumerator StillCase(string name, Stance stance, float pitch, float target, FirstPersonBody fp, StringBuilder summary, params Key[] keys)
        {
            Assert.AreEqual(stance, player.Stance, $"{name}: the player is {player.Stance}");
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            bool moving = target > 0f;
            int phases = moving ? 8 : 1;
            int bestRays = int.MaxValue, bestPhase = -1, stillSkips = 0;
            for (int ph = 0; ph < phases; ph++)
            {
                Place(floorCentre - Vector3.forward * 27f, 0f);
                if (moving)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                    yield return new WaitForSeconds(1.0f + 0.071f * ph);   // td: walk at least 1 s after the placement, then sample with no re-place in between
                }
                else yield return new WaitForSeconds(0.5f);
                player.SetLook(player.Yaw, pitch);
                yield return null;
                yield return null;
                var m = Measure(player.ViewCamera, Bake(fp));
                yield return StillAspect($"{StillDir}/tmp_{name}_p{ph}_16x9.png", 960, 540, 16f / 9f);
                yield return StillAspect($"{StillDir}/tmp_{name}_p{ph}_21x9.png", 1260, 540, 21f / 9f);
                var st = view.Body.Animator.GetCurrentAnimatorStateInfo(0);
                LogHits("stills", $"{name} phase {ph}", m);
                if (name.Contains("Crouch_+75_CrouchWalk"))
                {
                    yield return StillDots($"{StillDir}/tmp_{name}_p{ph}_hits_16x9.png", 960, 540, 16f / 9f, m.hits);
                    yield return StillDots($"{StillDir}/tmp_{name}_p{ph}_hits_21x9.png", 1260, 540, 21f / 9f, m.hits);
                }
                string advStill = null;
                yield return AdvancingCheck(r => advStill = r);
                if (advStill == Skipped) stillSkips++;
                else if (advStill != null) stillFailures.Add($"{name} phase {ph}: {advStill}");
                string token = name.Contains("CrouchWalk") ? "CrouchWalk" : name.Contains("Sprint") ? "Sprint" : name.Contains("Jog") ? "Jog" : "Idle";
                string notSettled = Settled(stance, target, token, out string settledDiag);
                var armIn = moving ? ArmInsideBody(fp) : (armVerts: 0, inside: 0);
                summary.AppendLine($"{name} phase {ph}: hand rays {m.handRays}; arm vertices inside the body {armIn.inside} of {armIn.armVerts}; SETTLED {(notSettled == null ? "yes" : "NO: " + notSettled)} [{settledDiag}]");
                if (notSettled != null) stillFailures.Add($"{name} phase {ph}: {notSettled}");
                summary.AppendLine($"{name} phase {ph}: applied f {fp.LastLookDownOffset.x:F3} m; {m}; DIAG stance {player.Stance} from {player.StanceFrom} blend {player.StanceBlend:F2} transitioning {player.StanceTransitioning}, eye {player.ViewCamera.transform.position.y - player.transform.position.y:F3}, pitch {player.Pitch:F1}, animator state hash {st.fullPathHash} t {st.normalizedTime:F2} speed {st.speed * st.speedMultiplier:F2}, LocoRate {view.Body.Animator.GetFloat("LocoRate"):F2}, controller speed {new Vector2(body.velocity.x, body.velocity.z).magnitude:F2}, view root {fp.ViewRoot.localPosition:F3}, shadow root {fp.ShadowRoot.localPosition:F3}");
                int score = name.Contains("watch") ? -m.handRays : m.torsoRays;   // watch cases: most hand rays; others: fewest torso rays
                if (score < bestRays) { bestRays = score; bestPhase = ph; }
                if (moving) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            }
            for (int ph = 0; ph < phases; ph++)
                foreach (var asp in name.Contains("Crouch_+75_CrouchWalk") ? new[] { "16x9", "21x9", "hits_16x9", "hits_21x9" } : new[] { "16x9", "21x9" })
                {
                    string tmp = $"{StillDir}/tmp_{name}_p{ph}_{asp}.png";
                    if (name.Contains("shadow")) { string keep = $"{StillDir}/{name}_p{ph}_{asp}.png"; if (File.Exists(keep)) File.Delete(keep); File.Move(tmp, keep); }
                    else if (ph == bestPhase) { string final = $"{StillDir}/{name}_{asp}.png"; if (File.Exists(final)) File.Delete(final); File.Move(tmp, final); }
                    else File.Delete(tmp);
                }
            summary.AppendLine($"{name}: advancing checks skipped {stillSkips} of {phases}");
            if (stillSkips * 8 > phases) stillFailures.Add($"{name}: {stillSkips} of {phases} advancing checks were skipped (more than 1 in 8)");
            summary.AppendLine($"{name}: kept phase {bestPhase} ({(name.Contains("watch") ? "hand rays " + (-bestRays) : "torso rays " + bestRays)})");
            yield return new WaitForSeconds(0.4f);
        }

        IEnumerator StillAspect(string path, int w, int h, float aspect)
        {
            var cam = player.ViewCamera;
            var rt = new RenderTexture(w, h, 24);
            var old = cam.targetTexture;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                cam.aspect = aspect;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = old;
                cam.ResetAspect();
                RenderTexture.active = null;
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
            yield return null;
        }

        /// <summary>
        /// td's rule for which sample counts: the stance is settled (blend 1, not transitioning), the pivot is at the stance's record eye (1.65 stand, 1.20 crouch)
        /// within 0.005, the expected clip family is the dominant clip of the base layer, the speed is within 5% of the target, grounded, and on the floor.
        /// Returns null when the sample counts, else the reasons; the DIAG text carries the same numbers.
        /// </summary>
        string Settled(Stance expected, float targetSpeed, string clipToken, out string diag)
        {
            var bad = new List<string>();
            float eye = player.ViewCamera.transform.position.y - player.transform.position.y;
            float record = expected == Stance.Crouch ? 1.20f : 1.65f;
            if (player.Stance != expected) bad.Add($"stance is {player.Stance}, expected {expected}");
            if (player.StanceTransitioning || player.StanceBlend < 0.999f) bad.Add($"stance not settled (transitioning {player.StanceTransitioning}, blend {player.StanceBlend:F2})");
            if (Mathf.Abs(eye - record) > 0.005f) bad.Add($"pivot eye {eye:F3} is not {record:F2} +-0.005");
            string dominant = "";
            float w = -1f;
            foreach (var ci in view.Body.Animator.GetCurrentAnimatorClipInfo(0))
                if (ci.weight > w) { w = ci.weight; dominant = ci.clip.name; }
            if (dominant.IndexOf(clipToken, System.StringComparison.OrdinalIgnoreCase) < 0) bad.Add($"dominant clip is '{dominant}' (weight {w:F2}), expected a {clipToken} clip");
            string gait = GaitCheck(targetSpeed, out string extra);
            if (gait != null) bad.Add(gait);
            diag = $"eye {eye:F3} blend {player.StanceBlend:F2} clip '{dominant}' w {w:F2}{extra}";
            return bad.Count == 0 ? null : string.Join("; ", bad);
        }

        /// <summary>Explicit, expected to go RED: sample during the crouch ease; the settled rule must reject it.</summary>
        [UnityTest, Explicit("Forced-bad case: a sample taken mid crouch-ease must fail the settled rule"), Timeout(600000)]
        public IEnumerator Settled_MidCrouchEase_MustTurnRed()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            player.PressStanceKey(StanceKey.Crouch);
            yield return null;
            yield return new WaitForSeconds(0.08f);
            string text = Settled(Stance.Crouch, 0f, "Idle", out string diag);
            Debug.Log($"LookDownTests: {(text == null ? "EXPECTED RED: got GREEN (harness blind)" : "EXPECTED RED: got RED: " + text)} [{diag}]");
            Assert.IsNull(text, "EXPECTED RED check (this assert is meant to fail): " + text);
        }

        /// <summary>
        /// The arbiter (td): Crouch +75 CrouchWalk through the REAL game path. Crouch toggled with the stance key on the keyboard device, walking by the
        /// controller's own input (ExternalMoveInput, as the FPS probe does), and NO Place after the settle. 8 phases at +75 with the same DIAG and the same
        /// settled asserts as the other harnesses. The harness that matches this within 1 neck ray and 2 leg rays is the final one for the record.
        /// </summary>
        [UnityTest, Timeout(1200000)]
        public IEnumerator Arbiter_Crouch75_CrouchWalk_RealGamePath()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsTrue(view?.FirstPerson != null && view.FirstPerson.HasViewBody, "no view body");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(StillDir);
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            Place(floorCentre - Vector3.forward * 27f, 0f);   // the only placement: the start
            yield return new WaitForSeconds(0.6f);
            // Crouch with the real stance key (Left Ctrl), then let the ease finish.
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(Stance.Crouch, player.Stance, "the stance key did not crouch");
            player.ExternalMoveInput = Vector2.up;   // the controller walks; the crouch speed limit sets 1.75 m/s
            yield return new WaitForSeconds(1.0f);
            var lines = new StringBuilder();
            var fails = new List<string>();
            int maxNeck = 0, minLegs = int.MaxValue, maxLegs = 0, maxTorso = 0, minTorso = int.MaxValue, arbSkips = 0;
            for (int ph = 0; ph < 8; ph++)
            {
                player.SetLook(player.Yaw, 75f);
                yield return null;
                yield return null;
                var m = Measure(player.ViewCamera, Bake(fp));
                LogHits("arbiter", $"phase {ph}", m);
                yield return StillDots($"{StillDir}/arbiter_p{ph}_hits_16x9.png", 960, 540, 16f / 9f, m.hits);
                string advArb = null;
                yield return AdvancingCheck(r => advArb = r);
                if (advArb == Skipped) arbSkips++;
                else if (advArb != null) fails.Add($"phase {ph}: {advArb}");
                string notSettled = Settled(Stance.Crouch, 1.75f, "CrouchWalk", out string diag);
                var st = view.Body.Animator.GetCurrentAnimatorStateInfo(0);
                lines.AppendLine($"arbiter phase {ph}: applied f {fp.LastLookDownOffset.x:F3} m; {m}; SETTLED {(notSettled == null ? "yes" : "NO: " + notSettled)} [{diag}], state hash {st.fullPathHash} t {st.normalizedTime:F2}, position {player.transform.position - floorCentre:F1}");
                if (notSettled != null) fails.Add($"phase {ph}: {notSettled}");
                maxNeck = Mathf.Max(maxNeck, m.neckShoulder);
                minLegs = Mathf.Min(minLegs, m.legs); maxLegs = Mathf.Max(maxLegs, m.legs);
                minTorso = Mathf.Min(minTorso, m.torsoRays); maxTorso = Mathf.Max(maxTorso, m.torsoRays);
                yield return new WaitForSeconds(0.05f + 0.031f * ph);
            }
            player.ExternalMoveInput = null;
            lines.AppendLine($"advancing checks skipped: {arbSkips} of 8");
            if (arbSkips > 1) fails.Add($"{arbSkips} of 8 advancing checks were skipped (more than 1 in 8)");
            lines.AppendLine($"ARBITER SUMMARY: max neck/shoulder rays {maxNeck}; legs/feet {minLegs}-{maxLegs}; torso rays {minTorso}-{maxTorso}");
            File.WriteAllText($"{OutDir}/arbiter-crouchwalk75.txt", lines.ToString());
            FlushAttribution();
            Debug.Log("LookDownTests: " + lines);
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>
        /// td: the animator must be advancing. Two snapshots 0.05 s of game time apart; the effective playback rate (change in normalized time x the weighted clip
        /// length / elapsed time) must be within 10% of the logged LocoRate x state speed, and above 0.5. Catches a frozen or zero-rate animator that samples one fixed
        /// pose while the controller still moves. A state change between the snapshots takes a fresh pair (up to three); only if all three straddle a change is the sample reported as Skipped, which the caller counts and caps.
        /// </summary>
        /// <summary>
        /// The advancing formula on its own: over consecutive frames, the sum of (change in normalized time x state length) divided by the sum of the frame
        /// delta times. 1.0 for a playing animator whatever its rate (the state length already includes the speed), 0 when frozen. NaN when there is nothing to
        /// measure; changed is true when the state hash differs between two frames.
        /// </summary>
        public static float AdvancingRate(IList<float> nt, IList<float> len, IList<float> dts, IList<int> hash, out bool changed)
        {
            changed = false;
            double num = 0.0, den = 0.0;
            bool infiniteLength = false;
            for (int i = 1; i < nt.Count; i++)
            {
                if (hash[i] != hash[i - 1]) { changed = true; continue; }   // skip the pair that straddles a change, keep measuring the rest
                float dn = nt[i] - nt[i - 1];
                if (dn < -0.5f) dn += 1f;   // the normalized time wrapped
                if (dn < 0f || dts[i] <= 0f) continue;
                if (float.IsInfinity(len[i]) || float.IsNaN(len[i]) || float.IsNaN(nt[i])) { infiniteLength = true; continue; }   // speed 0: the state length is infinite
                num += dn * len[i];
                den += dts[i];
            }
            if (den <= 0.0 && infiniteLength) return 0f;   // a frozen animator: nothing advances
            return den > 0.0 ? (float)(num / den) : float.NaN;
        }

        public static string AdvancingVerdict(float eff, float loggedRate)
        {
            if (eff <= 0.5f) return $"the animator is not advancing: normalized time moved {eff:F2} x real time";
            if (Mathf.Abs(eff - 1f) > 0.10f) return $"normalized time moved {eff:F2} x real time, not within 10% of 1.0 (logged LocoRate x state speed {loggedRate:F2})";
            return null;
        }

        static void Frames(List<float> nt, List<float> len, List<float> dts, List<int> hash, float startNt, float stateLength, float rate, int frames, float dt, int hashValue = 7)
        {
            // An animator playing at `rate` x real time on a state of `stateLength` seconds: normalized time moves rate x dt / stateLength per frame.
            float n = startNt;
            for (int i = 0; i < frames; i++)
            {
                nt.Add(n); len.Add(stateLength); dts.Add(dt); hash.Add(hashValue);
                n += rate * dt / stateLength;
            }
        }

        [Test]
        public void AdvancingMath_HandWorked_KnownAdvancing_ReadsOne()
        {
            // Hand-worked: state length 2.0 s, dt 0.01 s, 10 frames, normalized time +0.005 per frame (= dt / length): sum(dn x len) = 9 x 0.005 x 2.0 = 0.09, sum(dt) = 9 x 0.01 = 0.09, ratio 1.0.
            var nt = new List<float>(); var len = new List<float>(); var dts = new List<float>(); var hash = new List<int>();
            Frames(nt, len, dts, hash, 0.10f, 2.0f, 1.0f, 10, 0.01f);
            float eff = AdvancingRate(nt, len, dts, hash, out bool changed);
            Assert.IsFalse(changed);
            Assert.AreEqual(1.0f, eff, 1e-4f);
            Assert.IsNull(AdvancingVerdict(eff, 1.0f), "a playing animator must pass");
        }

        [Test]
        public void AdvancingMath_HandWorked_Wrap_StillReadsOne()
        {
            // Same animator starting at normalized time 0.98: it wraps past 1.0 inside the window; the wrap is undone and the ratio is still 1.0.
            var nt = new List<float>(); var len = new List<float>(); var dts = new List<float>(); var hash = new List<int>();
            Frames(nt, len, dts, hash, 0.98f, 2.0f, 1.0f, 10, 0.01f);
            for (int i = 0; i < nt.Count; i++) nt[i] = nt[i] % 1f;
            float eff = AdvancingRate(nt, len, dts, hash, out _);
            Assert.AreEqual(1.0f, eff, 1e-3f);
            Assert.IsNull(AdvancingVerdict(eff, 1.0f));
        }

        [Test]
        public void AdvancingMath_HandWorked_KnownFrozen_ReadsZero_AndFails()
        {
            var nt = new List<float>(); var len = new List<float>(); var dts = new List<float>(); var hash = new List<int>();
            Frames(nt, len, dts, hash, 0.40f, 2.0f, 0.0f, 10, 0.01f);   // speed 0: the normalized time never moves
            float eff = AdvancingRate(nt, len, dts, hash, out _);
            Assert.AreEqual(0.0f, eff, 1e-6f);
            Assert.IsNotNull(AdvancingVerdict(eff, 0.0f), "a frozen animator must FAIL the check");
        }

        [Test]
        public void AdvancingMath_HandWorked_TooFastAndTooSlow_Fail_ButTenPercentPasses()
        {
            foreach (var c in new[] { (2.0f, false), (0.4f, false), (1.2f, false), (0.8f, false), (1.09f, true), (0.91f, true) })
            {
                var nt = new List<float>(); var len = new List<float>(); var dts = new List<float>(); var hash = new List<int>();
                Frames(nt, len, dts, hash, 0.10f, 2.0f, c.Item1, 10, 0.01f);
                float eff = AdvancingRate(nt, len, dts, hash, out _);
                Assert.AreEqual(c.Item1, eff, 1e-3f);
                Assert.AreEqual(c.Item2, AdvancingVerdict(eff, 1.0f) == null, $"rate {c.Item1}");
            }
        }

        [Test]
        public void AdvancingMath_HandWorked_FrozenWithInfiniteStateLength_ReadsZero()
        {
            // Animator speed 0: the state length (which includes the speed) is infinite and 0 x infinity would be NaN; the frozen signature must read 0.0 and FAIL.
            var nt = new List<float>(); var len = new List<float>(); var dts = new List<float>(); var hash = new List<int>();
            for (int i = 0; i < 10; i++) { nt.Add(0.4f); len.Add(float.PositiveInfinity); dts.Add(0.01f); hash.Add(7); }
            float eff = AdvancingRate(nt, len, dts, hash, out _);
            Assert.AreEqual(0.0f, eff, 1e-6f);
            Assert.IsNotNull(AdvancingVerdict(eff, 0.0f));
        }

        [Test]
        public void AdvancingMath_StateChange_IsReported_NotMeasured()
        {
            var nt = new List<float>(); var len = new List<float>(); var dts = new List<float>(); var hash = new List<int>();
            Frames(nt, len, dts, hash, 0.10f, 2.0f, 1.0f, 5, 0.01f, 7);
            Frames(nt, len, dts, hash, 0.10f, 2.0f, 1.0f, 5, 0.01f, 8);
            AdvancingRate(nt, len, dts, hash, out bool changed);
            Assert.IsTrue(changed, "a state change inside the window takes a fresh window");
        }

        public const string Skipped = "SKIP";

        /// <summary>
        /// Advancing check, measured the way the foot-slide sampler reads 1.000: consecutive frames, in LateUpdate (after the Animator), each pair
        /// giving (change in normalized time x the state length) / that frame's delta time. A playing animator reads 1.0 whatever its rate, because the
        /// state length already includes the speed; a frozen or zero-rate one reads 0. Over about 0.05 s of game time it must be within 10% of 1.0 and
        /// above 0.5. A state change inside the window takes a fresh window (up to three); only if all three straddle a change is the sample Skipped,
        /// which the caller counts and caps. (The earlier version took two snapshots in Update, one frame out of step with the Animator, and read 2 to 5
        /// times too fast; the LocoRate x state speed it should equal is logged in DIAG, not asserted here.)
        /// </summary>
        IEnumerator AdvancingCheck(System.Action<string> result)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var sampler = player.gameObject.AddComponent<FootSampler>();
                sampler.left = view.Body.Bone("Foot.L");
                sampler.right = view.Body.Bone("Foot.R");
                sampler.animator = view.Body.Animator;
                sampler.recording = true;
                float t = 0f;
                int frames = 0;
                while (t < 0.05f || frames < 4) { yield return null; t += Time.deltaTime; frames++; }
                sampler.recording = false;
                float eff = AdvancingRate(sampler.nt, sampler.len, sampler.dts, sampler.hash, out bool changed);
                float rate = sampler.stateSpeed.Count > 0 ? sampler.stateSpeed[sampler.stateSpeed.Count - 1] : 0f;
                Object.Destroy(sampler);
                if (changed || float.IsNaN(eff)) continue;
                result(AdvancingVerdict(eff, rate));
                yield break;
            }
            result(Skipped);   // three windows in a row straddled a state change: counted by the caller, never a silent pass
        }

        (int hash, float nt, float length, float rate) AnimSnap()
        {
            var anim = view.Body.Animator;
            var st = anim.GetCurrentAnimatorStateInfo(0);
            return (st.fullPathHash, st.normalizedTime, st.length, st.speed * st.speedMultiplier);   // st.length already includes the speed, so a playing animator reads 1.0
        }

        readonly StringBuilder attribution = new StringBuilder();
        readonly Dictionary<string, Dictionary<string, int>> attributionByBone = new Dictionary<string, Dictionary<string, int>>();

        /// <summary>td: print every counted neck/shoulder ray with its dominant bone, top-2 vertex weights and frame position; summarise per set.</summary>
        void LogHits(string set, string label, Result r)
        {
            if (r.hits == null) return;
            if (!attributionByBone.TryGetValue(set, out var byBone)) attributionByBone[set] = byBone = new Dictionary<string, int>();
            foreach (var h in r.hits)
            {
                attribution.AppendLine($"{set}\t{label}\tframe ({h.nx:+0.00;-0.00}, {h.ny:+0.00;-0.00})\t{h.bone}\t{h.weights}");
                byBone[h.bone] = byBone.TryGetValue(h.bone, out int n) ? n + 1 : 1;
            }
        }

        void FlushAttribution()
        {
            Directory.CreateDirectory(OutDir);
            var sb = new StringBuilder();
            sb.AppendLine("SUMMARY (rays by dominant bone, per set):");
            foreach (var kv in attributionByBone)
                sb.AppendLine($"  {kv.Key}: " + string.Join(", ", kv.Value.OrderByDescending(x => x.Value).Select(x => $"{x.Key} x{x.Value}")));
            sb.AppendLine("DETAIL (set, sample, frame position in 16:9 normalised units, dominant bone, top-2 vertex weights):");
            sb.Append(attribution);
            File.AppendAllText($"{OutDir}/neck-attribution.txt", sb.ToString() + "\n");
            attribution.Clear();
            attributionByBone.Clear();
        }

        /// <summary>A still with a red dot at each counted neck/shoulder ray, so a person can see what was counted.</summary>
        IEnumerator StillDots(string path, int w, int h, float aspect, List<(float nx, float ny, string bone, string weights)> hits)
        {
            var cam = player.ViewCamera;
            var rt = new RenderTexture(w, h, 24);
            var old = cam.targetTexture;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                cam.aspect = aspect;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                if (hits != null)
                    foreach (var hit in hits)
                    {
                        float ndcX = hit.nx * (16f / 9f) / aspect;   // the ray grid is in 16:9 tangent space
                        int px = Mathf.RoundToInt((ndcX + 1f) * 0.5f * (w - 1)), py = Mathf.RoundToInt((hit.ny + 1f) * 0.5f * (h - 1));
                        for (int dx = -5; dx <= 5; dx++)
                            for (int dy = -5; dy <= 5; dy++)
                            {
                                int x = px + dx, y = py + dy;
                                if (x >= 0 && x < w && y >= 0 && y < h) tex.SetPixel(x, y, Color.red);
                            }
                    }
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = old;
                cam.ResetAspect();
                RenderTexture.active = null;
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
            yield return null;
        }

        /// <summary>The look-down offset eases through stance changes: no pop of the view body at +85 mid-transition (director amend3).</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator ViewBody_DoesNotPopThroughStanceChanges_AtPlus85()
        {
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsTrue(view?.FirstPerson != null && view.FirstPerson.HasViewBody, "no view body");
            var fp = view.FirstPerson;
            player.SetLook(player.Yaw, 85f);
            yield return new WaitForSeconds(0.5f);
            float worst = 0f;
            Vector3 last = fp.ViewRoot.localPosition;
            foreach (var key in new[] { StanceKey.Crouch, StanceKey.Crouch, StanceKey.Squat, StanceKey.Squat, StanceKey.Sit, StanceKey.Sit })
            {
                player.PressStanceKey(key);
                float t = 0f;
                do
                {
                    yield return null;
                    t += Time.deltaTime;
                    Vector3 p = fp.ViewRoot.localPosition;
                    worst = Mathf.Max(worst, (p - last).magnitude);
                    last = p;
                } while (player.StanceTransitioning && t < 4f);
            }
            Debug.Log($"LookDownTests: largest per-frame view-body step at +85 across stance changes: {worst:F4} m");
            Assert.Less(worst, 0.03f, "the view body popped between frames");
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
