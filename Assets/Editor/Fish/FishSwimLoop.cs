using System.Linq;
using UnityEngine;

namespace WashedAshore.Fish.Editor
{
    /// <summary>
    /// The fish Swim loop as the VAT bake plays it (one source for FishImportCheck and the bake). The vendor clip doesn't loop
    /// cleanly: Spine3/Tail end one key off their first key, and the eased first/last keys can stall a bone at the wrap.
    /// Sample: local TRS of every bone at keys 0..n (n = clip length in frames). Linear: each bone's frame-0 minus last-key
    /// difference spread linearly over the clip (Unity's Loop Pose idea; SampleAnimation ignores loopPose), giving n frames
    /// 0..n-1 that wrap (n-1) -> 0. SeamSmooth: then the 2W-1 frames round the wrap are rebuilt by a cubic Hermite
    /// that matches position and velocity W frames either side, so an eased key can't stall or jerk a bone across the seam.
    /// </summary>
    public static class FishSwimLoop
    {
        public static (Quaternion[][] rot, Vector3[][] pos) Sample(GameObject go, Transform[] bones, AnimationClip clip, out int n)
        {
            n = Mathf.RoundToInt(clip.length * clip.frameRate);
            var rot = new Quaternion[n + 1][];
            var pos = new Vector3[n + 1][];
            for (int f = 0; f <= n; f++)
            {
                clip.SampleAnimation(go, f / clip.frameRate);
                rot[f] = bones.Select(b => b.localRotation).ToArray();
                pos[f] = bones.Select(b => b.localPosition).ToArray();
            }
            return (rot, pos);
        }

        public static (Quaternion[][] rot, Vector3[][] pos) Linear(Quaternion[][] rot, Vector3[][] pos, int n)
        {
            int bones = rot[0].Length;
            var r = new Quaternion[n][];
            var p = new Vector3[n][];
            for (int f = 0; f < n; f++)
            {
                float w = (float)f / n;
                r[f] = new Quaternion[bones];
                p[f] = new Vector3[bones];
                for (int i = 0; i < bones; i++)
                {
                    var d = rot[0][i] * Quaternion.Inverse(rot[n][i]);
                    r[f][i] = Quaternion.Slerp(Quaternion.identity, d, w) * rot[f][i];
                    p[f][i] = pos[f][i] + w * (pos[0][i] - pos[n][i]);
                }
            }
            return (r, p);
        }

        /// <summary>Frames on each side of the wrap that SeamSmooth rebuilds.</summary>
        public const int SeamWindow = 3;

        // Rebuilds the 2W-1 frames around the wrap of an n-frame cyclic loop (in place): frames n-W+1 .. n-1 and 0 .. W-1 are
        // replaced by a cubic Hermite from frame n-W to frame W that matches both frames' positions and velocities, so a bone
        // whose vendor curve eases into key 0 (slow) but out of the last key (fast) changes speed over 2W steps, not one.
        public static void SeamSmooth(Quaternion[][] rot, Vector3[][] pos, int n, int w = SeamWindow)
        {
            if (n < 2 * w + 3) return;
            int a = n - w, b = w, span = 2 * w;
            int F(int f) => ((f % n) + n) % n;
            for (int i = 0; i < rot[0].Length; i++)
            {
                Vector3 pa = pos[a][i], pb = pos[b][i], ma = (pos[a][i] - pos[a - 1][i]) * span, mb = (pos[b + 1][i] - pos[b][i]) * span;
                var qa = rot[a][i];
                Vector4 V(Quaternion q) { if (Quaternion.Dot(q, qa) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); return new Vector4(q.x, q.y, q.z, q.w); }
                Vector4 va = V(qa), vb = V(rot[b][i]), vma = (va - V(rot[a - 1][i])) * span, vmb = (V(rot[b + 1][i]) - vb) * span;
                for (int k = 1; k < span; k++)
                {
                    float t = (float)k / span;
                    int f = F(a + k);
                    pos[f][i] = Hermite(pa, ma, pb, mb, t);
                    var v = Hermite4(va, vma, vb, vmb, t).normalized;
                    rot[f][i] = new Quaternion(v.x, v.y, v.z, v.w);
                }
            }
        }

        // Root-space bone positions for each frame of a local-TRS sequence (applies the locals to the instance).
        public static Vector3[][] RootPositions(GameObject go, Transform[] bones, Quaternion[][] rot, Vector3[][] pos)
        {
            var o = new Vector3[rot.Length][];
            for (int f = 0; f < rot.Length; f++)
            {
                for (int i = 0; i < bones.Length; i++) { bones[i].localRotation = rot[f][i]; bones[i].localPosition = pos[f][i]; }
                o[f] = bones.Select(t => go.transform.InverseTransformPoint(t.position)).ToArray();
            }
            return o;
        }

        static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1;
        }

        static Vector4 Hermite4(Vector4 p0, Vector4 m0, Vector4 p1, Vector4 m1, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0 + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1;
        }
    }
}
