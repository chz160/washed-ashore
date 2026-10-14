using UnityEngine;

namespace WashedAshore.Interaction
{
    /// <summary>
    /// Numbers and strings of the interaction system (spec I1, tower-design-spec.md §3 and §8, tower-td-note §3).
    /// The Interactor references one asset; tests read their targets from it.
    /// </summary>
    [CreateAssetMenu(fileName = "InteractionSettings", menuName = "Washed Ashore/Interaction Tuning")]
    public class InteractionSettings : ScriptableObject
    {
        /// <summary>Spec I1: the aim ray is never longer than this, m.</summary>
        public const float RangeCap = 2.5f;

        [Header("Aim")]
        [Tooltip("Aim ray length from the camera, m. Spec I1 caps it at 2.5.")]
        [Range(1.5f, RangeCap)] public float interactRange = 2.5f;

        [Header("Prompt line")]
        [Tooltip("Prompt baseline above the bottom of the screen, fraction of screen height.")]
        [Range(0.2f, 0.4f)] public float promptHeightFraction = 0.30f;
        [Tooltip("Prompt cap height, fraction of screen height.")]
        [Range(0.02f, 0.03f)] public float promptCapFraction = 0.024f;
        [Tooltip("Cap height over em size of the prompt's font (Unity's built-in LegacyRuntime font).")]
        [Range(0.5f, 0.9f)] public float promptCapToEm = 0.7f;
        [Tooltip("Between the key and the text: space, en dash, space.")]
        public string separator = " – ";
        [Tooltip("Key text for a gamepad the Input System has no name for.")]
        public string genericPadKey = "South";
        [Tooltip("What the Input System calls that pad's south button.")]
        public string unnamedSouthDisplay = "Button South";
        public Color promptColor = new Color(0.93f, 0.92f, 0.88f, 1f);
        public Color promptShadowColor = new Color(0f, 0f, 0f, 0.8f);
        [Tooltip("Drop shadow offset, pixels.")]
        public Vector2 promptShadowOffset = new Vector2(1f, -1f);

        /// <summary>Rules for this asset; null when fine.</summary>
        public string Validate()
        {
            if (interactRange > RangeCap) return $"interactRange {interactRange} is above the {RangeCap} m cap";
            if (interactRange <= 0f) return "interactRange must be positive";
            if (promptCapToEm <= 0f) return "promptCapToEm must be positive";
            if (string.IsNullOrEmpty(separator)) return "separator is empty";
            return null;
        }

        void OnValidate()
        {
            string problem = Validate();
            if (problem != null) Debug.LogWarning($"InteractionSettings '{name}': {problem}", this);
        }
    }
}
