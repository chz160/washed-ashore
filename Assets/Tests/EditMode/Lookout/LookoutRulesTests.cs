using NUnit.Framework;
using UnityEngine;
using WashedAshore.Interaction;
using WashedAshore.Lookout;
using WashedAshore.Tests.PlayMode.Lookout;

namespace WashedAshore.Tests.EditMode.Lookout
{
    /// <summary>
    /// The pure lookout rules and the LookoutTuning asset (spec I2-I4, tower-design-spec.md §4-§6, tower-td-note §6
    /// conditions 15 and 17): hinge animation, the firefinder's tap-or-hold rule, the bearing convention and the
    /// hatch duck curve.
    /// </summary>
    public class LookoutRulesTests
    {
        const float Tol = 1e-4f;
        const float StandingEye = 1.65f;                 // live controller (World.unity); PlayMode re-reads it
        // Example inputs for the pure rules only: a 12-in-9.25 stair and a 1.50 m opening in a 0.05 m floor. The built
        // pitch, opening and floor are data (lane ends and the build manifest) and are read by the built-tower tests.
        const float StairPitch = 52.37f;
        const float BuiltOpening = 1.50f, Floor = 0.05f;

        LookoutTuning t;

        [SetUp] public void SetUp() => t = ScriptableObject.CreateInstance<LookoutTuning>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(t);

        [Test]
        public void Tuning_DefaultsAreValid_AndTheSpecRulesAreEnforced()
        {
            Assert.IsNull(t.Validate(), t.Validate());
            Assert.GreaterOrEqual(t.hatchSeconds, 1f, "N13: the hatch takes a second or more");
            t.hatchStopHeadroom = t.hatchSwingDepth + t.hatchHeadroomMargin - 0.01f;
            Assert.IsNotNull(t.Validate(), "headroom at the swing depth: the hatch could not be opened from below");
            t.hatchStopHeadroom = 0.15f;
            t.eyeClearance = LookoutTuning.EyeClearanceR8 - 0.01f;
            Assert.IsNotNull(t.Validate(), "R8 is fixed");
            t.eyeClearance = LookoutTuning.EyeClearanceR8;
            t.hatchCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 1.2f), new Keyframe(1f, 1f));
            Assert.IsNotNull(t.Validate(), "a curve that overshoots");
        }

        /// <summary>T-I1b: the asset's default words give exactly the spec §3.2 strings, for every state and key. An edited word fails here.</summary>
        [Test]
        public void PromptWords_GiveExactlyTheSpecStrings()
        {
            var settings = ScriptableObject.CreateInstance<InteractionSettings>();
            string Line(string key, string verb, string noun) => InteractionRules.PromptLine(key, settings.separator, InteractionRules.VerbNoun(verb, noun));
            Assert.AreEqual(LookoutTargets.PromptOpenHatch, Line(LookoutTargets.KeyKeyboard, t.openVerb, t.hatchNoun));
            Assert.AreEqual(LookoutTargets.PromptCloseHatch, Line(LookoutTargets.KeyKeyboard, t.closeVerb, t.hatchNoun));
            Assert.AreEqual(LookoutTargets.PromptOpenWindow, Line(LookoutTargets.KeyKeyboard, t.openVerb, t.sashNoun));
            Assert.AreEqual(LookoutTargets.PromptCloseWindow, Line(LookoutTargets.KeyKeyboard, t.closeVerb, t.sashNoun));
            Assert.AreEqual(LookoutTargets.PromptUseFirefinder, Line(LookoutTargets.KeyKeyboard, t.useVerb, t.firefinderNoun));
            Assert.AreEqual(LookoutTargets.PromptLeaveFirefinder, Line(LookoutTargets.KeyKeyboard, t.leaveVerb, t.firefinderNoun));
            foreach (string pad in new[] { LookoutTargets.KeyXbox, LookoutTargets.KeyPlayStation, LookoutTargets.KeyOtherPad })
                Assert.AreEqual(pad + LookoutTargets.PromptOpenHatch.Substring(LookoutTargets.KeyKeyboard.Length), Line(pad, t.openVerb, t.hatchNoun), $"key text {pad}");
            Assert.AreEqual(LookoutTargets.KeyOtherPad, InteractionRules.KeyLabel(settings.unnamedSouthDisplay, settings.unnamedSouthDisplay, settings.genericPadKey), "a pad with no name shows South");
            Object.DestroyImmediate(settings);
        }

        [Test]
        public void Bearing_ZeroIsNorth_ClockwiseEastIsNinety()
        {
            Assert.AreEqual(0f, LookoutRules.Bearing(Vector3.forward), Tol);
            Assert.AreEqual(90f, LookoutRules.Bearing(Vector3.right), Tol);
            Assert.AreEqual(180f, LookoutRules.Bearing(Vector3.back), Tol);
            Assert.AreEqual(270f, LookoutRules.Bearing(Vector3.left), Tol);
            for (int b = 0; b < 360; b += 15)
                Assert.AreEqual(0f, Mathf.DeltaAngle(b, LookoutRules.Bearing(LookoutRules.BearingDirection(b))), 1e-3f, $"round trip at {b}");
        }

        [Test]
        public void HingeAngle_IsAFunctionOfStateAndElapsed_MonotonicWithNoOvershoot()
        {
            float open = t.hatchOpenAngle, seconds = t.hatchSeconds;
            Assert.AreEqual(0f, LookoutRules.HingeAngle(HingeState.Closed, 99f, open, seconds, t.hatchCurve), Tol);
            Assert.AreEqual(open, LookoutRules.HingeAngle(HingeState.Open, 0f, open, seconds, t.hatchCurve), Tol);
            Assert.AreEqual(0f, LookoutRules.HingeAngle(HingeState.Opening, 0f, open, seconds, t.hatchCurve), 1e-3f);
            Assert.AreEqual(open, LookoutRules.HingeAngle(HingeState.Opening, seconds, open, seconds, t.hatchCurve), 1e-3f);
            Assert.AreEqual(open, LookoutRules.HingeAngle(HingeState.Closing, 0f, open, seconds, t.hatchCurve), 1e-3f);
            Assert.AreEqual(0f, LookoutRules.HingeAngle(HingeState.Closing, seconds, open, seconds, t.hatchCurve), 1e-3f);

            const int steps = 280;
            float last = 0f, peak = 0f, dt = seconds / steps;
            for (int i = 1; i <= steps; i++)
            {
                float a = LookoutRules.HingeAngle(HingeState.Opening, i * dt, open, seconds, t.hatchCurve);
                Assert.GreaterOrEqual(a, last - 1e-3f, $"backward at step {i}");
                Assert.LessOrEqual(a, open + 1e-3f, $"overshoot at step {i}");
                peak = Mathf.Max(peak, (a - last) / dt);
                last = a;
            }
            Debug.Log($"LookoutRulesTests: hatch peak angular speed {peak:F1} deg/s over {seconds} s to {open} deg");
            Assert.LessOrEqual(peak, PeakHatchSpeed, "spec §4: peak angular speed");
        }

        const float PeakHatchSpeed = 150f; // tower-design-spec.md §4, deg/s

        [Test]
        public void Settle_EndsASwingByItself_AndNothingElseChanges()
        {
            float s = t.hatchSeconds;
            Assert.AreEqual(HingeState.Opening, LookoutRules.Settle(HingeState.Opening, s - 0.01f, s));
            Assert.AreEqual(HingeState.Open, LookoutRules.Settle(HingeState.Opening, s, s));
            Assert.AreEqual(HingeState.Closed, LookoutRules.Settle(HingeState.Closing, s, s));
            Assert.AreEqual(HingeState.Open, LookoutRules.Settle(HingeState.Open, 1e6f, s), "never closes by itself (N13)");
            Assert.AreEqual(HingeState.Closed, LookoutRules.Settle(HingeState.Closed, 1e6f, s), "never opens by itself (N13)");
        }

        [Test]
        public void TryToggle_OnlyAtRest_NoReversingMidSwing()
        {
            Assert.IsTrue(LookoutRules.TryToggle(HingeState.Closed, out var next));
            Assert.AreEqual(HingeState.Opening, next);
            Assert.IsTrue(LookoutRules.TryToggle(HingeState.Open, out next));
            Assert.AreEqual(HingeState.Closing, next);
            Assert.IsFalse(LookoutRules.TryToggle(HingeState.Opening, out next));
            Assert.AreEqual(HingeState.Opening, next);
            Assert.IsFalse(LookoutRules.TryToggle(HingeState.Closing, out _));
        }

        [Test]
        public void IsTap_EitherSideOfTheThreshold()
        {
            float tap = t.firefinderTapSeconds, eps = 0.01f, pressed = 3f;
            Assert.IsTrue(LookoutRules.IsTap(pressed, pressed + tap - eps, tap), "released just inside: a tap, leaves");
            Assert.IsFalse(LookoutRules.IsTap(pressed, pressed + tap + eps, tap), "released just past: a hold, never leaves");
            Assert.IsFalse(LookoutRules.IsTap(pressed, pressed + tap, tap), "exactly at the threshold is a hold");
        }

        [Test]
        public void KeyTurnRate_NothingInTheTapWindow_ThenSlow_ThenFast()
        {
            float tap = t.firefinderTapSeconds, slow = t.firefinderKeySlowSeconds, eps = 0.01f;
            Assert.AreEqual(0f, LookoutRules.KeyTurnRate(tap - eps, t), Tol);
            Assert.AreEqual(t.firefinderKeySlowDegPerSec, LookoutRules.KeyTurnRate(tap + eps, t), Tol);
            Assert.AreEqual(t.firefinderKeySlowDegPerSec, LookoutRules.KeyTurnRate(tap + slow - eps, t), Tol);
            Assert.AreEqual(t.firefinderKeyFastDegPerSec, LookoutRules.KeyTurnRate(tap + slow + eps, t), Tol);
        }

        [Test]
        public void CapsuleInBox_StandingOnIt_InTheOpening_BesideIt_BelowIt()
        {
            Vector3 min = new Vector3(-0.66f, -0.1f, 0.33f), max = new Vector3(0.84f, 0.66f, 0.99f);
            float radius = 0.30f, height = 1.8f;
            Assert.IsTrue(LookoutRules.CapsuleInBox(new Vector3(0f, 0f, 0.66f), radius, height, min, max), "standing on the leaf");
            Assert.IsTrue(LookoutRules.CapsuleInBox(new Vector3(0f, -1.2f, 0.66f), radius, height, min, max), "in the opening on the flight");
            Assert.IsTrue(LookoutRules.CapsuleInBox(new Vector3(min.x - radius + 0.01f, 0f, 0.66f), radius, height, min, max), "overhanging the edge");
            Assert.IsFalse(LookoutRules.CapsuleInBox(new Vector3(min.x - radius - 0.01f, 0f, 0.66f), radius, height, min, max), "clear of the edge");
            Assert.IsFalse(LookoutRules.CapsuleInBox(new Vector3(0f, min.y - height - 0.01f, 0.66f), radius, height, min, max), "head below the volume");
        }

        [Test]
        public void PassEyeHeight_IsTheSpecFormula_ClampedBetweenTheDuckFloorAndStanding()
        {
            float clear = t.eyeClearance, lowest = t.hatchDuckMinEyeHeight, pitch = StairPitch * Mathf.Deg2Rad;
            float expected = BuiltOpening * Mathf.Tan(pitch) - Floor - clear / Mathf.Cos(pitch);
            float pass = LookoutRules.PassEyeHeight(BuiltOpening, StairPitch, Floor, clear, lowest, StandingEye);
            Assert.AreEqual(expected, pass, 1e-3f);
            Debug.Log($"LookoutRulesTests: pass eye height for a {BuiltOpening} m opening at {StairPitch} deg = {pass:F3} m");
            Assert.AreEqual(StandingEye, LookoutRules.PassEyeHeight(3f, StairPitch, Floor, clear, lowest, StandingEye), Tol, "a long opening needs no duck");
            Assert.AreEqual(lowest, LookoutRules.PassEyeHeight(0.5f, StairPitch, Floor, clear, lowest, StandingEye), Tol, "never below the duck floor");
        }

        [Test]
        public void HatchEyeHeight_DependsOnPositionOnly_SmoothAndBackToStandingOnTheFloor()
        {
            float clear = t.eyeClearance, run = t.hatchDuckRun;
            float pass = LookoutRules.PassEyeHeight(BuiltOpening, StairPitch, Floor, clear, t.hatchDuckMinEyeHeight, StandingEye);
            float Eye(float x) => LookoutRules.HatchEyeHeight(x, BuiltOpening, StairPitch, pass, StandingEye, run, clear);

            Assert.AreEqual(StandingEye, Eye(0f), Tol, "feet on the cab floor");
            Assert.AreEqual(StandingEye, Eye(BuiltOpening + clear + run), Tol, "before the duck starts");
            Assert.AreEqual(StandingEye, Eye(10f), Tol, "far down the flight");
            Assert.AreEqual(pass, Eye(BuiltOpening + clear), Tol, "fully down the clearance before the opening's edge");
            Assert.AreEqual(pass, Eye(BuiltOpening), Tol, "under the edge");

            const float step = 0.005f;
            float prev = Eye(0f), maxStep = 0f;
            for (float x = step; x <= BuiltOpening + clear + run + 0.1f; x += step)
            {
                float e = Eye(x);
                Assert.That(e, Is.InRange(pass - Tol, StandingEye + Tol), $"at run {x:F3}");
                maxStep = Mathf.Max(maxStep, Mathf.Abs(e - prev));
                prev = e;
            }
            Assert.Less(maxStep, step, "no snap: the eye never moves faster than the feet's run");
            Assert.AreEqual(StandingEye, LookoutRules.HatchEyeHeight(1f, BuiltOpening, StairPitch, StandingEye, StandingEye, run, clear), Tol,
                "no duck at all when the opening passes a standing eye");
        }
    }
}
