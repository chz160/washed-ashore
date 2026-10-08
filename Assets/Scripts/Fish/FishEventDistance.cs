using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>
    /// The one surface-sign distance rule (f-td, f-qa ruling: flat): horizontal XZ distance, inclusive at the radius.
    /// Callers: FishEventScheduler admission, the F7 counters, and FishSurfaceFx.InEventRadius (rendering delegates here),
    /// so a sign the scheduler raises within the radius is one the renderer draws and the counter may count.
    /// </summary>
    public static class FishEventDistance
    {
        /// <summary>Horizontal (XZ) distance between two points, metres.</summary>
        public static float Flat(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        /// <summary>True when <paramref name="a"/> is within <paramref name="radius"/> of <paramref name="b"/> horizontally (inclusive).</summary>
        public static bool Within(Vector3 a, Vector3 b, float radius)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z <= radius * radius;
        }
    }
}
