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
    /// Spec I2 (tower-design-spec.md §4, T-I2a to T-I2e; tower-qa-plan.md I2): the trap door's sequence, its
    /// animation, the refused presses, that it never moves by itself, and the eye duck under the floor.
    /// </summary>
    public class LookoutHatchTests : LookoutFixture
    {
        IEnumerator AimAtHatch()
        {
            yield return LookAt(hatch.LeafCollider.bounds.center);
            yield return null;
        }

        void AssertSwing(Swing swing, float from, float to, string what)
        {
            Debug.Log($"LookoutHatchTests: {what}: {swing.Seconds:F2} s, {swing.StartAngle:F1} to {swing.EndAngle:F1} deg, peak {swing.PeakDegPerSec:F0} deg/s, monotonic={swing.Monotonic}");
            Assert.That(swing.Seconds, Is.InRange(LookoutTargets.HatchMinSeconds - 2f * Time.deltaTime, LookoutTargets.HatchMaxSeconds), $"{what}: duration (the press is two frames before the first sample)");
            Assert.IsTrue(swing.Monotonic, $"{what}: angle monotonic");
            Assert.LessOrEqual(swing.PeakDegPerSec, LookoutTargets.HatchPeakDegPerSec, $"{what}: peak angular speed");
            Assert.AreEqual(to, swing.EndAngle, LookoutTargets.HingeAngleTolerance, $"{what}: end angle");
            Assert.AreEqual(from, swing.StartAngle, lookout.hatchOpenAngle * 0.05f, $"{what}: start angle");
        }

        IEnumerator AssertIdle(float seconds, string what)
        {
            var state = hatch.State;
            float angle = hatch.Angle;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Assert.AreEqual(state, hatch.State, $"{what}: state changed by itself after {t:F1} s (N13)");
                Assert.AreEqual(angle, hatch.Angle, 0f, $"{what}: angle changed by itself after {t:F1} s (N13)");
                yield return null;
            }
        }

        /// <summary>T-I2a to T-I2d: open from below, climb in, close, stand on it, try to open it from on top, step off, open, climb down, close from below.</summary>
        [UnityTest]
        public IEnumerator A_OpenClimbInCloseStandOpenClimbDown()
        {
            yield return ReachCab();                         // opened from below with E, climbed in
            Assert.AreEqual(HingeState.Open, hatch.State);
            Assert.AreEqual(lookout.hatchOpenAngle, hatch.Angle, LookoutTargets.HingeAngleTolerance);

            // Close it from the cab floor, standing clear.
            yield return WalkTo(ClearOfHatch, 10f);
            yield return AimAtHatch();
            Assert.AreSame(hatch, interactor.Target, $"aiming at the open leaf from the cab (prompt '{interactor.PromptLine}')");
            Assert.AreEqual(LookoutTargets.PromptCloseHatch, interactor.PromptLine, "spec §3.2");
            var closing = new Swing();
            yield return Tap(Key.E);
            Assert.IsNull(interactor.PromptLine, "no prompt while the leaf moves");
            yield return Tap(Key.E);                         // a press during the swing does nothing
            yield return RecordSwing(() => hatch.Angle, () => hatch.State, closing);
            Assert.AreEqual(HingeState.Closed, hatch.State);
            AssertSwing(closing, lookout.hatchOpenAngle, 0f, "close");
            yield return AssertIdle(LookoutTargets.HatchIdleSeconds, "closed");

            // Stand on it: solid, no drift, grounded every frame.
            Vector3 onLeaf = hatch.OpeningBounds.center;
            yield return WalkTo(onLeaf, 10f);
            Assert.Less(Flat(player.transform.position - onLeaf).magnitude, capsule.radius, "standing on the closed leaf");
            Vector3 stood = player.transform.position;
            for (float t = 0f; t < LookoutTargets.HatchStandSeconds; t += Time.deltaTime)
            {
                Assert.IsTrue(player.IsGrounded, $"grounded on the closed leaf at {t:F1} s");
                yield return null;
            }
            Assert.LessOrEqual(Vector3.Distance(stood, player.transform.position), LookoutTargets.HatchStandDrift, "drift standing on the leaf");
            Assert.AreEqual(hatch.OpeningBounds.center.y, player.transform.position.y, LookoutTargets.HatchStandDrift + capsule.skinWidth, "standing at floor height");

            // On it: no prompt, E refused, jump ignored.
            yield return LookAt(onLeaf);
            Assert.IsNull(interactor.PromptLine, "no prompt while standing on the leaf");
            int refused = interactor.RefusedPresses;
            yield return Tap(Key.E);
            Assert.AreEqual(HingeState.Closed, hatch.State, "open refused while standing on the leaf");
            Assert.AreEqual(refused + 1, interactor.RefusedPresses, "the refused press is counted");
            float y = player.transform.position.y;
            yield return Tap(Key.Space);
            yield return Wait(0.3f);
            Assert.AreEqual(y, player.transform.position.y, LookoutTargets.HatchStandDrift, "jump is ignored in the cab");

            // Step off and it opens: always a way through.
            yield return WalkTo(ClearOfHatch, 10f);
            yield return AimAtHatch();
            Assert.AreEqual(LookoutTargets.PromptOpenHatch, interactor.PromptLine, "spec §3.2");
            var opening = new Swing();
            yield return Tap(Key.E);
            yield return RecordSwing(() => hatch.Angle, () => hatch.State, opening);
            Assert.AreEqual(HingeState.Open, hatch.State);
            AssertSwing(opening, 0f, lookout.hatchOpenAngle, "open");
            yield return AssertIdle(LookoutTargets.HatchIdleSeconds, "open");

            // In the opening on the top flight: close refused.
            int top = hatch.LaneIndex;
            StairLane lane = area.Lane(top);
            yield return EnterFromAbove(top);
            yield return Until(() => player.LaneDistance < lane.Length - capsule.radius, 10f, "walk down into the opening");
            Let(Key.W);
            yield return AimAtHatch();
            Assert.IsNull(interactor.PromptLine, "no prompt while in the opening");
            yield return Tap(Key.E);
            Assert.AreEqual(HingeState.Open, hatch.State, "close refused while in the opening");

            // Down to the landing, then close it from below.
            yield return Face(-lane.AxisHorizontal);
            Hold(Key.W);
            yield return Until(() => !player.OnLane, lane.Length / climb.stairDescendSpeed * 2f + 10f, "reach the top landing");
            Let(Key.W);
            yield return AimAtHatch();
            if (interactor.Target == hatch)
            {
                yield return Tap(Key.E);
                yield return Until(() => hatch.State == HingeState.Closed, LookoutTargets.HatchMaxSeconds + 1f, "hatch to close from below");
            }
            else Debug.Log($"LookoutHatchTests: leaf not in aim range from the top landing ({Vector3.Distance(player.CameraPivot.position, hatch.LeafCollider.bounds.center):F2} m); closing from below is covered from the lane in B.");

            Assert.LessOrEqual(watch.MaxUpwardSpeed, LookoutTargets.HatchMaxUpwardSpeed, "no upward speed above the hatch bound at any frame");
            AssertClean("hatch sequence");
            Evidence("i2-hatch.json", $"{{\"closeSeconds\":{closing.Seconds:F3},\"openSeconds\":{opening.Seconds:F3},\"closePeak\":{closing.PeakDegPerSec:F1},\"openPeak\":{opening.PeakDegPerSec:F1}," +
                                      $"\"refusedPresses\":{interactor.RefusedPresses},\"watch\":\"{watch.Summary()}\"}}");
        }

        /// <summary>T-I2c from below: on the top flight under the shut hatch the player stops at the gate, can open it, and can close it again from lower down.</summary>
        [UnityTest]
        public IEnumerator B_FromBelow_StopsAtTheGate_OpensAndCloses()
        {
            yield return LoadTower();
            yield return StartAtBase();
            yield return WalkTo(area.Lane(0).Point(-ClimbRules.FootRun(area.Lane(0), climb) * 0.5f, 0f), 15f);
            int top = hatch.LaneIndex;
            // Climb to the top landing, then join the top lane by hand.
            var log = new ClimbLog();
            log.MidLane = lane => lane == top ? StopBelowTheGate() : null;
            yield return ClimbUp(log);
            Assert.IsNotNull(player.ClimbArea);
        }

        IEnumerator StopBelowTheGate()
        {
            // ClimbUp has already opened the hatch at the gate. Close it again from here (below the gate), then reopen.
            Let(Key.W);
            yield return Until(() => Mathf.Approximately(player.LaneSpeed, 0f), 3f, "stop on the top flight");
            yield return AimAtHatch();
            if (interactor.Target == hatch)
            {
                yield return Tap(Key.E);
                yield return Until(() => hatch.State == HingeState.Closed, LookoutTargets.HatchMaxSeconds + 1f, "close from the flight");
                // Shut: W up the flight stops at the gate, head hatchStopHeadroom under the floor.
                yield return Face(area.Lane(hatch.LaneIndex).AxisHorizontal);
                Hold(Key.W);
                yield return Wait(area.Lane(hatch.LaneIndex).Length / climb.stairAscendSpeed);
                Let(Key.W);
                Assert.IsTrue(player.OnLane, "still on the top flight under the shut hatch");
                float head = player.transform.position.y + capsule.height;
                float underside = hatch.OpeningBounds.center.y - hatch.FloorThickness;
                Assert.AreEqual(lookout.hatchStopHeadroom, underside - head, capsule.skinWidth + 0.01f, "head stops hatchStopHeadroom below the shut hatch");
                yield return AimAtHatch();
                Assert.AreSame(hatch, interactor.Target, "the shut hatch can be opened from the gate");
                yield return Tap(Key.E);
                yield return Until(() => hatch.State == HingeState.Open, LookoutTargets.HatchMaxSeconds + 1f, "reopen from the gate");
            }
            else Assert.Fail($"The open leaf is not in aim range from the middle of the top flight (prompt '{interactor.PromptLine}')");
            yield return Face(area.Lane(hatch.LaneIndex).AxisHorizontal);
            Hold(Key.W);
        }

        /// <summary>T-I2e: the eye height on the top flight depends on position only: the same up, down and stopped, at 1/30 and 1/240 s frames.</summary>
        [UnityTest]
        public IEnumerator E_TheDuck_IsAFunctionOfPositionOnly()
        {
            var rows = new List<string>();
            foreach (float dt in new[] { LookoutTargets.ProofDeltaTimes[0], LookoutTargets.ProofDeltaTimes[LookoutTargets.ProofDeltaTimes.Length - 1] })
            {
                Time.captureDeltaTime = dt;
                yield return ReachCab();
                int top = hatch.LaneIndex;
                StairLane lane = area.Lane(top);
                float standing = player.AuthoredEyeHeight, lowest = float.MaxValue, worst = 0f;
                Assert.AreEqual(standing, EyeHeight(), LookoutTargets.ViewRestoreTolerance, "standing eye height on the cab floor");

                // Down the top flight, stopping once in the duck, then back up.
                yield return EnterFromAbove(top);
                bool stopped = false;
                for (int pass = 0; pass < 2; pass++)
                {
                    while (player.OnLane && player.LaneIndex == top && (pass == 0 ? player.LaneDistance > lane.Length * 0.25f : player.LaneDistance < lane.Length))
                    {
                        float eye = EyeHeight(), expected = hatch.EyeHeightAt(player.LaneDistance);
                        worst = Mathf.Max(worst, Mathf.Abs(eye - expected));
                        lowest = Mathf.Min(lowest, eye);
                        Assert.AreEqual(player.AuthoredFov, player.ViewCamera.fieldOfView, 0f, "FOV during the duck");
                        if (!stopped && eye < standing - LookoutTargets.DuckRepeatTolerance)
                        {
                            stopped = true;
                            Let(Key.W);
                            yield return Wait(LookoutTargets.DuckStopSeconds);
                            Assert.AreEqual(hatch.EyeHeightAt(player.LaneDistance), EyeHeight(), LookoutTargets.DuckRepeatTolerance, "eye height while stopped mid-duck");
                            float yaw = player.Yaw;
                            Set(mouse.delta, new Vector2(50f, 0f), queueEventOnly: true);
                            yield return null;
                            yield return null;
                            Assert.AreNotEqual(yaw, player.Yaw, "look input still turns the camera mid-duck");
                            yield return Face(-lane.AxisHorizontal);
                            Hold(Key.W);
                        }
                        yield return null;
                    }
                    Let(Key.W);
                    yield return Face(lane.AxisHorizontal);
                    Hold(Key.W);
                }
                LetAll();
                yield return LeaveAbove(top);
                yield return null;
                Assert.AreEqual(standing, EyeHeight(), LookoutTargets.ViewRestoreTolerance, "standing eye height back on the cab floor");
                Assert.AreEqual(0f, worst, LookoutTargets.DuckRepeatTolerance, $"eye height vs the position rule at dt {dt:F5}");
                Assert.GreaterOrEqual(lowest, lookout.hatchDuckMinEyeHeight - LookoutTargets.DuckRepeatTolerance, "never below the duck floor");
                Debug.Log($"LookoutHatchTests: duck at dt {dt:F5}: lowest eye {lowest:F3} m (pass height {hatch.PassEyeHeight:F3}), worst deviation {worst:F4} m, stopped={stopped}");
                rows.Add($"{{\"deltaTime\":{dt:F5},\"lowestEye\":{lowest:F4},\"passEye\":{hatch.PassEyeHeight:F4},\"worstDeviation\":{worst:F5},\"duckSeen\":{(stopped ? "true" : "false")}}}");
                AssertClean($"duck at dt {dt:F5}");
            }
            Evidence("i2-duck.json", "[" + string.Join(",", rows) + "]");
        }

        float EyeHeight() => player.CameraPivot.position.y - player.transform.position.y;
    }
}
