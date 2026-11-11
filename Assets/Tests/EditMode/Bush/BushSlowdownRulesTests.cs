using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// Bush slowdown maths and data (#21, docs/design/bush-slowdown.md): B1.3 to B1.6, B2.1, the section 4 rules and the match with the editor's
    /// DetailSpecs. The multipliers are read from the asset defaults, so a retune inside the section 4 rules does not need new numbers here.
    /// </summary>
    public class BushSlowdownRulesTests
    {
        const int Briar = 1, Thicket = 2, Mound = 3, Leaf = 4;
        const float Walk = 5f, Sprint = 8f;

        static BushSlowdownTuning Tuning() => ScriptableObject.CreateInstance<BushSlowdownTuning>();

        static float Speed(BushSlowdownTuning t, int layer, bool sprint, float cover)
        {
            var l = t.layers[layer];
            return (sprint ? Sprint : Walk) * BushSlowdownRules.LayerMultiplier(sprint ? l.sprintMultiplier : l.walkMultiplier, cover);
        }

        [Test]
        public void DefaultsPassTheSection4Rules() => Assert.IsNull(Tuning().Validate(Walk, Sprint));

        [Test]
        public void B1_FullCoverSpeedsMatchTheTable()
        {
            var t = Tuning();
            float[] walk = { 5f, 3.5f, 2.75f, 2.5f, 4.5f, 5f, 5f, 5f };
            float[] sprint = { 8f, 4f, 3.2f, 2.8f, 6.4f, 8f, 8f, 8f };
            for (int i = 0; i < 8; i++)
            {
                float w = t.layers[i].slows ? Speed(t, i, false, 1f) : Walk;
                float s = t.layers[i].slows ? Speed(t, i, true, 1f) : Sprint;
                Assert.AreEqual(walk[i], w, 0.05f, $"walk {t.layers[i].name}");
                Assert.AreEqual(sprint[i], s, 0.05f, $"sprint {t.layers[i].name}");
            }
        }

        [Test]
        public void B1_3_TallerCoverSlowsMore_StepsAtLeast02()
        {
            var t = Tuning();
            int[] order = { Leaf, Briar, Thicket, Mound };
            for (int i = 0; i + 1 < order.Length; i++)
            {
                Assert.Greater(Speed(t, order[i], false, 1f) - Speed(t, order[i + 1], false, 1f), 0.2f - 1e-4f, $"walk step {i}");
                Assert.Greater(Speed(t, order[i], true, 1f) - Speed(t, order[i + 1], true, 1f), 0.2f - 1e-4f, $"sprint step {i}");
            }
        }

        [Test]
        public void B1_4_DenserCoverSlowsMore()
        {
            var t = Tuning();
            foreach (int layer in new[] { Briar, Thicket, Mound })
                foreach (bool sprint in new[] { false, true })
                {
                    float prev = 1f;
                    foreach (float c in new[] { 0.25f, 0.5f, 1f })
                    {
                        var l = t.layers[layer];
                        float M = sprint ? l.sprintMultiplier : l.walkMultiplier;
                        float m = BushSlowdownRules.LayerMultiplier(M, c);
                        Assert.AreEqual(1f - (1f - M) * c, m, 0.001f);
                        Assert.Less(m, prev, $"{l.name} sprint={sprint} c={c}");
                        prev = m;
                    }
                }
        }

        [Test]
        public void B1_5_WorkedExamples()
        {
            var t = Tuning();
            // A: one briar cell, the player 1.5 m from its centre (fx 0.75 away from it, fy 0): c = 0.25.
            float c = BushSlowdownRules.Cover(1f, 0f, 1f, 0f, 0.75f, 0f);   // the briar cell is x0; fx = 0.75 leaves 0.25 of it
            Assert.AreEqual(0.25f, c, 1e-6f);
            Assert.AreEqual(7.00f, Sprint * BushSlowdownRules.LayerMultiplier(t.layers[Briar].sprintMultiplier, c), 0.001f);
            Assert.AreEqual(4.625f, Walk * BushSlowdownRules.LayerMultiplier(t.layers[Briar].walkMultiplier, c), 0.001f);
            // B: kudzu mound cover 0.6; kudzu leaf counts 3, 2, 2, 3 are all full (full count 2).
            float leaf = BushSlowdownRules.Cover(
                BushSlowdownRules.Fill(3, 2), BushSlowdownRules.Fill(2, 2), BushSlowdownRules.Fill(2, 2), BushSlowdownRules.Fill(3, 2), 0.3f, 0.7f);
            Assert.AreEqual(1f, leaf, 1e-6f);
            float walk = BushSlowdownRules.Combine(BushSlowdownRules.Combine(1f, BushSlowdownRules.LayerMultiplier(t.layers[Mound].walkMultiplier, 0.6f)),
                BushSlowdownRules.LayerMultiplier(t.layers[Leaf].walkMultiplier, leaf));
            float sprint = BushSlowdownRules.Combine(BushSlowdownRules.Combine(1f, BushSlowdownRules.LayerMultiplier(t.layers[Mound].sprintMultiplier, 0.6f)),
                BushSlowdownRules.LayerMultiplier(t.layers[Leaf].sprintMultiplier, leaf));
            Assert.AreEqual(3.50f, Walk * walk, 0.001f);
            Assert.AreEqual(4.88f, Sprint * sprint, 0.001f);
        }

        [Test]
        public void B1_6_OverlappingLayersTakeTheMinimum()
        {
            var t = Tuning();
            float walk = BushSlowdownRules.Combine(BushSlowdownRules.LayerMultiplier(t.layers[Mound].walkMultiplier, 1f), BushSlowdownRules.LayerMultiplier(t.layers[Leaf].walkMultiplier, 1f));
            float sprint = BushSlowdownRules.Combine(BushSlowdownRules.LayerMultiplier(t.layers[Mound].sprintMultiplier, 1f), BushSlowdownRules.LayerMultiplier(t.layers[Leaf].sprintMultiplier, 1f));
            Assert.AreEqual(2.50f, Walk * walk, 0.001f);
            Assert.AreEqual(2.80f, Sprint * sprint, 0.001f);
        }

        [Test]
        public void B2_1_NoCoverIsExactlyOne()
        {
            var t = Tuning();
            for (int i = 0; i < 8; i++)
            {
                float c = BushSlowdownRules.Cover(0f, 0f, 0f, 0f, 0.37f, 0.81f);
                Assert.IsTrue(BushSlowdownRules.LayerMultiplier(t.layers[i].walkMultiplier, c) == 1f, $"walk {i}");
                Assert.IsTrue(BushSlowdownRules.LayerMultiplier(t.layers[i].sprintMultiplier, c) == 1f, $"sprint {i}");
                Assert.IsTrue(BushSlowdownRules.Combine(1f, 1f) == 1f);
            }
        }

        [Test]
        public void FillCountsToTheFullCount()
        {
            Assert.AreEqual(0f, BushSlowdownRules.Fill(0, 2));
            Assert.AreEqual(0.5f, BushSlowdownRules.Fill(1, 2));
            Assert.AreEqual(1f, BushSlowdownRules.Fill(2, 2));
            Assert.AreEqual(1f, BushSlowdownRules.Fill(3, 2));
            Assert.AreEqual(1f, BushSlowdownRules.Fill(1, 1));
        }

        [Test]
        public void ValidateCatchesBrokenRules()
        {
            var t = Tuning();
            t.layers[Briar].sprintMultiplier = t.layers[Briar].walkMultiplier;
            Assert.IsNotNull(t.Validate(Walk, Sprint), "sprint must be cut harder than walk");
            t = Tuning();
            t.layers[Briar].sprintMultiplier = 0.3f;   // 8 x 0.3 = 2.4 < 3.5: sprint slower than walk
            Assert.IsNotNull(t.Validate(Walk, Sprint), "sprint never slower than walk");
            t = Tuning();
            t.layers[Mound].walkMultiplier = 0.9f;     // the tallest layer slowing less than a shorter one
            Assert.IsNotNull(t.Validate(Walk, Sprint), "taller layers slow more");
            t = Tuning();
            t.layers[Mound].walkMultiplier = 0.45f;    // 5 x 0.45 = 2.25: under the 2.5 m/s floor (sprint stays below walk)
            t.layers[Mound].sprintMultiplier = 0.38f;
            Assert.IsNotNull(t.Validate(Walk, Sprint), "no full-cover walk speed under 2.5 m/s");
            t = Tuning();
            t.layers[0].walkMultiplier = 0.9f;         // grass must not slow
            Assert.IsNotNull(t.Validate(Walk, Sprint), "non-shrub layers do not slow");
        }

        [Test]
        public void TheShippedTuningAssetPassesTheRules()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<BushSlowdownTuning>("Assets/Settings/BushSlowdownTuning.asset");
            Assert.IsNotNull(asset, "Assets/Settings/BushSlowdownTuning.asset is missing");
            Assert.IsNull(asset.Validate(Walk, Sprint));
        }

        /// <summary>The runtime assembly cannot see the editor's DetailSpecs, so the tuning order, shrub flags and max heights are checked against its source.</summary>
        [Test]
        public void LayerOrderShrubFlagsAndHeightsMatchBellsBendDetailSpecs()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Editor", "Art", "BellsBendVegetation.cs"));
            Assert.IsTrue(File.Exists(path), path);
            string src = File.ReadAllText(path);
            var rx = new Regex("\\(\"(?<key>\\w+)\",\\s*\"[^\"]+\",\\s*(?<min>[\\d.]+)f,\\s*(?<max>[\\d.]+)f,[^\\n]*?,\\s*(?<shrub>true|false)\\)\\s*,");
            var ms = rx.Matches(src);
            Assert.AreEqual(BushSlowdownTuning.LayerCount, ms.Count, "DetailSpecs rows found in BellsBendVegetation.cs");
            var t = Tuning();
            for (int i = 0; i < ms.Count; i++)
            {
                Assert.AreEqual(ms[i].Groups["key"].Value, t.layers[i].name, $"layer {i} name");
                Assert.AreEqual(ms[i].Groups["shrub"].Value == "true", t.layers[i].slows, $"layer {i} shrub flag");
                Assert.AreEqual(float.Parse(ms[i].Groups["max"].Value, System.Globalization.CultureInfo.InvariantCulture), t.layers[i].maxHeight, 1e-4f, $"layer {i} max height");
            }
        }
    }
}
