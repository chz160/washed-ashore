using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Birds
{
    /// <summary>
    /// Ground height, slope and terrain-tree crowns for the birds. Trees are bucketed on a 16 m grid; a crown is
    /// a vertical cylinder (prototype mesh radius x widthScale) from a fifth of the tree's height to its top.
    /// Rocks (prototype name "Rock_*") are not trees. Built once per terrain and shared by every bird.
    /// </summary>
    public class BirdTerrain
    {
        const float Cell = 16f;
        const float CrownBase = 0.2f;

        public struct Tree
        {
            public Vector2 xz;
            public float baseY, topY, radius;
            public float CrownBottom => Mathf.Lerp(baseY, topY, CrownBase);
        }

        static BirdTerrain cached;
        readonly Terrain terrain;
        readonly Dictionary<Vector2Int, List<Tree>> cells = new Dictionary<Vector2Int, List<Tree>>();
        float maxRadius;

        public static BirdTerrain Active
        {
            get
            {
                var t = Terrain.activeTerrain;
                if (cached == null || cached.terrain != t) cached = t ? new BirdTerrain(t) : null;
                return cached;
            }
        }

        public Terrain Terrain => terrain;

        BirdTerrain(Terrain t)
        {
            terrain = t;
            var d = t.terrainData;
            var o = t.transform.position;
            var protos = d.treePrototypes;
            var shapes = new (float top, float radius)[protos.Length];
            for (int i = 0; i < protos.Length; i++)
                shapes[i] = protos[i].prefab && !protos[i].prefab.name.StartsWith("Rock_") ? Shape(protos[i].prefab) : (0f, 0f);
            foreach (var inst in d.treeInstances)
            {
                var s = shapes[inst.prototypeIndex];
                if (s.top <= 0f) continue;
                var w = Vector3.Scale(inst.position, d.size) + o;
                var tree = new Tree
                {
                    xz = new Vector2(w.x, w.z), baseY = w.y,
                    topY = w.y + s.top * inst.heightScale, radius = s.radius * inst.widthScale,
                };
                maxRadius = Mathf.Max(maxRadius, tree.radius);
                var key = Key(tree.xz);
                if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Tree>();
                list.Add(tree);
            }
        }

        public float Height(Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;

        public Vector3 OnGround(Vector3 p)
        {
            p.y = Height(p);
            return p;
        }

        public float Slope(Vector3 p)
        {
            var d = terrain.terrainData;
            var o = terrain.transform.position;
            return d.GetSteepness((p.x - o.x) / d.size.x, (p.z - o.z) / d.size.z);
        }

        public bool Inside(Vector3 p, float margin)
        {
            var d = terrain.terrainData;
            Vector3 l = p - terrain.transform.position;
            return l.x >= margin && l.z >= margin && l.x <= d.size.x - margin && l.z <= d.size.z - margin;
        }

        /// <summary>Highest crown top whose cylinder (plus <paramref name="radius"/>) covers <paramref name="p"/>; the ground if none.</summary>
        public float CanopyTop(Vector3 p, float radius)
        {
            float top = Height(p);
            Visit(p, radius, t => top = Mathf.Max(top, t.topY));
            return top;
        }

        /// <summary>True if a sphere at <paramref name="p"/> touches a crown cylinder.</summary>
        public bool InCrown(Vector3 p, float radius)
        {
            bool hit = false;
            Visit(p, radius, t => hit |= p.y + radius >= t.CrownBottom && p.y - radius <= t.topY);
            return hit;
        }

        /// <summary>True if any crown or trunk lies within <paramref name="radius"/> (flat) of <paramref name="p"/>.</summary>
        public bool TreeNear(Vector3 p, float radius)
        {
            bool hit = false;
            Visit(p, radius, _ => hit = true);
            return hit;
        }

        void Visit(Vector3 p, float radius, System.Action<Tree> onHit)
        {
            var xz = new Vector2(p.x, p.z);
            float reach = radius + maxRadius;
            var min = Key(xz - Vector2.one * reach);
            var max = Key(xz + Vector2.one * reach);
            for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
            {
                if (!cells.TryGetValue(new Vector2Int(x, z), out var list)) continue;
                foreach (var t in list)
                {
                    float r = t.radius + radius;
                    if ((t.xz - xz).sqrMagnitude <= r * r) onHit(t);
                }
            }
        }

        static Vector2Int Key(Vector2 xz) => new Vector2Int(Mathf.FloorToInt(xz.x / Cell), Mathf.FloorToInt(xz.y / Cell));

        static (float top, float radius) Shape(GameObject prefab)
        {
            float top = 0f, radius = 0f;
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var bb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
                    var p = prefab.transform.InverseTransformPoint(mf.transform.TransformPoint(bb.min + Vector3.Scale(bb.size, c)));
                    top = Mathf.Max(top, p.y);
                    radius = Mathf.Max(radius, new Vector2(p.x, p.z).magnitude);
                }
            }
            return (top, radius);
        }
    }
}
