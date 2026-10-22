using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Character.Appearance
{
    /// <summary>
    /// Builds a character from a <see cref="CharacterAppearance"/>: instantiates the skeleton prefab, then attaches each slot's
    /// SkinnedMeshRenderer to it by remapping its bones by name (research B.1: equipping a part = point its bones at the shared
    /// armature, keep its own bind poses). No facing fix-up here: the import postprocessor already turned the FBX nodes, so the
    /// assembled root faces +Z (CLAUDE.md: never compensate in code).
    /// </summary>
    public static class CharacterAssembler
    {
        public static CharacterBody Assemble(CharacterAppearance appearance, Transform parent = null)
        {
            if (appearance == null) { Debug.LogError("CharacterAssembler: appearance is null"); return null; }
            if (appearance.skeleton == null) { Debug.LogError($"CharacterAssembler: '{appearance.name}' has no skeleton prefab"); return null; }
            var owned = Object.Instantiate(appearance); // the body owns a runtime copy; the asset is never written
            owned.name = appearance.name;
            var root = Object.Instantiate(owned.skeleton, parent);
            root.name = string.IsNullOrEmpty(owned.id) ? owned.name : owned.id;
            var body = root.GetComponent<CharacterBody>() ?? root.AddComponent<CharacterBody>();
            body.Init(owned);
            var shadowController = owned.shadowController != null ? owned.shadowController : owned.controller;
            if (shadowController != null && body.Animator != null) body.Animator.runtimeAnimatorController = shadowController;
            foreach (CharacterSlot slot in System.Enum.GetValues(typeof(CharacterSlot)))
            {
                var part = owned.GetPart(slot);
                if (part == null) { Debug.LogError($"CharacterAssembler: '{appearance.name}' has no {slot} part"); Destroy(root); return null; }
                if (!Attach(body, slot, part)) { Destroy(root); return null; }
            }
            body.ApplyTints();
            return body;
        }

        /// <summary>
        /// The first-person view body for <paramref name="shadow"/>: Body, Legs and Feet only, no Head, no Animator, no Collider, renderers
        /// shadow-off. It follows the shadow skeleton's pose each LateUpdate and rebuilds a slot when the shadow's slot changes.
        /// The root sits at local (0, 0, 0) under <paramref name="parent"/> with no yaw; the caller places it.
        /// </summary>
        public static CharacterViewCopy AssembleViewCopy(CharacterBody shadow, Transform parent = null) => CharacterViewCopy.Create(shadow, parent);

        internal static bool Attach(CharacterBody body, CharacterSlot slot, GameObject partModel)
        {
            if (partModel == null) { Debug.LogError($"CharacterAssembler: {slot} part is null"); return false; }
            var temp = Object.Instantiate(partModel);
            var fresh = new List<SkinnedMeshRenderer>();
            var failed = !FitsSkeleton(body, temp, partModel.name);
            foreach (var smr in temp.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var source = smr.bones;
                var mapped = new Transform[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    mapped[i] = source[i] ? body.Bone(source[i].name) : null;
                    if (mapped[i] == null)
                    {
                        Debug.LogError($"CharacterAssembler: {partModel.name}: bone '{(source[i] ? source[i].name : "null")}' is not in the skeleton");
                        failed = true;
                    }
                }
                if (failed) break;
                smr.bones = mapped;
                if (smr.rootBone) smr.rootBone = body.Bone(smr.rootBone.name);
                smr.transform.SetParent(body.transform, false);
                smr.updateWhenOffscreen = false;
                ApplyPoseBounds(body, smr);
                fresh.Add(smr);
            }
            Destroy(temp);
            if (failed || fresh.Count == 0)
            {
                if (!failed) Debug.LogError($"CharacterAssembler: {partModel.name} has no SkinnedMeshRenderer");
                foreach (var s in fresh) if (s) Destroy(s.gameObject);
                return false;
            }
            body.ReplaceSlotRenderers(slot, fresh);
            return true;
        }

        /// <summary>A part must carry exactly the skeleton's bones, or it belongs to the other body type (men 79 bones, women 62).</summary>
        static bool FitsSkeleton(CharacterBody body, GameObject partInstance, string partName)
        {
            var armature = partInstance.transform.Find("CharacterArmature");
            if (armature == null) { Debug.LogError($"CharacterAssembler: {partName} has no CharacterArmature node"); return false; }
            var names = new HashSet<string>();
            foreach (var t in armature.GetComponentsInChildren<Transform>(true)) names.Add(t.name);
            if (names.SetEquals(body.Bones.Keys)) return true;
            Debug.LogError($"CharacterAssembler: {partName} belongs to another body type ({names.Count} bones, the skeleton has {body.Bones.Count})");
            return false;
        }

        /// <summary>Local bounds covering every stance pose (the rest-pose bounds miss sleep and sit), from the measured union.</summary>
        static void ApplyPoseBounds(CharacterBody body, SkinnedMeshRenderer smr)
        {
            var a = body.Appearance;
            if (a == null || !a.hasPoseBounds || smr.rootBone == null) return;
            var b = a.poseBounds;
            var local = new Bounds(smr.rootBone.InverseTransformPoint(body.transform.TransformPoint(b.center)), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                local.Encapsulate(smr.rootBone.InverseTransformPoint(body.transform.TransformPoint(corner)));
            }
            smr.localBounds = local;
        }

        internal static void Destroy(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
