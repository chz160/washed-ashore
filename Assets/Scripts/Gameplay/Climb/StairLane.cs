using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// One flight as a straight lane along the nosing line (tower-design-spec.md §2.3, tower-td-note §6): where the
    /// feet stand at the lower end, at the upper end, and the clear width. A ladder is a lane at 90 degrees.
    /// The lane parameter <c>u</c> is metres up the slope from the foot; below 0 and above <see cref="Length"/> it
    /// runs level at that end's height (the run-in and run-out), in horizontal metres.
    /// </summary>
    [System.Serializable]
    public struct StairLane
    {
        public Vector3 foot;
        public Vector3 head;
        [Tooltip("Clear width between the stringers, m.")]
        public float width;
        [Tooltip("Leaving through the foot leaves the climb area (the ground at the first flight).")]
        public bool footLeavesArea;
        [Tooltip("Level floor beyond the foot, along the lane, that the capsule's centre can be carried over, m (from the model).")]
        public float footRunRoom;
        [Tooltip("Level floor beyond the head, along the lane, that the capsule's centre can be carried over, m (from the model).")]
        public float headRunRoom;
        [Tooltip("How far to the left / right of the line (for someone facing up the lane) a body can stand on the final level stretch at a free foot, m: samples every reachSpacing from the free end toward the flight or junction, never increasing (from the model). Empty = no limit from the model.")]
        public float[] footReachLeft, footReachRight;
        [Tooltip("The same at a free head.")]
        public float[] headReachLeft, headReachRight;
        [Tooltip("Spacing of the reach samples, m.")]
        public float reachSpacing;

        public StairLane(Vector3 foot, Vector3 head, float width, bool footLeavesArea = false,
            float footRunRoom = float.PositiveInfinity, float headRunRoom = float.PositiveInfinity)
        {
            this.foot = foot;
            this.head = head;
            this.width = width;
            this.footLeavesArea = footLeavesArea;
            this.footRunRoom = footRunRoom;
            this.headRunRoom = headRunRoom;
            footReachLeft = footReachRight = headReachLeft = headReachRight = null;
            reachSpacing = 0f;
        }

        /// <summary>Slope length, foot to head, m.</summary>
        public float Length => Vector3.Distance(foot, head);
        /// <summary>Unit vector up the slope.</summary>
        public Vector3 Axis => (head - foot).normalized;
        /// <summary>Unit horizontal vector pointing up the flight (zero for a vertical lane).</summary>
        public Vector3 AxisHorizontal => new Vector3(head.x - foot.x, 0f, head.z - foot.z).normalized;
        /// <summary>Unit horizontal vector to the right of someone facing up the flight.</summary>
        public Vector3 Right => Vector3.Cross(Vector3.up, AxisHorizontal);
        /// <summary>Pitch above the horizontal, degrees.</summary>
        public float Pitch => Mathf.Asin(Mathf.Clamp(Axis.y, -1f, 1f)) * Mathf.Rad2Deg;
        /// <summary>Horizontal metres per slope metre.</summary>
        public float RunPerMetre => Vector3.Dot(Axis, AxisHorizontal);

        /// <summary>The nosing line at <paramref name="u"/>, <paramref name="lateral"/> m right of the centre line.</summary>
        public Vector3 Point(float u, float lateral)
        {
            float length = Length;
            Vector3 p;
            if (u < 0f) p = foot + AxisHorizontal * u;
            else if (u > length) p = head + AxisHorizontal * (u - length);
            else p = foot + Axis * u;
            return p + Right * lateral;
        }

        /// <summary>The same lane moved by a transform (lanes are stored in their area's local space).</summary>
        public StairLane Transformed(Transform t)
        {
            return new StairLane(t.TransformPoint(foot), t.TransformPoint(head), width, footLeavesArea, footRunRoom, headRunRoom) { footReachLeft = footReachLeft, footReachRight = footReachRight, headReachLeft = headReachLeft, headReachRight = headReachRight, reachSpacing = reachSpacing };
        }
    }
}
