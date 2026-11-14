using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>
    /// Maps each <see cref="GroundType"/> to the alphamap channel that holds it on one tile, found by the
    /// TerrainLayer asset name. A tile's channel order can differ from every other tile's, and a tile may not
    /// hold a layer at all (its weight is then 0). Immutable once built. Not cached statically:
    /// TerrainData.terrainLayers hands out a fresh array and the painter changes it, so callers keep a map
    /// per tile for the length of their own run.
    /// </summary>
    public sealed class TerrainGroundMap
    {
        readonly int[] channel = new int[GroundTypes.Count];

        public int LayerCount { get; }

        TerrainGroundMap(int layerCount)
        {
            LayerCount = layerCount;
            for (int i = 0; i < channel.Length; i++) channel[i] = -1;
        }

        public static TerrainGroundMap Build(TerrainLayer[] layers)
        {
            var map = new TerrainGroundMap(layers == null ? 0 : layers.Length);
            if (layers == null) return map;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null || !GroundTypes.TryFromLayerName(layers[i].name, out var g)) continue;
                if (map.channel[(int)g] < 0) map.channel[(int)g] = i; // the first of a duplicate name wins
            }
            return map;
        }

        public static TerrainGroundMap Build(TerrainData d) => Build(d.terrainLayers);

        /// <summary>The alphamap channel of the type on this tile, or -1 when the tile has no such layer.</summary>
        public int Channel(GroundType g) => channel[(int)g];

        public bool Has(GroundType g) => channel[(int)g] >= 0;

        /// <summary>Weight of the type at alphamap texel [y, x]; 0 when the tile lacks the layer or the array is shallower than its channel.</summary>
        public float Weight(float[,,] alphamaps, int y, int x, GroundType g)
        {
            int c = channel[(int)g];
            if (c < 0 || c >= alphamaps.GetLength(2)) return 0f;
            return alphamaps[y, x, c];
        }

        /// <summary>Fills <paramref name="canonical"/> (length at least <see cref="GroundTypes.Count"/>) with the weight of every type at the texel, in ID order; absent types are 0.</summary>
        public void Expand(float[,,] alphamaps, int y, int x, float[] canonical)
        {
            for (int i = 0; i < GroundTypes.Count; i++) canonical[i] = Weight(alphamaps, y, x, (GroundType)i);
        }

        /// <summary>The heaviest ground type at the texel (null when the tile holds no known layer).</summary>
        public GroundType? Dominant(float[,,] alphamaps, int y, int x, out float weight)
        {
            GroundType? best = null;
            weight = 0f;
            for (int i = 0; i < GroundTypes.Count; i++)
            {
                var g = (GroundType)i;
                if (!Has(g)) continue;
                float w = Weight(alphamaps, y, x, g);
                if (best == null || w > weight) { best = g; weight = w; }
            }
            return best;
        }
    }
}
