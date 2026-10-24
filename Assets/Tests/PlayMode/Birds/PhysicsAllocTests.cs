using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using WashedAshore.Birds;
using WashedAshore.Gameplay;
using WashedAshore.World;
using static WashedAshore.Tests.Birds.BirdTestKit;
using WashedAshore.Tests.Wildlife;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace WashedAshore.Tests.Birds
{
    /// <summary>
    /// #44 A3: the world-bounds clamp pass and the robin flush planner allocate nothing per call once warm. Uses Unity's
    /// Is.Not.AllocatingGCMemory constraint (GC.GetAllocatedBytesForCurrentThread reads 0 in this Editor even for an
    /// allocating call, so it is not used). Counts managed allocations only, no ms or fps. The control proves the check is
    /// alive: Physics.SphereCastAll, the call this change removed, must be reported as allocating by the same check.
    /// </summary>
    public class PhysicsAllocTests
    {
        const int Seed = 101;

        [SetUp]
        public void SetUp() => WildlifeTestKit.PinFrameStep();

        [TearDown]
        public void TearDown()
        {
            Reset();
            WildlifeTestKit.UnpinFrameStep();
        }

        /// <summary>True if running <paramref name="a"/> <paramref name="calls"/> times, after a warm-up, allocates managed memory.</summary>
        static bool Allocates(Action a, int calls)
        {
            for (int i = 0; i < 20; i++) a(); // warm up
            try
            {
                Assert.That(() => { for (int i = 0; i < calls; i++) a(); }, Is.Not.AllocatingGCMemory());
                return false;
            }
            catch (AssertionException)
            {
                return true;
            }
        }

        [UnityTest]
        public IEnumerator Check_IsAlive_SphereCastAllIsReportedAsAllocating()
        {
            yield return LoadWorld(Seed);
            Vector3 o = Camera.main ? Camera.main.transform.position : Vector3.up * 10f;
            void Cast() => Physics.SphereCastAll(o, 0.3f, Vector3.down, 50f, WorldLayers.WorldSolidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < 20; i++) Cast(); // warm up, as for the checks below
            // A dead check fails here: it must see the allocation this change removed.
            Assert.That(() => { for (int i = 0; i < 50; i++) Cast(); }, Is.AllocatingGCMemory(),
                "the allocation check reads clean for SphereCastAll: it is blind, nothing below can be trusted");
        }

        [UnityTest]
        public IEnumerator WorldBoundsClamp_Tick_DoesNotAllocate_With20RegisteredBodies()
        {
            yield return LoadWorld(Seed);
            var clamp = UnityEngine.Object.FindAnyObjectByType<WorldBoundsClamp>();
            Assert.IsNotNull(clamp);
            var rule = clamp.Rule;
            var root = new GameObject("AllocBodies");
            for (int i = 0; i < 20; i++)
            {
                var go = new GameObject("Body_" + i);
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(i, 0f, rule.LineZAt(i) - 50f); // south of the line: a pass that snaps nothing
                go.AddComponent<WorldBoundsBody>();
            }
            yield return null;
            Assert.GreaterOrEqual(WorldBoundsClamp.RegisteredBodies, 20);
            bool allocates = Allocates(clamp.Tick, 200);
            Debug.Log($"PhysicsAllocTests: WorldBoundsClamp.Tick x200 with {WorldBoundsClamp.RegisteredBodies} bodies, {WorldBoundsClamp.RegisteredPlayers} player(s): allocates={allocates}");
            UnityEngine.Object.Destroy(root);
            Assert.IsFalse(allocates, "WorldBoundsClamp.Tick allocated");
        }

        [UnityTest]
        public IEnumerator RobinFlightPlanner_Plan_DoesNotAllocate_AtEightThreatBearings()
        {
            yield return LoadWorld(Seed);
            yield return new WaitForSeconds(1f);
            Assert.IsNotEmpty(RobinAgent.All, "no robins");
            var robin = RobinAgent.All[0];
            T Field<T>(string n) => (T)typeof(RobinAgent).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(robin);
            var site = Field<BirdPlacementRules.Site>("site");
            var ground = Field<BirdTerrain>("ground");
            var t = Field<RobinTuning>("t");
            int mask = Field<int>("mask");
            var player = GameObject.FindGameObjectWithTag("Player").transform;
            var path = new List<Vector3>(64);
            var rng = new System.Random(4401);
            Vector3 start = robin.transform.position;
            int bearing = 0;
            void Flush()
            {
                float a = (bearing++ % 8) * 45f * Mathf.Deg2Rad;
                Vector3 threat = start + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 6f;
                RobinFlightPlanner.Plan(site, ground, t, rng, start, threat, player, mask, path);
            }
            int fillsBefore = RobinFlightPlanner.FullBufferHits; // static counter: assert the change across the measured calls
            int castsBefore = RobinFlightPlanner.CastCount;
            bool allocates = Allocates(Flush, 160);
            Debug.Log($"PhysicsAllocTests: RobinFlightPlanner.Plan x160 over 8 bearings: allocates={allocates}, casts={RobinFlightPlanner.CastCount - castsBefore}, full-buffer hits={RobinFlightPlanner.FullBufferHits - fillsBefore}, most hits in one cast={RobinFlightPlanner.MaxCastHits} of {RobinFlightPlanner.CastBufferSize}");
            Assert.Greater(RobinFlightPlanner.CastCount, castsBefore, "no flight-segment cast ran during the 160 plans, so the measurement says nothing about the cast");
            Assert.AreEqual(fillsBefore, RobinFlightPlanner.FullBufferHits, "a real flush cast filled the buffer");
            Assert.IsFalse(allocates, "RobinFlightPlanner.Plan allocated");
        }
    }
}
