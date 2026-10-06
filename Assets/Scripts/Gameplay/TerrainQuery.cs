using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Ground height on a tiled terrain. Terrain.activeTerrain is only one tile, and sampling
    /// it outside its own bounds returns its edge height, so look up the tile under the point.
    /// In Play the tile list and each tile's bounds are cached once per frame (every wildlife and
    /// bird agent calls this each frame; BB-QA-7). Same tiles, same order, same bounds test as an
    /// uncached scan, so the results are identical. In edit mode builders add and remove tiles
    /// between calls, so nothing is cached there.
    /// </summary>
    public static class TerrainQuery
    {
        static readonly List<Terrain> tiles = new List<Terrain>();
        static Vector3[] origins = new Vector3[0], sizes = new Vector3[0];
        static bool[] usable = new bool[0];
        static int cachedFrame = -1;

        static void Refresh(bool force)
        {
            if (!force && Application.isPlaying && cachedFrame == Time.frameCount) return;
            Terrain.GetActiveTerrains(tiles);
            if (origins.Length < tiles.Count)
            {
                origins = new Vector3[tiles.Count];
                sizes = new Vector3[tiles.Count];
                usable = new bool[tiles.Count];
            }
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                usable[i] = t != null && t.terrainData != null;
                if (!usable[i]) continue;
                origins[i] = t.transform.position;
                sizes[i] = t.terrainData.size;
            }
            cachedFrame = Application.isPlaying ? Time.frameCount : -1;
        }

        public static Terrain TileAt(Vector3 position)
        {
            Refresh(false);
            for (int i = 0; i < tiles.Count; i++)
            {
                if (!usable[i]) continue;
                Vector3 o = origins[i], s = sizes[i];
                if (position.x >= o.x && position.x <= o.x + s.x && position.z >= o.z && position.z <= o.z + s.z)
                {
                    if (tiles[i] != null) return tiles[i];
                    // A tile was destroyed this frame: rescan uncached.
                    Refresh(true);
                    return TileAt(position);
                }
            }
            return null;
        }

        /// <summary>The tile under the point, or the nearest tile when none covers it (its edge clamps).</summary>
        public static Terrain TileAtOrNearest(Vector3 position)
        {
            var hit = TileAt(position);
            if (hit != null) return hit;
            float best = float.MaxValue;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (!usable[i] || t == null) continue;
                Vector3 o = origins[i], s = sizes[i];
                float dx = Mathf.Max(o.x - position.x, 0f, position.x - o.x - s.x);
                float dz = Mathf.Max(o.z - position.z, 0f, position.z - o.z - s.z);
                if (dx * dx + dz * dz < best) { best = dx * dx + dz * dz; hit = t; }
            }
            return hit;
        }

        /// <summary>World-space ground height, or false when no tile covers the point.</summary>
        public static bool TryGroundHeight(Vector3 position, out float height)
        {
            var t = TileAt(position);
            height = t != null ? t.SampleHeight(position) + t.transform.position.y : 0f;
            return t != null;
        }

        /// <summary>Ground height under the point; off the map it is the nearest tile's edge height, 0 with no terrain.</summary>
        public static float Height(Vector3 position)
        {
            var t = TileAtOrNearest(position);
            return t != null ? t.SampleHeight(position) + t.transform.position.y : 0f;
        }

        /// <summary>Point in the tile's 0-1 heightmap space (clamped), for TerrainData lookups.</summary>
        public static Vector2 Normalized(Terrain t, Vector3 position)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            return new Vector2(Mathf.Clamp01((position.x - o.x) / s.x), Mathf.Clamp01((position.z - o.z) / s.z));
        }

        /// <summary>Terrain slope in degrees under the point (0 with no terrain).</summary>
        public static float Steepness(Vector3 position)
        {
            var t = TileAtOrNearest(position);
            if (t == null) return 0f;
            var n = Normalized(t, position);
            return t.terrainData.GetSteepness(n.x, n.y);
        }

        /// <summary>Terrain surface normal under the point (up with no terrain).</summary>
        public static Vector3 Normal(Vector3 position)
        {
            var t = TileAtOrNearest(position);
            if (t == null) return Vector3.up;
            var n = Normalized(t, position);
            return t.terrainData.GetInterpolatedNormal(n.x, n.y);
        }
    }
}
