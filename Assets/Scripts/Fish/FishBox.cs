using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>
    /// A fish body box for overlap tests (F5 "no overlaps", N7): the clip-max box turned by yaw as an XZ rectangle
    /// (centre, unit forward, half width, half length) plus its world Y extent. Separating-axis test on the two
    /// rectangles' four axes, then the Y intervals.
    /// </summary>
    public readonly struct FishBox
    {
        public readonly Vector2 centre, forward;
        public readonly float halfWidth, halfLength;
        public readonly Vector2 y;

        public FishBox(Vector2 centre, Vector3 forward, float halfWidth, float halfLength, Vector2 y)
        {
            this.centre = centre;
            this.forward = new Vector2(forward.x, forward.z).normalized;
            this.halfWidth = halfWidth;
            this.halfLength = halfLength;
            this.y = y;
        }

        Vector2 Right => new Vector2(forward.y, -forward.x);

        /// <summary>Overlap in XZ and in Y.</summary>
        public bool Overlaps(in FishBox o) => !(y.y <= o.y.x || o.y.y <= y.x) && OverlapsXZ(o);

        /// <summary>
        /// Overlap of the XZ footprints only. The sim's guard uses this: a fish's depth eases after its pose is cleared, so
        /// two fish stacked at different depths could otherwise drift into each other vertically.
        /// </summary>
        public bool OverlapsXZ(in FishBox o)
        {
            Vector2 d = o.centre - centre;
            return !Separated(forward, d, o) && !Separated(Right, d, o) && !Separated(o.forward, d, o) && !Separated(o.Right, d, o);
        }

        bool Separated(Vector2 axis, Vector2 d, in FishBox o)
        {
            float ra = halfLength * Mathf.Abs(Vector2.Dot(forward, axis)) + halfWidth * Mathf.Abs(Vector2.Dot(Right, axis));
            float rb = o.halfLength * Mathf.Abs(Vector2.Dot(o.forward, axis)) + o.halfWidth * Mathf.Abs(Vector2.Dot(o.Right, axis));
            return Mathf.Abs(Vector2.Dot(d, axis)) > ra + rb;
        }
    }
}
