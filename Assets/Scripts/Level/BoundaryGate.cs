using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Level
{
    // North-line checkpoint gate (B2). Open state comes only from MapConfig.gateOpen; the leaves
    // swing north when open. Visible colliders stay on the leaves; the WorldBounds backstop is separate.
    [ExecuteAlways]
    public class BoundaryGate : MonoBehaviour
    {
        public MapConfig config;
        public Transform leftLeaf, rightLeaf;
        public float openAngle = 100f;

        public bool IsOpen => config && config.gateOpen;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            float a = IsOpen ? openAngle : 0f;
            if (leftLeaf) leftLeaf.localRotation = Quaternion.Euler(0f, -a, 0f);
            if (rightLeaf) rightLeaf.localRotation = Quaternion.Euler(0f, a, 0f);
        }
    }
}
