using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Character.Appearance
{
    /// <summary>
    /// Drives the Survivor's knee corrective blend shapes (td-49 td-knee-corrective.md, knees only). One component per body (shadow and view), added at
    /// assembly. It finds the shapes <c>Knee_L_90</c> and <c>Knee_R_90</c> by name on the body's renderers, reads each knee's flexion from that body's own
    /// UpperLeg / LowerLeg / LowerLeg_end bones, and sets the shape weight to a smoothstep from <see cref="startDegrees"/> to <see cref="targetDegrees"/>
    /// (0 to 100 percent). With no such shape it does nothing and is not added. Runs in LateUpdate after <see cref="CharacterViewCopy"/> (order 100) and
    /// the Animators, so both bodies read the same frame's pose.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class KneeCorrectiveDriver : MonoBehaviour
    {
        public const string ShapeFormat = "Knee_{0}_90";   // {0}: L or R

        [Tooltip("Knee flexion in degrees below which the shape is off (td-49: 20, so the shape stays off at walking knee angles).")]
        [SerializeField] float startDegrees = 20f;
        [Tooltip("Knee flexion in degrees at which the shape is at full weight: the shape's authored target (90 provisional; item 3's print of the largest shipped flex sets the real value).")]
        [SerializeField] float targetDegrees = 90f;

        struct Knee
        {
            public SkinnedMeshRenderer renderer;
            public int shape;
            public Transform hip, knee, ankle;
            public bool left;
        }

        readonly List<Knee> knees = new List<Knee>();
        CharacterBody body;

        public float StartDegrees { get => startDegrees; set => startDegrees = value; }
        public float TargetDegrees { get => targetDegrees; set => targetDegrees = value; }
        public int KneeCount => knees.Count;

        /// <summary>Last weight (0..1) written for the knee, by side "L" or "R"; -1 when that side has no shape.</summary>
        public float LastWeight(string side) => side == "L" ? weightL : weightR;
        float weightL = -1f, weightR = -1f;

        /// <summary>The smoothstep from <paramref name="start"/> to <paramref name="target"/>, clamped to 0..1.</summary>
        public static float Weight(float flexDegrees, float start, float target)
        {
            if (target <= start) return flexDegrees >= target ? 1f : 0f;
            float t = Mathf.Clamp01((flexDegrees - start) / (target - start));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Knee flexion in degrees from three joint positions: 0 for a straight leg, 90 for a right-angle bend.</summary>
        public static float FlexDegrees(Vector3 hip, Vector3 knee, Vector3 ankle) => Vector3.Angle(knee - hip, ankle - knee);

        /// <summary>
        /// Adds a driver to <paramref name="body"/> when one of its renderers carries a knee shape; otherwise returns null and adds nothing.
        /// </summary>
        public static KneeCorrectiveDriver AddTo(CharacterBody body)
        {
            if (body == null) return null;
            var driver = body.GetComponent<KneeCorrectiveDriver>();
            if (driver == null)
            {
                if (!HasShapes(body)) return null;
                driver = body.gameObject.AddComponent<KneeCorrectiveDriver>();
            }
            if (body.Appearance != null)   // the flex range comes from the appearance data (td-49 R1), not from a component default
            {
                driver.startDegrees = body.Appearance.kneeShapeStartDegrees;
                driver.targetDegrees = body.Appearance.kneeShapeTargetDegrees;
            }
            driver.Bind(body);
            return driver;
        }

        static bool HasShapes(CharacterBody body)
        {
            foreach (CharacterSlot slot in System.Enum.GetValues(typeof(CharacterSlot)))
                foreach (var r in body.Renderers(slot))
                    if (r != null && r.sharedMesh != null && (r.sharedMesh.GetBlendShapeIndex(string.Format(ShapeFormat, "L")) >= 0 || r.sharedMesh.GetBlendShapeIndex(string.Format(ShapeFormat, "R")) >= 0))
                        return true;
            return false;
        }

        /// <summary>(Re)finds the shapes and bones; called at assembly and after a slot swap.</summary>
        public void Bind(CharacterBody owner)
        {
            if (body != null) body.SlotChanged -= OnSlotChanged;
            body = owner;
            body.SlotChanged += OnSlotChanged;
            knees.Clear();
            weightL = weightR = -1f;
            foreach (var side in new[] { "L", "R" })
            {
                string shape = string.Format(ShapeFormat, side);
                var hip = body.Bone("UpperLeg." + side);
                var knee = body.Bone("LowerLeg." + side);
                var ankle = body.Bone("LowerLeg." + side + "_end");
                foreach (CharacterSlot slot in System.Enum.GetValues(typeof(CharacterSlot)))
                    foreach (var r in body.Renderers(slot))
                    {
                        if (r == null || r.sharedMesh == null) continue;
                        int index = r.sharedMesh.GetBlendShapeIndex(shape);
                        if (index < 0) continue;
                        if (hip == null || knee == null || ankle == null)
                        {
                            Debug.LogError($"KneeCorrectiveDriver: {shape} found but the leg bones of '{body.name}' are missing (UpperLeg.{side}, LowerLeg.{side}, LowerLeg.{side}_end)");
                            continue;
                        }
                        knees.Add(new Knee { renderer = r, shape = index, hip = hip, knee = knee, ankle = ankle, left = side == "L" });
                        if (side == "L") weightL = 0f; else weightR = 0f;
                    }
            }
            Apply();   // the FBX stores the shapes at weight 100 (the import default): write the pose's weights now, not at the first LateUpdate
        }

        void OnSlotChanged(CharacterSlot slot) => Bind(body);

        void OnDestroy()
        {
            if (body != null) body.SlotChanged -= OnSlotChanged;
        }

        void LateUpdate() => Apply();

        /// <summary>Reads the current pose and writes the shape weights. Also callable from tests.</summary>
        public void Apply()
        {
            for (int i = 0; i < knees.Count; i++)
            {
                var k = knees[i];
                float w = Weight(FlexDegrees(k.hip.position, k.knee.position, k.ankle.position), startDegrees, targetDegrees);
                k.renderer.SetBlendShapeWeight(k.shape, w * 100f);
                if (k.left) weightL = w; else weightR = w;   // no string work per frame (no allocation)
            }
        }
    }
}
