using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using Kind = WashedAshore.Tests.PlayMode.PlayerStreams.Kind;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// #46 P2 and P3 (spec 94f34aa6, 7 T2a and T3): the same command stream gives the same end state at 30, 60 and 144 fps, and a recorded
    /// stream replayed from a saved state gives the same result again. Known-fail cases are [Explicit] [Category("KnownFail46")]; each must
    /// FAIL and prints the value that failed.
    /// </summary>
    public class PlayerReplayTests : PlayerTickFixture
    {
        static readonly float[] Rates = { 1f / 30f, 1f / 60f, 1f / 144f };

        static string P4(Vector3 v) => $"({v.x:F4}, {v.y:F4}, {v.z:F4})";

        // ---- T2a ---------------------------------------------------------------------------------------------------

        [UnityTest, Timeout(1800000)]
        public IEnumerator T2a_FixedTick_SameEndAt30_60_144([Values] Kind kind) => SameEnd(kind);

        /// <summary>KF-T2a: one step per frame with the frame time (the old behaviour); the three end positions must differ by more than 1 cm.</summary>
        [UnityTest, Timeout(1800000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T2a_VariableStep([Values] Kind kind)
        {
            PlayerController.TestVariableStep = true;
            yield return SameEnd(kind);
        }

        /// <summary>KF-T2a-W: the swim case with the tick reading the frame-cached water surface; expected to FAIL (if it does not, W cannot see frame-time water at these rates).</summary>
        [UnityTest, Timeout(1800000), Explicit("Known-fail: expected to FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T2a_W_FrameWaterTime()
        {
            PlayerController.TestFrameWaterTime = true;
            yield return SameEnd(Kind.Swim);
        }

        IEnumerator SameEnd(Kind kind)
        {
            var outcomes = new List<StreamOutcome>();
            foreach (float rate in Rates)
            {
                SetFrameTime(rate);
                yield return Place(kind);
                yield return RunStream(kind, null, -1, null);
                outcomes.Add(LastOutcome);
                var o = LastOutcome;
                Debug.Log($"T2a {kind} frame time {rate:F5}: |SimVelocity| {o.speed:F3} at the measurement tick; position {P4(o.position)} yaw {o.yaw:F3} stance {o.stance} ticks {o.ticks} dropped {o.dropped} water {o.waterMode} snaps {o.snaps} wrongWater {o.wrongWater}");
            }
            float worst = 0f;
            for (int i = 0; i < outcomes.Count; i++)
                for (int j = i + 1; j < outcomes.Count; j++)
                    worst = Mathf.Max(worst, Vector3.Distance(outcomes[i].position, outcomes[j].position));
            float yawSpread = outcomes.Max(o => Mathf.Abs(Mathf.DeltaAngle(o.yaw, outcomes[0].yaw)));
            Debug.Log($"T2a {kind}: largest pairwise end distance {worst:F4} m, yaw spread {yawSpread:F4} deg");
            // The position and yaw checks come first (td-46 r1 rulings A): a known-fail that moves the end state must fail on the miss, with its value.
            Assert.LessOrEqual(worst, 0.010f, $"{kind}: the end positions differ by {worst:F4} m (limit 0.010): {string.Join(" | ", outcomes.Select(o => P4(o.position)))}");
            Assert.LessOrEqual(yawSpread, 0.01f, $"{kind}: the end yaws differ by {yawSpread:F4} deg");
            foreach (var o in outcomes)
            {
                Assert.AreEqual(PlayerStreams.LengthOf(kind), o.ticks, "the stream's exact tick count ran (360, tower 180)");
                if (kind == Kind.Tower || kind == Kind.TowerExit) Assert.GreaterOrEqual(o.speed, 1f, "the tower player is still moving at the measurement tick");
                Assert.AreEqual(0L, o.dropped, "no ticks were dropped");
                Assert.AreEqual(0, o.snaps, "the path stayed clear of the bounds line");
                Assert.AreEqual(0, o.wrongWater, "the water mode was the stream's own on every tick");
                Assert.AreEqual(outcomes[0].stance, o.stance, "same stance at the end");
            }
            if (kind == Kind.Tower)
                foreach (var o in outcomes)
                    Assert.Greater(Vector3.Distance(o.position, new Vector3(549.2247f, 95.1156f, -228.1646f)), 0.01f, "the tower stream must end before the dead point (the old stop at the F01 head wall)");
        }

        // ---- T3 ----------------------------------------------------------------------------------------------------

        static int SnapshotTick(Kind kind) => kind == Kind.Ground ? 155 : 120;

        [UnityTest, Timeout(1800000)]
        public IEnumerator T3_Replay_FromSnapshot_Within1mm([Values] Kind kind) => Replay(kind, ReplayMutation.None);

        /// <summary>KF-T3a: the snapshot is restored with both velocities zeroed; the replay must miss by more than 1 mm.</summary>
        [UnityTest, Timeout(1800000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T3a_ZeroedVelocities([Values] Kind kind) => Replay(kind, ReplayMutation.ZeroVelocities);

        /// <summary>KF-T3b: the crouch of stream tick 261 is moved to 262 in the replay (ground stream); the replay must miss.</summary>
        [UnityTest, Timeout(1800000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T3b_ShiftedCrouch() => Replay(Kind.Ground, ReplayMutation.ShiftCrouch);

        /// <summary>KF-T3c: the swim replay with the tick reading the frame-cached water surface (every replayed tick in one frame); the replay must miss.</summary>
        [UnityTest, Timeout(1800000), Explicit("Known-fail: expected to FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T3c_W_FrameWaterTime() => Replay(Kind.Swim, ReplayMutation.FrameWaterTime);

        /// <summary>KF-T3d: on the tower-exit stream (S0 slimmed inside the footprint, then off the tower) the snapshot is restored with the zone's saved capsule values and slimmed flag cleared; the exit then restores a wrong capsule and the tick-by-tick trace must miss.</summary>
        [UnityTest, Timeout(1800000), Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public IEnumerator KnownFail_T3d_ClearedTowerZone() => Replay(Kind.TowerExit, ReplayMutation.ClearTowerZone);

        enum ReplayMutation { None, ZeroVelocities, ShiftCrouch, FrameWaterTime, ClearTowerZone }

        IEnumerator Replay(Kind kind, ReplayMutation mutation)
        {
            SetFrameTime(1f / 144f);
            yield return Place(kind);
            var executed = new List<PlayerCommand>();
            PlayerSimState s0 = default;
            bool have = false;
            var liveTrace = new Dictionary<int, Vector3>();   // tick -> sim position after that tick (every tick, to compare tick by tick)
            System.Action<PlayerCommand> liveRec = c => liveTrace[c.Tick] = player.transform.position;
            player.CommandExecuted += liveRec;
            yield return RunStream(kind, executed, SnapshotTick(kind) , () => { s0 = player.CaptureState(); have = true; });
            player.CommandExecuted -= liveRec;
            Assert.IsTrue(have, "the snapshot tick was reached");
            PlayerSimState s1 = player.CaptureState();
            if (kind == Kind.TowerExit)
            {
                Assert.IsTrue(s0.towerZone.slimmed, "TowerExit: S0 is inside the footprint with the capsule slimmed");
                Assert.IsFalse(s1.towerZone.slimmed, "TowerExit: the stream left the footprint after S0 (the zone restored the saved capsule)");
                Debug.Log($"T3 TowerExit: S0 capsule radius {s0.capsuleRadius:F3} height {s0.capsuleHeight:F3} slimmed; live end capsule radius {s1.capsuleRadius:F3} height {s1.capsuleHeight:F3}");
            }
            var live = LastOutcome;
            Assert.AreEqual(PlayerStreams.LengthOf(kind), live.ticks, "the live run ran the stream's exact tick count (360, tower 180)");
            if (kind == Kind.Tower || kind == Kind.TowerExit) Assert.GreaterOrEqual(live.speed, 1f, "the tower player is still moving at the measurement tick");

            // The commands after the snapshot, in order.
            int startTick = executed[0].Tick - 1;
            var tail = executed.Where(c => c.Tick - startTick > SnapshotTick(kind)).ToList();
            if (mutation == ReplayMutation.ShiftCrouch)
            {
                int i = tail.FindIndex(c => c.Stance == WashedAshore.Gameplay.StanceKey.Crouch && c.Tick - startTick == 261);
                Assert.GreaterOrEqual(i, 0, "the tick-261 crouch is in the recorded tail");
                var a = tail[i]; var b = tail[i + 1];
                var moved = a; moved.Stance = WashedAshore.Gameplay.StanceKey.None;
                var shifted = b; shifted.Stance = WashedAshore.Gameplay.StanceKey.Crouch;
                tail[i] = moved; tail[i + 1] = shifted;
            }
            if (mutation == ReplayMutation.FrameWaterTime) PlayerController.TestFrameWaterTime = true;
            player.AutoTick = false;
            var first = RestoreAndReplay(s0, tail, mutation, liveTrace);
            var second = RestoreAndReplay(s0, tail, mutation, liveTrace);
            Debug.Log($"T3 {kind} {mutation}: live end {P4(s1.position)}; replay 1 {P4(first.position)}; replay 2 {P4(second.position)}");
            foreach (var r in new[] { first, second }) CompareStates(kind, s1, r);
        }

        PlayerSimState RestoreAndReplay(PlayerSimState s0, List<PlayerCommand> tail, ReplayMutation mutation, Dictionary<int, Vector3> liveTrace)
        {
            var from = s0;
            if (mutation == ReplayMutation.ZeroVelocities) { from.horizontalVelocity = Vector3.zero; from.verticalVelocity = 0f; }
            if (mutation == ReplayMutation.ClearTowerZone)
            {
                from.towerZone.slimmed = false;
                from.towerZone.savedRadius = 0f; from.towerZone.savedHeight = 0f; from.towerZone.savedStep = 0f;
            }
            player.RestoreState(from);
            // Every replayed tick must land within 1 mm of the live tick (the end state alone cannot see a start difference that the
            // stream later washes out, for example a run into a stop).
            float worst = 0f; int worstTick = -1, firstBad = -1; Vector3 firstLive = default, firstGot = default;
            System.Action<PlayerCommand> trace = c =>
            {
                if (!liveTrace.TryGetValue(c.Tick, out var want)) return;
                float miss = Vector3.Distance(want, player.transform.position);
                if (miss > worst) { worst = miss; worstTick = c.Tick; }
                if (miss > 0.001f && firstBad < 0) { firstBad = c.Tick; firstLive = want; firstGot = player.transform.position; }
            };
            player.CommandExecuted += trace;
            foreach (var c in tail) player.Simulate(c);
            player.CommandExecuted -= trace;
            var endState = player.CaptureState();
            Debug.Log($"T3 trace {mutation}: end capsule radius {endState.capsuleRadius:F3} height {endState.capsuleHeight:F3} step {endState.stepOffset:F3}; worst per-tick miss {worst * 1000f:F3} mm at tick {worstTick}" + (firstBad >= 0 ? $"; first tick over 1 mm: {firstBad - s0.tick} ticks after the snapshot" : "; no tick over 1 mm"));
            Assert.LessOrEqual(worst, 0.001f, $"tick-by-tick replay off by {worst * 1000f:F3} mm at tick {worstTick}; first over 1 mm at {(firstBad >= 0 ? (firstBad - s0.tick).ToString() : "-")} ticks after the snapshot: live {P4(firstLive)} replay {P4(firstGot)}");
            return player.CaptureState();
        }

        static void CompareStates(Kind kind, PlayerSimState want, PlayerSimState got)
        {
            float miss = Vector3.Distance(want.position, got.position);
            Assert.LessOrEqual(miss, 0.001f, $"{kind}: replay end position off by {miss * 1000f:F3} mm: live {P4(want.position)} replay {P4(got.position)}");
            Assert.LessOrEqual((want.horizontalVelocity - got.horizontalVelocity).magnitude, 0.001f, $"{kind}: horizontal velocity {want.horizontalVelocity} vs {got.horizontalVelocity}");
            Assert.LessOrEqual(Mathf.Abs(want.verticalVelocity - got.verticalVelocity), 0.001f, $"{kind}: vertical velocity {want.verticalVelocity} vs {got.verticalVelocity}");
            Assert.AreEqual(want.stance, got.stance, $"{kind}: stance");
            Assert.AreEqual(want.waterMode, got.waterMode, $"{kind}: water mode");
            Assert.AreEqual(want.grounded, got.grounded, $"{kind}: grounded");
            Assert.AreEqual(want.towerZone.OnRamp, got.towerZone.OnRamp, $"{kind}: OnRamp");
            Assert.AreEqual(want.towerZone.InFlightZone, got.towerZone.InFlightZone, $"{kind}: InFlightZone");
            Assert.AreEqual(want.towerZone.InFootprint, got.towerZone.InFootprint, $"{kind}: InFootprint");
            Assert.AreEqual(want.towerZone.slimmed, got.towerZone.slimmed, $"{kind}: slimmed");
            Assert.AreEqual(want.capsuleRadius, got.capsuleRadius, 1e-4f, $"{kind}: capsule radius");
            Assert.AreEqual(want.capsuleHeight, got.capsuleHeight, 1e-4f, $"{kind}: capsule height");
        }
    }
}
