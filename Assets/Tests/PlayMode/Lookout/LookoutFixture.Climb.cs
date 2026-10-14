using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using WashedAshore.Gameplay;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// The scripted climb and descent: W on the lanes, mouse turns on the landings and at junctions, E for the hatch.
    /// The route is read from the area's links, not typed: each flight with the level lanes chained to its foot and to
    /// its head is one group, walked foot to head going up; between groups the player crosses a landing on foot.
    /// </summary>
    public abstract partial class LookoutFixture
    {
        /// <summary>What one scripted climb or descent measured.</summary>
        protected class ClimbLog
        {
            public float Seconds, LaneSeconds, LandingSeconds, LaneLength, LandingCrossing, HatchSeconds;
            /// <summary>Join to hand-over on each group with no stop in it, seconds and metres.</summary>
            public float FreeLaneSeconds, FreeLaneLength;
            /// <summary>Level travel on the lanes: level lanes and the level runs at free ends, m.</summary>
            public float LevelLength;
            /// <summary>Condition waits inside the timed part of the run.</summary>
            public int Waits;
            /// <summary>Called once per inclined lane at its half-way point, with W held; it must leave W held.</summary>
            public System.Func<int, IEnumerator> MidLane;
            /// <summary>Called on the level run beyond a lane's free head, with W held and the player still on the lane; it must leave W held.</summary>
            public System.Func<int, IEnumerator> HeadRun;
            /// <summary>Called standing at a junction that stopped the player (at a lane's linked head), with W released, before the script turns.</summary>
            public System.Func<int, IEnumerator> Junction;
            /// <summary>Called half-way along a level lane entered through a junction, with W held; it must leave W held.</summary>
            public System.Func<int, IEnumerator> LevelLane;
            /// <summary>Yaw the script turned to get from one group's hand-over point round to the next group's lane, degrees, per landing.</summary>
            public readonly List<float> Turns = new List<float>();
            /// <summary>Yaw the script turned at each junction (a release where the player was stopped; a few degrees where carried), degrees.</summary>
            public readonly List<float> JunctionTurns = new List<float>();
            /// <summary>Junctions that stopped the player until they turned.</summary>
            public int Stops;
            public int LanesPassed, FlightsPassed;
        }

        const float JunctionEps = 0.001f;

        /// <summary>Flights in the model (the inclined lanes come first in the area, bottom to top).</summary>
        protected int FlightCount
        {
            get
            {
                int n = 0;
                while (n < area.LaneCount && !ClimbRules.IsLevel(area.Lane(n))) n++;
                return n;
            }
        }

        protected bool FootLinked(int lane) => area.TryLink(lane, ClimbRules.End.Foot, out _, out _);
        protected bool HeadLinked(int lane) => area.TryLink(lane, ClimbRules.End.Head, out _, out _);

        /// <summary>
        /// A flight's group, in the order it is walked going up: the level lanes chained below its foot (outermost
        /// first), the flight, the level lanes chained beyond its head (outermost last). Every lane in a group runs
        /// foot to head in that direction, so "up" is toward the head throughout.
        /// </summary>
        protected List<int> Group(int flight)
        {
            var below = new List<int>();
            int lane = flight;
            while (area.TryLink(lane, ClimbRules.End.Foot, out int next, out var end))
            {
                Assert.AreEqual(ClimbRules.End.Head, end, $"lane {next + 1} is chained below lane {lane + 1} by its foot; expected its head");
                below.Insert(0, next);
                lane = next;
            }
            var group = new List<int>(below) { flight };
            lane = flight;
            while (area.TryLink(lane, ClimbRules.End.Head, out int next, out var end))
            {
                Assert.AreEqual(ClimbRules.End.Foot, end, $"lane {next + 1} is chained beyond lane {lane + 1} by its head; expected its foot");
                group.Add(next);
                lane = next;
            }
            return group;
        }

        /// <summary>The hand-over point at the bottom of a group (the far end of its first lane's level run), and at its top.</summary>
        protected Vector3 GroupFoot(List<int> group)
        {
            StairLane lane = area.Lane(group[0]);
            return lane.Point(-ClimbRules.FootRun(lane, climb), 0f);
        }

        protected Vector3 GroupHead(List<int> group)
        {
            StairLane lane = area.Lane(group[group.Count - 1]);
            return lane.Point(lane.Length + ClimbRules.HeadRun(lane, climb), 0f);
        }

        /// <summary>Standing still at a linked end of a lane (the player holds there until the input points along the next lane).</summary>
        protected bool AtJunction(int lane, bool head)
        {
            if (!player.OnLane || player.LaneIndex != lane || !Mathf.Approximately(player.LaneSpeed, 0f)) return false;
            return head ? player.LaneDistance >= area.Lane(lane).Length - JunctionEps : player.LaneDistance <= JunctionEps;
        }

        bool On(int lane) => player.OnLane && player.LaneIndex == lane;

        /// <summary>
        /// Base to cab floor: W on each lane, a mouse turn on each landing and at each junction, E for the hatch. Starts
        /// timing when the first lane is joined and stops when the top group hands over to the cab floor.
        /// </summary>
        protected IEnumerator ClimbUp(ClimbLog log, int fromFlight = 0)
        {
            float started = Time.time;
            int flights = FlightCount;
            for (int f = fromFlight; f < flights; f++)
            {
                var group = Group(f);
                if (f > fromFlight)
                {
                    // Cross the landing to the next group's hand-over point, then turn to face along its first lane.
                    Vector3 from = player.transform.position;
                    float crossingStart = Time.time;
                    yield return WalkTo(GroupFoot(group), 10f);
                    float turned = LastTurn;
                    log.LandingCrossing += Flat(player.transform.position - from).magnitude;
                    yield return Face(area.Lane(group[0]).AxisHorizontal);
                    log.Turns.Add(turned + LastTurn);
                    log.LandingSeconds += Time.time - crossingStart;
                }
                else yield return Face(area.Lane(group[0]).AxisHorizontal);

                float groupStart = Time.time;
                bool stopped = false;
                for (int k = 0; k < group.Count; k++)
                {
                    int i = group[k];
                    StairLane lane = area.Lane(i);
                    bool level = ClimbRules.IsLevel(lane);
                    Hold(Key.W);
                    if (k > 0)
                    {
                        // At a junction: turning into the next lane's cone with W held is enough (nothing if it was carried).
                        yield return Face(lane.AxisHorizontal);
                        log.JunctionTurns.Add(LastTurn);
                    }
                    yield return Until(() => On(i), 5f, $"get onto lane {i + 1}");
                    if (f == 0 && k == 0) { watch.ExpectOnTower = true; started = groupStart = Time.time; log.Waits = -ConditionWaits; }
                    if (i == hatch.LaneIndex && hatch.State != HingeState.Open)
                    {
                        float hatchStart = Time.time;
                        yield return OpenHatchFromBelow();
                        log.HatchSeconds += Time.time - hatchStart;
                        stopped = true;
                    }
                    float along = lane.Length / (level ? climb.towerWalkSpeed : climb.stairAscendSpeed) * 2f + 10f;
                    if (log.LevelLane != null && level && k > 0)
                    {
                        yield return Until(() => !On(i) || player.LaneDistance >= lane.Length * 0.5f, along, $"reach the middle of level lane {i + 1}");
                        if (On(i)) { yield return log.LevelLane(i); stopped = true; }
                    }
                    if (log.MidLane != null && !level)
                    {
                        yield return Until(() => !On(i) || player.LaneDistance >= lane.Length * 0.5f, along, $"reach the middle of lane {i + 1}");
                        yield return log.MidLane(i);
                        stopped = true;
                    }
                    if (k < group.Count - 1)
                    {
                        // Either the junction carries the player straight onto the next lane, or it stops them there.
                        int next = group[k + 1];
                        yield return Until(() => AtJunction(i, true) || On(next), along, $"reach the junction at the head of lane {i + 1}");
                        if (AtJunction(i, true))
                        {
                            log.Stops++;
                            stopped = true;
                            if (log.Junction != null) { Let(Key.W); yield return log.Junction(i); }
                        }
                    }
                    else
                    {
                        if (log.HeadRun != null && ClimbRules.HeadRun(lane, climb) > 0f)
                        {
                            yield return Until(() => !player.OnLane || (On(i) && player.LaneDistance > lane.Length), along, $"reach the level run at the head of lane {i + 1}");
                            if (On(i)) { yield return log.HeadRun(i); stopped = true; }
                        }
                        yield return Until(() => !player.OnLane, along, $"leave lane {i + 1} at its head");
                    }
                    // The §2.4 formula: inclined lanes at the ascent speed; level lanes and level runs at the walk speed.
                    if (level) log.LevelLength += lane.Length; else log.LaneLength += lane.Length;
                    if (k == 0) log.LevelLength += ClimbRules.FootRun(lane, climb);
                    if (k == group.Count - 1) log.LevelLength += ClimbRules.HeadRun(lane, climb);
                    log.LanesPassed++;
                }
                Let(Key.W);
                log.LaneSeconds += Time.time - groupStart;
                if (!stopped) { log.FreeLaneSeconds += Time.time - groupStart; log.FreeLaneLength += area.Lane(f).Length; }
                log.FlightsPassed++;
                yield return null;
            }
            log.Seconds = Time.time - started;
            log.Waits += ConditionWaits;
        }

        /// <summary>On the hatch's lane under the shut hatch: wait for the gate, look at the leaf, press E, wait for it to open, carry on.</summary>
        protected IEnumerator OpenHatchFromBelow()
        {
            yield return Until(() => Mathf.Approximately(player.LaneSpeed, 0f) && player.LaneDistance > 0f, 30f, "stop under the shut hatch");
            Let(Key.W);
            yield return LookAt(hatch.GetComponentInChildren<Collider>(true).bounds.center);
            Assert.AreSame(hatch, interactor.Target, $"Aiming at the shut hatch from the gate shows no target (prompt '{interactor.PromptLine}')");
            yield return Tap(Key.E);
            yield return Until(() => hatch.State == HingeState.Open, LookoutTargets.HatchMaxSeconds + 1f, "hatch to open");
            yield return Face(area.Lane(hatch.LaneIndex).AxisHorizontal);
            Hold(Key.W);
        }

        /// <summary>Cab floor to the ground: the same route reversed. The hatch must be open.</summary>
        protected IEnumerator ClimbDown(ClimbLog log, int toFlight = 0)
        {
            float started = Time.time;
            for (int f = FlightCount - 1; f >= toFlight; f--)
            {
                var group = Group(f);
                yield return WalkTo(GroupHead(group), 10f);
                float turned = LastTurn;
                yield return RideDown(group, group.Count - 1, 0, log, turned);
                yield return Until(() => !player.OnLane, 60f, $"leave lane {group[0] + 1} at its foot");
                Let(Key.W);
                log.FlightsPassed++;
                yield return null;
            }
            log.Seconds = Time.time - started;
        }

        /// <summary>
        /// From the group's top hand-over point (or already on lane <c>group[from]</c>), walks down through the group's
        /// lanes to lane <c>group[to]</c> and stops waiting once on it. W is left held, facing down that lane.
        /// </summary>
        IEnumerator RideDown(List<int> group, int from, int to, ClimbLog log, float turnedSoFar)
        {
            for (int k = from; k >= to; k--)
            {
                int i = group[k];
                StairLane lane = area.Lane(i);
                if (k < from)
                {
                    int above = group[k + 1];
                    yield return Until(() => AtJunction(above, false) || On(i), area.Lane(above).Length / climb.stairDescendSpeed * 2f + 10f, $"reach the junction at the foot of lane {above + 1}");
                    if (AtJunction(above, false) && log != null) log.Stops++;
                }
                Hold(Key.W);
                yield return Face(-lane.AxisHorizontal);
                if (log != null)
                {
                    if (k == from) log.Turns.Add(turnedSoFar + LastTurn); else log.JunctionTurns.Add(LastTurn);
                    if (ClimbRules.IsLevel(lane)) log.LevelLength += lane.Length; else log.LaneLength += lane.Length;
                    log.LanesPassed++;
                }
                yield return Until(() => On(i), 5f, $"get onto lane {i + 1} from above");
            }
        }

        /// <summary>
        /// From the floor above flight <paramref name="flight"/>, walk onto it heading down, through any lanes chained
        /// beyond its head (a linked run-out, or the cab's exit lane). W is left held, facing down the flight.
        /// </summary>
        protected IEnumerator EnterFromAbove(int flight)
        {
            var group = Group(flight);
            yield return WalkTo(GroupHead(group), 10f);
            yield return RideDown(group, group.Count - 1, group.IndexOf(flight), null, 0f);
        }

        protected IEnumerator Until(System.Func<bool> done, float timeout, string what)
        {
            ConditionWaits++;
            for (float t = 0f; t < timeout; t += Time.deltaTime)
            {
                if (done()) yield break;
                yield return null;
            }
            Assert.IsTrue(done(), $"Timed out after {timeout:F0} s waiting to {what}. Player at {player.transform.position:F2}, lane {player.LaneIndex}, " +
                                  $"u {player.LaneDistance:F2}, hatch {hatch.State}. {watch.Summary()}");
        }

        /// <summary>On flight <paramref name="flight"/>: carry on up to the floor above, through any lanes chained beyond its head. Ends off the lanes with W released.</summary>
        protected IEnumerator LeaveAbove(int flight)
        {
            var group = Group(flight);
            for (int k = group.IndexOf(flight); k < group.Count; k++)
            {
                int i = group[k];
                Hold(Key.W);
                yield return Face(area.Lane(i).AxisHorizontal);
                yield return Until(() => On(i), 5f, $"get onto lane {i + 1}");
                if (i == hatch.LaneIndex && hatch.State != HingeState.Open) yield return OpenHatchFromBelow();
                if (k < group.Count - 1)
                {
                    int next = group[k + 1];
                    yield return Until(() => AtJunction(i, true) || On(next), area.Lane(i).Length / climb.stairAscendSpeed * 2f + 10f, $"reach the junction at the head of lane {i + 1}");
                }
            }
            yield return Until(() => !player.OnLane, 60f, $"leave lane {group[group.Count - 1] + 1} at its head");
            Let(Key.W);
            yield return null;
        }
    }
}
