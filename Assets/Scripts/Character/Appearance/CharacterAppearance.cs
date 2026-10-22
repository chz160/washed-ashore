using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Character.Appearance
{
    public enum BodyType { Men, Women }

    /// <summary>The four modular mesh slots of the Quaternius packs. Each part is its own SkinnedMeshRenderer on the shared rig.</summary>
    public enum CharacterSlot { Head, Body, Legs, Feet }

    /// <summary>Colour for one named material of one slot (the packs use flat-colour materials such as "Skin" or "Worker_Yellow").</summary>
    [Serializable]
    public struct SlotTint
    {
        public CharacterSlot slot;
        public string materialName;
        public Color color;
    }

    /// <summary>
    /// What a character looks like, as data: a body type (which rig), one part model per slot and tints. The assembler turns it into
    /// a character at runtime. <see cref="id"/> is the short descriptor that a save or a network message would carry later (future
    /// issue); nothing here depends on how it is stored.
    /// </summary>
    [CreateAssetMenu(menuName = "Washed Ashore/Character Appearance", fileName = "CharacterAppearance")]
    public sealed class CharacterAppearance : ScriptableObject
    {
        [Tooltip("Stable short id, for example Men_Worker.")]
        public string id;
        public BodyType bodyType;
        [Tooltip("Skeleton prefab for this body type: an Animator on the model root plus the armature, no meshes.")]
        public GameObject skeleton;
        [Tooltip("Animator controller for this body type (CharacterAnimator_<type>.controller). Assigned to the Animator when assembled.")]
        public RuntimeAnimatorController controller;
        [Tooltip("Controller of the SHADOW body (an override of 'controller' that plays the full-swing Jog and Sprint clips, director amend7). Null: the shadow uses 'controller'. The first-person VIEW body always uses 'controller' (capped arms).")]
        public RuntimeAnimatorController shadowController;

        [Header("Range-end controllers (designer-2 section 3a retunes; filled by the asset builder, usable in a dev build)")]
        [Tooltip("VIEW body: overrides of 'controller' with the Low / High range clips, capped arms.")]
        public RuntimeAnimatorController rangeLowView, rangeHighView;
        [Tooltip("SHADOW body: the same range clips plus the eight capped -> _Full swaps (full-swing arms).")]
        public RuntimeAnimatorController rangeLowShadow, rangeHighShadow;

        /// <summary>The range-end controller for a body: the shadow gets its variant (full-swing Jog and Sprint kept), the view the capped one.</summary>
        public RuntimeAnimatorController RangeController(bool high, bool forShadow) =>
            forShadow ? (high ? rangeHighShadow : rangeLowShadow) : (high ? rangeHighView : rangeLowView);
        [Tooltip("Part models (the FBX assets). Each carries one SkinnedMeshRenderer on the pack's armature.")]
        public GameObject head, body, legs, feet;
        [Tooltip("Uniform scale on the skeleton root (both the shadow and the view body use the skeleton prefab, so both carry it). Set by the builder from the authoring report; feet stay at the origin.")]
        public float skeletonScale = 1f;
        public List<SlotTint> tints = new List<SlotTint>();
        [Tooltip("Union of every stance clip's posed mesh, in body-root space, measured by the asset builder. The assembler sets each renderer's local bounds from it so the body and its shadow never pop out (td review).")]
        public Bounds poseBounds;
        public bool hasPoseBounds;

        public GameObject GetPart(CharacterSlot slot)
        {
            switch (slot)
            {
                case CharacterSlot.Head: return head;
                case CharacterSlot.Body: return body;
                case CharacterSlot.Legs: return legs;
                default: return feet;
            }
        }

        public void SetPart(CharacterSlot slot, GameObject part)
        {
            switch (slot)
            {
                case CharacterSlot.Head: head = part; break;
                case CharacterSlot.Body: body = part; break;
                case CharacterSlot.Legs: legs = part; break;
                default: feet = part; break;
            }
        }

        /// <summary>A runtime copy with one slot replaced (the asset itself is never changed).</summary>
        public CharacterAppearance WithSlot(CharacterSlot slot, GameObject part)
        {
            var copy = Instantiate(this);
            copy.name = name;
            copy.SetPart(slot, part);
            return copy;
        }
    }
}
