using System;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Bush slowdown numbers (#21, docs/design/bush-slowdown.md sections 4, 5 and 7). One entry per terrain detail layer, in the order of
    /// <c>BellsBendVegetation.DetailSpecs</c> (index 0-7); an EditMode test keeps that order and the shrub flags in step with the editor
    /// constants, which the runtime assembly cannot reference. All multipliers are assumed (not playtested).
    /// </summary>
    [CreateAssetMenu(fileName = "BushSlowdownTuning", menuName = "Washed Ashore/Bush Slowdown Tuning")]
    public class BushSlowdownTuning : ScriptableObject
    {
        [Serializable]
        public struct Layer
        {
            [Tooltip("Detail layer name, for the inspector and the tests.")] public string name;
            [Tooltip("Only layers with the shrub flag slow the player. Grass and fern never do.")] public bool slows;
            [Tooltip("Walk speed multiplier at full cover.")] [Range(0.3f, 1f)] public float walkMultiplier;
            [Tooltip("Sprint speed multiplier at full cover.")] [Range(0.3f, 1f)] public float sprintMultiplier;
            [Tooltip("Painted count that counts as a full cell (Briar, ShrubThicket, KudzuMound are 0 or 1; KudzuLeaf 0-3).")] [Range(1, 3)] public int fullCount;
            [Tooltip("Layer max height, m. The layer slows only while the feet are lower than this above the ground.")] public float maxHeight;

            public Layer(string name, bool slows, float walk, float sprint, int fullCount, float maxHeight)
            {
                this.name = name; this.slows = slows; walkMultiplier = walk; sprintMultiplier = sprint; this.fullCount = fullCount; this.maxHeight = maxHeight;
            }
        }

        public const int LayerCount = 8;
        /// <summary>Spec section 4: no full-cover walk speed under this, m/s, so crouch (1.75) stays below every bush speed.</summary>
        public const float MinFullCoverWalkSpeed = 2.5f;

        public Layer[] layers =
        {
            new Layer("Broomsedge", false, 1f, 1f, 1, 1.1f),
            new Layer("Briar", true, 0.70f, 0.50f, 1, 1.6f),
            new Layer("ShrubThicket", true, 0.55f, 0.40f, 1, 2.6f),
            new Layer("KudzuMound", true, 0.50f, 0.35f, 1, 3.2f),
            new Layer("KudzuLeaf", true, 0.90f, 0.80f, 2, 0.9f),
            new Layer("Fern", false, 1f, 1f, 1, 0.9f),
            new Layer("CrackGrass", false, 1f, 1f, 1, 0.5f),
            new Layer("BankGrass", false, 1f, 1f, 1, 1.1f),
        };

        /// <summary>Checks the section 4 rules. Returns null when they hold, else the first problem.</summary>
        public string Validate(float walkSpeed, float sprintSpeed)
        {
            if (layers == null || layers.Length != LayerCount) return $"needs {LayerCount} layers";
            for (int i = 0; i < layers.Length; i++)
            {
                var l = layers[i];
                if (!l.slows)
                {
                    if (l.walkMultiplier != 1f || l.sprintMultiplier != 1f) return $"{l.name} does not slow, so both multipliers must be 1";
                    continue;
                }
                if (l.sprintMultiplier >= l.walkMultiplier) return $"{l.name}: sprint multiplier must be below walk";
                if (walkSpeed * l.walkMultiplier < MinFullCoverWalkSpeed) return $"{l.name}: full-cover walk speed {walkSpeed * l.walkMultiplier:F2} m/s is under {MinFullCoverWalkSpeed}";
                if (sprintSpeed * l.sprintMultiplier < walkSpeed * l.walkMultiplier) return $"{l.name}: sprint would be slower than walk";
                for (int j = 0; j < layers.Length; j++)
                {
                    var o = layers[j];
                    if (j != i && o.slows && o.maxHeight > l.maxHeight && (o.walkMultiplier >= l.walkMultiplier || o.sprintMultiplier >= l.sprintMultiplier))
                        return $"{o.name} is taller than {l.name} so it must slow more";
                }
            }
            return null;
        }

        void OnValidate()
        {
            string problem = Validate(5f, 8f);
            if (problem != null) Debug.LogError($"BushSlowdownTuning '{name}': {problem}", this);
        }
    }
}
