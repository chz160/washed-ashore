using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>Spec W6 (water-swim-spec.md §2-§3): the pure wade/swim rules and the SwimTuning asset.</summary>
    public class SwimRulesTests
    {
        const string TuningPath = "Assets/World/Water/SwimTuning.asset";
        const float Walk = 5f, Sprint = 8f, Eye = 1.65f; // live controller (World.unity); PlayMode re-reads them

        SwimTuning t;

        [SetUp] public void SetUp() => t = ScriptableObject.CreateInstance<SwimTuning>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(t);

        [Test]
        public void Classify_DryWadeSwim_ByDepth()
        {
            Assert.AreEqual(WaterMode.Dry, SwimRules.Classify(WaterMode.Wade, t.wadeMinDepth, t));
            Assert.AreEqual(WaterMode.Wade, SwimRules.Classify(WaterMode.Dry, t.wadeMinDepth + 0.01f, t));
            Assert.AreEqual(WaterMode.Wade, SwimRules.Classify(WaterMode.Wade, t.swimEnterDepth - 0.01f, t));
            Assert.AreEqual(WaterMode.Swim, SwimRules.Classify(WaterMode.Wade, t.swimEnterDepth, t));
            Assert.AreEqual(WaterMode.Swim, SwimRules.Classify(WaterMode.Dry, 5f, t), "teleport into deep water swims at once");
        }

        [Test]
        public void Classify_Hysteresis_OneTransitionEachWay()
        {
            float between = (t.swimEnterDepth + t.swimExitDepth) * 0.5f;
            Assert.AreEqual(WaterMode.Swim, SwimRules.Classify(WaterMode.Swim, between, t));
            Assert.AreEqual(WaterMode.Wade, SwimRules.Classify(WaterMode.Wade, between, t));
            Assert.AreEqual(WaterMode.Wade, SwimRules.Classify(WaterMode.Swim, t.swimExitDepth, t));

            // Walk out along the shelf and back in 1 cm steps: exactly one flip each way.
            var mode = WaterMode.Dry;
            int toSwim = 0, toWade = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i <= 200; i++)
                {
                    float depth = pass == 0 ? i * 0.01f : 2f - i * 0.01f;
                    var next = SwimRules.Classify(mode, depth, t);
                    if (mode == WaterMode.Wade && next == WaterMode.Swim) toSwim++;
                    if (mode == WaterMode.Swim && next == WaterMode.Wade) toWade++;
                    mode = next;
                }
            Assert.AreEqual(1, toSwim);
            Assert.AreEqual(1, toWade);
        }

        [Test]
        public void TargetSpeed_MatchesSpecBars()
        {
            Assert.AreEqual(Walk, SwimRules.TargetSpeed(WaterMode.Dry, 0f, false, Walk, Sprint, t), 1e-4f);
            Assert.AreEqual(3.75f, SwimRules.TargetSpeed(WaterMode.Wade, 0.3f, false, Walk, Sprint, t), 0.01f, "depth 0.3");
            Assert.AreEqual(2.5f, SwimRules.TargetSpeed(WaterMode.Wade, 1.0f, false, Walk, Sprint, t), 0.01f, "depth 1.0");
            Assert.AreEqual(Walk * 0.4f, SwimRules.TargetSpeed(WaterMode.Wade, 1.35f, false, Walk, Sprint, t), 0.01f, "chest depth");
            Assert.AreEqual(1.8f, SwimRules.TargetSpeed(WaterMode.Swim, 3f, false, Walk, Sprint, t), 1e-4f);
            Assert.AreEqual(2.4f, SwimRules.TargetSpeed(WaterMode.Swim, 3f, true, Walk, Sprint, t), 1e-4f);
            Assert.AreEqual(Sprint * t.wadeSpeedByDepth.Evaluate(0.2f), SwimRules.TargetSpeed(WaterMode.Wade, 0.2f, true, Walk, Sprint, t), 1e-4f, "sprint shallow");
            Assert.AreEqual(Walk * t.wadeSpeedByDepth.Evaluate(0.5f), SwimRules.TargetSpeed(WaterMode.Wade, 0.5f, true, Walk, Sprint, t), 1e-4f, "sprint ignored deeper");
        }

        [Test]
        public void WadeCurve_IsLinearBetweenKeys_AndFalls()
        {
            Assert.AreEqual(0.825f, t.wadeSpeedByDepth.Evaluate(0.175f), 1e-3f, "midpoint of (0.05,0.9)-(0.3,0.75)");
            float prev = float.MaxValue;
            for (float d = 0.05f; d <= 1.35f; d += 0.01f)
            {
                float v = t.wadeSpeedByDepth.Evaluate(d);
                Assert.LessOrEqual(v, prev + 1e-5f, $"curve rises at {d}");
                prev = v;
            }
        }

        [Test]
        public void StrokeSurge_AveragesOneOverWholeStrokes()
        {
            float period = 1f / t.strokeSurgeHz, sum = 0f;
            int n = 600;
            for (int i = 0; i < n; i++) sum += SwimRules.StrokeSurge(i * period / n, t);
            Assert.AreEqual(1f, sum / n, 1e-3f);
        }

        [Test]
        public void JumpAndRates_FollowSpec()
        {
            Assert.IsTrue(SwimRules.CanJump(WaterMode.Dry, 0f, t));
            Assert.IsTrue(SwimRules.CanJump(WaterMode.Wade, 0.5f, t));
            Assert.IsFalse(SwimRules.CanJump(WaterMode.Wade, 0.7f, t));
            Assert.IsFalse(SwimRules.CanJump(WaterMode.Swim, 3f, t));
            Assert.AreEqual(t.swimOverspeedDeceleration, SwimRules.Rate(WaterMode.Swim, true, true, 20f, 25f, t));
            Assert.AreEqual(t.swimAcceleration, SwimRules.Rate(WaterMode.Swim, true, false, 20f, 25f, t));
            Assert.AreEqual(t.swimDeceleration, SwimRules.Rate(WaterMode.Swim, false, false, 20f, 25f, t));
            Assert.AreEqual(t.waterAcceleration, SwimRules.Rate(WaterMode.Wade, true, false, 20f, 25f, t));
            Assert.AreEqual(t.waterDeceleration, SwimRules.Rate(WaterMode.Wade, true, true, 20f, 25f, t));
        }

        [Test]
        public void Float_FollowsSurface_NeverBelowGround_Capped()
        {
            Assert.AreEqual(10f - t.floatDepth, SwimRules.FloatFeetY(10f, 0f, 0.05f, t), 1e-5f);
            Assert.AreEqual(9.5f, SwimRules.FloatFeetY(10f, 9.45f, 0.05f, t), 1e-5f, "shelf: ground + skin wins");
            Assert.AreEqual(t.surfaceFollowRate, SwimRules.FollowSpeed(0f, 100f, 1f / 60f, t), 1e-5f);
            Assert.AreEqual(0.6f, SwimRules.FollowSpeed(0f, 0.01f, 1f / 60f, t), 1e-4f, "small offsets are reached in one frame");
        }

        [Test]
        public void Validate_DefaultsPass_BadNumbersFail()
        {
            Assert.IsNull(t.Validate(Walk, Eye));
            t.swimExitDepth = t.swimEnterDepth;
            Assert.IsNotNull(t.Validate(Walk, Eye), "exit >= enter");
            t.swimExitDepth = 1.15f;
            Assert.IsNotNull(t.Validate(Walk, 1.4f), "camera would go under");
            t.swimSpeed = 2.2f;
            t.wadeSpeedByDepth = SwimTuning.Linear(new Vector2(0.05f, 0.9f), new Vector2(1.35f, 0.35f));
            Assert.IsNotNull(t.Validate(Walk, Eye), "director note b: chest wade 1.75 < swim 2.2");
        }

        /// <summary>w-designer: the shipped World.unity PlayerController links the SwimTuning asset, so a missing link can't
        /// silently pass W6. Read from the scene file (opening it would replace the Editor's open scene).</summary>
        [Test]
        public void WorldScene_PlayerReferencesSwimTuning()
        {
#if UNITY_EDITOR
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(TuningPath);
            Assert.IsNotEmpty(guid, $"No SwimTuning at {TuningPath}");
            string scene = System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "Scenes/World.unity"));
            var refs = System.Text.RegularExpressions.Regex.Matches(scene, @"swimTuning: \{fileID: (\d+)(?:, guid: (\w+))?");
            Assert.AreEqual(1, refs.Count, "expected exactly one PlayerController.swimTuning field in World.unity");
            Assert.AreEqual(guid, refs[0].Groups[2].Value, "World.unity PlayerController.swimTuning is not the SwimTuning asset");
#endif
        }

        [Test]
        public void ProjectAsset_PassesValidation_AndApprovedBand()
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<SwimTuning>(TuningPath);
            Assert.IsNotNull(asset, $"No SwimTuning at {TuningPath}");
            Assert.IsNull(asset.Validate(Walk, Eye));
            // water-swim-spec.md §8 band, ruling/water-swim-numbers.
            Assert.That(asset.swimSpeed, Is.InRange(1.5f, 2.2f), "swimSpeed band");
            Assert.LessOrEqual(asset.swimSprintSpeed, 2.8f, "sprint-stroke cap");
            Assert.Less(asset.swimSprintSpeed, Walk * 0.5f, "sprint-stroke under half of walk");
            Assert.That(asset.wadeSpeedByDepth.Evaluate(asset.swimEnterDepth), Is.InRange(0.35f, 0.5f), "chest-depth multiplier band");
            Assert.AreEqual(1.35f, asset.swimEnterDepth, 1e-4f, "approved swimEnterDepth");
            Assert.AreEqual(1.15f, asset.swimExitDepth, 1e-4f, "approved swimExitDepth");
#endif
        }
    }
}
