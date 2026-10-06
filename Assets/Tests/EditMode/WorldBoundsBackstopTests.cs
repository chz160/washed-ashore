using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// Spec B3: the hidden backstop is continuous along the whole north line (raycast every 1 m,
    /// ground to 30 m up), at least 2 m thick and 30 m tall, on the WorldBounds layer, and there
    /// are no terrain holes within 50 m of the line. Reads the built World scene; build first with
    /// WorldBoundsBuilder.Build().
    /// </summary>
    public class WorldBoundsBackstopTests
    {
        const string ScenePath = "Assets/Scenes/World.unity";
        const float MinThickness = 2f;
        const float MinHeight = 30f;
        const float HoleBand = 50f;

        WorldBoundsClamp clamp;
        BoxCollider[] walls;
        int layer;

        [OneTimeSetUp]
        public void OpenWorld()
        {
            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find("WorldBounds");
            Assert.IsNotNull(root, "No WorldBounds root; run WorldBoundsBuilder.Build()");
            clamp = root.GetComponent<WorldBoundsClamp>();
            Assert.IsNotNull(clamp, "WorldBounds has no WorldBoundsClamp");
            walls = root.GetComponentsInChildren<BoxCollider>();
            layer = LayerMask.NameToLayer("WorldBounds");
            Assert.GreaterOrEqual(layer, 0, "No WorldBounds layer");
            Assert.IsNotEmpty(Terrain.activeTerrains, "No terrain tiles in World");
            Physics.SyncTransforms();
        }

        [Test]
        public void Walls_AreHiddenThickTallAndOnWorldBoundsLayer()
        {
            Assert.IsNotEmpty(walls);
            foreach (var w in walls)
            {
                Assert.AreEqual(layer, w.gameObject.layer, $"{w.name} not on WorldBounds");
                Assert.GreaterOrEqual(w.size.x * w.transform.lossyScale.x, MinThickness, $"{w.name} thinner than {MinThickness} m");
                Assert.IsFalse(w.isTrigger, $"{w.name} is a trigger");
                Assert.IsNull(w.GetComponent<Renderer>(), $"{w.name} is visible");
            }
        }

        [Test]
        public void RaycastEveryMetre_FindsNoGaps()
        {
            var rule = clamp.Rule;
            float minX = Terrain.activeTerrains.Min(t => t.transform.position.x);
            float maxX = Terrain.activeTerrains.Max(t => t.transform.position.x + t.terrainData.size.x);
            int mask = 1 << layer, rays = 0;
            var gaps = new System.Collections.Generic.List<string>();
            for (float x = minX; x <= maxX; x += 1f)
            {
                float z = rule.LineZAt(x);
                float ground = float.MinValue;
                foreach (float dz in new[] { -8f, -4f, 0f, 4f })
                    if (TerrainQuery.TryGroundHeight(new Vector3(x, 0f, z + dz), out float h)) ground = Mathf.Max(ground, h);
                if (ground == float.MinValue) ground = 0f;
                for (float up = 0.25f; up <= MinHeight; up += 1f)
                {
                    rays++;
                    var from = new Vector3(x, ground + up, z - 12f);
                    if (!Physics.Raycast(from, Vector3.forward, 14f, mask, QueryTriggerInteraction.Ignore))
                    {
                        if (gaps.Count < 20) gaps.Add($"x={x:F0} y={from.y:F1}");
                        else break;
                    }
                }
            }
            Debug.Log($"WorldBoundsBackstopTests: {rays} rays over x=[{minX:F0},{maxX:F0}], gaps={gaps.Count}");
            Assert.IsEmpty(gaps, "Backstop gaps: " + string.Join("; ", gaps));
        }

        [Test]
        public void NoTerrainHoles_Within50mOfLine()
        {
            var rule = clamp.Rule;
            int checkedSamples = 0;
            var holes = new System.Collections.Generic.List<string>();
            foreach (var t in Terrain.activeTerrains)
            {
                var d = t.terrainData;
                int res = d.holesResolution;
                var solid = d.GetHoles(0, 0, res, res);
                Vector3 o = t.transform.position;
                for (int iz = 0; iz < res; iz++)
                    for (int ix = 0; ix < res; ix++)
                    {
                        float x = o.x + (ix + 0.5f) / res * d.size.x, z = o.z + (iz + 0.5f) / res * d.size.z;
                        if (Mathf.Abs(z - rule.LineZAt(x)) > HoleBand) continue;
                        checkedSamples++;
                        if (!solid[iz, ix] && holes.Count < 20) holes.Add($"{t.name}@({x:F0},{z:F0})");
                    }
            }
            Debug.Log($"WorldBoundsBackstopTests: {checkedSamples} hole samples within {HoleBand} m, holes={holes.Count}");
            Assert.Greater(checkedSamples, 0, "No terrain within 50 m of the line");
            Assert.IsEmpty(holes, "Terrain holes near the line: " + string.Join("; ", holes));
        }
    }
}
