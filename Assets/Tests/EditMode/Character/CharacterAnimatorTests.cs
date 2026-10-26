using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>Spec H3 (static part): the controller reaches every stance clip from its default state, and the rate helper meets the designer's limits.</summary>
    public class CharacterAnimatorTests
    {
        static readonly BodyType[] Types = { BodyType.Men, BodyType.Women };

        static IEnumerable<AnimationClip> Clips(Motion m)
        {
            if (m is AnimationClip c) yield return c;
            else if (m is BlendTree t)
                foreach (var child in t.children)
                    foreach (var x in Clips(child.motion)) yield return x;
        }

        [TestCaseSource(nameof(Types))]
        public void EveryStateIsReachableAndEveryRequiredClipIsUsed(BodyType type)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(type));
            Assert.IsNotNull(controller);
            var sm = controller.layers[0].stateMachine;
            var seen = new HashSet<AnimatorState> { sm.defaultState };
            var queue = new Queue<AnimatorState>(seen);
            while (queue.Count > 0)
                foreach (var t in queue.Dequeue().transitions)
                    if (t.destinationState && seen.Add(t.destinationState)) queue.Enqueue(t.destinationState);
            foreach (var s in sm.states.Select(x => x.state))
                Assert.IsTrue(seen.Contains(s), $"{type}: state {s.name} is unreachable");
            var used = new HashSet<string>(sm.states.SelectMany(s => Clips(s.state.motion)).Select(c => c.name));
            foreach (var name in CharacterClipSet.Names.Required)
                Assert.IsTrue(used.Contains(name), $"{type}: clip {name} is in no state");
            // every motion comes from this body type's own clip set
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            foreach (var s in sm.states)
                foreach (var c in Clips(s.state.motion)) Assert.AreSame(set.Get(c.name), c, $"{type}/{s.state.name}: {c.name}");
        }

        [TestCaseSource(nameof(Types))]
        public void FullSwingJogAndSprintClipsAreBuiltButHeldOutOfTheController(BodyType type)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(type));
            var used = new HashSet<string>(controller.layers[0].stateMachine.states.SelectMany(x => Clips(x.state.motion)).Select(c => c.name));
            foreach (var family in new[] { "Jog", "Sprint" })
                foreach (var dir in new[] { "", "Back", "Left", "Right" })
                {
                    var full = set.Get(family + dir + "_Full");
                    Assert.IsNotNull(full, $"{type}: {family}{dir}_Full missing");
                    Assert.IsTrue(full.isLooping);
                    Assert.IsFalse(used.Contains(full.name), $"{type}: {full.name} is in the controller");
                    Assert.AreEqual(set.Get(family + dir).length, full.length, 1e-3f, "same cycle length as the capped clip");
                }
        }

        [TestCaseSource(nameof(Types))]
        public void ShadowOverrideMapsExactlyTheEightCappedClipsToTheirFullSwingVersions(BodyType type)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            var o = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CharacterAnimatorBuilder.ShadowControllerPath(type));
            Assert.IsNotNull(o);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(type)), o.runtimeAnimatorController);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            o.GetOverrides(pairs);
            var mapped = pairs.Where(p => p.Value != null).ToList();
            Assert.AreEqual(8, mapped.Count, "exactly 8 clips are overridden");
            foreach (var name in CharacterAnimatorBuilder.ShadowSwapped)
            {
                var pair = mapped.Single(p => p.Key.name == name);
                Assert.AreSame(set.Get(name), pair.Key);
                Assert.AreSame(set.Get(name + CharacterClipSet.Names.FullSuffix), pair.Value, name);
            }
            var appearance = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(CharacterAssetBuilder.AppearancePath(type, "Worker"));
            Assert.AreSame(o, appearance.shadowController);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(type)), appearance.controller);
        }

        static List<KeyValuePair<AnimationClip, AnimationClip>> Mapped(AnimatorOverrideController o)
        {
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            o.GetOverrides(pairs);
            return pairs.Where(p => p.Value != null).ToList();
        }

        [TestCaseSource(nameof(Types))]
        public void EveryControllerTheShadowCanReceiveMapsAllEightToFullAndEveryControllerTheViewCanReceiveMapsNone(BodyType type)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            var shadowControllers = new List<AnimatorOverrideController>
            {
                AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CharacterAnimatorBuilder.ShadowControllerPath(type)),
                CharacterAnimatorBuilder.RangeControllerFor(type, false, true),
                CharacterAnimatorBuilder.RangeControllerFor(type, true, true),
            };
            foreach (var o in shadowControllers)
            {
                Assert.IsNotNull(o, $"{type}: a shadow controller is missing");
                var mapped = Mapped(o);
                foreach (var name in CharacterAnimatorBuilder.ShadowSwapped)
                    Assert.AreSame(set.Get(name + CharacterClipSet.Names.FullSuffix), mapped.Single(p => p.Key.name == name).Value, $"{type}/{o.name}: {name}");
                Assert.AreEqual(8, mapped.Count(p => p.Value.name.EndsWith(CharacterClipSet.Names.FullSuffix)), $"{o.name}: exactly 8 _Full");
            }
            var viewControllers = new List<AnimatorOverrideController>
            {
                CharacterAnimatorBuilder.RangeControllerFor(type, false, false),
                CharacterAnimatorBuilder.RangeControllerFor(type, true, false),
            };
            foreach (var o in viewControllers)
            {
                Assert.IsNotNull(o, $"{type}: a view controller is missing");
                Assert.AreEqual(0, Mapped(o).Count(p => p.Value.name.EndsWith(CharacterClipSet.Names.FullSuffix)), $"{o.name}: the view must map no _Full clip");
            }
            // runtime access: every appearance carries the four range controllers, so a dev build can retune without the Editor API
            foreach (var outfit in CharacterAssetBuilder.Outfits[type])
            {
                var appearance = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(CharacterAssetBuilder.AppearancePath(type, outfit.folder));
                Assert.AreSame(shadowControllers[1], appearance.RangeController(false, true), outfit.folder);
                Assert.AreSame(shadowControllers[2], appearance.RangeController(true, true), outfit.folder);
                Assert.AreSame(viewControllers[0], appearance.RangeController(false, false), outfit.folder);
                Assert.AreSame(viewControllers[1], appearance.RangeController(true, false), outfit.folder);
            }
            // the shadow range variants also carry their range clips
            Assert.IsTrue(Mapped(shadowControllers[1]).Any(p => p.Key.name == "Crouch_Idle" && p.Value.name == "Crouch_Idle_Low"));
            Assert.IsTrue(Mapped(shadowControllers[2]).Any(p => p.Key.name == "Sleep_Loop" && p.Value.name == "Sleep_Loop_High"));
            // and the base controller itself (the view's default) references no _Full clip
            var baseController = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(type));
            Assert.IsFalse(baseController.layers[0].stateMachine.states.SelectMany(x => Clips(x.state.motion)).Any(c => c.name.EndsWith(CharacterClipSet.Names.FullSuffix)));
        }

        [TestCaseSource(nameof(Types))]
        public void RangeControllersAlsoSwapTheEnterAndExitClips(BodyType type)
        {
            foreach (bool forShadow in new[] { false, true })
                foreach (bool high in new[] { false, true })
                {
                    string suffix = high ? "_High" : "_Low";
                    var mapped = Mapped(CharacterAnimatorBuilder.RangeControllerFor(type, high, forShadow));
                    var names = new List<string> { "Squat_Enter", "Squat_Exit", "Sit_Enter", "Sit_Exit" };
                    if (high) names.AddRange(new[] { "Sleep_Down", "Sleep_Wake" });
                    foreach (var n in names)
                        Assert.AreEqual(n + suffix, mapped.Single(p => p.Key.name == n).Value.name, $"{type} {(forShadow ? "shadow" : "view")} {suffix} {n}");
                    if (!high) Assert.IsFalse(mapped.Any(p => p.Key.name.StartsWith("Sleep")), "Sleep Low is the record pose: no sleep override");
                }
        }

        [TestCaseSource(nameof(Types))]
        public void FullSwingClipsAreReferencedOnlyByTheShadowOverride(BodyType type)
        {
            var baseController = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(type));
            var inBase = baseController.layers[0].stateMachine.states.SelectMany(x => Clips(x.state.motion)).Select(c => c.name);
            Assert.IsFalse(inBase.Any(n => n.EndsWith(CharacterClipSet.Names.FullSuffix)), "the base controller must not reference a _Full clip");
            foreach (bool high in new[] { false, true })
            {
                var range = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CharacterAnimatorBuilder.RangeControllerPath(type, high));
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                range.GetOverrides(pairs);
                Assert.IsFalse(pairs.Any(p => p.Value != null && p.Value.name.EndsWith(CharacterClipSet.Names.FullSuffix)), "range-end controllers must not reference _Full");
            }
        }

        [TestCaseSource(nameof(Types))]
        public void FullAndCappedClipsDifferOnlyInTheArmChains(BodyType type)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            foreach (var name in CharacterAnimatorBuilder.ShadowSwapped)
            {
                var capped = set.Get(name);
                var full = set.Get(name + CharacterClipSet.Names.FullSuffix);
                Assert.AreEqual(capped.length, full.length, 1e-4f, name);
                int armCurvesDiffering = 0, otherCompared = 0;
                foreach (var b in AnimationUtility.GetCurveBindings(capped))
                {
                    var c = AnimationUtility.GetEditorCurve(capped, b);
                    var f = AnimationUtility.GetEditorCurve(full, b);
                    Assert.IsNotNull(f, $"{name}: {b.path}:{b.propertyName} missing in the full clip");
                    bool arm = b.path.Contains("Shoulder.L") || b.path.Contains("Shoulder.R");
                    float worst = 0f;
                    for (int i = 0; i <= 32; i++)
                    {
                        float t = capped.length * i / 32f;
                        worst = Mathf.Max(worst, Mathf.Abs(c.Evaluate(t) - f.Evaluate(t)));
                    }
                    if (arm) { if (worst > 1e-4f) armCurvesDiffering++; }
                    else { otherCompared++; Assert.Less(worst, 1e-4f, $"{name}: non-arm curve {b.path}:{b.propertyName} differs by {worst}"); }
                }
                Assert.Greater(armCurvesDiffering, 0, $"{name}: the arm curves must differ (capped vs full)");
                Assert.Greater(otherCompared, 100, $"{name}: legs, spine and root compared");
            }
        }

        [TestCaseSource(nameof(Types))]
        public void RangeEndControllersSwapTheHoldClipsForTheVariants(BodyType type)
        {
            foreach (bool high in new[] { false, true })
            {
                var o = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(CharacterAnimatorBuilder.RangeControllerPath(type, high));
                Assert.IsNotNull(o, $"{type} range {(high ? "high" : "low")}");
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                o.GetOverrides(overrides);
                string suffix = high ? "_High" : "_Low";
                foreach (var n in new[] { "Crouch_Idle", "CrouchWalk", "Squat_Hold", "Sit_Loop" })
                {
                    var pair = overrides.First(p => p.Key.name == n);
                    Assert.IsNotNull(pair.Value, n);
                    Assert.AreEqual(n + suffix, pair.Value.name);
                }
                Assert.AreEqual(high ? "Sleep_Loop_High" : "Sleep_Loop", overrides.First(p => p.Key.name == "Sleep_Loop").Value?.name ?? "Sleep_Loop");
            }
        }

        [TestCase(BodyType.Men, 1.030f)]
        [TestCase(BodyType.Women, 1.0f)]
        public void LocomotionRateAndClipSpeedUseOneConvention(BodyType type, float scale)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(type));
            Assert.AreEqual(scale, CharacterAssetBuilder.SkeletonScaleOf(type), 1e-4f);
            Assert.AreEqual(5f / set.Speed("Jog"), CharacterClipSet.LocomotionRate(0, 5f, scale), 0.001f, "Jog at 5 m/s");
            Assert.AreEqual(8f / set.Speed("Sprint"), CharacterClipSet.LocomotionRate(0, 8f, scale), 0.001f, "Sprint at 8 m/s");
            Assert.AreEqual(1.5f / set.Speed("Walk"), CharacterClipSet.LocomotionRate(0, 1.5f, scale), 0.002f, "Walk alone at the tower speed");
            Assert.AreEqual(2.5f / set.Speed("CrouchWalk"), CharacterClipSet.LocomotionRate(1, 2.5f, scale), 0.002f, "CrouchWalk at its top speed");
        }

        [Test]
        public void ForwardAndSidewaysFamiliesPlayTheMovingClipAloneDownToRate07()
        {
            const float s = 1.030f;
            // crouch: edge exactly at 1.25 m/s, rate 0.70; 0.84 at 1.5; 1.40 at 2.5
            Assert.AreEqual(1.75f / 1.786f, CharacterClipSet.LocomotionRate(1, 1.75f, s), 0.002f);
            Assert.AreEqual(5f / 4.478f, CharacterClipSet.LocomotionRate(0, 5f, s), 0.002f);
            Assert.GreaterOrEqual(CharacterClipSet.LocomotionRate(1, 1.25f, s), 0.695f, "crouch 1.25 sits on the solo edge");
            Assert.AreEqual(1.25f / 1.7857f, CharacterClipSet.LocomotionRate(1, 1.25f, s), 0.002f);
            Assert.AreEqual(1.5f / 1.7857f, CharacterClipSet.LocomotionRate(1, 1.5f, s), 0.002f);
            Assert.AreEqual(2.5f / 1.7857f, CharacterClipSet.LocomotionRate(1, 2.5f, s), 0.002f);
            // below the edge the clip blends toward Idle at the edge rate (0.70), not at a rate that scales with speed
            Assert.AreEqual(0.70f, CharacterClipSet.LocomotionRate(1, 0.6f, s), 0.01f);
            // stand forward: Walk alone between 0.7 x and 1.4 x its on-body speed
            for (float v = 0.7f * 1.337f * s; v <= 1.4f * 1.337f * s; v += 0.1f)
                Assert.AreEqual(v / (1.337f * s), CharacterClipSet.LocomotionRate(0, v, s), 0.003f, $"Walk alone at {v}");
            // strafe and back: Run_* alone between 0.7 and 1.0 x its on-body speed
            for (float v = 0.7f * 3.035f * s; v <= 3.035f * s; v += 0.2f)
                Assert.AreEqual(v / (3.035f * s), CharacterClipSet.LocomotionRate(0, v, s, 1f), 0.003f, $"Run_* alone at {v}");
        }

        [Test]
        public void ControllerHasTheDriverParameters()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(BodyType.Men));
            foreach (var p in new[] { "Stance", "Transitioning", "Speed", "MoveX", "MoveZ", "Grounded", "VerticalSpeed", "AirTime", "JumpStart", "JumpLand", "LocoRate" })
                Assert.IsTrue(controller.parameters.Any(x => x.name == p), "missing parameter " + p);
        }

        [Test]
        public void LocomotionRateMeetsTheLimits()
        {
            Assert.AreEqual(1.15f, CharacterClipSet.LocomotionRate(0, 5f), 0.01f);
            Assert.AreEqual(8f / 5.80f, CharacterClipSet.LocomotionRate(0, 8f), 0.005f);
            for (float v = 0.1f; v <= 8.0f; v += 0.1f) // 8 m/s is the top speed
                Assert.LessOrEqual(CharacterClipSet.LocomotionRate(0, v), 1.4f, $"stand rate at {v}");
            Assert.AreEqual(1f, CharacterClipSet.LocomotionRate(2, 3f));
            // a scaled body: the planted foot is faster by the scale, so the rate drops by it (td: about 1.12 / 1.11 at s = 1.028)
            const float s = 1.0297f;
            Assert.AreEqual(1f, CharacterClipSet.LocomotionRate(0, 1.337f * s, s), 0.005f, "Walk plays at rate 1 on its own on-body speed");
            // Walk plays alone across the plateau: rate = speed / on-body clip speed from 0.7 to 1.4 (tower speeds 1.15 and 1.5)
            Assert.AreEqual(1.15f / (1.337f * s), CharacterClipSet.LocomotionRate(0, 1.15f, s), 0.005f);
            Assert.AreEqual(1.5f / (1.337f * s), CharacterClipSet.LocomotionRate(0, 1.5f, s), 0.005f);
            foreach (var v in new[] { 1.25f, 1.5f, 1.75f, 2.0f, 2.5f })
                Assert.AreEqual(v / 1.786f, CharacterClipSet.LocomotionRate(1, v, s), 0.005f, $"CrouchWalk alone at {v}");
            Assert.AreEqual(5f / (4.348f * s), CharacterClipSet.LocomotionRate(0, 5f, s), 0.005f);
            Assert.AreEqual(8f / 5.80f, CharacterClipSet.LocomotionRate(0, 8f, s), 0.005f, "the sprint clip's on-body speed is 5.80 for every scale");
            for (float v = 0.1f; v <= 8.0f; v += 0.1f)
                Assert.LessOrEqual(CharacterClipSet.LocomotionRate(0, v, s), 1.4f, $"scaled stand rate at {v}");
            foreach (var v in new[] { 1.25f, 1.75f, 2.5f })
                Assert.That(CharacterClipSet.LocomotionRate(1, v, s), Is.InRange(0.699f, 1.401f), $"scaled crouch rate at {v}");
            foreach (var v in new[] { 1.25f, 1.75f, 2.5f })
                Assert.That(CharacterClipSet.LocomotionRate(1, v), Is.InRange(0.699f, 1.401f), $"crouch rate at {v}");
        }

        static AnimatorController Tiny(int destination, float threshold)
        {
            var c = new AnimatorController();
            c.AddParameter("P", AnimatorControllerParameterType.Float);
            c.AddLayer("Base");
            var sm = c.layers[0].stateMachine;
            var a = sm.AddState("A"); var b = sm.AddState("B"); var d = sm.AddState("C");
            var t = a.AddTransition(destination == 0 ? b : d);
            t.AddCondition(AnimatorConditionMode.Greater, threshold, "P");
            b.AddTransition(a);
            return c;
        }

        [Test]
        public void ControllerDescriptionSeesAChangedDestinationOrCondition()
        {
            var one = Tiny(0, 0.5f); var same = Tiny(0, 0.5f); var moved = Tiny(1, 0.5f); var tuned = Tiny(0, 0.6f);
            try
            {
                Assert.AreEqual(CharacterAnimatorBuilder.Describe(one), CharacterAnimatorBuilder.Describe(same), "two identical controllers describe equally");
                Assert.AreNotEqual(CharacterAnimatorBuilder.Describe(one), CharacterAnimatorBuilder.Describe(moved), "a transition that points at another state changes the description");
                Assert.AreNotEqual(CharacterAnimatorBuilder.Describe(one), CharacterAnimatorBuilder.Describe(tuned), "a changed condition threshold changes the description");
            }
            finally { foreach (var c in new[] { one, same, moved, tuned }) Object.DestroyImmediate(c); }
        }

        [TestCaseSource(nameof(Types))]
        public void BuiltControllerDescribesItselfStably(BodyType type)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CharacterAnimatorBuilder.ControllerPath(type));
            Assert.IsNotNull(controller);
            var text = CharacterAnimatorBuilder.Describe(controller);
            Assert.AreEqual(text, CharacterAnimatorBuilder.Describe(controller));
            StringAssert.Contains("transition Base Layer/Stand -> Base Layer/Crouch", text);
        }
    }
}
