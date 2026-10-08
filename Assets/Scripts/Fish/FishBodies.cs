using System;
using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>
    /// One model variant's body as measured from its imported Swim clip (f-artist's bake writes these): at scale 1 with
    /// the variant's non-uniform axis scale already applied, local +Z forward. Entry order = variantIndex.
    /// </summary>
    [Serializable]
    public struct FishBody
    {
        public string variant;
        [Tooltip("Local-space bounds, the union over every Swim frame at scale 1 (tail flicks and sway included).")]
        public Bounds clipBounds;
        [Tooltip("Nose to tail at scale 1, metres.")]
        public float noseToTail;
        [Tooltip("Root travel per Swim loop at scale 1, metres (0 = swims in place).")]
        public float rootTravelPerLoop;
        [Tooltip("Peak lateral tail-tip travel as a fraction of noseToTail.")]
        public float tailBeatAmplitude;
    }

    /// <summary>
    /// The measured fish bodies, indexed by FishSpecies.variantIndex. Written by the import bake in Assets/Editor/Fish,
    /// never typed in. The depth clamp (F4), spacing (F5), the visibility test and f-qa's body dump all use clipBounds
    /// times the instance's scale and rotation.
    /// </summary>
    [CreateAssetMenu(fileName = "FishBodies", menuName = "Washed Ashore/Fish Bodies")]
    public class FishBodies : ScriptableObject
    {
        public FishBody[] bodies = new FishBody[0];

        public bool Has(int variant) => variant >= 0 && variant < bodies.Length;

        /// <summary>World-space Y extent of a body at <paramref name="centre"/> with this rotation and uniform scale.</summary>
        public Vector2 VerticalExtent(int variant, Vector3 centre, Quaternion rotation, float scale)
        {
            var b = bodies[variant].clipBounds;
            Vector3 c = b.center, e = b.extents;
            // Y extent of a rotated box: |R row y| . extents.
            Vector3 ry = new Vector3(
                Mathf.Abs((rotation * Vector3.right).y),
                Mathf.Abs((rotation * Vector3.up).y),
                Mathf.Abs((rotation * Vector3.forward).y));
            float half = scale * (ry.x * e.x + ry.y * e.y + ry.z * e.z);
            float mid = centre.y + scale * (rotation * c).y;
            return new Vector2(mid - half, mid + half);
        }
    }
}
