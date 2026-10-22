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
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// Foot slide per plant (design section 4, spec H4: at most 0.10 m) and the playback-rate chain, measured on the SHADOW body (the one with
    /// the Animator) after LateUpdate by a sampler that runs last in the frame. td's order: prove the measurement on known cases first
    /// (Idle at 0 m/s must read about 0; Walk and Run at their exact clip speeds must read 0.03 m or less), then judge Jog, Sprint and the crouch speeds.
    /// A plant is a run of frames where the foot bone is within 3 cm of its lowest height; the slide is the largest horizontal distance inside it.
    /// Rate chain per sample: speed fed in, CharacterClipSet.LocomotionRate, the Animator LocoRate parameter, the state's speed and multiplier,
    /// and the effective playback rate read from normalizedTime advance times clip length over dt. CSVs go to TestResults/player-character/range/.
    /// </summary>
    public class FootSlideTests : StanceFixture
    {
        static string OutDir => RunOutput.Dir("range");
        const string RangeLow = "Assets/Art/Character/CharacterAnimator_Men_RangeLow.overrideController";
        public const float RateMin = 0.7f, RateMax = 1.4f;
        /// <summary>The band edges are inclusive; this only absorbs float rounding (td: 1e-4; 2.50 / 1.786 prints 1.400 and can compare above 1.4f). The 0.7 to 1.4 rule itself is unchanged.</summary>
        public const float RateEdgeTolerance = 0.0001f;
        /// <summary>The slide bar. The forced-bad test lowers it to prove the check can fail.</summary>
        public static float SlideLimit = 0.10f;

        public struct Measured
        {
            public float advancing, slide, meanSlide, plantsShown, speed, expectedRate, locoParam, stateSpeed, effectiveRate;
            public int plants, frames;
        }

        PlayerCharacterView view;

        const string WomenAppearance = "Assets/Art/Character/Appearances/Women_Worker.asset";

        /// <summary>Loads the World; on the women's rig the Player's bodies are rebuilt from Women_Worker (the scene ships Men_Farmer). Both rigs run the same cases.</summary>
        IEnumerator Begin(bool crouchRange = false, bool women = false)
        {
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view?.Body, "no body on the Player");
#if UNITY_EDITOR
            if (women)
            {
                var app = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(WomenAppearance);
                Assert.IsNotNull(app, "no " + WomenAppearance);
                view.RebuildWith(app);
                yield return null;
                yield return null;
                Assert.AreEqual(BodyType.Women, view.Body.Appearance.bodyType, "the rebuild did not give the women's body");
                Assert.IsNotNull(view.ViewCopy, "no view body after the rebuild");
                yield return Settle();
            }
#endif
            Directory.CreateDirectory(OutDir);
            if (crouchRange)
            {
                // The crouch-speed range runs on the Low range controllers: the shadow on its variant, the view on the base-based one.
                var app = view.Body.Appearance;
                view.Body.Animator.runtimeAnimatorController = app.RangeController(false, true);
                if (view.ViewCopy != null && view.ViewCopy.ViewAnimator != null) view.ViewCopy.ViewAnimator.runtimeAnimatorController = app.RangeController(false, false);
                Assert.IsTrue(view.Body.Animator.runtimeAnimatorController.name.Contains("Range"), "the crouch-speed runs must be on a range controller");
            }
            yield return null;
        }

        IEnumerator Measure(string label, float cap, System.Action<Measured> done, params Key[] keys)
        {
            // Start on the side the run travels away from, so the 4 s run (about 32 m at 8 m/s) stays on the 60 m floor. Starting always at z-27 ran the back keys
            // (S) off the floor edge after 0.4 s: the character fell, the foot only dropped, and no plant was found (n3: 0 plants going back). Yaw 0: W = +z, D = +x.
            float dirX = (System.Array.IndexOf(keys, Key.D) >= 0 ? 1f : 0f) - (System.Array.IndexOf(keys, Key.A) >= 0 ? 1f : 0f);
            float dirZ = (System.Array.IndexOf(keys, Key.W) >= 0 ? 1f : 0f) - (System.Array.IndexOf(keys, Key.S) >= 0 ? 1f : 0f);
            if (cap > 0f) { dirX = 0f; dirZ = 1f; }
            Place(new Vector3(floor.transform.position.x - 27f * dirX, floor.transform.position.y + 2.05f, floor.transform.position.z - 27f * dirZ), 0f);
            yield return new WaitForSeconds(0.3f);
            // A held speed comes from the analog move input (the controller scales the walk target by its magnitude); TowerWalkZones rewrites WalkSpeedCap every frame, so a cap cannot hold it.
            if (cap > 0f) player.ExternalMoveInput = Vector2.up * (cap / player.WalkSpeed);
            else if (keys.Length > 0) InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return new WaitForSeconds(1.2f);   // gait settled
            var sampler = player.gameObject.AddComponent<FootSampler>();
            sampler.left = view.Body.Bone("Foot.L");
            sampler.right = view.Body.Bone("Foot.R");
            sampler.animator = view.Body.Animator;
            sampler.recording = true;
            float t = 0f;
            while (t < 2.5f) { yield return null; t += Time.deltaTime; }
            sampler.recording = false;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            player.ExternalMoveInput = null;
            var m = Compute(sampler);
            WriteCsv(label, sampler);
            Object.Destroy(sampler);
            m.speed = Mathf.Max(m.speed, 0f);
            m.expectedRate = CharacterClipSet.LocomotionRate((int)player.Stance, m.speed, view.Body.SkeletonScale);
            Debug.Log($"FootSlideTests[{label}]: speed {m.speed:F3} m/s, slide max {m.slide:F3} m, mean {m.meanSlide:F3} m over {m.plants} plants, LocomotionRate {m.expectedRate:F3} (scale argument {view.Body.SkeletonScale:F3}; with scale 1.0 it would be {CharacterClipSet.LocomotionRate((int)player.Stance, m.speed, 1f):F3}, with the scale squared {CharacterClipSet.LocomotionRate((int)player.Stance, m.speed, view.Body.SkeletonScale * view.Body.SkeletonScale):F3}), Animator LocoRate {m.locoParam:F3}, state speed x multiplier {m.stateSpeed:F3}, effective playback rate {m.effectiveRate:F3}, {m.frames} frames");
            yield return new WaitForSeconds(0.5f);
            done(m);
        }

        static Measured Compute(FootSampler s)
        {
            var m = new Measured { frames = s.l.Count };
            if (m.frames < 5) return m;
            float dist = 0f, time = 0f, sumLoco = 0f, sumState = 0f, adv = 0f, advTime = 0f;
            for (int i = 1; i < m.frames; i++)
            {
                dist += new Vector2(s.root[i].x - s.root[i - 1].x, s.root[i].z - s.root[i - 1].z).magnitude;
                time += s.dts[i];
                sumLoco += s.loco[i];
                sumState += s.stateSpeed[i];
                if (s.hash[i] == s.hash[i - 1] && s.dts[i] > 0f)
                {
                    float dn = s.nt[i] - s.nt[i - 1];
                    if (dn < -0.5f) dn += 1f;   // wrapped
                    if (dn >= 0f && dn < 0.5f) { adv += dn * s.len[i]; advTime += s.dts[i]; }
                }
            }
            m.advancing = LookDownTests.AdvancingRate(s.nt, s.len, s.dts, s.hash, out bool stateChanged);
            if (stateChanged) m.advancing = float.NaN;   // a state change inside a steady-gait window means it was not steady
            m.speed = time > 0f ? dist / time : 0f;
            m.locoParam = sumLoco / (m.frames - 1);
            m.stateSpeed = sumState / (m.frames - 1);
            m.effectiveRate = m.stateSpeed;   // state speed x multiplier; normalizedTime x length / dt reads 1.0 because length already includes the speed
            for (int foot = 0; foot < 2; foot++)
            {
                var pos = foot == 0 ? s.l : s.r;
                float lowest = float.MaxValue;
                foreach (var p in pos) lowest = Mathf.Min(lowest, p.y);
                int start = -1;
                for (int k = 0; k <= pos.Count; k++)
                {
                    bool planted = k < pos.Count && pos[k].y <= lowest + 0.03f;
                    if (planted && start < 0) start = k;
                    if (!planted && start >= 0)
                    {
                        if (k - start >= 3)
                        {
                            float d = 0f;
                            for (int a = start; a < k; a++)
                                for (int b = a + 1; b < k; b++)
                                    d = Mathf.Max(d, new Vector2(pos[a].x - pos[b].x, pos[a].z - pos[b].z).magnitude);
                            m.slide = Mathf.Max(m.slide, d);
                            m.meanSlide += d;
                            m.plants++;
                        }
                        start = -1;
                    }
                }
            }
            if (m.plants > 0) m.meanSlide /= m.plants;
            return m;
        }

        static void WriteCsv(string label, FootSampler s)
        {
            var sb = new StringBuilder("frame,dt,rootX,rootZ,footLy,footRy,locoRate,stateSpeedTimesMultiplier,normalizedTime,length\n");
            for (int i = 0; i < s.l.Count; i += 10)
                sb.AppendLine($"{i},{s.dts[i]:F5},{s.root[i].x:F3},{s.root[i].z:F3},{s.l[i].y:F3},{s.r[i].y:F3},{s.loco[i]:F3},{s.stateSpeed[i]:F3},{s.nt[i]:F3},{s.len[i]:F3}");
            File.WriteAllText($"{OutDir}/ratechain-{label}.csv", sb.ToString());
        }

        /// <summary>The one judging rule: slide within the limit, and (when a rate band applies) the effective rate inside 0.7 to 1.4.</summary>
        public static bool Judge(float slide, float effectiveRate, float limit, bool rateBand) =>
            slide <= limit && (!rateBand || (effectiveRate >= RateMin - RateEdgeTolerance && effectiveRate <= RateMax + RateEdgeTolerance));

        [Test]
        public void Judge_FailsWhatItShouldFail()
        {
            Assert.IsFalse(Judge(0.133f, 0.98f, 0.10f, true), "0.133 m is over the 0.10 bar");
            Assert.IsFalse(Judge(0.157f, 1.0f, 0.10f, true), "0.157 m is over the 0.10 bar");
            Assert.IsFalse(Judge(0.05f, 0.65f, 0.10f, true), "rate under 0.7");
            Assert.IsFalse(Judge(0.05f, 1.45f, 0.10f, true), "rate over 1.4");
            Assert.IsTrue(Judge(0.05f, 1.4000001f, 0.10f, true), "the 1.4 edge is inclusive");
            Assert.IsTrue(Judge(0.05f, 0.7f, 0.10f, true), "the 0.7 edge is inclusive");
            Assert.IsFalse(Judge(0.05f, 1.402f, 0.10f, true), "a real excess over 1.4 still fails");
            Assert.IsFalse(Judge(0.05f, 1.4011f, 0.10f, true), "1.4011 is over the 1.4 edge by more than the 1e-4 tolerance");
            Assert.IsFalse(Judge(0.05f, 0.6989f, 0.10f, true), "0.6989 is under the 0.7 edge by more than the 1e-4 tolerance");
            Assert.IsTrue(Judge(0.05f, 1.40005f, 0.10f, true), "inside the tolerance of the 1.4 edge");
            Assert.IsTrue(Judge(0.05f, 0.69995f, 0.10f, true), "inside the tolerance of the 0.7 edge");
            Assert.IsTrue(Judge(0.09f, 1.0f, 0.10f, true));
            Assert.IsTrue(Judge(0.09f, 3.0f, 0.10f, false), "no rate band for gaits");
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator Calibration_Idle_AtZeroSpeed_ReadsAboutZero()
        {
            yield return Begin();
            Measured m = default;
            yield return Measure("idle", 0f, r => m = r);
            Assert.Less(m.slide, 0.01f, $"idle at 0 m/s slid {m.slide:F4} m: the measurement itself is off");
        }

        /// <summary>
        /// Cross-check against the artist's Blender figures (td): the pack clips carry an intrinsic slide, Walk 0.033 m and Run 0.021 m (+-0.01).
        /// Walk and Run play alone at their exact on-body clip speeds. Our figure must agree, which shows the harness measures what Blender measures.
        /// </summary>
        [UnityTest, Timeout(1200000)]
        public IEnumerator Calibration_WalkAndRun_AgreeWithBlenderIntrinsics()
        {
            var results = new List<string>();
            var disagreements = new List<string>();
            foreach (var c in new[] { ("walk", 1.337f, 0.048f), ("run", 3.035f, 0.066f) })   // artist, latest Blender figures: pack Walk 0.048, Run 0.066 (td quoted 0.033 and 0.021 earlier)
            {
                yield return Begin();
                float scale = view.Body.SkeletonScale;
                Measured m = default;
                yield return Measure("calib-" + c.Item1, c.Item2 * scale, r => m = r, Key.W);
                string line = $"{c.Item1} at its exact on-body clip speed {c.Item2 * scale:F3} m/s: slide {m.slide:F3} m ({m.plants} plants), effective rate {m.effectiveRate:F3}; Blender intrinsic {c.Item3:F3} +-0.015";
                bool agree = Mathf.Abs(m.slide - c.Item3) <= 0.015f;
                results.Add(line + (agree ? " AGREES" : " DISAGREES"));
                Debug.Log("FootSlideTests: " + line);
                if (!agree) disagreements.Add("our figure does not agree with Blender on a known clip: " + line);
            }
            File.AppendAllText($"{OutDir}/foot-slide-calibration.txt", string.Join("\n", results) + "\n");
            Assert.IsEmpty(disagreements, string.Join("; ", disagreements));
        }

        /// <summary>
        /// td, #19: an explicit pass/fail on the FootSampler effective playback rate per gait (normalized time x state length / dt over the 2.5 s steady window): within
        /// 10% of 1.0 and above 0.5. A playing animator reads 1.0 at any rate because the state length includes the speed; a frozen one reads 0.
        /// </summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator Gaits_AnimatorIsAdvancing_PerGait()
        {
            var results = new List<string>();
            var fails = new List<string>();
            foreach (var g in new[] { ("Jog 5", 5f, false, new[] { Key.W }), ("Sprint 8", 0f, false, new[] { Key.W, Key.LeftShift }), ("Walk 1.15", 1.15f, true, new[] { Key.W }), ("Walk 1.5", 1.5f, true, new[] { Key.W }), ("CrouchWalk 1.75", 0f, false, new[] { Key.W }) })
            {
                yield return Begin();
                if (g.Item1.StartsWith("Crouch")) yield return Go(StanceKey.Crouch, 3f);
                Measured m = default;
                float hold = g.Item3 ? g.Item2 : 0f;   // Walk speeds are held by the analog input
                yield return Measure("advancing-" + g.Item1.Replace(" ", ""), hold, r => m = r, g.Item4);
                bool ok = !float.IsNaN(m.advancing) && m.advancing > 0.5f && Mathf.Abs(m.advancing - 1f) <= 0.10f;
                string line = $"{(ok ? "PASS" : "FAIL")} {g.Item1}: animator advancing at {m.advancing:F3} x real time (need within 10% of 1.0 and above 0.5); speed {m.speed:F2} m/s; LocoRate {m.locoParam:F3}";
                results.Add(line);
                if (!ok) fails.Add(line);
            }
            File.AppendAllText($"{OutDir}/animator-advancing.txt", string.Join("\n", results) + "\n");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>S-A7 / H4: the gaits that play at 5 and 8 m/s, at most SlideLimit per plant.</summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator Gaits_Jog5_Sprint8()
        {
            var results = new List<string>();
            foreach (var g in new[] { ("jog5", "Jog 5 m/s", new[] { Key.W }), ("sprint8", "Sprint 8 m/s", new[] { Key.W, Key.LeftShift }) })
            {
                yield return Begin();
                Measured m = default;
                yield return Measure(g.Item1, 0f, r => m = r, g.Item3);
                bool ok = Judge(m.slide, m.effectiveRate, SlideLimit, false);
                if (g.Item1 == "jog5") Debug.Log($"FootSlideTests: authored-clip known case (Jog at 5 m/s, intrinsic 0.000 after the re-key): slide {m.slide:F3} m, expected 0.020 or less: {(m.slide <= 0.02f ? "OK" : "OVER")}");
                results.Add($"{(ok ? "PASS" : "FAIL")} {g.Item2}: max foot slide per plant {m.slide:F3} m, mean {m.meanSlide:F3} m, over {m.plants} plants (limit {SlideLimit:F2}); measured speed {m.speed:F2} m/s, LocomotionRate {m.expectedRate:F3} (scale argument {view.Body.SkeletonScale:F3}; with scale 1.0 it would be {CharacterClipSet.LocomotionRate((int)player.Stance, m.speed, 1f):F3}, with the scale squared {CharacterClipSet.LocomotionRate((int)player.Stance, m.speed, view.Body.SkeletonScale * view.Body.SkeletonScale):F3}), Animator LocoRate {m.locoParam:F3}, state speed x multiplier {m.stateSpeed:F3}, effective rate {m.effectiveRate:F3}");
            }
            File.AppendAllText($"{OutDir}/foot-slide.txt", string.Join("\n", results) + "\n");
            Assert.IsFalse(results.Exists(r => r.StartsWith("FAIL")), string.Join("\n", results));
        }

        /// <summary>The crouch speed range: every step is judged honestly and printed; the ends (1.25, 2.50) decide the range.</summary>
        [UnityTest, Timeout(2400000)]
        public IEnumerator CrouchSpeeds_RangeEnds()
        {
            var results = new List<string>();
            var endFails = new List<string>();
            foreach (float speed in new[] { 1.25f, 1.50f, 1.75f, 2.00f, 2.25f, 2.50f })
            {
                yield return Begin(true);
                player.StanceTuning.crouchSpeed = speed;
                yield return Go(StanceKey.Crouch, 3f);
                Measured m = default;
                yield return Measure($"crouch{speed:F2}", 0f, r => m = r, Key.W);
                bool ok = Judge(m.slide, m.effectiveRate, SlideLimit, true);
                string line = $"{(ok ? "PASS" : "FAIL")} crouch speed {speed:F2}: slide {m.slide:F3} m over {m.plants} plants (limit {SlideLimit:F2}); required rate {speed / 1.786f:F2} (= speed / 1.786); LocomotionRate {m.expectedRate:F3}, Animator LocoRate {m.locoParam:F3}, state speed x multiplier {m.stateSpeed:F3}, effective rate {m.effectiveRate:F3} (band 0.7-1.4)";
                results.Add(line);
                if (!ok && (speed == 1.25f || speed == 2.50f)) endFails.Add(line);
            }
            File.AppendAllText($"{OutDir}/range-crouch-speed.txt", string.Join("\n", results) + "\n");
            Assert.IsEmpty(endFails, "range ends failed:\n" + string.Join("\n", endFails) + "\n-- all steps:\n" + string.Join("\n", results));
        }

        /// <summary>
        /// td ruling (QA report line 265): forward (H4, Noah's scope) and back (amend8b: SprintBack's overreach accepted only while slide stays at or under the bar) are asserted
        /// and print PASS or FAIL. Left, right and the four diagonals print INFO with the figure (report-only, #35), plus "over bar" above the limit, and never PASS or FAIL.
        /// </summary>
        public static string GradeSprintDirection(string rig, string dir, Measured m, float limit)
        {
            string body = $"Sprint 8 {rig} {dir}: max slide per plant {m.slide:F3} m, mean {m.meanSlide:F3} m over {m.plants} plants (limit {limit:F2}); measured speed {m.speed:F2} m/s, Animator LocoRate {m.locoParam:F3}, advancing {m.advancing:F3}";
            if (dir == "forward" || dir == "back")
                return (Judge(m.slide, m.effectiveRate, limit, false) && m.plants >= 2 ? "PASS " : "FAIL ") + body;
            return "INFO " + body + " (report-only, #35)" + (m.slide > limit ? " over bar" : "");
        }

        /// <summary>
        /// Director amend8b/amend8f: SprintBack's 1.3 cm overreach is accepted only while the back-sprint foot slide stays at or under 0.10 m; amend8b sets no condition on left, right or the
        /// diagonals. Sprint 8 on both rigs, Shift held, all eight directions printed: forward (H4) and back are asserted, left, right and the four diagonals are information for #35 (see GradeSprintDirection).
        /// </summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator Sprint8_AllDirections_BothRigs()
        {
            var dirs = new (string, Key[])[]
            {
                ("forward", new[] { Key.W, Key.LeftShift }), ("back", new[] { Key.S, Key.LeftShift }), ("left", new[] { Key.A, Key.LeftShift }), ("right", new[] { Key.D, Key.LeftShift }),
                ("forward-left", new[] { Key.W, Key.A, Key.LeftShift }), ("forward-right", new[] { Key.W, Key.D, Key.LeftShift }),
                ("back-left", new[] { Key.S, Key.A, Key.LeftShift }), ("back-right", new[] { Key.S, Key.D, Key.LeftShift }),
            };
            var results = new List<string>();
            foreach (bool women in new[] { false, true })
                foreach (var d in dirs)
                {
                    yield return Begin(false, women);
                    Measured m = default;
                    string rig = women ? "women" : "men";
                    yield return Measure($"sprint8-{rig}-{d.Item1}", 0f, r => m = r, d.Item2);
                    results.Add(GradeSprintDirection(rig, d.Item1, m, SlideLimit));
                }
            File.AppendAllText($"{OutDir}/foot-slide-sprint-directions.txt", string.Join("\n", results) + "\n");
            Assert.IsFalse(results.Exists(r => r.StartsWith("FAIL")), string.Join("\n", results));
        }

        /// <summary>
        /// Force-the-failure for the Sprint8_AllDirections forward assert (td): Sprint 8 forward with the driver's LocoRate forced to 1.0 (the correct rate is 1.38) is graded by the same
        /// GradeSprintDirection and must come out FAIL. Explicit: expected to go RED when run by name; the assert fails exactly when the forward grade is FAIL.
        /// </summary>
        [UnityTest, Explicit("Forced-bad case: expected to FAIL, proving the Sprint 8 forward assert can fail"), Timeout(1200000)]
        public IEnumerator ForcedBad_Sprint8Forward_MustTurnRed()
        {
            yield return Begin();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PlayerCharacterView.TestRateOverride = 1.0f;
#endif
            Measured m = default;
            yield return Measure("forcedbad-sprint8-forward-rate1.0", 0f, r => m = r, Key.W, Key.LeftShift);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PlayerCharacterView.TestRateOverride = null;
#endif
            string grade = GradeSprintDirection("men", "forward", m, SlideLimit);
            string verdict = grade.StartsWith("FAIL")
                ? $"EXPECTED RED: got RED: {grade}"
                : $"EXPECTED RED: got GREEN (assert blind): {grade}";
            Debug.Log("FootSlideTests: " + verdict);
            File.AppendAllText($"{OutDir}/forced-bad-sprint8-forward.txt", verdict + "\n");
            Assert.IsFalse(grade.StartsWith("FAIL"), verdict + " (this assert is meant to fail)");
        }

        /// <summary>
        /// Sprint hip and foot check on the new clip (director amend8b). Steady Sprint 8, 2.5 s: (1) lowest foot vertex against the floor at the plants, (2) shadow Hips height above the
        /// floor at its lowest (mid-stance) and highest (mid-flight) against the clip's hip height x skeleton scale, tolerance 0.01 m. Numbers only: the clip figures come from the artist as the environment variables
        /// SPRINT_HIP_STANCE and SPRINT_HIP_FLIGHT (metres, before the skeleton scale); when both are set the test asserts +-0.01, unset it only prints.
        /// </summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator Sprint8_HipAndFoot_BothRigs()
        {
            var lines = new List<string>();
            var fails = new List<string>();
            float.TryParse(System.Environment.GetEnvironmentVariable("SPRINT_HIP_STANCE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float expStance);
            float.TryParse(System.Environment.GetEnvironmentVariable("SPRINT_HIP_FLIGHT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float expFlight);
            foreach (bool women in new[] { false, true })
            {
                yield return Begin(false, women);
                Place(new Vector3(floor.transform.position.x, floor.transform.position.y + 2.05f, floor.transform.position.z - 27f), 0f);
                yield return new WaitForSeconds(0.3f);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                yield return new WaitForSeconds(1.2f);
                float floorTop = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
                var hips = view.Body.Bone("Hips");
                var footL = view.Body.Bone("Foot.L");
                var footR = view.Body.Bone("Foot.R");
                var feetSmr = view.Body.Renderers(CharacterSlot.Feet);
                var hipY = new List<float>();
                var fl = new List<float>();
                var fr = new List<float>();
                var vertexLow = new List<float>();
                float t = 0f;
                var mesh = new Mesh();
                while (t < 2.5f)
                {
                    yield return null;   // WaitForEndOfFrame is not evoked in batchmode; the bones are read after this frame's Update and animation, as before
                    t += Time.deltaTime;
                    hipY.Add(hips.position.y - floorTop);
                    fl.Add(footL.position.y); fr.Add(footR.position.y);
                    float low = float.MaxValue;
                    foreach (var r in feetSmr)
                    {
                        r.BakeMesh(mesh, true);
                        var v = mesh.vertices;
                        Vector3 pos = r.transform.position; Quaternion rot = r.transform.rotation;
                        for (int i = 0; i < v.Length; i++) low = Mathf.Min(low, (pos + rot * v[i]).y);
                    }
                    vertexLow.Add(low - floorTop);
                }
                Object.Destroy(mesh);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                // plant frames: the foot bone within 3 cm of its own lowest height
                float lowL = Mathf.Min(fl.ToArray()), lowR = Mathf.Min(fr.ToArray());
                var plantVertex = new List<float>();
                for (int i = 0; i < hipY.Count; i++)
                    if (fl[i] <= lowL + 0.03f || fr[i] <= lowR + 0.03f) plantVertex.Add(vertexLow[i]);
                float hipLow = Mathf.Min(hipY.ToArray()), hipHigh = Mathf.Max(hipY.ToArray());
                float footAtPlant = plantVertex.Count > 0 ? Mathf.Min(plantVertex.ToArray()) : float.NaN;
                float footAtPlantMean = plantVertex.Count > 0 ? Mathf.Min(plantVertex.ToArray()) + 0f : float.NaN;
                float sum = 0f; foreach (var x in plantVertex) sum += x;
                float meanPlant = plantVertex.Count > 0 ? sum / plantVertex.Count : float.NaN;
                float scale = view.Body.SkeletonScale;
                string rig = women ? "women" : "men";
                string line = $"{rig}: skeleton scale {scale:F3}; lowest foot vertex above the floor at plants: min {footAtPlant:+0.000;-0.000} m, mean {meanPlant:+0.000;-0.000} m over {plantVertex.Count} plant frames; Hips above the floor: lowest (mid-stance) {hipLow:F3} m, highest (mid-flight) {hipHigh:F3} m over {hipY.Count} frames";
                if (expStance > 0f) line += $"; clip mid-stance hip x scale {expStance * scale:F3} m (diff {hipLow - expStance * scale:+0.000;-0.000}), clip mid-flight hip x scale {expFlight * scale:F3} m (diff {hipHigh - expFlight * scale:+0.000;-0.000}), tolerance 0.010";
                lines.Add(line);
                Debug.Log("FootSlideTests[sprint hip/foot]: " + line);
                if (expStance > 0f && (Mathf.Abs(hipLow - expStance * scale) > 0.01f || Mathf.Abs(hipHigh - expFlight * scale) > 0.01f)) fails.Add(line);
                if (plantVertex.Count == 0) fails.Add(rig + ": no plant frames");
            }
            File.AppendAllText($"{OutDir}/sprint-hip-foot.txt", string.Join("\n", lines) + "\n");
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>
        /// Proof that the check can fail: crouch 1.25 m/s with the driver's LocoRate forced to 1.0 (the old stuck-rate fault) must slide more than 0.10 m
        /// or leave the rate band. Explicit: expected to go RED when run by name.
        /// </summary>
        [UnityTest, Explicit("Forced-bad case: expected to FAIL, proving the slide check can fail"), Timeout(1200000)]
        public IEnumerator ForcedBad_RateStuckAtOne_MustTurnRed()
        {
            yield return Begin(true);
            player.StanceTuning.crouchSpeed = 1.25f;
            yield return Go(StanceKey.Crouch, 3f);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PlayerCharacterView.TestRateOverride = 1.0f;
#endif
            Measured m = default;
            yield return Measure("forcedbad-crouch1.25-rate1.0", 0f, r => m = r, Key.W);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PlayerCharacterView.TestRateOverride = null;
#endif
            bool judged = Judge(m.slide, m.effectiveRate, SlideLimit, true);
            string verdict = judged
                ? $"EXPECTED RED: got GREEN (harness blind): slide {m.slide:F3} m, rate {m.effectiveRate:F2}"
                : $"EXPECTED RED: got RED: slide {m.slide:F3} m, rate {m.effectiveRate:F2} (limit {SlideLimit:F2}, band 0.7-1.4)";
            Debug.Log("FootSlideTests: " + verdict);
            File.AppendAllText($"{OutDir}/forced-bad.txt", verdict + "\n");
            Assert.IsTrue(judged, verdict + " (this assert is meant to fail)");
        }
    }
}
