using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;
using WashedAshore.Lookout.Editor;
using WashedAshore.Tests.PlayMode.Lookout;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode.Lookout
{
    /// <summary>The margin report at every hand-over point (body clearance measured on the built colliders; drop distance from the manifest), and the check that ties the manifest's open stretches to the built walls.</summary>
    public partial class LookoutBuiltTowerTests
    {
        /// <summary>
        /// The margin at every hand-over point, measured on the built colliders (t-td / t-qa): the largest capsule radius
        /// that touches nothing there, less the 0.30 m bar. Three levels, all from the targets file: fail under the bar
        /// less the float tolerance; warn under the bar + MarginWarn; list as under target below the bar + MarginTarget.
        /// Where the manifest states a margin for that end, the two must agree within MarginAgreement.
        /// </summary>
        [Test]
        public void HandOverPoints_ReportTheirMargins([Values(false, true)] bool hatchOpen)
        {
            var t = area.Tuning;
            bool leafWas = hatch.LeafCollider.enabled;
            Quaternion leafPose = hatch.transform.localRotation;
            if (hatchOpen) hatch.SetState(HingeState.Open, 0f);
            Physics.SyncTransforms();
            var rows = new List<string>();
            var failed = new List<string>();
            var underTarget = new List<string>();
            var disagree = new List<string>();
            try
            {
                for (int i = 0; i < area.LaneCount; i++)
                {
                    StairLane lane = area.Lane(i);
                    foreach (bool head in new[] { false, true })
                    {
                        if (area.TryLink(i, head ? ClimbRules.End.Head : ClimbRules.End.Foot, out _, out _)) continue;
                        float run = head ? ClimbRules.HeadRun(lane, t) : ClimbRules.FootRun(lane, t);
                        float u = head ? lane.Length + run : -run;
                        // The largest capsule that touches nothing at the hand-over point, up to ClearanceSearch.
                        float lo = 0f, hi = ClearanceSearch;
                        string nearest = null;
                        for (int b = 0; b < OverlapBisections; b++)
                        {
                            float mid = (lo + hi) * 0.5f;
                            var hits = Touching(lane, u, 0f, mid);
                            if (hits.Count > 0) { hi = mid; nearest = hits[0].name; } else lo = mid;
                        }
                        float margin = lo - LookoutTargets.ClearanceBar;
                        string end = $"lane {i + 1} {(head ? "head" : "foot")}";
                        Claim(lane.Point(u, 0f), out float claimed, out float claimedDropMargin);
                        string claimedText = float.IsNaN(claimed) ? "null" : claimed.ToString("F4");
                        // Second column: distance to the nearest drop, arithmetic on the manifest (not measured on the build):
                        // a landing's open stretches, or in the cab the hatch opening, which is a drop only while the hatch is open.
                        float drop = DropDistance(lane.Point(u, 0f), hatchOpen, out string dropKind);
                        float dropMargin = drop - LookoutTargets.ClearanceBar;
                        // No drop anywhere near (the ground end) is reported as "none": it is not a pass.
                        string dropText = float.IsNaN(drop) ? "\"none\"" : drop.ToString("F4"), dropMarginText = float.IsNaN(drop) ? "\"none\"" : dropMargin.ToString("F4");
                        string claimedDropText = float.IsNaN(claimedDropMargin) ? "null" : claimedDropMargin.ToString("F4");
                        rows.Add($"{{\"end\":\"{end}\",\"clearance\":{lo:F4},\"margin\":{margin:F4},\"nearest\":\"{nearest}\",\"manifestMargin\":{claimedText}," +
                                 $"\"dropFromManifest\":{dropText},\"dropMargin\":{dropMarginText},\"dropIs\":\"{dropKind}\",\"manifestDropMargin\":{claimedDropText}}}");
                        if (!float.IsNaN(drop) && !float.IsNaN(claimedDropMargin) && Mathf.Abs(claimedDropMargin - dropMargin) > LookoutTargets.MarginAgreement)
                            disagree.Add($"{end}: drop margin +{dropMargin * 1000f:F1} mm by this test's arithmetic ({dropKind}), the manifest says +{claimedDropMargin * 1000f:F1} mm");
                        if (!float.IsNaN(drop))
                        {
                            if (dropMargin < -LookoutTargets.FloatTolerance) failed.Add($"{end}: {drop:F4} m from a drop (manifest arithmetic), {-dropMargin * 1000f:F1} mm short of {LookoutTargets.ClearanceBar} m");
                            else if (dropMargin < LookoutTargets.MarginWarn) Debug.LogWarning($"LookoutBuiltTowerTests: {end} is only {dropMargin * 1000f:F1} mm over the bar from a drop (manifest arithmetic)");
                            if (dropMargin >= -LookoutTargets.FloatTolerance && dropMargin < LookoutTargets.MarginTarget) underTarget.Add($"{end} drop +{dropMargin * 1000f:F1} mm");
                        }
                        bool passes = margin >= -LookoutTargets.FloatTolerance;
                        if (!passes) failed.Add($"{end}: {lo:F4} m to {nearest}, {-margin * 1000f:F1} mm short of {LookoutTargets.ClearanceBar} m");
                        else if (margin < LookoutTargets.MarginWarn)
                            Debug.LogWarning($"LookoutBuiltTowerTests: {end} passes with only {margin * 1000f:F1} mm of margin (nearest {nearest}), hatch {(hatchOpen ? "open" : "closed")}");
                        if (passes && margin < LookoutTargets.MarginTarget) underTarget.Add($"{end} +{margin * 1000f:F1} mm ({nearest})");
                        // Compare with the build's own figure, unless the measurement ran into the search limit.
                        bool measured = lo < ClearanceSearch - LookoutTargets.MarginAgreement;
                        if (measured && !float.IsNaN(claimed) && Mathf.Abs(claimed - margin) > LookoutTargets.MarginAgreement)
                            disagree.Add($"{end}: measured +{margin * 1000f:F1} mm, the manifest says +{claimed * 1000f:F1} mm");
                    }
                }
            }
            finally
            {
                hatch.SetState(HingeState.Closed, 0f);
                hatch.transform.localRotation = leafPose;
                hatch.LeafCollider.enabled = leafWas;
                Physics.SyncTransforms();
            }
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "TestResults", "tower-eng"));
            System.IO.Directory.CreateDirectory(dir);
            string file = hatchOpen ? "c1g-margins-hatch-open.json" : "c1g-margins-hatch-closed.json";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, file),
                $"{{\"bar\":{LookoutTargets.ClearanceBar},\"floatTolerance\":{LookoutTargets.FloatTolerance},\"warnUnder\":{LookoutTargets.MarginWarn},\"target\":{LookoutTargets.MarginTarget}," +
                $"\"hatch\":\"{(hatchOpen ? "open" : "closed")}\",\"note\":\"dropFromManifest is arithmetic on the manifest, not a measurement. In the cab the drop is the hatch opening and applies with the hatch open only; with it closed the cab row reads none. none = no drop exists there, not a pass.\",\"rows\":[\n" + string.Join(",\n", rows) + "]}");
            Debug.Log($"LookoutBuiltTowerTests: hand-over margins (hatch {(hatchOpen ? "open" : "closed")}), under the {LookoutTargets.MarginTarget * 1000f:F0} mm target: [{string.Join("; ", underTarget)}]");
            Assert.IsEmpty(failed, "hand-over points short of the bar:\n" + string.Join("\n", failed));
            Assert.IsEmpty(disagree, "margins measured on the built colliders differ from the manifest by more than 1 mm:\n" + string.Join("\n", disagree));
        }

        /// <summary>
        /// Distance from a hand-over point to the nearest drop: ARITHMETIC ON THE MANIFEST, not a measurement of the
        /// build. On a landing: the point to the nearest open stretch of that landing's edge, in plan
        /// (<see cref="OpenStretches_AreGapsBetweenTheBuiltLandingWalls_FlightSideWallsNotCounted"/> ties those back to
        /// the built colliders). On the cab floor with the hatch open: the point to the hatch opening's rectangle, from
        /// its corners. NaN where there is no drop (the ground; the cab with the hatch closed).
        /// </summary>
        float DropDistance(Vector3 worldPoint, bool hatchOpen, out string kind)
        {
            Vector3 p = tower.InverseTransformPoint(worldPoint);
            float best = float.NaN;
            kind = "none";
            if (hatchOpen && Mathf.Abs(p.y - manifest.targets.cab_floor_y) <= playerCapsule.stepOffset)
            {
                Vector3 a = LookoutManifest.V(manifest.hatch.opening_min, "hatch.opening_min"), b = LookoutManifest.V(manifest.hatch.opening_max, "hatch.opening_max");
                Vector3 min = Vector3.Min(a, b), max = Vector3.Max(a, b);
                float dx = Mathf.Max(min.x - p.x, 0f, p.x - max.x), dz = Mathf.Max(min.z - p.z, 0f, p.z - max.z);
                kind = "hatch opening (hatch open)";
                return Mathf.Sqrt(dx * dx + dz * dz);
            }
            if (manifest.landings == null) return best;
            foreach (var landing in manifest.landings)
            {
                if (Mathf.Abs(landing.y - p.y) > playerCapsule.stepOffset || landing.open_stretches == null) continue;
                foreach (var stretch in landing.open_stretches)
                {
                    Vector3 a = LookoutManifest.V(stretch.from, "open_stretches.from"), b = LookoutManifest.V(stretch.to, "open_stretches.to");
                    Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z), p2 = new Vector2(p.x, p.z);
                    float t = Mathf.Clamp01(Vector2.Dot(p2 - a2, b2 - a2) / Mathf.Max((b2 - a2).sqrMagnitude, 1e-9f));
                    float d = Vector2.Distance(p2, a2 + (b2 - a2) * t);
                    if (float.IsNaN(best) || d < best) { best = d; kind = "landing open stretch"; }
                }
            }
            return best;
        }

        /// <summary>
        /// Every open stretch in the manifest (a length of landing edge with no rail, where a flight stands) is a gap
        /// between the built landing walls, to about a millimetre: no landing wall reaches into it, and a landing wall
        /// or the deck's corner begins at each end. This is what lets the drop column be computed from the manifest.
        /// Only the landings' own walls (clip boxes named for a landing) are counted: a flight's side walls stand in or
        /// beside a stair mouth by design and are not part of this check.
        /// </summary>
        [Test]
        public void OpenStretches_AreGapsBetweenTheBuiltLandingWalls_FlightSideWallsNotCounted()
        {
            Assert.IsNotNull(manifest.landings, "manifest landings");
            int wallMask = 1 << WorldLayers.WorldBounds;
            var problems = new List<string>();
            int stretches = 0;
            foreach (var landing in manifest.landings)
            {
                if (landing.open_stretches == null) continue;
                foreach (var stretch in landing.open_stretches)
                {
                    stretches++;
                    Vector3 a = tower.TransformPoint(LookoutManifest.V(stretch.from, "open_stretches.from")) + Vector3.up * WallProbeHeight;
                    Vector3 b = tower.TransformPoint(LookoutManifest.V(stretch.to, "open_stretches.to")) + Vector3.up * WallProbeHeight;
                    Vector3 along = (b - a).normalized;
                    float length = Vector3.Distance(a, b);
                    // Inside the stretch, from 1 mm in at each end: no landing wall.
                    for (float s = StretchInset; s <= length - StretchInset; s += RunSweepStep)
                        if (LandingWallAt(a + along * s, wallMask, out string wall))
                            problems.Add($"landing {landing.index}: {wall} stands {s:F3} m into an open stretch {length:F3} m long");
                    // Just outside each end: a landing wall begins (the stretch is no longer than the gap).
                    if (!LandingWallAt(a - along * StretchInset, wallMask, out _)) problems.Add($"landing {landing.index}: no landing wall within a millimetre before the open stretch at {stretch.from[0]:F3}, {stretch.from[2]:F3}");
                    if (!LandingWallAt(b + along * StretchInset, wallMask, out _)) problems.Add($"landing {landing.index}: no landing wall within a millimetre after the open stretch at {stretch.to[0]:F3}, {stretch.to[2]:F3}");
                }
            }
            Assert.Greater(stretches, 0, "the manifest lists no open stretches");
            Assert.IsEmpty(problems, "the manifest's open stretches are not the gaps in the built walls:\n" + string.Join("\n", problems));
        }

        /// <summary>A landing's invisible wall within half a millimetre of a point.</summary>
        bool LandingWallAt(Vector3 point, int mask, out string name)
        {
            name = null;
            foreach (var hit in Physics.OverlapSphere(point, StretchInset * 0.5f, mask, QueryTriggerInteraction.Ignore))
                if (hit.transform.IsChildOf(tower) && hit.name.StartsWith(LookoutTargets.ClipPrefix) && hit.name.Contains("Landing")) { name = hit.name; return true; }
            return false;
        }

        const float StretchInset = 0.001f;      // td: open stretches coincide with the gaps in the built walls within 1 mm
        const float WallProbeHeight = 1f;       // walls are probed this far above the deck, m (they stand 2.2 m high)

        /// <summary>
        /// The margins over the bar the build manifest states for the hand-over point at a world position (wall, and
        /// drop), found by position within the link tolerance: a built-in run's hand-over record, a run-out's last
        /// point, an exit lane's free end. NaN where it states none.
        /// </summary>
        void Claim(Vector3 worldPoint, out float wall, out float drop)
        {
            wall = drop = float.NaN;
            Vector3 p = tower.InverseTransformPoint(worldPoint);
            foreach (var flight in manifest.flights)
            {
                foreach (var over in new[] { flight.foot_hand_over, flight.head_hand_over })
                    if (over != null && over.point != null && over.point.Length == 3 && Vector3.Distance(p, LookoutManifest.V(over.point, "hand_over.point")) <= LookoutTargets.MarginAgreement)
                    { wall = Given(over.point_wall_margin); drop = Given(over.point_mouth_margin); return; }
                foreach (var runOut in new[] { flight.foot_run_out, flight.head_run_out })
                    if (runOut != null && runOut.Present && Vector3.Distance(p, Require(runOut.empties[runOut.empties.Length - 1])) <= LookoutTargets.MarginAgreement)
                    { wall = Given(runOut.point_wall_margin); drop = Given(runOut.point_mouth_margin); return; }
            }
            if (manifest.exit_lanes == null) return;
            foreach (var exit in manifest.exit_lanes)
                if (exit.free_end_clear != null && Vector3.Distance(p, LookoutManifest.V(exit.head, "exit_lanes.head")) <= LookoutTargets.MarginAgreement)
                {
                    wall = exit.free_end_clear.margin_over_reach >= 0f ? exit.free_end_clear.margin_over_reach : float.NaN;
                    drop = exit.free_end_clear.open_hatch > 0f ? exit.free_end_clear.open_hatch - LookoutTargets.ClearanceBar : float.NaN;
                    return;
                }
        }

        static float Given(float value) => value <= LookoutManifest.NotGiven + 1f ? float.NaN : value;

        const float ClearanceSearch = 0.45f;   // clearances above this are reported as this, m (a script constant)
    }
}
