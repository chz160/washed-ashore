using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;
using WashedAshore.World;

namespace WashedAshore.Tests.Lookout.PlayMode
{
    /// <summary>
    /// #45 (director amend1 and amend2): the hatch's and the sashes' moving leaves on the REAL tower. <see cref="RealTowerWiring"/> wires
    /// the World scene's LookoutTower instance in memory through the real components' public Configure calls, mirroring
    /// LookoutGameplaySetup.Apply (which cannot run: its reader is schema 4, the manifest schema 6). Real geometry, real TrapDoor,
    /// WindowSash and Hinge; the stand-ins S1 to S5 are named in RealTowerWiring and logged by every test. Nothing is saved, no
    /// Interactor is added (TrapDoor.CanInteract ignores its argument). The tower is not wired in World.unity (#40), so each test
    /// wires it in memory; the scene is left unsaved.
    /// Uses only API that exists on main, so it runs unchanged BEFORE and AFTER #45; the parts that only hold AFTER are in
    /// <see cref="LookoutMoversRules"/> and are expected to be red on main.
    /// </summary>
    public abstract class LookoutMoversBase
    {
        protected const float Dt = 1f / 60f;
        protected PlayerController player;
        protected CharacterController capsule;
        protected Transform tower;
        protected TrapDoor hatch;
        protected WindowSash[] sashes;
        float savedCapture;

        [SetUp]
        public void SetUp()
        {
            savedCapture = Time.captureDeltaTime;
            Time.captureDeltaTime = Dt;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = savedCapture;
            if (player != null) player.enabled = true;
        }

        /// <summary>Loads World and wires the real tower in memory (RealTowerWiring; no save).</summary>
        protected IEnumerator LoadRealTower(bool needHatch = true, bool needSashes = true)
        {
            SceneManager.LoadScene("World", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            Assert.AreEqual(1, tagged.Length, "Expected exactly one object tagged Player");
            player = tagged[0].GetComponent<PlayerController>();
            capsule = tagged[0].GetComponent<CharacterController>();
            var root = GameObject.Find("LookoutTower");
            Assert.IsNotNull(root, "No LookoutTower in World");
            tower = root.transform;
            RealTowerWiring.Parts parts = default;
            try { parts = RealTowerWiring.Wire(root); }
            catch (Exception e) { Assert.Fail("RealTowerWiring.Wire threw: " + e); }
            hatch = parts.hatch;
            sashes = parts.sashes ?? new WindowSash[0];
            Debug.Log("LookoutMovers: " + RealTowerWiring.StandInSentence);
            if (parts.standIns != null) foreach (var s in parts.standIns) Debug.Log("LookoutMovers stand-in: " + s);
            // each part is wired on its own: a failure in one fails only the tests that need it
            if (needHatch) { Assert.IsNull(parts.hatchError, "hatch wiring failed: " + parts.hatchError); Assert.IsNotNull(hatch, "wiring did not give a TrapDoor"); }
            if (needSashes) { Assert.IsNull(parts.sashError, "sash wiring failed: " + parts.sashError); Assert.Greater(sashes.Length, 0, "wiring did not give a WindowSash"); }
            yield return null;
        }

        protected static Collider[] CollidersUnder(Component owner) => owner.GetComponentsInChildren<Collider>(true);
        protected static Renderer[] RenderersUnder(Component owner) => owner.GetComponentsInChildren<Renderer>(true);

        protected static Bounds RendererBounds(Renderer[] rs)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var r in rs) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        protected static string V(Vector3 v) => $"({v.x:F4},{v.y:F4},{v.z:F4})";

        /// <summary>Mesh pose against collider pose, for the BEFORE/AFTER comparison.</summary>
        protected static string Pose(string label, Transform leaf, Collider[] cols, Renderer[] rends)
        {
            var sb = new StringBuilder($"LookoutMovers pose {label}: leafEuler={V(leaf.localEulerAngles)} meshCentre={V(RendererBounds(rends).center)}");
            for (int i = 0; i < cols.Length; i++)
                sb.Append($" | col{i} {cols[i].name} on={cols[i].enabled} centre={V(cols[i].bounds.center)} rot={V(cols[i].transform.eulerAngles)}");
            return sb.ToString();
        }

        /// <summary>The leaf's swept volume, from its renderers (valid whether or not the colliders are on), sampled every 1/20 of the swing.</summary>
        protected static Bounds SweptVolume(Action<float> poseAt, Renderer[] rends)
        {
            Bounds b = default;
            for (int i = 0; i <= 20; i++)
            {
                poseAt(i / 20f);
                var s = RendererBounds(rends);
                if (i == 0) b = s; else b.Encapsulate(s);
            }
            return b;
        }

        protected void Place(Vector3 feet)
        {
            player.enabled = false;
            capsule.enabled = false;
            player.transform.position = feet;
            capsule.enabled = true;
        }
    }

    public class LookoutMoversReport : LookoutMoversBase
    {
        const float Timeout = 4f;

        IEnumerator Swing(string label, Func<HingeState> state, Func<float> angle, Action<HingeState> set, Transform leaf, Collider[] cols, Renderer[] rends, float openAngle, HingeState moving, HingeState end, float expectedSeconds)
        {
            set(moving);
            int frames = 0;
            var marks = new[] { 0.25f, 0.5f, 0.75f };
            int next = 0;
            Debug.Log(Pose(label + " start", leaf, cols, rends));
            while (state() == moving && frames < Timeout / Dt)
            {
                yield return null;
                frames++;
                float a = Mathf.Abs(angle());
                float frac = openAngle > 0f ? a / openAngle : 0f;
                if (moving == HingeState.Closing) frac = 1f - frac;
                while (next < marks.Length && frac >= marks[next])
                {
                    Debug.Log(Pose($"{label} mid {marks[next]:P0} (frame {frames}, angle {angle():F3})", leaf, cols, rends));
                    next++;
                }
            }
            Assert.AreEqual(end, state(), label + " did not settle");
            Debug.Log($"LookoutMovers duration {label}: {frames} frames = {frames * Dt:F4} s (tuning {expectedSeconds:F3} s), end angle {angle():F4}");
            Debug.Log(Pose(label + " rest, same frame", leaf, cols, rends));
            yield return null;
            Debug.Log(Pose(label + " rest, next frame", leaf, cols, rends));
        }

        [UnityTest]
        public IEnumerator HatchAndSashes_ReportDurationsAnglesAndPoses()
        {
            yield return LoadRealTower();
            Debug.Log($"LookoutMovers: hatch '{hatch.Id}', {sashes.Length} sash(es), tuning hatch {hatch.Tuning.hatchSeconds:F3} s/{hatch.Tuning.hatchOpenAngle:F2} deg, sash {hatch.Tuning.sashSeconds:F3} s/{hatch.Tuning.sashOpenAngle:F2} deg, autoSyncTransforms={Physics.autoSyncTransforms}");
            var hl = hatch.transform; // the TrapDoor sits on the leaf
            var hc = CollidersUnder(hatch); var hr = RenderersUnder(hatch);
            yield return Swing("hatch opening", () => hatch.State, () => hatch.Angle, s => hatch.SetState(s, 0f), hl, hc, hr, hatch.Tuning.hatchOpenAngle, HingeState.Opening, HingeState.Open, hatch.Tuning.hatchSeconds);
            Assert.AreEqual(hatch.Tuning.hatchOpenAngle, hatch.Angle, 0.05f, "hatch open angle");
            Debug.Log($"LookoutMovers hatch open against the wall: leaf collider bounds {V(hatch.LeafCollider.bounds.min)}..{V(hatch.LeafCollider.bounds.max)}, mesh bounds {V(RendererBounds(hr).min)}..{V(RendererBounds(hr).max)}");
            yield return Swing("hatch closing", () => hatch.State, () => hatch.Angle, s => hatch.SetState(s, 0f), hl, hc, hr, hatch.Tuning.hatchOpenAngle, HingeState.Closing, HingeState.Closed, hatch.Tuning.hatchSeconds);
            Assert.AreEqual(0f, hatch.Angle, 0.05f, "hatch closed angle");
            foreach (var sash in sashes)
            {
                var sl = sash.transform; var sc = CollidersUnder(sash); var sr = RenderersUnder(sash);
                var s2 = sash;
                yield return Swing($"{sash.Id} opening", () => s2.State, () => s2.Angle, s => s2.SetState(s, 0f), sl, sc, sr, sash.Tuning.sashOpenAngle, HingeState.Opening, HingeState.Open, sash.Tuning.sashSeconds);
                Assert.AreEqual(sash.Tuning.sashOpenAngle, sash.Angle, 0.05f, sash.Id + " open angle");
                yield return Swing($"{sash.Id} closing", () => s2.State, () => s2.Angle, s => s2.SetState(s, 0f), sl, sc, sr, sash.Tuning.sashOpenAngle, HingeState.Closing, HingeState.Closed, sash.Tuning.sashSeconds);
                Assert.AreEqual(0f, sash.Angle, 0.05f, sash.Id + " closed angle");
            }
        }

        [UnityTest]
        public IEnumerator Hatch_ClosingIsRefusedWithThePlayerInTheOpening_AndOpeningToo()
        {
            yield return LoadRealTower(needSashes: false);
            hatch.SetState(HingeState.Open, 0f);
            yield return null;
            var open = hatch.OpeningBounds;
            Place(new Vector3(open.center.x, open.center.y, open.center.z));
            yield return null;
            bool canClose = hatch.CanInteract(null);
            hatch.Interact(null);
            yield return null;
            Debug.Log($"LookoutMovers refusal: player in the opening of the OPEN hatch, CanInteract={canClose}, state after Interact={hatch.State}");
            Assert.IsFalse(canClose, "closing must be refused with the player in the opening");
            Assert.AreEqual(HingeState.Open, hatch.State);
            hatch.SetState(HingeState.Closed, 0f);
            yield return null;
            bool canOpen = hatch.CanInteract(null);
            Debug.Log($"LookoutMovers refusal: player in the opening of the SHUT hatch, CanInteract={canOpen}");
            Assert.IsFalse(canOpen, "opening must be refused with the player in the swing volume");
        }
    }

    /// <summary>
    /// What #45 adds. On main the sash parts are red by design: BEFORE records that the sash leaf collider moves while on and that
    /// nothing refuses a swing around the player; AFTER shows them fixed.
    /// </summary>
    public class LookoutMoversRules : LookoutMoversBase
    {
        const float OneMillimetre = 0.001f;

        // ---- placement: a capsule that overlaps nothing, so a wall push-out can never fake (or hide) the leaf's effect ----

        Vector3 CapsuleBottom(Vector3 feet) => feet + capsule.center + Vector3.up * (capsule.radius - capsule.height * 0.5f);
        Vector3 CapsuleTop(Vector3 feet) => feet + capsule.center + Vector3.up * (capsule.height * 0.5f - capsule.radius);

        bool CapsuleClear(Vector3 feet) =>
            !Physics.CheckCapsule(CapsuleBottom(feet), CapsuleTop(feet), capsule.radius, WorldLayers.WorldSolidMask, QueryTriggerInteraction.Ignore);

        Bounds CapsuleBounds(Vector3 feet)
        {
            float r = capsule.radius + capsule.skinWidth;
            return new Bounds(feet + capsule.center, new Vector3(2f * r, capsule.height, 2f * r));
        }

        /// <summary>
        /// The clear capsule position inside <paramref name="volume"/> nearest its centre (grid of 0.1 m), clear with the leaf at rest CLOSED and at
        /// rest OPEN (the two poses it comes to rest in), so a no-penetration check at the end of a forced swing is fair; null if none.
        /// </summary>
        Vector3? ClearSpotInside(Bounds volume, WindowSash sash)
        {
            Physics.SyncTransforms();
            var candidates = new List<Vector3>();
            for (float y = volume.min.y; y <= volume.max.y; y += 0.1f)
                for (float x = volume.min.x; x <= volume.max.x; x += 0.1f)
                    for (float z = volume.min.z; z <= volume.max.z; z += 0.1f)
                    {
                        var feet = new Vector3(x, y, z);
                        if (CapsuleBounds(feet).Intersects(volume) && CapsuleClear(feet)) candidates.Add(feet);
                    }
            sash.SetState(HingeState.Open, 0f);
            Physics.SyncTransforms();
            Vector3? best = null;
            float bestD = float.MaxValue;
            foreach (var feet in candidates)
            {
                if (!CapsuleClear(feet)) continue;
                float d = (feet + capsule.center - volume.center).sqrMagnitude;
                if (d < bestD) { bestD = d; best = feet; }
            }
            sash.SetState(HingeState.Closed, 0f);
            Physics.SyncTransforms();
            Debug.Log($"LookoutMovers placement {sash.Id}: {candidates.Count} candidate positions clear with the leaf closed, {(best.HasValue ? "a spot also clear with it open: " + V(best.Value) : "none also clear with it open")}");
            return best;
        }

        /// <summary>The capsule's deepest penetration into any of the leaf's enabled colliders, from a probe capsule of the player's size.</summary>
        float WorstPenetration(Collider[] leafColliders)
        {
            var probe = new GameObject("PenetrationProbe");
            probe.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
            var cap = probe.AddComponent<CapsuleCollider>();
            cap.center = capsule.center; cap.radius = capsule.radius; cap.height = capsule.height;
            Physics.SyncTransforms();
            float worst = 0f;
            foreach (var c in leafColliders)
            {
                if (c == null || !c.enabled) continue;
                if (Physics.ComputePenetration(cap, probe.transform.position, probe.transform.rotation, c, c.transform.position, c.transform.rotation, out _, out float dist))
                    worst = Mathf.Max(worst, dist);
            }
            UnityEngine.Object.Destroy(probe);
            return worst;
        }

        /// <summary>
        /// A forced swing around the placed capsule. The baseline is taken after one Move(zero) and a frame (E3), so settling is not
        /// counted. Each frame the capsule is asked to resolve overlaps (Move(zero)); a solid leaf around it would push it.
        /// </summary>
        IEnumerator ForcedSwingStaysPut(string label, Func<HingeState> state, Action<HingeState> set, Collider[] cols, Vector3 feet, HingeState moving)
        {
            Place(feet);
            yield return null;
            Assert.IsTrue(CapsuleClear(feet), label + ": the placed capsule overlaps something before the swing (placement precondition)");
            capsule.Move(Vector3.zero);
            yield return null;
            var start = player.transform.position;
            float worst = 0f;
            bool collidersEverOnWhileMoving = false;
            set(moving);
            int frames = 0;
            while (state() == moving && frames < 4f / Dt)
            {
                capsule.Move(Vector3.zero);
                yield return null;
                frames++;
                worst = Mathf.Max(worst, Vector3.Distance(player.transform.position, start));
                if (state() == moving && cols.Any(c => c.enabled)) collidersEverOnWhileMoving = true;
            }
            capsule.Move(Vector3.zero);
            float penetration = WorstPenetration(cols);
            Debug.Log($"LookoutMovers K2 {label}: forced swing {frames} frames, worst player displacement {worst * 1000f:F3} mm, colliders on while moving={collidersEverOnWhileMoving}, deepest penetration into a leaf collider at rest {penetration * 1000f:F3} mm, capsule clear of WorldSolidMask at rest={CapsuleClear(player.transform.position)}");
            Assert.LessOrEqual(worst, OneMillimetre, label + ": the swinging leaf displaced the player");
            Assert.IsFalse(collidersEverOnWhileMoving, label + ": a leaf collider was on while the leaf moved");
            Assert.LessOrEqual(penetration, OneMillimetre, label + ": the capsule penetrates a leaf collider at rest");
        }

        [UnityTest]
        public IEnumerator Sash_PlayerInTheSwingVolume_IsRefused_AndNeverMoved([Values(0, 1, 2, 3)] int index)
        {
            yield return LoadRealTower(needHatch: false);
            Assume.That(index < sashes.Length, "fewer sashes than the case index");
            var sash = sashes[index];
            var rends = RenderersUnder(sash);
            var volume = SweptVolume(f => sash.SetState(HingeState.Opening, f * sash.Tuning.sashSeconds), rends);
            sash.SetState(HingeState.Closed, 0f);
            yield return null;
            var spot = ClearSpotInside(volume, sash);
            if (spot == null)
            {
                Debug.Log($"LookoutMovers K2 {sash.Id}: REFUSAL-ONLY: no capsule position inside the swept volume is clear with the leaf both closed and open {V(volume.min)}..{V(volume.max)}; the displacement check is skipped");
                var edge = new Vector3(volume.center.x, volume.min.y, volume.center.z);
                Place(edge);
                yield return null;
                Assert.IsFalse(sash.CanInteract(null), sash.Id + ": a swing must be refused with the player in its volume");
                sash.SetState(HingeState.Opening, 0f);
                yield return null;
                Assert.IsTrue(CollidersUnder(sash).All(c => !c.enabled), sash.Id + ": colliders must be off while moving");
                yield break;
            }
            Place(spot.Value);
            yield return null;
            bool can = sash.CanInteract(null);
            Debug.Log($"LookoutMovers K2 {sash.Id}: capsule placed at {V(spot.Value)} in the swept volume {V(volume.min)}..{V(volume.max)}, clear of every solid at rest, CanInteract={can}");
            Assert.IsFalse(can, sash.Id + ": a swing must be refused with the player in its volume");
            yield return ForcedSwingStaysPut(sash.Id + " forced opening", () => sash.State, s => sash.SetState(s, 0f), CollidersUnder(sash), spot.Value, HingeState.Opening);
        }

        [UnityTest]
        public IEnumerator Hatch_PlayerJustOutside_IsNeverMoved_AndNoOverlapAfter()
        {
            yield return LoadRealTower(needSashes: false);
            var open = hatch.OpeningBounds;
            // Lifted by the skin width: with the feet exactly on the floor top the bottom sphere touches it, and an overlap test counts touching.
            float lift = capsule.skinWidth;
            var planned = new Vector3(open.min.x - capsule.radius - 0.4f, open.center.y + lift, open.center.z);
            Debug.Log($"LookoutMovers hatch placement: floor top y={open.center.y:F4}, lift={lift:F4} (skinWidth), planned feet {V(planned)}");
            // The planned spot must overlap nothing; if the cab floor or a rail is in the way, take the nearest clear spot beside it.
            Vector3? feet = null;
            float bestD = float.MaxValue;
            Physics.SyncTransforms();
            for (float dx = -0.6f; dx <= 0.6f; dx += 0.1f)
                for (float dz = -0.6f; dz <= 0.6f; dz += 0.1f)
                {
                    var c = planned + new Vector3(dx, 0f, dz);
                    if (!CapsuleClear(c) || CapsuleBounds(c).Intersects(open)) continue;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; feet = c; }
                }
            Assert.IsNotNull(feet, "no clear capsule position beside the hatch opening (placement precondition)");
            Place(feet.Value);
            yield return null;
            Assert.IsTrue(CapsuleClear(feet.Value), "the placed capsule overlaps something (placement precondition)");
            Assert.IsTrue(hatch.CanInteract(null), "the player just outside the swing volume may use the hatch");
            yield return ForcedSwingStaysPut("hatch opening", () => hatch.State, s => hatch.SetState(s, 0f), CollidersUnder(hatch), feet.Value, HingeState.Opening);
            yield return ForcedSwingStaysPut("hatch closing", () => hatch.State, s => hatch.SetState(s, 0f), CollidersUnder(hatch), feet.Value, HingeState.Closing);
        }

        [UnityTest]
        public IEnumerator AllHingeLeafColliders_AreOnAtRest_AndOffWhileMoving()
        {
            yield return LoadRealTower(needHatch: false);
            foreach (var sash in sashes)
            {
                var cols = CollidersUnder(sash);
                Assume.That(cols.Length > 0, sash.Id + " has no collider under its leaf");
                sash.SetState(HingeState.Closed, 0f);
                yield return null;
                Assert.IsTrue(cols.All(c => c.enabled), sash.Id + ": colliders must be on at rest");
                sash.SetState(HingeState.Opening, 0f);
                yield return null;
                Assert.IsTrue(cols.All(c => !c.enabled), sash.Id + ": colliders must be off while moving");
                sash.SetState(HingeState.Open, 0f);
                yield return null;
                Assert.IsTrue(cols.All(c => c.enabled), sash.Id + ": colliders must be on at rest, open");
                sash.SetState(HingeState.Closed, 0f);
            }
        }
    }
}
