using System;
using UnityEngine;

namespace WashedAshore.Level
{
    public enum StructureKind { Bluff, FerryLanding, Slipway, IslandFace, ChuteMouth, HollowMouth }

    // Shoreline structure anchors for Bells Bend, written by BellsBendStructureAnchorsBuilder from Data/terrain/build.
    // Each anchor is a run of inner-bank stations: a waterline polyline in world XZ, the outward (water-side) unit normal
    // at each point, and a reach in metres out from the bank. Geometry only; consumers (fish, later pods) attach meaning.
    // Short anchors (mouths, landing, slipway) come before long runs, so on a distance tie the specific anchor wins.
    public class BellsBendStructureAnchors : ScriptableObject
    {
        public const string AssetPath = "Assets/World/BellsBend/StructureAnchors.asset";

        [Serializable]
        public class Anchor
        {
            public StructureKind kind;
            public string name;
            [Tooltip("Waterline points, world XZ, in bank-station order.")]
            public Vector2[] points;
            [Tooltip("Outward (water-side) unit normal at each point.")]
            public Vector2[] normals;
            [Tooltip("How far out from the waterline the anchor reaches, m.")]
            public float reach = 20f;
            [Tooltip("Polyline length along the bank, m.")]
            public float length;
            [Tooltip("Inclusive bank_stations.json index range the polyline was taken from.")]
            public int stationFrom, stationTo;
            [Tooltip("XZ bounds of the points grown by reach (x = world X, y = world Z); TryNearest's quick reject.")]
            public Rect bounds;
        }

        [Tooltip("First 16 hex chars of SHA-256 over bank_stations.json + map_vectors.json at build time.")]
        public string sourceSha16;
        public Anchor[] anchors = new Anchor[0];
        [Tooltip("Union of every anchor's bounds: outside it, TryNearest is always false.")]
        public Rect bounds;

        /// <summary>
        /// Nearest anchor whose reach covers xz on the water side (dot with the interpolated normal > 0).
        /// Ties keep the lower index. Returns false when no anchor covers xz.
        /// </summary>
        public bool TryNearest(Vector2 xz, out int anchor, out float distance)
        {
            anchor = -1;
            distance = float.MaxValue;
            for (int a = 0; a < anchors.Length; a++)
            {
                var an = anchors[a];
                var p = an.points;
                if (p == null || p.Length == 0 || !an.bounds.Contains(xz)) continue;
                for (int i = 0; i < p.Length; i++)
                {
                    int j = Mathf.Min(i + 1, p.Length - 1);
                    Vector2 ab = p[j] - p[i];
                    float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(xz - p[i], ab) / ab.sqrMagnitude) : 0f;
                    Vector2 c = p[i] + ab * t;
                    float d = Vector2.Distance(xz, c);
                    if (d > an.reach || d >= distance) continue;
                    Vector2 n = Vector2.Lerp(an.normals[i], an.normals[j], t);
                    if (Vector2.Dot(xz - c, n) <= 0f) continue;
                    anchor = a;
                    distance = d;
                }
            }
            return anchor >= 0;
        }
    }
}
