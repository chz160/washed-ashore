using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WashedAshore.Character.Appearance
{
    /// <summary>
    /// The first-person VIEW body (td look-down method): Body, Legs and Feet of the shadow body: no Head and no Collider. It has its own Animator (the view Animator, below).
    /// The view body has its OWN Animator on the base (capped-arms) controller; the owner writes the same parameters to both Animators, so the
    /// 2D blend trees give the same weights. Every LateUpdate (after the Animators) all bones EXCEPT the arm chains (Shoulder.L/R and below) are
    /// copied from the shadow, so the two bodies never diverge there and only the arms differ (capped in the view). A sync guard resyncs the view
    /// Animator to the shadow's state and normalized time if they drift apart, and counts it in <see cref="Resyncs"/>.
    /// Its renderers cast no shadow (the shadow body casts it) and receive shadows. The owner places the root; it starts at local (0,0,0)
    /// with no yaw. It rebuilds a slot when the shadow raises <see cref="CharacterBody.SlotChanged"/>.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class CharacterViewCopy : MonoBehaviour
    {
        static readonly CharacterSlot[] ViewSlots = { CharacterSlot.Body, CharacterSlot.Legs, CharacterSlot.Feet };

        CharacterBody shadow;
        CharacterBody view;
        Animator viewAnimator;
        bool synced;
        readonly List<(Transform from, Transform to)> pairs = new List<(Transform, Transform)>();

        /// <summary>The view body's own Animator (base controller, capped arms). The driver writes the same parameters to this and to the shadow's.</summary>
        public Animator ViewAnimator => viewAnimator;

        /// <summary>How many times the sync guard had to resync the view Animator to the shadow (not counting the first alignment). Expected 0.</summary>
        public int Resyncs { get; private set; }

        /// <summary>Largest normalized-time gap, over one loop, tolerated before a resync.</summary>
        public const float SyncTolerance = 0.01f;

        /// <summary>The view body (its bones and renderers).</summary>
        public CharacterBody Body => view;
        public CharacterBody Shadow => shadow;

        public void SetVisible(bool visible)
        {
            foreach (var slot in ViewSlots)
                foreach (var r in view.Renderers(slot)) r.enabled = visible;
        }

        internal static CharacterViewCopy Create(CharacterBody source, Transform parent)
        {
            if (source == null || source.Appearance == null) { Debug.LogError("CharacterViewCopy: no shadow body"); return null; }
            var root = Object.Instantiate(source.Appearance.skeleton, parent);
            root.name = source.name + "_View";
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            var copy = root.AddComponent<CharacterViewCopy>();
            copy.shadow = source;
            copy.view = root.AddComponent<CharacterBody>();
            copy.viewAnimator = root.GetComponentInChildren<Animator>(true);
            copy.view.Init(Object.Instantiate(source.Appearance));
            if (copy.viewAnimator != null)
            {
                copy.viewAnimator.runtimeAnimatorController = source.Appearance.controller; // the base controller: capped arms
                copy.viewAnimator.applyRootMotion = false;
                copy.viewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            foreach (var slot in ViewSlots)
                if (!CharacterAssembler.Attach(copy.view, slot, source.Appearance.GetPart(slot)))
                {
                    CharacterAssembler.Destroy(root);
                    return null;
                }
            copy.StyleRenderers();
            copy.view.ApplyTints(); // the view body is the one the player looks down at: same colours as the shadow body
            copy.MapBones();
            copy.Apply();
            source.SlotChanged += copy.OnShadowSlotChanged;
            source.TintChanged += copy.OnShadowTintChanged;
            return copy;
        }

        void MapBones()
        {
            pairs.Clear();
            foreach (var kv in view.Bones)
            {
                if (IsArmChain(kv.Value)) continue; // the view Animator animates the arms (capped clips)
                var from = shadow.Bone(kv.Key);
                if (from != null) pairs.Add((from, kv.Value));
            }
        }

        /// <summary>True for Shoulder.L/R and every bone below them.</summary>
        bool IsArmChain(Transform t)
        {
            for (; t != null && t != transform; t = t.parent)
                if (t.name == "Shoulder.L" || t.name == "Shoulder.R") return true;
            return false;
        }

        static float Frac(float normalizedTime) => normalizedTime - Mathf.Floor(normalizedTime);

        /// <summary>
        /// The sync guard (called every LateUpdate): if the two Animators' base-layer state or normalized time differ by more than
        /// <see cref="SyncTolerance"/>, the view Animator is put at the shadow's state and time and <see cref="Resyncs"/> counts it.
        /// Skipped while either Animator is mid-transition (the states settle on their own). Returns true when it resynced.
        /// </summary>
        public bool CheckSync()
        {
            var a = shadow != null ? shadow.Animator : null;
            var v = viewAnimator;
            if (a == null || v == null || a.runtimeAnimatorController == null || v.runtimeAnimatorController == null) return false;
            if (!a.isInitialized || !v.isInitialized) return false;
            if (a.IsInTransition(0) || v.IsInTransition(0)) return false;
            var sa = a.GetCurrentAnimatorStateInfo(0);
            var sv = v.GetCurrentAnimatorStateInfo(0);
            float gap = Mathf.Abs(Frac(sa.normalizedTime) - Frac(sv.normalizedTime));
            gap = Mathf.Min(gap, 1f - gap);
            bool drift = sa.shortNameHash != sv.shortNameHash || gap > SyncTolerance;
            if (!drift) { synced = true; return false; }
            v.Play(sa.shortNameHash, 0, Frac(sa.normalizedTime));
            v.Update(0f); // apply now, so this frame's pose is already aligned
            if (synced) Resyncs++; // the first alignment after creation is not a fault
            synced = true;
            return true;
        }

        void OnShadowSlotChanged(CharacterSlot slot)
        {
            if (slot == CharacterSlot.Head || view == null) return; // the view body has no head
            view.SetSlot(slot, shadow.Appearance.GetPart(slot));
            StyleRenderers();
        }

        void OnShadowTintChanged(CharacterSlot slot, string materialName, Color color)
        {
            if (slot != CharacterSlot.Head && view != null) view.SetTint(slot, materialName, color);
        }

        void StyleRenderers()
        {
            foreach (var slot in ViewSlots)
                foreach (var r in view.Renderers(slot)) Style(r);
        }

        static void Style(SkinnedMeshRenderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
        }

        /// <summary>Copies the shadow skeleton's current local pose now (also called every LateUpdate).</summary>
        public void Apply()
        {
            foreach (var (from, to) in pairs)
                to.SetLocalPositionAndRotation(from.localPosition, from.localRotation);
        }

        void LateUpdate()
        {
            if (shadow == null) return;
            CheckSync();
            Apply();
        }

        void OnDestroy()
        {
            if (shadow == null) return;
            shadow.SlotChanged -= OnShadowSlotChanged;
            shadow.TintChanged -= OnShadowTintChanged;
        }
    }
}
