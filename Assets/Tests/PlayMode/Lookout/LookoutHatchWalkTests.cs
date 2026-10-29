using System.Collections;
using System.Collections.Generic;
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
    /// #40 T3, td R1e: the hatch on the REAL built tower (the walkable tower). The closed hatch is a floor from above (the cab floor, no step,
    /// bump or jitter), a ceiling from below (the player stops under it and is never inside it), and the camera's near-plane corners never
    /// enter the leaf collider or the swing blocker. Opening it from below shows "Open". If the camera check fails it is a design and look
    /// question for designer-40 and the director, not a collider change (td R1e).
    /// </summary>
    public partial class LookoutHatchWalkTests : LookoutFixture
    {
        const float Floor = 0.05f;          // y within this of the floor top while standing on the closed leaf
        const float FrameStep = 0.002f;      // no step, bump or jitter: the feet move no more than this between frames while walking over it
        const float Millimetre = 0.001f;

        /// <summary>True when the world point is inside the box, whether or not the collider is enabled (the swing blocker is off at rest).</summary>
        static bool Inside(BoxCollider box, Vector3 world)
        {
            var local = box.transform.InverseTransformPoint(world) - box.center;
            var half = box.size * 0.5f;
            return Mathf.Abs(local.x) < half.x && Mathf.Abs(local.y) < half.y && Mathf.Abs(local.z) < half.z;
        }

        IEnumerable<Vector3> NearPlaneCorners()
        {
            var cam = player.ViewCamera;
            foreach (var x in new[] { 0f, 1f })
                foreach (var y in new[] { 0f, 1f })
                    yield return cam.ViewportToWorldPoint(new Vector3(x, y, cam.nearClipPlane));
        }

        /// <summary>td part 13: the near-plane check is against the LEAF only (its collider and its visible mesh); the stop and blocker are logged, never failed on.</summary>
        bool InLeaf(Vector3 world)
        {
            foreach (var b in hatch.GetComponentsInChildren<BoxCollider>(true)) if (Inside(b, world)) return true;
            foreach (var r in hatch.GetComponentsInChildren<MeshRenderer>(true)) if (r.enabled && r.bounds.Contains(world)) return true;
            return false;
        }

        BoxCollider[] LeafAndBlocker()
        {
            var boxes = new List<BoxCollider>();
            boxes.AddRange(hatch.GetComponentsInChildren<BoxCollider>(true));
            foreach (var b in tower.GetComponentsInChildren<BoxCollider>(true))
                if (b.name == "CLIP_BOX_HatchSwing_01") boxes.Add(b);
            Assert.IsTrue(boxes.Any(b => b.name == "CLIP_BOX_HatchSwing_01"), "the swing blocker CLIP_BOX_HatchSwing_01 is not on the tower");
            Assert.IsTrue(boxes.Any(b => b.name == "COL_BOX_Hatch_01"), "the hatch leaf collider COL_BOX_Hatch_01 is not on the tower");
            return boxes.ToArray();
        }

        /// <summary>Deepest penetration of the player's capsule into any of <paramref name="colliders"/> (enabled ones), by a probe capsule.</summary>
        float Penetration(IEnumerable<Collider> colliders)
        {
            var probe = new GameObject("PenetrationProbe");
            probe.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
            var cap = probe.AddComponent<CapsuleCollider>();
            cap.center = capsule.center; cap.radius = capsule.radius; cap.height = capsule.height;
            Physics.SyncTransforms();
            float worst = 0f;
            foreach (var c in colliders)
            {
                if (c == null || !c.enabled) continue;
                if (Physics.ComputePenetration(cap, probe.transform.position, probe.transform.rotation, c, c.transform.position, c.transform.rotation, out _, out float d))
                    worst = Mathf.Max(worst, d);
            }
            Object.Destroy(probe);
            return worst;
        }

        /// <summary>L2 / T3 from above: stand on the closed leaf, then walk across it as over the cab floor.</summary>
        [UnityTest]
        public IEnumerator A_TheClosedHatch_IsAFloorFromAbove()
        {
            yield return LoadTower();
            Assert.AreEqual(HingeState.Closed, hatch.State, "the hatch starts closed");
            var opening = hatch.OpeningBounds;
            float floorTop = opening.center.y;
            var leafBoxes = LeafAndBlocker();
            // The cab is 2.06 m square with the pedestal at its centre and the opening against the east wall, so the walk is the one the guided tower used:
            // along x on the strip 0.15 m inside the opening's south edge, from the west cab floor east across the closed leaf (K40 diagnostic: the old start was outside the cab).
            float lineZ = opening.min.z + 0.15f;
            yield return StartAt(new Vector3(opening.min.x - 0.5f, floorTop + 0.3f, lineZ), 90f);
            yield return Wait(0.5f);
            Assert.IsTrue(player.IsGrounded, "the player stands on the cab floor before the opening");
            Hold(Key.W);
            // The CharacterController rests on the floor by its skin (50 mm here), so the feet are compared with the SAME player's feet standing on the plain cab floor
            // beside the opening, not with the floor top (td part 49): stricter than the old 0.05 m bound, within the 2 mm frame step.
            float plainFloorY = player.transform.position.y;
            float lastY = plainFloorY, worstJump = 0f, worstOff = 0f, worstPen = 0f;
            bool onLeaf = false;
            for (float t = 0f; t < 6f; t += Time.deltaTime)
            {
                yield return null;
                var p = player.transform.position;
                if (p.x > opening.min.x + 0.1f && p.x < opening.max.x - 0.1f) onLeaf = true;
                if (p.x > opening.max.x - 0.3f) break;   // well onto the leaf toward the east wall: crossed
                worstJump = Mathf.Max(worstJump, Mathf.Abs(p.y - lastY));
                worstOff = Mathf.Max(worstOff, Mathf.Abs(p.y - plainFloorY));
                worstPen = Mathf.Max(worstPen, Penetration(leafBoxes));
                lastY = p.y;
            }
            Let(Key.W);
            Debug.Log($"LookoutHatchWalk A: walked over the closed leaf={onLeaf}, floor top y={floorTop:F4}, plain cab floor feet y={plainFloorY:F4}, worst |y - plain floor feet| {worstOff * 1000f:F2} mm, worst frame step {worstJump * 1000f:F2} mm, deepest penetration {worstPen * 1000f:F3} mm");
            Assert.IsTrue(onLeaf, "the walk crossed the opening");
            Assert.LessOrEqual(worstOff, FrameStep, "over the closed hatch the player's feet stay level with their feet on the plain cab floor");
            Assert.LessOrEqual(worstJump, FrameStep, "no step, bump or jitter between frames over the closed hatch");
            Assert.LessOrEqual(worstPen, Millimetre, "the player never sinks into the closed leaf");
            Assert.IsEmpty(watch.Violations, "lane-free watch: " + string.Join("; ", watch.Violations));
        }

        /// <summary>The headroom stop (td part 4 B): a WorldBounds box under the closed leaf. Fails by name if the build step did not make it.</summary>
        BoxCollider HeadroomStop()
        {
            var stop = tower.GetComponentsInChildren<BoxCollider>(true).FirstOrDefault(b => b.name == "HATCH_HeadroomStop");
            Assert.IsNotNull(stop, "HATCH_HeadroomStop is not on the tower");
            return stop;
        }

        /// <summary>The top flight's foot and head in world space, from the manifest (no lanes).</summary>
        void TopFlight(out Vector3 foot, out Vector3 head)
        {
            var top = ReadManifest().flights.Last();
            foot = tower.TransformPoint(new Vector3(top.foot[0], top.foot[1], top.foot[2]));
            head = tower.TransformPoint(new Vector3(top.head[0], top.head[1], top.head[2]));
        }

        float EyeHeight() => player.CameraPivot.position.y - player.transform.position.y;

        /// <summary>Lowest world y of the closed leaf's colliders (its underside).</summary>
        float LeafUnderside()
        {
            var cols = hatch.GetComponentsInChildren<Collider>(true);
            return cols.Min(c => c.bounds.min.y);
        }

        /// <summary>World y of the headroom stop's lower face (its box centre is zero).</summary>
        float StopLowerY(BoxCollider stop) => stop.transform.position.y - stop.size.y * 0.5f * Mathf.Abs(stop.transform.lossyScale.y);

        /// <summary>
        /// Places the player on the top flight, on the steps, with the capsule top at <paramref name="topY"/> (world), settles, and asserts the player is
        /// grounded and inside the lane (td part 53: a start must be somewhere a player can reach by walking).
        /// </summary>
        IEnumerator PlaceOnFlight(float topY, Vector3 foot, Vector3 head, float width)
        {
            Vector3 dirH = Flat(head - foot).normalized;
            float length = Flat(head - foot).magnitude, tan = (head.y - foot.y) / length;
            // A rough placement first, so the tower's slim capsule (TowerWalkZones) is in force and capsule.height is the one the player really has there.
            yield return StartAt(foot + dirH * 0.05f + Vector3.up * 0.1f, Quaternion.LookRotation(dirH).eulerAngles.y);
            yield return Wait(0.3f);
            float surfaceY = topY - capsule.height;
            float along = (surfaceY - foot.y) / tan;
            Assert.That(along, Is.InRange(0f, length), $"start: capsule top {topY:F3} puts the feet {along:F3} m along a {length:F3} m flight (surface y {surfaceY:F3})");
            Vector3 feet = foot + dirH * along + Vector3.up * (surfaceY - foot.y + 0.1f);
            yield return StartAt(feet, Quaternion.LookRotation(dirH).eulerAngles.y);
            yield return Wait(0.5f);
            var rel = player.transform.position - foot;
            float lateral = Mathf.Abs(Vector3.Dot(rel, Vector3.Cross(Vector3.up, dirH)));
            Assert.IsTrue(player.IsGrounded, $"start: the player is grounded on the flight (feet {player.transform.position:F3})");
            Assert.LessOrEqual(lateral, width * 0.5f, $"start: the player is inside the lane after StartAt (lateral {lateral:F3} m, feet {player.transform.position:F3})");
        }

        /// <summary>The start for B and C: on the flight, the capsule top at least 0.10 m under the stop's lower face (td part 53).</summary>
        IEnumerator StartBelowTheStop(BoxCollider stop, Vector3 foot, Vector3 head)
        {
            float limit = StopLowerY(stop) - 0.10f;   // the start must keep the capsule top at least 0.10 m under the stop's lower face
            float width = ReadManifest().flights.Last().width;
            float top = limit - 0.02f;
            yield return PlaceOnFlight(top, foot, head, width);
            // The controller settles on the steps a skin above the surface, so the real top lands higher than asked: read it, and step down the flight by the excess (max 5 times).
            for (int i = 0; i < 5; i++)
            {
                float real = player.transform.position.y + capsule.height, excess = real - limit;
                Debug.Log($"LookoutHatchWalk start {i}: asked top {top:F4}, real top {real:F4}, limit {limit:F4}, excess {excess * 1000f:F1} mm, feet {player.transform.position:F4}");
                if (excess <= 0f) break;
                top -= excess + 0.005f;
                yield return PlaceOnFlight(top, foot, head, width);
            }
            Assert.LessOrEqual(player.transform.position.y + capsule.height, limit, "start: the capsule top is at least 0.10 m under the stop's lower face");
        }

        /// <summary>td part 4 / director amend1, from below: the player walks up and stops at the HEADROOM STOP with the leaf just overhead.
        /// Eye to leaf at least 0.15 m (hard floor 0.12), no slide or jitter at the stop, near-plane corners clear of the leaf, the blocker and the stop.</summary>
        [UnityTest]
        public IEnumerator B_FromBelow_ThePlayerStopsAtTheHeadroomStop_WithTheLeafJustOverhead()
        {
            yield return LoadTower();
            var stop = HeadroomStop();
            Assert.IsTrue(stop.enabled, "the stop is on while the hatch is Closed");
            Assert.AreEqual(WorldLayers.WorldBounds, stop.gameObject.layer, "the stop is on WorldBounds");
            var boxes = LeafAndBlocker().Concat(new[] { stop }).ToArray();
            TopFlight(out var foot, out var head);
            Vector3 dir = Flat(head - foot);
            yield return StartBelowTheStop(stop, foot, head);
            yield return Face(dir);
            Hold(Key.W);
            float highest = player.transform.position.y, worstPen = 0f;
            int cornerBreaks = 0, otherCorner = 0; string firstBreak = null;
            for (float t = 0f; t < 12f; t += Time.deltaTime)
            {
                yield return null;
                var p = player.transform.position;
                highest = Mathf.Max(highest, p.y);
                worstPen = Mathf.Max(worstPen, Penetration(boxes));
                foreach (var corner in NearPlaneCorners())
                {
                    if (InLeaf(corner)) { cornerBreaks++; firstBreak ??= $"near-plane corner {corner:F3} inside the leaf at feet {p:F3}"; }
                    else if (boxes.Any(b => Inside(b, corner))) otherCorner++;
                }
            }
            // Held against the stop for a further 2 s: no slide, no jitter.
            var rest = player.transform.position; float drift = 0f;
            for (float t = 0f; t < 2f; t += Time.deltaTime) { yield return null; drift = Mathf.Max(drift, Vector3.Distance(player.transform.position, rest)); }
            Let(Key.W);
            float eyeToLeaf = LeafUnderside() - player.CameraPivot.position.y;
            Debug.Log($"LookoutHatchWalk B: stopped at {rest:F3}, eye to leaf {eyeToLeaf:F3} m, drift {drift * 1000f:F2} mm, penetration {worstPen * 1000f:F3} mm, near-plane corner breaks (leaf) {cornerBreaks}, (stop or blocker, logged only) {otherCorner}");
            Assert.Less(highest, LeafUnderside() - 0.2f, "the player never reaches the floor height from below");
            Assert.GreaterOrEqual(eyeToLeaf, 0.12f, "eye to leaf is never under the 0.12 m hard floor");
            Assert.GreaterOrEqual(eyeToLeaf, 0.15f, "eye to leaf at the stop is at least 0.15 m");
            Assert.LessOrEqual(drift, Millimetre * 2f, "no slide or jitter while held against the stop");
            Assert.LessOrEqual(worstPen, Millimetre, "the player is never inside the leaf, the blocker or the stop");
            Assert.AreEqual(0, cornerBreaks, "the camera's near plane never enters the leaf: " + firstBreak);
        }

        /// <summary>From the stop: the aim hits the leaf, the prompt reads "Open", E opens it, and nothing clips during the swing.</summary>
        [UnityTest]
        public IEnumerator C_FromTheStop_TheAimHitsTheLeaf_ThePromptReadsOpen_AndEOpensIt()
        {
            yield return LoadTower();
            var stop = HeadroomStop();
            var boxes = LeafAndBlocker().Concat(new[] { stop }).ToArray();
            TopFlight(out var foot, out var head);
            Vector3 dir = Flat(head - foot);
            yield return StartBelowTheStop(stop, foot, head);
            yield return Face(dir);
            Hold(Key.W);
            yield return Wait(8f); // up to the stop
            Let(Key.W);
            yield return LookAt(hatch.OpeningBounds.center);
            yield return Wait(0.3f);
            Debug.Log($"LookoutHatchWalk C: prompt '{interactor.PromptLine}', aimed at {(interactor.AimedAt as Component)?.name}");
            Assert.AreSame(hatch, interactor.AimedAt, "the interactor's aim from the stop hits the hatch leaf");
            StringAssert.Contains("Open", interactor.PromptLine ?? "", "the prompt reads Open");
            yield return Tap(Key.E);
            int corners = 0, otherC = 0; string first = null;
            for (float t = 0f; t < hatch.Tuning.hatchSeconds + 0.5f; t += Time.deltaTime)
            {
                yield return null;
                foreach (var corner in NearPlaneCorners())
                {
                    if (InLeaf(corner)) { corners++; first ??= $"leaf at {corner:F3}"; }
                    else if (boxes.Any(b => b.enabled && Inside(b, corner))) otherC++;
                }
            }
            Assert.AreNotEqual(HingeState.Closed, hatch.State, "E opened the hatch");
            Debug.Log($"LookoutHatchWalk C: near-plane corners in the leaf {corners}, in the stop or blocker (logged only) {otherC}");
            Assert.AreEqual(0, corners, "no near-plane corner enters the leaf during the swing: " + first);
            if (hatch.State == HingeState.Open) Assert.IsFalse(stop.enabled, "the stop is off once the hatch is open");
        }

        /// <summary>One place the player's feet are put on the flight for the band test: the surface y at `feetY`, settled, with the real feet y read back.</summary>
        IEnumerator PutFeetAtY(float feetY, Vector3 foot, Vector3 head)
        {
            Vector3 dirH = Flat(head - foot).normalized;
            float length = Flat(head - foot).magnitude, tan = (head.y - foot.y) / length;
            float ask = feetY, err = 0f, lowest = foot.y + 0.01f, highest = head.y - 0.01f;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                ask = Mathf.Clamp(ask, lowest, highest);   // a re-ask never leaves the flight
                float along = (ask - foot.y) / tan;
                Assert.That(along, Is.InRange(0f, length), $"band step: feet y {ask:F3} is {along:F3} m along a {length:F3} m flight (target {feetY:F3})");
                yield return StartAt(foot + dirH * along + Vector3.up * (ask - foot.y + 0.02f), Quaternion.LookRotation(dirH).eulerAngles.y);
                yield return Wait(0.25f);
                err = feetY - player.transform.position.y;
                if (Mathf.Abs(err) < 0.002f) break;
                bool atEnd = (ask <= lowest + 1e-4f && err < 0f) || (ask >= highest - 1e-4f && err > 0f);
                if (atEnd) break;   // the flight cannot offer this height: log UNREACHABLE below and use where the player settled
                ask += err;
            }
            if (Mathf.Abs(err) >= 0.002f)
                Debug.Log($"LookoutHatchWalk E: UNREACHABLE height: target feet y {feetY:F4}, the player settled at {player.transform.position.y:F4} (off by {err * 1000f:F1} mm after 4 placements, ask {ask:F4})");
        }

        /// <summary>
        /// td part 54 (4), director amend5: through the OPEN hatch, up and down the top flight, the eye is at full height, matches up and down at the middle of the flight within 1 mm,
        /// stays at least 0.15 m from the cab floor and the leaf, and no near-plane corner is inside either. PassEyeHeight's unclamped value and inputs are logged next to the standing eye.
        /// </summary>
        [UnityTest]
        public IEnumerator D_ThroughTheOpenHatch_TheEyeIsFullHeight_UpAndDown_AndClearOfTheFloorAndLeaf()
        {
            yield return LoadTower();
            hatch.SetState(HingeState.Open, 0f);
            yield return null;
            TopFlight(out var foot, out var head);
            Vector3 dir = Flat(head - foot), dirH = dir.normalized;
            float length = dir.magnitude, pitchRad = Mathf.Atan2(head.y - foot.y, length);
            var tuning = hatch.Tuning;
            float standing = player.AuthoredEyeHeight;
            // No interaction has happened: with P3 fixed TrapDoor finds the player from LateUpdate on its own, so after a frame PassEyeHeight is the derived value clamped to the standing eye.
            yield return null;
            yield return null;
            Assert.AreEqual(standing, hatch.PassEyeHeight, 0.001f, "PassEyeHeight is 1.65 after a frame with no interaction (the duck mechanism is awake, td part 61 P3)");
            float unclamped = hatch.OpeningLength * Mathf.Tan(pitchRad) - hatch.FloorThickness - tuning.eyeClearance / Mathf.Cos(pitchRad);
            Debug.Log($"LookoutHatchWalk D: PassEyeHeight {hatch.PassEyeHeight:F4} (derived: openingLength {hatch.OpeningLength:F4} x tan(pitch {pitchRad * Mathf.Rad2Deg:F3} deg) - floorThickness {hatch.FloorThickness:F4} - eyeClearance {tuning.eyeClearance:F3} / cos = unclamped {unclamped:F4}, clamped to [hatchDuckMinEyeHeight {tuning.hatchDuckMinEyeHeight:F3}, standing {standing:F4}]; hatchDuckRun {tuning.hatchDuckRun:F3})");
            var surfaces = tower.GetComponentsInChildren<Collider>(true).Where(c => c.name.StartsWith("COL_BOX_CabFloor") || c.name.StartsWith("COL_BOX_CabWall") || c.transform.IsChildOf(hatch.transform)).ToArray();
            float minDist = float.MaxValue; string minWhere = ""; int cornerIn = 0; float minEye = float.MaxValue, maxEye = float.MinValue;
            var leg2 = new System.Collections.Generic.Dictionary<string, (float dist, string where, float eyeMin, float eyeMax, int corners)>();
            float eyeUpMid = float.NaN, eyeDownMid = float.NaN;
            float mid = length * 0.5f;
            void Sample(string leg)
            {
                var cam = player.CameraPivot.position;
                float eye = player.EyeHeight;   // #46: the sim eye; the camera is one tick behind by design (td-46 c2-rulings/final a)
                minEye = Mathf.Min(minEye, eye); maxEye = Mathf.Max(maxEye, eye);
                string key = leg.StartsWith("up") ? "up" : "down";
                (float dist, string where, float eyeMin, float eyeMax, int corners) fresh = (float.MaxValue, "", float.MaxValue, float.MinValue, 0);
                var s = leg2.TryGetValue(key, out var have) ? have : fresh;
                s.eyeMin = Mathf.Min(s.eyeMin, eye); s.eyeMax = Mathf.Max(s.eyeMax, eye);
                foreach (var c in surfaces)
                {
                    if (!c.enabled) continue;
                    float d = Vector3.Distance(c.ClosestPoint(cam), cam);
                    string where = $"{leg}: {c.name} at feet {player.transform.position:F3}";
                    if (d < minDist) { minDist = d; minWhere = where; }
                    if (d < s.dist) { s.dist = d; s.where = where; }
                    if (c is BoxCollider bc) foreach (var corner in NearPlaneCorners()) if (Inside(bc, corner)) { cornerIn++; s.corners++; }
                }
                leg2[key] = s;
            }
            // UP: from the foot of the flight, through the open hatch, onto the cab floor.
            yield return PutFeetAtY(foot.y + 0.05f, foot, head);
            yield return Face(dir);
            Hold(Key.W);
            for (float t = 0f; t < 12f; t += Time.deltaTime)
            {
                yield return null;
                var rel = player.transform.position - foot;
                float along = Vector3.Dot(Flat(rel), dirH);
                Sample("up");
                if (float.IsNaN(eyeUpMid) && along >= mid) eyeUpMid = player.EyeHeight;   // #46: the sim eye at the sim position; the camera is one tick behind by design
                if (player.transform.position.y > head.y - 0.05f) { yield return Wait(0.3f); Sample("up-top"); break; }
            }
            Let(Key.W);
            Assert.IsFalse(float.IsNaN(eyeUpMid), "going up passed the middle of the flight");
            Assert.Greater(hatch.PassEyeHeight, 0f, "the duck mechanism is awake after walking the flight with no interaction: TrapDoor found the player on its own (td part 61, P3)");
            // DOWN: turn round on the cab floor and walk back down through the opening.
            yield return Face(-dir);
            Hold(Key.W);
            for (float t = 0f; t < 12f; t += Time.deltaTime)
            {
                yield return null;
                var rel = player.transform.position - foot;
                float along = Vector3.Dot(Flat(rel), dirH);
                Sample("down");
                if (float.IsNaN(eyeDownMid) && along <= mid && player.transform.position.y < head.y - 0.3f) eyeDownMid = player.EyeHeight;   // #46: same reading going down
                if (player.transform.position.y < foot.y + 0.3f) break;
            }
            Let(Key.W);
            foreach (var kv in leg2)
                Debug.Log($"LookoutHatchWalk D {kv.Key} leg: eye min {kv.Value.eyeMin:F4} max {kv.Value.eyeMax:F4}; closest surface to the camera {kv.Value.dist:F4} m ({kv.Value.where}); near-plane corners inside the floor or leaf {kv.Value.corners}");
            Debug.Log($"LookoutHatchWalk D: PassEyeHeight now {hatch.PassEyeHeight:F4}; eye min {minEye:F4} max {maxEye:F4} (standing {standing:F4}); at the middle of the flight up {eyeUpMid:F4}, down {eyeDownMid:F4}; closest surface to the camera {minDist:F4} m ({minWhere}); near-plane corners inside the floor or leaf: {cornerIn}");
            Assert.IsFalse(float.IsNaN(eyeDownMid), "going down passed the middle of the flight");
            Assert.AreEqual(eyeUpMid, eyeDownMid, 0.001f, "the same eye height at the middle of the flight going up and down (1 mm)");
            Assert.GreaterOrEqual(minEye, standing - 0.001f, "the eye stays at full height through the open hatch (director amend5)");
            Assert.GreaterOrEqual(minDist, 0.15f, "the eye stays at least 0.15 m from the cab floor and the leaf: " + minWhere);
            Assert.AreEqual(0, cornerIn, "no near-plane corner inside the cab floor or the leaf");
        }
    }
}
