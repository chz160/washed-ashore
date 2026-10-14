using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Interaction;
using WashedAshore.Lookout;
using WashedAshore.Lookout.Editor;
using WashedAshore.Tests.PlayMode.Lookout;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode.Lookout
{
    /// <summary>
    /// Checks on the built tower as placed in the World scene (tower-design-spec.md §9 T-C1g, T-I1a, T-I3b;
    /// tower-td-note §6 conditions 4 and 8): every lane end is clear for the capsule, every lane lies on the visual
    /// nosing line, the wiring carries no null reference, the ring's numerals stand at their bearings, and the aim
    /// ray's range. Reads the saved scene and the build manifest; nothing here is typed in.
    /// </summary>
    public partial class LookoutBuiltTowerTests
    {
        const string ScenePath = "Assets/Scenes/World.unity";
        const string StairsMeshWord = "Stairs";        // the LOD0 render mesh that carries the treads
        const float NosingTolerance = 0.02f;            // td §6 condition 8, spec §2.3 point 1: lane within this of the nosing line, m
        const float NumeralTolerance = 0.5f;            // spec T-I3b: numeral N at N +/- this, degrees
        const float NosingSampleRun = 0.005f;           // sample spacing along the run, m (a script constant)
        const float RunSweepStep = 0.02f;               // spec T-C1g part 2: every 2 cm along a level run
        const float LevelTolerance = 0.01f;             // spec T-C1h, td §12 condition 3: a level run lies on its deck within this, m
        const float RunTolerance = 1e-4f;               // float allowance when comparing a run's length with radius + skin
        const float NosingProbeLift = 0.3f;             // the probe ray starts this far above the lane and reaches as far below, m (a script constant)

        Transform tower;
        ClimbArea area;
        TrapDoor hatch;
        CharacterController playerCapsule;
        LookoutManifest manifest;
        readonly List<Object> temp = new List<Object>();

        [OneTimeSetUp]
        public void OpenWorld()
        {
            if (SceneManager.GetActiveScene().path != ScenePath) EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find(LookoutTargets.TowerRoot);
            Assert.IsNotNull(root, $"No '{LookoutTargets.TowerRoot}' in {ScenePath}");
            tower = root.transform;
            area = root.GetComponentInChildren<ClimbArea>();
            hatch = root.GetComponentInChildren<TrapDoor>();
            Assert.IsNotNull(area, "Tower has no ClimbArea (LookoutGameplaySetup.Apply not run?)");
            Assert.IsNotNull(hatch, "Tower has no TrapDoor");
            area.Rebuild();
            var player = GameObject.FindGameObjectWithTag("Player");
            Assert.IsNotNull(player, "No Player in World");
            playerCapsule = player.GetComponent<CharacterController>();
            manifest = LookoutManifest.Load();
            Physics.SyncTransforms();
        }

        [TearDown]
        public void RemoveTemp()
        {
            foreach (var o in temp) if (o) Object.DestroyImmediate(o);
            temp.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void Wiring_HasEveryPart_AndNoMissingReference()
        {
            Assert.IsNotNull(area.Tuning, "ClimbArea.tuning");
            Assert.IsNull(area.Tuning.Validate(playerCapsule.radius, 5f), area.Tuning.Validate(playerCapsule.radius, 5f));
            Assert.IsNotNull(hatch.Tuning, "TrapDoor.tuning");
            Assert.IsNotNull(hatch.LeafCollider, "TrapDoor.leafCollider");
            Assert.AreEqual(area.LaneCount - 1, hatch.LaneIndex, "the hatch is on the top lane");
            Assert.AreEqual(LookoutTargets.HingedSashes, tower.GetComponentsInChildren<WindowSash>(true).Length, "hinged sashes");
            foreach (var sash in tower.GetComponentsInChildren<WindowSash>(true))
            {
                Assert.IsNotNull(sash.Tuning, $"{sash.name}.tuning");
                Assert.IsNotNull(sash.GetComponentInChildren<Collider>(true), $"{sash.name} has no aim collider under it");
            }
            var firefinder = tower.GetComponentInChildren<Firefinder>(true);
            Assert.IsNotNull(firefinder, "Tower has no Firefinder");
            Assert.IsNotNull(firefinder.Alidade, "Firefinder.alidade");
            Assert.IsNotNull(firefinder.GetComponentInChildren<Collider>(true), "Firefinder has no aim collider under it");
            var interactor = playerCapsule.GetComponentInChildren<Interactor>(true);
            Assert.IsNotNull(interactor, "No Interactor on the Player (LookoutGameplaySetup.ApplyToPlayer not run?)");
            Assert.IsNotNull(interactor.Settings, "Interactor.settings");
            Assert.IsNull(interactor.Settings.Validate(), interactor.Settings.Validate());
        }

        /// <summary>The capsule (radius given) standing with its feet at a lane point: every solid or invisible collider it touches.</summary>
        List<Collider> Touching(in StairLane lane, float u, float lateral, float radius)
        {
            var t = area.Tuning;
            Vector3 feet = lane.Point(u, lateral) + Vector3.up * playerCapsule.skinWidth;
            Vector3 bottom = feet + Vector3.up * (radius + t.capsuleFitLift), top = feet + Vector3.up * (playerCapsule.height - radius);
            var found = new List<Collider>();
            foreach (var hit in Physics.OverlapCapsule(bottom, top, radius, WorldLayers.SightMask | (1 << WorldLayers.WorldBounds), QueryTriggerInteraction.Ignore))
                if (hit != playerCapsule) found.Add(hit);
            return found;
        }

        /// <summary>
        /// T-C1g (tower-td-note §12a rule 5; tower-design-spec.md §9): at every free lane end the level run is at least
        /// radius + skin long; the capsule at radius + skin, every 2 cm along the run and across the lateral slack,
        /// touches nothing; and so at the hand-over point at the run's far end. Linked ends hand nothing over and are
        /// not checked. With the hatch closed and open. A failing end is named.
        /// </summary>
        [Test]
        public void FreeLaneEnds_AndTheirRuns_AreClearForTheCapsule([Values(false, true)] bool hatchOpen)
        {
            var t = area.Tuning;
            float radius = t.towerCapsuleRadius + playerCapsule.skinWidth;
            bool leafWas = hatch.LeafCollider.enabled;
            Quaternion leafPose = hatch.transform.localRotation;
            if (hatchOpen) hatch.SetState(HingeState.Open, 0f);
            Physics.SyncTransforms();
            var blocked = new List<string>();
            try
            {
                for (int i = 0; i < area.LaneCount; i++)
                {
                    StairLane lane = area.Lane(i);
                    bool exit = i >= manifest.flights.Length;
                    foreach (bool head in new[] { false, true })
                    {
                        if (area.TryLink(i, head ? ClimbRules.End.Head : ClimbRules.End.Foot, out _, out _)) continue;
                        float run = head ? ClimbRules.HeadRun(lane, t) : ClimbRules.FootRun(lane, t);
                        string end = $"lane {i + 1} {(head ? "head" : "foot")}";
                        // An exit lane is itself the level stretch: its free end is the hand-over point and needs no run of its own.
                        if (!exit && run < radius - RunTolerance)
                            blocked.Add($"{end}: level run {run:F3} m is shorter than the capsule's {radius:F2} m (radius + skin), so the hand-over is on the lip");
                        foreach (float lateral in new[] { -t.laneLateralSlack, 0f, t.laneLateralSlack })
                            for (float along = 0f; along <= run + RunSweepStep * 0.5f; along += RunSweepStep)
                            {
                                float u = head ? lane.Length + Mathf.Min(along, run) : -Mathf.Min(along, run);
                                foreach (var hit in Touching(lane, u, lateral, radius))
                                    blocked.Add($"{end} {(along >= run ? "hand-over point" : $"run at {along:F2} m")}, lateral {lateral:+0.00;-0.00}: {hit.name}");
                                if (run <= 0f) break;
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
            Assert.IsEmpty(blocked, $"free lane ends not clear for a {radius} m capsule (hatch {(hatchOpen ? "open" : "closed")}):\n" + string.Join("\n", blocked));
        }

        /// <summary>tower-td-note §12a and the lane-end ruling: links join exactly two ends and never two inclined flights; exit and run-out lanes are level, chained from a flight end to one free end.</summary>
        [Test]
        public void LaneLinks_AreSound()
        {
            Assert.IsNull(area.LinkProblem, area.LinkProblem);
            Assert.IsNull(ClimbRules.TaperProblem(AllLanes(), area.Tuning), ClimbRules.TaperProblem(AllLanes(), area.Tuning));
            int exits = manifest.exit_lanes != null ? manifest.exit_lanes.Length : 0, segments = 0;
            foreach (var flight in manifest.flights)
            {
                if (flight.head_run_out != null && flight.head_run_out.Present) segments += flight.head_run_out.empties.Length;
                if (flight.foot_run_out != null && flight.foot_run_out.Present) segments += flight.foot_run_out.empties.Length;
            }
            Assert.AreEqual(manifest.flights.Length + exits + segments, area.LaneCount, "one lane per flight, per exit lane and per run-out segment");
            for (int i = 0; i < manifest.flights.Length; i++)
                Assert.IsFalse(ClimbRules.IsLevel(area.Lane(i)), $"lane {i + 1} is a flight but is level");
            for (int i = manifest.flights.Length; i < area.LaneCount; i++)
            {
                StairLane lane = area.Lane(i);
                Assert.IsTrue(ClimbRules.IsLevel(lane), $"level lane {i + 1} is not level");
                Assert.AreEqual(lane.foot.y, lane.head.y, LevelTolerance, $"level lane {i + 1} runs on its deck's plane");
                bool footLinked = area.TryLink(i, ClimbRules.End.Foot, out _, out _), headLinked = area.TryLink(i, ClimbRules.End.Head, out _, out _);
                Assert.IsTrue(footLinked || headLinked, $"level lane {i + 1} is linked to nothing");
            }
            for (int k = 0; k < exits; k++)
            {
                int i = manifest.flights.Length + k;
                Assert.IsTrue(area.TryLink(i, ClimbRules.End.Foot, out int flight, out var end), $"exit lane {i + 1} is not linked at its foot");
                Assert.AreEqual(ClimbRules.End.Head, end, $"exit lane {i + 1} is linked to lane {flight + 1}'s foot, expected its head");
                Assert.IsFalse(area.TryLink(i, ClimbRules.End.Head, out _, out _), $"exit lane {i + 1}'s head must be free: it is the hand-over point");
            }
            // Every chain of level lanes ends in exactly one free end, and every flight end is free or linked to a level lane.
            for (int i = 0; i < manifest.flights.Length; i++)
                foreach (var end in new[] { ClimbRules.End.Foot, ClimbRules.End.Head })
                {
                    int lane = i, hops = 0;
                    var side = end;
                    while (area.TryLink(lane, side, out int next, out var nextEnd))
                    {
                        Assert.IsTrue(ClimbRules.IsLevel(area.Lane(next)), $"flight {i + 1}'s {end} is chained to inclined lane {next + 1}");
                        lane = next;
                        side = nextEnd == ClimbRules.End.Foot ? ClimbRules.End.Head : ClimbRules.End.Foot;
                        Assert.Less(++hops, area.LaneCount, $"the chain from flight {i + 1}'s {end} never ends");
                    }
                }
        }

        StairLane[] AllLanes()
        {
            var lanes = new StairLane[area.LaneCount];
            for (int i = 0; i < lanes.Length; i++) lanes[i] = area.Lane(i);
            return lanes;
        }

        /// <summary>
        /// The two layout checks that read the junction lever (ruling/lookout-tower-upper-landing-step-on-amend3; the
        /// lane-end ruling): at every junction the turn is either carried (at most the lever's angle) or a real corner
        /// (at least the lever's angle + NoTwitchDegrees), never in between; and a run-out the manifest says steps away
        /// from the next flight turns at most AwayMaxDegrees. Angles are measured on the built lanes, with the lever's
        /// angle computed from the tuning value itself. Every angle is computed from the built lane ends; the manifest's
        /// per-segment figures are only the claim it is compared with.
        /// </summary>
        [Test]
        public void Junctions_AreCarriedOrRealCorners_AndStepsAwayAreSmall()
        {
            float lever = Mathf.Acos(area.Tuning.laneJunctionAlong) * Mathf.Rad2Deg;
            var problems = new List<string>();
            var rows = new List<string>();
            for (int i = 0; i < area.LaneCount; i++)
            {
                // Each junction once, from the lane whose head is at it; chains run foot to head upward, so that covers them all.
                if (!area.TryLink(i, ClimbRules.End.Head, out int next, out var nextEnd)) continue;
                Vector3 arriving = area.Lane(i).AxisHorizontal;
                Vector3 leaving = nextEnd == ClimbRules.End.Foot ? area.Lane(next).AxisHorizontal : -area.Lane(next).AxisHorizontal;
                float angle = Vector3.Angle(arriving, leaving);
                string outcome = angle <= lever ? "carried" : "stop-and-turn";
                rows.Add($"lanes {i + 1}->{next + 1}: {angle:F2} deg, {outcome} at a {lever:F2} deg lever");
                if (angle > lever && angle < lever + LookoutTargets.NoTwitchDegrees)
                    problems.Add($"junction of lanes {i + 1} and {next + 1}: {angle:F2} deg is a stop released by only {angle - lever:F2} deg of turn (the band {lever:F2} to {lever + LookoutTargets.NoTwitchDegrees:F2} is not allowed)");
            }
            Debug.Log("LookoutBuiltTowerTests: junctions: " + string.Join("; ", rows));
            for (int f = 0; f < manifest.flights.Length; f++)
                foreach (bool head in new[] { false, true })
                {
                    var runOut = head ? manifest.flights[f].head_run_out : manifest.flights[f].foot_run_out;
                    if (runOut == null || !runOut.Present) continue;
                    string end = $"flight {f + 1} {(head ? "head" : "foot")}";
                    var turns = ChainTurns(f, head);
                    Assert.AreEqual(runOut.empties.Length, turns.Count, $"{end}: one built segment per run-out empty");
                    if (runOut.segment_turns_deg == null || runOut.segment_turns_deg.Length != turns.Count)
                        problems.Add($"{end}: the manifest gives {(runOut.segment_turns_deg == null ? 0 : runOut.segment_turns_deg.Length)} segment_turns_deg for {turns.Count} segments");
                    float largest = 0f;
                    for (int k = 0; k < turns.Count; k++)
                    {
                        largest = Mathf.Max(largest, turns[k]);
                        if (runOut.segment_turns_deg != null && k < runOut.segment_turns_deg.Length && Mathf.Abs(turns[k] - runOut.segment_turns_deg[k]) > LookoutTargets.TurnAgreementDegrees)
                            problems.Add($"{end}: segment {k + 1} turns {turns[k]:F2} deg on the built lanes, the manifest says {runOut.segment_turns_deg[k]:F2}");
                    }
                    bool away = runOut.turn != null && runOut.turn.StartsWith("away");
                    if (away && largest > LookoutTargets.AwayMaxDegrees)
                        problems.Add($"{end}: steps {largest:F2} deg away from the next flight (at most {LookoutTargets.AwayMaxDegrees}, or named by t-director)");
                }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>The position of a named empty under the tower, tower-local.</summary>
        Vector3 Require(string name)
        {
            foreach (var t in tower.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return tower.InverseTransformPoint(t.position);
            Assert.Fail($"No object named '{name}' under the tower");
            return Vector3.zero;
        }

        /// <summary>
        /// The turn at the start of each lane chained off a flight's end, walking away from the flight: the first from
        /// the flight's heading, the rest from the lane before. Computed from the built lane ends only.
        /// </summary>
        List<float> ChainTurns(int flight, bool head)
        {
            var turns = new List<float>();
            int lane = flight;
            var side = head ? ClimbRules.End.Head : ClimbRules.End.Foot;
            Vector3 heading = head ? area.Lane(flight).AxisHorizontal : -area.Lane(flight).AxisHorizontal;
            while (turns.Count < area.LaneCount && area.TryLink(lane, side, out int next, out var nextEnd))
            {
                Vector3 leaving = nextEnd == ClimbRules.End.Foot ? area.Lane(next).AxisHorizontal : -area.Lane(next).AxisHorizontal;
                turns.Add(Vector3.Angle(heading, leaving));
                heading = leaving;
                lane = next;
                side = nextEnd == ClimbRules.End.Foot ? ClimbRules.End.Head : ClimbRules.End.Foot;
            }
            return turns;
        }

        /// <summary>T-C1h: every level run lies on its deck: the floor under it is at the lane end's height within 0.01 m, all the way along.</summary>
        [Test]
        public void LevelRuns_LieOnTheirDecks()
        {
            var t = area.Tuning;
            var off = new List<string>();
            for (int i = 0; i < area.LaneCount; i++)
            {
                StairLane lane = area.Lane(i);
                foreach (bool head in new[] { false, true })
                {
                    if (area.TryLink(i, head ? ClimbRules.End.Head : ClimbRules.End.Foot, out _, out _)) continue;
                    float run = head ? ClimbRules.HeadRun(lane, t) : ClimbRules.FootRun(lane, t);
                    // Past the lip: the first sample is one step in, where there is deck under the lane's line.
                    for (float along = RunSweepStep; along <= run + RunSweepStep * 0.5f; along += RunSweepStep)
                    {
                        Vector3 on = lane.Point(head ? lane.Length + Mathf.Min(along, run) : -Mathf.Min(along, run), 0f);
                        bool hit = Physics.Raycast(on + Vector3.up * playerCapsule.stepOffset, Vector3.down, out RaycastHit h, playerCapsule.stepOffset * 2f, WorldLayers.SightMask, QueryTriggerInteraction.Ignore);
                        if (!hit) off.Add($"lane {i + 1} {(head ? "head" : "foot")} run at {along:F2} m: no deck under it");
                        else if (Mathf.Abs(h.point.y - on.y) > LevelTolerance) off.Add($"lane {i + 1} {(head ? "head" : "foot")} run at {along:F2} m: deck {h.point.y - on.y:+0.000;-0.000} m from the run ({h.collider.name})");
                    }
                }
            }
            Assert.IsEmpty(off, "level runs off their decks:\n" + string.Join("\n", off));
        }

        /// <summary>
        /// T-C1g part 3 (exit lane only): carried along the exit lane the capsule may overlap one named proxy, the cab wall
        /// beside the top of the stair, by at most the manifest's figure, never more toward the free end, and not at all
        /// at the free end; any other overlap fails. Checked every 2 cm.
        /// </summary>
        [Test]
        public void ExitLane_OverlapsOnlyTheNamedWall_LessAndLessTowardItsFreeEnd()
        {
            if (manifest.exit_lanes == null || manifest.exit_lanes.Length == 0) Assert.Ignore("This model has no exit lane.");
            float radius = area.Tuning.towerCapsuleRadius + playerCapsule.skinWidth;
            Quaternion leafPose = hatch.transform.localRotation;
            hatch.SetState(HingeState.Open, 0f);
            Physics.SyncTransforms();
            var problems = new List<string>();
            try
            {
                for (int k = 0; k < manifest.exit_lanes.Length; k++)
                {
                    var data = manifest.exit_lanes[k];
                    StairLane lane = area.Lane(manifest.flights.Length + k);
                    Assert.IsFalse(string.IsNullOrEmpty(data.wall_proxy), $"manifest exit_lanes[{k}].wall_proxy");
                    Assert.GreaterOrEqual(data.wall_overlap_max, 0f, $"manifest exit_lanes[{k}].wall_overlap_max");
                    float last = float.MaxValue, worst = 0f;
                    for (float u = 0f; u <= lane.Length + RunSweepStep * 0.5f; u += RunSweepStep)
                    {
                        float at = Mathf.Min(u, lane.Length), depth = 0f;
                        foreach (var hit in Touching(lane, at, 0f, radius))
                        {
                            if (hit.name != data.wall_proxy) { problems.Add($"exit lane {data.index} at {at:F2} m: touches {hit.name}"); continue; }
                            depth = OverlapDepth(lane, at, radius, hit);
                        }
                        worst = Mathf.Max(worst, depth);
                        if (depth > last + RunTolerance) problems.Add($"exit lane {data.index} at {at:F2} m: overlap with {data.wall_proxy} grows ({last:F3} to {depth:F3} m) toward the free end");
                        if (at >= lane.Length && depth > 0f) problems.Add($"exit lane {data.index}: {depth:F3} m inside {data.wall_proxy} at its free end");
                        last = depth;
                    }
                    Debug.Log($"LookoutBuiltTowerTests: exit lane {data.index}: deepest overlap with {data.wall_proxy} {worst:F3} m (allowed {data.wall_overlap_max})");
                    if (worst > data.wall_overlap_max) problems.Add($"exit lane {data.index}: {worst:F3} m inside {data.wall_proxy}, more than the allowed {data.wall_overlap_max} m");
                }
            }
            finally
            {
                hatch.SetState(HingeState.Closed, 0f);
                hatch.transform.localRotation = leafPose;
                Physics.SyncTransforms();
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        /// <summary>How far the capsule reaches into one collider at a lane point: shrink it until it clears.</summary>
        float OverlapDepth(in StairLane lane, float u, float radius, Collider wall)
        {
            float lo = 0f, hi = radius;
            for (int b = 0; b < OverlapBisections; b++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Touching(lane, u, 0f, mid).Contains(wall)) hi = mid; else lo = mid;
            }
            return radius - lo;
        }

        const int OverlapBisections = 12;   // 0.30 m / 2^12: well under a millimetre

        /// <summary>td condition 8: each lane lies on the visual nosing line within 0.02 m: no tread stands above it, and every tread's nosing reaches it.</summary>
        [Test]
        public void Lanes_LieOnTheVisualNosingLine()
        {
            int mask = 1 << WorldLayers.Water;   // nothing gameplay casts at; removed in TearDown
            int probes = 0;
            foreach (var filter in tower.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.name.Contains(StairsMeshWord) || filter.GetComponent<Renderer>() == null || filter.sharedMesh == null) continue;
                var go = new GameObject("NosingProbe") { layer = WorldLayers.Water };
                go.transform.SetParent(filter.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
                temp.Add(go);
                probes++;
            }
            Assert.Greater(probes, 0, $"no render mesh named *{StairsMeshWord}* under the tower");
            Physics.SyncTransforms();

            // Flights only: an exit lane runs on the floor, not on a nosing line.
            for (int i = 0; i < manifest.flights.Length; i++)
            {
                StairLane lane = area.Lane(i);
                float stepSlope = NosingSampleRun / Mathf.Max(lane.RunPerMetre, 1e-3f), highest = float.MinValue;
                int touches = 0;
                bool touching = false;
                float lift = NosingProbeLift;
                for (float u = stepSlope; u < lane.Length - stepSlope; u += stepSlope)
                {
                    Vector3 on = lane.Point(u, 0f);
                    bool hit = Physics.Raycast(on + Vector3.up * lift, Vector3.down, out RaycastHit h, lift * 2f, mask, QueryTriggerInteraction.Ignore);
                    float above = hit ? h.point.y - on.y : float.MinValue;      // tread surface relative to the lane
                    highest = Mathf.Max(highest, above);
                    bool near = hit && Mathf.Abs(above) <= NosingTolerance;
                    if (near && !touching) touches++;
                    touching = near;
                }
                Debug.Log($"LookoutBuiltTowerTests: lane {i + 1}: highest tread {highest:F4} m above the lane, {touches} nosings within {NosingTolerance} m");
                Assert.LessOrEqual(highest, NosingTolerance, $"a tread stands above lane {i + 1}");
                // A flight of n risers has n - 1 treads between its landings; each tread's nosing must reach the lane.
                int risers = manifest.targets.risers_per_flight[i];
                Assert.GreaterOrEqual(touches, risers - 1, $"lane {i + 1}: nosings reaching the lane (of {risers} risers)");
            }
        }

        /// <summary>T-I3b: each ring numeral's empty stands at its bearing from the pedestal axis (000 at +Z, 090 at +X).</summary>
        [Test]
        public void RingNumerals_StandAtTheirBearings()
        {
            Vector3 axis = tower.TransformPoint(LookoutManifest.V(manifest.firefinder.axis, "firefinder.axis"));
            string prefix = manifest.firefinder.numeral_empty_prefix;
            Assert.IsFalse(string.IsNullOrEmpty(prefix), "manifest firefinder.numeral_empty_prefix");
            int found = 0;
            foreach (var t in tower.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith(prefix)) continue;
                Assert.IsTrue(int.TryParse(t.name.Substring(prefix.Length), out int bearing), $"{t.name}: no bearing in the name");
                float azimuth = LookoutRules.Bearing(t.position - axis);
                Assert.AreEqual(0f, Mathf.DeltaAngle(bearing, azimuth), NumeralTolerance, $"{t.name} stands at {azimuth:F2} deg");
                found++;
            }
            Assert.AreEqual(36, found, "one numeral every 10 degrees (spec §5)");
        }

        /// <summary>T-I1a: the aim ray against stub colliders: inside and outside the range, occlusion, layers that don't occlude, and an unusable target.</summary>
        [Test]
        public void AimRay_RangeOcclusionAndLayers()
        {
            var settings = ScriptableObject.CreateInstance<InteractionSettings>();
            temp.Add(settings);
            var eye = new GameObject("AimProbe");
            temp.Add(eye);
            // High above the map, where nothing else is on the ray.
            eye.transform.position = tower.position + Vector3.up * 500f;
            var interactor = eye.AddComponent<Interactor>();
            interactor.Configure(settings, null);

            StubInteractable Stub(float distance)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                temp.Add(go);
                go.transform.position = eye.transform.position + eye.transform.forward * (distance + 0.5f);
                Physics.SyncTransforms();
                return go.AddComponent<StubInteractable>();
            }

            var near = Stub(LookoutTargets.RayInside);
            Assert.AreSame(near, interactor.Aim(eye.transform.position, eye.transform.forward), $"target at {LookoutTargets.RayInside} m");
            near.Usable = false;
            Assert.IsNull(interactor.Aim(eye.transform.position, eye.transform.forward), "CanInteract false: no target");
            near.Usable = true;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.Add(wall);
            wall.transform.localScale = new Vector3(3f, 3f, 0.05f);
            wall.transform.position = eye.transform.position + eye.transform.forward;
            Physics.SyncTransforms();
            Assert.IsNull(interactor.Aim(eye.transform.position, eye.transform.forward), "behind a solid collider: no target");
            wall.layer = WorldLayers.WorldBounds;
            Assert.AreSame(near, interactor.Aim(eye.transform.position, eye.transform.forward), "WorldBounds in between: still the target");
            wall.layer = WorldLayers.Water;
            Assert.AreSame(near, interactor.Aim(eye.transform.position, eye.transform.forward), "Water in between: still the target");
            Object.DestroyImmediate(wall);

            near.transform.position = eye.transform.position + eye.transform.forward * (LookoutTargets.RayOutside + 0.5f);
            Physics.SyncTransforms();
            Assert.IsNull(interactor.Aim(eye.transform.position, eye.transform.forward), $"no target at {LookoutTargets.RayOutside} m");
        }

        class StubInteractable : MonoBehaviour, IInteractable
        {
            public bool Usable = true;
            public string Verb => "Open";
            public string Noun => "crate";
            public bool CanInteract(Interactor by) => Usable;
            public void Interact(Interactor by) { }
        }
    }
}
