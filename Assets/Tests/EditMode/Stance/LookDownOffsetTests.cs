using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.EditMode.Stance
{
    /// <summary>
    /// td ruling, director amend2 (C3): the look-down view offset is a pure function of the stance, the previous stance, the stance blend
    /// and the camera pitch. Zero below the first table point, continuous, free of overshoot (monotone cubic), zero for Squat, Sit and
    /// Sleep, and it eases through a stance change. Nothing else (animation, speed, time) is an input: the function has no other parameters.
    /// </summary>
    public class LookDownOffsetTests
    {
        static float F(LookDownTuning t, Gameplay.Stance from, Gameplay.Stance to, float blend, float pitch)
        {
            LookDownOffset.Evaluate(t, from, to, blend, pitch, out float b, out float d);
            Assert.AreEqual(0f, d, "down is always 0");
            return b;
        }

        [TestCase(Gameplay.Stance.Stand)]
        [TestCase(Gameplay.Stance.Crouch)]
        public void ZeroBelowTheFirstPoint(Gameplay.Stance s)
        {
            var t = new LookDownTuning();
            float first = t.Of(s)[0].x;
            for (float p = -85f; p < first; p += 5f) Assert.AreEqual(0f, F(t, s, s, 1f, p), $"{s} at pitch {p}");
        }

        [TestCase(Gameplay.Stance.Stand)]
        [TestCase(Gameplay.Stance.Crouch)]
        public void HitsEveryTablePoint(Gameplay.Stance s)
        {
            var t = new LookDownTuning();
            foreach (var p in t.Of(s)) Assert.AreEqual(p.y, F(t, s, s, 1f, p.x), 1e-4f, $"{s} at {p.x}");
        }

        [TestCase(Gameplay.Stance.Stand)]
        [TestCase(Gameplay.Stance.Crouch)]
        public void ContinuousAndNoOvershoot(Gameplay.Stance s)
        {
            var t = new LookDownTuning();
            var pts = t.Of(s);
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (var p in pts) { lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); }
            float last = F(t, s, s, 1f, -85f), maxStep = 0f;
            for (float p = -85f; p <= 85.01f; p += 0.5f)
            {
                float v = F(t, s, s, 1f, p);
                Assert.GreaterOrEqual(v, Mathf.Min(0f, lo) - 1e-4f, $"undershoot at {p}");
                Assert.LessOrEqual(v, hi + 1e-4f, $"overshoot at {p}");
                maxStep = Mathf.Max(maxStep, Mathf.Abs(v - last));
                last = v;
            }
            Assert.Less(maxStep, 0.02f, "offset jumps between 0.5 degree steps");
        }

        [Test]
        public void MonotoneWhereTheTableIsMonotone()
        {
            var t = new LookDownTuning();
            foreach (var s in new[] { Gameplay.Stance.Stand, Gameplay.Stance.Crouch })
            {
                float last = 0f;
                for (float p = -85f; p <= 85.01f; p += 0.5f)
                {
                    float v = F(t, s, s, 1f, p);
                    Assert.GreaterOrEqual(v, last - 1e-5f, $"{s} fell at pitch {p}");
                    last = v;
                }
            }
        }

        [TestCase(Gameplay.Stance.Squat)]
        [TestCase(Gameplay.Stance.Sit)]
        [TestCase(Gameplay.Stance.Sleep)]
        public void LoweredStancesAreAccepted_NoOffset(Gameplay.Stance s)
        {
            var t = new LookDownTuning();
            for (float p = -85f; p <= 85f; p += 5f) Assert.AreEqual(0f, F(t, s, s, 1f, p));
        }

        [Test]
        public void StanceChangeBlendsWithTheEyeEase_NoJump()
        {
            var t = new LookDownTuning();
            float a = F(t, Gameplay.Stance.Stand, Gameplay.Stance.Squat, 0f, 85f);
            float b = F(t, Gameplay.Stance.Stand, Gameplay.Stance.Squat, 1f, 85f);
            Assert.AreEqual(t.stand[t.stand.Length - 1].y, a, 1e-4f, "start of the change equals the old stance");
            Assert.AreEqual(0f, b, 1e-5f, "end of the change equals the new stance");
            float last = a;
            for (float k = 0f; k <= 1f; k += 0.05f)
            {
                float v = F(t, Gameplay.Stance.Stand, Gameplay.Stance.Squat, k, 85f);
                Assert.LessOrEqual(v, last + 1e-6f);
                last = v;
            }
        }

        [Test]
        public void CrouchClearsTheNeckAtPlus65AndPlus70_AndStandAtPlus50IsZero()
        {
            var t = new LookDownTuning();
            Assert.GreaterOrEqual(F(t, Gameplay.Stance.Crouch, Gameplay.Stance.Crouch, 1f, 70f), 0.05f, "amend4: Crouch +70 must clear the neck (neck rays go to 0 at f >= 0.05)");
            Assert.GreaterOrEqual(F(t, Gameplay.Stance.Crouch, Gameplay.Stance.Crouch, 1f, 65f), 0.04f, "Crouch +65 clears the neck at 0.05 in the sweep");
            Assert.AreEqual(0f, F(t, Gameplay.Stance.Stand, Gameplay.Stance.Stand, 1f, 40f), 1e-5f);
        }

        [Test]
        public void SameInputsSameOutput()
        {
            var t = new LookDownTuning();
            Assert.AreEqual(F(t, Gameplay.Stance.Stand, Gameplay.Stance.Crouch, 0.4f, 63f), F(t, Gameplay.Stance.Stand, Gameplay.Stance.Crouch, 0.4f, 63f));
        }

        [Test]
        public void MonotoneCubicDoesNotOvershootASteepKnee()
        {
            var pts = new[] { new Vector2(20f, 0f), new Vector2(60f, 0.05f), new Vector2(85f, 0.35f) };
            for (float p = 20f; p <= 85f; p += 0.25f)
            {
                float v = LookDownOffset.MonotoneCubic(pts, p);
                Assert.GreaterOrEqual(v, -1e-5f);
                Assert.LessOrEqual(v, 0.35f + 1e-5f);
            }
        }
    }
}
