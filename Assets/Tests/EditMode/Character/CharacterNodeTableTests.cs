using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>
    /// Pod 49 item 5 (td-49 option b and Addendum 20): the Stand locomotion node speeds come from the appearance data, every rate over the controller's speed
    /// range stays inside 0.7 to 1.4, and no Stand-family state or blend node plays the pack Idle (the zero node is Stand_Idle).
    /// </summary>
    public class CharacterNodeTableTests
    {
        const float RateLow = 0.695f, RateHigh = 1.4f + 1e-3f;

        static CharacterAppearance Survivor()
        {
            var guids = AssetDatabase.FindAssets("t:CharacterAppearance Survivor");
            Assert.Greater(guids.Length, 0, "no Survivor appearance asset");
            return AssetDatabase.LoadAssetAtPath<CharacterAppearance>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        [Test]
        public void DefaultNodeSpeedsKeepTheOldBehaviourAndExplicitSpeedsChangeTheWalkAndRunNodes()
        {
            const float s = 1.03f;
            Assert.AreEqual(CharacterClipSet.LocomotionRate(0, 1.5f, s), CharacterClipSet.LocomotionRate(0, 1.5f, s, 0f, 1.337f, 3.035f), 1e-6f, "defaults are the Quaternius values");
            Assert.AreEqual(CharacterClipSet.LocomotionRate(0, 5f, s, 1f), CharacterClipSet.LocomotionRate(0, 5f, s, 1f, 1.337f, 3.035f), 1e-6f);
            // Walk alone: rate = speed / (clip speed x scale)
            Assert.AreEqual(1.0f / (1.204f * s), CharacterClipSet.LocomotionRate(0, 1.0f, s, 0f, 1.204f, 2.640f), 0.003f, "Walk at its own speed");
            Assert.AreEqual(1f, CharacterClipSet.LocomotionRate(0, 2.640f * s, s, 0f, 1.204f, 2.640f), 0.003f, "Run at its node plays at rate 1");
            Assert.AreEqual(1f, CharacterClipSet.LocomotionRate(0, 2.640f * s, s, 1f, 1.204f, 2.640f), 0.003f, "Run_* at its node plays at rate 1");
            // Jog 5 and Sprint 8 do not move with the Walk and Run constants
            Assert.AreEqual(CharacterClipSet.LocomotionRate(0, 5f, s), CharacterClipSet.LocomotionRate(0, 5f, s, 0f, 1.204f, 2.640f), 1e-5f);
            Assert.AreEqual(CharacterClipSet.LocomotionRate(0, 8f, s), CharacterClipSet.LocomotionRate(0, 8f, s, 0f, 1.204f, 2.640f), 1e-5f);
        }

        [Test]
        public void EveryRateOverTheSpeedRangeInTenthsStaysWithinTheBandForBothFamilies()
        {
            var a = Survivor();
            float s = a.skeletonScale;
            var sb = new StringBuilder();
            sb.AppendLine($"Stand rate table: skeletonScale {s:F4}, walk clip speed {a.walkClipSpeed:F4}, run clip speed {a.runClipSpeed:F4} (unscaled authored m/s), Jog node 5, Sprint node 8");
            sb.AppendLine("speed m/s | forward: rate, clips blended | strafe/back: rate, clips blended");
            var fwd = CharacterClipSet.StandNodes(s, true, a.walkClipSpeed, a.runClipSpeed);
            var side = CharacterClipSet.StandNodes(s, false, a.walkClipSpeed, a.runClipSpeed);
            string[] fwdNames = { "Stand_Idle", "Walk", "Walk", "Run", "Jog", "Sprint" };
            string[] sideNames = { "Stand_Idle", "Run_*", "Run_*", "Jog", "Sprint" };
            string Active(( float position, float clipSpeed)[] nodes, string[] names, float v)
            {
                for (int i = 1; i < nodes.Length; i++)
                    if (v <= nodes[i].position + 1e-4f) return names[i - 1] == names[i] ? names[i] : names[i - 1] + "+" + names[i];
                return names[names.Length - 1];
            }
            for (int i = 1; i <= 80; i++)
            {
                float v = i * 0.1f;
                float rf = CharacterClipSet.LocomotionRate(0, v, s, 0f, a.walkClipSpeed, a.runClipSpeed);
                float rs = CharacterClipSet.LocomotionRate(0, v, s, 1f, a.walkClipSpeed, a.runClipSpeed);
                sb.AppendLine($"{v,5:F1}     | {rf,5:F3} {Active(fwd, fwdNames, v),-12} | {rs,5:F3} {Active(side, sideNames, v)}");
                Assert.GreaterOrEqual(rf, RateLow, $"forward rate at {v:F1} m/s");
                Assert.LessOrEqual(rf, RateHigh, $"forward rate at {v:F1} m/s");
                Assert.GreaterOrEqual(rs, RateLow, $"strafe rate at {v:F1} m/s");
                Assert.LessOrEqual(rs, RateHigh, $"strafe rate at {v:F1} m/s");
            }
            Directory.CreateDirectory("TestResults/pod49");
            File.WriteAllText("TestResults/pod49/stand-rate-table.txt", sb.ToString());
            // the convention tests of the old suite still hold on the new nodes
            Assert.AreEqual(5f / (4.348f * s), CharacterClipSet.LocomotionRate(0, 5f, s, 0f, a.walkClipSpeed, a.runClipSpeed), 0.001f, "Jog at 5 m/s");
            Assert.AreEqual(8f / (5.80f), CharacterClipSet.LocomotionRate(0, 8f, s, 0f, a.walkClipSpeed, a.runClipSpeed), 0.001f, "Sprint at 8 m/s (designed on-body 5.80)");
        }

        [Test]
        public void NoStandFamilyStateOrBlendNodePlaysThePackIdle()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(BodyType.Men));
            Assert.IsNotNull(set);
            Assert.IsNotNull(set.Get(CharacterClipSet.Names.StandIdle), "the Men clip set has no Stand_Idle");
            var pack = set.Get(CharacterClipSet.Names.Idle);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(BodyType.Men));
            var checkedMotions = new List<string>();
            foreach (var st in controller.layers[0].stateMachine.states)
            {
                var motion = st.state.motion;
                if (st.state.name != CharacterAnimatorBuilder.StandState) continue;
                Assert.IsInstanceOf<BlendTree>(motion, "the Stand state is a blend tree");
                var tree = (BlendTree)motion;
                foreach (var child in tree.children)
                {
                    var clip = child.motion as AnimationClip;
                    Assert.IsNotNull(clip, "a blend node without a clip");
                    checkedMotions.Add(clip.name);
                    Assert.AreNotSame(pack, clip, "a Stand blend node plays the pack Idle at " + child.position);
                    Assert.AreNotEqual(CharacterClipSet.Names.Idle, clip.name.Split('|').Last(), "a Stand blend node is named Idle");
                    if (child.position == Vector2.zero) Assert.AreEqual(set.Get(CharacterClipSet.Names.StandIdle), clip, "the zero node is Stand_Idle");
                }
            }
            Assert.Greater(checkedMotions.Count, 15, "the Stand blend tree was read (forward and strafe/back families)");
            Assert.AreEqual(1, checkedMotions.Count(n => n.EndsWith("Stand_Idle")), "exactly one Stand_Idle node (the zero node serves both families)");
            // the shadow and range-end override controllers swap clips only by name: none may map the Stand zero node back to the pack Idle
            foreach (var path in new[] { CharacterAnimatorBuilder.ShadowControllerPath(BodyType.Men), CharacterAnimatorBuilder.RangeControllerPath(BodyType.Men, false), CharacterAnimatorBuilder.RangeControllerPath(BodyType.Men, true) })
            {
                var o = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                if (o == null) continue;
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                o.GetOverrides(pairs);
                foreach (var p in pairs)
                    if (p.Key != null && p.Key.name == CharacterClipSet.Names.StandIdle)
                        Assert.IsTrue(p.Value == null || p.Value.name != CharacterClipSet.Names.Idle, path + ": Stand_Idle overridden by the pack Idle");
            }
        }

        [Test]
        public void TheAppearanceCarriesTheBuilderNodeSpeeds()
        {
            var a = Survivor();
            Assert.AreEqual(CharacterAssetBuilder.WalkClipSpeedOf(BodyType.Men), a.walkClipSpeed, 1e-4f);
            Assert.AreEqual(CharacterAssetBuilder.RunClipSpeedOf(BodyType.Men), a.runClipSpeed, 1e-4f);
            Assert.AreNotEqual(CharacterClipSet.DefaultWalkClipSpeed, a.walkClipSpeed, "the Survivor's Walk is slower than the Quaternius clip");
        }
    }
}
