using NUnit.Framework;
using UnityEngine;
using WashedAshore.Interaction;

namespace WashedAshore.Tests.EditMode.Lookout
{
    /// <summary>Spec I1 (tower-design-spec.md §3, T-I1b): the pure interaction rules and the InteractionSettings defaults.</summary>
    public class InteractionRulesTests
    {
        const string EnDash = "–";

        InteractionSettings t;

        [SetUp] public void SetUp() => t = ScriptableObject.CreateInstance<InteractionSettings>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(t);

        [Test]
        public void Tuning_DefaultsAreValid_AndRangeIsCapped()
        {
            Assert.IsNull(t.Validate(), t.Validate());
            Assert.LessOrEqual(t.interactRange, InteractionSettings.RangeCap);
            t.interactRange = InteractionSettings.RangeCap + 0.1f;
            Assert.IsNotNull(t.Validate(), "a range above the cap must be rejected");
            Assert.AreEqual(InteractionSettings.RangeCap, InteractionRules.Range(t.interactRange, InteractionSettings.RangeCap), 1e-6f,
                "the ray never uses more than the cap, whatever the asset says");
        }

        [TestCase("E", "Open hatch")]
        [TestCase("E", "Close hatch")]
        [TestCase("E", "Open window")]
        [TestCase("E", "Close window")]
        [TestCase("E", "Use firefinder")]
        [TestCase("E", "Leave firefinder")]
        [TestCase("A", "Open hatch")]
        [TestCase("Cross", "Open hatch")]
        [TestCase("South", "Open hatch")]
        public void PromptLine_IsKeyEnDashVerbNoun(string key, string text)
        {
            Assert.AreEqual($"{key} {EnDash} {text}", InteractionRules.PromptLine(key, t.separator, text));
        }

        [Test]
        public void VerbNoun_IsVerbSpaceNoun()
        {
            Assert.AreEqual("Open hatch", InteractionRules.VerbNoun("Open", "hatch"));
            Assert.AreEqual("Open", InteractionRules.VerbNoun("Open", null));
            Assert.IsNull(InteractionRules.VerbNoun(null, "hatch"), "no verb means no prompt");
            Assert.AreEqual($"E {EnDash} Open hatch", InteractionRules.PromptLine("E", t.separator, InteractionRules.VerbNoun("Open", "hatch")));
        }

        [Test]
        public void PromptLine_NothingToSay_IsNull()
        {
            Assert.IsNull(InteractionRules.PromptLine("E", t.separator, null));
            Assert.IsNull(InteractionRules.PromptLine("E", t.separator, ""));
        }

        [Test]
        public void KeyLabel_NamedDevicesKeepTheirName_UnnamedPadIsSouth()
        {
            Assert.AreEqual("E", InteractionRules.KeyLabel("E", t.unnamedSouthDisplay, t.genericPadKey));
            Assert.AreEqual("A", InteractionRules.KeyLabel("A", t.unnamedSouthDisplay, t.genericPadKey));
            Assert.AreEqual("Cross", InteractionRules.KeyLabel("Cross", t.unnamedSouthDisplay, t.genericPadKey));
            Assert.AreEqual(t.genericPadKey, InteractionRules.KeyLabel(t.unnamedSouthDisplay, t.unnamedSouthDisplay, t.genericPadKey));
            Assert.AreEqual(t.genericPadKey, InteractionRules.KeyLabel(null, t.unnamedSouthDisplay, t.genericPadKey));
        }

        [Test]
        public void JumpBlocked_OnlyForAGamepadPressThatInteracted()
        {
            Assert.IsTrue(InteractionRules.JumpBlocked(jumpPressFromGamepad: true, interactTookPressThisFrame: true));
            Assert.IsFalse(InteractionRules.JumpBlocked(true, false), "South with no prompt jumps as today");
            Assert.IsFalse(InteractionRules.JumpBlocked(false, true), "Space always jumps");
            Assert.IsFalse(InteractionRules.JumpBlocked(false, false));
        }

        [Test]
        public void FontSize_GivesTheTunedCapHeight()
        {
            foreach (int height in new[] { 720, 1080, 1440, 2160 })
            {
                int size = InteractionRules.FontSize(height, t.promptCapFraction, t.promptCapToEm);
                float cap = size * t.promptCapToEm;
                Assert.AreEqual(height * t.promptCapFraction, cap, t.promptCapToEm, $"cap height at {height}p (within one font pixel)");
            }
        }
    }
}
