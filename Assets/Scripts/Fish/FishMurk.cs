using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>
    /// The one fish visibility rule (adr/fish-1 T7, gate/fish-technique addendum): the water shader's Beer-Lambert murk
    /// and Schlick Fresnel, evaluated on the CPU. The renderer culls with it and the sim's spawn rule uses it, so
    /// "drawn" and "could be seen" never disagree. The values mirror BellsBendWater.shader (_Extinction, _MinCosView,
    /// _F0); whoever holds the water material fills one instance at startup with <see cref="FromWater"/>. The sim holds
    /// no Material, so a headless server can skip this entirely.
    /// </summary>
    [System.Serializable]
    public struct FishMurk
    {
        public const float DefaultFloor = 0.02f;

        [Tooltip("Water _Extinction RGB, 1/m.")]
        public Vector3 extinction;
        [Tooltip("Water _MinCosView: the smallest view cosine used for the path length.")]
        public float minCosView;
        [Tooltip("Water _F0: Fresnel reflectance straight down.")]
        public float f0;
        [Tooltip("A body is drawable when (1 - F) x Tavg is at least this.")]
        public float floor;

        public FishMurk(Vector3 extinction, float minCosView, float f0, float floor = DefaultFloor)
        {
            this.extinction = extinction;
            this.minCosView = minCosView;
            this.f0 = f0;
            this.floor = floor;
        }

        /// <summary>Reads the water material's terms (shader property names, not copied numbers).</summary>
        public static FishMurk FromWater(Material water, float floor = DefaultFloor)
        {
            Vector4 e = water.GetVector("_Extinction");
            return new FishMurk(new Vector3(e.x, e.y, e.z), water.GetFloat("_MinCosView"), water.GetFloat("_F0"), floor);
        }

        /// <summary>
        /// (1 - F) x Tavg for a body whose shallowest point is <paramref name="depth"/> metres under the surface, seen
        /// along <paramref name="viewY"/> = the y of the unit vector from the fish to the camera. Depth &lt;= 0 (a jump) is 1.
        /// </summary>
        public float Visibility(float depth, float viewY)
        {
            if (depth <= 0f) return 1f;
            float vy = Mathf.Clamp01(viewY);
            float path = depth / Mathf.Max(vy, minCosView);
            float t = (Mathf.Exp(-extinction.x * path) + Mathf.Exp(-extinction.y * path) + Mathf.Exp(-extinction.z * path)) / 3f;
            float c = 1f - vy;
            float fresnel = f0 + (1f - f0) * c * c * c * c * c; // Schlick on the flat surface normal
            return (1f - fresnel) * t;
        }

        /// <summary>
        /// True when the body can show through the water. <paramref name="bodyTopY"/> is the shallowest point of the body
        /// (centre + half its clip-max height), so the CPU test is conservative against the per-pixel shader.
        /// </summary>
        public bool Drawable(float surfaceY, float bodyTopY, Vector3 bodyCentre, Vector3 camera)
        {
            Vector3 v = camera - bodyCentre;
            float len = v.magnitude;
            float viewY = len > 1e-5f ? v.y / len : 1f;
            return Visibility(surfaceY - bodyTopY, viewY) >= floor;
        }
    }
}
