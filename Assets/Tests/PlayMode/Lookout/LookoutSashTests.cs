using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using WashedAshore.Lookout;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// #40 L7, spec T4.4, td part 5 D: an open sash is no new way out of the cab. A capsule sweep (the tower capsule) outward through each open sash's
    /// window opening is blocked by a CLIP_BOX_CabBand wall on WorldBounds, and walking at each open sash never takes the player off the cab floor.
    /// </summary>
    public class LookoutSashTests : LookoutFixture
    {
        Vector3 Outward(WindowSash s)
        {
            var b = s.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            return Flat(b.center - CabCentre).normalized;
        }

        Vector3 WindowCentre(WindowSash s)
        {
            var b = s.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
            return b.center;
        }

        /// <summary>
        /// L7, td part 41 (2): two sweeps through each open sash's window, outward from inside the cab. STANDING (capsule on the cab floor): blocked by any
        /// collider in PlayerCollisionMask (the wall box below the sill, COL_BOX_CabWall_0N on Default, is hit first; its name and layer are logged).
        /// RAISED (capsule bottom 0.05 m above the sill): must be stopped by a CLIP_BOX_CabBand_0N box on WorldBounds, which is what closes the window opening itself.
        /// </summary>
        [UnityTest]
        public IEnumerator A_ACapsuleSweepOutThroughEachOpenSash_IsBlocked_StandingByAnything_AndRaisedByTheCabBandWall()
        {
            yield return LoadTower();
            Assert.IsNotEmpty(sashes, "the tower has no sashes");
            var walk = tower.GetComponent<TowerWalkZones>();
            Assert.IsNotNull(walk, "the tower has no TowerWalkZones");
            float floorTop = hatch.OpeningBounds.center.y;
            float span = walk.towerHeight - 2f * walk.towerRadius;
            foreach (var sash in sashes)
            {
                // The window's sill is the bottom of the CLOSED sash's bounds.
                float sill = sash.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds.min.y).Min();
                sash.SetState(HingeState.Open, 0f);
                yield return null;
                Physics.SyncTransforms();
                var b = sash.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });
                Vector3 outward = Flat(b.center - CabCentre).normalized;
                Vector3 inside = b.center - outward * 0.8f;
                Vector3 s1 = inside; s1.y = floorTop + 0.02f + walk.towerRadius;
                bool standing = Physics.CapsuleCast(s1, s1 + Vector3.up * span, walk.towerRadius, outward, out var hs, 3f, WorldLayers.PlayerCollisionMask, QueryTriggerInteraction.Ignore);
                Vector3 s2 = inside; s2.y = sill + 0.05f + walk.towerRadius;
                bool raised = Physics.CapsuleCast(s2, s2 + Vector3.up * span, walk.towerRadius, outward, out var hr, 3f, WorldLayers.PlayerCollisionMask, QueryTriggerInteraction.Ignore);
                Debug.Log($"LookoutSashTests A: '{sash.name}' open, sill y {sill:F4}; standing sweep: {(standing ? hs.collider.name + " layer " + hs.collider.gameObject.layer + " at " + hs.distance.ToString("F3") : "NOT BLOCKED")}; raised sweep (bottom {sill + 0.05f:F4}): {(raised ? hr.collider.name + " layer " + hr.collider.gameObject.layer + " at " + hr.distance.ToString("F3") : "NOT BLOCKED")}");
                Assert.IsTrue(standing, $"the standing capsule sweep through the open sash '{sash.name}' is not blocked: a new way out");
                Assert.IsTrue(raised, $"the raised capsule sweep through the open sash '{sash.name}' is not blocked: a new way out");
                Assert.AreEqual(WorldLayers.WorldBounds, hr.collider.gameObject.layer, $"the raised sweep through '{sash.name}' is stopped by {hr.collider.name}, not a WorldBounds wall");
                StringAssert.Contains("CabBand", hr.collider.name, $"the blocker of the raised sweep at '{sash.name}' is the window band wall");
                sash.SetState(HingeState.Closed, 0f);
            }
        }

        /// <summary>The closed sash's world bounds (the window's frame and pane), taken before it is opened.</summary>
        static Bounds ClosedBounds(WindowSash s) =>
            s.GetComponentsInChildren<Renderer>(true).Select(r => r.bounds).Aggregate((a, c) => { a.Encapsulate(c); return a; });

        /// <summary>The wall's outward normal at a window: the cab walls are axis aligned, so it is the thin horizontal axis of the closed sash, pointing away from the pedestal.</summary>
        Vector3 WallNormal(Bounds closed)
        {
            Vector3 rel = Flat(closed.center - CabCentre);
            return closed.size.x < closed.size.z ? new Vector3(Mathf.Sign(rel.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(rel.z));
        }

        /// <summary>
        /// L7, spec T4.4, td part 60: at each open sash, with the hatch closed, for 10 s: W and Shift held, the yaw swept +-60 degrees around the wall normal at the script
        /// turn rate, a jump every 0.5 s. The player stays on the cab floor (feet >= floor - 0.1) and the capsule never goes past the window plane. Lowest feet and farthest
        /// outward reach are logged.
        /// </summary>
        [UnityTest]
        public IEnumerator B_WalkingAtEachOpenSash_NeverTakesThePlayerOffTheCabFloor()
        {
            yield return ReachCab();
            // ReachCab leaves the hatch open, and an open sash's window stands right over the opening (against the east wall): a sprint and jump there drops the player
            // 0.21 m onto the top flight, inside the tower. The sashes are tested with the hatch closed, as in the real cab (td part 59 (1)).
            yield return WalkTo(ClearOfHatch, 10f);
            yield return LookAt(hatch.LeafCollider.bounds.center);
            yield return null;
            Assert.AreSame(hatch, interactor.Target, "aiming at the open leaf from the cab");
            yield return Tap(Key.E);
            yield return Until(() => hatch.State == HingeState.Closed, LookoutTargets.HatchMaxSeconds + 1f, "close the hatch");
            Assert.AreEqual(HingeState.Closed, hatch.State, "the hatch is closed before the sashes are tried");
            float floorTop = hatch.OpeningBounds.center.y;
            watch.ExpectAuthoredCamera = true;
            foreach (var sash in sashes)
            {
                var closed = ClosedBounds(sash);
                Vector3 normal = WallNormal(closed);
                float baseYaw = Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg;
                sash.SetState(HingeState.Open, 0f);
                // 0.6 m in from the window plane: the pedestal's ring (COL_CVX_FirefinderRing_01) lies further in, and the start must overlap only the floor (td part 80 (2)).
                yield return StartAt(closed.center - normal * 0.6f + Vector3.up * (floorTop - closed.center.y + 0.05f), baseYaw);
                yield return Wait(0.3f);
                var startFeet = player.transform.position;
                var atStart = Physics.OverlapCapsule(startFeet + Vector3.up * capsule.radius, startFeet + Vector3.up * (capsule.height - capsule.radius), capsule.radius + 0.02f, ~0, QueryTriggerInteraction.Ignore)
                    .Where(c => !c.transform.IsChildOf(player.transform)).Select(c => c.name).Distinct().ToArray();
                Debug.Log($"LookoutSashTests B start '{sash.name}': feet {startFeet:F3}, overlapping {string.Join(" ", atStart)}");
                Assert.IsFalse(atStart.Any(n => n.Contains("Firefinder") || n.Contains("Pedestal")), $"the start at '{sash.name}' overlaps the pedestal: {string.Join(" ", atStart)}");
                // The two key presses go in different frames: InputTestFixture builds the second press from stale state when both are queued in one frame, and the first is lost.
                Hold(Key.W);
                yield return null;
                Hold(Key.LeftShift);
                yield return null;
                Assert.IsTrue(keyboard[Key.W].isPressed, "W is pressed before the timer starts");
                Assert.IsTrue(keyboard[Key.LeftShift].isPressed, "Shift is pressed before the timer starts");
                var diagAt = new System.Collections.Generic.Queue<float>(new[] { 0f, 0.5f, 1.0f });
                float lowest = player.transform.position.y, farthest = float.MinValue, yawOffset = 0f, direction = 1f, nextJump = 0f, releaseJump = -1f;
                for (float t = 0f; t < 10f; t += Time.deltaTime)
                {
                    yawOffset += direction * LookoutTargets.ScriptTurnDegPerSec * Time.deltaTime;
                    if (Mathf.Abs(yawOffset) >= 60f) { yawOffset = Mathf.Clamp(yawOffset, -60f, 60f); direction = -direction; }
                    player.SetLook(baseYaw + yawOffset, player.Pitch);
                    if (t >= nextJump) { Hold(Key.Space); nextJump += 0.5f; releaseJump = t + 0.1f; }
                    else if (releaseJump >= 0f && t >= releaseJump) { Let(Key.Space); releaseJump = -1f; }
                    yield return null;
                    var feet = player.transform.position;
                    if (diagAt.Count > 0 && t >= diagAt.Peek())
                    {
                        float at = diagAt.Dequeue();
                        var overlapping = Physics.OverlapCapsule(feet + Vector3.up * capsule.radius, feet + Vector3.up * (capsule.height - capsule.radius), capsule.radius + 0.05f, ~0, QueryTriggerInteraction.Ignore)
                            .Where(c => !c.transform.IsChildOf(player.transform)).Select(c => $"{c.name}(L{c.gameObject.layer})").Distinct().Take(10);
                        Debug.Log($"LookoutSashTests B diag '{sash.name}' t {at:F1}: controlSuspended {player.ControlSuspended} viewOverridden {player.ViewOverridden} grounded {player.IsGrounded}; W pressed {keyboard[Key.W].isPressed} Shift pressed {keyboard[Key.LeftShift].isPressed} Space pressed {keyboard[Key.Space].isPressed}; capsule.enabled {capsule.enabled} velocity {capsule.velocity:F2} timeScale {Time.timeScale}; feet {feet:F3} yaw {player.Yaw:F1}; overlapping {string.Join(" ", overlapping)}; hatch {hatch.State}, sash {sash.State}, firefinder {firefinder.CurrentPhase}");
                        if (at >= 1.0f)
                        {
                            float moved = Vector3.Distance(startFeet, feet);
                            Assert.GreaterOrEqual(moved, 0.05f, $"'{sash.name}': the player moved only {moved:F3} m in 1 s with W+Shift held (feet {startFeet:F3} -> {feet:F3}); see the diag lines for what holds them (td part 72)");
                        }
                    }
                    lowest = Mathf.Min(lowest, feet.y);
                    farthest = Mathf.Max(farthest, Vector3.Dot(feet - closed.center, normal) + capsule.radius);
                }
                Let(Key.W); Let(Key.LeftShift); Let(Key.Space);
                Debug.Log($"LookoutSashTests B: '{sash.name}' open, 10 s with W+Shift, yaw sweep +-60 deg, jump every 0.5 s: lowest feet y {lowest:F3} (floor {floorTop:F3}), farthest capsule reach past the window plane {farthest:F3} m (wall normal {normal:F0}); feet {startFeet:F3} -> {player.transform.position:F3}");
                Assert.GreaterOrEqual(farthest, -0.30f, $"the player did not get to the window at '{sash.name}' (reach {farthest:F3} m, feet {startFeet:F3} -> {player.transform.position:F3}): W+Shift must move them, or this pass proves nothing");
                Assert.GreaterOrEqual(lowest, floorTop - 0.1f, $"walking and jumping at the open sash '{sash.name}' dropped the player out of the cab");
                Assert.LessOrEqual(farthest, 0f, $"the capsule went past the window plane at the open sash '{sash.name}' by {farthest:F3} m");
                sash.SetState(HingeState.Closed, 0f);
                yield return null;
            }
        }
    }
}
