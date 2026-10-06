using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;
using WashedAshore.Birds;
using WashedAshore.Tests.Wildlife;
using static WashedAshore.Tests.Birds.BirdTestKit;

namespace WashedAshore.Tests.Birds
{
    /// <summary>
    /// B3, B4 and B5 of poc-spec-washed-ashore-birds.md, with the numbers from bird-density-brief.md (BirdTuning).
    /// Seed 101 is the baked layout. Every check has a positive control (the "c"/"d" tests) that breaks the behaviour
    /// on purpose through a test hook and asserts the same check reports it, so no check passes by construction.
    /// </summary>
    public class BirdBehaviourTests
    {
        const int Seed = 101;
        const float Window = 0.1f;

        [SetUp]
        public void SetUp() => WildlifeTestKit.PinFrameStep();

        [TearDown]
        public void TearDown()
        {
            Reset();
            WildlifeTestKit.UnpinFrameStep();
        }

        // ---- B3: ground cycle -----------------------------------------------------------------------

        [UnityTest, Timeout(180000)]
        public IEnumerator B3_RobinsCycleGroundClipsOutOfStepWithNeighbours()
        {
            yield return LoadWorld(Seed);
            var pop = Population();
            var robins = pop.Robins.ToList();
            Assert.AreEqual(pop.Plan.patches.Sum(p => p.size), robins.Count, "Robin count differs from the configured population");
            Assert.That(robins.Count, Is.InRange(pop.Tuning.robinTotal.x, pop.Tuning.robinTotal.y), "Robin count outside the spec bounds");
            foreach (var r in robins) r.FlushDisabled = true; // ground behaviour only; B4 covers the flush
            var route = Route();
            var check = new GroundCycleCheck(robins, pop.Tuning.robin.neighbourRadius, route, pop.Tuning.robin.offTrail);
            yield return Run(60f, check.Sample);
            Debug.Log($"B3 {check}");
            foreach (var kv in check.Clips) Debug.Log($"B3 {kv.Key.name}: {string.Join(",", kv.Value.OrderBy(x => x))}");
            Assert.Greater(check.NeighbourPairSamples, 0, "No two robins were ever neighbours; the lockstep check proved nothing");
            var failures = check.Failures();
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Positive control: a robin mirrored onto its neighbour must be caught in lockstep, a robin locked to
        /// one clip must be caught playing too few clips, and a robin moved onto the route must be caught by A4.</summary>
        [UnityTest, Timeout(120000)]
        public IEnumerator B3c_GroundCheckCatchesLockstepASingleClipAndTheTrail()
        {
            yield return LoadWorld(Seed);
            var pop = Population();
            var robins = pop.Robins.ToList();
            Assert.GreaterOrEqual(robins.Count, 4, "Control needs four robins");
            foreach (var r in robins) r.FlushDisabled = true;
            var leader = robins[0];
            var mirror = robins[1];
            var locked = robins[2];
            var onTrail = robins[3];
            mirror.transform.position = leader.transform.position + leader.transform.right * 1f;
            mirror.MirrorOf = leader;
            locked.LockedClip = pop.Tuning.robin.bouts[2].states[0];
            var route = Route();
            onTrail.MirrorOf = leader; // a mirroring robin holds whatever position it is given
            onTrail.transform.position = route[1];
            var check = new GroundCycleCheck(robins, pop.Tuning.robin.neighbourRadius, route, pop.Tuning.robin.offTrail);
            yield return Run(20f, check.Sample);
            Debug.Log($"B3c {check}");
            var failures = check.Failures();
            string all = string.Join("\n", failures);
            Assert.IsTrue(failures.Any(f => f.Contains("lockstep") && f.Contains(leader.name) && f.Contains(mirror.name)),
                "Control: a robin mirroring its neighbour PASSED the lockstep check:\n" + all);
            Assert.IsTrue(failures.Any(f => f.StartsWith(locked.name) && f.Contains("distinct")),
                "Control: a robin locked to one clip PASSED the variety check:\n" + all);
            Assert.IsTrue(failures.Any(f => f.StartsWith(onTrail.name) && f.Contains("keep-out")),
                "Control: a robin on the route PASSED the A4 check:\n" + all);
        }

        // ---- B4: flush ------------------------------------------------------------------------------

        [UnityTest, Timeout(180000)]
        public IEnumerator B4_RobinFlushesClimbsAwayAndLandsClear()
        {
            var probes = new List<FlightProbe>();
            yield return Approach(probes, robin => { });
            var t = Population().Tuning;
            Debug.Log($"B4 target {probes[0]}");
            foreach (var p in probes.Skip(1).Where(p => p.Flushed)) Debug.Log($"B4 other {p}");
            Assert.IsFalse(probes[0].Social, "The target flushed socially, so the A1 trigger distance wasn't tested");
            var failures = probes.Where(p => p == probes[0] || p.Flushed)
                .SelectMany(p => p.Failures(t.robin.flightInitiationDistance, t.sighting.fidBelow, t.sighting.fidAbove, t.robin.landMinFromRoute)).ToList();
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Positive control: the flushing robin is turned 180 degrees and a box and a crown are dropped on its
        /// path after it has planned it; the facing, overlap and crown checks must all report it. The A1 band is also
        /// re-evaluated as if the FID were 3 m shorter, and the escape minimums 1000x higher, which must all fail.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator B4c_FlightCheckCatchesFacingOverlapCrownAndTrigger()
        {
            var probes = new List<FlightProbe>();
            GameObject box = null;
            yield return Approach(probes, robin => robin.ExtraModelYaw = 180f, (robin, crowns) =>
            {
                if (box || robin.Current != RobinAgent.State.Fly) return;
                Vector3 ahead = robin.BodyCenter + robin.Velocity.normalized * 1.5f;
                box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.position = ahead;
                box.transform.localScale = Vector3.one * 1.5f;
                Physics.SyncTransforms();
                crowns.Inject(ahead, 1f, ahead.y - 2f, ahead.y + 2f);
            });
            if (box) Object.Destroy(box);
            var p = probes[0];
            var t = Population().Tuning;
            Debug.Log($"B4c {p}");
            var failures = p.Failures(t.robin.flightInitiationDistance, t.sighting.fidBelow, t.sighting.fidAbove, t.robin.landMinFromRoute);
            var shifted = p.Failures(t.robin.flightInitiationDistance - 3f, t.sighting.fidBelow, t.sighting.fidAbove, t.robin.landMinFromRoute);
            string all = string.Join("\n", failures);
            Assert.IsNotNull(box, "Control: the robin never reached Fly, so no obstacle was placed");
            Assert.IsTrue(failures.Any(f => f.Contains("faced its flight")), "Control: a robin flying tail-first PASSED the facing check:\n" + all);
            Assert.IsTrue(failures.Any(f => f.Contains("overlapped")), "Control: a box on the flight path PASSED the overlap check:\n" + all);
            Assert.IsTrue(failures.Any(f => f.Contains("crown")), "Control: a crown on the flight path PASSED the crown check:\n" + all);
            Assert.IsTrue(shifted.Any(f => f.Contains("A1 band")), "Control: a flush 3 m outside the FID PASSED the A1 check:\n" + string.Join("\n", shifted));
            // The same flight judged against minimums 1000x higher must fail growth, height, landing distance and route distance.
            var strict = p.Failures(t.robin.flightInitiationDistance, t.sighting.fidBelow, t.sighting.fidAbove, t.robin.landMinFromRoute, 1000f);
            string strictAll = string.Join("\n", strict);
            Assert.IsTrue(p.Landed || p.Despawned, "Control: the robin neither landed nor despawned");
            Assert.IsTrue(p.Despawned || strict.Any(f => f.Contains("grew only")), "Control: the distance-growth check can't fail:\n" + strictAll);
            Assert.IsTrue(strict.Any(f => f.Contains("gained only")), "Control: the height-gain check can't fail:\n" + strictAll);
            Assert.IsTrue(!p.Landed || strict.Any(f => f.Contains("m away (<")), "Control: the landing-distance check can't fail:\n" + strictAll);
            Assert.IsTrue(!p.Landed || strict.Any(f => f.Contains("from the route")), "Control: the landing route-distance check can't fail:\n" + strictAll);
        }

        /// <summary>Positive control: a robin that ignores the player must fail the flush check.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator B4d_FlightCheckCatchesARobinThatNeverFlies()
        {
            var probes = new List<FlightProbe>();
            yield return Approach(probes, robin => robin.FlushDisabled = true, timeout: 12f);
            var t = Population().Tuning;
            var failures = probes[0].Failures(t.robin.flightInitiationDistance, t.sighting.fidBelow, t.sighting.fidAbove, t.robin.landMinFromRoute);
            Debug.Log($"B4d {probes[0]}");
            Assert.IsTrue(failures.Any(f => f.Contains("never flushed")), "Control: a robin that never flew PASSED the flush check:\n" + string.Join("\n", failures));
        }

        /// <summary>Picks a robin of a meadow patch (room to land), walks the player at it from FID + 15 m, and samples
        /// every robin's flight until the target lands or despawns.</summary>
        IEnumerator Approach(List<FlightProbe> probes, System.Action<RobinAgent> setup,
            System.Action<RobinAgent, TestCrowns> perFrame = null, float timeout = 30f)
        {
            yield return LoadWorld(Seed);
            var player = WildlifeTestKit.Player();
            yield return WildlifeTestKit.WaitGrounded(player);
            var pop = Population();
            var t = pop.Tuning.robin;
            var route = Route();
            var target = pop.Robins.Where(r => r.Patch.type == PatchType.Meadow).OrderBy(r => r.name).First();
            foreach (var r in pop.Robins.Where(r => r.Patch == target.Patch && r != target)) r.FlushDisabled = true; // keep the target's flush player-triggered
            setup(target);

            WildlifeTestKit.Teleport(player, StartPoint(target.transform.position, t.flightInitiationDistance + 15f, route));
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(target.IsFlying || target.Despawned, "Target robin was not on the ground before the approach");

            var crowns = new TestCrowns(WildlifeTestKit.Ground());
            int mask = ObstacleMask();
            var cam = player.GetComponentInChildren<Camera>();
            probes.Add(new FlightProbe(target));
            probes.AddRange(pop.Robins.Where(r => r != target).Select(r => new FlightProbe(r)));
            var walker = new WildlifeTestKit.Walker(player, 4f);
            float elapsed = 0f, repath = 0f;
            while (elapsed < timeout && !probes[0].Done)
            {
                foreach (var p in probes) p.Sample(Time.deltaTime, player.transform, cam, mask, crowns, route);
                if (!probes[0].Flushed || probes[0].SinceFlush < 3f)
                {
                    if ((repath -= Time.deltaTime) <= 0f) { walker.SetGoal(target.transform.position); repath = 0.5f; }
                    walker.Step(Time.deltaTime);
                    walker.Face(0f);
                }
                perFrame?.Invoke(target, crowns);
                yield return null;
                elapsed += Time.deltaTime;
            }
        }

        /// <summary>A NavMesh point <paramref name="distance"/> from the robin with a path to it, preferring the side
        /// towards the route (the player comes from the trail).</summary>
        static Vector3 StartPoint(Vector3 robin, float distance, List<Vector3> route)
        {
            var nearest = route.OrderBy(p => WildlifeTestKit.Flat(p, robin)).First();
            Vector3 toward = nearest - robin;
            toward.y = 0f;
            toward = toward.sqrMagnitude > 1f ? toward.normalized : Vector3.forward;
            foreach (float a in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f })
            {
                Vector3 p = robin + Quaternion.Euler(0f, a, 0f) * toward * distance;
                p.y = TerrainY(p);
                var navPath = new NavMeshPath();
                if (NavMesh.SamplePosition(p, out var hit, 3f, NavMesh.AllAreas)
                    && NavMesh.SamplePosition(robin, out var rh, 3f, NavMesh.AllAreas)
                    && NavMesh.CalculatePath(hit.position, rh.position, NavMesh.AllAreas, navPath)
                    && navPath.status == NavMeshPathStatus.PathComplete)
                    return hit.position;
            }
            Assert.Fail($"No NavMesh start point {distance} m from {robin}");
            return default;
        }

        // ---- B5: flocks -----------------------------------------------------------------------------

        [UnityTest, Timeout(180000)]
        public IEnumerator B5_FlocksHoldTheBandSpacingAndWingRule()
        {
            yield return LoadWorld(Seed);
            var pop = Population();
            Assert.That(pop.Flocks.Count, Is.InRange(pop.Tuning.flockCount.x, pop.Tuning.flockCount.y), "Flock count outside the spec bounds");
            for (int i = 0; i < pop.Flocks.Count; i++)
            {
                Assert.AreEqual(pop.Plan.flocks[i].size, pop.Flocks[i].Birds.Count, $"{pop.Flocks[i].name} size differs from the plan");
                Assert.That(pop.Flocks[i].Birds.Count, Is.InRange(pop.Tuning.flockSize.x, pop.Tuning.flockSize.y), $"{pop.Flocks[i].name} size outside the spec bounds");
            }
            var check = new FlockCheck(pop.Flocks.SelectMany(f => f.Birds), pop.Tuning, new TestCrowns(WildlifeTestKit.Ground()));
            yield return Run(60f, check.Sample);
            Debug.Log($"B5 {check}");
            var failures = check.Failures(pop.Flocks);
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>Positive control on the first flock: slots collapsed, wings inverted, one bird turned 180 degrees,
        /// the flock dropped to about 2 m above the ground, and a crown injected over the orbit. Each check must report it.</summary>
        [UnityTest, Timeout(180000)]
        public IEnumerator B5c_FlockCheckCatchesSpacingWingsFacingBandAndCrown()
        {
            yield return LoadWorld(Seed);
            var pop = Population();
            var flock = pop.Flocks[0];
            var crowns = new TestCrowns(WildlifeTestKit.Ground());
            flock.CollapseSlots = true;
            foreach (var b in flock.Birds) b.InvertWings = true;
            flock.Birds[0].ExtraModelYaw = 180f;
            var p0 = flock.Birds[0].transform.position;
            flock.AltitudeOffset = TerrainY(p0) + 2f - p0.y;
            crowns.Inject(flock.Lead, 200f, -1000f, 10000f); // a crown over the whole orbit and commute
            var check = new FlockCheck(flock.Birds, pop.Tuning, crowns);
            yield return Run(45f, check.Sample);
            Debug.Log($"B5c {check}");
            var failures = check.Failures(new[] { flock });
            string all = string.Join("\n", failures);
            Assert.IsTrue(failures.Any(f => f.StartsWith("Spacing under")), "Control: collapsed slots PASSED the spacing check:\n" + all);
            Assert.IsTrue(failures.Any(f => f.StartsWith("Wing meshes")), "Control: collapsed slots PASSED the mesh-overlap check:\n" + all);
            Assert.IsTrue(failures.Any(f => f.StartsWith("Flap rule")), "Control: inverted wings PASSED the flap rule:\n" + all);
            Assert.IsTrue(failures.Any(f => f.Contains(flock.Birds[0].name) && f.Contains("faced its flight")), "Control: a bird flying tail-first PASSED the facing check:\n" + all);
            Assert.IsTrue(failures.Any(f => f.StartsWith("Outside the")), "Control: a flock near the ground PASSED the band check:\n" + all);
            Assert.IsTrue(failures.Any(f => f.StartsWith("Below crown top")), "Control: a bird under a crown PASSED the crown check:\n" + all);
        }

        static IEnumerator Run(float seconds, System.Action<float> sample)
        {
            float elapsed = 0f, window = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.deltaTime;
                window += Time.deltaTime;
                if (window >= Window) { sample(window); window = 0f; }
            }
        }
    }
}
