using System.Collections.Generic;
using UnityEngine;
using WashedAshore.Birds;
using WashedAshore.Fish;
using WashedAshore.Gameplay;
using WashedAshore.Wildlife;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// The one occlusion rule for "seen" (ruling/fish-f7-occlusion): the eye-to-point segment is blocked by terrain
    /// (heights, and the terrain collider with its tree trunks) and by static colliders on visible geometry (an object or
    /// parent with a Renderer or LODGroup: props, buildings, rocks). Not occluders: Water, WorldBounds, renderer-less
    /// colliders (barrier, invisible blockers), triggers, the player's capsule, birds, wildlife and fish. Canopies have no
    /// colliders, so they aren't modelled (accepted limitation). Every excluded collider class is recorded once, and each
    /// test call reports which occluder class blocked it ("terrain", "tree", "prop:&lt;name&gt;" or "" when clear).
    /// </summary>
    static class FishOcclusion
    {
        public static readonly SortedSet<string> Excluded = new SortedSet<string>();
        /// <summary>
        /// f-td C2 review 3: colliders the old rule (Linecast on WorldLayers.SightMask) hit where this rule sees clear, as
        /// "name|type|layer|eyeInside" with a count: what caused the earlier false occlusion.
        /// </summary>
        public static readonly SortedDictionary<string, int> OldMaskFalseBlocks = new SortedDictionary<string, int>();
        static readonly RaycastHit[] hits = new RaycastHit[64];

        public static bool Blocked(Vector3 eye, Vector3 point, out string by)
        {
            by = "";
            // Terrain heights (the ground surface itself, independent of colliders).
            float flat = FishTestKit.Flat(eye, point);
            int steps = Mathf.Max(2, Mathf.CeilToInt(flat / 0.5f));
            for (int i = 1; i < steps; i++)
            {
                var p = Vector3.Lerp(eye, point, i / (float)steps);
                if (TerrainQuery.TryGroundHeight(p, out float g) && g > p.y) { by = "terrain"; return true; }
            }
            // Colliders along the segment, Water and WorldBounds left out, triggers ignored.
            Vector3 d = point - eye;
            float len = d.magnitude;
            if (len < 1e-3f) return false;
            int mask = ~((1 << WorldLayers.Water) | (1 << WorldLayers.WorldBounds));
            int n = Physics.RaycastNonAlloc(eye, d / len, hits, len, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c is TerrainCollider) { by = "tree"; return true; }   // the terrain collider's hits off the heightfield are tree trunks
                string cls = Classify(c);
                if (cls == null) { by = "prop:" + c.name; return true; }
                Excluded.Add(cls);
            }
            OldRuleCheck(eye, point);
            return false;
        }

        static void OldRuleCheck(Vector3 eye, Vector3 point)
        {
            if (!Physics.Linecast(eye, point, out var hit, WorldLayers.SightMask, QueryTriggerInteraction.Ignore)) return;
            var c = hit.collider;
            bool inside = c.bounds.Contains(eye);
            string key = $"{c.name}|{c.GetType().Name}|{c.gameObject.layer}|{(inside ? "eyeInside" : "eyeOutside")}";
            OldMaskFalseBlocks[key] = (OldMaskFalseBlocks.TryGetValue(key, out int n) ? n : 0) + 1;
        }

        /// <summary>Null for an occluder; otherwise the excluded class it belongs to.</summary>
        static string Classify(Collider c)
        {
            if (c.GetComponentInParent<CharacterController>() || c.GetComponentInParent<PlayerController>()) return "player";
            if (c.GetComponentInParent<RobinAgent>() || c.GetComponentInParent<FlockBird>()) return "bird";
            if (c.GetComponentInParent<WildlifeAgent>()) return "wildlife";
            if (c.GetComponentInParent<FishPopulation>()) return "fish";
            bool visible = c.GetComponentInParent<Renderer>() || c.GetComponentInParent<LODGroup>() || c.GetComponentInChildren<Renderer>();
            return visible ? null : $"invisible:{c.GetType().Name}:{c.gameObject.layer}";
        }
    }
}
