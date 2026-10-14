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
    /// The shifted-lane twin (plan C2, agreed with t-qa and t-td): every live lane is moved TwinLaneShift across the
    /// flights through the builder's own entry (ClimbArea.Configure), with the invisible walls off. The detector's
    /// lanes come from the manifest and don't move, so it has to report a lane-lateral escape on every flight and on
    /// the level stretches; a detector that stays silent fails the suite.
    /// </summary>
    public class LookoutTwinTests : LookoutFixture
    {
        [UnityTest]
        public IEnumerator ShiftedLanes_TheDetectorReportsEveryFlight_AndTheLevelStretches()
        {
            yield return LoadTower();
            yield return StartAtBase();

            // One shift for every lane, so the links survive: diagonal to the first flight, which puts the whole
            // TwinLaneShift across every flight (they run along or square to the first one).
            StairLane first = area.Lane(0);
            Vector3 shift = (first.Right + first.AxisHorizontal) * LookoutTargets.TwinLaneShift;
            var shifted = new StairLane[area.LaneCount];
            for (int i = 0; i < shifted.Length; i++)
            {
                StairLane lane = area.Lane(i);
                shifted[i] = new StairLane(area.transform.InverseTransformPoint(lane.foot + shift), area.transform.InverseTransformPoint(lane.head + shift),
                    lane.width, lane.footLeavesArea, lane.footRunRoom, lane.headRunRoom)
                {
                    footReachLeft = lane.footReachLeft, footReachRight = lane.footReachRight, headReachLeft = lane.headReachLeft, headReachRight = lane.headReachRight, reachSpacing = lane.reachSpacing
                };
            }
            area.Configure(climb, shifted, area.LocalBounds);
            Assert.IsNull(area.LinkProblem, area.LinkProblem);
            int wallsOff = 0;
            foreach (var c in tower.GetComponentsInChildren<Collider>())
                if (c.name.StartsWith(LookoutTargets.ClipPrefix)) { c.enabled = false; wallsOff++; }
            Assert.Greater(wallsOff, 0, $"no {LookoutTargets.ClipPrefix} walls found to switch off");

            var silentFlights = new List<int>();
            var silentLevel = new List<int>();
            var notExpectedLevel = new List<int>();
            for (int f = 0; f < FlightCount; f++)
            {
                // Each flight's group is joined at its free foot and ridden to the floor above.
                int i = Group(f)[0];
                StairLane lane = area.Lane(i);
                float across = Mathf.Abs(Vector3.Dot(shift, watch.Model[i].Right));
                yield return WalkTo(ClimbRules.IsLevel(lane) ? lane.foot : lane.Point(-ClimbRules.FootRun(lane, climb), 0f), 15f);
                yield return Face(lane.AxisHorizontal);
                Hold(Key.W);
                yield return Until(() => player.OnLane, 5f, $"join the shifted lanes below flight {f + 1}");
                if (f == 0) watch.ExpectOnTower = true;
                int flightBefore = watch.LateralOnFlight, levelBefore = watch.LateralOnLevel;
                if (f == hatch.LaneIndex && hatch.State != HingeState.Open) yield return OpenHatchFromBelow();
                yield return LeaveAbove(f);
                if (watch.LateralOnFlight == flightBefore) silentFlights.Add(f + 1);
                // On a level stretch the shift only shows where it lies across that stretch by more than its bound allows.
                if (across <= watch.AuthoredSlack + LookoutTargets.LaneLateralMargin) notExpectedLevel.Add(f + 1);
                else if (watch.LateralOnLevel == levelBefore) silentLevel.Add(f + 1);
                yield return null;
            }
            Evidence("c2-twin-shifted-lanes.json",
                $"{{\"shift\":{LookoutTargets.TwinLaneShift},\"wallsOff\":{wallsOff},\"silentFlights\":[{string.Join(",", silentFlights)}],\"silentLevelStretches\":[{string.Join(",", silentLevel)}]," +
                $"\"levelStretchesTheShiftRunsAlong\":[{string.Join(",", notExpectedLevel)}],\"lateralOnFlight\":{watch.LateralOnFlight},\"lateralOnLevel\":{watch.LateralOnLevel},\"watch\":\"{watch.Summary()}\"}}");
            Assert.IsEmpty(silentFlights, "the detector stayed silent on these flights with the lanes shifted: " + string.Join(", ", silentFlights));
            Assert.IsEmpty(silentLevel, "the detector stayed silent on the level stretch below these flights with the lanes shifted: " + string.Join(", ", silentLevel));
            Assert.Less(notExpectedLevel.Count, FlightCount, "the shift ran along every level stretch, so none of them tested the detector");
        }
    }
}
