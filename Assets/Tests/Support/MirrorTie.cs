using UnityEngine;

namespace WashedAshore.Tests.Support
{
    /// <summary>
    /// The back-face tie rule for the look-down, pitch-sweep and range-end ray sweeps (td-49 "CAP FIX", qa-49 12cp T1-T4). The neck cap is DOUBLE-SIDED: every cap triangle has an exact mirror
    /// (the same three vertex positions, opposite winding), so a ray that hits the lid meets both at the same distance. The nearest-hit rule must then prefer the FRONT-facing one, but ONLY for an
    /// exact mirror pair within <see cref="TieTolerance"/>: an unrelated back face at the same distance as a front face still counts, and nothing is applied to non-mirrored triangles.
    /// </summary>
    public static class MirrorTie
    {
        /// <summary>
        /// Test-only switch (qa-49 12cv (b)): true = the mirror tie rule; false = the plain nearest-hit rule, for the OFF/ON prints of each sweep. Starts false when the environment variable
        /// MIRRORTIE_OFF is "1", else true. Never read by game code.
        /// </summary>
        public static bool Enabled = System.Environment.GetEnvironmentVariable("MIRRORTIE_OFF") != "1";

        /// <summary>One line naming the rule state, written at the top of every result file and log so an ON set and an OFF set can never be confused.</summary>
        public static string StateLine => "MirrorTie rule " + (Enabled ? "ON" : "OFF") + " (MIRRORTIE_OFF=" + (System.Environment.GetEnvironmentVariable("MIRRORTIE_OFF") ?? "unset") + ")";

        static MirrorTie()
        {
            Debug.Log(StateLine);   // once per Editor session (qa-49 12cx)
        }

        public const float TieTolerance = 1e-5f;     // metres along the ray
        public const float PositionTolerance = 1e-5f; // metres, per vertex of the mirror test

        /// <summary>Tie-resolved hits since <see cref="ResetCounters"/>: rays where a front face won over its exact mirror.</summary>
        public static int Resolved { get; private set; }
        /// <summary>Hits within the tie tolerance of the current best that were NOT an exact mirror (they keep the plain nearest-hit rule).</summary>
        public static int NearTiesNotMirrored { get; private set; }

        public static void ResetCounters() { Resolved = 0; NearTiesNotMirrored = 0; }

        /// <summary>True when the two triangles have the same three vertex positions with the opposite winding.</summary>
        public static bool IsExactMirror(Vector3 a1, Vector3 b1, Vector3 c1, Vector3 a2, Vector3 b2, Vector3 c2)
        {
            // opposite winding: the second triangle is the first with two vertices swapped, for any of the three starting vertices
            return Same(a1, a2) && Same(b1, c2) && Same(c1, b2)
                || Same(a1, b2) && Same(b1, a2) && Same(c1, c2)
                || Same(a1, c2) && Same(b1, b2) && Same(c1, a2);
        }

        static bool Same(Vector3 p, Vector3 q) => (p - q).sqrMagnitude <= PositionTolerance * PositionTolerance;

        /// <summary>
        /// Whether a hit (distance <paramref name="t"/>, back-facing flag, triangle) replaces the current best hit. With no best yet it always does; otherwise it does when it is nearer, except that an
        /// exact mirror pair within the tolerance resolves to the front-facing triangle.
        /// </summary>
        public static bool Replaces(bool haveBest, float bestT, bool bestBack, Vector3 bA, Vector3 bB, Vector3 bC, float t, bool back, Vector3 a, Vector3 b, Vector3 c)
        {
            if (!haveBest) return true;
            if (!Enabled) return t < bestT;
            if (Mathf.Abs(t - bestT) <= TieTolerance && back != bestBack)
            {
                if (IsExactMirror(bA, bB, bC, a, b, c))
                {
                    if (!back) { Resolved++; return true; }   // the new hit is the front face of the pair: it wins
                    Resolved++;
                    return false;                              // the best is the front face: keep it
                }
                NearTiesNotMirrored++;
            }
            return t < bestT;
        }
    }
}
