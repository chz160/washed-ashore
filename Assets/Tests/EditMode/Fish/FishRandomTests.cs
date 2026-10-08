using NUnit.Framework;
using WashedAshore.Fish;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>adr/fish-1 §5.7: the fish RNG is stateless, order-independent and seed-sensitive.</summary>
    public class FishRandomTests
    {
        [Test]
        public void SameInputs_SameValue_InAnyOrder()
        {
            float a = FishRandom.Value(101, FishRandom.StreamPlan, FishRandom.CellId(3, -7), 2, 5);
            FishRandom.Value(101, FishRandom.StreamPlan, FishRandom.CellId(9, 9), 0, 0);
            Assert.AreEqual(a, FishRandom.Value(101, FishRandom.StreamPlan, FishRandom.CellId(3, -7), 2, 5));
        }

        [Test]
        public void SeedStreamCellFishCounter_EachChangeTheValue()
        {
            long c = FishRandom.CellId(3, -7);
            float a = FishRandom.Value(101, FishRandom.StreamPlan, c, 2, 5);
            Assert.AreNotEqual(a, FishRandom.Value(102, FishRandom.StreamPlan, c, 2, 5));
            Assert.AreNotEqual(a, FishRandom.Value(101, FishRandom.StreamSteer, c, 2, 5));
            Assert.AreNotEqual(a, FishRandom.Value(101, FishRandom.StreamPlan, FishRandom.CellId(-7, 3), 2, 5));
            Assert.AreNotEqual(a, FishRandom.Value(101, FishRandom.StreamPlan, c, 3, 5));
            Assert.AreNotEqual(a, FishRandom.Value(101, FishRandom.StreamPlan, c, 2, 6));
        }

        [Test]
        public void Values_AreInUnitRange_AndRoughlyUniform()
        {
            int[] bins = new int[10];
            for (uint i = 0; i < 20000; i++)
            {
                float v = FishRandom.Value(7, FishRandom.StreamEvents, 0, 0, i);
                Assert.That(v, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
                bins[(int)(v * 10)]++;
            }
            foreach (int b in bins) Assert.That(b, Is.InRange(1800, 2200));
        }

        [Test]
        public void Draws_AdvanceTheCounter_AndReplay()
        {
            var a = new FishDraws(101, FishRandom.StreamSteer, 42, 1);
            var b = new FishDraws(101, FishRandom.StreamSteer, 42, 1);
            float a0 = a.Next(), a1 = a.Next();
            Assert.AreNotEqual(a0, a1);
            Assert.AreEqual(a0, b.Next());
            Assert.AreEqual(a1, b.Next());
            Assert.AreEqual(2u, a.Counter);
        }

        [TearDown]
        public void Restore() => FishRandom.OverrideSeed(null);
    }
}
