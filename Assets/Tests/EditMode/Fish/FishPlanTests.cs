using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>
    /// A straight bank along z at x = 0, water toward +x, with the level's bed profile: a 0.3 m lip at the waterline
    /// (x = 0), shelf to W - 2 at 7 m, ramp to W - 10 by 20 m, flat beyond. Structure (type 0) is the strip
    /// 0 &lt;= z &lt; structureLength out to 20 m. No tile beyond x = noTileX (50 m by default, so the open band is 30 m
    /// wide, its nominalWidth).
    /// </summary>
    class AnalyticRiver : IFishWater
    {
        public float WaterLevelY => 0f;
        public float structureLength = 200f;
        public float noTileX = 50f;
        /// <summary>Optional moving surface for F4-style checks; flat by default.</summary>
        public System.Func<float, float, float> surface;

        public bool TryBed(float x, float z, out float bedY)
        {
            bedY = Bed(x);
            return x < noTileX;
        }

        public static float Bed(float x)
        {
            if (x <= 0f) return 0.3f - x * 0.1f; // the lip and the bank, dry
            if (x <= 7f) return Mathf.Lerp(0f, -2f, x / 7f);
            if (x <= 20f) return Mathf.Lerp(-2f, -10f, (x - 7f) / 13f);
            return -10f;
        }

        public float ShoreDistance(float x, float z) => -x;

        public int StructureAt(float x, float z, out float anchorArea)
        {
            anchorArea = structureLength * 20f;
            return x > 0f && x <= 20f && z >= 0f && z < structureLength ? 0 : -1;
        }

        public float SurfaceY(float x, float z) => surface != null ? surface(x, z) : 0f;
    }

    /// <summary>F3 groundwork: the virtual population is a pure function of (seed, cell) and hits the brief's densities.</summary>
    public class FishPlanTests
    {
        public const int Sunfish = 0, Catfish = 1, Shad = 2, Flathead = 3;

        /// <summary>A small brief in fish-targets.json's shape: three depth bands, three adult species, one school type, one structure type.</summary>
        internal static FishTuning Tuning()
        {
            var t = ScriptableObject.CreateInstance<FishTuning>();
            t.bands = new[]
            {
                new FishBand { name = "Shelf", bedDepth = new Vector2(0f, 2f), nominalWidth = 7f, fishPer100m = 10f },
                new FishBand { name = "Ramp", bedDepth = new Vector2(2f, 9.9f), nominalWidth = 13f, fishPer100m = 8f },
                new FishBand { name = "Open", bedDepth = new Vector2(9.9f, 99f), nominalWidth = 30f, fishPer100m = 5f },
            };
            t.species = new[]
            {
                new FishSpecies { name = "Sunfish", variants = new[] { 0 }, lengthRange = new Vector2(0.13f, 0.24f), per100mByBand = new[] { 10f, 2f, 0f },
                    schoolsPer100mByBand = new float[3], grouping = FishGrouping.Loose, groupSize = new Vector2Int(3, 8), minBedDepth = 0.35f,
                    depthMode = FishDepthMode.Mid, depthRange = new Vector2(0.3f, 1.5f), bedClearance = 0.08f, surfaceMargin = 0.05f },
                new FishSpecies { name = "Catfish", variants = new[] { 1 }, lengthRange = new Vector2(0.3f, 0.8f), per100mByBand = new[] { 0f, 6f, 5f },
                    schoolsPer100mByBand = new float[3], grouping = FishGrouping.Solitary, groupSize = new Vector2Int(1, 3), minBedDepth = 1f,
                    depthMode = FishDepthMode.NearBed, bedOffset = new Vector2(0.2f, 0.6f), bottom = true },
                new FishSpecies { name = "Shad", variants = new[] { 1 }, lengthRange = new Vector2(0.1f, 0.3f), per100mByBand = new float[3],
                    schoolUnit = true, schoolsPer100mByBand = new[] { 0.5f, 0.5f, 0.5f }, grouping = FishGrouping.School, groupSize = new Vector2Int(15, 40),
                    minBedDepth = 1f, depthRange = new Vector2(0.3f, 3f), spacingBodyLengths = new Vector2(0.6f, 1f) },
                new FishSpecies { name = "Flathead", variants = new[] { 1 }, lengthRange = new Vector2(0.38f, 1.14f), per100mByBand = new float[3],
                    schoolsPer100mByBand = new float[3], grouping = FishGrouping.Solitary, groupSize = new Vector2Int(1, 1), minBedDepth = 1f,
                    depthMode = FishDepthMode.NearBed, bedOffset = new Vector2(0.1f, 0.4f), bottom = true },
            };
            t.structures = new[]
            {
                new FishStructureType { kind = "HollowMouth", bandMultiplier = new[] { 2f, 2f, 1f }, levelKinds = new[] { "HollowMouth" }, prefs = new[] { 3f, 1f, 1f, 1f },
                    extraSpecies = Flathead, extraPer100mShore = 2.5f, extraSchoolSpecies = Shad, extraSchoolsPerAnchor = 1f },
            };
            foreach (var s in t.species) { s.burstSpeed = 1.5f; s.cruiseSpeed = 0.2f; s.lengthTailMax = s.lengthRange.y; }
            // The brief's burstRule: idle + burst / (0.7 x lengthMin) <= 12.5, so burst <= 8.5 x lengthMin.
            t.species[Sunfish].burstSpeed = 1.1f;
            t.species[Shad].burstSpeed = 0.85f;
            t.jumpBodyDrawRadius = t.bodyDrawDistance;
            return t;
        }

        internal static List<FishGroupPlan> Plan(FishTuning t, IFishWater w, int seed, float length)
        {
            var all = new List<FishGroupPlan>();
            int cells = Mathf.CeilToInt(length / t.cellSize), across = Mathf.CeilToInt(60f / t.cellSize);
            for (int cz = 0; cz < cells; cz++)
                for (int cx = -1; cx <= across; cx++)
                    FishPlan.Cell(t, w, seed, cx, cz, all);
            return all;
        }

        [Test]
        public void SameSeedAndCell_SameGroups_InAnyOrder()
        {
            var t = Tuning();
            var w = new AnalyticRiver();
            var a = new List<FishGroupPlan>();
            var b = new List<FishGroupPlan>();
            FishPlan.Cell(t, w, 101, 0, 3, a);
            FishPlan.Cell(t, w, 101, 1, 7, b); // unrelated cell first
            b.Clear();
            FishPlan.Cell(t, w, 101, 0, 3, b);
            Assert.Greater(a.Count, 0);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].anchor, b[i].anchor);
                Assert.AreEqual(a[i].species, b[i].species);
                Assert.AreEqual(a[i].members, b[i].members);
            }
        }

        [Test]
        public void DifferentSeeds_DifferentPlans()
        {
            var t = Tuning();
            var w = new AnalyticRiver();
            var a = Plan(t, w, 101, 640f);
            var b = Plan(t, w, 202, 640f);
            Assert.IsTrue(a.Count != b.Count || a[0].anchor != b[0].anchor);
        }

        [TestCase(101), TestCase(202), TestCase(303)]
        public void BandAdultsAndSchools_WithinTwentyFivePercent(int seed)
        {
            var t = Tuning();
            var w = new AnalyticRiver { structureLength = 0f };
            const float length = 25600f; // school units are few (0.5 per 100 m): 128 per band keeps Poisson noise near 9%
            var adults = new float[3];
            var schools = new float[3];
            foreach (var g in Plan(t, w, seed, length))
            {
                if (t.species[g.species].schoolUnit) schools[g.band] += 1f;
                else adults[g.band] += g.members;
            }
            for (int b = 0; b < 3; b++)
            {
                Assert.That(adults[b] / (length / 100f), Is.InRange(t.bands[b].fishPer100m * 0.75f, t.bands[b].fishPer100m * 1.25f), t.bands[b].name + " adults");
                Assert.That(schools[b] / (length / 100f), Is.InRange(0.5f * 0.75f, 0.5f * 1.25f), t.bands[b].name + " schools");
            }
        }

        [Test]
        public void NoAnchor_ShallowerThanTheSpeciesMinimum_OrOnLand()
        {
            var t = Tuning();
            var w = new AnalyticRiver();
            foreach (var g in Plan(t, w, 101, 1600f))
            {
                float depth = w.WaterLevelY - AnalyticRiver.Bed(g.anchor.x);
                Assert.Greater(g.anchor.x, 0f);
                Assert.GreaterOrEqual(depth, t.species[g.species].minBedDepth, t.species[g.species].name);
            }
        }

        [Test]
        public void NoTile_MeansNoFish()
        {
            var t = Tuning();
            var w = new AnalyticRiver { noTileX = 10f };
            foreach (var g in Plan(t, w, 101, 1600f)) Assert.Less(g.anchor.x, 10f);
        }

        [Test]
        public void Structure_HoldsMoreFish_ItsOwnMix_AndItsExtras()
        {
            var t = Tuning();
            var w = new AnalyticRiver { structureLength = 1280f };
            const float length = 6400f, sLen = 1280f, oLen = length - sLen;
            float shelfIn = 0f, shelfOut = 0f, rampIn = 0f, rampOut = 0f, sunIn = 0f, sunOut = 0f, flathead = 0f, flatheadOut = 0f;
            foreach (var g in Plan(t, w, 101, length))
            {
                bool inS = g.structure >= 0;
                if (g.species == Flathead) { if (inS) flathead += g.members; else flatheadOut += g.members; }
                if (t.species[g.species].schoolUnit || g.species == Flathead) continue;
                if (g.band == 0) { if (inS) shelfIn += g.members; else shelfOut += g.members; }
                if (g.band != 1) continue;
                if (inS) rampIn += g.members; else rampOut += g.members;
                if (g.species == Sunfish) { if (inS) sunIn += g.members; else sunOut += g.members; }
            }
            Assert.That(shelfIn / sLen / (shelfOut / oLen), Is.InRange(1.5f, 2.5f), "shelf density x2 in structure");
            Assert.That(flathead / (sLen / 100f), Is.InRange(2.5f * 0.75f, 2.5f * 1.25f), "flathead per 100 m of anchor shore");
            Assert.AreEqual(0f, flatheadOut, "flathead only in structure");
            // Ramp mix: sunfish 2 vs catfish 6 outside (25%); with prefs x3 inside, 6 vs 6 (50%).
            Assert.That(sunOut / rampOut, Is.InRange(0.15f, 0.35f), "ramp sunfish share outside structure");
            Assert.That(sunIn / rampIn, Is.InRange(0.38f, 0.62f), "ramp sunfish share inside structure");
        }

        [Test]
        public void Validate_RejectsCapsAboveTheAdr_APopInRadius_AndMissingBodies()
        {
            var t = Tuning();
            Assert.IsNull(t.Validate());
            t.maxSchoolMembers = 250;
            StringAssert.Contains("TD ruling", t.Validate());
            t.maxSchoolMembers = 80;
            t.simRadiusIn = t.bodyDrawDistance + 1f;
            StringAssert.Contains("pop-in", t.Validate());
            t.simRadiusIn = 50f;
            var bodies = ScriptableObject.CreateInstance<FishBodies>();
            StringAssert.Contains("no measured body", t.Validate(bodies, 0.028f));
        }

        [Test]
        public void Validate_MinBedDepth_CoversClearanceMarginWavesAndTheTallestBody()
        {
            var t = Tuning();
            var bodies = ScriptableObject.CreateInstance<FishBodies>();
            var box = new Bounds(Vector3.zero, new Vector3(0.1f, 0.4f, 1f)); // 0.4 m tall at 1 m long
            bodies.bodies = new[] { new FishBody { variant = "a", noseToTail = 1f, clipBounds = box }, new FishBody { variant = "b", noseToTail = 1f, clipBounds = box } };
            // Sunfish: 0.08 + 0.05 + 0.028 + 0.4 x 0.24 = 0.254 <= 0.35: fine. Catfish at 0.8 m: 0.15 + 0.1 + 0.028 + 0.32 = 0.598 <= 1.
            Assert.IsNull(t.Validate(bodies, 0.028f));
            t.species[Sunfish].minBedDepth = 0.2f;
            StringAssert.Contains("minBedDepth", t.Validate(bodies, 0.028f));
        }
    }
}
