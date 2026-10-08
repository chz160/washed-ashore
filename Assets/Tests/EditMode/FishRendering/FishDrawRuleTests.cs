using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.Fish.Rendering;

namespace WashedAshore.Tests.EditMode.FishRendering
{
    /// <summary>
    /// f-td review 3a (N7 no-pop spawn rule): whatever the renderer draws, the sim's FishView.CouldSee must also call
    /// visible, so tiers, R_sub and the frustum only ever remove fish. Plus the tier table (ruling/fish-visibility-tiers).
    /// Murk values are the landed water's (2.2/2.4/3.2, 0.15, 0.02), floor 0.02.
    /// </summary>
    public class FishDrawRuleTests
    {
        static readonly FishMurk Murk = new FishMurk(new Vector3(2.2f, 2.4f, 3.2f), 0.15f, 0.02f);
        const float TierDepth = 0.6f, DrawDistance = 25f, JumpRadius = 40f;
        static FishTuning Tuning()
        {
            var t = ScriptableObject.CreateInstance<FishTuning>();
            t.bodyDrawDistance = DrawDistance; t.jumpBodyDrawRadius = JumpRadius;
            return t;
        }
        // The shared rule (FishTuning.BodyDrawRadius), as the renderer calls it.
        static float Radius(bool jumping, float topDepth)
        {
            var t = Tuning();
            float r = t.BodyDrawRadius(jumping ? FishMode.Jump : FishMode.Cruise, topDepth);
            Object.DestroyImmediate(t);
            return r;
        }

        [Test]
        public void DrawnSet_IsInsideCouldSee_OverRandomFishAndCameras()
        {
            var rng = new System.Random(1987);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int drawn = 0, checkedFish = 0, airborne = 0;
            var tuning = Tuning();
            for (int c = 0; c < 200; c++)
            {
                var eye = new Vector3(R(-30f, 30f), R(0.3f, 30f), R(-30f, 30f));
                var look = new Vector3(R(-30f, 30f), 0f, R(-30f, 30f)) - eye;
                var proj = Matrix4x4.Perspective(R(20f, 70f), 16f / 9f, 0.1f, 3000f);
                var view = Matrix4x4.TRS(eye, Quaternion.LookRotation(look.sqrMagnitude > 1e-4f ? look : Vector3.forward), new Vector3(1f, 1f, -1f)).inverse;
                var planes = GeometryUtility.CalculateFrustumPlanes(proj * view);
                var fv = new FishView(eye, planes, tuning, Murk); // CouldSee with the same BodyDrawRadius
                for (int f = 0; f < 200; f++)
                {
                    float surface = 0f;
                    float length = R(0.1f, 1.2f), height = length * R(0.1f, 0.6f);
                    float top = R(-0.3f, 2f) * -1f; // body top from 0.3 m above to 2 m below the surface
                    var centre = new Vector3(R(-40f, 40f), top - 0.5f * height, R(-40f, 40f));
                    var tier = (FishTier)rng.Next(0, 3);
                    bool jumping = rng.NextDouble() < 0.2;
                    if (jumping) top = R(0f, 0.6f); // airborne: body top at or above the surface
                    centre.y = top - 0.5f * height;
                    var fishMode = jumping ? FishMode.Jump : FishMode.Cruise;
                    float radius = tuning.BodyDrawRadius(fishMode, surface - top);
                    var mode = FishDrawRule.Decide(Murk, planes, eye, tier, false, surface, top - height, top, centre, 0.5f * length,
                                                   TierDepth, radius, out _, out _);
                    checkedFish++;
                    if (mode == FishDrawMode.None) continue;
                    drawn++;
                    if (jumping && top >= surface && (centre - eye).magnitude > DrawDistance) airborne++;
                    Assert.IsTrue(fv.CouldSee(top, centre, surface, length, fishMode),
                        $"drawn but not CouldSee: tier {tier}, top {top:F2}, centre {centre}, eye {eye}, mode {mode}");
                }
            }
            Object.DestroyImmediate(tuning);
            Assert.Greater(drawn, 50, $"the sample must draw some fish ({drawn} of {checkedFish})");
            Assert.Greater(airborne, 0, "the sample must include airborne bodies drawn beyond 25 m");
        }

        [TestCase(FishTier.V1, false, 0.3f, FishDrawMode.Body)]
        [TestCase(FishTier.V1, false, 0.8f, FishDrawMode.None)]   // V1 body only within 0.6 m
        [TestCase(FishTier.V2, false, 0.3f, FishDrawMode.Shadow)]  // V2 (incl. gar, L1) never a lit body
        [TestCase(FishTier.V3, true, 0.1f, FishDrawMode.None)]    // V3 signs only
        [TestCase(FishTier.V1, true, 0.1f, FishDrawMode.None)]    // neverDrawBody wins
        public void Tiers(FishTier tier, bool never, float topDepth, FishDrawMode expected)
        {
            Assert.AreEqual(expected, FishDrawRule.Decide(tier, never, topDepth, 5f, 0.5f, Murk.floor, TierDepth, DrawDistance));
        }

        [Test]
        public void JumpingBody_DrawsTo40m_SubmergedTo25m()
        {
            Assert.AreEqual(FishDrawMode.Body, FishDrawRule.Decide(FishTier.V1, false, -0.1f, 35f, 1f, Murk.floor, TierDepth, Radius(true, -0.1f)));
            Assert.AreEqual(FishDrawMode.None, FishDrawRule.Decide(FishTier.V1, false, -0.1f, 41f, 1f, Murk.floor, TierDepth, Radius(true, -0.1f)));
            Assert.AreEqual(FishDrawMode.None, FishDrawRule.Decide(FishTier.V1, false, 0.2f, 35f, 0.5f, Murk.floor, TierDepth, Radius(true, 0.2f)));
        }

        [Test]
        public void AirborneBody_OnlyForAllowedSpecies_OnlyAboveTheSurface()
        {
            // Skipjack (V3, neverDrawBody, airborneBodyAllowed): a body in its jump, none underwater.
            Assert.AreEqual(FishDrawMode.Body, FishDrawRule.Decide(FishTier.V3, true, -0.1f, 30f, 1f, Murk.floor, TierDepth, JumpRadius, airborneBody: true));
            Assert.AreEqual(FishDrawMode.None, FishDrawRule.Decide(FishTier.V3, true, 0.1f, 10f, 0.5f, Murk.floor, TierDepth, DrawDistance, airborneBody: true));
            Assert.AreEqual(FishDrawMode.None, FishDrawRule.Decide(FishTier.V3, true, -0.1f, 30f, 1f, Murk.floor, TierDepth, JumpRadius, airborneBody: false));
        }

        [TestCase(60f, 5f, 1f, 0.02f)]   // walking camera, full murk alpha
        [TestCase(30f, 5f, 1f, 0.02f)]   // web
        [TestCase(60f, 2.5f, 0.3f, 0.01f)] // f-td r3: a 0.3-alpha fish at 2.5 m/s, bound ~0.0052
        [TestCase(30f, 2.5f, 0.3f, 0.01f)] // web
        public void ShallowV1_WalkingOut22To26m_FadesSmoothlyAcrossTheBand(float fps, float speed, float murkAlpha, float slack)
        {
            // Brief 3 m edge band (f-td bound): per frame the drawn alpha may drop at most murkAlpha x speed x dt / band + slack,
            // which a hard edge at the radius would fail.
            float dt = 1f / fps, bound = murkAlpha * speed * dt / FishDrawRule.RangeFadeBand + slack;
            float prev = -1f, dist = 22f;
            while (dist < 26f)
            {
                var mode = FishDrawRule.Decide(FishTier.V1, false, 0.1f, dist, murkAlpha, Murk.floor, TierDepth, DrawDistance);
                float alpha = mode == FishDrawMode.None ? 0f : murkAlpha * FishDrawRule.RangeFade(dist, DrawDistance);
                if (prev >= 0f) Assert.LessOrEqual(prev - alpha, bound, $"alpha step at {dist:F2} m, {fps} fps (bound {bound:F3})");
                prev = alpha; dist += speed * dt;
            }
            Assert.AreEqual(0f, prev, "gone past the radius");
            Assert.AreEqual(1f, FishDrawRule.RangeFade(22f, 25f), 1e-5);
            Assert.AreEqual(0.5f, FishDrawRule.RangeFade(23.5f, 25f), 1e-5);
            Assert.AreEqual(0f, FishDrawRule.RangeFade(25f, 25f), 1e-5);
        }

        [Test]
        public void NothingDrawn_BeyondRSub_OrUnderTheMurkFloor()
        {
            Assert.AreEqual(FishDrawMode.None, FishDrawRule.Decide(FishTier.V1, false, 0.2f, 26f, 0.5f, Murk.floor, TierDepth, DrawDistance));
            Assert.AreEqual(FishDrawMode.None, FishDrawRule.Decide(FishTier.V1, false, -0.1f, 26f, 1f, Murk.floor, TierDepth, DrawDistance));
            Assert.AreEqual(FishDrawMode.None, FishDrawRule.Decide(FishTier.V2, false, 0.2f, 5f, 0.019f, Murk.floor, TierDepth, DrawDistance));
        }
    }
}
