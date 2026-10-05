using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WashedAshore.Tests.Wildlife
{
    /// <summary>
    /// Non-gating foliage check for A6 (sightingRateFoliage, wl-director's optional metric). The gating
    /// raycast only sees tree trunk colliders; this also treats any terrain tree or bush (not rocks) as
    /// blocking a sight ray that passes within 2 m of it horizontally, between its base and its crown top.
    /// Distance checks against the terrain tree instances only; no colliders are added.
    /// </summary>
    class WildlifeFoliage
    {
        const float Radius = 2f;

        readonly List<(Vector2 xz, float baseY, float topY)> trees = new List<(Vector2, float, float)>();

        public WildlifeFoliage(Terrain terrain)
        {
            var d = terrain.terrainData;
            var o = terrain.transform.position;
            var protos = d.treePrototypes;
            var heights = protos.Select(p => PrototypeHeight(p.prefab)).ToArray();
            foreach (var inst in d.treeInstances)
            {
                var prefab = protos[inst.prototypeIndex].prefab;
                if (!prefab || prefab.name.StartsWith("Rock_")) continue;
                var w = Vector3.Scale(inst.position, d.size) + o;
                trees.Add((new Vector2(w.x, w.z), w.y, w.y + heights[inst.prototypeIndex] * inst.heightScale));
            }
        }

        /// <summary>Same two rays as the gating test (to the bounds centre and top-centre); visible if either is clear.</summary>
        public bool SeesThrough(Vector3 eye, Bounds b) =>
            Clear(eye, b.center) || Clear(eye, new Vector3(b.center.x, b.max.y, b.center.z));

        bool Clear(Vector3 from, Vector3 to)
        {
            var a = new Vector2(from.x, from.z);
            var ab = new Vector2(to.x, to.z) - a;
            float len2 = Mathf.Max(ab.sqrMagnitude, 1e-4f);
            foreach (var (xz, baseY, topY) in trees)
            {
                float u = Mathf.Clamp01(Vector2.Dot(xz - a, ab) / len2);
                if ((a + ab * u - xz).sqrMagnitude > Radius * Radius) continue;
                float y = Mathf.Lerp(from.y, to.y, u);
                if (y >= baseY && y <= topY) return false;
            }
            return true;
        }

        static float PrototypeHeight(GameObject prefab)
        {
            if (!prefab) return 0f;
            float top = 0f;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var bb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
                    var corner = bb.min + Vector3.Scale(bb.size, c);
                    top = Mathf.Max(top, prefab.transform.InverseTransformPoint(mf.transform.TransformPoint(corner)).y);
                }
            }
            return top;
        }
    }
}
