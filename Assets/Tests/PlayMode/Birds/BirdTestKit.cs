using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Birds;
using WashedAshore.Wildlife;
using WashedAshore.Tests.Wildlife;

namespace WashedAshore.Tests.Birds
{
    /// <summary>Shared setup and measurements for the bird PlayMode tests. The measurements use their own geometry
    /// (head bones, renderer bounds, terrain trees, the wildlife route maths) rather than the runtime's, so a test
    /// can't pass because it asks the code under test what it did.</summary>
    static class BirdTestKit
    {
        public static IEnumerator LoadWorld(int seed)
        {
            BirdRandom.OverrideSeed(seed);
            yield return WildlifeTestKit.LoadWorld(seed);
        }

        public static void Reset()
        {
            BirdRandom.OverrideSeed(null);
            WildlifeRandom.OverrideSeed(null);
            Time.timeScale = 1f;
        }

        public static BirdPopulation Population()
        {
            var pop = Object.FindAnyObjectByType<BirdPopulation>();
            Assert.IsNotNull(pop, "No BirdPopulation in World");
            Assert.IsNull(pop.PlacementError, $"Bird placement failed: {pop.PlacementError}");
            return pop;
        }

        public static List<Vector3> Route()
        {
            var wild = WildlifeTestKit.Population();
            return WildlifePopulation.Route(wild.Tuning, wild.PlayerSpawn.position);
        }

        /// <summary>Flat distance from <paramref name="p"/> to the route polyline (the wildlife harness's maths).</summary>
        public static float RouteDistance(List<Vector3> route, Vector3 p) => WildlifeRules.RouteDistance(route, new Vector2(p.x, p.z));

        public static int ObstacleMask() => WildlifeTestKit.OccluderMask();

        public static float TerrainY(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t.SampleHeight(p) + t.transform.position.y;
        }

        // ---- Animator ------------------------------------------------------------------------------

        /// <summary>The clip carrying the most weight on layer 0, and that state's normalised time (0-1).</summary>
        public static (string clip, float phase) Dominant(Animator animator)
        {
            bool next = animator.IsInTransition(0) && animator.GetAnimatorTransitionInfo(0).normalizedTime > 0.5f;
            var clips = next ? animator.GetNextAnimatorClipInfo(0) : animator.GetCurrentAnimatorClipInfo(0);
            var state = next ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            if (clips.Length == 0) return (null, 0f);
            string name = clips.OrderByDescending(c => c.weight).First().clip.name;
            return (name, Mathf.Repeat(state.normalizedTime, 1f));
        }

        public static bool IsFlap(string clip) => clip != null && clip.IndexOf("flap", System.StringComparison.OrdinalIgnoreCase) >= 0;
        public static bool IsGlide(string clip) => clip != null && clip.IndexOf("glid", System.StringComparison.OrdinalIgnoreCase) >= 0;

        // ---- Visibility (brief 3.4) ------------------------------------------------------------------

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// <summary>In the frustum, within range, and one of the two rays (bounds centre, top-centre) clear of terrain
        /// and Default colliders and, when <paramref name="foliage"/> is given, passing its foliage test too.</summary>
        public static bool Visible(Vector3 eye, Plane[] planes, Bounds b, float range, int mask, Transform player, WildlifeFoliage foliage)
        {
            if (Vector3.Distance(eye, b.center) > range || !GeometryUtility.TestPlanesAABB(planes, b)) return false;
            return RayClear(eye, b.center, mask, player, foliage)
                   || RayClear(eye, new Vector3(b.center.x, b.max.y, b.center.z), mask, player, foliage);
        }

        static bool RayClear(Vector3 from, Vector3 to, int mask, Transform player, WildlifeFoliage foliage)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            int n = Physics.RaycastNonAlloc(from, d / len, Hits, len, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i].collider.transform;
                if (h.IsChildOf(player) || h.GetComponentInParent<WildlifeAgent>()) continue;
                return false;
            }
            // A zero-size Bounds makes SeesThrough test exactly this one ray.
            return foliage == null || foliage.SeesThrough(from, new Bounds(to, Vector3.zero));
        }

        // ---- Facing (head bone, not transform.forward) ---------------------------------------------

        public static Transform HeadBone(Component bird)
        {
            var bones = bird.GetComponentsInChildren<Transform>();
            return bones.FirstOrDefault(b => b.name == "Head") ?? bones.FirstOrDefault(b => b.name == "DEF-head")
                   ?? bones.FirstOrDefault(b => b.name.ToLowerInvariant().EndsWith("head"));
        }

        /// <summary>Flat direction from the bird's body centre (renderer bounds) to its head bone.</summary>
        public static Vector3 ModelForward(Transform head, Bounds body)
        {
            Vector3 f = head.position - body.center;
            f.y = 0f;
            return f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.zero;
        }

        public class Facing
        {
            public const float MinDot = 0.7f, MinShare = 0.95f;
            public const int MinSamples = 5;
            public int Samples, Ok;
            public float MinSeen = 1f;
            public float Share => Samples == 0 ? 1f : (float)Ok / Samples;

            public void Add(Vector3 modelForward, Vector3 velocity)
            {
                velocity.y = 0f;
                if (velocity.sqrMagnitude < 1f) return; // < 1 m/s flat: no meaningful heading
                float dot = Vector3.Dot(modelForward, velocity.normalized);
                Samples++;
                if (dot >= MinDot) Ok++;
                MinSeen = Mathf.Min(MinSeen, dot);
            }

            public string Failure(string who) =>
                Samples < MinSamples ? $"{who}: only {Samples} facing samples"
                : Share < MinShare ? $"{who}: model faced its flight in {Share:P0} of {Samples} samples (min dot {MinSeen:F2})" : null;

            public override string ToString() => $"facing {Ok}/{Samples} ({Share:P0}, min dot {MinSeen:F2})";
        }

        /// <summary>Half the largest horizontal extent of the bird's skinned mesh (its half wingspan).</summary>
        public static float BodyRadius(Component bird)
        {
            float r = 0f;
            foreach (var smr in bird.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var space = smr.rootBone ? smr.rootBone : smr.transform;
                var e = Vector3.Scale(smr.localBounds.extents, space.lossyScale);
                r = Mathf.Max(r, Mathf.Max(Mathf.Abs(e.x), Mathf.Abs(e.z)));
            }
            return r;
        }
    }

    /// <summary>
    /// The test's own model of terrain-tree crowns (independent of the runtime's BirdTerrain). Crown top = prototype
    /// mesh top x heightScale + base y (the WildlifeFoliage.PrototypeHeight rule); the crown volume is a cylinder of
    /// 0.8 x the prototype's horizontal mesh extent x widthScale from 30% of the tree's height to its top. Rocks are
    /// not trees. Extra crowns can be injected for positive controls.
    /// </summary>
    class TestCrowns
    {
        public readonly List<(Vector2 xz, float bottom, float top, float radius)> Trees = new List<(Vector2, float, float, float)>();

        public TestCrowns(Terrain terrain)
        {
            var d = terrain.terrainData;
            var o = terrain.transform.position;
            var protos = d.treePrototypes;
            var shape = protos.Select(p => p.prefab && !p.prefab.name.StartsWith("Rock_") ? Shape(p.prefab) : (0f, 0f)).ToArray();
            foreach (var inst in d.treeInstances)
            {
                var (h, r) = shape[inst.prototypeIndex];
                if (h <= 0f) continue;
                var w = Vector3.Scale(inst.position, d.size) + o;
                float top = w.y + h * inst.heightScale;
                Trees.Add((new Vector2(w.x, w.z), Mathf.Lerp(w.y, top, 0.3f), top, 0.8f * r * inst.widthScale));
            }
        }

        public void Inject(Vector3 at, float radius, float bottom, float top) => Trees.Add((new Vector2(at.x, at.z), bottom, top, radius));

        /// <summary>True if a sphere at <paramref name="p"/> is inside a crown cylinder.</summary>
        public bool Inside(Vector3 p, float radius)
        {
            var xz = new Vector2(p.x, p.z);
            foreach (var (c, bottom, top, r) in Trees)
                if ((c - xz).sqrMagnitude <= (r + radius) * (r + radius) && p.y + radius >= bottom && p.y - radius <= top) return true;
            return false;
        }

        /// <summary>Highest crown top of any tree whose trunk is within <paramref name="within"/> m (flat), or whose
        /// crown covers <paramref name="p"/>; -infinity if none.</summary>
        public float TopWithin(Vector3 p, float within)
        {
            var xz = new Vector2(p.x, p.z);
            float best = float.NegativeInfinity;
            foreach (var (c, _, top, r) in Trees)
            {
                float d2 = (c - xz).sqrMagnitude;
                if (d2 <= within * within || d2 <= r * r) best = Mathf.Max(best, top);
            }
            return best;
        }

        static (float, float) Shape(GameObject prefab)
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
                    radius = Mathf.Max(radius, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)));
                }
            }
            return (top, radius);
        }
    }
}
