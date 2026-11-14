namespace WashedAshore.World
{
    /// <summary>
    /// Which ground layers a terrain tile keeps (I-3 step 4). Pure logic over canonical layer weights
    /// (index = <see cref="GroundType"/>); the editor painter (BellsBendGround) feeds it, and EditMode tests reach it here.
    ///
    /// Seam rule: weights on a shared tile border must be identical on both sides. That holds because
    /// (1) the weight function is a pure function of the world point (BellsBendMaps.GameSlope picks the first
    /// name-sorted tile whose closed range holds the point, never "the tile being painted"; keep it so),
    /// (2) <see cref="Prefilter"/> runs on every texel of every tile, so both sides see the same filtered vector,
    /// (3) <see cref="Select"/> keeps every layer that is non-zero anywhere on the tile's edge lines, so
    /// <see cref="Restrict"/> drops nothing at an edge texel, and
    /// (4) <see cref="Restrict"/> renormalises with a sum taken in canonical ID order; the dropped entries are exactly 0,
    /// so the sum over kept entries in ID order is the same float on both sides.
    /// A tile's list depends only on its own geometry, never on which neighbours are painted in the same call.
    /// </summary>
    public static class TerrainLayerPlan
    {
        /// <summary>A layer whose weight never reaches this on a tile (margin included) is dropped.</summary>
        public const float DropBelow = 0.02f;
        /// <summary>Below one 8-bit alphamap step: weights under this are float dust and are zeroed.</summary>
        public const float EdgeEps = 0.5f / 255f;
        /// <summary>Texels beyond each tile edge that also count toward the layer maxima.</summary>
        public const int MarginTexels = 4;
        /// <summary>Layers one shader pass renders; tiles needing more still keep them (a second pass).</summary>
        public const int PassLimit = 4;

        /// <summary>
        /// Zeroes weights below <see cref="EdgeEps"/> and renormalises to sum 1. Needed so float dust above 0 does not put every
        /// layer into every edge list. Applied to every texel of every tile, so every kept weight scales by at most 1/(1 - 6*EdgeEps), about 3 8-bit steps in the worst case and typically under 1; measure with BellsBendCoverage before and after.
        /// </summary>
        public static void Prefilter(float[] w)
        {
            float sum = 0f;
            for (int i = 0; i < w.Length; i++)
            {
                if (w[i] < EdgeEps) w[i] = 0f;
                sum += w[i];
            }
            if (sum <= 0f) return; // cannot happen after Weights() normalisation (the largest entry is at least 1/7)
            for (int i = 0; i < w.Length; i++) w[i] /= sum;
        }

        /// <summary>Bit i set where the (prefiltered) weight is above 0. OR it over every texel on the tile's four edge lines.</summary>
        public static uint EdgeMask(float[] w)
        {
            uint m = 0;
            for (int i = 0; i < w.Length; i++) if (w[i] > 0f) m |= 1u << i;
            return m;
        }

        /// <summary>Kept layers: max weight at least <see cref="DropBelow"/>, or non-zero somewhere on the tile edge.</summary>
        public static uint Select(float[] maxPerLayer, uint edgeMask)
        {
            uint m = edgeMask;
            for (int i = 0; i < maxPerLayer.Length; i++) if (maxPerLayer[i] >= DropBelow) m |= 1u << i;
            return m;
        }

        public static int Count(uint mask)
        {
            int n = 0;
            for (; mask != 0; mask >>= 1) n += (int)(mask & 1u);
            return n;
        }

        /// <summary>Kept layer IDs in ascending order.</summary>
        public static int[] IdsOf(uint mask)
        {
            var ids = new int[Count(mask)];
            int k = 0;
            for (int i = 0; i < 32; i++) if ((mask & (1u << i)) != 0) ids[k++] = i;
            return ids;
        }

        /// <summary>
        /// Writes the kept layers' weights, renormalised, to dst[0..k-1] in ID order. The sum runs in ID order over the kept entries;
        /// at an edge texel every non-zero entry is kept, so the dropped ones are exactly 0 and the sum is bit-identical across tiles that keep different sets.
        /// </summary>
        public static void Restrict(float[] w, uint mask, float[] dst)
        {
            float sum = 0f;
            for (int i = 0; i < w.Length; i++) if ((mask & (1u << i)) != 0) sum += w[i];
            int k = 0;
            for (int i = 0; i < w.Length; i++)
            {
                if ((mask & (1u << i)) == 0) continue;
                dst[k++] = sum > 0f ? w[i] / sum : 0f;
            }
        }
    }
}
