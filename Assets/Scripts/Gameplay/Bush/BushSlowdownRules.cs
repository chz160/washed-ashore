namespace WashedAshore.Gameplay
{
    /// <summary>The pure maths of the bush slowdown (spec section 5): no engine calls, so EditMode tests can pin it.</summary>
    public static class BushSlowdownRules
    {
        /// <summary>Fill of one detail cell: min(count, full) / full.</summary>
        public static float Fill(int count, int fullCount)
        {
            if (count <= 0) return 0f;
            if (fullCount < 1) fullCount = 1;
            return count >= fullCount ? 1f : (float)count / fullCount;
        }

        /// <summary>Bilinear blend of four fills: f00 at (x0,y0), f10 at (x0+1,y0), f01 at (x0,y0+1), f11 at (x0+1,y0+1).</summary>
        public static float Cover(float f00, float f10, float f01, float f11, float fx, float fy)
        {
            float a = f00 + (f10 - f00) * fx;
            float b = f01 + (f11 - f01) * fx;
            return a + (b - a) * fy;
        }

        /// <summary>One layer's multiplier: 1 - (1 - M) * c. Exactly 1f when the cover is 0.</summary>
        public static float LayerMultiplier(float fullCoverMultiplier, float cover) => 1f - (1f - fullCoverMultiplier) * cover;

        /// <summary>The strongest slowdown wins: the lower of the running multiplier and this layer's.</summary>
        public static float Combine(float current, float layerMultiplier) => layerMultiplier < current ? layerMultiplier : current;
    }
}
