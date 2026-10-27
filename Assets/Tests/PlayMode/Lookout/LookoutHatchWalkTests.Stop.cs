using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using WashedAshore.Lookout;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// #40 the headroom stop and the close refusal (director amend5, td parts 54, 55, 61): the band rows, the walk-up sweep, the close swing and the open from the stop.
    /// A partial of LookoutHatchWalkTests so it shares its helpers.
    /// </summary>
    public partial class LookoutHatchWalkTests
    {
        /// <summary>
        /// Director amend5, td parts 54 and 55 (the P fix). Hatch OPEN, feet stepped every 1 cm up the top flight through the stop band and the swing box (115.28-115.60,
        /// which covers 115.39-115.54): closing is refused silently exactly when the capsule grown by its skin meets the stop volume, E then leaves the hatch Open and the player
        /// does not move (1 mm). Below the band E closes it and the player does not move. Then walked up to the stop, E opens it and the player does not move. Every row is logged.
        /// </summary>
        [UnityTest]
        public IEnumerator E_Close_IsRefusedAcrossTheStopBand_AndAllowedBelowIt_AndOpenFromTheStopWorks_WithoutMovingThePlayer()
        {
            yield return LoadTower();
            var stop = HeadroomStop();
            TopFlight(out var foot, out var head);
            hatch.SetState(HingeState.Open, 0f);
            yield return null;
            Assert.IsFalse(stop.enabled, "the stop is off while the hatch is open");
            yield return PutFeetAtY(foot.y + 0.05f, foot, head);   // slim capsule in force
            float skin = capsule.skinWidth, capH = capsule.height;
            float underside = hatch.OpeningBounds.min.y - hatch.FloorThickness;
            float stopLower = StopLowerY(stop);
            Debug.Log($"LookoutHatchWalk E: capsule h {capH:F3} r {capsule.radius:F3} skin {skin:F3}; underside {underside:F4}, stop lower face {stopLower:F4} (depth {underside - stopLower:F4}); the band starts where feet + h + skin > {stopLower:F4}, i.e. feet y > {stopLower - capH - skin:F4}");
            var rows = new System.Text.StringBuilder();
            int refusedRows = 0, allowedRows = 0; float worstMove = 0f;
            for (float y = foot.y + 0.10f; y <= 115.60f + 1e-4f; y += 0.01f)
            {
                yield return PutFeetAtY(y, foot, head);
                var p0 = player.transform.position;
                float top = p0.y + capH;
                bool overlaps = top + skin > stopLower;
                bool can = hatch.CanInteract(interactor);
                string note = "";
                float gap = top + skin - stopLower;   // > 0: the skin-grown capsule top is inside the stop volume
                bool clearlyIn = gap > 0.0005f, clearlyOut = gap < -0.0005f;   // 0.5 mm deadband, only for the float noise at the exact boundary
                if (!clearlyIn && !clearlyOut) note = " BOUNDARY ROW (not asserted)";
                if (clearlyIn) Assert.IsFalse(can, $"band row feet y {p0.y:F4} (capsule top {top:F4} + skin {skin:F3} > stop face {stopLower:F4} by {gap * 1000f:F2} mm): closing must be refused");
                if (clearlyOut) Assert.IsTrue(can, $"row feet y {p0.y:F4} (capsule top {top:F4} + skin {skin:F3} < stop face {stopLower:F4} by {-gap * 1000f:F2} mm): closing is allowed");
                if (clearlyIn)
                {
                    refusedRows++;
                    hatch.Interact(interactor);
                    yield return Wait(0.1f);
                    float moved = Vector3.Distance(p0, player.transform.position);
                    worstMove = Mathf.Max(worstMove, moved);
                    Assert.AreEqual(HingeState.Open, hatch.State, $"E at feet y {p0.y:F4} left the hatch Open");
                    Assert.LessOrEqual(moved, Millimetre, $"the player did not move at feet y {p0.y:F4}");
                    note += $" E -> {hatch.State}, moved {moved * 1000f:F2} mm";
                }
                else if (clearlyOut) allowedRows++;
                rows.AppendLine($"  feet y {p0.y:F4} top {top:F4} +skin {top + skin:F4} gap {gap * 1000f:F2} mm overlapsStop {overlaps} canInteract {can}{note}");
            }
            Debug.Log($"LookoutHatchWalk E rows ({refusedRows} refused, {allowedRows} allowed, worst move {worstMove * 1000f:F2} mm):\n{rows}");
            Assert.Greater(refusedRows, 0, "some rows were in the band");
            Assert.Greater(allowedRows, 0, "some rows were below the band");

            // Below the band: E closes it and the player does not move.
            yield return PutFeetAtY(stopLower - 0.10f - capH, foot, head);
            var before = player.transform.position;
            Assert.IsTrue(hatch.CanInteract(interactor), "closing is allowed with the head 0.10 m below the stop's lower face");
            hatch.Interact(interactor);
            yield return Wait(hatch.Tuning.hatchSeconds + 0.5f);
            Debug.Log($"LookoutHatchWalk E: below the band: feet {before:F4} -> {player.transform.position:F4}, hatch {hatch.State}, stop enabled {stop.enabled}");
            Assert.AreEqual(HingeState.Closed, hatch.State, "the hatch closed");
            Assert.IsTrue(stop.enabled, "the stop is on once closed");
            Assert.LessOrEqual(Vector3.Distance(before, player.transform.position), Millimetre, "the player did not move while the hatch closed");

            // Walk up to the stop (hatch Closed), then open it from there.
            Vector3 dir = Flat(head - foot);
            yield return Face(dir);
            Hold(Key.W);
            yield return Wait(4f);
            Let(Key.W);
            yield return Wait(0.5f);
            before = player.transform.position;
            Assert.IsTrue(hatch.CanInteract(interactor), "with the hatch Closed and the player resting at the stop, opening is allowed");
            hatch.Interact(interactor);
            yield return Wait(hatch.Tuning.hatchSeconds + 0.5f);
            Debug.Log($"LookoutHatchWalk E: from the stop: feet {before:F4} -> {player.transform.position:F4}, hatch {hatch.State}");
            Assert.AreEqual(HingeState.Open, hatch.State, "E opened the hatch from the stop");
            Assert.LessOrEqual(Vector3.Distance(before, player.transform.position), Millimetre, "the player did not move while the hatch opened");
        }

        /// <summary>
        /// td part 61 (Q5): the walk-up sweep, hatch OPEN. Every frame while the player climbs the flight through the band edge, CanInteract must equal !(gap > 0) wherever
        /// |gap| > 0.5 mm (gap = capsule top + skin - stop face); every frame within +-10 mm of the edge is logged.
        /// </summary>
        [UnityTest]
        public IEnumerator E2_WalkingUpThroughTheBandEdge_CanInteractFollowsTheGap_EveryFrame()
        {
            yield return LoadTower();
            var stop = HeadroomStop();
            TopFlight(out var foot, out var head);
            hatch.SetState(HingeState.Open, 0f);
            yield return null;
            yield return PutFeetAtY(foot.y + 0.05f, foot, head);
            float skin = capsule.skinWidth, capH = capsule.height, stopLower = StopLowerY(stop);
            yield return Face(Flat(head - foot));
            Hold(Key.W);
            var edge = new System.Text.StringBuilder();
            int frames = 0, asserted = 0; float lowestGap = float.MaxValue, highestGap = float.MinValue;
            for (float t = 0f; t < 8f; t += Time.deltaTime)
            {
                yield return null;
                var p = player.transform.position;
                float gap = p.y + capH + skin - stopLower;
                bool can = hatch.CanInteract(interactor);
                frames++;
                lowestGap = Mathf.Min(lowestGap, gap); highestGap = Mathf.Max(highestGap, gap);
                if (Mathf.Abs(gap) <= 0.010f) edge.AppendLine($"  t {t:F3} feet {p:F4} gap {gap * 1000f:F2} mm canInteract {can}");
                if (Mathf.Abs(gap) > 0.0005f)
                {
                    asserted++;
                    Assert.AreEqual(!(gap > 0f), can, $"frame {frames}: feet y {p.y:F4}, gap {gap * 1000f:F2} mm: CanInteract must be {!(gap > 0f)}");
                }
                if (gap > 0.30f) break;
            }
            Let(Key.W);
            Debug.Log($"LookoutHatchWalk E2: {frames} frames, {asserted} asserted, gap from {lowestGap * 1000f:F1} to {highestGap * 1000f:F1} mm; rows within +-10 mm of the band edge:\n{edge}");
            Assert.Less(lowestGap, -0.010f, "the sweep started below the band edge");
            Assert.Greater(highestGap, 0.010f, "the sweep went through the band edge");
        }

        /// <summary>
        /// td part 61 (P2) and director amend5: E below the band, then walk up DURING the close swing. The stop is on while Closing, so the player is held at it, never pushed:
        /// at Closed the capsule top + skin is no more than 1 mm above the stop face and no frame moves a held player more than 1 mm. Then E opens it from the stop, and the stop is
        /// off while Opening and while Open.
        /// </summary>
        [UnityTest]
        public IEnumerator F_DuringTheCloseSwing_TheStopHoldsAWalkingPlayerStill_AndIsOffWhileOpeningAndOpen()
        {
            yield return LoadTower();
            var stop = HeadroomStop();
            TopFlight(out var foot, out var head);
            hatch.SetState(HingeState.Open, 0f);
            yield return null;
            Assert.IsFalse(stop.enabled, "the stop is off while the hatch is Open");
            yield return PutFeetAtY(foot.y + 0.05f, foot, head);
            float skin = capsule.skinWidth, capH = capsule.height, stopLower = StopLowerY(stop);
            yield return PutFeetAtY(stopLower - 0.10f - capH, foot, head);   // head 0.10 m below the stop's lower face
            yield return Face(Flat(head - foot));
            Assert.IsTrue(hatch.CanInteract(interactor), "closing is allowed below the band");
            hatch.Interact(interactor);   // what E does
            Assert.AreEqual(HingeState.Closing, hatch.State, "E started the close swing");
            Assert.IsTrue(stop.enabled, "the stop is ON while the hatch is Closing (td part 61, P2)");
            Hold(Key.W);                  // walk up during the swing
            var prev = player.transform.position; int held = 0, pushes = 0, heldAtClosed = -1; float worstPush = 0f, closedAt = -1f, topAtClosed = float.NaN;
            for (float t = 0f; t < hatch.Tuning.hatchSeconds + 2.5f; t += Time.deltaTime)
            {
                yield return null;
                var p = player.transform.position;
                float step = Vector3.Distance(prev, p);
                if (closedAt < 0f && hatch.State == HingeState.Closed) { closedAt = t; heldAtClosed = held; topAtClosed = p.y + capH - stopLower; }
                if (held >= 3 && step > Millimetre) { pushes++; worstPush = Mathf.Max(worstPush, step); }
                held = step < 0.0005f ? held + 1 : 0;
                prev = p;
            }
            Let(Key.W);
            // The release lands on the next input update, so W is still held for a frame: wait it out and prove the player is still before sampling rest (td part 85).
            yield return Wait(0.5f);
            var last = player.transform.position; float worstStill = 0f;
            for (int i = 0; i < 10; i++) { yield return null; var q = player.transform.position; worstStill = Mathf.Max(worstStill, Vector3.Distance(last, q)); last = q; }
            Assert.Less(worstStill, 0.0005f, $"the player is still after W is released (worst step over 10 frames {worstStill * 1000f:F2} mm)");
            var rest = player.transform.position;
            Debug.Log($"LookoutHatchWalk F: closed at {closedAt:F2} s with the player already held for {heldAtClosed} frames (capsule top without skin {topAtClosed * 1000f:F2} mm above the stop face on that frame; negative = under it); held at feet {rest:F4}, capsule top without skin {(rest.y + capH - stopLower) * 1000f:F2} mm above the stop face (the skin is {skin * 1000f:F0} mm), pushes {pushes}, worst push {worstPush * 1000f:F2} mm, eye to leaf underside {LeafUnderside() - player.CameraPivot.position.y:F4} m");
            Assert.AreEqual(HingeState.Closed, hatch.State, "the hatch closed");
            Assert.GreaterOrEqual(heldAtClosed, 3, "the player was already held at the stop on the frame the hatch became Closed (otherwise P2 was not exercised: the stop would have appeared around a moving player)");
            Assert.LessOrEqual(topAtClosed, Millimetre, "on the frame the hatch became Closed the capsule top (no skin) is no more than 1 mm above the stop face (td part 80: the capsule rests inside its skin of the face)");
            Assert.IsTrue(stop.enabled, "the stop is on at Closed");
            Assert.LessOrEqual(rest.y + capH - stopLower, Millimetre, "at rest the capsule top (no skin) is no more than 1 mm above the stop face");
            Assert.AreEqual(0, pushes, $"a held player was moved more than 1 mm in one frame {pushes} time(s) (worst {worstPush * 1000f:F2} mm): the stop pushed them");
            Assert.IsTrue(hatch.CanInteract(interactor), "opening is allowed from the stop");
            hatch.Interact(interactor);
            Assert.AreEqual(HingeState.Opening, hatch.State, "E started the open swing");
            Assert.IsFalse(stop.enabled, "the stop is OFF while the hatch is Opening");
            yield return Wait(hatch.Tuning.hatchSeconds + 0.5f);
            Assert.AreEqual(HingeState.Open, hatch.State, "the hatch opened");
            Assert.IsFalse(stop.enabled, "the stop is OFF while the hatch is Open");
            Assert.LessOrEqual(Vector3.Distance(rest, player.transform.position), Millimetre, "the player did not move while the hatch opened from the stop");
        }

        /// <summary>
        /// td part 80 (3), E3: a PREDICATE sweep. Hatch Open, the capsule switched off, the player's transform placed on the flight line with the skin-grown capsule top at the band edge
        /// +-20 mm in 1 mm steps. CanInteract must equal !(gap > 0) wherever |gap| > 0.5 mm; every row is logged; the capsule and the position are restored afterwards.
        /// </summary>
        [UnityTest]
        public IEnumerator E3_ThePredicateSweep_CanInteractFollowsTheGap_ThroughTheBandEdge()
        {
            yield return LoadTower();
            var stop = HeadroomStop();
            TopFlight(out var foot, out var head);
            hatch.SetState(HingeState.Open, 0f);
            yield return null;
            yield return PutFeetAtY(foot.y + 0.05f, foot, head);   // slim capsule in force, player found
            float skin = capsule.skinWidth, capH = capsule.height, stopLower = StopLowerY(stop);
            Vector3 dirH = Flat(head - foot).normalized;
            float tan = (head.y - foot.y) / Flat(head - foot).magnitude;
            var home = player.transform.position;
            var rows = new System.Text.StringBuilder();
            int asserted = 0, boundary = 0;
            var rot = player.transform.rotation;
            var parked = TestSpawn.Park(player);
            try
            {
                for (int mm = -20; mm <= 20; mm++)
                {
                    float gap = mm * 0.001f;
                    float feetY = stopLower - capH - skin + gap;   // capsule top + skin - stop face == gap
                    float along = (feetY - foot.y) / tan;
                    parked.Move(foot + dirH * along + Vector3.up * (feetY - foot.y), rot);
                    bool can = hatch.CanInteract(interactor);
                    float realGap = player.transform.position.y + capH + skin - stopLower;
                    string note = Mathf.Abs(realGap) <= 0.0005f ? " BOUNDARY (not asserted)" : "";
                    if (note != "") boundary++;
                    rows.AppendLine($"  gap {realGap * 1000f:F2} mm feetY {player.transform.position.y:F4} canInteract {can}{note}");
                    if (Mathf.Abs(realGap) > 0.0005f)
                    {
                        asserted++;
                        Assert.AreEqual(!(realGap > 0f), can, $"gap {realGap * 1000f:F2} mm (feet y {player.transform.position.y:F4}): CanInteract must be {!(realGap > 0f)}");
                    }
                }
            }
            finally
            {
                parked.Move(home, rot);
                parked.Release();
            }
            Debug.Log($"LookoutHatchWalk E3: {asserted} rows asserted, {boundary} boundary; capsule h {capH:F3} skin {skin:F3}, stop face {stopLower:F4}; every row:\n{rows}");
            Assert.Greater(asserted, 30, "the sweep asserted the rows on both sides of the edge");
        }
    }
}
