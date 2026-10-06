using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Birds
{
    /// <summary>
    /// Level-design record on the "Birds" root: the baked <see cref="BirdPlan"/> (seed, patches with validated
    /// robin starts, flock primary/secondary POIs) and the tallest crown. Written by the editor BirdPlacer;
    /// runtime code reads it instead of re-deriving placement. Re-plan per seed with <see cref="BirdPlacementRules.Plan"/>.
    /// </summary>
    public class BirdLayout : MonoBehaviour
    {
        public int seed;
        public float tallestCrown, tallestCrownTopY;
        public List<PatchPlan> patches = new List<PatchPlan>();
        public List<FlockPlan> flocks = new List<FlockPlan>();

        public void Set(BirdPlan plan)
        {
            seed = plan.seed;
            tallestCrown = plan.tallestCrown;
            tallestCrownTopY = plan.tallestCrownTopY;
            patches = plan.patches;
            flocks = plan.flocks;
        }

        void OnDrawGizmosSelected()
        {
            foreach (var p in patches)
            {
                Gizmos.color = p.type == PatchType.Trail ? new Color(1f, 0.5f, 0.1f) : new Color(0.3f, 0.9f, 0.3f);
                foreach (var s in p.starts) Gizmos.DrawSphere(s + Vector3.up * 0.2f, 0.3f);
            }
            Gizmos.color = new Color(0.2f, 0.2f, 0.3f);
            foreach (var f in flocks) Gizmos.DrawLine(f.primary + Vector3.up * 30f, f.secondary + Vector3.up * 30f);
        }
    }
}
