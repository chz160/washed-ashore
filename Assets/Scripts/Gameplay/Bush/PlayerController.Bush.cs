using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Bush slowdown (#21, docs/design/bush-slowdown.md). A lookup of the terrain detail layers under the feet, run inside the sim tick, that
    /// scales the dry move target before the tower and stance caps. No colliders, no physics queries, no state kept between ticks.
    /// </summary>
    public partial class PlayerController
    {
        [Header("Bush slowdown")]
        [Tooltip("Per-layer multipliers. Empty: the defaults of a new BushSlowdownTuning (spec section 4).")]
        [SerializeField] BushSlowdownTuning bushTuning;

        BushCoverSampler bushSampler;

        /// <summary>The tuning in use (the assigned asset, else the defaults once the first dry move has run).</summary>
        public BushSlowdownTuning BushTuning { get { EnsureBushSampler(); return bushTuning; } }
        /// <summary>The move target's magnitude after the bush multiplier and before the tower and stance caps (spec B2).</summary>
        public float LastMoveTargetMagnitude { get; private set; }
        /// <summary>The bush multiplier the last tick used; 1 outside bushes.</summary>
        public float LastBushMultiplier { get; private set; } = 1f;

        /// <summary>Drops the cached detail window. Only a test or tool that edits the terrain detail layers at run time needs this; baked data never changes.</summary>
        public void InvalidateBushCover() => bushSampler?.Invalidate();

        void EnsureBushSampler()
        {
            if (bushSampler != null) return;
            if (bushTuning == null)
            {
                Debug.LogWarning("PlayerController: no BushSlowdownTuning assigned, using the built-in defaults.", this);
                bushTuning = ScriptableObject.CreateInstance<BushSlowdownTuning>();
            }
            bushSampler = new BushCoverSampler(bushTuning);
        }

        /// <summary>Applies the multiplier to <paramref name="target"/>: dry only, never under a tower cap (spec section 6).</summary>
        Vector3 ApplyBushSlowdown(Vector3 target, bool sprintHeld)
        {
            float m = 1f;
            if (waterMode == WaterMode.Dry && WalkSpeedCap <= 0f && target.sqrMagnitude > 0f)
            {
                EnsureBushSampler();
                m = bushSampler.Multiplier(transform.position, sprintHeld && stance == Stance.Stand);
            }
            LastBushMultiplier = m;
            target *= m;
            LastMoveTargetMagnitude = target.magnitude;
            return target;
        }
    }
}
