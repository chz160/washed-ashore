using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WashedAshore.Lookout.Editor
{
    /// <summary>
    /// #86 collider cleanup (spec-86 r4: K3 and K5), run after the widen and trim steps so the numbers (tower-local metres, post-widen) hold.
    /// Collider only: nothing is removed, no renderer or hatch leaf changes. Both edits are BoxColliders (a plate face and two landing wall moves).
    /// </summary>
    public static partial class WalkableTowerBuilder
    {
        const float ExpectTol = 0.002f;

        /// <summary>
        /// K3: the cab-floor plate west of the hatch opening (COL_BOX_CabFloor_01). Its east face moves -0.05, so the 5 cm rim no longer stands
        /// past the face of the F8 wall at the head. Collider only; the boards, the hatch leaf and its opening bounds are unchanged.
        /// </summary>
        static string TrimHatchRim(Dictionary<string, Transform> byName)
        {
            var sb = new StringBuilder("hatch rim (K3):");
            EditBox(byName, "COL_BOX_CabFloor_01", 0, true, -0.050f, 0.2674f, sb);
            return sb.ToString();
        }

        /// <summary>K5: the two landing 6 guard boxes move out so their inner faces stand 0.406 m from the lane, like every flight wall.</summary>
        static string FlushLanding6Walls(Dictionary<string, Transform> byName)
        {
            var sb = new StringBuilder("landing 6 walls out 0.063 (K5):");
            // Both boxes are thin on the moved axis; the inner (max) face is at -1.3136 now. Move the box, size unchanged.
            MoveBox(byName, "CLIP_BOX_Landing06_02", 0, -0.063f, -1.3136f, sb);
            MoveBox(byName, "CLIP_BOX_Landing06_03", 2, -0.063f, -1.3136f, sb);
            return sb.ToString();
        }

        static BoxCollider AxisBox(Dictionary<string, Transform> byName, string name, out Transform t)
        {
            if (!byName.TryGetValue(name, out t) || !t) throw new InvalidOperationException(name + " not found");
            var bc = t.GetComponent<BoxCollider>();
            if (!bc) throw new InvalidOperationException(name + " has no BoxCollider");
            if (Quaternion.Angle(t.rotation, Quaternion.identity) > 0.1f || (t.lossyScale - Vector3.one).magnitude > 1e-3f)
                throw new InvalidOperationException(name + " is rotated or scaled: the spec edit is axis-aligned");
            return bc;
        }

        /// <summary>Moves the max face of an axis-aligned box on one axis by delta (negative = shrink), keeping the min face. Asserts the face first.</summary>
        static void EditBox(Dictionary<string, Transform> byName, string name, int axis, bool maxFace, float delta, float expectFace, StringBuilder sb)
        {
            var bc = AxisBox(byName, name, out var t);
            float centre = t.position[axis] + bc.center[axis], half = bc.size[axis] * 0.5f;
            float face = maxFace ? centre + half : centre - half;
            if (Mathf.Abs(face - expectFace) > ExpectTol) throw new InvalidOperationException($"{name}: face {face:F4} on axis {axis} is not the spec's {expectFace:F4}");
            var size = bc.size; size[axis] += delta; bc.size = size;
            var p = t.position; p[axis] += delta * 0.5f; t.position = p;
            sb.Append($"\n  {name} axis {axis} face {face:F4} -> {face + delta:F4}");
        }

        /// <summary>Moves an axis-aligned box on one axis by delta, size unchanged. Asserts the inner face first.</summary>
        static void MoveBox(Dictionary<string, Transform> byName, string name, int axis, float delta, float expectMaxFace, StringBuilder sb)
        {
            var bc = AxisBox(byName, name, out var t);
            float face = t.position[axis] + bc.center[axis] + bc.size[axis] * 0.5f;
            if (Mathf.Abs(face - expectMaxFace) > ExpectTol) throw new InvalidOperationException($"{name}: inner face {face:F4} on axis {axis} is not the spec's {expectMaxFace:F4}");
            var p = t.position; p[axis] += delta; t.position = p;
            sb.Append($"\n  {name} axis {axis} inner face {face:F4} -> {face + delta:F4}");
        }
    }
}
