using System.Collections.Generic;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Wildlife
{
    /// <summary>
    /// The ground the wildlife and bird habitat rules plan on: a 512 m window over the terrain tiles,
    /// placed around PlayerSpawn the way the old single 512 m test terrain was (spawn at 205, 205).
    /// Rule constants written for that terrain (terrain centre, distance to edge) are window-local, so
    /// the rules keep their geometry on the tiled Bells Bend map. Every lookup goes to the tile under
    /// the point, so the window can straddle tile seams. Land is ground at or above
    /// <see cref="MinGroundY"/> (MapConfig.WaterLevelY plus a margin), so nothing plans in the river.
    /// </summary>
    public sealed class HabitatGround
    {
        public static readonly Vector2 WindowSize = new Vector2(512f, 512f);
        public static readonly Vector2 SpawnInWindow = new Vector2(205f, 205f);

        readonly List<Terrain> tiles = new List<Terrain>();
        readonly Dictionary<Terrain, float[,,]> splat = new Dictionary<Terrain, float[,,]>();
        readonly Dictionary<Terrain, TerrainGroundMap> ground = new Dictionary<Terrain, TerrainGroundMap>();
        bool warnedUnknownLayers;

        /// <summary>South-west corner of the window (y = 0).</summary>
        public Vector3 Origin { get; }
        public Vector2 Size => WindowSize;
        public float MinGroundY { get; }
        /// <summary>PlayerSpawn pose the window was laid out from; the WorldWalkTests lane follows its yaw.</summary>
        public Vector3 SpawnPosition { get; }
        public float SpawnYaw { get; }
        public IReadOnlyList<Terrain> Tiles => tiles;

        public HabitatGround(Vector3 origin, float minGroundY, Vector3 spawn, float spawnYaw)
        {
            SpawnPosition = spawn;
            SpawnYaw = spawnYaw;
            Origin = new Vector3(origin.x, 0f, origin.z);
            MinGroundY = minGroundY;
            Terrain.GetActiveTerrains(tiles);
            tiles.RemoveAll(t => t == null || t.terrainData == null || !Overlaps(t, 0f));
        }

        /// <summary>The window around a spawn point, laid out like the old 512 m terrain.</summary>
        public static HabitatGround Around(Vector3 spawn, float spawnYaw, float minGroundY = float.NegativeInfinity) =>
            new HabitatGround(new Vector3(spawn.x - SpawnInWindow.x, 0f, spawn.z - SpawnInWindow.y), minGroundY, spawn, spawnYaw);

        /// <summary>The spawn clearing and the walk-test lane ahead of PlayerSpawn (no animals or birds there).</summary>
        public bool InTestLane(Vector3 p) => WildlifeRules.InTestLane(p, SpawnPosition, SpawnYaw);

        /// <summary>A window-local point (old terrain coordinates) in world XZ.</summary>
        public Vector2 ToWorld(Vector2 local) => new Vector2(Origin.x + local.x, Origin.z + local.y);

        public Vector2 Centre => ToWorld(Size * 0.5f);

        public float Height(Vector3 p) => TerrainQuery.Height(p);
        public Vector3 OnGround(Vector3 p) { p.y = Height(p); return p; }
        public float Steepness(Vector3 p) => TerrainQuery.Steepness(p);
        public Vector3 Normal(Vector3 p) => TerrainQuery.Normal(p);
        public bool IsLand(Vector3 p) => TerrainQuery.TileAt(p) != null && Height(p) >= MinGroundY;

        /// <summary>Distance from the point to the nearest window edge (negative outside).</summary>
        public float EdgeDistance(Vector3 p)
        {
            // The loop Mathf.Min(params float[]) runs, inline, over the same four operands in the same order: the params overload allocated an array per call (#44).
            float m = p.x - Origin.x, b = p.z - Origin.z, c = Origin.x + Size.x - p.x, d = Origin.z + Size.y - p.z;
            if (b < m) m = b;
            if (c < m) m = c;
            if (d < m) m = d;
            return m;
        }

        /// <summary>Splat weight of the ground type under the point, found by TerrainLayer asset on the tile under it (0 when that tile holds no such layer).</summary>
        public float Splat(Vector3 p, GroundType g)
        {
            var t = TerrainQuery.TileAtOrNearest(p);
            if (t == null) return 0f;
            var d = t.terrainData;
            if (!ground.TryGetValue(t, out var map))
            {
                ground[t] = map = TerrainGroundMap.Build(d);
                // A tile may legitimately hold no grass (the painter drops unused layers), but a tile with layers and none of the known names means a renamed TerrainLayer.
                if (map.LayerCount > 0 && !map.Has(GroundType.Grass) && !map.Has(GroundType.ForestFloor) && !map.Has(GroundType.Asphalt) && !map.Has(GroundType.Gravel)
                    && !map.Has(GroundType.Bank) && !map.Has(GroundType.Rock) && !map.Has(GroundType.Kudzu) && !warnedUnknownLayers)
                {
                    warnedUnknownLayers = true;
                    Debug.LogWarning($"HabitatGround: tile '{t.name}' has {map.LayerCount} terrain layers but none named in GroundTypes.LayerNames; ground weights read as 0 there (was a layer renamed?).");
                }
            }
            if (!map.Has(g)) return 0f;
            if (!splat.TryGetValue(t, out var a))
                splat[t] = a = d.GetAlphamaps(0, 0, d.alphamapWidth, d.alphamapHeight);
            var n = TerrainQuery.Normalized(t, p);
            int sx = Mathf.Clamp(Mathf.RoundToInt(n.x * (d.alphamapWidth - 1)), 0, d.alphamapWidth - 1);
            int sz = Mathf.Clamp(Mathf.RoundToInt(n.y * (d.alphamapHeight - 1)), 0, d.alphamapHeight - 1);
            return map.Weight(a, sz, sx, g);
        }

        /// <summary>Old-field grass weight under the point.</summary>
        public float Grass(Vector3 p) => Splat(p, GroundType.Grass);

        public struct TreeAt
        {
            public Vector3 world;
            public GameObject prefab;
            public float heightScale, widthScale;
        }

        /// <summary>Terrain tree instances (trees, bushes, rocks) inside the window grown by <paramref name="margin"/>.</summary>
        public List<TreeAt> Trees(float margin = 50f)
        {
            var result = new List<TreeAt>();
            var all = new List<Terrain>();
            Terrain.GetActiveTerrains(all);
            foreach (var t in all)
            {
                if (t == null || t.terrainData == null || !Overlaps(t, margin)) continue;
                var d = t.terrainData;
                var protos = d.treePrototypes;
                Vector3 o = t.transform.position;
                foreach (var inst in d.treeInstances)
                {
                    var w = Vector3.Scale(inst.position, d.size) + o;
                    if (w.x < Origin.x - margin || w.z < Origin.z - margin || w.x > Origin.x + Size.x + margin || w.z > Origin.z + Size.y + margin) continue;
                    result.Add(new TreeAt { world = w, prefab = protos[inst.prototypeIndex].prefab, heightScale = inst.heightScale, widthScale = inst.widthScale });
                }
            }
            return result;
        }

        /// <summary>World heights on a res x res grid over the window, [z, x], and the grid step in metres.</summary>
        public float[,] Heights(int res, out float step)
        {
            step = Size.x / (res - 1);
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = Height(new Vector3(Origin.x + x * step, 0f, Origin.z + z * step));
            return h;
        }

        bool Overlaps(Terrain t, float margin)
        {
            Vector3 o = t.transform.position, s = t.terrainData.size;
            return o.x <= Origin.x + Size.x + margin && o.x + s.x >= Origin.x - margin
                && o.z <= Origin.z + Size.y + margin && o.z + s.z >= Origin.z - margin;
        }
    }
}
