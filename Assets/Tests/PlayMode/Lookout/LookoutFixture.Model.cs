using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// The detector's lanes, built from the build manifest alone (agreed with t-qa), and the first assertion of every
    /// lookout fixture: the live lanes are those lanes. Positions within 1 mm, directions within 0.1 degrees, the same
    /// links, the same stretch lengths, and the same reach lists on the same sides.
    /// </summary>
    public abstract partial class LookoutFixture
    {
        const float ModelSideAgreement = 0.99f;   // the manifest's left_dir lies along a lane's own right or left at least this closely (a dot product)

        static Vector3 V(float[] xyz, string what)
        {
            Assert.IsTrue(xyz != null && xyz.Length == 3, $"manifest {what} is not a 3-vector");
            return new Vector3(xyz[0], xyz[1], xyz[2]);
        }

        static bool Has(ManifestRunOut runOut) => runOut != null && runOut.empties != null && runOut.empties.Length > 0;

        /// <summary>Lanes in the order the build wires them: flights bottom up, exit lanes, then each flight's head and foot run-out segments.</summary>
        protected LookoutWatch.ModelLane[] BuildModel(Manifest built)
        {
            var lanes = new List<LookoutWatch.ModelLane>();
            foreach (var flight in built.flights)
            {
                Assert.Greater(flight.width, 0f, $"manifest flight {flight.index} width");
                var lane = new LookoutWatch.ModelLane { foot = tower.TransformPoint(V(flight.foot, "flight foot")), head = tower.TransformPoint(V(flight.head, "flight head")), width = flight.width };
                if (!Has(flight.foot_run_out)) lane.footRun = SetReach(lane, false, flight.foot_mouth, $"flight {flight.index} foot");
                if (!Has(flight.head_run_out)) lane.headRun = SetReach(lane, true, flight.head_mouth, $"flight {flight.index} head");
                lanes.Add(lane);
            }
            if (built.exit_lanes != null)
                foreach (var exit in built.exit_lanes)
                {
                    var lane = new LookoutWatch.ModelLane { foot = tower.TransformPoint(V(exit.foot, "exit lane foot")), head = tower.TransformPoint(V(exit.head, "exit lane head")), width = exit.width };
                    float stretch = SetReach(lane, true, exit.head_mouth, "exit lane");
                    Assert.AreEqual(lane.Length, stretch, LookoutTargets.MarginAgreement, "the exit lane's mouth runs its whole length");
                    lanes.Add(lane);
                }
            for (int f = 0; f < built.flights.Length; f++)
            {
                AddRunOut(lanes, lanes[f].head, built.flights[f].head_run_out, built.flights[f].head_mouth, built.flights[f].width, true, $"flight {f + 1} head run-out");
                AddRunOut(lanes, lanes[f].foot, built.flights[f].foot_run_out, built.flights[f].foot_mouth, built.flights[f].width, false, $"flight {f + 1} foot run-out");
            }
            // Links: two lane ends within a millimetre of each other.
            for (int a = 0; a < lanes.Count; a++)
                for (int b = 0; b < lanes.Count; b++)
                {
                    if (a == b) continue;
                    if (Near(lanes[a].foot, lanes[b].foot) || Near(lanes[a].foot, lanes[b].head)) lanes[a].footLink = b;
                    if (Near(lanes[a].head, lanes[b].foot) || Near(lanes[a].head, lanes[b].head)) lanes[a].headLink = b;
                }
            foreach (var lane in lanes)
            {
                if (lane.footLink >= 0) lane.footRun = 0f;
                if (lane.headLink >= 0) lane.headRun = 0f;
            }
            return lanes.ToArray();
        }

        static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= LookoutTargets.MarginAgreement;

        void AddRunOut(List<LookoutWatch.ModelLane> lanes, Vector3 laneEnd, ManifestRunOut runOut, ManifestMouth mouth, float width, bool head, string what)
        {
            if (!Has(runOut)) return;
            Assert.IsTrue(runOut.points_flat != null && runOut.points_flat.Length == runOut.empties.Length * 3, $"manifest {what}: points_flat does not have one point per empty");
            Vector3 inner = laneEnd;
            for (int k = 0; k < runOut.empties.Length; k++)
            {
                Vector3 outer = tower.TransformPoint(new Vector3(runOut.points_flat[k * 3], runOut.points_flat[k * 3 + 1], runOut.points_flat[k * 3 + 2]));
                // Like the flights, every segment runs "up": away from a head, in toward a foot.
                var lane = head ? new LookoutWatch.ModelLane { foot = inner, head = outer, width = width } : new LookoutWatch.ModelLane { foot = outer, head = inner, width = width };
                if (k == runOut.empties.Length - 1)
                    Assert.AreEqual(lane.Length, SetReach(lane, head, mouth, what), LookoutTargets.MarginAgreement, $"{what}: the mouth is the whole final segment");
                lanes.Add(lane);
                inner = outer;
            }
        }

        /// <summary>
        /// Puts a free end's reach lists on a model lane by the sign of the offset, using the manifest's left_dir and
        /// nothing else to tell the sides apart, each list made never-increasing from the free end. Returns the
        /// manifest's stretch length.
        /// </summary>
        float SetReach(LookoutWatch.ModelLane lane, bool head, ManifestMouth mouth, string what)
        {
            Assert.IsTrue(mouth != null && mouth.left != null && mouth.right != null && mouth.left.reach != null && mouth.right.reach != null
                          && mouth.left.reach.Length > 0 && mouth.right.reach.Length > 0, $"manifest {what}: no join mouth with left.reach and right.reach");
            Assert.Greater(mouth.reach_spacing, 0f, $"manifest {what}: reach_spacing");
            Assert.Greater(mouth.stretch_length, 0f, $"manifest {what}: stretch_length");
            float along = Vector3.Dot(tower.TransformDirection(V(mouth.left_dir, $"{what} left_dir")).normalized, lane.Right);
            Assert.GreaterOrEqual(Mathf.Abs(along), ModelSideAgreement, $"manifest {what}: left_dir is not across the lane");
            float[] pos = RunningMinimum(along > 0f ? mouth.left.reach : mouth.right.reach), neg = RunningMinimum(along > 0f ? mouth.right.reach : mouth.left.reach);
            lane.spacing = mouth.reach_spacing;
            if (head) { lane.headPos = pos; lane.headNeg = neg; } else { lane.footPos = pos; lane.footNeg = neg; }
            return mouth.stretch_length;
        }

        static float[] RunningMinimum(float[] samples)
        {
            var result = (float[])samples.Clone();
            for (int i = 1; i < result.Length; i++) result[i] = Mathf.Min(result[i], result[i - 1]);
            return result;
        }

        /// <summary>The first assertion of every fixture: the lanes the game is running are the manifest's.</summary>
        protected void AssertLiveLanesAreTheManifests(LookoutWatch.ModelLane[] model)
        {
            Assert.AreEqual(model.Length, area.LaneCount, "live lane count against the manifest (flights + exit lanes + run-out segments)");
            for (int i = 0; i < model.Length; i++)
            {
                StairLane live = area.Lane(i);
                var lane = model[i];
                string name = $"lane {i + 1}";
                Assert.LessOrEqual(Vector3.Distance(live.foot, lane.foot), LookoutTargets.MarginAgreement, $"{name} foot: live {live.foot:F4}, manifest {lane.foot:F4}");
                Assert.LessOrEqual(Vector3.Distance(live.head, lane.head), LookoutTargets.MarginAgreement, $"{name} head: live {live.head:F4}, manifest {lane.head:F4}");
                Assert.LessOrEqual(Vector3.Angle(live.head - live.foot, lane.head - lane.foot), LookoutTargets.TurnAgreementDegrees, $"{name} direction");
                Assert.AreEqual(lane.width, live.width, 1e-4f, $"{name} width");
                Assert.AreEqual(lane.Level, ClimbRules.IsLevel(live), $"{name}: level in one and inclined in the other");
                Assert.AreEqual(lane.footLink, area.TryLink(i, ClimbRules.End.Foot, out int footLink, out _) ? footLink : -1, $"{name} foot link");
                Assert.AreEqual(lane.headLink, area.TryLink(i, ClimbRules.End.Head, out int headLink, out _) ? headLink : -1, $"{name} head link");
                if (!lane.Level && lane.footLink < 0) Assert.AreEqual(lane.footRun, ClimbRules.FootRun(live, climb), LookoutTargets.MarginAgreement, $"{name} foot run");
                if (!lane.Level && lane.headLink < 0) Assert.AreEqual(lane.headRun, ClimbRules.HeadRun(live, climb), LookoutTargets.MarginAgreement, $"{name} head run");
                // A live lane's left is the negative side of the offset.
                SameReach(lane.footNeg, live.footReachLeft, $"{name} foot reach, left");
                SameReach(lane.footPos, live.footReachRight, $"{name} foot reach, right");
                SameReach(lane.headNeg, live.headReachLeft, $"{name} head reach, left");
                SameReach(lane.headPos, live.headReachRight, $"{name} head reach, right");
                if (lane.spacing > 0f) Assert.AreEqual(lane.spacing, live.reachSpacing, 1e-6f, $"{name} reach spacing");
            }
        }

        static void SameReach(float[] manifest, float[] live, string what)
        {
            int expected = manifest == null ? 0 : manifest.Length, got = live == null ? 0 : live.Length;
            Assert.AreEqual(expected, got, $"{what}: sample count");
            for (int i = 0; i < expected; i++) Assert.AreEqual(manifest[i], live[i], 1e-5f, $"{what}: sample {i}");
        }
    }
}
