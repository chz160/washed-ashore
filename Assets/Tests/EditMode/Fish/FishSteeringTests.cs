using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>F4/F5 pure rules: depth band, turn cap, anim-rate map, separation, flee direction.</summary>
    public class FishSteeringTests
    {
        [Test]
        public void DepthBand_StaysBetweenBedClearanceAndSurfaceMargin()
        {
            var b = FishSteering.DepthBand(-10f, 0.02f, 0.3f, 0.25f);
            Assert.AreEqual(-9.7f, b.x, 1e-5f);
            Assert.AreEqual(-0.23f, b.y, 1e-5f);
            Assert.AreEqual(b.x, FishSteering.HoldY(b, -1f), 1e-6f);
            Assert.AreEqual(b.y, FishSteering.HoldY(b, 2f), 1e-6f);
        }

        [Test]
        public void DepthBand_CollapsesInShallowWater_AndColumnIsUnusable()
        {
            var b = FishSteering.DepthBand(-0.3f, 0f, 0.3f, 0.25f);
            Assert.AreEqual(b.x, b.y, 1e-6f);
            Assert.IsFalse(FishSteering.Usable(-0.3f, 0f, 0.3f, 0.25f, 0.5f));
            Assert.IsTrue(FishSteering.Usable(-2f, 0f, 0.3f, 0.25f, 0.5f));
        }

        [Test]
        public void TurnToward_NeverExceedsCap()
        {
            float y = FishSteering.TurnToward(0f, 179f, 720f, 0.5f, 45f);
            Assert.AreEqual(45f, y, 1e-4f);
            y = FishSteering.TurnToward(0f, -90f, 60f, 0.1f, 45f);
            Assert.AreEqual(-6f, y, 1e-4f);
        }

        [Test]
        public void Hitch_FourTicksOf179DegTurn_RendersAtMost45DegPerFrame()
        {
            var tick = new FishTick(15f, 4);
            float simPrev = 0f, sim = 0f, rendered = 0f;
            // Tick cap 45 deg: a 4-tick hitch moves the sim 180 deg toward a 179 deg target in one frame.
            int n = tick.Advance(1f);
            Assert.AreEqual(4, n);
            for (int i = 0; i < n; i++) { simPrev = sim; sim = FishSteering.TurnToward(sim, 179f, 10000f, tick.Step, 45f); }
            float before = rendered;
            rendered = FishSteering.RenderedYaw(rendered, FishSteering.LerpYaw(simPrev, sim, tick.Alpha), 45f);
            Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(before, rendered)), 45f + 1e-4f);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, sim)), 45f, "the hitch must actually exceed the cap in sim");
            // Following frames catch up, still capped.
            for (int f = 0; f < 10; f++)
            {
                before = rendered;
                tick.Advance(1f / 60f);
                rendered = FishSteering.RenderedYaw(rendered, FishSteering.LerpYaw(simPrev, sim, tick.Alpha), 45f);
                Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(before, rendered)), 45f + 1e-4f);
            }
        }

        [Test]
        public void RateCoversBurst_FlagsAClampingTuning()
        {
            Assert.IsTrue(FishSteering.RateCoversBurst(1.5f, 0.3f, 2f, 4f));
            Assert.IsFalse(FishSteering.RateCoversBurst(2.5f, 0.3f, 2f, 4f));
        }

        [Test]
        public void AnimRate_IsLinearInSpeed_WithIdleFloorAndCap()
        {
            Assert.AreEqual(0.3f, FishSteering.AnimRate(0f, 0.3f, 2f, 4f), 1e-6f);
            Assert.AreEqual(1.3f, FishSteering.AnimRate(0.5f, 0.3f, 2f, 4f), 1e-6f);
            Assert.AreEqual(4f, FishSteering.AnimRate(10f, 0.3f, 2f, 4f), 1e-6f);
        }

        [Test]
        public void Separation_PushesApartOverlappingMates_Only()
        {
            var mates = new[] { Vector3.zero, new Vector3(0.2f, 0f, 0f), new Vector3(5f, 0f, 0f) };
            var push = FishSteering.Separation(mates, mates.Length, 0, 0.5f);
            Assert.Less(push.x, 0f);
            Assert.AreEqual(0f, push.z, 1e-6f);
            Assert.AreEqual(Vector3.zero, FishSteering.Separation(mates, mates.Length, 2, 0.5f));
        }

        [Test]
        public void FleeDirection_IsAwayFromPlayer_AndHorizontal()
        {
            var d = FishSteering.FleeDirection(new Vector3(1f, -2f, 0f), Vector3.zero, Vector3.zero, 0f);
            Assert.AreEqual(1f, d.x, 1e-5f);
            Assert.AreEqual(0f, d.y, 1e-6f);
            d = FishSteering.FleeDirection(new Vector3(1f, 0f, 0f), Vector3.zero, Vector3.forward, 1f);
            Assert.Greater(d.x, 0f);
            Assert.Greater(d.z, 0f);
        }
    }
}
