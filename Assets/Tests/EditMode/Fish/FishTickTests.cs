using NUnit.Framework;
using WashedAshore.Fish;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>adr/fish-1 §5.5: the fixed step is independent of frame rate.</summary>
    public class FishTickTests
    {
        [Test]
        public void SameSimTime_SameTickCount_AtAnyFrameRate()
        {
            var fast = new FishTick(15f);
            var slow = new FishTick(15f);
            for (int i = 0; i < 1440; i++) fast.Advance(1f / 144f);
            for (int i = 0; i < 300; i++) slow.Advance(1f / 30f);
            Assert.AreEqual(150, fast.Count, 1);
            Assert.AreEqual(150, slow.Count, 1);
        }

        [Test]
        public void Alpha_StaysInUnitRange()
        {
            var t = new FishTick(15f);
            for (int i = 0; i < 500; i++)
            {
                t.Advance(0.0123f);
                Assert.That(t.Alpha, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        [Test]
        public void Hitch_IsCapped_AndBacklogDropped()
        {
            var t = new FishTick(15f, 4);
            Assert.AreEqual(4, t.Advance(2f));
            Assert.AreEqual(0f, t.Alpha, 1e-6f);
            Assert.AreEqual(1, t.Advance(1f / 15f + 1e-4f));
        }
    }
}
