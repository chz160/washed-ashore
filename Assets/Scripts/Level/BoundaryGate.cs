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

        bool refusedLogged;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            float a = IsOpen ? openAngle : 0f;
            if (Application.isPlaying)
            {
                // The leaves carry static colliders: moving them in play would move static colliders (#45). The pose is the saved
                // scene's; this only reports a request to change it.
                bool differs = (leftLeaf && Quaternion.Angle(leftLeaf.localRotation, Quaternion.Euler(0f, -a, 0f)) > 0.01f)
                    || (rightLeaf && Quaternion.Angle(rightLeaf.localRotation, Quaternion.Euler(0f, a, 0f)) > 0.01f);
                if (differs && !refusedLogged)
                {
                    refusedLogged = true;
                    Debug.LogError("BoundaryGate: the gate cannot open or close in play; its leaves are static colliders. Give them kinematic Rigidbodies and a collider-off or MoveRotation scheme first (#45).", this);
                }
                return;
            }
            if (leftLeaf) leftLeaf.localRotation = Quaternion.Euler(0f, -a, 0f);
            if (rightLeaf) rightLeaf.localRotation = Quaternion.Euler(0f, a, 0f);
        }
    }
}
