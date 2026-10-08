using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>The one sign-distance rule (f-td): flat XZ, inclusive at the radius, height ignored.</summary>
    public class FishEventDistanceTests
    {
        [Test]
        public void Flat_IgnoresHeight_AndIsInclusiveAtTheRadius()
        {
            var eye = new Vector3(0f, 27.7f, 0f);
            Assert.AreEqual(150f, FishEventDistance.Flat(eye, new Vector3(150f, 0f, 0f)), 1e-4f);
            Assert.IsTrue(FishEventDistance.Within(new Vector3(90f, -3f, 120f), eye, 150f), "exactly 150 m flat counts");
            Assert.IsFalse(FishEventDistance.Within(new Vector3(90.01f, 0f, 120f), eye, 150f));
            Assert.IsTrue(FishEventDistance.Within(new Vector3(147.5f, 0f, 0f), eye, 150f), "inside flat though 150.1 m in 3D from a raised eye");
        }
    }
}
