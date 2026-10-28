using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>Option A prototype (#64): own wall slide on top of the CharacterController. Not shipped.</summary>
    public enum SlideMode { Off, A1, A2 }

    public partial class PlayerController
    {
        /// <summary>Off = today's behaviour. A1 = project horizontalVelocity on the wall normals hit this Move. A2 = A1 plus up to two in-tick re-moves of the lost displacement.</summary>
        public SlideMode Slide { get; set; } = SlideMode.Off;
        /// <summary>Fires for every wall-classified hit (normal, point) during Move. Observation only; nothing reads it back.</summary>
        public event Action<Vector3, Vector3, bool> HitObserved;

        readonly List<Vector3> wallNormals = new List<Vector3>(8);   // flat (y = 0), unit; cleared at the start of every Move, consumed before it returns

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Vector3 n = hit.normal;
            float feet = transform.position.y + controller.center.y - controller.height * 0.5f;
            bool floor = n.y >= Mathf.Cos(controller.slopeLimit * Mathf.Deg2Rad);
            bool lowLip = hit.point.y < feet + controller.stepOffset;
            HitObserved?.Invoke(n, hit.point, !floor && !lowLip);
            if (floor || lowLip) return;
            Vector3 flat = new Vector3(n.x, 0f, n.z);
            if (flat.sqrMagnitude < 1e-4f) return;
            flat.Normalize();
            foreach (var w in wallNormals) if (Vector3.Dot(w, flat) > 0.999f) return;
            wallNormals.Add(flat);
        }

        /// <summary>Removes the into-wall part of v for every wall normal hit (two walls: sequential projection leaves the crease direction, zero for vertical walls).</summary>
        Vector3 SlideAlong(Vector3 v)
        {
            for (int pass = 0; pass < 2; pass++)
                foreach (var n in wallNormals)
                {
                    float d = Vector3.Dot(v, n);
                    if (d < 0f) v -= n * d;
                }
            return v;
        }

        void SlideAfterMove(Vector3 before, Vector3 intendedHorizontal)
        {
            if (Slide == SlideMode.Off || wallNormals.Count == 0) return;
            horizontalVelocity = SlideAlong(horizontalVelocity);
            if (Slide != SlideMode.A2) return;
            Vector3 after = transform.position;
            Vector3 lost = intendedHorizontal - new Vector3(after.x - before.x, 0f, after.z - before.z);
            for (int i = 0; i < 2; i++)
            {
                lost = SlideAlong(lost);
                if (lost.sqrMagnitude < 1e-8f) break;
                Vector3 p0 = transform.position;
                controller.Move(lost);
                Vector3 p1 = transform.position;
                lost -= new Vector3(p1.x - p0.x, 0f, p1.z - p0.z);
            }
        }
    }
}
