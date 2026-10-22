using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Character.Appearance
{
    /// <summary>
    /// An assembled character: the shared skeleton plus one SkinnedMeshRenderer set per slot. Created by
    /// <see cref="CharacterAssembler.Assemble"/>; swap a slot with <see cref="SetSlot"/>. The root faces +Z.
    /// </summary>
    public sealed class CharacterBody : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Color_ = Shader.PropertyToID("_Color");

        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<CharacterSlot, List<SkinnedMeshRenderer>> slots = new Dictionary<CharacterSlot, List<SkinnedMeshRenderer>>();
        CharacterAppearance appearance;
        bool ownsAppearance;

        public CharacterAppearance Appearance => appearance;
        /// <summary>The Animator sits on the CharacterArmature node, not on the root: the pack's clips are keyed relative to the armature
        /// (paths start at "Root"), and their own curves on that node equal its rest pose.</summary>
        public Animator Animator { get; private set; }

        /// <summary>The uniform scale of the skeleton root. Pass it to <see cref="CharacterClipSet.LocomotionRate"/>.</summary>
        public float SkeletonScale => transform.localScale.x;

        /// <summary>Raised after a slot's renderers were replaced (the first-person body rescans its renderers on it).</summary>
        public event Action<CharacterSlot> SlotChanged;

        /// <summary>Raised by <see cref="SetTint"/> (the first-person view copy mirrors it).</summary>
        public event Action<CharacterSlot, string, Color> TintChanged;

        public IReadOnlyDictionary<string, Transform> Bones => bones;

        void OnDestroy()
        {
            if (ownsAppearance && appearance != null) CharacterAssembler.Destroy(appearance);
        }

        public Transform Bone(string boneName) => bones.TryGetValue(boneName, out var t) ? t : null;

        public IReadOnlyList<SkinnedMeshRenderer> Renderers(CharacterSlot slot) =>
            slots.TryGetValue(slot, out var list) ? list : (IReadOnlyList<SkinnedMeshRenderer>)Array.Empty<SkinnedMeshRenderer>();

        /// <summary>The body works on its own copy of the appearance: swaps and tints never write into the shared asset.</summary>
        internal void Init(CharacterAppearance ownCopy)
        {
            appearance = ownCopy;
            ownsAppearance = true;
            Animator = GetComponentInChildren<Animator>(true);
            bones.Clear();
            // the skeleton prefab holds the armature only, so every Transform with a name is a bone. Never parent anything under the
            // skeleton prefab or every part swap (FitsSkeleton compares bone sets) fails.
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t != transform && !bones.ContainsKey(t.name)) bones.Add(t.name, t);
            foreach (CharacterSlot s in Enum.GetValues(typeof(CharacterSlot))) slots[s] = new List<SkinnedMeshRenderer>();
        }

        /// <summary>Replaces one slot. Returns false (and logs an error) when the part does not fit the skeleton.</summary>
        public bool SetSlot(CharacterSlot slot, GameObject partModel)
        {
            if (!CharacterAssembler.Attach(this, slot, partModel)) return false;
            if (appearance != null) appearance.SetPart(slot, partModel);
            ApplyTints();
            SlotChanged?.Invoke(slot);
            return true;
        }

        internal void ReplaceSlotRenderers(CharacterSlot slot, List<SkinnedMeshRenderer> fresh)
        {
            foreach (var old in slots[slot])
                if (old) CharacterAssembler.Destroy(old.gameObject);
            slots[slot] = fresh;
        }

        public void SetTint(CharacterSlot slot, string materialName, Color color)
        {
            if (appearance == null) return;
            var list = appearance.tints;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].slot != slot || list[i].materialName != materialName) continue;
                list[i] = new SlotTint { slot = slot, materialName = materialName, color = color };
                ApplyTints();
                TintChanged?.Invoke(slot, materialName, color);
                return;
            }
            list.Add(new SlotTint { slot = slot, materialName = materialName, color = color });
            ApplyTints();
            TintChanged?.Invoke(slot, materialName, color);
        }

        /// <summary>Applies the appearance's tints with property blocks, one per material index, so the shared materials stay untouched.</summary>
        public void ApplyTints()
        {
            if (appearance == null) return;
            foreach (var tint in appearance.tints)
            {
                foreach (var smr in Renderers(tint.slot))
                {
                    var mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (!mats[i] || mats[i].name != tint.materialName) continue;
                        var block = new MaterialPropertyBlock();
                        smr.GetPropertyBlock(block, i);
                        block.SetColor(BaseColor, tint.color);
                        block.SetColor(Color_, tint.color);
                        smr.SetPropertyBlock(block, i);
                    }
                }
            }
        }
    }
}
