using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>
    /// Pins the fish visibility rule (gate/fish-technique addendum, T7) at the landed water numbers, so a change to the
    /// water material shows up here as a test diff. The last test checks the shipped material still carries them.
    /// </summary>
    public class FishMurkTests
    {
        const string WaterMaterial = "Assets/World/BellsBend/Water/BellsBendWater.mat";
        static readonly FishMurk Landed = new FishMurk(new Vector3(2.2f, 2.4f, 3.2f), 0.15f, 0.02f);

        static float Limit(float viewY)
        {
            // Deepest body top still drawable, by bisection on the monotonic visibility.
            float lo = 0f, hi = 5f;
            for (int i = 0; i < 40; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Landed.Visibility(mid, viewY) >= Landed.floor) lo = mid; else hi = mid;
            }
            return lo;
        }

        [Test]
        public void StraightDown_LimitIs1_57m()
        {
            Assert.AreEqual(0.98f, Landed.Visibility(0f + 1e-6f, 1f), 0.01f);
            Assert.That(Limit(1f), Is.InRange(1.52f, 1.62f));
        }

        [Test]
        public void BluffTop_LimitIs1_10m()
        {
            Assert.That(Limit(0.7f), Is.InRange(1.05f, 1.15f));
        }

        [Test]
        public void ShoreEye_FiveMetresOut_LimitIs0_48m()
        {
            // 1.7 m eye, 5 m out: V.y ~ 0.32.
            float vy = 1.7f / Mathf.Sqrt(1.7f * 1.7f + 25f);
            Assert.That(Limit(vy), Is.InRange(0.44f, 0.53f));
        }

        [Test]
        public void Jump_IsAlwaysDrawable()
        {
            Assert.AreEqual(1f, Landed.Visibility(-0.2f, 0.1f));
            Assert.IsTrue(Landed.Drawable(0f, 0.3f, new Vector3(0f, 0.1f, 0f), new Vector3(30f, 0.1f, 0f)));
        }

        [Test]
        public void Drawable_UsesTheShallowestPointOfTheBody()
        {
            var cam = new Vector3(0f, 10f, 0f);
            Assert.IsTrue(Landed.Drawable(0f, -0.5f, new Vector3(0f, -2f, 0f), cam));
            Assert.IsFalse(Landed.Drawable(0f, -2f, new Vector3(0f, -2f, 0f), cam));
        }

        [Test]
        public void ShippedWaterMaterial_MatchesThePinnedNumbers()
        {
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(WaterMaterial);
            Assert.IsNotNull(mat, WaterMaterial);
            var m = FishMurk.FromWater(mat);
            Assert.AreEqual(Landed.extinction.x, m.extinction.x, 1e-4f);
            Assert.AreEqual(Landed.extinction.y, m.extinction.y, 1e-4f);
            Assert.AreEqual(Landed.extinction.z, m.extinction.z, 1e-4f);
            Assert.AreEqual(Landed.minCosView, m.minCosView, 1e-4f);
            Assert.AreEqual(Landed.f0, m.f0, 1e-4f);
        }
    }
}
