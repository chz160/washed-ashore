using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;
using WashedAshore.Lookout.Editor;
using WashedAshore.Tests.PlayMode.Lookout;

namespace WashedAshore.Tests.EditMode.Lookout
{
    /// <summary>The join envelope at every free lane end, checked on the built colliders, and the run-out points against the manifest.</summary>
    public partial class LookoutBuiltTowerTests
    {
        const float EnvelopeLateralStep = 0.01f;   // sideways spacing of the sweep, m (a script constant)

        /// <summary>
        /// tower-td-note, lane-end ruling: at every free end and on each side, (1) a body at the manifest's wall_reach
        /// touches nothing anywhere inside the join envelope, every 2 cm along the stretch and every 1 cm across it;
        /// (2) the envelope at the free end covers the manifest's reachable span, unless the mouth's cap or the glide
        /// slope is what binds there (named in the log, not a failure); (3) where the model's reach binds, the
        /// steepest narrowing and the wall the manifest names are written out for the director's capture list.
        /// The envelope is computed by ClimbRules from the lanes as wired; the spans and wall names are the manifest's.
        /// </summary>
        [Test]
        public void JoinEnvelopes_AreClearAtWallReach_AndCoverTheReachableSpan([Values(false, true)] bool hatchOpen)
        {
            var t = area.Tuning;
            bool leafWas = hatch.LeafCollider.enabled;
            Quaternion leafPose = hatch.transform.localRotation;
            if (hatchOpen) hatch.SetState(HingeState.Open, 0f);
            Physics.SyncTransforms();
            var problems = new List<string>();
            var capped = new List<string>();
            var rows = new List<string>();
            int ends = 0;
            try
            {
                for (int i = 0; i < area.LaneCount; i++)
                {
                    StairLane lane = area.Lane(i);
                    bool level = ClimbRules.IsLevel(lane);
                    bool footLinked = area.TryLink(i, ClimbRules.End.Foot, out _, out _), headLinked = area.TryLink(i, ClimbRules.End.Head, out _, out _);
                    float footRun = footLinked ? -1f : ClimbRules.FootRun(lane, t), headRun = headLinked ? -1f : ClimbRules.HeadRun(lane, t);
                    foreach (bool head in new[] { false, true })
                    {
                        if (head ? headLinked : footLinked) continue;
                        float stretch = level ? lane.Length : (head ? headRun : footRun);
                        if (stretch <= 0f) continue;
                        // The lane parameter s metres in from the free end.
                        System.Func<float, float> at = s => level ? (head ? lane.Length - s : s) : (head ? lane.Length + stretch - s : s - stretch);
                        string end = $"lane {i + 1} {(head ? "head" : "foot")}";
                        var mouth = MouthAt(lane.Point(at(0f), 0f));
                        Assert.IsNotNull(mouth, $"{end}: the manifest has no join mouth at this free end");
                        Assert.Greater(mouth.wall_reach, 0f, $"{end}: manifest wall_reach");
                        ends++;
                        float open = Mathf.Min(ClimbRules.MouthWidth(lane, t) * 0.5f, Mathf.Tan(t.laneGlideMaxDegrees * Mathf.Deg2Rad) * stretch);
                        foreach (float sign in new[] { -1f, 1f })
                        {
                            // The manifest's sides are seen from the free end looking toward the stair: mirrored at a free head.
                            bool manifestLeft = (sign < 0f) != head;
                            var side = manifestLeft ? mouth.left : mouth.right;
                            string name = $"{end} {(manifestLeft ? "left" : "right")}";
                            float smallest = float.PositiveInfinity, steepest = 0f, steepestAt = 0f;
                            float last = ClimbRules.LateralBand(lane, at(0f), footRun, headRun, t, sign);
                            float atFreeEnd = last;
                            for (float s = 0f; s <= stretch + RunTolerance; s += RunSweepStep)
                            {
                                float u = at(Mathf.Min(s, stretch));
                                float allowed = ClimbRules.LateralBand(lane, u, footRun, headRun, t, sign);
                                if (allowed > last + ClimbRules.BandEpsilon) problems.Add($"{name}: the envelope widens from {last:F4} to {allowed:F4} at {s:F2} m from the free end");
                                // Narrowing faster than the glide slope can only be the model's reach (a wall); record the steepest.
                                float narrowing = s > 0f ? (last - allowed) / RunSweepStep : 0f;
                                if (narrowing > steepest) { steepest = narrowing; steepestAt = s; }
                                last = allowed;
                                for (float lateral = 0f; lateral <= allowed + RunTolerance; lateral += EnvelopeLateralStep)
                                    foreach (var hit in Touching(lane, u, sign * Mathf.Min(lateral, allowed), mouth.wall_reach - LookoutTargets.FloatTolerance))
                                        problems.Add($"{name}: {hit.name} is within wall_reach {mouth.wall_reach} m of a body {Mathf.Min(lateral, allowed):F3} m off the line, {s:F2} m from the free end");
                                // Clearance at the envelope's edge: the largest body that touches nothing there.
                                float lo = 0f, hi = ClearanceSearch;
                                for (int b = 0; b < OverlapBisections; b++)
                                {
                                    float mid = (lo + hi) * 0.5f;
                                    if (Touching(lane, u, sign * allowed, mid).Count > 0) hi = mid; else lo = mid;
                                }
                                smallest = Mathf.Min(smallest, lo);
                            }
                            bool covers = side.reachable_span >= 0f && atFreeEnd >= side.reachable_span - LookoutTargets.MarginAgreement;
                            bool openBinds = Mathf.Abs(atFreeEnd - open) <= ClimbRules.BandEpsilon;
                            if (side.reachable_span < 0f) problems.Add($"{name}: the manifest gives no reachable_span");
                            else if (!covers && openBinds) capped.Add($"{name}: {atFreeEnd:F3} of a {side.reachable_span:F3} m span (the cap or the glide slope binds)");
                            else if (!covers) problems.Add($"{name}: the envelope is {atFreeEnd:F3} m at the free end, the reachable span is {side.reachable_span:F3} m, and neither the cap nor the slope ({open:F3} m) explains it");
                            float glide = Mathf.Tan(t.laneGlideMaxDegrees * Mathf.Deg2Rad);
                            rows.Add($"{{\"end\":\"{name}\",\"stretch\":{stretch:F4},\"atFreeEnd\":{atFreeEnd:F4},\"capOrSlope\":{open:F4},\"reachableSpan\":{side.reachable_span:F4}," +
                                     $"\"binds\":\"{(openBinds ? "cap or slope" : "reach")}\",\"covers\":{(covers ? "true" : "false")},\"smallestClearance\":{smallest:F4},\"wallReach\":{mouth.wall_reach:F4}," +
                                     $"\"steepestNarrowing\":{steepest:F3},\"steepestAt\":{steepestAt:F2},\"overGlideSlope\":{(steepest > glide + 1e-3f ? "true" : "false")},\"manifestBoundBy\":\"{side.bound_by}\"}}");
                        }
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
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, hatchOpen ? "join-envelopes-hatch-open.json" : "join-envelopes-hatch-closed.json"),
                $"{{\"hatch\":\"{(hatchOpen ? "open" : "closed")}\",\"glideMaxDegrees\":{t.laneGlideMaxDegrees},\"sweepAlong\":{RunSweepStep},\"sweepAcross\":{EnvelopeLateralStep}," +
                "\"note\":\"sides are the manifest's: standing at the free end looking toward the stair. steepestNarrowing is metres sideways per metre walked, sampled at the sweep spacing; over the glide slope it is a wall binding (manifestBoundBy is the manifest's name for it, not measured here).\",\"rows\":[\n" + string.Join(",\n", rows) + "]}");
            Debug.Log($"LookoutBuiltTowerTests: join envelopes (hatch {(hatchOpen ? "open" : "closed")}), {ends} free ends; not covering the whole reachable span because the cap or the slope binds: [{string.Join("; ", capped)}]");
            Assert.Greater(ends, 0, "no free lane ends");
            Assert.IsEmpty(problems, "join envelopes:\n" + string.Join("\n", problems));
        }

        /// <summary>The manifest's join mouth for the free end at a world position (by position, within the link tolerance), or null.</summary>
        LookoutManifest.Mouth MouthAt(Vector3 worldPoint)
        {
            Vector3 p = tower.InverseTransformPoint(worldPoint);
            foreach (var flight in manifest.flights)
                foreach (bool head in new[] { false, true })
                {
                    var over = head ? flight.head_hand_over : flight.foot_hand_over;
                    var runOut = head ? flight.head_run_out : flight.foot_run_out;
                    bool here = runOut != null && runOut.Present
                        ? Vector3.Distance(p, Require(runOut.empties[runOut.empties.Length - 1])) <= LookoutTargets.MarginAgreement
                        : over != null && over.point != null && over.point.Length == 3 && Vector3.Distance(p, LookoutManifest.V(over.point, "hand_over.point")) <= LookoutTargets.MarginAgreement;
                    if (here) return head ? flight.head_mouth : flight.foot_mouth;
                }
            if (manifest.exit_lanes != null)
                foreach (var exit in manifest.exit_lanes)
                    if (Vector3.Distance(p, LookoutManifest.V(exit.head, "exit_lanes.head")) <= LookoutTargets.MarginAgreement) return exit.head_mouth;
            return null;
        }

        /// <summary>
        /// tower-td-note §6 condition 8 and the lane-end ruling: positions come from the FBX empties, and each run-out
        /// empty is within 1 mm of the manifest's point for it, so an FBX and a manifest from different builds can't
        /// pass together.
        /// </summary>
        [Test]
        public void RunOutEmpties_AreWhereTheManifestSaysWithinAMillimetre()
        {
            var problems = new List<string>();
            int checkedPoints = 0;
            for (int f = 0; f < manifest.flights.Length; f++)
                foreach (bool head in new[] { false, true })
                {
                    var runOut = head ? manifest.flights[f].head_run_out : manifest.flights[f].foot_run_out;
                    if (runOut == null || !runOut.Present) continue;
                    string end = $"flight {f + 1} {(head ? "head" : "foot")} run-out";
                    if (runOut.points_flat == null || runOut.points_flat.Length != runOut.empties.Length * 3)
                    {
                        problems.Add($"{end}: points_flat has {(runOut.points_flat == null ? 0 : runOut.points_flat.Length)} numbers for {runOut.empties.Length} empties");
                        continue;
                    }
                    for (int k = 0; k < runOut.empties.Length; k++)
                    {
                        var stated = new Vector3(runOut.points_flat[k * 3], runOut.points_flat[k * 3 + 1], runOut.points_flat[k * 3 + 2]);
                        float off = Vector3.Distance(Require(runOut.empties[k]), stated);
                        checkedPoints++;
                        if (off > LookoutTargets.MarginAgreement) problems.Add($"{runOut.empties[k]}: {off * 1000f:F2} mm from the manifest's point {stated:F4}");
                    }
                }
            Assert.Greater(checkedPoints, 0, "the manifest lists no run-out points");
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }
    }
}
