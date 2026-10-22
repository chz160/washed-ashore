using System.Collections;
using System.Collections.Generic;
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

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// qa-2 independent harness for the director's landing-6 condition (td research.md, Director items 2): the accepted under-2.5 cm
    /// shoulder-height body-in-steel overlap on landing 6 must not be visible now that the player has a view body.
    /// For several standing poses on the landing 6 deck and the pitches 0, +-45, +60, +-85 it renders the player camera three ways:
    ///   with  = the scene with the view body,
    ///   hidden = the same frame with every body renderer off (the twin),
    ///   body  = the view body alone on a magenta clear (its silhouette, drawn with no scene in front of it).
    /// Changed pixels (with vs hidden) are what the body adds; "over" pixels are silhouette pixels where the frame did NOT change,
    /// that is, scene geometry (steel) is drawn in front of the body there. A planted cube is the positive control: the metric must
    /// see it. The worst pose is saved as stills at 16:9 and 21:9. Evidence: TestResults/player-character/landing6/.
    /// The test asserts only that the harness works; qa reads the CSV and the stills for the verdict.
    /// </summary>
    public class QaLanding6StillsTests : StanceFixture
    {
        const string OutDir = "TestResults/player-character/landing6";
        const string DeckName = "COL_CVX_Landing_06";
        const int BodyLayer = 31;
        static readonly float[] Pitches = { 0f, 45f, -45f, 60f, 85f, -85f };
        static readonly float[] Yaws = { 0f, 90f, 180f, 270f };
        static readonly Color32 Clear = new Color32(255, 0, 255, 255);

        struct Metric { public int body, changed, over, total; }

        struct Pose { public Vector3 at; public float yaw; public string name, group; public float top; public int overSum; }

        static int Diff(Color32 a, Color32 b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        Color32[] Render(Camera cam, int w, int h, bool bodyOnly)
        {
            var rt = new RenderTexture(w, h, 24);
            var oldRt = cam.targetTexture;
            int oldMask = cam.cullingMask;
            var oldFlags = cam.clearFlags;
            var oldBg = cam.backgroundColor;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                if (bodyOnly)
                {
                    cam.cullingMask = 1 << BodyLayer;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Clear;
                }
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                return tex.GetPixels32();
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = oldRt;
                cam.cullingMask = oldMask;
                cam.clearFlags = oldFlags;
                cam.backgroundColor = oldBg;
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        static void Save(string path, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        /// <summary>The three renders of one frame: with the body, with it hidden, and the view body alone.</summary>
        void Triple(Camera cam, FirstPersonBody fp, int w, int h, out Color32[] with, out Color32[] hidden, out Color32[] body)
        {
            with = Render(cam, w, h, false);

            var enabled = new List<(Renderer r, bool on)>();
            foreach (var r in fp.BodyRenderers) enabled.Add((r, r.enabled));
            foreach (var r in fp.ViewRenderers) enabled.Add((r, r.enabled));
            foreach (var e in enabled) e.r.enabled = false;
            hidden = Render(cam, w, h, false);
            foreach (var e in enabled) e.r.enabled = e.on;

            var layers = new List<(GameObject go, int layer)>();
            foreach (var r in fp.ViewRenderers) layers.Add((r.gameObject, r.gameObject.layer));
            foreach (var l in layers) l.go.layer = BodyLayer;
            try { body = Render(cam, w, h, true); }
            finally { foreach (var l in layers) l.go.layer = l.layer; }
        }

        static Metric Compare(Color32[] with, Color32[] hidden, Color32[] body, Color32[] mask)
        {
            var m = new Metric { total = with.Length };
            for (int i = 0; i < with.Length; i++)
            {
                bool isBody = Diff(body[i], Clear) > 24;
                bool changed = Diff(with[i], hidden[i]) > 6;
                if (isBody) m.body++;
                if (changed) m.changed++;
                bool over = isBody && !changed;
                if (over) m.over++;
                if (mask != null) mask[i] = over ? new Color32(255, 0, 0, 255) : changed ? new Color32(255, 255, 255, 255) : isBody ? new Color32(0, 120, 255, 255) : new Color32(0, 0, 0, 255);
            }
            return m;
        }

        struct Tri { public Vector3 a, b, c; }

        static List<Tri> BakeView(FirstPersonBody fp)
        {
            var tris = new List<Tri>(40000);
            var mesh = new Mesh();
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled || !(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < t.Length; i += 3) tris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]] });
            }
            Object.Destroy(mesh);
            return tris;
        }

        static float NearestTri(Vector3 o, Vector3 d, List<Tri> tris)
        {
            float best = float.MaxValue;
            foreach (var tri in tris)
            {
                Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a, p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1f / det;
                Vector3 sv = o - tri.a;
                float u = Vector3.Dot(sv, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(sv, e1);
                float v = Vector3.Dot(d, q) * inv;
                if (v < 0f || u + v > 1f) continue;
                float t = Vector3.Dot(e2, q) * inv;
                if (t > 0f && t < best) best = t;
            }
            return best;
        }

        /// <summary>For up to 1500 "over" (red) pixels: the nearest scene collider along the pixel ray (the tower's collision proxies, the player
        /// excluded) against the nearest view-body triangle. d = body distance - scene distance. Buckets: [d &lt; 0 (scene behind the body: proxy
        /// mismatch), 0-0.05 m (scene surface within 5 cm of the body skin: steel through the body), 0.05-0.15, 0.15-0.5, over 0.5 (steel well in front)].</summary>
        int[] DepthBuckets(Camera cam, FirstPersonBody fp, Color32[] mask, int w, int h, List<Tri> tris)
        {
            var buckets = new int[5];
            var reds = new List<int>();
            for (int i = 0; i < mask.Length; i++) if (mask[i].r == 255 && mask[i].g == 0 && mask[i].b == 0) reds.Add(i);
            int step = Mathf.Max(1, reds.Count / 1500);
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), halfH = halfV * (w / (float)h);
            Transform ct = cam.transform;
            for (int k = 0; k < reds.Count; k += step)
            {
                int px = reds[k] % w, py = reds[k] / w;
                float x = ((px + 0.5f) / w * 2f - 1f) * halfH, y = ((py + 0.5f) / h * 2f - 1f) * halfV;
                Vector3 d = (ct.forward + ct.right * x + ct.up * y).normalized;
                float tb = NearestTri(ct.position, d, tris);
                float ts = float.MaxValue;
                foreach (var hit in Physics.RaycastAll(ct.position, d, 20f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.transform.IsChildOf(player.transform)) continue;
                    if (hit.collider.name.StartsWith("WALK_") || (hit.collider.transform.parent != null && hit.collider.transform.parent.name == "WALK_Ramps")) continue;   // the invisible walking ramps are not steel
                    if (hit.distance < ts) ts = hit.distance;
                }
                if (tb == float.MaxValue || ts == float.MaxValue) continue;
                float dd = tb - ts;
                int bkt = dd < 0f ? 0 : dd < 0.05f ? 1 : dd < 0.15f ? 2 : dd < 0.5f ? 3 : 4;
                buckets[bkt]++;
            }
            return buckets;
        }

        IEnumerator PlaceAndSettle(Pose pose, float top, System.Action<bool> ok)
        {
            Place(pose.at, pose.yaw);
            Physics.SyncTransforms();
            for (int i = 0; i < 20; i++) yield return null;
            var p = player.transform.position;
            bool good = player.IsGrounded && Mathf.Abs(p.y - top) < 0.35f && Vector2.Distance(new Vector2(p.x, p.z), new Vector2(pose.at.x, pose.at.z)) < 0.4f;
            ok(good);
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator Landing6_ViewBody_StillsAndTwinDiff()
        {
            yield return LoadWorld();
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsTrue(view != null && view.FirstPerson != null && view.FirstPerson.HasViewBody, "no view body on the Player");
            var fp = view.FirstPerson;
            var cam = player.ViewCamera;
            Assert.IsNotNull(cam, "no view camera");
            Directory.CreateDirectory(OutDir);

            var deck = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == DeckName);
            Assert.IsNotNull(deck, "tower node " + DeckName + " not found");
            var col = deck.GetComponent<Collider>() ?? deck.GetComponentInChildren<Collider>();
            Assert.IsNotNull(col, DeckName + " has no collider");
            Bounds b = col.bounds;
            float top = b.max.y;

            var poses = new List<Pose>();
            // td/director (amend35): the overlap is at flight 6's head and flight 7's foot, on the stair centre line, toward the X-brace's
            // upper diagonal. Poses at both lane ends, four yaws each (lane heading and +90/+180/+270), then a coarse grid on the deck.
            var nodes = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Transform Node(string n) => nodes.FirstOrDefault(t => t.name == n);
            foreach (var (group, endName, otherName) in new[] { ("F06head", "LANE_F06_Head", "LANE_F06_Foot"), ("F07foot", "LANE_F07_Foot", "LANE_F07_Head") })
            {
                var end = Node(endName); var other = Node(otherName);
                Assert.IsTrue(end != null && other != null, $"tower nodes {endName}/{otherName} not found");
                Vector3 d = group == "F06head" ? end.position - other.position : other.position - end.position;
                float heading = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                for (int k = 0; k < 4; k++)
                {
                    float yaw = heading + 90f * k;
                    poses.Add(new Pose { at = end.position + Vector3.up * 0.06f, yaw = yaw, top = end.position.y, group = group, name = $"{group}_y{k * 90}" });
                }
            }
            for (int ix = 0; ix < 3; ix++)
                for (int iz = 0; iz < 3; iz++)
                    foreach (float yaw in Yaws)
                    {
                        float x = Mathf.Lerp(b.min.x + 0.3f, b.max.x - 0.3f, ix / 2f), z = Mathf.Lerp(b.min.z + 0.3f, b.max.z - 0.3f, iz / 2f);
                        poses.Add(new Pose { at = new Vector3(x, top + 0.06f, z), yaw = yaw, top = top, group = "deck", name = $"deck_x{ix}z{iz}y{yaw:F0}" });
                    }

            var csv = new StringBuilder($"# deck {DeckName} bounds min {b.min} max {b.max}; search at 480x270 16:9\npose,x,y,z,yaw,pitch,body_px,changed_px,over_px,total_px\n");
            int valid = 0;
            var bestOf = new Dictionary<string, int>();
            for (int i = 0; i < poses.Count; i++)
            {
                bool ok = false;
                yield return PlaceAndSettle(poses[i], poses[i].top, v => ok = v);
                if (!ok) { csv.AppendLine($"{poses[i].name},skipped (not grounded on the deck or pushed off)"); continue; }
                valid++;
                int sum = 0;
                foreach (float pitch in Pitches)
                {
                    player.SetLook(poses[i].yaw, pitch);
                    yield return null; yield return null; yield return null;
                    Triple(cam, fp, 480, 270, out var w, out var h, out var bd);
                    var m = Compare(w, h, bd, null);
                    sum += m.over;
                    var p = player.transform.position;
                    csv.AppendLine($"{poses[i].name},{p.x:F3},{p.y:F3},{p.z:F3},{poses[i].yaw:F0},{pitch:F0},{m.body},{m.changed},{m.over},{m.total}");
                }
                var pz = poses[i]; pz.overSum = sum; poses[i] = pz;
                if (!bestOf.TryGetValue(poses[i].group, out int bi) || sum > poses[bi].overSum) bestOf[poses[i].group] = i;
            }
            File.WriteAllText($"{OutDir}/search.csv", csv.ToString());
            Assert.Greater(valid, 0, "no standing pose on the landing 6 deck was usable");

            // Stills at the worst pose of each group, both aspects, every pitch.
            var fin = new StringBuilder("pose,aspect,pitch,body_px,changed_px,over_px,total_px\n");
            int body85 = 0, controlPose = -1;
            foreach (var kv in bestOf)
            {
                int best = kv.Value;
                if (controlPose < 0) controlPose = best;
                bool ok2 = false;
                yield return PlaceAndSettle(poses[best], poses[best].top, v => ok2 = v);
                Assert.IsTrue(ok2, "the worst pose could not be re-placed: " + poses[best].name);
                foreach (var aspect in new[] { ("16x9", 960, 540), ("21x9", 1260, 540) })
                    foreach (float pitch in Pitches)
                    {
                        player.SetLook(poses[best].yaw, pitch);
                        yield return null; yield return null; yield return null;
                        Triple(cam, fp, aspect.Item2, aspect.Item3, out var w, out var h, out var bd);
                        var mask = new Color32[w.Length];
                        var m = Compare(w, h, bd, mask);
                        string tag = $"{OutDir}/{poses[best].name}_{aspect.Item1}_p{pitch:+0;-0;0}";
                        Save(tag + "_with.png", w, aspect.Item2, aspect.Item3);
                        Save(tag + "_hidden.png", h, aspect.Item2, aspect.Item3);
                        Save(tag + "_body.png", bd, aspect.Item2, aspect.Item3);
                        Save(tag + "_mask.png", mask, aspect.Item2, aspect.Item3);
                        string depth = "";
                        if (aspect.Item1 == "16x9")
                        {
                            var bk = DepthBuckets(cam, fp, mask, aspect.Item2, aspect.Item3, BakeView(fp));
                            depth = $",depth_behind_body {bk[0]},within_5cm {bk[1]},5_to_15cm {bk[2]},15_to_50cm {bk[3]},over_50cm {bk[4]}";
                        }
                        fin.AppendLine($"{poses[best].name},{aspect.Item1},{pitch:F0},{m.body},{m.changed},{m.over},{m.total}{depth}");
                        if (aspect.Item1 == "16x9" && pitch == 85f) body85 = Mathf.Max(body85, m.body);
                    }
            }
            File.WriteAllText($"{OutDir}/stills.csv", fin.ToString());
            Assert.Greater(body85, 0, "the view body is not visible at +85 from any worst pose; the harness measures nothing");
            var best0 = controlPose;
            bool okc = false;
            yield return PlaceAndSettle(poses[best0], poses[best0].top, v => okc = v);
            Assert.IsTrue(okc, "control pose could not be re-placed");

            // Positive control: a cube planted against the body must be seen as scene geometry in front of it.
            player.SetLook(poses[best0].yaw, 85f);
            yield return null; yield return null; yield return null;
            Triple(cam, fp, 960, 540, out var cw, out var ch, out var cb);
            var baseM = Compare(cw, ch, cb, null);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(cube.GetComponent<Collider>());
            cube.transform.localScale = Vector3.one * 0.3f;
            // On the body itself: at the baked view-body vertex nearest the optical axis (so it is in frame at +85), moved 5 cm toward the camera.
            Vector3 pick = Vector3.zero;
            float bestOff = float.MaxValue;
            Vector3 co = cam.transform.position, cf = cam.transform.forward;
            foreach (var tri in BakeView(fp))
                foreach (var v in new[] { tri.a, tri.b, tri.c })
                {
                    float along = Vector3.Dot(v - co, cf);
                    if (along < 0.3f) continue;
                    float off = Vector3.Cross(v - co, cf).magnitude;
                    if (off < bestOff) { bestOff = off; pick = v; }
                }
            cube.transform.position = pick - cf * 0.05f;
            yield return null; yield return null;
            Triple(cam, fp, 960, 540, out cw, out ch, out cb);
            var withCube = Compare(cw, ch, cb, null);
            Object.Destroy(cube);
            File.AppendAllText($"{OutDir}/stills.csv", $"control,85,base_over {baseM.over},with_cube_over {withCube.over}\n");
            Assert.Greater(withCube.over, baseM.over + 100, $"positive control not seen: over {baseM.over} -> {withCube.over}");
        }

        // ---- qa-2, amend6 (b): Walk 1.15 / 1.5 and Run (Jog 5) hand stills at pitch +30, hand-only share of the 16:9 frame (test only). ----

        static bool IsHandBone(string n, bool withForearm) =>
            n.Contains("Hand") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky") || n.Contains("Thumb") || n.Contains("Wrist")
            || (withForearm && n.Contains("LowerArm"));

        /// <summary>
        /// Pixels the VIEW body's hand triangles (dominant bone Hand, fingers, Wrist; plus LowerArm when withForearm) cover in this camera's frame, drawn alone on a
        /// magenta clear from the baked pose. Triangles are kept when at least 2 of their 3 vertices have a hand dominant bone.
        /// </summary>
        int HandPixels(Camera cam, FirstPersonBody fp, int w, int h, bool withForearm, out Color32[] px)
        {
            var temps = new List<GameObject>();
            try
            {
                foreach (var r in fp.ViewRenderers)
                {
                    if (!r.enabled || !(r is SkinnedMeshRenderer smr) || smr.sharedMesh == null) continue;
                    var baked = new Mesh();
                    smr.BakeMesh(baked, false);
                    var v = baked.vertices; var t = baked.triangles;
                    var weights = smr.sharedMesh.boneWeights; var bones = smr.bones;
                    Vector3 pos = smr.transform.position; Quaternion rot = smr.transform.rotation;
                    bool Hand(int vi)
                    {
                        int bi = weights.Length > vi ? weights[vi].boneIndex0 : -1;
                        return bi >= 0 && bi < bones.Length && bones[bi] != null && IsHandBone(bones[bi].name, withForearm);
                    }
                    var verts = new List<Vector3>(); var idx = new List<int>();
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        int n = (Hand(t[i]) ? 1 : 0) + (Hand(t[i + 1]) ? 1 : 0) + (Hand(t[i + 2]) ? 1 : 0);
                        if (n < 2) continue;
                        for (int k = 0; k < 3; k++) { idx.Add(verts.Count); verts.Add(pos + rot * v[t[i + k]]); }
                    }
                    Object.Destroy(baked);
                    if (idx.Count == 0) continue;
                    var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    m.SetVertices(verts); m.SetTriangles(idx, 0); m.RecalculateNormals(); m.RecalculateBounds();
                    var go = new GameObject("QaHandOnly") { layer = BodyLayer };
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    go.AddComponent<MeshRenderer>().sharedMaterial = smr.sharedMaterial;
                    temps.Add(go);
                }
                px = Render(cam, w, h, true);
                int count = 0;
                for (int i = 0; i < px.Length; i++) if (Diff(px[i], Clear) > 24) count++;
                return count;
            }
            finally { foreach (var g in temps) { var mf = g.GetComponent<MeshFilter>(); if (mf != null) Object.Destroy(mf.sharedMesh); Object.Destroy(g); } }
        }

        /// <summary>One gait at one pitch: 8 phases (hold at least 1 s after one placement, then sample, as LookDownTests.StillCase). peakHandPx gets the most hand pixels over the phases.</summary>
        IEnumerator HandCase(string name, float target, string clipToken, float cap, Key[] keys, float pitch, FirstPersonBody fp, PlayerCharacterView view, StringBuilder csv, List<string> failures, System.Action<int> peakHandPx)
        {
            var cam = player.ViewCamera;
            var floorCentre = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z);
            var dir = RunOutput.Dir("handstills");
            int bestHand = -1, bestPhase = -1;
            for (int ph = 0; ph < 8; ph++)
            {
                Place(floorCentre - Vector3.forward * 27f, 0f);
                if (cap > 0f) player.ExternalMoveInput = Vector2.up * (cap / player.WalkSpeed);
                else InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                yield return new WaitForSeconds(1.0f + 0.071f * ph);
                player.SetLook(player.Yaw, pitch);
                yield return null; yield return null;
                // The 16:9 frame, the hand-only mask (hand + fingers + wrist; and with the forearm) and the body-only silhouette, all from this frame's pose.
                var withPx = Render(cam, 960, 540, false);
                int hand = HandPixels(cam, fp, 960, 540, false, out var handMask);
                int handArm = HandPixels(cam, fp, 960, 540, true, out var armMask);
                var bodyPx = Render(cam, 960, 540, true);
                int bodyCount = 0; for (int i = 0; i < bodyPx.Length; i++) if (Diff(bodyPx[i], Clear) > 24) bodyCount++;
                // Settled: Stand, the gait's clip dominant, grounded, speed within 5%, on the floor.
                float speed = new Vector2(body.velocity.x, body.velocity.z).magnitude;
                string dominant = ""; float w = -1f;
                foreach (var ci in view.Body.Animator.GetCurrentAnimatorClipInfo(0)) if (ci.weight > w) { w = ci.weight; dominant = ci.clip.name; }
                float air = view.Body.Animator.GetFloat("AirTime");
                var bad = new List<string>();
                if (player.Stance != Stance.Stand) bad.Add("stance " + player.Stance);
                if (dominant.IndexOf(clipToken, System.StringComparison.OrdinalIgnoreCase) < 0) bad.Add($"dominant clip '{dominant}' w {w:F2}");
                if (!player.IsGrounded || air > 0.05f) bad.Add($"not grounded (AirTime {air:F2})");
                if (Mathf.Abs(speed - target) > 0.05f * target) bad.Add($"speed {speed:F2} not within 5% of {target:F2}");
                var p = player.transform.position; var c = floor.transform.position;
                if (Mathf.Min(30f - Mathf.Abs(p.x - c.x), 30f - Mathf.Abs(p.z - c.z)) < 2f) bad.Add("near the floor edge");
                string settled = bad.Count == 0 ? "yes" : "NO: " + string.Join("; ", bad);
                if (bad.Count > 0) failures.Add($"{name} phase {ph}: {settled}");
                csv.AppendLine($"{name},{pitch:F0},{ph},\"{settled}\",{dominant},{w:F2},{speed:F2},{hand},{handArm},{bodyCount},{withPx.Length},{100.0 * hand / withPx.Length:F3},{100.0 * handArm / withPx.Length:F3},{100.0 * bodyCount / withPx.Length:F3},{fp.LastLookDownOffset.x:F3}");
                if (hand > bestHand || bestPhase < 0)
                {
                    bestHand = hand; bestPhase = ph;
                    Save($"{dir}/{name}_p{pitch:+0}_16x9_with.png", withPx, 960, 540);
                    Save($"{dir}/{name}_p{pitch:+0}_16x9_handmask.png", armMask, 960, 540);
                    Save($"{dir}/{name}_p{pitch:+0}_16x9_body.png", bodyPx, 960, 540);
                }
                if (cap > 0f) player.ExternalMoveInput = null; else InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            }
            csv.AppendLine($"# {name} +{pitch:F0}: kept phase {bestPhase} (most hand pixels {bestHand})");
            peakHandPx(bestHand);
            yield return new WaitForSeconds(0.4f);
        }

        /// <summary>
        /// amend6 (b): Walk 1.15, Walk 1.5 and Run (Jog 5) at pitch +30, view body, 16:9, at the arm-swing peak (the phase of 8 with the most hand pixels). Reports hand pixels
        /// and hand-plus-forearm pixels as a share of the frame; over about a tenth of the frame goes to the director. Positive control: Jog 5 at +60, where hands are known to be
        /// in frame (hand rays 4-8 at +60 in the waiver stills), must read nonzero.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator HandStills_Walk_Run_Plus30()
        {
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsTrue(view != null && view.FirstPerson != null && view.FirstPerson.HasViewBody, "no view body on the Player");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(RunOutput.Dir("handstills"));
            var csv = new StringBuilder("case,pitch,phase,settled,clip,weight,speed,handPx,handForearmPx,bodyPx,totalPx,hand_pct,handForearm_pct,body_pct,appliedF\n");
            var failures = new List<string>();
            int w115 = 0, w15 = 0, run5 = 0, control = 0;
            yield return HandCase("Walk1.15", 1.15f, "Walk", 1.15f, null, 30f, fp, view, csv, failures, v => w115 = v);
            yield return HandCase("Walk1.5", 1.5f, "Walk", 1.5f, null, 30f, fp, view, csv, failures, v => w15 = v);
            yield return HandCase("Run_Jog5", 5f, "Jog", 0f, new[] { Key.W }, 30f, fp, view, csv, failures, v => run5 = v);
            yield return HandCase("Control_Jog5", 5f, "Jog", 0f, new[] { Key.W }, 60f, fp, view, csv, failures, v => control = v);
            File.WriteAllText($"{RunOutput.Dir("handstills")}/hand-stills.csv", csv.ToString());
            Debug.Log("HandStills\n" + csv);
            Assert.Greater(control, 0, "positive control: the hand-only mask saw no hand pixels at Jog 5 +60 (hands are in frame there); the method is blind");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
