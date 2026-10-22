using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using WashedAshore.Gameplay;

namespace WashedAshore.Character.Stance
{
    /// <summary>
    /// The player's body in first person (research a1-a4-engineer.md section 2; td method ruling for the look-down fix).
    /// The camera stays on the CameraPivot and never follows a bone. Two bodies:
    /// the SHADOW body (the assembly with its one Animator, all four slots, shadow-only) sits at the base placement and never moves
    /// with pitch, so the full shadow is unchanged; the VIEW body (a pose-following copy with Body, Legs and Feet, no Head,
    /// casting no shadow) is what the camera sees and moves back (and optionally down) as the player looks down
    /// (<see cref="LookDownOffset"/>). With no view body the shadow body is drawn as before, head shadow-only.
    /// Base placement: <see cref="FootDrop"/> below the player origin and a fixed back offset per stance.
    /// </summary>
    public class FirstPersonBody : MonoBehaviour
    {
        [System.Serializable]
        public struct BackOffsets
        {
            public float stand, crouch, squat, sit, sleep;
        }

        [Tooltip("Metres the body sits behind the player axis, per stance. Starts at 0.17 [artist advice: eye point is 0.174 m ahead of the model origin]; a fixed value over 0.20 goes to the director.")]
        public BackOffsets backOffset = new BackOffsets { stand = 0.17f, crouch = 0.17f, squat = 0.17f, sit = 0.17f, sleep = 0.17f };
        [Tooltip("Renderer name contains this: the head slot. Used when no head renderers were set.")]
        [SerializeField] string headNameToken = "Head";
        [Tooltip("Metres the bodies sit below the player origin: the measured controller hover on flat ground, a constant (td ruling). Never a probe.")]
        [SerializeField] float footDrop = 0.05f;

        readonly List<Renderer> head = new List<Renderer>();
        readonly List<Renderer> all = new List<Renderer>();
        readonly List<Renderer> viewRenderers = new List<Renderer>();
        PlayerController player;
        Transform shadowRoot, viewRoot;
        bool debugView;

        public float FootDrop => footDrop;
        public bool HeadVisible => debugView;
        public bool HasViewBody => viewRoot != null;
        public IReadOnlyList<Renderer> HeadRenderers => head;
        /// <summary>The shadow body's renderers (all slots).</summary>
        public IReadOnlyList<Renderer> BodyRenderers => all;
        /// <summary>The renderers the camera sees: the view body's, or the shadow body's non-head ones when there is no view body.</summary>
        public IReadOnlyList<Renderer> ViewRenderers => viewRenderers;
        public Transform ShadowRoot => shadowRoot;
        public Transform ViewRoot => viewRoot;
        /// <summary>The look-down offset applied to the view body this frame (back, down), for tests and logs.</summary>
        public Vector2 LastLookDownOffset { get; private set; }

        /// <summary>Binds to the Player. <paramref name="shadowBodyRoot"/> is the assembled character (a child of the Player, facing +Z); <paramref name="viewBodyRoot"/> its pose-following copy, or null.</summary>
        public void Bind(PlayerController owner, Transform shadowBodyRoot, Transform viewBodyRoot = null)
        {
            player = owner;
            shadowRoot = shadowBodyRoot;
            viewRoot = viewBodyRoot;
            Rescan();
        }

        /// <summary>Reads the renderers again, after the assembler swapped a slot.</summary>
        public void Rescan()
        {
            head.Clear();
            all.Clear();
            viewRenderers.Clear();
            if (shadowRoot != null)
                foreach (var r in shadowRoot.GetComponentsInChildren<Renderer>(true))
                {
                    all.Add(r);
                    if (r.name.IndexOf(headNameToken, System.StringComparison.OrdinalIgnoreCase) >= 0) head.Add(r);
                }
            if (viewRoot != null)
                foreach (var r in viewRoot.GetComponentsInChildren<Renderer>(true)) viewRenderers.Add(r);
            else
                foreach (var r in all) if (!head.Contains(r)) viewRenderers.Add(r);
            Apply();
        }

        /// <summary>
        /// The debug camera shows the whole shadow body with its head and hides the view body; the player never does.
        /// Dev builds only (PlayerDebugCamera).
        /// </summary>
        public void SetHeadVisible(bool visible)
        {
            debugView = visible;
            Apply();
        }

        void Apply()
        {
            if (viewRoot != null)
            {
                // Shadow body: casts the full shadow, never drawn to the camera; in the debug view it is drawn normally.
                var shadowMode = debugView ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
                foreach (var r in all) r.shadowCastingMode = shadowMode;
                foreach (var r in viewRenderers)
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = true;
                    r.enabled = !debugView;
                }
            }
            else
            {
                var mode = debugView ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
                foreach (var r in head) r.shadowCastingMode = mode;
            }
        }

        float BackFor(Gameplay.Stance s)
        {
            switch (s)
            {
                case Gameplay.Stance.Crouch: return backOffset.crouch;
                case Gameplay.Stance.Squat: return backOffset.squat;
                case Gameplay.Stance.Sit: return backOffset.sit;
                case Gameplay.Stance.Sleep: return backOffset.sleep;
            }
            return backOffset.stand;
        }

        void LateUpdate()
        {
            if (player == null || shadowRoot == null) return;
            float back = BackFor(player.Stance);
            shadowRoot.localPosition = new Vector3(0f, -footDrop, -back);
            shadowRoot.localRotation = Quaternion.identity;
            if (viewRoot == null) return;
            LookDownOffset.Evaluate(player.LookDownTuning, player.StanceFrom, player.Stance, player.StanceBlend, player.Pitch, out float lb, out float ld);
            LastLookDownOffset = new Vector2(lb, ld);
            viewRoot.localPosition = new Vector3(0f, -footDrop - ld, -back - lb);
            viewRoot.localRotation = Quaternion.identity;
        }
    }
}
