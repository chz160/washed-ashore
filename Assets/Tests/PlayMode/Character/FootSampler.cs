using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// Records both foot bones, the root and the Animator's state after LateUpdate (script execution order 10000, last in the frame), so the foot
    /// slide is measured on the pose that is actually drawn. Used by FootSlideTests.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class FootSampler : MonoBehaviour
    {
        public Transform left, right;
        public Animator animator;
        public readonly List<Vector3> l = new List<Vector3>(), r = new List<Vector3>(), root = new List<Vector3>();
        public readonly List<float> dts = new List<float>(), loco = new List<float>(), stateSpeed = new List<float>(), nt = new List<float>(), len = new List<float>();
        public readonly List<int> hash = new List<int>();
        public bool recording;
        static readonly int LocoRate = Animator.StringToHash("LocoRate");

        void LateUpdate()
        {
            if (!recording) return;
            l.Add(left.position);
            r.Add(right.position);
            root.Add(transform.position);
            dts.Add(Time.deltaTime);
            var st = animator.GetCurrentAnimatorStateInfo(0);
            loco.Add(animator.GetFloat(LocoRate));
            stateSpeed.Add(st.speed * st.speedMultiplier);
            nt.Add(st.normalizedTime);
            len.Add(st.length);
            hash.Add(st.fullPathHash);
        }
    }
}
