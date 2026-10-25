using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.EditMode.Lookout
{
    /// <summary>
    /// The pure lane rules and the ClimbTuning asset (spec C1-C2, tower-design-spec.md §2.3, tower-td-note §6
    /// condition 1). Geometry here is a made-up flight at the standard's 12-in-9.25 pitch; the built lanes are
    /// checked by the lane-data tests.
    /// </summary>
    public class ClimbRulesTests
    {
        const float Rise = 12f, Going = 9.25f;      // the MC-39 stair, inches per step
        const float Tol = 1e-4f;
        const float Walk = 5f, Radius = 0.4f;       // live controller (World.unity); PlayMode re-reads them

        ClimbTuning t;
        StairLane lane;

        [SetUp]
        public void SetUp()
        {
            t = ScriptableObject.CreateInstance<ClimbTuning>();
            // 3 m of rise heading north-east from an arbitrary point.
            float rise = 3f, run = rise * Going / Rise;
            Vector3 foot = new Vector3(10f, 4f, -2f), heading = new Vector3(1f, 0f, 1f).normalized;
            lane = new StairLane(foot, foot + heading * run + Vector3.up * rise, 0.61f);
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(t);

        [Test]
        public void Tuning_DefaultsAreValid_AndBrokenNumbersAreRejected()
        {
            Assert.IsNull(t.Validate(Radius, Walk), t.Validate(Radius, Walk));
            t.towerCapsuleRadius = Radius + 0.01f;
            Assert.IsNotNull(t.Validate(Radius, Walk), "a climb capsule wider than the authored one");
            t.towerCapsuleRadius = 0.25f;
            t.laneFullSpeedAlong = t.laneAlongDeadband;
            Assert.IsNotNull(t.Validate(Radius, Walk), "full-speed threshold at the deadband");
            t.laneFullSpeedAlong = 0.5f;
            t.laneJoinAlong = t.laneAlongDeadband - 0.01f;
            Assert.IsNotNull(t.Validate(Radius, Walk), "join cone wider than the on-lane deadband");
            t.laneJoinAlong = 0.5f;
            t.laneJunctionAlong = t.laneJoinAlong - 0.01f;
            Assert.IsNotNull(t.Validate(Radius, Walk), "junction cone wider than 35 degrees");
            t.laneJunctionAlong = ClimbTuning.JunctionAlongFloor;
            Assert.IsNull(t.Validate(Radius, Walk), t.Validate(Radius, Walk));
            t.laneRunOutLength = t.towerCapsuleRadius + 0.05f + ClimbTuning.RunOutMargin - 0.01f;
            Assert.IsNotNull(t.Validate(Radius, Walk, 0.05f), "a run-out shorter than the capsule plus its margin");
        }

        [Test]
        public void Lane_Geometry_MatchesItsEnds()
        {
            Assert.AreEqual(Mathf.Atan2(Rise, Going) * Mathf.Rad2Deg, lane.Pitch, 1e-3f);
            Assert.AreEqual(0f, Vector3.Distance(lane.foot, lane.Point(0f, 0f)), Tol);
            Assert.AreEqual(0f, Vector3.Distance(lane.head, lane.Point(lane.Length, 0f)), Tol);
            Assert.AreEqual(0f, Vector3.Dot(lane.Right, lane.AxisHorizontal), Tol);
            Assert.AreEqual(1f, lane.Right.magnitude, Tol);
        }

        [Test]
        public void Point_RunInAndRunOut_AreLevelAtTheEndsHeight()
        {
            float d = t.laneRunOutLength;
            Assert.AreEqual(lane.foot.y, lane.Point(-d, 0f).y, Tol);
            Assert.AreEqual(lane.head.y, lane.Point(lane.Length + d, 0f).y, Tol);
            Assert.AreEqual(d, Vector3.Distance(lane.foot, lane.Point(-d, 0f)), Tol);
            Assert.AreEqual(d, Vector3.Distance(lane.head, lane.Point(lane.Length + d, 0f)), Tol);
        }

        [Test]
        public void Locate_IsTheInverseOfPoint_OnTheInclineAndBothRuns()
        {
            foreach (float u in new[] { -0.25f, 0f, 0.4f, lane.Length * 0.5f, lane.Length, lane.Length + 0.2f })
                foreach (float lateral in new[] { -0.2f, 0f, 0.05f })
                {
                    ClimbRules.Locate(lane.Point(u, lateral), lane, out float gotU, out float gotLateral, out float above);
                    Assert.AreEqual(u, gotU, 1e-3f, $"u at {u}, {lateral}");
                    Assert.AreEqual(lateral, gotLateral, 1e-3f, $"lateral at {u}, {lateral}");
                    Assert.AreEqual(0f, above, 1e-3f, $"height at {u}, {lateral}");
                }
        }

        [Test]
        public void Along_FacingUpTheFlightIsOne_AcrossIsZero_DownIsMinusOne()
        {
            Assert.AreEqual(1f, ClimbRules.Along(lane.AxisHorizontal, lane), Tol);
            Assert.AreEqual(-1f, ClimbRules.Along(-lane.AxisHorizontal, lane), Tol);
            Assert.AreEqual(0f, ClimbRules.Along(lane.Right, lane), Tol);
        }

        [Test]
        public void TargetSpeed_Deadband_FullSpeedWithinSixtyDegrees_AndDirection()
        {
            float mid = lane.Length * 0.5f, len = lane.Length;
            Assert.AreEqual(0f, ClimbRules.TargetSpeed(t.laneAlongDeadband - 0.01f, false, mid, len, t), Tol, "inside the deadband");
            Assert.AreEqual(0f, ClimbRules.TargetSpeed(0f, true, mid, len, t), Tol, "sideways input does nothing");
            Assert.AreEqual(t.stairAscendSpeed, ClimbRules.TargetSpeed(1f, false, mid, len, t), Tol);
            Assert.AreEqual(t.stairAscendSpeed, ClimbRules.TargetSpeed(t.laneFullSpeedAlong, false, mid, len, t), Tol, "full speed from the threshold");
            Assert.AreEqual(-t.stairDescendSpeed, ClimbRules.TargetSpeed(-1f, false, mid, len, t), Tol);
            Assert.AreEqual(t.stairAscendSpeed * t.stairSprintMultiplier, ClimbRules.TargetSpeed(1f, true, mid, len, t), Tol);
            float part = (t.laneAlongDeadband + t.laneFullSpeedAlong) * 0.5f;
            Assert.AreEqual(t.stairAscendSpeed * part / t.laneFullSpeedAlong, ClimbRules.TargetSpeed(part, false, mid, len, t), Tol, "scaled in between");
        }

        [Test, Ignore("Retired by name (#40, td part 14): guided path. ClimbRules is only used by ClimbArea, PlayerController.Climb and TrapDoor's lane gate; the lane stair-speed ramp is not used on the walkable tower (TowerWalkZones caps speed). Pre-existing number 4.073596.")]
        public void LevelSpeed_IsWalkSpeed_RampedToStairSpeedWhereTheLevelMeetsTheIncline()
        {
            float stair = t.stairAscendSpeed, a = t.stairAcceleration;
            Assert.AreEqual(stair, ClimbRules.LevelSpeed(0f, stair, t), Tol, "at the first tread: stair speed");
            Assert.AreEqual(t.towerWalkSpeed, ClimbRules.LevelSpeed(10f, stair, t), Tol, "far out on the level: walk speed");
            float d = 0.1f;
            Assert.AreEqual(Mathf.Sqrt(stair * stair + 2f * a * d), ClimbRules.LevelSpeed(d, stair, t), Tol, "in between: what stairAcceleration allows over that distance");
            // A function of position that never asks for more deceleration than stairAcceleration: walk the run toward the incline.
            float dt = 1f / 240f, u = t.laneRunOutLength, speed = ClimbRules.LevelSpeed(u, stair, t), worst = 0f;
            while (u > 0f)
            {
                u -= speed * dt;
                float next = ClimbRules.LevelSpeed(Mathf.Max(u, 0f), stair, t);
                worst = Mathf.Max(worst, (speed - next) / dt);
                speed = next;
            }
            Assert.LessOrEqual(worst, a * 1.01f, "deceleration along the run never exceeds stairAcceleration");
            Assert.AreEqual(stair, speed, 1e-3f, "arrives at the first tread at stair speed");
            // On the lane function: level speed on the run, stair speed on the flight, both ends.
            float len = lane.Length, run = t.laneRunOutLength;
            Assert.AreEqual(ClimbRules.LevelSpeed(run, t.stairAscendSpeed, t), ClimbRules.TargetSpeed(1f, false, len + run, len, t), Tol, "head run, heading out");
            Assert.AreEqual(-ClimbRules.LevelSpeed(run, t.stairDescendSpeed, t), ClimbRules.TargetSpeed(-1f, false, len + run, len, t), Tol, "head run, heading back to the flight");
            Assert.AreEqual(ClimbRules.LevelSpeed(run, t.stairAscendSpeed, t), ClimbRules.TargetSpeed(1f, false, -run, len, t), Tol, "foot run, heading for the flight");
            Assert.AreEqual(ClimbRules.LevelSpeed(run, t.stairAscendSpeed, t), ClimbRules.TargetSpeed(1f, true, -run, len, t), Tol, "sprint has no effect on the level at the default multiplier");
        }

        [Test]
        public void Step_ReachesSpeedByAcceleration_AndStopsWithinTheSpecDistance()
        {
            float u = 1f, speed = 0f, dt = 1f / 60f;
            for (int i = 0; i < 60; i++) u = ClimbRules.Step(u, ref speed, t.stairAscendSpeed, t.stairAcceleration, 0f, lane.Length, dt);
            Assert.AreEqual(t.stairAscendSpeed, speed, Tol, "at speed after a second");
            float released = u;
            for (int i = 0; i < 60; i++) u = ClimbRules.Step(u, ref speed, 0f, t.stairAcceleration, 0f, lane.Length, dt);
            Assert.AreEqual(0f, speed, Tol);
            Assert.LessOrEqual(u - released, t.stairAscendSpeed * t.stairAscendSpeed / (2f * t.stairAcceleration) + t.stairAscendSpeed * dt,
                "stop distance is v^2 / 2a plus one frame");
            float held = u;
            for (int i = 0; i < 600; i++) u = ClimbRules.Step(u, ref speed, 0f, t.stairAcceleration, 0f, lane.Length, dt);
            Assert.AreEqual(held, u, 0f, "no creep while released (R3)");
        }

        const float Linked = -1f;   // a negative run marks an end linked to another lane

        [Test]
        public void LateralBand_OnAFlight_IsTheSlackTaperingToZeroAtBothEnds()
        {
            float run = t.laneRunOutLength, len = lane.Length, taper = t.laneTaperLength;
            Assert.AreEqual(t.laneLateralSlack, ClimbRules.LateralBand(lane, len * 0.5f, run, run, t), Tol, "mid-flight: the full slack");
            Assert.AreEqual(t.laneLateralSlack, ClimbRules.LateralBand(lane, taper, run, run, t), Tol, "one taper length from the foot: still the full slack");
            Assert.AreEqual(t.laneLateralSlack * 0.5f, ClimbRules.LateralBand(lane, taper * 0.5f, run, run, t), Tol, "half-way through the taper");
            Assert.AreEqual(t.laneLateralSlack * 0.5f, ClimbRules.LateralBand(lane, len - taper * 0.5f, run, run, t), Tol, "and the same before the head");
            Assert.AreEqual(0f, ClimbRules.LateralBand(lane, 0f, run, run, t), Tol, "on the line at the foot");
            Assert.AreEqual(0f, ClimbRules.LateralBand(lane, len, run, run, t), Tol, "on the line at the head");
            Assert.AreEqual(0f, ClimbRules.LateralBand(lane, len, run, Linked, t), Tol, "and at a head linked to another lane");
        }

        [Test]
        public void TaperProblem_WhenTwoTapersWouldOverlapOnAShortFlight()
        {
            Assert.IsNull(ClimbRules.TaperProblem(new[] { lane, ExitFromHead() }, t), "a level lane has no taper to fit");
            Vector3 up = lane.Axis;
            var stub = new StairLane(lane.foot, lane.foot + up * (t.laneTaperLength * 2f - 0.01f), lane.width);
            Assert.IsNotNull(ClimbRules.TaperProblem(new[] { lane, stub }, t), "a flight shorter than two tapers");
        }

        [Test]
        public void SettleOffsets_ClampsIntoTheBand_HoldsWhenStandingStill_AndNeverPushesOutward()
        {
            float dt = 1f / 60f, lift = 0f;
            float offset = 0.2f;
            ClimbRules.SettleOffsets(ref offset, ref lift, 0.3f, t, dt);
            Assert.AreEqual(0.2f, offset, 0f, "inside the band: untouched");
            for (int i = 0; i < 600; i++) ClimbRules.SettleOffsets(ref offset, ref lift, 0.3f, t, dt);
            Assert.AreEqual(0.2f, offset, 0f, "standing still (the band is the same every frame): the offset is held");
            ClimbRules.SettleOffsets(ref offset, ref lift, 0.05f, t, dt);
            Assert.AreEqual(0.05f, offset, 0f, "advanced into a narrower band: brought to its edge");
            ClimbRules.SettleOffsets(ref offset, ref lift, 0.3f, t, dt);
            Assert.AreEqual(0.05f, offset, 0f, "the band widened again (the player turned back): never pushed outward");
            float negative = -0.2f;
            ClimbRules.SettleOffsets(ref negative, ref lift, 0f, t, dt);
            Assert.AreEqual(0f, negative, 0f, "on the line where the band is zero");
            float raised = 0.1f;
            ClimbRules.SettleOffsets(ref offset, ref raised, 0.3f, t, dt);
            Assert.AreEqual(0.1f - t.stairAscendSpeed * dt, raised, Tol, "the height offset still closes at stair speed");
        }

        [Test]
        public void Step_IsFrameRateIndependentAtSpeed()
        {
            float seconds = 4f;
            float slow = Travel(1f / 30f, seconds), fast = Travel(1f / 240f, seconds);
            Assert.AreEqual(slow, fast, 0.02f * slow, "distance at 30 and 240 FPS within 2% (R10)");
        }

        float Travel(float dt, float seconds)
        {
            float u = 0f, speed = 0f;
            int frames = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < frames; i++) u = ClimbRules.Step(u, ref speed, t.stairAscendSpeed, t.stairAcceleration, 0f, 100f, dt);
            return u;
        }

        [Test]
        public void Step_StopsAtTheGate_AndAtTheBottom()
        {
            float gate = lane.Length * 0.5f, u = gate - 0.01f, speed = t.stairAscendSpeed;
            for (int i = 0; i < 30; i++) u = ClimbRules.Step(u, ref speed, t.stairAscendSpeed, t.stairAcceleration, 0f, gate, 1f / 60f);
            Assert.AreEqual(gate, u, Tol);
            Assert.AreEqual(0f, speed, Tol, "speed is dropped at the gate");
            u = 0.01f; speed = -t.stairDescendSpeed;
            for (int i = 0; i < 30; i++) u = ClimbRules.Step(u, ref speed, -t.stairDescendSpeed, t.stairAcceleration, 0f, gate, 1f / 60f);
            Assert.AreEqual(0f, u, Tol);
        }

        [Test]
        public void JoinsAtEnd_AnywhereOnTheFinalLevelStretch_InsideTheFunnel_WalkingIn()
        {
            Vector3 up = lane.AxisHorizontal, down = -lane.AxisHorizontal;
            float run = t.laneRunOutLength, half = ClimbRules.LateralBand(lane, -run, run, run, t), len = lane.Length;
            Assert.AreEqual(ClimbRules.End.Foot, ClimbRules.JoinsAtEnd(lane.Point(-run * 0.5f, 0f), up, lane, t, out _, out _));
            Assert.AreEqual(ClimbRules.End.Foot, ClimbRules.JoinsAtEnd(lane.Point(-run, half - 0.01f), up, lane, t, out _, out _), "at the free end, anywhere across the mouth");
            Assert.AreEqual(ClimbRules.End.Foot, ClimbRules.JoinsAtEnd(lane.Point(-0.02f, 0f), up, lane, t, out _, out _), "right up to the lip, on the line: the whole run is the mouth");
            Assert.AreEqual(ClimbRules.End.Head, ClimbRules.JoinsAtEnd(lane.Point(len + run * 0.5f, 0f), down, lane, t, out _, out _));
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(-run * 0.5f, 0f), down, lane, t, out _, out _), "walking away from the foot");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(-run * 0.5f, 0f), lane.Right, lane, t, out _, out _), "walking across the mouth");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(-run - 0.05f, 0f), up, lane, t, out _, out _), "before the free end");
            // Crossing a landing toward the next flight heads a little back down this one: inside the deadband-to-cone gap, no join.
            float gap = (t.laneAlongDeadband + t.laneJoinAlong) * 0.5f;
            Vector3 crossing = (down * gap + lane.Right * Mathf.Sqrt(1f - gap * gap)).normalized;
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(len + run * 0.5f, 0f), crossing, lane, t, out _, out _), "crossing the landing");
            // The envelope narrows toward the lip, and a join is always inside it (never a snap).
            float midway = ClimbRules.LateralBand(lane, -run * 0.5f, run, run, t);
            Assert.Less(midway, half, "narrower half-way along");
            half = midway * 2f;
            Assert.AreEqual(ClimbRules.End.Foot, ClimbRules.JoinsAtEnd(lane.Point(-run * 0.5f, half * 0.5f - 0.01f), up, lane, t, out _, out float lateral));
            Assert.LessOrEqual(Mathf.Abs(lateral), ClimbRules.LateralBand(lane, -run * 0.5f, run, run, t) + ClimbRules.BandEpsilon, "joined inside the band");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(-run * 0.5f, half * 0.5f + 0.01f), up, lane, t, out _, out _), "outside the funnel half-way along");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(-run, ClimbRules.LateralBand(lane, -run, run, run, t) + 0.01f), up, lane, t, out _, out _), "beside the mouth at the free end");
            Vector3 high = lane.Point(-run * 0.5f, 0f) + Vector3.up * (t.laneMouthHeight + 0.01f);
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(high, up, lane, t, out _, out _), "above the mouth's height band");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(len * 0.5f, 0f), up, lane, t, out _, out _), "mid-flight is not an end");
            // A level lane with a free head (the cab's exit lane): the whole lane is the mouth, inside its funnel.
            StairLane exit = ExitFromHead();
            Vector3 toJunction = -exit.AxisHorizontal;
            Assert.AreEqual(ClimbRules.End.Head, ClimbRules.JoinsAtEnd(exit.Point(exit.Length * 0.5f, 0f), toJunction, exit, t, Linked, 0f, 0f, out _, out _), "half-way along the exit lane, on its line");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(exit.Point(exit.Length * 0.5f, ClimbRules.LateralBand(exit, exit.Length * 0.5f, Linked, 0f, t) + 0.01f), toJunction, exit, t, Linked, 0f, 0f, out _, out _), "outside its envelope");
        }

        [Test]
        public void JoinsFromAbove_OnlyInsideThePrismAtOrJustBelowTheLine()
        {
            Vector3 mid = lane.Point(lane.Length * 0.5f, 0f);
            Assert.IsTrue(ClimbRules.JoinsFromAbove(mid + Vector3.down * 0.01f, lane, t.laneCatchBand, out float u, out _));
            Assert.AreEqual(lane.Length * 0.5f, u, 0.02f);
            Assert.IsFalse(ClimbRules.JoinsFromAbove(mid + Vector3.up * 0.05f, lane, t.laneCatchBand, out _, out _), "still above the line: keep falling");
            Assert.IsFalse(ClimbRules.JoinsFromAbove(mid + Vector3.down * (t.laneCatchBand + 0.4f), lane, t.laneCatchBand, out _, out _), "far below");
            Assert.IsFalse(ClimbRules.JoinsFromAbove(mid + lane.Right * (lane.width * 0.5f + 0.01f) + Vector3.down * 0.01f, lane, t.laneCatchBand, out _, out _), "beside the lane");
        }

        [Test]
        public void LeavesAt_OnlyAtTheFarEndOfTheLevelRun_WhileHeadingOut()
        {
            float len = lane.Length, footRun = ClimbRules.FootRun(lane, t), headRun = ClimbRules.HeadRun(lane, t);
            Assert.AreEqual(t.laneRunOutLength, footRun, Tol, "with room to spare the run is the tuned run-out length");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.LeavesAt(len * 0.5f, 0f, len, footRun, headRun), "standing mid-flight");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.LeavesAt(len * 0.5f, -1f, len, footRun, headRun));
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.LeavesAt(len + 0.01f, 1f, len, footRun, headRun), "just past the head: still on the lip, no hand-over");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.LeavesAt(len + headRun * 0.5f, 0f, len, footRun, headRun), "stopped part-way along the run: holds on the lane");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.LeavesAt(len + headRun, 0f, len, footRun, headRun), "stopped at the end of the run: holds until they walk on");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.LeavesAt(len + headRun, -1f, len, footRun, headRun), "turned back at the end of the run");
            Assert.AreEqual(ClimbRules.End.Head, ClimbRules.LeavesAt(len + headRun, 1f, len, footRun, headRun), "the end of the run, walking on");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.LeavesAt(-footRun * 0.5f, -1f, len, footRun, headRun), "part-way along the run below the foot");
            Assert.AreEqual(ClimbRules.End.Foot, ClimbRules.LeavesAt(-footRun, -1f, len, footRun, headRun));
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.LeavesAt(-footRun, 1f, len, footRun, headRun), "at the run's end, walking back in");
        }

        [Test]
        public void RunLength_IsTheTunedRunOut_OrLessWhereTheModelHasLessFloor()
        {
            var tight = new StairLane(lane.foot, lane.head, lane.width, false, footRunRoom: 0.1f, headRunRoom: 0f);
            Assert.AreEqual(0.1f, ClimbRules.FootRun(tight, t), Tol);
            Assert.AreEqual(0f, ClimbRules.HeadRun(tight, t), Tol, "no floor beyond the head: the hand-over is at the head itself");
            Assert.AreEqual(ClimbRules.End.Head, ClimbRules.LeavesAt(tight.Length, 1f, tight.Length, 0.1f, 0f));
            var roomy = new StairLane(lane.foot, lane.head, lane.width, false, footRunRoom: 5f, headRunRoom: 5f);
            Assert.AreEqual(t.laneRunOutLength, ClimbRules.HeadRun(roomy, t), Tol, "never longer than the tuned run-out length");
        }

        /// <summary>A flat lane heading west from the flight's head: the cab's step off the top flight (a 90 degree junction).</summary>
        StairLane ExitFromHead(float length = 0.65f)
        {
            Vector3 west = Vector3.Cross(Vector3.up, lane.AxisHorizontal);
            return new StairLane(lane.head, lane.head + west * length, lane.width, false, 0f, 0f);
        }

        [Test]
        public void Continues_AtAJunction_OnlyWhenTheInputPointsAlongTheNextLane()
        {
            StairLane exit = ExitFromHead();
            Vector3 alongExit = exit.AxisHorizontal, upFlight = lane.AxisHorizontal;
            Assert.IsTrue(ClimbRules.IsLevel(exit));
            Assert.IsFalse(ClimbRules.IsLevel(lane));
            // Arriving at the head of the flight, the exit lane is entered at its foot.
            Assert.IsFalse(ClimbRules.Continues(upFlight, exit, ClimbRules.End.Foot, t), "W on the old heading: the player stands at the junction");
            Assert.IsTrue(ClimbRules.Continues(alongExit, exit, ClimbRules.End.Foot, t), "turned along the exit lane");
            float cone = Mathf.Acos(t.laneJunctionAlong) * Mathf.Rad2Deg;
            Vector3 justInside = Quaternion.Euler(0f, cone - 2f, 0f) * alongExit, justOutside = Quaternion.Euler(0f, cone + 2f, 0f) * alongExit;
            Assert.IsTrue(ClimbRules.Continues(justInside, exit, ClimbRules.End.Foot, t), "just inside the join cone");
            Assert.IsFalse(ClimbRules.Continues(justOutside, exit, ClimbRules.End.Foot, t), "just outside the join cone");
            Assert.IsFalse(ClimbRules.Continues(Vector3.zero, exit, ClimbRules.End.Foot, t), "no input");
            // Coming back along the exit lane to the junction, the flight is entered at its head: the input must point down it.
            Assert.IsTrue(ClimbRules.Continues(-upFlight, lane, ClimbRules.End.Head, t), "turned down the flight");
            Assert.IsFalse(ClimbRules.Continues(-alongExit, lane, ClimbRules.End.Head, t), "still heading along the exit lane: stands");
            Assert.IsFalse(ClimbRules.Continues(upFlight, lane, ClimbRules.End.Head, t), "pointing up the flight from its head");
        }

        [Test]
        public void LevelLane_MovesAtWalkSpeed_RampedWhereItMeetsTheFlight()
        {
            StairLane exit = ExitFromHead();
            float len = exit.Length;
            // Junction at the exit lane's foot, with the flight's head: heading for the junction means going down the flight.
            float away = ClimbRules.LevelLaneTargetSpeed(1f, false, len, len, ClimbRules.End.Foot, ClimbRules.End.Head, t);
            Assert.AreEqual(t.towerWalkSpeed, away, Tol, "walk speed at the far end, heading away");
            float atJunctionLeaving = ClimbRules.LevelLaneTargetSpeed(1f, false, 0f, len, ClimbRules.End.Foot, ClimbRules.End.Head, t);
            Assert.AreEqual(t.stairAscendSpeed, atJunctionLeaving, Tol, "leaving the junction: starts from the ascent speed");
            float atJunctionArriving = ClimbRules.LevelLaneTargetSpeed(-1f, false, 0f, len, ClimbRules.End.Foot, ClimbRules.End.Head, t);
            Assert.AreEqual(-t.stairDescendSpeed, atJunctionArriving, Tol, "arriving at the junction: down to the descent speed");
            Assert.AreEqual(0f, ClimbRules.LevelLaneTargetSpeed(t.laneAlongDeadband - 0.01f, false, len * 0.5f, len, ClimbRules.End.Foot, ClimbRules.End.Head, t), Tol, "deadband");
            Assert.AreEqual(t.towerWalkSpeed, ClimbRules.LevelLaneTargetSpeed(1f, false, 0f, len, ClimbRules.End.None, ClimbRules.End.None, t), Tol, "a level lane that meets no flight: walk speed everywhere");
            // A run-out of two segments: on the second, the ramp counts the first segment's length too, so the speed is
            // continuous across the level-to-level link and the braking distance is the whole chain's.
            float first = 0.03f, second = 0.379f;   // example lengths, as on a landing whose run-out bends
            float endOfFirst = ClimbRules.LevelLaneTargetSpeed(1f, false, first, first, ClimbRules.End.Foot, ClimbRules.End.Head, t);
            float startOfSecond = ClimbRules.LevelLaneTargetSpeed(1f, false, 0f, second, ClimbRules.End.Foot, ClimbRules.End.Head, t, levelBetween: first);
            Assert.AreEqual(endOfFirst, startOfSecond, Tol, "the ramp is continuous across a level-to-level link");
            Assert.AreEqual(ClimbRules.LevelSpeed(first + 0.2f, t.stairAscendSpeed, t),
                ClimbRules.LevelLaneTargetSpeed(1f, false, 0.2f, second, ClimbRules.End.Foot, ClimbRules.End.Head, t, levelBetween: first), Tol, "measured along the whole chain");
            Assert.Less(startOfSecond, t.towerWalkSpeed, "3 cm from the stair the player is nowhere near walk speed");
        }

        [Test]
        public void JoinsAtEnd_ALinkedEndHasNoMouth_AndAJoinNeverStartsBeyondTheFreeEnd()
        {
            Vector3 down = -lane.AxisHorizontal;
            const float skin = 0.05f;   // the live controller's skinWidth; the controller passes its own
            float run = t.laneRunOutLength, len = lane.Length;
            Vector3 onHeadRun = lane.Point(len + run * 0.5f, 0f);
            Assert.AreEqual(ClimbRules.End.Head, ClimbRules.JoinsAtEnd(onHeadRun, down, lane, t, run, run, skin, out float u, out _));
            Assert.AreEqual(len + run * 0.5f, u, 1e-3f, "the join starts where the player is");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(onHeadRun, down, lane, t, run, Linked, skin, out _, out _), "linked head: joined only by passing from the linked lane");
            Vector3 justPast = lane.Point(len + run + skin * 0.5f, 0f);
            Assert.AreEqual(ClimbRules.End.Head, ClimbRules.JoinsAtEnd(justPast, down, lane, t, run, run, skin, out u, out _), "within the controller's skin of the free end");
            Assert.AreEqual(len + run, u, Tol, "and it starts at the free end, not beyond it");
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(len + run + skin + 0.02f, 0f), down, lane, t, run, run, skin, out _, out _), "beyond the free end and its slop");
            // A head with no run at all (no floor beyond it): only within the slop of the head itself.
            Assert.AreEqual(ClimbRules.End.None, ClimbRules.JoinsAtEnd(lane.Point(len + 0.2f, 0f), down, lane, t, run, 0f, skin, out _, out _), "beyond a head with no run");
        }

        [Test]
        public void RejoinAllowed_AfterTheDistanceOrARelease()
        {
            Assert.IsFalse(ClimbRules.RejoinAllowed(t.laneRejoinDistance - 0.01f, t.laneRejoinDistance, false));
            Assert.IsTrue(ClimbRules.RejoinAllowed(t.laneRejoinDistance, t.laneRejoinDistance, false));
            Assert.IsTrue(ClimbRules.RejoinAllowed(0f, t.laneRejoinDistance, true));
        }

        [Test]
        public void GateUnder_KeepsTheHeadBelowTheCeiling()
        {
            float height = 1.8f, headroom = 0.15f;
            float gate = ClimbRules.GateUnder(lane, lane.head.y, height, headroom);
            Assert.AreEqual(lane.head.y - headroom, lane.Point(gate, 0f).y + height, 1e-3f);
            Assert.Less(gate, lane.Length);
            Assert.AreEqual(lane.Length, ClimbRules.GateUnder(lane, lane.head.y + height + headroom + 1f, height, headroom), Tol, "a high ceiling doesn't gate");
        }

        [Test]
        public void ClimbSeconds_IsTheSpecFormula()
        {
            float lanes = 33.2f, landings = 7f * 0.5f;
            Assert.AreEqual(lanes / t.stairAscendSpeed + landings / t.towerWalkSpeed, ClimbRules.ClimbSeconds(lanes, landings, t), Tol);
        }
    }
}
