using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>Shared lookups for the north-boundary PlayMode tests (spec B4-B6).</summary>
    static class WorldBoundsTestKit
    {
        public const string MapConfigPath = "Assets/World/MapConfig.asset";

        public static WorldBoundsClamp Clamp()
        {
            var clamps = Object.FindObjectsByType<WorldBoundsClamp>();
            Assert.AreEqual(1, clamps.Length, "Expected exactly one WorldBoundsClamp in World");
            return clamps[0];
        }

        /// <summary>Thinnest backstop wall segment under the clamp's root, in metres.</summary>
        public static float BackstopThickness(WorldBoundsClamp clamp)
        {
            float t = float.MaxValue;
            foreach (var box in clamp.GetComponentsInChildren<BoxCollider>())
                t = Mathf.Min(t, box.size.x * box.transform.lossyScale.x);
            Assert.Less(t, float.MaxValue, "No backstop colliders under WorldBounds");
            return t;
        }

        public static PlayerController Player()
        {
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            Assert.AreEqual(1, tagged.Length, "Expected exactly one object tagged Player");
            return tagged[0].GetComponent<PlayerController>();
        }

        public static MapConfig Config()
        {
#if UNITY_EDITOR
            var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<MapConfig>(MapConfigPath);
#else
            MapConfig cfg = null;
#endif
            Assert.IsNotNull(cfg, $"No MapConfig at {MapConfigPath}");
            return cfg;
        }

        /// <summary>Moves a CharacterController body to <paramref name="xz"/> on the ground.</summary>
        public static void Teleport(PlayerController player, Vector3 xz, float yaw, float above = 0.5f)
        {
            Assert.IsTrue(TerrainQuery.TryGroundHeight(xz, out float ground), $"No terrain at {xz}");
            xz.y = ground + above;
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.SetPositionAndRotation(xz, Quaternion.Euler(0f, yaw, 0f));
            cc.enabled = true;
        }

        /// <summary>
        /// X positions on land just south of the line (ground above WaterLevelY), bank to bank,
        /// <paramref name="count"/> evenly spaced. A point that lands in water slides to the
        /// nearest land sample.
        /// </summary>
        public static List<float> LandPointsAlongLine(WorldBoundsRule rule, float waterLevelY, int count, float southOffset = 20f, float sideMargin = 40f)
        {
            var land = LandXs(rule, waterLevelY, southOffset, sideMargin);
            Assert.IsNotEmpty(land, "No land along the north line");
            return Spread(land, count);
        }

        /// <summary>Every 1 m X with land at the line and at the sweep starts, ascending.</summary>
        static List<float> LandXs(WorldBoundsRule rule, float waterLevelY, float southOffset, float sideMargin)
        {
            bool Land(float x, float z) => TerrainQuery.TryGroundHeight(new Vector3(x, 0f, z), out float h) && h > waterLevelY + 0.5f;
            // Bank to bank = the barrier's span (spec B2); off it, the line runs over lake bed or the north bank,
            // which the lake-bed sweep covers.
            var (minX, maxX) = BarrierSpanX();
            var land = new List<float>();
            for (float x = minX; x <= maxX; x += 1f)
            {
                // Land at the line and at every sweep start (straight, and 30/60 degrees off on both sides).
                float z = rule.LineZAt(x);
                if (!(Land(x, z - 1f) && Land(x, z - southOffset) && Land(x - sideMargin, z - southOffset) && Land(x + sideMargin, z - southOffset))) continue;
                land.Add(x);
            }
            Debug.Log($"WorldBoundsTestKit: barrier span x=[{minX:F0},{maxX:F0}] land samples={land.Count} (no slope pre-filter: steep approaches go through replace, skip and the caps)");
            return land;
        }

        /// <summary>X span of level-2's visible barrier ("NorthBarrier", bank to bank); the whole terrain if there is none.</summary>
        public static (float min, float max) BarrierSpanX()
        {
            var root = GameObject.Find("NorthBarrier");
            if (root == null) return TerrainXRange();
            float min = float.MaxValue, max = float.MinValue;
            foreach (var c in root.GetComponentsInChildren<Collider>())
            {
                min = Mathf.Min(min, c.bounds.min.x);
                max = Mathf.Max(max, c.bounds.max.x);
            }
            return min <= max ? (min, max) : TerrainXRange();
        }

        /// <summary>Terrain X extent over every tile.</summary>
        public static (float min, float max) TerrainXRange()
        {
            var tiles = Terrain.activeTerrains;
            Assert.IsNotEmpty(tiles, "No terrain");
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var t in tiles)
            {
                minX = Mathf.Min(minX, t.transform.position.x);
                maxX = Mathf.Max(maxX, t.transform.position.x + t.terrainData.size.x);
            }
            return (minX, maxX);
        }

        /// <summary>
        /// qa-2 escape risk: X positions in the dry lake bed (ground below WaterLevelY at the line and at the
        /// sweep start) west of the west bank and east of the east bank, <paramref name="perSide"/> each, evenly
        /// spaced and kept 2 x <paramref name="southOffset"/> in from the terrain edges.
        /// </summary>
        public static (List<float> west, List<float> east) LakeBedPointsAlongLine(WorldBoundsRule rule, float waterLevelY, int perSide, float southOffset = 20f)
        {
            bool Bed(float x, float z) => TerrainQuery.TryGroundHeight(new Vector3(x, 0f, z), out float h) && h < waterLevelY;
            var (minX, maxX) = TerrainXRange();
            var (westBank, eastBank) = BarrierSpanX();
            var west = new List<float>();
            var east = new List<float>();
            for (float x = minX + 2f * southOffset; x <= maxX - 2f * southOffset; x += 1f)
            {
                float z = rule.LineZAt(x);
                if (!Bed(x, z - 1f) || !Bed(x, z - southOffset)) continue;
                if (x < westBank) west.Add(x);
                else if (x > eastBank) east.Add(x);
            }
            Assert.IsNotEmpty(west, "No dry lake bed west of the west bank");
            Assert.IsNotEmpty(east, "No dry lake bed east of the east bank");
            return (Spread(west, perSide), Spread(east, perSide));
        }

        /// <summary><paramref name="count"/> members of the ascending 1 m sample list <paramref name="xs"/>, evenly spaced by index from
        /// the first sample to the last (qa-2: the points reach both ends): distinct points over every stretch of the list,
        /// even when it has gaps (lake bed between banks).</summary>
        static List<float> Spread(List<float> xs, int count)
        {
            var points = new List<float>();
            for (int i = 0; i < count && i < xs.Count; i++)
                points.Add(xs[count == 1 ? xs.Count / 2 : Mathf.RoundToInt(i * (xs.Count - 1) / (float)(count - 1))]);
            return points;
        }
    }
}
