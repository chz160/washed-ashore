using System;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Pure north-boundary rule (spec B4). No MonoBehaviour or scene state, so a server can
    /// run the same check later. The line is a polyline of XZ points sorted by X; past either
    /// end it continues flat, so nothing gets round the ends. North is +Z.
    /// </summary>
    [Serializable]
    public sealed class WorldBoundsRule
    {
        readonly Vector2[] line;
        readonly float snapInset;

        public WorldBoundsRule(Vector2[] lineXZ, float snapInset)
        {
            if (lineXZ == null || lineXZ.Length == 0)
                throw new ArgumentException("North line needs at least one point", nameof(lineXZ));
            line = (Vector2[])lineXZ.Clone();
            Array.Sort(line, (a, b) => a.x.CompareTo(b.x));
            this.snapInset = Mathf.Max(0f, snapInset);
        }

        public Vector2[] Line => (Vector2[])line.Clone();

        /// <summary>Unity Z of the north line at world X.</summary>
        public float LineZAt(float x)
        {
            if (x <= line[0].x) return line[0].y;
            for (int i = 1; i < line.Length; i++)
            {
                if (x > line[i].x) continue;
                var a = line[i - 1];
                var b = line[i];
                float span = b.x - a.x;
                return span <= 0f ? Mathf.Min(a.y, b.y) : Mathf.Lerp(a.y, b.y, (x - a.x) / span);
            }
            return line[line.Length - 1].y;
        }

        /// <summary>How far north of the line a point is; positive means it has crossed.</summary>
        public float NorthOfLine(Vector3 position) => position.z - LineZAt(position.x);

        public bool IsNorth(Vector3 position) => NorthOfLine(position) > 0f;

        /// <summary>
        /// Returns true and the corrected position when <paramref name="position"/> is north of
        /// the line. The correction moves only Z, to <c>snapInset</c> metres south of the line.
        /// </summary>
        public bool TryConstrain(Vector3 position, out Vector3 corrected)
        {
            corrected = position;
            if (!IsNorth(position)) return false;
            corrected.z = LineZAt(position.x) - snapInset;
            return true;
        }
    }
}
