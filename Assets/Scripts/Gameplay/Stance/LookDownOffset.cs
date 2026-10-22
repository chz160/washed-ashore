using System;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Look-down view offset (td ruling, director amend2): how far the VIEW body (the one the camera sees) is moved back (-Z) as the
    /// player looks down, so the torso top clears the bottom edge of the frame. Per stance a point table (camera pitch in degrees, back
    /// in metres), interpolated with a monotone cubic (Fritsch-Carlson, no overshoot). It never moves the camera or the shadow body.
    /// A pure function of the stance, the previous stance, the stance blend and the camera pitch: never animation, speed or time.
    /// Stand and Crouch only; Squat, Sit and Sleep are accepted at zero (director).
    /// </summary>
    [Serializable]
    public class LookDownTuning
    {
        [Tooltip("Stand: x = camera pitch (degrees, positive looks down), y = metres back. Ascending pitch. Zero below the first point, flat above the last.")]
        public Vector2[] stand = { new Vector2(20f, 0f), new Vector2(40f, 0f), new Vector2(45f, 0.02f), new Vector2(50f, 0.04f), new Vector2(55f, 0.06f), new Vector2(60f, 0.07f), new Vector2(65f, 0.10f), new Vector2(70f, 0.16f), new Vector2(75f, 0.21f), new Vector2(80f, 0.23f), new Vector2(85f, 0.30f) };
        [Tooltip("Crouch: same layout.")]
        public Vector2[] crouch = { new Vector2(20f, 0f), new Vector2(60f, 0f), new Vector2(65f, 0.05f), new Vector2(70f, 0.06f), new Vector2(75f, 0.11f), new Vector2(80f, 0.125f), new Vector2(85f, 0.175f) };

        /// <summary>Test hook: when not NaN, the back offset is this regardless of pitch and stance table. Never serialized.</summary>
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [NonSerialized] public float debugBackOverride = float.NaN;
#endif

        public Vector2[] Of(Stance s)
        {
            switch (s)
            {
                case Stance.Stand: return stand;
                case Stance.Crouch: return crouch;
            }
            return null;
        }
    }

    public static class LookDownOffset
    {
        /// <summary>Back offset (metres) for one stance at <paramref name="pitch"/>; 0 for stances with no table.</summary>
        public static float Back(LookDownTuning t, Stance stance, float pitch)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!float.IsNaN(t.debugBackOverride) && (stance == Stance.Stand || stance == Stance.Crouch)) return t.debugBackOverride;
#endif
            var pts = t.Of(stance);
            return pts == null || pts.Length == 0 ? 0f : MonotoneCubic(pts, pitch);
        }

        /// <summary>
        /// Offset for <paramref name="stance"/> blended from <paramref name="previous"/> by <paramref name="blend"/> (0 to 1, the stance's
        /// own eye ease). <paramref name="down"/> is always 0: the method moves the view body back only.
        /// </summary>
        public static void Evaluate(LookDownTuning t, Stance previous, Stance stance, float blend, float pitch, out float back, out float down)
        {
            blend = Mathf.Clamp01(blend);
            back = Mathf.Lerp(Back(t, previous, pitch), Back(t, stance, pitch), blend);
            down = 0f;
        }

        /// <summary>Fritsch-Carlson monotone cubic through (x, y) points sorted by x; clamped to the end values outside the range.</summary>
        public static float MonotoneCubic(Vector2[] p, float x)
        {
            int n = p.Length;
            if (n == 1 || x <= p[0].x) return x < p[0].x ? 0f : p[0].y;
            if (x >= p[n - 1].x) return p[n - 1].y;
            var d = new float[n - 1];
            var m = new float[n];
            for (int i = 0; i < n - 1; i++) d[i] = (p[i + 1].y - p[i].y) / Mathf.Max(1e-6f, p[i + 1].x - p[i].x);
            m[0] = d[0];
            m[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++) m[i] = d[i - 1] * d[i] <= 0f ? 0f : (d[i - 1] + d[i]) * 0.5f;
            for (int i = 0; i < n - 1; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-9f) { m[i] = m[i + 1] = 0f; continue; }
                float a = m[i] / d[i], b = m[i + 1] / d[i];
                float s = a * a + b * b;
                if (s > 9f) { float tau = 3f / Mathf.Sqrt(s); m[i] = tau * a * d[i]; m[i + 1] = tau * b * d[i]; }
            }
            int k = 0;
            while (k < n - 2 && x > p[k + 1].x) k++;
            float h = p[k + 1].x - p[k].x, t = (x - p[k].x) / h;
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p[k].y + (t3 - 2f * t2 + t) * h * m[k] + (-2f * t3 + 3f * t2) * p[k + 1].y + (t3 - t2) * h * m[k + 1];
        }
    }
}
