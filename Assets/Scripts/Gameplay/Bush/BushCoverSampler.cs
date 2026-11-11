using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Reads the terrain detail layers under the feet and returns the bush multiplier (spec section 5). It reads baked terrain data and the
    /// position only, so a replay gives the same answer. Detail counts near the player are cached in a window and refetched when the feet
    /// leave it, so a tick allocates nothing.
    /// </summary>
    public sealed class BushCoverSampler
    {
        const int Window = 16;   // cells per side (32 m at the 2 m Bells Bend cell)

        readonly BushSlowdownTuning tuning;
        readonly int[][] counts;                 // per layer: Window * Window, row-major [y * Window + x]
        Terrain tile;
        TerrainData data;
        int res, originX, originY, layerCount;
        bool valid;

        public BushCoverSampler(BushSlowdownTuning tuning)
        {
            this.tuning = tuning;
            counts = new int[BushSlowdownTuning.LayerCount][];
        }

        /// <summary>Multiplier for the feet at <paramref name="feet"/>; exactly 1f with no slowing cover.</summary>
        public float Multiplier(Vector3 feet, bool sprint)
        {
            var layers = tuning != null ? tuning.layers : null;
            if (layers == null) return 1f;
            var t = TerrainQuery.TileAt(feet);
            if (t == null || t.terrainData == null) return 1f;
            if (t != tile || t.terrainData != data)
            {
                tile = t; data = t.terrainData; valid = false;
                res = data.detailResolution;
                layerCount = data.detailPrototypes.Length;   // allocates: only on a tile change
            }
            if (res < 2) return 1f;

            Vector3 o = t.transform.position, size = data.size;
            float u = (feet.x - o.x) / size.x * res - 0.5f, v = (feet.z - o.z) / size.z * res - 0.5f;
            int x0 = Mathf.FloorToInt(u), y0 = Mathf.FloorToInt(v);
            float fx = u - x0, fy = v - y0;
            int xa = Mathf.Clamp(x0, 0, res - 1), xb = Mathf.Clamp(x0 + 1, 0, res - 1);
            int ya = Mathf.Clamp(y0, 0, res - 1), yb = Mathf.Clamp(y0 + 1, 0, res - 1);
            EnsureWindow(xa, ya, xb, yb);

            float above = feet.y - (t.SampleHeight(feet) + o.y);
            float m = 1f;
            for (int i = 0; i < layers.Length && i < layerCount; i++)
            {
                var l = layers[i];
                if (!l.slows || above >= l.maxHeight) continue;
                int[] c = counts[i];
                float cover = BushSlowdownRules.Cover(
                    BushSlowdownRules.Fill(c[(ya - originY) * Window + (xa - originX)], l.fullCount),
                    BushSlowdownRules.Fill(c[(ya - originY) * Window + (xb - originX)], l.fullCount),
                    BushSlowdownRules.Fill(c[(yb - originY) * Window + (xa - originX)], l.fullCount),
                    BushSlowdownRules.Fill(c[(yb - originY) * Window + (xb - originX)], l.fullCount), fx, fy);
                if (cover <= 0f) continue;
                m = BushSlowdownRules.Combine(m, BushSlowdownRules.LayerMultiplier(sprint ? l.sprintMultiplier : l.walkMultiplier, cover));
            }
            return m;
        }

        /// <summary>Forget the cached window (a test edited the detail layers).</summary>
        public void Invalidate() => valid = false;

        void EnsureWindow(int xa, int ya, int xb, int yb)
        {
            if (valid && xa >= originX && ya >= originY && xb < originX + Window && yb < originY + Window) return;
            int half = Window / 2;
            originX = Mathf.Clamp(xa - half, 0, Mathf.Max(0, res - Window));
            originY = Mathf.Clamp(ya - half, 0, Mathf.Max(0, res - Window));
            int w = Mathf.Min(Window, res - originX), h = Mathf.Min(Window, res - originY);
            var layers = tuning.layers;
            for (int i = 0; i < layers.Length && i < layerCount; i++)
            {
                if (!layers[i].slows) continue;
                int[,] got = data.GetDetailLayer(originX, originY, w, h, i);   // [y, x]; allocates, but only on a window refetch
                if (counts[i] == null) counts[i] = new int[Window * Window];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++) counts[i][y * Window + x] = got[y, x];
            }
            valid = true;
        }
    }
}
