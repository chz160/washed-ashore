using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Spec C1 (tower-design-spec.md §9 T-C1a to T-C1f; tower-qa-plan.md C1): a scripted climb from the base to the
    /// cab floor and back down, through the Input System only. Targets come from LookoutTargets and the tuning assets.
    /// </summary>
    [Ignore("Retired by name (#40, td R4): climbs the ClimbArea lanes, a guided path the walkable-tower restart (#7) removed; the tower walk is TowerWalkTests.")]
    public class LookoutClimbTests : LookoutFixture
    {
        /// <summary>The controller values that must never change, and the radius that may only be the area's inside it.</summary>
        struct Authored
        {
            public float radius, height, slopeLimit, stepOffset, skinWidth;
            public Vector3 center;

            public static Authored Of(CharacterController c) => new Authored
            {
                radius = c.radius, height = c.height, slopeLimit = c.slopeLimit, stepOffset = c.stepOffset, skinWidth = c.skinWidth, center = c.center,
            };

            public void AssertSameExceptRadius(CharacterController c, string when)
            {
                Assert.AreEqual(height, c.height, 0f, $"height {when}");
                Assert.AreEqual(slopeLimit, c.slopeLimit, 0f, $"slopeLimit {when}");
                Assert.AreEqual(stepOffset, c.stepOffset, 0f, $"stepOffset {when}");
                Assert.AreEqual(skinWidth, c.skinWidth, 0f, $"skinWidth {when}");
                Assert.AreEqual(center, c.center, $"center {when}");
            }
        }

        IEnumerator ToFirstFoot()
        {
            yield return StartAtBase();
            yield return WalkTo(area.Lane(0).Point(-ClimbRules.FootRun(area.Lane(0), climb) * 0.5f, 0f), 15f);
        }

        /// <summary>T-C1a, T-C1b, T-C1d, T-C1f: up with W and mouse turns only, then all the way down; controller values checked on the way.</summary>
        [UnityTest]
        public IEnumerator A_ClimbsToTheCabAndBackDown_WithWOnly()
        {
            yield return LoadTower();
            var authored = Authored.Of(capsule);
            yield return ToFirstFoot();
            Assert.AreEqual(authored.radius, capsule.radius, 0f, "authored radius on the ground before the first tread");

            var up = new ClimbLog();
            yield return ClimbUp(up);
            float formula = ClimbRules.ClimbSeconds(up.LaneLength, up.LandingCrossing + up.LevelLength, climb);
            Debug.Log($"LookoutClimbTests: climb {up.Seconds:F1} s (lanes {up.LaneSeconds:F1} incl. hatch {up.HatchSeconds:F1}, landings {up.LandingSeconds:F1}); " +
                      $"formula {formula:F1} s from {up.LaneLength:F2} m of flight, {up.LevelLength:F2} m of level lane and {up.LandingCrossing:F2} m of landing; turns [{string.Join(", ", up.Turns.ConvertAll(t => t.ToString("F0")))}]");
            Assert.AreEqual(area.LaneCount, up.LanesPassed, "every flight passed");
            Assert.IsFalse(player.OnLane, "handed over to the cab floor");
            Assert.IsTrue(player.IsGrounded, "standing on the cab floor");
            Assert.That(up.Seconds, Is.InRange(LookoutTargets.ClimbMinSeconds, LookoutTargets.ClimbMaxSeconds), "base to cab, seconds (C1)");
            Assert.AreEqual(formula, up.Seconds, formula * LookoutTargets.ClimbFormulaTolerance, "climb time against the §2.4 formula with built lengths");
            Assert.AreEqual(FlightCount, up.FlightsPassed, "every flight's group passed");
            Assert.AreEqual(FlightCount - 1, up.Turns.Count, "one landing crossing per landing");
            foreach (float turn in up.Turns) Assert.GreaterOrEqual(turn, LookoutTargets.LandingTurnMinDegrees, "a real turn on each landing (N9)");

            // T-C1f inside the area: the small radius, everything else as authored.
            Assert.AreEqual(climb.towerCapsuleRadius, capsule.radius, 1e-6f, "the area's capsule radius in the cab");
            authored.AssertSameExceptRadius(capsule, "in the cab");
            AssertClean("climb");
            Assert.AreEqual(LookoutTargets.JoinsFromAboveAllowed, player.LaneCatches, "joins from above on a normal climb (a catch at the cab hand-over would mean the hand-over is wrong)");

            var down = new ClimbLog();
            yield return ClimbDown(down);
            watch.ExpectOnTower = false;
            Debug.Log($"LookoutClimbTests: descent {down.Seconds:F1} s over {down.LaneLength:F2} m of lane");
            Assert.AreEqual(area.LaneCount, down.LanesPassed, "every flight passed going down");
            Assert.IsNull(player.ClimbArea, "off the first tread: the area's rules are over the same frame");

            // T-C1b: authored radius within RadiusRestoreSeconds of being RadiusRestoreClear clear of the first foot.
            Vector3 clear = area.Lane(0).Point(-LookoutTargets.RadiusRestoreClear - climb.laneRunOutLength, 0f);
            yield return WalkTo(clear, 10f);
            yield return Wait(LookoutTargets.RadiusRestoreSeconds);
            Assert.AreEqual(authored.radius, capsule.radius, 0f, "authored radius back after leaving the first foot");
            authored.AssertSameExceptRadius(capsule, "after leaving");
            AssertClean("descent");
            Evidence("c1-climb.json", $"{{\"climbSeconds\":{up.Seconds:F2},\"formulaSeconds\":{formula:F2},\"laneLength\":{up.LaneLength:F3},\"landingCrossing\":{up.LandingCrossing:F3}," +
                                      $"\"hatchSeconds\":{up.HatchSeconds:F2},\"descentSeconds\":{down.Seconds:F2},\"turns\":[{string.Join(",", up.Turns.ConvertAll(t => t.ToString("F1")))}]," +
                                      $"\"deltaTime\":{Time.captureDeltaTime:F5},\"watch\":\"{watch.Summary()}\"}}");
        }

        /// <summary>T-C1c (R3, R4): on every flight, stop and hold with no drift, turn round, walk back down, turn and carry on.</summary>
        [UnityTest]
        public IEnumerator C_OnEveryFlight_StopsWithoutDrift_AndTurnsBack()
        {
            yield return LoadTower();
            yield return ToFirstFoot();
            var drifts = new List<float>();
            var log = new ClimbLog();
            log.MidLane = lane => HoldTurnBackAndResume(lane, drifts);
            yield return ClimbUp(log);
            Assert.AreEqual(FlightCount, drifts.Count, "held on every flight");
            Debug.Log($"LookoutClimbTests: drift per flight over {LookoutTargets.FlightHoldSeconds} s: [{string.Join(", ", drifts.ConvertAll(d => d.ToString("F4")))}]");
            foreach (float drift in drifts) Assert.LessOrEqual(drift, LookoutTargets.FlightHoldDrift, "drift while released on a flight (R3)");
            AssertClean("stop and turn back");
        }

        IEnumerator HoldTurnBackAndResume(int index, List<float> drifts)
        {
            StairLane lane = area.Lane(index);
            Let(Key.W);
            // Let the speed run out, then hold.
            yield return Until(() => Mathf.Approximately(player.LaneSpeed, 0f), 3f, "stop on the flight");
            Vector3 held = player.transform.position;
            yield return Wait(LookoutTargets.FlightHoldSeconds);
            Assert.IsTrue(player.OnLane, $"still on lane {index + 1} after holding");
            drifts.Add(Vector3.Distance(held, player.transform.position));

            float before = player.LaneDistance;
            yield return Face(-lane.AxisHorizontal);
            Hold(Key.W);
            yield return Wait(2f);
            Let(Key.W);
            Assert.IsTrue(player.OnLane && player.LaneIndex == index, $"left lane {index + 1} while turning back");
            Assert.Less(player.LaneDistance, before, $"W after turning round walks down lane {index + 1} (R4)");

            // S while facing down the flight backs up it.
            float low = player.LaneDistance;
            Hold(Key.S);
            yield return Wait(1f);
            Let(Key.S);
            Assert.Greater(player.LaneDistance, low, $"S while facing down backs up lane {index + 1}");

            yield return Face(lane.AxisHorizontal);
            Hold(Key.W);
        }

        /// <summary>
        /// T-C1i: on the level run at the top of every flight, and on the cab's exit lane itself, the player can stop and
        /// stay (5 s, no drift, still on the lane), then leave both ways: back by S, then on by W. At a junction where
        /// the heading changes, W on the old heading leaves them standing with no rotation made by the game, and turning
        /// into the next lane's cone with W held is enough to carry on.
        /// </summary>
        [UnityTest]
        public IEnumerator I_StopsOnEveryLevelRun_AndStandsAtAJunctionUntilTurned()
        {
            yield return LoadTower();
            yield return ToFirstFoot();
            int runs = 0, junctions = 0, levelLanes = 0;
            var log = new ClimbLog();
            log.HeadRun = lane => StopOnRun(lane, () => runs++);
            log.Junction = lane => StandAtJunction(lane, () => junctions++);
            log.LevelLane = lane => StopOnLevelLane(lane, () => levelLanes++);
            yield return ClimbUp(log);
            int freeHeads = 0, linkedHeads = 0, linkedLevel = 0;
            for (int i = 0; i < area.LaneCount; i++)
            {
                if (HeadLinked(i)) linkedHeads++; else if (ClimbRules.HeadRun(area.Lane(i), climb) > 0f) freeHeads++;
                if (FootLinked(i) && ClimbRules.IsLevel(area.Lane(i))) linkedLevel++;
            }
            Assert.AreEqual(freeHeads, runs, "stopped on the level run at every free head that has one");
            Assert.AreEqual(log.Stops, junctions, "stood at every junction that stopped the player");
            Assert.LessOrEqual(junctions, linkedHeads, "no more stops than junctions");
            Assert.AreEqual(linkedLevel, levelLanes, "stopped on every level lane entered through a junction (the cab's exit lane)");
            AssertClean("level runs and junctions");
        }

        IEnumerator StopOnRun(int index, System.Action done)
        {
            StairLane lane = area.Lane(index);
            Let(Key.W);
            yield return Until(() => Mathf.Approximately(player.LaneSpeed, 0f), 3f, "stop on the level run");
            Assert.IsTrue(player.OnLane && player.LaneIndex == index, $"stopping on lane {index + 1}'s level run hands the player over; it must hold them on the lane");
            Vector3 held = player.transform.position;
            yield return Wait(LookoutTargets.RunStopSeconds);
            Assert.IsTrue(player.OnLane && player.LaneIndex == index, $"still on lane {index + 1}'s level run after {LookoutTargets.RunStopSeconds} s");
            Assert.LessOrEqual(Vector3.Distance(held, player.transform.position), LookoutTargets.FlightHoldDrift, $"drift while stopped on lane {index + 1}'s level run");
            // Both ways from every run: S backs down onto the flight; then W carries on up and out.
            float before = player.LaneDistance;
            Hold(Key.S);
            yield return Until(() => player.LaneDistance < lane.Length, 5f, $"back down onto flight {index + 1} with S");
            Let(Key.S);
            Assert.Less(player.LaneDistance, before, "S on the level run walks back toward the flight");
            Assert.IsTrue(player.OnLane && player.LaneIndex == index, $"S on lane {index + 1}'s run left the lane");
            done();
            Hold(Key.W);
        }

        /// <summary>On the exit lane itself: stop 5 s, back toward the junction by S, then out by W in the lane's cone.</summary>
        IEnumerator StopOnLevelLane(int index, System.Action done)
        {
            Let(Key.W);
            yield return Until(() => Mathf.Approximately(player.LaneSpeed, 0f), 3f, "stop on the level lane");
            Assert.IsTrue(player.OnLane && player.LaneIndex == index, $"stopping on level lane {index + 1} left the lane");
            Vector3 held = player.transform.position;
            yield return Wait(LookoutTargets.RunStopSeconds);
            Assert.IsTrue(player.OnLane && player.LaneIndex == index, $"still on level lane {index + 1} after {LookoutTargets.RunStopSeconds} s");
            Assert.LessOrEqual(Vector3.Distance(held, player.transform.position), LookoutTargets.FlightHoldDrift, $"drift while stopped on level lane {index + 1}");
            float before = player.LaneDistance;
            Hold(Key.S);
            yield return Wait(0.5f);
            Let(Key.S);
            // On a lane only centimetres long, S may carry the player back through the junction onto the lane before it.
            Assert.IsTrue(player.OnLane, $"S on level lane {index + 1} left the lanes");
            Assert.IsTrue(player.LaneIndex != index || player.LaneDistance < before, "S on the level lane walks back toward the junction");
            done();
            Hold(Key.W);
        }

        IEnumerator StandAtJunction(int index, System.Action done)
        {
            // W on the old heading: the player stands. Nothing turns them.
            Vector3 stood = player.transform.position;
            float yaw = player.Yaw, pitch = player.Pitch;
            Hold(Key.W);
            yield return Wait(LookoutTargets.JunctionHoldSeconds);
            Assert.IsTrue(AtJunction(index, true), $"W on the old heading at the head of lane {index + 1} did not leave the player standing at the junction");
            Assert.LessOrEqual(Vector3.Distance(stood, player.transform.position), LookoutTargets.JunctionHoldDrift, "moved at the junction with W on the old heading");
            Assert.AreEqual(yaw, player.Yaw, 0f, "the game turned the player at the junction");
            Assert.AreEqual(pitch, player.Pitch, 0f, "the game pitched the view at the junction");
            done();
            // ClimbUp now turns into the next lane's cone with W still held, which must be enough on its own.
        }

        /// <summary>T-C1e (R10; tower-td-note §6 condition 9): the same climb at 1/30, 1/120 and 1/240 s frames; the climb times agree within 2%.</summary>
        [UnityTest]
        public IEnumerator E_ClimbTime_IsTheSameAtEveryFrameRate()
        {
            var totals = new List<float>();
            var rows = new List<string>();
            foreach (float dt in LookoutTargets.ProofDeltaTimes)
            {
                Time.captureDeltaTime = dt;
                yield return LoadTower();
                yield return ToFirstFoot();
                var log = new ClimbLog();
                yield return ClimbUp(log);
                Assert.That(log.Seconds, Is.InRange(LookoutTargets.ClimbMinSeconds, LookoutTargets.ClimbMaxSeconds), $"base to cab at dt {dt:F5}");
                AssertClean($"climb at dt {dt:F5}");
                // Pre-registered with t-qa: each condition wait costs half a frame on average; the 2% is judged on the corrected total.
                float corrected = log.Seconds - log.Waits * dt * 0.5f;
                totals.Add(corrected);
                rows.Add($"{{\"deltaTime\":{dt:F5},\"climbSeconds\":{log.Seconds:F3},\"conditionWaits\":{log.Waits},\"correctedSeconds\":{corrected:F3},\"laneSeconds\":{log.LaneSeconds:F3},\"landingSeconds\":{log.LandingSeconds:F3},\"hatchSeconds\":{log.HatchSeconds:F3}," +
                         $"\"freeLaneSeconds\":{log.FreeLaneSeconds:F3},\"freeLaneLength\":{log.FreeLaneLength:F3},\"watch\":\"{watch.Summary()}\"}}");
            }
            Evidence("c1-framerate.json", "[" + string.Join(",", rows) + "]");
            Debug.Log($"LookoutClimbTests: corrected climb seconds per frame rate: [{string.Join(", ", totals.ConvertAll(s => s.ToString("F2")))}]");
            for (int i = 1; i < totals.Count; i++)
                Assert.AreEqual(totals[0], totals[i], totals[0] * LookoutTargets.FrameRateTolerance, $"climb time at dt {LookoutTargets.ProofDeltaTimes[i]:F5} vs {LookoutTargets.ProofDeltaTimes[0]:F5}");
        }

        /// <summary>T-C1f (tower-td-note §6 conditions 2-3): a Respawn from the cab clears lane and area state that frame and every authored value returns.</summary>
        [UnityTest]
        public IEnumerator F_RespawnFromTheCab_ClearsClimbState_AndRestoresTheController()
        {
            yield return LoadTower();
            var authored = Authored.Of(capsule);
            yield return ToFirstFoot();
            yield return ClimbUp(new ClimbLog());
            Assert.IsNotNull(player.ClimbArea, "in the climb area in the cab");

            watch.ExpectOnTower = false;
            player.Respawn();
            watch.Forget();
            Assert.IsNull(player.ClimbArea, "Respawn clears the area state the same frame");
            Assert.IsFalse(player.OnLane, "Respawn clears the lane state the same frame");
            Assert.IsTrue(capsule.enabled, "controller enabled after Respawn");
            yield return Wait(LookoutTargets.RadiusRestoreSeconds);
            Assert.AreEqual(authored.radius, capsule.radius, 0f, "authored radius after a Respawn from the cab");
            authored.AssertSameExceptRadius(capsule, "after Respawn");

            // Jump works again at once off the tower (the director's jump condition).
            yield return WaitGrounded(5f);
            float y = player.transform.position.y;
            Press(keyboard.spaceKey);
            yield return Wait(0.2f);
            Release(keyboard.spaceKey);
            Assert.Greater(player.transform.position.y, y + capsule.skinWidth, "jump works off the tower");
        }

        /// <summary>
        /// tower-td-note §6 ruling 1: off the tower the radius is the authored one on every frame of a walk and a swim
        /// entry. (The controller also logs an error if it ever stays small off the tower, which fails any suite.)
        /// </summary>
        [UnityTest]
        public IEnumerator G_OffTheTower_TheRadiusIsAlwaysTheAuthoredOne()
        {
            yield return LoadTower();
            yield return WaitGrounded(5f);
            float authored = player.AuthoredRadius;
            Hold(Key.LeftShift);
            Hold(Key.W);
            for (float t = 0f; t < 10f; t += Time.deltaTime)
            {
                Assert.AreEqual(authored, capsule.radius, 0f, $"radius at {player.transform.position:F1} ({player.WaterMode})");
                Assert.IsNull(player.ClimbArea, "no climb area at the spawn");
                yield return null;
            }
            LetAll();
        }
    }
}
