using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;
using Range = WashedAshore.Character.Stance.StanceRangeArgument;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// The -bbStance launch argument (player-stances.md section 3b): parsing, the whole-argument eye-ordering refusal (6 of 27 combinations), the exact-0.10 pass, the
    /// per-stance clips on BOTH Animators, the eye and capsule at the retuned ends, and the release strip. Dev builds and the Editor only.
    /// </summary>
    public class StanceRangeArgumentTests : StanceFixture
    {
        static Range.Settings S(string v) { Assert.IsTrue(Range.TryParse(v, out var s, out string e), e); return s; }

        [Test]
        public void Parse_KeysAndValues()
        {
            var s = S("crouch=low,sleep=high,crouchspeed=high");
            Assert.AreEqual(Range.End.Low, s.crouch);
            Assert.AreEqual(Range.End.High, s.sleep);
            Assert.AreEqual(Range.End.High, s.crouchSpeed);
            Assert.AreEqual(Range.End.Record, s.squat);
            Assert.AreEqual(Range.End.Record, s.sit);
            Assert.IsFalse(Range.TryParse("crouch=medium", out _, out _));
            Assert.IsFalse(Range.TryParse("walk=low", out _, out _));
            Assert.IsFalse(Range.TryParse("", out _, out _));
        }

        [Test]
        public void Ordering_ExactlyTheSixRefusedCombinations()
        {
            var ends = new[] { "low", "default", "high" };
            var refused = new List<string>();
            foreach (var c in ends) foreach (var q in ends) foreach (var t in ends)
            {
                string v = $"crouch={c},squat={q},sit={t}";
                if (Range.ValidateOrdering(S(v)) != null) refused.Add(v);
            }
            Assert.AreEqual(6, refused.Count, "refused: " + string.Join(" | ", refused));
            foreach (var r in refused)
                Assert.IsTrue(r.StartsWith("crouch=low,squat=high") || (r.Contains("squat=low") && r.EndsWith("sit=high")), r);
        }

        [Test]
        public void Ordering_CrouchLowSquatHigh_NamesTheCrouchSquatPair()
        {
            string msg = Range.ValidateOrdering(S("crouch=low,squat=high"));
            StringAssert.Contains("crouch=Low", msg);
            StringAssert.Contains("squat=High", msg);
        }

        [Test]
        public void Ordering_SquatLowSitHigh_NamesTheSquatSitPair()
        {
            string msg = Range.ValidateOrdering(S("squat=low,sit=high"));
            StringAssert.Contains("squat=Low", msg);
            StringAssert.Contains("sit=High", msg);
        }

        [Test]
        public void Ordering_ExactTenCentimetreGap_Passes()
        {
            // crouch low 1.10 against squat record 1.00, and squat record 1.00 against sit high 0.90: both exactly 0.10, float rounding inside the 0.0001 epsilon.
            Assert.IsNull(Range.ValidateOrdering(S("crouch=low,squat=default")));
            Assert.IsNull(Range.ValidateOrdering(S("squat=default,sit=high")));
            Assert.IsNull(Range.ValidateOrdering(S("crouch=low,squat=default,sit=high")));
        }

        [UnityTest]
        public IEnumerator Refusal_ChangesNothing_OneError()
        {
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            var shadowBefore = view.Body.Animator.runtimeAnimatorController;
            var viewBefore = view.ViewCopy.ViewAnimator.runtimeAnimatorController;
            float crouchBefore = player.StanceTuning.crouchEye, squatBefore = player.StanceTuning.squatEye;
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("-bbStance refused: crouch=Low"));
            string r = Range.Apply("crouch=low,squat=high,sit=low", player, view.Body, view.ViewCopy);
            Assert.IsNotNull(r);
            Assert.AreSame(shadowBefore, view.Body.Animator.runtimeAnimatorController, "a refused argument must not touch the shadow controller");
            Assert.AreSame(viewBefore, view.ViewCopy.ViewAnimator.runtimeAnimatorController, "a refused argument must not touch the view controller");
            Assert.AreEqual(crouchBefore, player.StanceTuning.crouchEye);
            Assert.AreEqual(squatBefore, player.StanceTuning.squatEye);
            Assert.AreEqual(0.20f, player.StanceTuning.sleepEye, 1e-6f, "sleep stays at record in a refused argument");
        }

        /// <summary>Clips of the composed controller against the body's own overrides and the range controller's: stance clips from the range end, everything else untouched.</summary>
        static void CheckClips(RuntimeAnimatorController composed, RuntimeAnimatorController own, RuntimeAnimatorController range, string stance, string who, List<string> log)
        {
            var c = composed as AnimatorOverrideController;
            Assert.IsNotNull(c, who + ": not an override controller");
            var cl = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            c.GetOverrides(cl);
            var ownMap = Map(own);
            var rangeMap = Map(range);
            int stanceClips = 0;
            foreach (var kv in cl)
            {
                string st = Range.StanceOfClip(kv.Key.name);
                if (st == stance)
                {
                    if (rangeMap.TryGetValue(kv.Key, out var rv) && rv != null)
                    {
                        Assert.AreSame(rv, kv.Value, $"{who}: {kv.Key.name} should be the range clip");
                        stanceClips++;
                        log.Add($"{who}: {kv.Key.name} -> {kv.Value.name}");
                    }
                }
                else
                {
                    ownMap.TryGetValue(kv.Key, out var ov);
                    Assert.AreSame(ov, kv.Value, $"{who}: {kv.Key.name} (not a {stance} clip) must be unchanged");
                }
            }
            Assert.Greater(stanceClips, 0, $"{who}: no {stance} clip was overridden");
        }

        static Dictionary<AnimationClip, AnimationClip> Map(RuntimeAnimatorController rc)
        {
            var d = new Dictionary<AnimationClip, AnimationClip>();
            if (rc is AnimatorOverrideController o)
            {
                var l = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                o.GetOverrides(l);
                foreach (var kv in l) d[kv.Key] = kv.Value;
            }
            return d;
        }

        IEnumerator EndCase(string arg, string stance, bool high, StanceKey key, Stance expect, float eye)
        {
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            var app = view.Body.Appearance;
            var shadowOwn = view.Body.Animator.runtimeAnimatorController;
            var viewOwn = view.ViewCopy.ViewAnimator.runtimeAnimatorController;
            Assert.IsNull(Range.Apply(arg, player, view.Body, view.ViewCopy));
            var log = new List<string>();
            CheckClips(view.Body.Animator.runtimeAnimatorController, shadowOwn, app.RangeController(high, true), stance, "shadow", log);
            CheckClips(view.ViewCopy.ViewAnimator.runtimeAnimatorController, viewOwn, app.RangeController(high, false), stance, "view", log);
            // the shadow keeps its eight capped Jog and Sprint clips mapped to _Full; the view maps none of them
            int shadowFull = 0, viewMapped = 0;
            foreach (var kv in Map(view.Body.Animator.runtimeAnimatorController)) if (kv.Value != null && kv.Value.name.EndsWith("_Full")) shadowFull++;
            foreach (var kv in Map(view.ViewCopy.ViewAnimator.runtimeAnimatorController)) if (kv.Value != null && kv.Value.name.EndsWith("_Full")) viewMapped++;
            Assert.AreEqual(8, shadowFull, "the shadow must still map the eight capped clips to _Full");
            Assert.AreEqual(0, viewMapped, "the view must map none to _Full");
            yield return Go(key, 6f);
            Assert.AreEqual(expect, player.Stance);
            yield return new WaitForSeconds(2f);
            float eyeNow = player.ViewCamera.transform.position.y - player.transform.position.y;
            Assert.AreEqual(eye, eyeNow, Eps, "pivot eye");
            // capsule = eye + 0.15, sleep at the 0.80 floor
            Assert.AreEqual(expect == Stance.Sleep ? 0.80f : eye + 0.15f, body.height, Eps, "capsule height");
            // H5 eye match: camera against the hidden-head eye point of the shadow body, within 3 cm
            var head = view.Body.Bone("Head");
            Vector3 p = head.position + Vector3.up * 0.1094f + player.transform.forward * 0.1011f;
            Assert.AreEqual(player.ViewCamera.transform.position.y, p.y, 0.03f, "H5 eye match");
            string dir = Path.Combine("TestResults", "player-character", "range");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, $"bbstance-{stance}-{(high ? "high" : "low")}.txt"), log);
        }

        [UnityTest] public IEnumerator CrouchLow_EyeCapsuleClipsAndH5() => EndCase("crouch=low", "crouch", false, StanceKey.Crouch, Stance.Crouch, 1.10f);
        [UnityTest]
        public IEnumerator SleepHigh_EyeCapsuleClipsAndH5()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern); its sibling EndCase tests stay unpinned
            return EndCase("sleep=high", "sleep", true, StanceKey.Sleep, Stance.Sleep, 0.25f);
        }

        [UnityTest]
        public IEnumerator RebuildWith_TearsDownFully_NoLeakedBodies()
        {
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            int Bodies() => Object.FindObjectsByType<CharacterBody>(FindObjectsInactive.Include).Length;
            int Appearances() => Resources.FindObjectsOfTypeAll<CharacterAppearance>().Length;
            int bodies0 = Bodies(), apps0 = Appearances();
            Assert.AreEqual(2, bodies0, "one shadow and one view body at the start");
            var same = view.Appearance;
            for (int i = 0; i < 2; i++)
            {
                view.RebuildWith(same);
                yield return null;
                yield return null;
            }
            Assert.AreEqual(2, Bodies(), "after two rebuilds: exactly one shadow and one view body");
            Assert.AreEqual(apps0, Appearances(), "owned appearance copies leaked by the rebuild");
            Assert.IsNotNull(view.Body);
            Assert.IsNotNull(view.ViewCopy);
            Assert.AreSame(view.Body, view.ViewCopy.Shadow, "the view copy follows the new shadow body");
        }

        [UnityTest]
        public IEnumerator StartupLine_FormatAndDevValues()
        {
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            string none = PlayerCharacterView.StanceLogLine(player, Range.Applied);
            StringAssert.IsMatch(@"^\[Stance\] eyes stand \d+\.\d\d crouch \d+\.\d\d squat \d+\.\d\d sit \d+\.\d\d sleep \d+\.\d\d; crouchspeed \d+\.\d\d; range args: none$", none);
            StringAssert.Contains("crouch 1.20 squat 1.00 sit 0.80 sleep 0.20; crouchspeed 1.75; range args: none", none);
            Assert.IsNull(Range.Apply("crouch=low,sleep=high,crouchspeed=high", player, view.Body, view.ViewCopy));
            string line = PlayerCharacterView.StanceLogLine(player, Range.Applied);
            Debug.Log("StanceRangeArgumentTests: " + line);
            StringAssert.Contains("crouch 1.10 squat 1.00 sit 0.80 sleep 0.25; crouchspeed 2.50; range args: crouch=low,sleep=high,crouchspeed=high", line);
            StringAssert.StartsWith("[Stance] eyes stand ", line);
        }

        [Test]
        public void ReleaseBuild_IgnoresTheArgument_SourceIsStripped()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "Scripts", "Character", "Stance"));
            string src = File.ReadAllText(Path.Combine(root, "StanceRangeArgument.cs")).Replace("\r\n", "\n");
            Assert.IsTrue(src.StartsWith("#if UNITY_EDITOR || DEVELOPMENT_BUILD\n"), "the whole type must sit behind the dev define");
            Assert.IsTrue(src.TrimEnd().EndsWith("#endif"), "the define must close the file");
            string drv = File.ReadAllText(Path.Combine(root, "PlayerCharacterView.cs")).Replace("\r\n", "\n");
            int use = drv.IndexOf("StanceRangeArgument.FromCommandLine");
            Assert.Greater(use, 0, "the driver must read the argument");
            int open = drv.LastIndexOf("#if UNITY_EDITOR || DEVELOPMENT_BUILD", use);
            int close = drv.IndexOf("#endif", use);
            Assert.IsTrue(open >= 0 && close > use && drv.IndexOf("#endif", open) == close, "the driver's use must sit inside its own dev #if block");
        }
    }
}
