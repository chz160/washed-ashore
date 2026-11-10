using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>
    /// qa-49 12cp T2/T4: the back-face tie rule on synthetic triangles. An exact mirror pair within 1e-5 m prefers the front face; an unrelated back face at the same distance, a lone back face, a mirror
    /// pair seen from behind and a near-mirror (one vertex 1 mm off) are all still reported.
    /// </summary>
    public class MirrorTieTests
    {
        struct Hit { public float t; public bool back; public Vector3 a, b, c; }

        static readonly Vector3 A = new Vector3(0, 0, 0), B = new Vector3(1, 0, 0), C = new Vector3(0, 1, 0);

        static Hit Front(float t) => new Hit { t = t, back = false, a = A, b = B, c = C };
        static Hit MirrorOf(Hit h, float t) => new Hit { t = t, back = !h.back, a = h.a, b = h.c, c = h.b };   // same positions, opposite winding
        static Hit Other(float t, bool back) => new Hit { t = t, back = back, a = new Vector3(5, 5, 5), b = new Vector3(6, 5, 5), c = new Vector3(5, 6, 5) };

        /// <summary>The nearest-hit loop of the sweeps, in the order the hits arrive; returns whether the final nearest hit is a back face (and false when there is no hit).</summary>
        static bool NearestIsBack(params Hit[] hits)
        {
            bool have = false; Hit best = default;
            foreach (var h in hits)
                if (MirrorTie.Replaces(have, best.t, best.back, best.a, best.b, best.c, h.t, h.back, h.a, h.b, h.c)) { best = h; have = true; }
            return have && best.back;
        }

        [SetUp] public void Reset() => MirrorTie.ResetCounters();

        [Test]
        public void ExactMirrorPairPrefersTheFrontFaceInEitherOrder()
        {
            var f = Front(0.200f);
            var m = MirrorOf(f, 0.200f);
            Assert.IsFalse(NearestIsBack(f, m), "front then mirror");
            Assert.IsFalse(NearestIsBack(m, f), "mirror then front");
            Assert.AreEqual(2, MirrorTie.Resolved, "one tie-resolved hit per ray");
            var f2 = Front(0.200f);
            Assert.IsFalse(NearestIsBack(f2, MirrorOf(f2, 0.200f + 0.9e-5f)), "inside the 1e-5 tolerance");
        }

        [Test]
        public void WithTheRuleSwitchedOffTheMirrorPairFallsBackToThePlainNearestHit()
        {
            var f = Front(0.200f);
            bool was = MirrorTie.Enabled;
            try
            {
                MirrorTie.Enabled = false;
                Assert.IsTrue(NearestIsBack(MirrorOf(f, 0.200f), f), "OFF: the first of two equal hits stays, here the back face");
                Assert.IsFalse(NearestIsBack(f, MirrorOf(f, 0.200f)), "OFF: the first of two equal hits stays, here the front face");
                Assert.AreEqual(0, MirrorTie.Resolved, "OFF: nothing is tie-resolved");
            }
            finally { MirrorTie.Enabled = was; }
        }

        [Test]
        public void MirrorPairBeyondTheToleranceKeepsTheNearestHit()
        {
            var f = Front(0.200f);
            Assert.IsTrue(NearestIsBack(MirrorOf(f, 0.200f), Front(0.200f + 1e-3f)), "a back face nearer than the front by 1 mm is a real back face");
            Assert.AreEqual(0, MirrorTie.Resolved);
        }

        [Test]
        public void AnUnrelatedBackFaceAtTheSameDistanceStillCounts()
        {
            Assert.IsTrue(NearestIsBack(Other(0.200f, true), Front(0.200f)), "unrelated back face first, equal distance front face after");
            Assert.AreEqual(0, MirrorTie.Resolved, "nothing was resolved");
            Assert.AreEqual(1, MirrorTie.NearTiesNotMirrored, "the near tie was seen and left to the plain rule");
        }

        [Test]
        public void ALoneBackFaceIsReported()
        {
            Assert.IsTrue(NearestIsBack(Other(0.3f, true)));
            Assert.IsFalse(NearestIsBack(), "no hit is not a back face");
        }

        [Test]
        public void ANearMirrorWithOneVertexOneMillimetreOffIsNotAMirror()
        {
            var f = Front(0.200f);
            var m = MirrorOf(f, 0.200f);
            m.c += new Vector3(0.001f, 0, 0);
            Assert.IsTrue(NearestIsBack(m, f), "not an exact mirror: the plain rule keeps the first hit, a back face");
            Assert.AreEqual(0, MirrorTie.Resolved);
        }

        [Test]
        public void ForcedCase_AMirroredFrontFaceDoesNotHideARealBackFaceElsewhere()
        {
            var f = Front(0.200f);
            // ray 1 meets a mirrored lid (front wins) and a real back face further behind; ray 2 meets only the real back face
            Assert.IsFalse(NearestIsBack(f, MirrorOf(f, 0.200f), Other(0.250f, true)), "ray 1: the lid shows its front");
            Assert.IsTrue(NearestIsBack(Other(0.250f, true)), "ray 2: the real back face is still reported");
            Assert.AreEqual(1, MirrorTie.Resolved);
        }
    }
}
