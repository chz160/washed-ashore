using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Birds;
using WashedAshore.Wildlife;
using WashedAshore.Wildlife.Level;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// Water spec W4 layer audit (headless): Water is built-in layer 4, every invisible boundary collider (backstop and
    /// barrier extensions) is on WorldBounds, the player's layer still collides with both, and robin, sightline and
    /// NavMesh masks leave both out. Reads the saved World scene; re-run after the water quads exist (w-qa W11).
    /// </summary>
    public class LayerAuditTests
    {
        const string ScenePath = "Assets/Scenes/World.unity";
        const string WaterRoot = "Water"; // BellsBendWater.RootName (w-level): one top-level root, a Water_x{ix}_z{iz} child per tile

        int water, bounds, defaultLayer;
        readonly List<GameObject> temp = new List<GameObject>();

        [OneTimeSetUp]
        public void OpenWorld()
        {
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            water = WorldLayers.Water;
            bounds = WorldLayers.WorldBounds;
            defaultLayer = LayerMask.NameToLayer("Default");
            Physics.SyncTransforms();
        }

        [TearDown]
        public void RemoveProbes()
        {
            foreach (var go in temp) if (go) Object.DestroyImmediate(go);
            temp.Clear();
            Physics.SyncTransforms();
        }

        [Test]
        public void Layers_ResolveByName()
        {
            Assert.AreEqual(WorldLayers.BuiltInWater, LayerMask.NameToLayer(WorldLayers.WaterName), "Water must be built-in layer 4");
            Assert.GreaterOrEqual(LayerMask.NameToLayer(WorldLayers.WorldBoundsName), 8, "WorldBounds must be a user layer");
            Assert.AreNotEqual(water, bounds);
            Debug.Log($"LayerAudit: water={water} worldBounds={bounds} default={defaultLayer}");
        }

        [Test]
        public void PhysicsMatrix_PlayerLayerStillCollidesWithBoundsAndWater()
        {
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(defaultLayer, bounds), "Default x WorldBounds must collide (fence and backstop)");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(defaultLayer, water), "Default x Water changed; the W4 plan leaves the matrix alone");
        }

        [Test]
        public void BarrierExtensions_AreAllOnWorldBounds()
        {
            var root = GameObject.Find(BarrierBuilder.RootName);
            Assert.IsNotNull(root, $"No {BarrierBuilder.RootName} in World");
            var ext = root.GetComponentsInChildren<Transform>(true).Where(t => BarrierBuilder.ExtensionNames.Contains(t.name)).ToList();
            Assert.IsNotEmpty(ext, "No barrier extensions found");
            var wrong = ext.Where(t => t.gameObject.layer != bounds).Select(t => $"{t.name}@{t.gameObject.layer}").Take(10).ToList();
            var counts = string.Join(" ", BarrierBuilder.ExtensionNames.Select(n => $"{n}={ext.Count(t => t.name == n)}"));
            Debug.Log($"LayerAudit: barrier extensions {ext.Count} ({counts}), off WorldBounds {wrong.Count}");
            Assert.IsEmpty(wrong, "Extensions not on WorldBounds: " + string.Join(", ", wrong));
            foreach (var t in ext)
            {
                var c = t.GetComponent<Collider>();
                Assert.IsNotNull(c, $"{t.name} has no collider");
                Assert.IsNull(t.GetComponent<Renderer>(), $"{t.name} is visible");
            }
        }

        [Test]
        public void Backstop_IsOnWorldBounds()
        {
            var root = GameObject.Find("WorldBounds");
            Assert.IsNotNull(root, "No WorldBounds root in World");
            var cols = root.GetComponentsInChildren<Collider>(true);
            Assert.IsNotEmpty(cols);
            Assert.IsEmpty(cols.Where(c => c.gameObject.layer != bounds).Select(c => c.name), "Backstop colliders off WorldBounds");
            Debug.Log($"LayerAudit: backstop colliders {cols.Length}");
        }

        [Test]
        public void WaterObjects_AreOnWaterLayerWithNoSolidCollider()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == WaterRoot).ToList();
            Assert.LessOrEqual(roots.Count, 1, $"{roots.Count} top-level '{WaterRoot}' roots");
            var objs = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
            int tiles = Terrain.activeTerrains.Length;
            int quads = objs.Count(o => o.GetComponent<Renderer>());
            Debug.Log($"LayerAudit: water objects: {objs.Count}{(objs.Count == 0 ? " (vacuous)" : "")} water renderers: {quads} " +
                      $"terrain tiles: {tiles} roots: [{string.Join(",", roots.Select(r => r.name))}]");
            Assert.IsEmpty(objs.Where(o => o.layer != water).Select(o => o.name).Take(10), "Water objects off layer 4");
            Assert.IsEmpty(objs.SelectMany(o => o.GetComponents<Collider>()).Where(c => !c.isTrigger).Select(c => c.name), "Water has a solid collider");
            var stray = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include)
                .Where(c => c.gameObject.layer == water && !c.isTrigger).Select(c => c.name).Take(10).ToList();
            Assert.IsEmpty(stray, "Solid colliders on the Water layer");
        }

        [Test]
        public void Masks_LeaveOutWaterAndWorldBounds()
        {
            int both = (1 << water) | (1 << bounds);
            Assert.AreEqual(both, WorldLayers.NonGroundMask);
            Assert.AreEqual(0, WorldLayers.SightMask & both, "Sightline mask (Birds/Wildlife TerrainBlocks)");
            var terrain = Terrain.activeTerrains.FirstOrDefault();
            Assert.IsNotNull(terrain, "No terrain");
            int robin = RobinFlightPlanner.ObstacleMask(terrain);
            Assert.AreEqual(0, robin & both, "RobinFlightPlanner.ObstacleMask");
            Assert.AreNotEqual(0, robin & (1 << terrain.gameObject.layer), "Robin mask lost the terrain");
            Assert.AreEqual(0, WildlifePlacer.BakeLayerMask & (1 << water), "WildlifePlacer bake mask includes Water");
            Assert.AreNotEqual(0, WildlifePlacer.BakeLayerMask & (1 << bounds), "Bake mask must keep the fence colliders");
            var surfaces = Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include);
            Assert.IsNotEmpty(surfaces, "No NavMeshSurface in World");
            foreach (var s in surfaces) Assert.AreEqual(0, s.layerMask.value & (1 << water), $"{s.name} layerMask includes Water");
        }

        [Test]
        public void Sightlines_StillFindTerrainUnderWaterAndFences()
        {
            // The lowest tile is an all-water tile (flat bed, no trees), so nothing else sits on the probe rays.
            var t = Terrain.activeTerrains.OrderBy(x => x.transform.position.y + x.terrainData.bounds.max.y).First();
            var c = t.transform.position + t.terrainData.size * 0.5f;
            float g = t.SampleHeight(c) + t.transform.position.y;
            Vector3 top = new Vector3(c.x, g + 80f, c.z), below = new Vector3(c.x, g - 5f, c.z);
            int robin = RobinFlightPlanner.ObstacleMask(t);
            Vector3 p = new Vector3(c.x, g + 2f, c.z), f = new Vector3(c.x, g + 10f, c.z);
            bool openBefore = !Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.3f, robin, QueryTriggerInteraction.Ignore)
                              && !Physics.CheckCapsule(f, f + Vector3.up * 3f, 0.3f, robin, QueryTriggerInteraction.Ignore);
            if (!openBefore) Assert.Inconclusive($"Probe point {c} on {t.name} is not clear before the probes");
            Debug.Log($"LayerAudit: sightline probe on {t.name} at ({c.x:F0},{g:F1},{c.z:F0})");
            // A water slab and 80 stacked fence boxes on the ray: more hits than either old 16-hit buffer held.
            // The water slab is solid on purpose: the worst case if a water collider ever appears.
            Box("ProbeWater", water, new Vector3(c.x, g + 2f, c.z), new Vector3(40f, 0.2f, 40f));
            for (int i = 0; i < 80; i++) Box($"ProbeFence{i}", bounds, new Vector3(c.x, g + 5f + i * 0.9f, c.z), new Vector3(3f, 0.3f, 3f));
            Physics.SyncTransforms();

            var birds = typeof(BirdPlacementRules.Site).GetMethod("TerrainBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            var wild = typeof(WildlifeRules).GetMethod("TerrainBlocks", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(birds, "BirdPlacementRules.Site.TerrainBlocks not found");
            Assert.IsNotNull(wild, "WildlifeRules.TerrainBlocks not found");
            Assert.IsTrue((bool)birds.Invoke(null, new object[] { top, below }), "Bird sightline missed the terrain");
            Assert.IsTrue((bool)wild.Invoke(null, new object[] { top, below }), "Wildlife sightline missed the terrain");
            // Straight through water and fences, never touching ground: not blocked.
            Vector3 a = new Vector3(c.x - 1f, g + 2f, c.z), b = new Vector3(c.x + 1f, g + 40f, c.z);
            Assert.IsFalse((bool)birds.Invoke(null, new object[] { a, b }), "Water/fence blocked a bird sightline");
            Assert.IsFalse((bool)wild.Invoke(null, new object[] { a, b }), "Water/fence blocked a wildlife sightline");

            Assert.IsFalse(Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.3f, robin, QueryTriggerInteraction.Ignore),
                "Robin capsule hit water");
            Assert.IsFalse(Physics.CheckCapsule(f, f + Vector3.up * 3f, 0.3f, robin, QueryTriggerInteraction.Ignore), "Robin capsule hit a fence box");
            Assert.IsTrue(Physics.CheckCapsule(f, f + Vector3.up * 3f, 0.3f, 1 << bounds, QueryTriggerInteraction.Ignore), "Probe fence not hit at all");
        }

        void Box(string name, int layer, Vector3 pos, Vector3 size)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.position = pos;
            go.AddComponent<BoxCollider>().size = size;
            temp.Add(go);
        }
    }
}
