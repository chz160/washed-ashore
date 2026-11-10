using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
    /// PRINT ONLY, [Explicit] (director-49 gate/pod49-walk-jog-bar2-to-noah-note2-qa-cells, qa-49 12jj): first-person stills of the Stand gait cells whose Bar 2 (torso in the bottom band) fails, for Noah: Walk1.15 +60 and +65, Walk1.5 +70, Jog5 +70, +75 and +80,
    /// Sprint8 +70, plus the amend8g carry-over cell Jog5 +60 in Noah's question (producer-49; Sprint8 +60 was dropped from the rows, td B20h: the director's eight rows). Each gait is walked the way the gate walks it (Place at 25 m behind the floor centre, 0.3 s, the speed cap or keys, 0.9 s, then eight cycle points at 0.1 + 0.11 k s with the pitch set and two frames); at every cycle point the bottom-band Bar 2 measure of the gate is taken
    /// on the same frame (the same 32x18 ray grid, MirrorTie winner, torso = Chest/Abdomen rays at row -0.6 or lower, legs, hand rays). A frame is a representative FAILING example only when it is a SETTLED gait frame by the gate's rule (speed within 5 percent of the target, the right dominant clip, grounded, stance settled, inside the floor; each frame prints the speed against the target and the dominant clip) and torsoBottomBand is 0 and legs is above 0; a frame with torsoBottomBand above 0 is NOT one and the next cycle
    /// point is tried (qa's rule; cycle points shift between runs). For each pitch the first failing frame is kept as an unmarked still and a marked copy (red line at the top of the bottom band, 20 percent of the frame height). Writes runs\\RUN\\bar2stills: the pngs and bar2-stills-index.txt
    /// with, per still, torsoBottomBand, legs, handRays, the view animator clip and normalized time, the camera, the scene light, FOV and eye height. The scene's own light, FOV and eye height are the live ones (the same camera the gate uses). Nothing is asserted.
    /// </summary>
    public class Bar2GaitStills : StanceFixture
    {
        const int GridX = 32, GridY = 18;
        const float FloorHalf = 30f, FloorMarginMin = 2f, SpeedTolerance = 0.05f;   // as LookDownTests :30
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(float v) => v.ToString("R", Inv);
        static string V3(Vector3 v) => F(v.x) + ";" + F(v.y) + ";" + F(v.z);

        enum Cat { Other, NeckShoulder, Torso, Legs, Hand }
        struct Tri { public Vector3 a, b, c; public Cat cat; }

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
                    tris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]], cat = Classify(bone) });
                }
            }
            Object.Destroy(mesh);
            return tris;
        }

        static bool RayHit(Vector3 o, Vector3 d, Tri tri, out float t, out bool back)
        {
            t = 0f; back = false;
            Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a, p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 sv = o - tri.a;
            float u = Vector3.Dot(sv, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(sv, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            if (t < 0f) return false;
            back = Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f;
            return true;
        }

        /// <summary>The gate's Bar 2 measure on one frame: torso rays in the bottom 20 percent band (ny at or below -0.6), legs, hand rays (the winner is the nearest hit under MirrorTie, as in LookDownTests.Measure).</summary>
        static (int torsoBottomBand, int legs, int handRays) MeasureBar2(Camera cam, List<Tri> tris)
        {
            int band = 0, legs = 0, hands = 0;
            Transform ct = cam.transform;
            Vector3 o = ct.position;
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfH = halfV * (16f / 9f);
            for (int iy = 0; iy < GridY; iy++)
                for (int ix = 0; ix < GridX; ix++)
                {
                    float x = (ix / (float)(GridX - 1) * 2f - 1f) * halfH, ny = iy / (float)(GridY - 1) * 2f - 1f, y = ny * halfV;
                    Vector3 d = (ct.forward + ct.right * x + ct.up * y).normalized;
                    float best = float.MaxValue; bool backFace = false; Tri bestTri = default;
                    foreach (var tri in tris)
                    {
                        if (!RayHit(o, d, tri, out float t, out bool back)) continue;
                        if (MirrorTie.Replaces(best < float.MaxValue, best, backFace, bestTri.a, bestTri.b, bestTri.c, t, back, tri.a, tri.b, tri.c)) { best = t; backFace = back; bestTri = tri; }
                    }
                    if (best == float.MaxValue) continue;
                    if (bestTri.cat == Cat.Legs) legs++;
                    if (bestTri.cat == Cat.Hand) hands++;
                    if (bestTri.cat == Cat.Torso && ny <= -0.6f) band++;
                }
            return (band, legs, hands);
        }

        static Texture2D Render(Camera c, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24);
            var old = c.targetTexture;
            try
            {
                c.targetTexture = rt;
                c.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex;
            }
            finally
            {
                c.targetTexture = old;
                RenderTexture.active = null;
                rt.Release();
                Object.Destroy(rt);
            }
        }

        /// <summary>
        /// The gate's settled-gait rule (LookDownTests.Settled and GaitCheck, td-49 B20d): the stance is settled (blend 1, not transitioning), the pivot eye is 1.65 +-0.005, the dominant clip of the base layer is the gait's family, the controller's horizontal speed is within
        /// 5 percent of the target (zero for none), the controller is grounded with AirTime at most 0.05, and the player is at least 2 m inside the test floor. Returns null when the frame counts, else the reasons; diag prints the same numbers.
        /// </summary>
        string SettledGait(float target, string clipToken, out string diag)
        {
            var bad = new List<string>();
            float eye = player.ViewCamera.transform.position.y - player.transform.position.y;
            if (player.Stance != Stance.Stand) bad.Add($"stance is {player.Stance}, expected Stand");
            if (player.StanceTransitioning || player.StanceBlend < 0.999f) bad.Add($"stance not settled (transitioning {player.StanceTransitioning}, blend {player.StanceBlend:F2})");
            if (Mathf.Abs(eye - 1.65f) > 0.005f) bad.Add($"pivot eye {eye:F3} is not 1.65 +-0.005");
            string dominant = "";
            float w = -1f;
            var animator = player.GetComponent<PlayerCharacterView>().Body.Animator;
            foreach (var ci in animator.GetCurrentAnimatorClipInfo(0)) if (ci.weight > w) { w = ci.weight; dominant = ci.clip.name; }
            if (dominant.IndexOf(clipToken, System.StringComparison.OrdinalIgnoreCase) < 0) bad.Add($"dominant clip is '{dominant}' (weight {w:F2}), expected a {clipToken} clip");
            float v = new Vector2(body.velocity.x, body.velocity.z).magnitude;
            float air = animator.GetFloat("AirTime");
            bool grounded = player.IsGrounded && air <= 0.05f;
            Vector3 pp = player.transform.position, c = floor.transform.position;
            float margin = Mathf.Min(FloorHalf - Mathf.Abs(pp.x - c.x), FloorHalf - Mathf.Abs(pp.z - c.z));
            if (!grounded) bad.Add($"not grounded (controller {player.IsGrounded}, AirTime {air:F2})");
            if (target > 0f ? Mathf.Abs(v - target) > SpeedTolerance * target : v > 0.05f) bad.Add($"speed {v:F2} m/s is not within 5% of {target:F2}");
            if (margin < FloorMarginMin) bad.Add($"{margin:F1} m from the floor edge (need {FloorMarginMin:F0})");
            diag = $"eye {eye:F3} blend {player.StanceBlend:F2} clip '{dominant}' w {w:F2} grounded {grounded} speed {v:F2} (target {target:F2}) floor margin {margin:F1} m";
            return bad.Count == 0 ? null : string.Join("; ", bad);
        }

        struct GaitRow { public string label; public float target, cap; public Key[] keys; public float[] pitches; }

        [UnityTest, Explicit("Print only: first-person stills of the Stand gait cells whose Bar 2 fails, for Noah (qa 12jj rows)"), Timeout(1800000)]
        public IEnumerator Bar2Stills_QaSevenRows()
        {
            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.FirstPerson, "no first-person body on the Player");
            var fp = view.FirstPerson;
            Assert.AreEqual(Stance.Stand, player.Stance, "the gait cells are Stand cells");
            string dir = RunOutput.Dir("bar2stills");
            var index = new StringBuilder("# " + MirrorTie.StateLine + System.Environment.NewLine);
            var rows = new[]
            {
                new GaitRow { label = "Walk1.15", target = 1.15f, cap = 1.15f, keys = new Key[0], pitches = new[] { 60f, 65f } },
                new GaitRow { label = "Walk1.5", target = 1.5f, cap = 1.5f, keys = new Key[0], pitches = new[] { 70f } },
                new GaitRow { label = "Jog5", target = 5f, cap = 0f, keys = new[] { Key.W }, pitches = new[] { 60f, 70f, 75f, 80f } },
                new GaitRow { label = "Sprint8", target = 8f, cap = 0f, keys = new[] { Key.W, Key.LeftShift }, pitches = new[] { 70f } },
            };
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            var cam = player.ViewCamera;
            Light sun = null;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            index.AppendLine($"light {(sun != null ? "euler " + V3(sun.transform.eulerAngles) + "; intensity " + F(sun.intensity) : "none")}; FOV {F(cam.fieldOfView)}");
            foreach (var row in rows)
            {
                var found = new HashSet<float>();
                Place(floorCentre - Vector3.forward * 25f, 0f);
                yield return new WaitForSeconds(0.3f);
                if (row.cap > 0f) player.ExternalMoveInput = Vector2.up * (row.cap / player.WalkSpeed);
                else InputSystem.QueueStateEvent(keyboard, new KeyboardState(row.keys));
                yield return new WaitForSeconds(0.9f);
                for (int round = 0; round < 2 && found.Count < row.pitches.Length; round++)
                    for (int k = 0; k < 8 && found.Count < row.pitches.Length; k++)
                    {
                        Place(floorCentre - Vector3.forward * 25f, 0f);
                        yield return new WaitForSeconds(0.1f + 0.11f * k);
                        foreach (float pitch in row.pitches)
                        {
                            if (found.Contains(pitch)) continue;
                            player.SetLook(player.Yaw, pitch);
                            Place(floorCentre - Vector3.forward * 25f, 0f);
                            yield return null;
                            yield return null;
                            // same frame from here: measure, then render
                            var m = MeasureBar2(cam, Bake(fp));
                            var va = view.ViewCopy != null && view.ViewCopy.ViewAnimator != null ? view.ViewCopy.ViewAnimator : view.Body.Animator;
                            var info = va.GetCurrentAnimatorStateInfo(0);
                            string clip = va.GetCurrentAnimatorClipInfo(0).OrderByDescending(c => c.weight).Select(c => c.clip.name).FirstOrDefault() ?? "";
                            string head = $"{row.label} +{pitch:F0} round {round} cycle point {k}: torsoBottomBand {m.torsoBottomBand}, legs {m.legs}, handRays {m.handRays}, view clip {clip}, normalizedTime {F(info.normalizedTime)}";
                            string clipToken = row.label.StartsWith("Walk") ? "Walk" : row.label.StartsWith("Jog") ? "Jog" : "Sprint";
                            string notSettled = SettledGait(row.target, clipToken, out string settledDiag);
                            head += $"; {settledDiag}";
                            if (notSettled != null) { index.AppendLine($"  retake (NOT A SETTLED GAIT FRAME: {notSettled}): " + head); continue; }
                            if (m.torsoBottomBand != 0 || m.legs == 0) { index.AppendLine("  retake (not a failing example): " + head); continue; }
                            var tex = Render(cam, 960, 540);
                            string name = $"{row.label}_p{pitch:+0;-0;0}_r{round}cp{k}";
                            File.WriteAllBytes($"{dir}/{name}.png", tex.EncodeToPNG());
                            int bandRow = Mathf.RoundToInt(0.2f * 540f);   // rows from the bottom: the band is 20 percent of the frame height
                            for (int t = 0; t < 3; t++) for (int x = 0; x < tex.width; x++) tex.SetPixel(x, bandRow + t, Color.red);
                            tex.Apply();
                            File.WriteAllBytes($"{dir}/{name}.marked.png", tex.EncodeToPNG());
                            Object.Destroy(tex);
                            found.Add(pitch);
                            var ct = cam.transform;
                            index.AppendLine($"STILL {name}.png (+ .marked.png, red line = top of the bottom band): " + head);
                            index.AppendLine($"  camera position {V3(ct.position)}; euler {V3(ct.eulerAngles)}; FOV {F(cam.fieldOfView)}; eye height above the player origin {F(ct.position.y - player.transform.position.y)}; speed target {F(row.target)}");
                        }
                    }
                foreach (float pitch in row.pitches) if (!found.Contains(pitch)) index.AppendLine($"NO FAILING FRAME in 16 cycle points for {row.label} +{pitch:F0} (every sampled frame had torsoBottomBand above 0, no legs, or was not a settled gait frame)");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                player.ExternalMoveInput = null;
                yield return new WaitForSeconds(0.6f);
            }
            File.WriteAllText($"{dir}/bar2-stills-index.txt", index.ToString());
            Debug.Log("Bar2GaitStills written to " + dir);
        }
    }
}
