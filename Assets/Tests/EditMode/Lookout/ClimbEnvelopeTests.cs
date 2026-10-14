using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.EditMode.Lookout
{
    /// <summary>
    /// The join envelope on a final level stretch (tower-td-note, lane-end ruling): per side, the smallest of half
    /// the mouth's width, tan(laneGlideMaxDegrees) times the distance still to go to the lip or junction, and the
    /// model's reach there; never increasing from the free end, zero at the lip, a pure function of position.
    /// Geometry here is made up; the built lanes are checked by the built-tower tests.
    /// </summary>
    public class ClimbEnvelopeTests
    {
        const float Tol = 1e-4f;
        const float Linked = -1f;       // a negative run marks an end linked to another lane
        const float Spacing = 0.01f;    // the reach samples' spacing in these cases, m

        ClimbTuning t;
        StairLane lane;
        float run, slope, cap;

        [SetUp]
        public void SetUp()
        {
            t = ScriptableObject.CreateInstance<ClimbTuning>();
            Vector3 foot = new Vector3(10f, 4f, -2f), heading = new Vector3(1f, 0f, 1f).normalized;
            lane = new StairLane(foot, foot + heading * 2.3f + Vector3.up * 3f, 0.61f);
            run = t.laneRunOutLength;
            slope = Mathf.Tan(t.laneGlideMaxDegrees * Mathf.Deg2Rad);
            cap = lane.width * t.laneMouthWidthFactor * 0.5f;
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(t);

        StairLane Level(float length)
        {
            Vector3 along = Quaternion.Euler(0f, 100f, 0f) * lane.AxisHorizontal;
            return new StairLane(lane.head, lane.head + along * length, lane.width, false, 0f, 0f);
        }

        static float[] Flat(float value, float length)
        {
            var samples = new float[Mathf.CeilToInt(length / Spacing) + 1];
            for (int i = 0; i < samples.Length; i++) samples[i] = value;
            return samples;
        }

        [Test]
        public void WithNoReachFromTheModel_ItIsTheCapOrTheGlideSlope_AndZeroAtTheLip()
        {
            float len = lane.Length;
            Assert.AreEqual(Mathf.Min(cap, slope * run), ClimbRules.LateralBand(lane, -run, run, run, t), Tol, "at the foot run's free end");
            Assert.AreEqual(Mathf.Min(cap, slope * run * 0.5f), ClimbRules.LateralBand(lane, -run * 0.5f, run, run, t), Tol, "half-way along");
            Assert.AreEqual(0f, ClimbRules.LateralBand(lane, -1e-5f, run, run, t), 1e-3f, "nothing at the lip");
            Assert.AreEqual(Mathf.Min(cap, slope * run), ClimbRules.LateralBand(lane, len + run, run, run, t), Tol, "and the same beyond the head");
            Assert.AreEqual(0f, ClimbRules.LateralBand(lane, len + 1e-5f, run, run, t), 1e-3f);
            // A long level lane free at its head (the cab's exit lane): the cap binds near the free end, the slope near the junction.
            StairLane exit = Level(0.7f);
            Assert.AreEqual(cap, ClimbRules.LateralBand(exit, exit.Length, Linked, 0f, t), Tol, "the cap at a long stretch's free end");
            Assert.AreEqual(slope * 0.1f, ClimbRules.LateralBand(exit, 0.1f, Linked, 0f, t), Tol, "the slope 0.1 m from the junction");
            Assert.AreEqual(0f, ClimbRules.LateralBand(exit, 0f, Linked, 0f, t), Tol, "on the line at the junction");
            Assert.AreEqual(0f, ClimbRules.LateralBand(exit, exit.Length * 0.5f, Linked, Linked, t), Tol, "an inner level segment holds the line");
        }

        [Test]
        public void TheMouth_FollowsTheLanesOwnWidth()
        {
            Assert.AreEqual(lane.width, ClimbRules.MouthWidth(lane, t), Tol, "factor 1: the stringers");
            t.laneMouthWidthFactor = 1.5f;
            Assert.AreEqual(lane.width * 1.5f, ClimbRules.MouthWidth(lane, t), Tol);
            t.laneMouthWidthFactor = 1f;
            // The cap shows where the slope doesn't bind first: on a long stretch.
            StairLane exit = Level(0.7f);
            var wide = new StairLane(exit.foot, exit.head, exit.width * 1.2f, false, 0f, 0f);
            Assert.AreEqual(wide.width * 0.5f, ClimbRules.LateralBand(wide, wide.Length, Linked, 0f, t), Tol, "a wider lane has a wider mouth with no retune");
        }

        [Test]
        public void TheModelsReach_NarrowsEachSideOnItsOwn_AndZeroIsAFigure()
        {
            // Reach on the left only, at the foot: a wall 0.04 m to the left all the way along.
            var walled = new StairLane(lane.foot, lane.head, lane.width) { footReachLeft = Flat(0.04f, run), footReachRight = Flat(1f, run), reachSpacing = Spacing };
            Assert.AreEqual(0.04f, ClimbRules.LateralBand(walled, -run, run, run, t, -1f), Tol, "the left is the wall's reach");
            Assert.AreEqual(Mathf.Min(cap, slope * run), ClimbRules.LateralBand(walled, -run, run, run, t, 1f), Tol, "the right is still the cap or the slope");
            Assert.AreEqual(0.04f, ClimbRules.LateralBand(walled, -run, run, run, t), Tol, "no side asked: the narrower");
            Assert.AreEqual(Mathf.Min(0.04f, slope * 0.02f), ClimbRules.LateralBand(walled, -0.02f, run, run, t, -1f), Tol, "near the lip the slope is the smaller");
            Assert.AreEqual(Mathf.Min(cap, slope * run), ClimbRules.LateralBand(walled, walled.Length + run, run, run, t, -1f), Tol, "the head has no profile: no limit from the model");
            var onTheLine = new StairLane(lane.foot, lane.head, lane.width) { footReachLeft = Flat(0f, run), footReachRight = Flat(0f, run), reachSpacing = Spacing };
            Assert.AreEqual(0f, ClimbRules.LateralBand(onTheLine, -run, run, run, t, 1f), 0f, "a reach of zero means on the line only, not 'no limit'");
            Assert.AreEqual(ClimbRules.End.Foot, ClimbRules.JoinsAtEnd(onTheLine.Point(-run * 0.5f, 0f), onTheLine.AxisHorizontal, onTheLine, t, out _, out _), "and the line itself still joins");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(onTheLine.Point(-run * 0.5f, 0.01f), onTheLine.AxisHorizontal, onTheLine, t, out _, out _));
        }

        [Test]
        public void Reach_TakesTheSmallerNeighbour_AndStoredProfilesNeverIncrease()
        {
            float[] profile = { 0.10f, 0.06f, 0.08f, 0.02f };
            Assert.AreEqual(0.10f, ClimbRules.Reach(profile, Spacing, 0f), 0f, "on a sample");
            Assert.AreEqual(0.06f, ClimbRules.Reach(profile, Spacing, 0.004f), 0f, "between two: the smaller, not an interpolation");
            Assert.AreEqual(0.06f, ClimbRules.Reach(profile, Spacing, 0.015f), 0f);
            Assert.AreEqual(0.02f, ClimbRules.Reach(profile, Spacing, 0.5f), 0f, "past the last sample: the last");
            Assert.IsTrue(float.IsPositiveInfinity(ClimbRules.Reach(null, Spacing, 0.1f)), "no profile: no limit");
            float[] stored = ClimbRules.NonIncreasing(profile);
            CollectionAssert.AreEqual(new[] { 0.10f, 0.06f, 0.06f, 0.02f }, stored, "each sample is the smallest so far from the free end");
            CollectionAssert.AreEqual(new[] { 0.10f, 0.06f, 0.08f, 0.02f }, profile, "the model's own list is not changed");
        }

        [Test]
        public void TheEnvelope_NeverIncreasesTowardTheLip_AndClosesNoFasterThanTheGlideSlope()
        {
            // A pocket that opens and closes along the stretch, stored as the setup stores it.
            var raw = new float[Mathf.CeilToInt(run / Spacing) + 1];
            for (int i = 0; i < raw.Length; i++) raw[i] = 0.08f + 0.05f * Mathf.Sin(i * 0.7f);
            var pocket = new StairLane(lane.foot, lane.head, lane.width) { footReachLeft = ClimbRules.NonIncreasing(raw), footReachRight = Flat(1f, run), reachSpacing = Spacing };
            float before = float.PositiveInfinity, beforeOpen = float.PositiveInfinity;
            const float step = 0.001f;
            for (float u = -run; u <= 0f; u += step)
            {
                float left = ClimbRules.LateralBand(pocket, u, run, run, t, -1f), open = ClimbRules.LateralBand(pocket, u, run, run, t, 1f);
                Assert.LessOrEqual(left, before + Tol, $"the walled side widened at {u:F3}");
                Assert.LessOrEqual(open, beforeOpen + Tol, $"the open side widened at {u:F3}");
                // Where no wall binds, the closing is the glide slope at most.
                if (!float.IsPositiveInfinity(beforeOpen)) Assert.LessOrEqual(beforeOpen - open, slope * step + Tol, $"the open side closes faster than the glide slope at {u:F3}");
                before = left;
                beforeOpen = open;
            }
            Assert.AreEqual(0f, ClimbRules.LateralBand(pocket, 0f, run, run, t, -1f), 1e-3f, "zero at the lip");
        }

        [Test]
        public void WalkingIn_TheOffsetFollowsTheEnvelope_InsideTheFeetStepBound_AndHoldsWhenStopped()
        {
            float dt = 1f / 60f, offset = ClimbRules.LateralBand(lane, -run, run, run, t), lift = 0f, u = -run, worstStep = 0f;
            float started = offset;
            while (u < 0f)
            {
                float was = offset;
                u = Mathf.Min(0f, u + t.towerWalkSpeed * dt);
                ClimbRules.SettleOffsets(ref offset, ref lift, ClimbRules.LateralBand(lane, u, run, run, t, offset), t, dt);
                Assert.LessOrEqual(offset, was, "never outward");
                worstStep = Mathf.Max(worstStep, Mathf.Sqrt(Mathf.Pow(t.towerWalkSpeed * dt, 2f) + Mathf.Pow(was - offset, 2f)));
                if (u < -run * 0.5f) continue;
                // Stopped half-way: the envelope is the same every frame, so the offset is held.
                float held = offset;
                for (int i = 0; i < 120; i++) ClimbRules.SettleOffsets(ref held, ref lift, ClimbRules.LateralBand(lane, u, run, run, t, held), t, dt);
                Assert.AreEqual(offset, held, 0f, "standing still holds the offset");
            }
            Assert.AreEqual(0f, offset, 1e-3f, "on the line at the lip");
            Debug.Log($"ClimbEnvelopeTests: from {started:F3} m off the line the offset closes at most {slope:F2} m per metre walked; worst feet step {worstStep:F4} m at 60 FPS");
            const float feetStepSlack = 0.02f;   // spec R6: feet step <= speed in force x dt + 0.02 m
            Assert.LessOrEqual(worstStep, t.towerWalkSpeed * dt + feetStepSlack, "walking the envelope stays inside the feet-step bound (R6)");
            Assert.LessOrEqual(t.laneLateralSlack / t.laneTaperLength * t.stairAscendSpeed, t.stairAscendSpeed, "a flight's taper closes slower than the stair is climbed");
        }
    }
}
