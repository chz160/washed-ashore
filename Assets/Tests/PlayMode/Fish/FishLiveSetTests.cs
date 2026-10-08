using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Birds;
using WashedAshore.Fish;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// Spec F9 (live set follows the player; gate/fish-technique tests) and F10 (fish never collide, robins never target
    /// them, fish off leaves birds and wildlife alone): no live cell beyond simRadiusOut unless still in view, no group
    /// appears or vanishes where the camera could see it, a disabled terrain tile takes its fish with it, no Collider or
    /// Rigidbody anywhere under the Fish root, and robins' obstacle casts find nothing at fish positions.
    /// </summary>
    public class FishLiveSetTests
    {
        const int Seed = 202;

        [TearDown]
        public void TearDown() => FishTestKit.Restore();

        [UnityTest]
        public IEnumerator F9_LiveSetFollowsThePlayer_AndNothingPopsInView()
        {
            yield return FishTestKit.Load(Seed, "f9");
            var pop = FishPopulation.Active;
            var t = pop.Tuning;
            var cam = Camera.main;
            Assert.IsNotNull(cam);
            var frustum = new Plane[6];
            int spawns = 0, despawns = 0, inViewChanges = 0, farCells = 0;
            var murk = pop.Murk;
            var visibleLastFrame = new HashSet<int>();
            void OnGroup(int g, Vector3 home, bool added)
            {
                if (added) spawns++; else despawns++;
                // Independent of the population's own check: the camera's frustum now and the shipped murk for a spawn; for
                // a despawn, whether the group could be seen on the last drawn frame (the group is gone by now).
                GeometryUtility.CalculateFrustumPlanes(cam, frustum);
                var view = new FishView(cam.transform.position, frustum, t, murk);
                if (added ? pop.World.GroupLive(g) && pop.World.GroupVisible(g, view) : visibleLastFrame.Contains(g)) inViewChanges++;
            }
            pop.GroupChanged += OnGroup;
            float length = FishTestKit.RouteLength();
            for (float time = 0f; time * FishTestKit.WalkSpeed < length; time += FishTestKit.Step)
            {
                FishTestKit.OnRoute(time * FishTestKit.WalkSpeed, out var feet, out var look);
                FishTestKit.Place(feet, look, WaterMode.Dry);
                yield return null;
                visibleLastFrame.Clear();
                GeometryUtility.CalculateFrustumPlanes(cam, frustum);
                var frameView = new FishView(cam.transform.position, frustum, t, murk);
                for (int g = 0; g < pop.World.GroupSlots; g++)
                    if (pop.World.GroupLive(g) && pop.World.GroupVisible(g, frameView)) visibleLastFrame.Add(g);
                for (int g = 0; g < pop.World.GroupSlots; g++)
                {
                    if (!pop.World.GroupLive(g)) continue;
                    long cell = pop.World.GroupCell(g);
                    int cx = (int)(uint)(cell & 0xFFFFFFFF), cz = (int)(cell >> 32);
                    float s = t.cellSize;
                    float dx = Mathf.Max(cx * s - feet.x, 0f, feet.x - (cx + 1) * s), dz = Mathf.Max(cz * s - feet.z, 0f, feet.z - (cz + 1) * s);
                    // A cell may outlive simRadiusOut only while its fish could be seen; allow one cell of travel for the tick.
                    if (Mathf.Sqrt(dx * dx + dz * dz) > t.simRadiusOut + FishTestKit.WalkSpeed * FishTestKit.Step * 4f) farCells++;
                }
            }
            pop.GroupChanged -= OnGroup;
            FishTestKit.Write("fish-f9-liveset.json", $"{{\"seed\":{Seed},\"spawns\":{spawns},\"despawns\":{despawns},\"inViewSpawnsOrDespawns\":{inViewChanges},\"groupFramesBeyondOut\":{farCells}}}");
            Assert.Greater(spawns, 0);
            Assert.Greater(despawns, 0, "walking 900 m must drop cells behind");
            Assert.AreEqual(0, inViewChanges, "N7: a group appeared or vanished where the camera could see it");
            Assert.AreEqual(0, farCells, "F9: live groups beyond simRadiusOut");
        }

        [UnityTest]
        public IEnumerator F9_DisablingATileUnderLiveFish_TakesThemAway()
        {
            yield return FishTestKit.Load(Seed, "f9tile");
            var pop = FishPopulation.Active;
            FishTestKit.OnRoute(0f, out var feet, out var look);
            for (int i = 0; i < 120; i++) { FishTestKit.Place(feet, look, WaterMode.Dry); yield return null; }
            Assert.Greater(pop.Count, 0, "no live fish at the route start");
            var tile = TerrainQuery.TileAt(pop.States[0].position);
            Assert.IsNotNull(tile);
            Vector3 o = tile.transform.position, size = tile.terrainData.size;
            bool OnTile(Vector3 p) => p.x >= o.x && p.x <= o.x + size.x && p.z >= o.z && p.z <= o.z + size.z;
            tile.gameObject.SetActive(false);
            try
            {
                for (int i = 0; i < 10; i++) { FishTestKit.Place(feet, look, WaterMode.Dry); yield return null; }
                int left = 0;
                for (int g = 0; g < pop.World.GroupSlots; g++) if (pop.World.GroupLive(g) && OnTile(pop.World.GroupHome(g))) left++;
                Assert.AreEqual(0, left, "groups whose home lost its tile must go");
            }
            finally { tile.gameObject.SetActive(true); }
        }

        [UnityTest]
        public IEnumerator F10_FishHaveNoColliders_AndRobinCastsNeverHitThem()
        {
            yield return FishTestKit.Load(Seed, "f10");
            var pop = FishPopulation.Active;
            FishTestKit.OnRoute(0f, out var feet, out var look);
            for (int i = 0; i < 120; i++) { FishTestKit.Place(feet, look, WaterMode.Swim); yield return null; }
            Assert.AreEqual(0, pop.GetComponentsInChildren<Collider>(true).Length, "Collider under the Fish root");
            Assert.AreEqual(0, pop.GetComponentsInChildren<Rigidbody>(true).Length, "Rigidbody under the Fish root");
            var robin = Object.FindAnyObjectByType<RobinAgent>();
            int mask = RobinFlightPlanner.ObstacleMask(Terrain.activeTerrain);
            int hits = 0;
            for (int i = 0; i < pop.Count; i++)
                foreach (var c in Physics.OverlapSphere(pop.States[i].position, 0.5f, mask, QueryTriggerInteraction.Collide))
                    if (c.transform.IsChildOf(pop.transform)) hits++;
            Assert.AreEqual(0, hits, "a robin obstacle cast found a fish");
            Assert.IsNotNull(robin, "robins must still be in World with fish on");
        }
    }
}
