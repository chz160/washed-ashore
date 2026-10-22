using System.Collections;
using System.Collections.Generic;
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

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// Director amend7 (view-only arm cap), td's checks: the VIEW body (capped arms) and the SHADOW body (full swing) share one drive. Over a scripted 60 s run through every
    /// gait, the stance changes and a jump, every non-arm bone of the view body, measured in its body's own root frame (so the look-down and back offsets drop out), is
    /// within 1 mm of the shadow's, every frame, sampled after the pose copy; the sync guard resyncs 0 times. The arm chains (Shoulder.* and below) are excluded: they are the
    /// point of the cap. Also: one debug-camera style still of the shadow with the full swing, for Sprint 8 and Jog 5.
    /// </summary>
    public class ViewShadowSyncTests : StanceFixture
    {
        static string OutDir => RunOutput.Dir("viewsync");
        const float ToleranceMetres = 0.001f;

        [DefaultExecutionOrder(10000)]
        class BodySyncSampler : MonoBehaviour
        {
            public Transform shadowRoot, viewRoot;
            public readonly List<(Transform shadow, Transform view, string name)> pairs = new List<(Transform, Transform, string)>();
            public string label = "";
            public float worst;
            public string worstBone = "";
            public string worstLabel = "";
            public int frames;
            public readonly Dictionary<string, float> worstByLabel = new Dictionary<string, float>();
            public Animator shadowAnim, viewAnim;
            public int skippedFrames, longestSkipFrames, currentSkip;
            public float longestSkipSeconds, currentSkipSeconds;

            void LateUpdate()
            {
                frames++;
                // CheckSync skips frames where either Animator is mid-transition; count them and the longest unbroken skip so a stuck transition cannot hide drift.
                bool skipped = shadowAnim != null && viewAnim != null && (shadowAnim.IsInTransition(0) || viewAnim.IsInTransition(0));
                if (skipped) { skippedFrames++; currentSkip++; currentSkipSeconds += Time.deltaTime; if (currentSkip > longestSkipFrames) longestSkipFrames = currentSkip; if (currentSkipSeconds > longestSkipSeconds) longestSkipSeconds = currentSkipSeconds; }
                else { currentSkip = 0; currentSkipSeconds = 0f; }
                float scale = shadowRoot.lossyScale.x;
                float frameWorst = 0f;
                foreach (var pr in pairs)
                {
                    Vector3 a = shadowRoot.InverseTransformPoint(pr.shadow.position);
                    Vector3 b = viewRoot.InverseTransformPoint(pr.view.position);
                    float d = (a - b).magnitude * scale;
                    if (d > frameWorst) frameWorst = d;
                    if (d > worst) { worst = d; worstBone = pr.name; worstLabel = label; }
                }
                worstByLabel[label] = worstByLabel.TryGetValue(label, out float w) ? Mathf.Max(w, frameWorst) : frameWorst;
            }
        }

        static bool IsArm(Transform t, Transform root)
        {
            for (var x = t; x != null && x != root; x = x.parent)
                if (x.name.StartsWith("Shoulder")) return true;
            return false;
        }

        PlayerCharacterView view;
        Vector3 start;
        string pairCountLine = "";

        void Restart() { Place(start, 0f); }

        IEnumerator Segment(BodySyncSampler sampler, string label, float seconds, params Key[] keys)
        {
            sampler.label = label;
            if (keys.Length > 0) InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return new WaitForSeconds(seconds);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }

        IEnumerator Tap(Key key)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        }

        [UnityTest, Timeout(1800000)]
        public IEnumerator NonArmBones_ViewMatchesShadow_Within1mm_OverA60sMixedRun_NoResyncs()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.ViewCopy, "no view copy");
            Directory.CreateDirectory(OutDir);
            start = new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z - 27f);
            Place(start, 0f);
            yield return new WaitForSeconds(0.6f);

            var sampler = player.gameObject.AddComponent<BodySyncSampler>();
            sampler.shadowRoot = view.Body.transform;
            sampler.viewRoot = view.ViewCopy.Body.transform;
            sampler.shadowAnim = view.Body.Animator;
            sampler.viewAnim = view.ViewCopy.ViewAnimator;
            foreach (var kv in view.Body.Bones)
            {
                var vb = view.ViewCopy.Body.Bone(kv.Key);
                if (vb == null || IsArm(kv.Value, sampler.shadowRoot) || IsArm(vb, sampler.viewRoot)) continue;
                sampler.pairs.Add((kv.Value, vb, kv.Key));
            }
            // The pair count is computed from the skeleton, not hard-coded: every shadow bone that is not Shoulder.L/R or below must have a view twin and be paired.
            int skeletonBones = view.Body.Bones.Count, armBones = 0, nonArmBones = 0;
            foreach (var kv in view.Body.Bones) { if (IsArm(kv.Value, sampler.shadowRoot)) armBones++; else nonArmBones++; }
            string bodyType = view.Body.Appearance.bodyType.ToString();
            pairCountLine = $"{bodyType}: skeleton {skeletonBones} bones, {armBones} in the Shoulder.L/R subtrees, {nonArmBones} outside them, {sampler.pairs.Count} paired";
            Debug.Log("ViewShadowSyncTests: " + pairCountLine);
            Assert.AreEqual(nonArmBones, sampler.pairs.Count, "every non-arm bone of the skeleton must be paired with its view twin (" + pairCountLine + ")");
            Assert.Greater(sampler.pairs.Count, 0, "no bone pairs");
            float started = Time.time;

            // Gaits, held by the controller's own input.
            yield return Segment(sampler, "idle", 3f);
            Restart(); player.ExternalMoveInput = Vector2.up * (1.15f / player.WalkSpeed); yield return Segment(sampler, "Walk 1.15", 3f); player.ExternalMoveInput = null;
            Restart(); player.ExternalMoveInput = Vector2.up * (1.5f / player.WalkSpeed); yield return Segment(sampler, "Walk 1.5", 3f); player.ExternalMoveInput = null;
            Restart(); yield return Segment(sampler, "Jog 5", 5f, Key.W);
            Restart(); yield return Segment(sampler, "Sprint 8", 5f, Key.W, Key.LeftShift);
            Restart(); yield return Segment(sampler, "strafe left", 3f, Key.A);
            Restart(); yield return Segment(sampler, "strafe right", 3f, Key.D);
            Restart(); yield return Segment(sampler, "back", 3f, Key.S);
            Restart(); yield return Segment(sampler, "Jog 5 again", 5f, Key.W);
            // Stance changes: crouch, crouch walk, stop, sit, sleep, wake.
            sampler.label = "crouch";
            yield return Tap(Key.LeftCtrl);
            yield return new WaitForSeconds(1.2f);
            Restart(); yield return Segment(sampler, "CrouchWalk", 4f, Key.W);
            yield return Segment(sampler, "stop", 1.0f);
            sampler.label = "sit";
            yield return Tap(Key.X);
            yield return new WaitForSeconds(2.0f);
            sampler.label = "sleep";
            yield return Tap(Key.Z);
            yield return new WaitForSeconds(4.5f);
            sampler.label = "wake";
            yield return Segment(sampler, "wake", 2.5f, Key.W);
            yield return new WaitForSeconds(1.0f);
            if (player.Stance != Stance.Stand) { sampler.label = "stand"; yield return Tap(Key.LeftCtrl); yield return new WaitForSeconds(1.5f); }
            // Sprint into the jumps.
            Restart(); yield return Segment(sampler, "Sprint 8 again", 5f, Key.W, Key.LeftShift);
            for (int j = 0; j < 3; j++)
            {
                sampler.label = $"jump {j}";
                Restart();
                yield return new WaitForSeconds(0.6f);
                yield return Tap(Key.Space);
                yield return new WaitForSeconds(1.2f);
            }
            sampler.label = "end";
            yield return new WaitForSeconds(3f);   // margin so the run is at least 60 s of game time
            float elapsed = Time.time - started;
            int resyncs = view.ViewCopy.Resyncs;
            sampler.label = "end";

            var sb = new StringBuilder();
            sb.AppendLine(pairCountLine);
            sb.AppendLine($"run length {elapsed:F1} s of game time, {sampler.frames} frames, {sampler.pairs.Count} non-arm bone pairs");
            sb.AppendLine($"worst non-arm bone difference {sampler.worst * 1000f:F3} mm at '{sampler.worstBone}' during '{sampler.worstLabel}' (limit {ToleranceMetres * 1000f:F1} mm)");
            foreach (var kv in sampler.worstByLabel) sb.AppendLine($"  {kv.Key}: {kv.Value * 1000f:F3} mm");
            sb.AppendLine($"sync-guard resyncs: {resyncs}");
            sb.AppendLine($"frames CheckSync skips (either Animator mid-transition): {sampler.skippedFrames} of {sampler.frames}; longest unbroken skip {sampler.longestSkipFrames} frames = {sampler.longestSkipSeconds:F2} s");
            File.WriteAllText($"{OutDir}/viewsync.txt", sb.ToString());
            Debug.Log("ViewShadowSyncTests:\n" + sb);
            Object.Destroy(sampler);
            Assert.GreaterOrEqual(elapsed, 55f, "the scripted run was shorter than 55 s of game time (td: a setup floor, so the 1 mm, resync and skip asserts below execute)");
            Assert.LessOrEqual(sampler.worst, ToleranceMetres, $"non-arm bones differ by {sampler.worst * 1000f:F3} mm at '{sampler.worstBone}' during '{sampler.worstLabel}'");
            Assert.AreEqual(0, resyncs, "the sync guard had to resync the view Animator");
            Assert.Less(sampler.longestSkipSeconds, 3.0f, "a transition stayed unbroken long enough to hide drift from the sync guard");
        }

        /// <summary>One debug-camera style still of the SHADOW (full swing) mid-stride, for Sprint 8 and Jog 5, at the debug camera's offset behind and to the right; the shadow body is shown, the view body hidden.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator DebugCamera_FullSwing_Stills()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Directory.CreateDirectory(OutDir);
            var fp = view.FirstPerson;
            var camGo = new GameObject("DebugStillCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(player.ViewCamera);
            foreach (var g in new[] { ("Sprint8", new[] { Key.W, Key.LeftShift }), ("Jog5", new[] { Key.W }) })
            {
                Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z - 27f), 0f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(g.Item2));
                yield return new WaitForSeconds(1.2f);
                fp.SetHeadVisible(true);   // the debug view: the shadow body drawn, the view body hidden
                var t = player.transform;
                camGo.transform.SetPositionAndRotation(t.TransformPoint(new Vector3(2.0f, 1.2f, -0.2f)), Quaternion.LookRotation((t.position + Vector3.up * 1.0f) - t.TransformPoint(new Vector3(2.0f, 1.2f, -0.2f)), Vector3.up));
                var rt = new RenderTexture(960, 540, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                tex.Apply();
                File.WriteAllBytes($"{OutDir}/debugcam_{g.Item1}_full_swing_side.png", tex.EncodeToPNG());
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
                fp.SetHeadVisible(false);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return new WaitForSeconds(0.5f);
            }
            Object.Destroy(camGo);
        }

        /// <summary>
        /// Director's condition: a debug-camera SIDE still of the full-swing Sprint 8 at mid-stance (lowest Hips of the cycle) and mid-flight (highest), from the right and the left,
        /// the shadow body drawn and the view body hidden. The phases are picked by the shadow Hips height over a 1.5 s sample, then captured on the frames within 5 mm of each extreme.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator DebugCamera_Sprint_SideStills_MidStanceAndMidFlight()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Directory.CreateDirectory(OutDir);
            var fp = view.FirstPerson;
            var hips = view.Body.Bone("Hips");
            var camGo = new GameObject("DebugSideStillCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(player.ViewCamera);
            Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z - 27f), 0f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
            yield return new WaitForSeconds(1.2f);
            float floorTop = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
            float lo = float.MaxValue, hi = float.MinValue, t = 0f;
            while (t < 1.5f) { yield return null; t += Time.deltaTime; float h = hips.position.y - floorTop; lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); }
            fp.SetHeadVisible(true);
            bool stance = false, flight = false;
            var lines = new StringBuilder();
            t = 0f;
            while (t < 4f && !(stance && flight))
            {
                yield return null;
                t += Time.deltaTime;
                float h = hips.position.y - floorTop;
                bool atLow = !stance && h <= lo + 0.005f, atHigh = !flight && h >= hi - 0.005f;
                if (!atLow && !atHigh) continue;
                string phase = atLow ? "midstance" : "midflight";
                foreach (var side in new[] { ("right", 2.0f), ("left", -2.0f) })
                {
                    var tr = player.transform;
                    Vector3 at = tr.TransformPoint(new Vector3(side.Item2, 1.2f, -0.2f));
                    camGo.transform.SetPositionAndRotation(at, Quaternion.LookRotation((tr.position + Vector3.up * 1.0f) - at, Vector3.up));
                    var rt = new RenderTexture(960, 540, 24);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes($"{OutDir}/debugcam_Sprint8_{phase}_side_{side.Item1}.png", tex.EncodeToPNG());
                    RenderTexture.active = null;
                    cam.targetTexture = null;
                    Object.Destroy(tex);
                    rt.Release();
                    Object.Destroy(rt);
                }
                lines.AppendLine($"{phase}: Hips {h:F3} m above the floor (cycle range {lo:F3} to {hi:F3}), both sides saved");
                lines.AppendLine($"  clips at capture: shadow Animator [{ClipNames(view.Body.Animator)}]; view Animator [{ClipNames(view.ViewCopy != null ? view.ViewCopy.ViewAnimator : null)}]");
                if (atLow) stance = true; else flight = true;
            }
            fp.SetHeadVisible(false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Object.Destroy(camGo);
            File.WriteAllText($"{OutDir}/debugcam_Sprint8_side_phases.txt", lines.ToString());
            Debug.Log("ViewShadowSyncTests side stills: " + lines);
            Assert.IsTrue(stance && flight, "did not catch both phases: " + lines);
        }

        /// <summary>The clips an Animator is playing on layer 0 right now, with blend weights, plus the next state's clips during a transition; controller name first.</summary>
        static string ClipNames(Animator a)
        {
            if (a == null) return "no animator";
            var sb = new StringBuilder(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name + ": " : "no controller: ");
            foreach (var ci in a.GetCurrentAnimatorClipInfo(0)) sb.Append($"{ci.clip.name} x{ci.weight:F2}; ");
            if (a.IsInTransition(0)) foreach (var ci in a.GetNextAnimatorClipInfo(0)) sb.Append($"next {ci.clip.name} x{ci.weight:F2}; ");
            return sb.ToString().TrimEnd(' ', ';');
        }

        /// <summary>One-frame dump for a pose-bounds failure: every body renderer's bounds, and each arm bone's position with its distance outside the union of those bounds.</summary>
        static string BoundsDump(string tag, List<Transform> arm, IEnumerable<SkinnedMeshRenderer> rends)
        {
            var sb = new StringBuilder($"  [{tag}] frame {Time.frameCount}\n");
            var rl = new List<SkinnedMeshRenderer>(rends);
            foreach (var r in rl) sb.AppendLine($"    bounds {r.name}: center {r.bounds.center:F4} extents {r.bounds.extents:F4} updateWhenOffscreen {r.updateWhenOffscreen}");
            foreach (var bn in arm)
            {
                float best = float.MaxValue;
                foreach (var r in rl) best = Mathf.Min(best, Mathf.Sqrt(r.bounds.SqrDistance(bn.position)));
                if (best > 0.0001f) sb.AppendLine($"    bone {bn.name} at {bn.position:F4}, {best * 1000f:F2} mm outside");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Pose bounds (producer-2, #23b): with updateWhenOffscreen off, a skinned mesh whose arm swings outside its bounds gets culled. During steady Sprint 8 and Jog 5 (2.5 s each)
        /// every frame, after LateUpdate, each arm bone from the hand down (Hand, fingers, thumb) of the SHADOW body must lie inside the union of the shadow body-slot renderers' bounds.
        /// Prints the frames and the worst distance outside; any frame outside fails.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator PoseBounds_ShadowHandsInsideRendererBounds()
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Directory.CreateDirectory(OutDir);
            var arm = new List<Transform>();
            foreach (var kv in view.Body.Bones)
                if (kv.Key.StartsWith("Hand") || kv.Key.Contains("Index") || kv.Key.Contains("Middle") || kv.Key.Contains("Ring") || kv.Key.Contains("Pinky") || kv.Key.Contains("Thumb")) arm.Add(kv.Value);
            // The hand bones are Wrist.L and Wrist.R in this rig. A name that matches nothing must fail the test, not shrink it (td, QA report line 271).
            foreach (var wn in new[] { "Wrist.L", "Wrist.R" })
            {
                var wb = view.Body.Bone(wn);
                Assert.IsNotNull(wb, "no bone named exactly '" + wn + "' on the shadow body");
                if (!arm.Contains(wb)) arm.Add(wb);
            }
            Assert.IsTrue(arm.Contains(view.Body.Bone("Wrist.L")) && arm.Contains(view.Body.Bone("Wrist.R")), "Wrist.L and Wrist.R must both be in the arm list");
            Assert.Greater(arm.Count, 2, "no hand bones found");
            var lines = new StringBuilder();
            var fails = new List<string>();
            foreach (var g in new[] { ("Sprint8", new[] { Key.W, Key.LeftShift }), ("Jog5", new[] { Key.W }) })
            {
                Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z - 27f), 0f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(g.Item2));
                yield return new WaitForSeconds(1.2f);
                int frames = 0, bad = 0;
                float worst = 0f;
                string worstBone = "";
                float t = 0f;
                bool dumpNext = false;
                int dumps = 0;
                while (t < 2.5f)
                {
                    yield return null;
                    t += Time.deltaTime;
                    frames++;
                    var rends = view.Body.Renderers(CharacterSlot.Body);
                    bool frameBad = false;
                    foreach (var b in arm)
                    {
                        float best = float.MaxValue;
                        foreach (var r in rends) best = Mathf.Min(best, Mathf.Sqrt(r.bounds.SqrDistance(b.position)));
                        if (best > 0.0001f) { frameBad = true; if (best > worst) { worst = best; worstBone = b.name; } }
                    }
                    if (frameBad) bad++;
                    if (dumpNext) { lines.AppendLine(BoundsDump(g.Item1 + " NEXT frame after a failing frame" + (frameBad ? " (still outside)" : " (gap closed)"), arm, rends)); dumpNext = false; }
                    if (frameBad && dumps < 3) { lines.AppendLine(BoundsDump(g.Item1 + " FAILING frame", arm, rends)); dumps++; dumpNext = true; }
                }
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                string line = $"{g.Item1}: {frames} frames, {bad} with an arm bone outside the body renderers' bounds, worst {worst * 1000f:F1} mm at '{worstBone}'";
                lines.AppendLine(line);
                if (bad > 0) fails.Add(line);
                yield return new WaitForSeconds(0.5f);
            }
            File.WriteAllText($"{OutDir}/pose-bounds.txt", lines.ToString());
            Debug.Log("ViewShadowSyncTests pose bounds: " + lines);
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }
    }
}
