using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Birds
{
    public static partial class BirdPlacementRules
    {
        /// <summary>The closed route split into equal-length arcs from W0 (0-based); same maths as WildlifeRules.RouteArcs.</summary>
        public class Arcs
        {
            readonly List<Vector3> route;
            readonly float[] cum;
            readonly int count;
            public float Length { get; }
            public float ArcLength => Length / count;

            public Arcs(List<Vector3> closedRoute, int arcCount)
            {
                route = closedRoute; count = Mathf.Max(1, arcCount);
                cum = new float[route.Count];
                for (int i = 1; i < route.Count; i++) cum[i] = cum[i - 1] + Vector2.Distance(Flat(route[i - 1]), Flat(route[i]));
                Length = cum[route.Count - 1];
            }

            public float Wrap(float s) => Length > 0f ? ((s % Length) + Length) % Length : 0f;

            public Vector3 PointAt(float s)
            {
                s = Wrap(s);
                for (int i = 0; i + 1 < route.Count; i++)
                    if (s <= cum[i + 1]) return Vector3.Lerp(route[i], route[i + 1], Mathf.InverseLerp(cum[i], cum[i + 1], s));
                return route[route.Count - 1];
            }

            public Vector3 Tangent(float s)
            {
                s = Wrap(s);
                for (int i = 0; i + 1 < route.Count; i++)
                    if (s <= cum[i + 1]) { var v = route[i + 1] - route[i]; v.y = 0f; return v.normalized; }
                return Vector3.forward;
            }

            /// <summary>Arc length of the single nearest route point; ties break to the lower segment.</summary>
            public float NearestS(Vector2 p)
            {
                float best = float.MaxValue, bestS = 0f;
                for (int i = 0; i + 1 < route.Count; i++)
                {
                    Vector2 a = Flat(route[i]), ab = Flat(route[i + 1]) - a;
                    float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                    float dist = Vector2.Distance(p, a + ab * u);
                    if (dist < best) { best = dist; bestS = cum[i] + u * ab.magnitude; }
                }
                return bestS;
            }

            public float Distance(Vector2 p) => Vector2.Distance(p, Flat(PointAt(NearestS(p))));

            public int ArcOf(Vector2 p) => Mathf.Min(count - 1, Mathf.FloorToInt(NearestS(p) / ArcLength));
        }
    }
}
