using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using WashedAshore.Lookout;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// Spec C3, I3 and I4 (tower-design-spec.md §5-§7, T-C3a/b, T-I3a, T-I4a/b; tower-qa-plan.md): the cab walk and
    /// eye clearances, the firefinder, and the hinged sashes.
    /// </summary>
    public class LookoutCabTests : LookoutFixture
    {
        const int RingPoints = 16;

        IEnumerator CloseHatchFromTheCab()
        {
            yield return WalkTo(ClearOfHatch, 10f);
            yield return LookAt(hatch.LeafCollider.bounds.center);
            yield return null;
            Assert.AreSame(hatch, interactor.Target, "aiming at the open leaf from the cab");
            yield return Tap(Key.E);
            yield return Until(() => hatch.State == HingeState.Closed, LookoutTargets.HatchMaxSeconds + 1f, "close the hatch");
        }

        /// <summary>
        /// Render meshes can't be asked for a distance, so each gets a temporary mesh collider on the Water layer,
        /// which the player and every gameplay cast skip. Returns the mask to query them with.
        /// </summary>
        int AddRenderMeshProbes(List<Collider> made)
        {
            foreach (var filter in tower.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.GetComponent<Renderer>() == null || filter.sharedMesh == null) continue;
                var go = new GameObject("RenderProbe_" + filter.name) { layer = WorldLayers.Water };
                go.transform.SetParent(filter.transform, false);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = filter.sharedMesh;
                made.Add(mc);
            }
            Physics.SyncTransforms();
            return 1 << WorldLayers.Water;
        }

        /// <summary>T-C3a, T-C3b: with the hatch closed, walk the ring both ways; at every point the eye clears every sill and stays eyeClearance from every surface.</summary>
        [UnityTest]
        public IEnumerator A_WalksTheCabBothWays_EyeClearOfEverySurface()
        {
            yield return ReachCab();
            yield return CloseHatchFromTheCab();
            var probes = new List<Collider>();
            int renderMask = AddRenderMeshProbes(probes);
            Assert.IsNotEmpty(probes, "no render meshes under the tower");

            float sill = float.MinValue;
            foreach (var sash in sashes) sill = Mathf.Max(sill, sash.GetComponentInChildren<Collider>().bounds.min.y);
            float minClear = float.MaxValue;
            var rows = new List<string>();
            for (int way = 0; way < 2; way++)
                for (int k = 0; k <= RingPoints; k++)
                {
                    float degrees = (way == 0 ? k : -k) * 360f / RingPoints + 90f;
                    Vector3 point = CabPoint(degrees);
                    float t0 = Time.time;
                    yield return WalkTo(point, 5f);
                    float off = Flat(player.transform.position - point).magnitude;
                    Assert.Less(off, capsule.radius, $"reach ring point {degrees:F0} deg (stopped {off:F2} m short after {Time.time - t0:F1} s)");
                    Assert.IsTrue(player.IsGrounded, $"grounded at ring point {degrees:F0}");

                    Vector3 eye = player.CameraPivot.position;
                    Assert.GreaterOrEqual(eye.y - sill, LookoutTargets.SillClearance, $"eye above the sill at ring point {degrees:F0} (N10)");
                    float clear = lookout.eyeClearance;
                    bool nearMesh = Physics.CheckSphere(eye, clear, renderMask, QueryTriggerInteraction.Ignore);
                    bool nearCollider = Physics.CheckSphere(eye, clear, WorldLayers.SightMask | (1 << WorldLayers.WorldBounds), QueryTriggerInteraction.Ignore);
                    if (nearMesh || nearCollider) minClear = 0f;
                    rows.Add($"{{\"deg\":{degrees:F1},\"nearMesh\":{(nearMesh ? "true" : "false")},\"nearCollider\":{(nearCollider ? "true" : "false")}}}");
                    Assert.IsFalse(nearMesh, $"a render mesh within {clear} m of the eye at ring point {degrees:F0} (R8)");
                    Assert.IsFalse(nearCollider, $"a collider within {clear} m of the eye at ring point {degrees:F0} (R8)");
                }
            foreach (var probe in probes) Object.Destroy(probe.gameObject);
            Evidence("c3-cab.json", "[" + string.Join(",", rows) + "]");
            AssertClean("cab walk");
        }

        /// <summary>
        /// tower-td-note §6 condition 18 (as amended): the open leaf is solid against the wall and blocks the aim ray like
        /// any solid, so with the hatch open the firefinder and every hinged sash must still be aimable from the ring.
        /// </summary>
        [UnityTest]
        public IEnumerator D_WithTheHatchOpen_FirefinderAndSashesCanStillBeAimedAt()
        {
            yield return ReachCab();
            Assert.AreEqual(HingeState.Open, hatch.State);
            yield return WalkTo(ClearOfHatch, 10f);
            yield return LookAt(firefinder.Alidade.position);
            yield return null;
            Assert.AreSame(firefinder, interactor.Target, $"firefinder hidden with the hatch open (prompt '{interactor.PromptLine}')");
            foreach (var sash in sashes)
            {
                var aim = sash.GetComponentInChildren<Collider>();
                Vector3 stand = CabCentre + Flat(aim.bounds.center - CabCentre).normalized * CabRing;
                // Skip a standing point that is in the open hatch itself; the sash is then aimed at from the clear spot.
                if (hatch.OpeningBounds.SqrDistance(stand) < capsule.radius * capsule.radius) stand = ClearOfHatch;
                yield return WalkTo(stand, 10f);
                yield return LookAt(aim.bounds.center);
                yield return null;
                Assert.AreSame(sash, interactor.Target, $"{sash.name} can't be aimed at with the hatch open (prompt '{interactor.PromptLine}')");
            }
            AssertClean("aiming with the hatch open");
        }

        /// <summary>T-I3a: enter, hold E to turn 90 degrees, read the pointer, tap E to leave; Esc also leaves; everything is put back.</summary>
        [UnityTest]
        public IEnumerator B_Firefinder_EnterTurnNinetyReadLeave()
        {
            yield return ReachCab();
            yield return CloseHatchFromTheCab();
            Vector3 pivotLocal = player.CameraPivot.localPosition;
            float fov = player.AuthoredFov;

            yield return LookAt(firefinder.Alidade.position);
            yield return null;
            Assert.AreSame(firefinder, interactor.Target, $"aiming at the firefinder (prompt '{interactor.PromptLine}')");
            Assert.AreEqual(LookoutTargets.PromptUseFirefinder, interactor.PromptLine, "spec §3.2");
            Assert.AreEqual(lookout.firefinderStartBearing, firefinder.BearingDegrees, LookoutTargets.BearingTolerance, "start bearing");

            watch.ExpectAuthoredCamera = false;
            Vector3 feet = player.transform.position;
            float entered = Time.time;
            yield return Tap(Key.E);
            Assert.IsTrue(firefinder.InUse, "in use after the press");
            Assert.AreEqual(LookoutTargets.PromptLeaveFirefinder, interactor.PromptLine, "the leave line at the start of a use (spec §3.2)");
            yield return Until(() => firefinder.CurrentPhase == Firefinder.Phase.InUse, LookoutTargets.FirefinderEnterSeconds, "reach the sight");
            Assert.AreEqual(lookout.firefinderFov, player.ViewCamera.fieldOfView, LookoutTargets.FovTolerance, "FOV at the sight");

            // Movement input moves nobody.
            Hold(Key.W);
            yield return Wait(1f);
            Let(Key.W);
            Assert.LessOrEqual(Vector3.Distance(feet, player.transform.position), LookoutTargets.FirefinderPlayerDrift, "the player stays put while using the firefinder");

            // Hold E: clockwise, after the tap window.
            float start = firefinder.BearingDegrees;
            Press(keyboard.eKey);
            yield return Until(() => Mathf.DeltaAngle(start, firefinder.BearingDegrees) >= LookoutTargets.FirefinderTurnDegrees - lookout.firefinderKeyFastDegPerSec * Time.deltaTime * 0.5f,
                LookoutTargets.FirefinderTurnDegrees / lookout.firefinderKeySlowDegPerSec, "turn 90 degrees with E held");
            Release(keyboard.eKey);
            yield return null;
            yield return null;
            Assert.IsTrue(firefinder.InUse, "releasing a hold does not leave");
            float turned = Mathf.DeltaAngle(start, firefinder.BearingDegrees);
            float pointer = LookoutRules.Bearing(firefinder.Alidade.forward);
            Debug.Log($"LookoutCabTests: firefinder {start:F1} -> {firefinder.BearingDegrees:F1} deg (turned {turned:F2}); pointer azimuth {pointer:F2}");
            Assert.AreEqual(LookoutTargets.FirefinderTurnDegrees, turned, lookout.firefinderKeyFastDegPerSec * Time.deltaTime + LookoutTargets.BearingTolerance, "turned 90 degrees clockwise");
            Assert.AreEqual(0f, Mathf.DeltaAngle(firefinder.BearingDegrees, pointer), LookoutTargets.BearingTolerance, "the pointer's world azimuth is the bearing");

            // Q turns back; the mouse turns too.
            float beforeQ = firefinder.BearingDegrees;
            Press(keyboard.qKey);
            yield return Wait(lookout.firefinderTapSeconds + lookout.firefinderKeySlowSeconds + 0.5f);
            Release(keyboard.qKey);
            yield return null;
            Assert.Less(Mathf.DeltaAngle(beforeQ, firefinder.BearingDegrees), 0f, "Q held turns counter-clockwise");
            float beforeMouse = firefinder.BearingDegrees;
            const float counts = 100f;
            Set(mouse.delta, new Vector2(counts, 0f), queueEventOnly: true);
            yield return null;
            yield return null;
            Assert.AreEqual(counts * lookout.firefinderMouseDegPerCount, Mathf.DeltaAngle(beforeMouse, firefinder.BearingDegrees), LookoutTargets.BearingTolerance, "mouse turns the bearing");

            // The leave line goes away after its time.
            yield return Until(() => Time.time - entered > lookout.firefinderPromptSeconds, lookout.firefinderPromptSeconds + 1f, "the leave line's time to pass");
            yield return null;
            Assert.IsNull(interactor.PromptLine, "no prompt after the first seconds of a use");

            // Tap E: leave. The player looks where the sight pointed, and the camera is put back exactly.
            float bearing = firefinder.BearingDegrees;
            yield return Tap(Key.E);
            yield return Until(() => !firefinder.InUse, lookout.firefinderBlendSeconds + 1f, "leave on an E tap");
            yield return null;
            watch.ExpectAuthoredCamera = true;
            Assert.AreEqual(fov, player.ViewCamera.fieldOfView, LookoutTargets.ViewRestoreTolerance, "FOV restored");
            Assert.AreEqual(0f, Vector3.Distance(pivotLocal, player.CameraPivot.localPosition), LookoutTargets.ViewRestoreTolerance, "camera pivot restored");
            Assert.AreEqual(0f, Mathf.DeltaAngle(bearing, player.Yaw), LookoutTargets.BearingTolerance, "the player faces the bearing after leaving");
            Assert.IsFalse(player.ControlSuspended, "controls returned");
            Assert.AreEqual(bearing, firefinder.BearingDegrees, 0f, "the alidade keeps its bearing between uses");

            // Esc also leaves.
            yield return LookAt(firefinder.Alidade.position);
            yield return null;
            watch.ExpectAuthoredCamera = false;
            yield return Tap(Key.E);
            yield return Until(() => firefinder.CurrentPhase == Firefinder.Phase.InUse, LookoutTargets.FirefinderEnterSeconds, "reach the sight again");
            yield return Tap(Key.Escape);
            yield return Until(() => !firefinder.InUse, lookout.firefinderBlendSeconds + 1f, "leave on Esc");
            yield return null;
            watch.ExpectAuthoredCamera = true;
            Assert.AreEqual(fov, player.ViewCamera.fieldOfView, LookoutTargets.ViewRestoreTolerance, "FOV restored after Esc");
            Hold(Key.W);
            yield return Wait(0.5f);
            Let(Key.W);
            Assert.Greater(Vector3.Distance(feet, player.transform.position), LookoutTargets.FirefinderPlayerDrift, "the player can walk again");
            AssertClean("firefinder");
            Evidence("i3-firefinder.json", $"{{\"start\":{start:F2},\"turned\":{turned:F3},\"pointerAzimuth\":{pointer:F3},\"fovInUse\":{lookout.firefinderFov},\"watch\":\"{watch.Summary()}\"}}");
        }

        static int MeshHash(Transform root)
        {
            unchecked
            {
                int hash = 17;
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    hash = hash * 31 + filter.name.GetHashCode();
                    hash = hash * 31 + filter.transform.localPosition.GetHashCode();
                    var mesh = filter.sharedMesh;
                    if (mesh == null) continue;
                    hash = hash * 31 + mesh.GetHashCode();
                    foreach (var v in mesh.vertices) hash = hash * 31 + v.GetHashCode();
                    var renderer = filter.GetComponent<Renderer>();
                    if (renderer != null) foreach (var m in renderer.sharedMaterials) hash = hash * 31 + (m != null ? m.GetHashCode() : 0);
                }
                return hash;
            }
        }

        /// <summary>T-I4a, T-I4b: each hinged sash opens outward to its angle and closes; its meshes are the same objects open and closed; fixed sashes give no prompt.</summary>
        [UnityTest]
        public IEnumerator C_Sashes_OpenOutwardAndClose_SamePanesBothWays()
        {
            yield return ReachCab();
            yield return CloseHatchFromTheCab();
            Assert.AreEqual(LookoutTargets.HingedSashes, sashes.Length, "hinged sashes");
            var rows = new List<string>();
            foreach (var sash in sashes)
            {
                var aim = sash.GetComponentInChildren<Collider>();
                Assert.IsNotNull(aim, $"{sash.name} has no aim collider");
                // Stand on the ring on the sash's side of the pedestal, so the firefinder isn't in the way.
                Vector3 toSash = Flat(aim.bounds.center - CabCentre).normalized;
                yield return WalkTo(CabCentre + toSash * CabRing, 10f);
                yield return LookAt(aim.bounds.center);
                yield return null;
                Assert.AreSame(sash, interactor.Target, $"aiming at {sash.name} (prompt '{interactor.PromptLine}')");
                Assert.AreEqual(LookoutTargets.PromptOpenWindow, interactor.PromptLine, "spec §3.2");

                int closedHash = MeshHash(sash.transform);
                float closedOut = Flat(aim.bounds.center - CabCentre).magnitude;
                var open = new Swing();
                yield return Tap(Key.E);
                yield return RecordSwing(() => sash.Angle, () => sash.State, open);
                Assert.AreEqual(HingeState.Open, sash.State, sash.name);
                Assert.AreEqual(lookout.sashOpenAngle, open.EndAngle, LookoutTargets.HingeAngleTolerance, $"{sash.name} open angle");
                Assert.That(open.Seconds, Is.InRange(LookoutTargets.SashMinSeconds - 2f * Time.deltaTime, LookoutTargets.SashMaxSeconds), $"{sash.name} open time");
                Assert.IsTrue(open.Monotonic, $"{sash.name} swing monotonic");
                Assert.Greater(Flat(aim.bounds.center - CabCentre).magnitude, closedOut, $"{sash.name} swings outward");
                Assert.AreEqual(closedHash, MeshHash(sash.transform), $"{sash.name}: meshes, materials and panes identical open and closed");
                yield return Wait(2f);
                Assert.AreEqual(HingeState.Open, sash.State, $"{sash.name} stays open (N13)");

                yield return LookAt(aim.bounds.center);
                yield return null;
                Assert.AreSame(sash, interactor.Target, $"aiming at the open {sash.name}");
                Assert.AreEqual(LookoutTargets.PromptCloseWindow, interactor.PromptLine, "spec §3.2");
                var close = new Swing();
                yield return Tap(Key.E);
                yield return RecordSwing(() => sash.Angle, () => sash.State, close);
                Assert.AreEqual(HingeState.Closed, sash.State, sash.name);
                Assert.AreEqual(0f, close.EndAngle, LookoutTargets.HingeAngleTolerance, $"{sash.name} closed angle");
                Assert.AreEqual(closedHash, MeshHash(sash.transform), $"{sash.name}: identical after closing");
                rows.Add($"{{\"sash\":\"{sash.name}\",\"openSeconds\":{open.Seconds:F3},\"closeSeconds\":{close.Seconds:F3},\"openAngle\":{open.EndAngle:F2},\"meshHash\":{closedHash}}}");
            }

            // Fixed sashes: nothing to aim at.
            foreach (var t in tower.GetComponentsInChildren<Transform>())
            {
                if (!t.name.Contains("Sash") || !t.name.EndsWith("_Fixed") || t.GetComponent<Renderer>() == null) continue;
                Vector3 centre = t.GetComponent<Renderer>().bounds.center;
                yield return WalkTo(CabCentre + Flat(centre - CabCentre).normalized * CabRing, 10f);
                yield return LookAt(centre);
                yield return null;
                Assert.IsNull(interactor.PromptLine, $"fixed sash {t.name} shows no prompt");
            }
            Evidence("i4-sashes.json", "[" + string.Join(",", rows) + "]");
            AssertClean("sashes");
        }
    }
}
