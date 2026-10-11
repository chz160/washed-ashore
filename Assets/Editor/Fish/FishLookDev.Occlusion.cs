using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WashedAshore.Fish.Editor
{
    // Slot #19 (f-qa): per-row facts for the legibility table. Line of sight from the eye to the sign point against the
    // terrain heightfield, terrain trees (each instance as its prototype's renderer bounds: a vertical cylinder of the
    // crown's half-width, scaled per instance; conservative) and any physics collider; plus the projected height of the
    // drawn sign in the 1920x1080 frame. Used to search the nearest clear point along a ring for an occluded grid point.
    public static partial class FishLookDev
    {
        struct LegTree { public Vector3 pos; public float radius, bottom, top; public string name; }
        static List<LegTree> legTrees;

        static void LegCollectTrees()
        {
            legTrees = new List<LegTree>();
            var protoBounds = new Dictionary<GameObject, Bounds>();
            foreach (var t in Object.FindObjectsByType<Terrain>())
            {
                var d = t.terrainData;
                Vector3 origin = t.GetPosition(), size = d.size;
                foreach (var inst in d.treeInstances)
                {
                    var prefab = d.treePrototypes[inst.prototypeIndex].prefab;
                    if (!prefab) continue;
                    if (!protoBounds.TryGetValue(prefab, out var b))
                    {
                        var tmp = Object.Instantiate(prefab); tmp.hideFlags = HideFlags.HideAndDontSave;
                        tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        var rs = tmp.GetComponentsInChildren<Renderer>();
                        b = rs.Length > 0 ? rs.Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; }) : new Bounds(Vector3.up, Vector3.one * 2f);
                        Object.DestroyImmediate(tmp);
                        protoBounds[prefab] = b;
                    }
                    var p = origin + Vector3.Scale(inst.position, size);
                    legTrees.Add(new LegTree
                    {
                        pos = p, radius = Mathf.Max(b.extents.x, b.extents.z) * inst.widthScale,
                        bottom = p.y + (b.center.y - b.extents.y) * inst.heightScale, top = p.y + (b.center.y + b.extents.y) * inst.heightScale,
                        name = prefab.name,
                    });
                }
            }
        }

        /// <summary>What blocks the eye-to-point line ("" = clear): "terrain", "tree:&lt;prefab&gt;" or "collider:&lt;name&gt;".</summary>
        static string LegOccluder(Vector3 eye, Vector3 point)
        {
            if (legTrees == null) LegCollectTrees();
            Vector3 d = point - eye;
            float len = d.magnitude;
            var dir = d / len;
            // Terrain: sample the heightfield along the line (the last 0.5 m is the sign itself, on the water).
            for (float s = 0.5f; s < len - 0.5f; s += 0.5f)
            {
                var q = eye + dir * s;
                foreach (var t in Terrain.activeTerrains)
                {
                    var o = t.GetPosition(); var sz = t.terrainData.size;
                    if (q.x < o.x || q.z < o.z || q.x > o.x + sz.x || q.z > o.z + sz.z) continue;
                    if (t.SampleHeight(q) + o.y > q.y) return "terrain";
                }
            }
            float minX = Mathf.Min(eye.x, point.x) - 15f, maxX = Mathf.Max(eye.x, point.x) + 15f, minZ = Mathf.Min(eye.z, point.z) - 15f, maxZ = Mathf.Max(eye.z, point.z) + 15f;
            foreach (var tr in legTrees)
            {
                if (tr.pos.x < minX || tr.pos.x > maxX || tr.pos.z < minZ || tr.pos.z > maxZ) continue;
                for (float s = 0.25f; s < len - 0.25f; s += 0.25f)
                {
                    var q = eye + dir * s;
                    if (q.y < tr.bottom || q.y > tr.top) continue;
                    float dx = q.x - tr.pos.x, dz = q.z - tr.pos.z;
                    if (dx * dx + dz * dz < tr.radius * tr.radius) return "tree:" + tr.name;
                }
            }
            Physics.SyncTransforms();
            return Physics.Raycast(eye, dir, out var hit, len - 0.25f) ? "collider:" + hit.collider.name : "";
        }

        /// <summary>Projected height (px) of a flat quad (width x length along heading) on the water, in the 1920x1080 frame.</summary>
        static float LegProjectedHeightPx(Camera cam, Vector3 at, Vector3 heading, float width, float length)
        {
            var fwd = new Vector3(heading.x, 0f, heading.z).normalized;
            var right = Vector3.Cross(Vector3.up, fwd);
            var ys = new[] { -1f, 1f }.SelectMany(a => new[] { -1f, 1f }.Select(b => LegPx(cam, at + right * (a * width * 0.5f) + fwd * (b * length * 0.5f)).y)).ToArray();
            return ys.Max() - ys.Min();
        }

        /// <summary>
        /// The nearest point on the ring of radius d around the eye (horizontal), within +-maxOffset metres of arc, that is water,
        /// in frame for cam and has a clear line of sight. offsetM is the signed arc offset (+ = clockwise seen from above).
        /// </summary>
        static bool LegNearestClear(Vector3 eye, Vector3 facing, float d, float W, Camera cam, float maxOffset, out Vector3 at, out float offsetM)
        {
            for (float s = 1f; s <= maxOffset; s += 1f)
                foreach (float sign in new[] { 1f, -1f })
                {
                    var f = Quaternion.AngleAxis(sign * s / d * Mathf.Rad2Deg, Vector3.up) * facing;
                    var p = WithY(new Vector3(eye.x, 0f, eye.z) + f * d, W);
                    if (W - WashedAshore.Gameplay.TerrainQuery.Height(p) <= 0.05f) continue;
                    var px = LegPx(cam, p);
                    if (px.z <= 0f || px.x < 0f || px.x >= 1920f || px.y < 0f || px.y >= 1080f) continue;
                    if (LegOccluder(eye, p).Length > 0) continue;
                    at = p; offsetM = sign * s;
                    return true;
                }
            at = default; offsetM = 0f;
            return false;
        }
    }
}
