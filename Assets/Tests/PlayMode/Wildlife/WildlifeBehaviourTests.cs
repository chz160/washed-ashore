using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using WashedAshore.Wildlife;
using static WashedAshore.Tests.Wildlife.WildlifeTestKit;

namespace WashedAshore.Tests.Wildlife
{
    /// <summary>
    /// A4 and A5 of poc-spec-washed-ashore-wildlife.md, with the numbers from wildlife-density-brief.md.
    /// Seed 101 is the authored population.
    /// </summary>
    public class WildlifeBehaviourTests
    {
        const int Seed = 101;
        const float SampleWindow = 0.2f;
        const float MaxMismatchSeconds = 1f; // tighter than the spec's 5 s; damping needs ~0.3 s
        const int MaxEpisodeSeconds = 5;     // spec A4: float or clip for longer than 5 s fails
        const float MaxNavMeshDelta = 0.5f;  // wl-qa (c): terrain height vs agent.nextPosition.y

        [SetUp]
        public void SetUp() => WildlifeTestKit.PinFrameStep();

        [TearDown]
        public void TearDown()
        {
            WildlifeRandom.OverrideSeed(null);
            Time.timeScale = 1f;
            WildlifeTestKit.UnpinFrameStep();
        }

        [UnityTest]
        public IEnumerator A0_AnimalsHaveNoBlockingCollidersAndStayOffTheSpawnLane()
        {
            yield return LoadWorld(Seed);
            var agents = WildlifeAgent.All.ToList();
            Assert.That(agents.Count, Is.InRange(6, 40), "Population outside the spec's 6-40 bound");
            var spawn = Population().PlayerSpawn;
            Vector3 fwd = spawn.forward;
            foreach (var a in agents)
            {
                Assert.IsFalse(a.GetComponentsInChildren<Collider>().Any(c => !c.isTrigger), $"{a.name} has a solid collider");
                Vector3 rel = a.transform.position - spawn.position;
                float along = Vector3.Dot(rel, fwd);
                float side = Vector3.Cross(fwd, rel).y;
                Assert.IsFalse(along > -2f && along < 36f && Mathf.Abs(side) < 5f, $"{a.name} sits in the W lane");
            }
        }

        /// <summary>WL-BUG-1 regression. Only a player build reproduces the load-order bug (the Editor already has
        /// the NavMeshSurface data loaded), so run this in the player path too.</summary>
        [UnityTest]
        public IEnumerator A0b_EveryAgentIsOnTheNavMeshAtLoadAndAfterFiveSeconds([Values(101, 202, 303)] int seed)
        {
            var failures = new List<string>();
            void Capture(string msg, string stack, LogType type)
            {
                if (msg.Contains("Failed to create agent")) failures.Add(msg);
            }
            Application.logMessageReceived += Capture;
            try
            {
                yield return LoadWorld(seed); // two frames: every WildlifeAgent.Start has run (202/303 re-plan in Awake)
                var agents = WildlifeAgent.All.ToList();
                Assert.IsNotEmpty(agents);
                var offAtLoad = agents.Where(a => !a.Agent.enabled || !a.Agent.isOnNavMesh).Select(a => a.name).ToList();
                yield return new WaitForSeconds(5f);
                var offLater = agents.Where(a => !a.Agent.isOnNavMesh).Select(a => a.name).ToList();
                Assert.IsEmpty(failures, "NavMeshAgent creation failed during load");
                Assert.IsEmpty(offAtLoad, "Off the NavMesh right after load");
                Assert.IsEmpty(offLater, "Off the NavMesh after 5 s");
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator A4_MovementMatchesAnimationAndGroundForSixtySeconds()
        {
            yield return LoadWorld(Seed);
            var player = Player();
            yield return WaitGrounded(player);
            var pop = Population();
            var route = WildlifePopulation.Route(pop.Tuning, pop.PlayerSpawn.position);
            var walker = new Walker(player, pop.Tuning.sighting.walkSpeed);
            int leg = 1;
            walker.SetGoal(route[leg]);

            var agents = WildlifeAgent.All.ToList();
            var probes = agents.Select(a => new WildlifeA4Probe(a)).ToList();
            var terrain = Terrain.activeTerrain;
            float elapsed = 0f, window = 0f, second = 0f;
            while (elapsed < 60f)
            {
                yield return null;
                float dt = Time.deltaTime;
                elapsed += dt;
                window += dt;
                second += dt;
                // Walk the route so the test covers wander, alert and flee, not just grazing.
                if (walker.Step(dt)) { leg = leg % (route.Count - 1) + 1; walker.SetGoal(route[leg]); }
                walker.Face(0f);
                if (window >= SampleWindow)
                {
                    foreach (var p in probes) p.SampleMotion(window, player.transform.position);
                    window = 0f;
                }
                if (second >= 1f)
                {
                    foreach (var p in probes) p.SampleSecond(terrain);
                    second -= 1f;
                }
            }

            foreach (var p in probes) Debug.Log("A4 " + p);
            var control = probes[0].NavMeshControl(terrain);
            string controlText = System.FormattableString.Invariant(
                $"agent={probes[0].Name} lifted1m={control.lifted:F3} offEdge0.5m={control.offEdge:F3} rawNextPosVsTerrain={control.rawNextPosDelta:F3}");
            Debug.Log($"A4 (c) method: {WildlifeA4Probe.NavMeshMethod}; positive control: {controlText}");
            WriteA4Report(probes, controlText);
            Assert.That(control.lifted, Is.InRange(0.9f, 1.1f), $"(c) control: lifted point should read ~1.0 m ({controlText})");
            Assert.Greater(control.offEdge, 0.1f, $"(c) control: off-mesh point should read non-zero ({controlText})");
            int moving = probes.Sum(t => t.MovingSamples), still = probes.Sum(t => t.StillSamples);
            int all = probes.Sum(t => t.Samples);
            Debug.Log($"A4 summary: agents={probes.Count} samples={all} moving={moving} still={still}");
            Assert.Greater(moving, all / 10, "Too little movement to prove anything");
            Assert.Greater(still, all / 10, "Too little idling to prove anything");
            foreach (var p in probes)
            {
                Assert.AreEqual(0, p.OffNavMeshSamples, $"{p.Name} left the NavMesh");
                Assert.LessOrEqual(p.MaxMismatch, MaxMismatchSeconds, $"{p.Name}: velocity and animation disagreed ({p})");
                Assert.LessOrEqual(p.MaxClipRunSeconds, MaxEpisodeSeconds, $"{p.Name} clipped > {WildlifeA4Probe.MaxClip} m too long ({p})");
                Assert.LessOrEqual(p.MaxFloatRunSeconds, MaxEpisodeSeconds, $"{p.Name} floated > {WildlifeA4Probe.MaxFloat} m too long ({p})");
                Assert.AreEqual(0, p.StuckEvents, $"{p.Name} stuck ({p})");
                Assert.LessOrEqual(p.MaxNavMeshDelta, MaxNavMeshDelta, $"{p.Name}: terrain and NavMesh heights diverged ({p})");
                p.AssertFacing("A4");
                // WL-BUG-7: with no runtime yaw left, the head bone itself must sit at +Z in the model frame.
                Assert.Greater(p.HeadDirInModelFrame().z, 0.9f, $"{p.Name}: mesh does not face +Z (import facing fix missing?) ({p})");
            }
        }

        static void WriteA4Report(List<WildlifeA4Probe> probes, string navMeshControl)
        {
            var sb = new System.Text.StringBuilder("{\n  \"method\": \"footprint = 4 lowest corners of SkinnedMeshRenderer.localBounds in rootBone space (tilts with the model) + their centre; sampled 1 Hz over 60 s\",\n");
            sb.Append("  \"facingMethod\": \"model forward = flat(Head bone - root); while moving > 0.5 m/s, dot(model forward, displacement) >= 0.7 in >= 95% of samples per agent; while Alert/watching (>= 1 s in state, still), dot(model forward, to player) >= 0.7; positive control in A4c\",\n");
            sb.Append($"  \"navMeshDeltaMethod\": \"{WildlifeA4Probe.NavMeshMethod}\",\n  \"navMeshPositiveControl\": \"{navMeshControl}\",\n  \"species\": [\n");
            var groups = probes.GroupBy(p => p.Species).ToList();
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                sb.Append(System.FormattableString.Invariant(
                    $"    {{\"species\": \"{g.Key}\", \"agents\": {g.Count()}, \"maxClipDepth\": {g.Max(p => p.MaxClipDepth):F3}, \"maxClipRunSec\": {g.Max(p => p.MaxClipRunSeconds)}, "));
                sb.Append(System.FormattableString.Invariant(
                    $"\"maxFloatGap\": {g.Max(p => p.MaxFloatGap):F3}, \"maxFloatRunSec\": {g.Max(p => p.MaxFloatRunSeconds)}, \"stuckEvents\": {g.Sum(p => p.StuckEvents)}, "));
                sb.Append(System.FormattableString.Invariant(
                    $"\"maxNavMeshDeltaY\": {g.Max(p => p.MaxNavMeshDelta):F3}, \"maxMismatchSec\": {g.Max(p => p.MaxMismatch):F2}, \"offNavMeshSamples\": {g.Sum(p => p.OffNavMeshSamples)}, "));
                int fs = g.Sum(p => p.FacingSamples), ws = g.Sum(p => p.WatchSamples);
                sb.Append(System.FormattableString.Invariant(
                    $"\"facingMovingSamples\": {fs}, \"facingShareOk\": {(fs == 0 ? 1f : (float)g.Sum(p => p.FacingOk) / fs):F3}, \"facingMinDot\": {g.Min(p => p.MinMovingDot):F2}, "));
                sb.Append(System.FormattableString.Invariant(
                    $"\"watchSamples\": {ws}, \"watchShareOk\": {(ws == 0 ? 1f : (float)g.Sum(p => p.WatchOk) / ws):F3}, \"watchMinDot\": {g.Min(p => p.MinWatchDot):F2}, "));
                var basis = g.First().HeadDirInModelFrame();
                sb.Append(System.FormattableString.Invariant(
                    $"\"basisHeadDirInModelFrame\": {{\"x\": {basis.x:F2}, \"z\": {basis.z:F2}"));
                sb.Append("}}"); // closing braces kept out of the format string: "}}" right after ":F2" is read as part of the format
                sb.Append(i + 1 < groups.Count ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "TestResults"));
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "wildlife-a4.json"), sb.ToString());
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator A5a_DeerHerdFleesAndDistanceGrows() => Approach(WildlifeSpecies.Deer);

        [UnityTest, Timeout(180000)]
        public IEnumerator A5b_FoxFleesAndDistanceGrows() => Approach(WildlifeSpecies.Fox);

        [UnityTest, Timeout(180000)]
        public IEnumerator A5c_WolvesKeepTheirDistance() => Approach(WildlifeSpecies.Wolf);

        /// <summary>WL-BUG-7 positive control: one deer is deliberately turned 180 degrees (ExtraModelYaw) and must
        /// FAIL the facing assertion while its herd flees; its unflipped herd-mates must pass it.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator A4c_FacingCheckCatchesAFlippedModel() => Approach(WildlifeSpecies.Deer, flipControl: true);

        IEnumerator Approach(WildlifeSpecies species, bool flipControl = false)
        {
            yield return LoadWorld(Seed);
            var player = Player();
            yield return WaitGrounded(player);
            var pop = Population();
            var herd = pop.GetComponentsInChildren<WildlifeHerd>().First(h => h.Species == species);
            var members = herd.Members.ToList();
            Assert.IsNotEmpty(members);
            var t = members[0].SpeciesTuning;
            bool prey = t.response == ThreatResponse.Flee;
            float startDistance = (prey ? t.alertDistance : t.releaseDistance) + 12f;

            Teleport(player, StartPoint(pop.Ground, herd.Centroid, startDistance, pop.Ground.ToWorld(pop.Tuning.placement.terrainCentre)));
            yield return new WaitForSeconds(1.5f);
            float calmWait = 0f;
            while (members.Any(m => m.IsEscaping) && calmWait < 12f) { calmWait += Time.deltaTime; yield return null; }
            Assert.IsFalse(members.Any(m => m.IsEscaping), $"{species} still escaping before the approach");

            if (flipControl)
            {
                Assert.GreaterOrEqual(members.Count, 2, "Control needs a flipped and an unflipped herd member");
                members[0].ExtraModelYaw = 180f; // deliberately run this one tail-first
            }
            var probes = members.Select(m => new WildlifeA4Probe(m)).ToList();
            float window = 0f;

            var walker = new Walker(player, pop.Tuning.sighting.walkSpeed);
            var escaped = new HashSet<WildlifeAgent>();
            float elapsed = 0f, repath = 0f, minD = float.MaxValue, dAtTrigger = -1f, sinceTrigger = 0f, dEnd = 0f;
            float limit = prey ? 30f : 25f;
            float trace = 0f;
            var log = new System.Text.StringBuilder();
            while (elapsed < limit)
            {
                float d = members.Min(m => Flat(player.transform.position, m.transform.position));
                minD = Mathf.Min(minD, d);
                foreach (var m in members) if (m.IsEscaping) escaped.Add(m);
                if (dAtTrigger < 0f && escaped.Count > 0) dAtTrigger = d;
                if (dAtTrigger >= 0f) sinceTrigger += Time.deltaTime;
                dEnd = d;
                trace -= Time.deltaTime;
                if (trace <= 0f)
                {
                    var m0 = members[0];
                    log.Append($" [{elapsed:F0}s d={d:F1} {m0.Current} v={m0.Agent.velocity.magnitude:F1}]");
                    trace = 1f;
                }
                if (prey && sinceTrigger >= 6f) break;

                repath -= Time.deltaTime;
                if (repath <= 0f)
                {
                    var nearest = members.OrderBy(m => Flat(player.transform.position, m.transform.position)).First();
                    walker.SetGoal(nearest.transform.position);
                    repath = 0.5f;
                }
                walker.Step(Time.deltaTime);
                walker.Face(0f);
                elapsed += Time.deltaTime;
                window += Time.deltaTime;
                if (window >= SampleWindow)
                {
                    foreach (var p in probes) p.SampleMotion(window, player.transform.position);
                    window = 0f;
                }
                yield return null;
            }

            foreach (var p in probes) Debug.Log($"{(flipControl ? "A4c" : "A5")} facing {p}");
            if (flipControl)
            {
                var flipped = probes[0];
                members[0].ExtraModelYaw = 0f;
                Assert.GreaterOrEqual(flipped.FacingSamples, WildlifeA4Probe.MinFacingSamples, "Control: the flipped deer never moved");
                Assert.Less(flipped.FacingShare, WildlifeA4Probe.MinFacingShare,
                    $"Control: a deer turned 180 degrees PASSED the facing check, so the check can't see the bug ({flipped})");
                foreach (var p in probes.Skip(1)) p.AssertFacing("A4c (unflipped herd-mate)");
                yield break;
            }

            Debug.Log($"A5 {species}: trigger={t.triggerDistance} dAtTrigger={dAtTrigger:F1} dEnd={dEnd:F1} " +
                      $"minD={minD:F1} escaped={escaped.Count}/{members.Count} stalls={walker.StallRecoveries} trace:{log}");
            foreach (var p in probes) p.AssertFacing($"A5 {species}");
            Assert.AreEqual(members.Count, escaped.Count, $"Not every {species} reacted (the group reacts as one)");
            Assert.LessOrEqual(dAtTrigger, t.triggerDistance + 0.5f, $"{species} reacted before the player was within {t.triggerDistance} m");
            Assert.GreaterOrEqual(dAtTrigger, t.triggerDistance - 3f, $"{species} reacted late");
            if (prey)
                Assert.Greater(dEnd, dAtTrigger, $"{species}: distance did not grow while the player kept walking at them");
            else
                Assert.GreaterOrEqual(minD, t.triggerDistance - 5f, $"Wolves let the player within {minD:F1} m");
        }

        /// <summary>A NavMesh point at <paramref name="distance"/> from the group, preferring the side away
        /// from the map centre so the animals have room to run inward.</summary>
        static Vector3 StartPoint(HabitatGround ground, Vector3 centroid, float distance, Vector2 centre)
        {
            Vector3 outward = centroid - new Vector3(centre.x, centroid.y, centre.y);
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.forward;
            foreach (float a in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f })
            {
                Vector3 p = centroid + Quaternion.Euler(0f, a, 0f) * outward * distance;
                p.y = ground.Height(p); // sample at ground height, not the herd's
                if (ground.EdgeDistance(p) < 12f || !ground.IsLand(p)) continue;
                if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)) return hit.position;
            }
            Assert.Fail($"No start point {distance} m from {centroid}");
            return default;
        }
    }
}
