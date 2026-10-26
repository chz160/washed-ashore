using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using WashedAshore.Character.Appearance;
using N = WashedAshore.Character.Appearance.CharacterClipSet.Names;

namespace WashedAshore.Character.Editor
{
    /// <summary>
    /// Builds CharacterAnimator_{Men,Women}.controller from the clip set, by script (spec H3). The parameter contract is
    /// engineer-2's StanceAnimatorDriver: Stance (int 0 stand, 1 crouch, 2 squat, 3 sit, 4 sleep), Transitioning, Speed, MoveX (right),
    /// MoveZ (forward), Grounded, VerticalSpeed, AirTime, LocoRate, triggers JumpStart and JumpLand.
    /// Stand and Crouch are 2D directional blend trees at game speeds (see CharacterClipSet.LocomotionRate); Squat, Sit and Sleep are
    /// enter / hold / exit chains; Jump is start / air / land. Leaving a stance always plays its exit clip first.
    /// </summary>
    public static class CharacterAnimatorBuilder
    {
        public const string StandState = "Stand", CrouchState = "Crouch", JumpStartState = "Jump_Start", JumpAirState = "Jump_Air", JumpLandState = "Jump_Land";

        public static string ControllerPath(BodyType type) => $"{CharacterAssetBuilder.ArtRoot}/CharacterAnimator_{type}.controller";

        static AnimatorState Add(AnimatorStateMachine sm, string name, Motion motion, Vector3 pos)
        {
            var s = sm.AddState(name, pos);
            s.motion = motion;
            s.writeDefaultValues = false;
            return s;
        }

        static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float duration, float exitTime = -1f)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = exitTime >= 0f;
            if (exitTime >= 0f) t.exitTime = exitTime;
            t.hasFixedDuration = true;
            t.duration = duration;
            t.interruptionSource = TransitionInterruptionSource.None;
            return t;
        }

        static void Stance(AnimatorStateTransition t, AnimatorConditionMode mode, int value) => t.AddCondition(mode, value, "Stance");

        static string BuildPath(BodyType type) => $"{CharacterAssetBuilder.ArtRoot}/CharacterAnimator_{type}_Build.controller";

        /// <summary>
        /// Builds the controller at a scratch path, then keeps the existing file when Describe says it is the same controller. New states and
        /// transitions get random fileIDs, so a plain rebuild would rewrite the file in a different order every run (#62).
        /// </summary>
        public static AnimatorController Build(BodyType type, CharacterClipSet clips)
        {
            var path = ControllerPath(type);
            var scratch = BuildPath(type);
            var fresh = BuildAt(type, clips, scratch);
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null && Describe(existing) == Describe(fresh))
            {
                AssetDatabase.DeleteAsset(scratch);
                return existing;
            }
            if (existing != null) AssetDatabase.DeleteAsset(path);
            var error = AssetDatabase.MoveAsset(scratch, path);
            if (!string.IsNullOrEmpty(error)) throw new System.InvalidOperationException($"{type}: could not move {scratch} to {path}: {error}");
            return AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }

        static AnimatorController BuildAt(BodyType type, CharacterClipSet clips, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null) AssetDatabase.DeleteAsset(path);
            var c = AnimatorController.CreateAnimatorControllerAtPath(path);
            c.AddParameter("Stance", AnimatorControllerParameterType.Int);
            c.AddParameter("Transitioning", AnimatorControllerParameterType.Bool);
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            c.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            c.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            c.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            c.AddParameter("AirTime", AnimatorControllerParameterType.Float);
            c.AddParameter("JumpStart", AnimatorControllerParameterType.Trigger);
            c.AddParameter("JumpLand", AnimatorControllerParameterType.Trigger);
            c.AddParameter("LocoRate", AnimatorControllerParameterType.Float);
            var rate = c.parameters;
            for (int i = 0; i < rate.Length; i++) if (rate[i].name == "LocoRate") rate[i].defaultFloat = 1f;
            rate[System.Array.FindIndex(rate, p => p.name == "Grounded")].defaultBool = true;
            c.parameters = rate;

            var sm = c.layers[0].stateMachine;
            float scale = CharacterAssetBuilder.SkeletonScaleOf(type);
            var stand = LocoState(c, sm, StandState, new Vector3(250, 0), StandNodes(clips, scale));
            var crouch = LocoState(c, sm, CrouchState, new Vector3(250, 120), CrouchNodes(clips));
            sm.defaultState = stand;

            var squatEnter = Add(sm, N.SquatEnter, Clip(clips, N.SquatEnter), new Vector3(520, -60));
            var squatHold = Add(sm, N.SquatHold, Clip(clips, N.SquatHold), new Vector3(760, -60));
            var squatExit = Add(sm, N.SquatExit, Clip(clips, N.SquatExit), new Vector3(1000, -60));
            var sitEnter = Add(sm, N.SitEnter, Clip(clips, N.SitEnter), new Vector3(520, 60));
            var sitLoop = Add(sm, N.SitLoop, Clip(clips, N.SitLoop), new Vector3(760, 60));
            var sitExit = Add(sm, N.SitExit, Clip(clips, N.SitExit), new Vector3(1000, 60));
            var sleepDown = Add(sm, N.SleepDown, Clip(clips, N.SleepDown), new Vector3(520, 180));
            var sleepLoop = Add(sm, N.SleepLoop, Clip(clips, N.SleepLoop), new Vector3(760, 180));
            var sleepWake = Add(sm, N.SleepWake, Clip(clips, N.SleepWake), new Vector3(1000, 180));
            var jumpStart = Add(sm, JumpStartState, Clip(clips, N.JumpStart), new Vector3(250, -200));
            var jumpAir = Add(sm, JumpAirState, Clip(clips, N.JumpAir), new Vector3(520, -200));
            var jumpLand = Add(sm, JumpLandState, Clip(clips, N.JumpLand), new Vector3(760, -200));

            // stand <-> crouch crossfade over the designer's 0.25 s
            Stance(Go(stand, crouch, 0.25f), AnimatorConditionMode.Equals, 1);
            Stance(Go(crouch, stand, 0.25f), AnimatorConditionMode.Equals, 0);

            // enter a stance from standing or crouching
            foreach (var from in new[] { stand, crouch })
            {
                Stance(Go(from, squatEnter, 0.15f), AnimatorConditionMode.Equals, 2);
                Stance(Go(from, sitEnter, 0.15f), AnimatorConditionMode.Equals, 3);
                Stance(Go(from, sleepDown, 0.15f), AnimatorConditionMode.Equals, 4);
            }

            // squat, sit: enter -> hold; any other Stance value plays the exit clip first, even mid-enter
            Chain(squatEnter, squatHold, squatExit, 2);
            Chain(sitEnter, sitLoop, sitExit, 3);
            Stance(Go(squatExit, stand, 0.1f, 0.9f), AnimatorConditionMode.Equals, 0);
            Stance(Go(squatExit, crouch, 0.1f, 0.9f), AnimatorConditionMode.Equals, 1);
            Stance(Go(squatExit, sitEnter, 0.1f, 0.9f), AnimatorConditionMode.Equals, 3);
            Stance(Go(squatExit, sleepDown, 0.1f, 0.9f), AnimatorConditionMode.Equals, 4);
            Stance(Go(sitExit, stand, 0.1f, 0.9f), AnimatorConditionMode.Equals, 0);
            Stance(Go(sitExit, crouch, 0.1f, 0.9f), AnimatorConditionMode.Equals, 1);
            Stance(Go(sitExit, squatEnter, 0.1f, 0.9f), AnimatorConditionMode.Equals, 2);
            Stance(Go(sitExit, sleepDown, 0.1f, 0.9f), AnimatorConditionMode.Equals, 4);

            // sleep: down -> loop; waking (any other Stance value) plays Sleep_Wake, then stands
            Go(sleepDown, sleepLoop, 0.05f, 0.95f);
            Stance(Go(sleepDown, sleepWake, 0.1f), AnimatorConditionMode.NotEqual, 4);
            Stance(Go(sleepLoop, sleepWake, 0.1f), AnimatorConditionMode.NotEqual, 4);
            Go(sleepWake, stand, 0.1f, 0.9f);

            // jump (standing only: lowered stances block jumping, designer-2 section 5)
            var js = Go(stand, jumpStart, 0.02f);
            js.AddCondition(AnimatorConditionMode.If, 0, "JumpStart");
            Stance(js, AnimatorConditionMode.Equals, 0);
            var walkOff = Go(stand, jumpAir, 0.1f);
            walkOff.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            walkOff.AddCondition(AnimatorConditionMode.Greater, 0.2f, "AirTime");
            Go(jumpStart, jumpAir, 0.02f, 0.95f);
            var land = Go(jumpAir, jumpLand, 0.05f);
            land.AddCondition(AnimatorConditionMode.If, 0, "JumpLand");
            var shortAir = Go(jumpAir, stand, 0.1f);
            shortAir.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            Go(jumpLand, stand, 0.1f, 0.6f); // clip 0.267 s: the 0.1 s blend ends with the clip

            EditorUtility.SetDirty(c);
            AssetDatabase.SaveAssets();
            return c;
        }

        /// <summary>
        /// Text description of a controller made through the Unity API, with no fileIDs: parameters, layers, and per state machine the
        /// states (sorted by name), their motions (clip GUID + local id, blend trees with all children), and every transition keyed by
        /// source and destination names, with its settings and conditions in order.
        /// </summary>
        public static string Describe(AnimatorController c)
        {
            var sb = new StringBuilder();
            var paths = new Dictionary<AnimatorState, string>();
            foreach (var l in c.layers) CollectPaths(l.stateMachine, l.name, paths);
            foreach (var p in c.parameters) sb.Append($"param {p.name} {p.type} {p.defaultFloat:R} {p.defaultInt} {p.defaultBool}\n");
            foreach (var l in c.layers)
            {
                sb.Append($"layer {l.name} w={l.defaultWeight:R} blend={l.blendingMode} sync={l.syncedLayerIndex} ik={l.iKPass} mask={Ident(l.avatarMask)}\n");
                DescribeMachine(sb, l.stateMachine, l.name, paths);
            }
            return sb.ToString();
        }

        static string Ident(Object o)
        {
            if (o == null) return "-";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out var guid, out long id) ? $"{guid}:{id}" : o.name;
        }

        static void CollectPaths(AnimatorStateMachine sm, string path, Dictionary<AnimatorState, string> paths)
        {
            foreach (var cs in sm.states) paths[cs.state] = path + "/" + cs.state.name;
            foreach (var child in sm.stateMachines) CollectPaths(child.stateMachine, path + "/" + child.stateMachine.name, paths);
        }

        static void DescribeMachine(StringBuilder sb, AnimatorStateMachine sm, string path, Dictionary<AnimatorState, string> paths)
        {
            sb.Append($"machine {path} default={(sm.defaultState ? sm.defaultState.name : "-")}\n");
            foreach (var cs in sm.states.OrderBy(x => x.state.name, System.StringComparer.Ordinal))
            {
                var s = cs.state; var sp = $"{path}/{s.name}";
                sb.Append($"state {sp} pos={cs.position} speed={s.speed:R} speedP={s.speedParameterActive}:{s.speedParameter} tag={s.tag} wd={s.writeDefaultValues} mirror={s.mirror}:{s.mirrorParameterActive}:{s.mirrorParameter} cycle={s.cycleOffset:R}:{s.cycleOffsetParameterActive}:{s.cycleOffsetParameter} timeP={s.timeParameterActive}:{s.timeParameter} ik={s.iKOnFeet} behaviours={s.behaviours.Length}\n");
                DescribeMotion(sb, s.motion, sp);
                foreach (var t in s.transitions) DescribeTransition(sb, t, sp, paths);
            }
            foreach (var t in sm.anyStateTransitions) DescribeTransition(sb, t, path + "/*any", paths);
            foreach (var t in sm.entryTransitions) DescribeTransition(sb, t, path + "/*entry", paths);
            foreach (var child in sm.stateMachines.OrderBy(x => x.stateMachine.name, System.StringComparer.Ordinal)) DescribeMachine(sb, child.stateMachine, path + "/" + child.stateMachine.name, paths);
        }

        static void DescribeMotion(StringBuilder sb, Motion m, string at)
        {
            if (m is BlendTree t)
            {
                sb.Append($"tree {at} {t.blendType} {t.blendParameter} {t.blendParameterY} auto={t.useAutomaticThresholds}\n");
                foreach (var ch in t.children) { sb.Append($"child pos={ch.position} th={ch.threshold:R} ts={ch.timeScale:R} cycle={ch.cycleOffset:R} mirror={ch.mirror} direct={ch.directBlendParameter}\n"); DescribeMotion(sb, ch.motion, at + "/child"); }
            }
            else sb.Append($"motion {at} {Ident(m)}\n");
        }

        static void DescribeTransition(StringBuilder sb, AnimatorTransitionBase t, string from, Dictionary<AnimatorState, string> paths)
        {
            var to = t.destinationState ? (paths.TryGetValue(t.destinationState, out var dest) ? dest : t.destinationState.name) : t.destinationStateMachine ? "machine:" + t.destinationStateMachine.name : "-";
            sb.Append($"transition {from} -> {to} exit={t.isExit} mute={t.mute} solo={t.solo}");
            if (t is AnimatorStateTransition st)
                sb.Append($" exitTime={st.hasExitTime}:{st.exitTime:R} dur={st.hasFixedDuration}:{st.duration:R} offset={st.offset:R} interrupt={st.interruptionSource}:{st.orderedInterruption} self={st.canTransitionToSelf}");
            sb.Append('\n');
            foreach (var cond in t.conditions) sb.Append($"  if {cond.parameter} {cond.mode} {cond.threshold:R}\n");
        }

        public static string ShadowControllerPath(BodyType type) => $"{CharacterAssetBuilder.ArtRoot}/CharacterAnimator_{type}_Shadow.overrideController";

        /// <summary>The eight capped clips the shadow replaces with their full-swing versions (director amend7).</summary>
        public static readonly string[] ShadowSwapped =
        {
            N.Jog, N.JogBack, N.JogLeft, N.JogRight, N.Sprint, N.SprintBack, N.SprintLeft, N.SprintRight,
        };

        /// <summary>
        /// Override of the base controller for the SHADOW body: the 8 capped Jog and Sprint clips play their _Full versions, everything
        /// else is unchanged. The VIEW body's Animator uses the base controller (capped arms). The _Full clips are referenced only here.
        /// </summary>
        public static AnimatorOverrideController BuildShadowOverride(BodyType type, AnimatorController baseController, CharacterClipSet clips)
        {
            var path = ShadowControllerPath(type);
            if (AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path) != null) AssetDatabase.DeleteAsset(path);
            var o = new AnimatorOverrideController(baseController);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            foreach (var n in ShadowSwapped)
            {
                var full = clips.Get(n + N.FullSuffix);
                if (full == null) throw new System.InvalidOperationException($"{type}: clip {n + N.FullSuffix} missing");
                pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(clips.Get(n), full));
            }
            o.ApplyOverrides(pairs);
            AssetDatabase.CreateAsset(o, path);
            return o;
        }

        /// <summary>Range-end controller path. The plain one overrides the BASE controller (for the VIEW body); the _Shadow one adds the eight capped -> _Full swaps (for the SHADOW body).</summary>
        public static string RangeControllerPath(BodyType type, bool high, bool forShadow = false) =>
            $"{CharacterAssetBuilder.ArtRoot}/CharacterAnimator_{type}_Range{(high ? "High" : "Low")}{(forShadow ? "_Shadow" : "")}.overrideController";

        /// <summary>The range-end controller to hand to a body: the shadow gets its variant (full-swing Jog and Sprint kept), the view the base-controller one.</summary>
        public static AnimatorOverrideController RangeControllerFor(BodyType type, bool high, bool forShadow) =>
            AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(RangeControllerPath(type, high, forShadow));

        /// <summary>
        /// Override controllers that swap the hold / idle / walk-loop clips for the range-end variants (designer-2 section 3a), for the
        /// range proof. High also swaps Sleep_Loop for Sleep_Loop_High. Each range has two controllers: the plain one is for the VIEW body (base
        /// controller, capped arms); the _Shadow one also maps the eight capped Jog and Sprint clips to _Full and is for the SHADOW body.
        /// Hand each body its own (RangeControllerFor); assigning the plain one to the shadow would drop its full-swing mapping.
        /// </summary>
        public static void BuildRangeEnds(BodyType type, AnimatorController baseController, CharacterClipSet clips)
        {
            foreach (bool high in new[] { false, true })
            foreach (bool forShadow in new[] { false, true })
            {
                string suffix = high ? "_High" : "_Low";
                var names = new List<string> { N.CrouchIdle, N.CrouchWalk, N.CrouchWalkBack, N.CrouchWalkLeft, N.CrouchWalkRight, N.SquatHold, N.SitLoop };
                // the enter / exit clips that hand over to the holds, authored at the same end pose (no jump at the boundary)
                names.AddRange(new[] { N.SquatEnter, N.SquatExit, N.SitEnter, N.SitExit });
                if (high) names.AddRange(new[] { N.SleepLoop, N.SleepDown, N.SleepWake });
                var path = RangeControllerPath(type, high, forShadow);
                if (AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path) != null) AssetDatabase.DeleteAsset(path);
                var o = new AnimatorOverrideController(baseController);
                var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                foreach (var n in names)
                {
                    var variant = clips.Get(n + suffix);
                    if (variant == null) throw new System.InvalidOperationException($"{type}: clip {n + suffix} missing");
                    pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(clips.Get(n), variant));
                }
                if (forShadow)
                    foreach (var n in ShadowSwapped)
                    {
                        var full = clips.Get(n + N.FullSuffix);
                        if (full == null) throw new System.InvalidOperationException($"{type}: clip {n + N.FullSuffix} missing");
                        pairs.Add(new KeyValuePair<AnimationClip, AnimationClip>(clips.Get(n), full));
                    }
                o.ApplyOverrides(pairs);
                AssetDatabase.CreateAsset(o, path);
            }
        }

        static void Chain(AnimatorState enter, AnimatorState hold, AnimatorState exit, int stance)
        {
            Go(enter, hold, 0.05f, 0.95f);
            Stance(Go(enter, exit, 0.1f), AnimatorConditionMode.NotEqual, stance);
            Stance(Go(hold, exit, 0.05f), AnimatorConditionMode.NotEqual, stance);
        }

        static Motion Clip(CharacterClipSet set, string name)
        {
            var clip = set.Get(name);
            if (clip == null) throw new System.InvalidOperationException($"{set.bodyType}: clip '{name}' missing");
            return clip;
        }

        static AnimatorState LocoState(AnimatorController c, AnimatorStateMachine sm, string name, Vector3 pos, List<(AnimationClip clip, Vector2 at)> nodes)
        {
            var tree = new BlendTree { name = name, blendType = BlendTreeType.FreeformDirectional2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, c);
            foreach (var (clip, at) in nodes)
            {
                if (clip == null) throw new System.InvalidOperationException($"{name} blend tree: a clip is missing at {at}");
                tree.AddChild(clip, at);
            }
            var state = Add(sm, name, tree, pos);
            state.speedParameterActive = true;
            state.speedParameter = "LocoRate";
            return state;
        }

        // (x = right m/s, y = forward m/s). Positions are the game speeds each clip carries, see CharacterClipSet.StandNodes.
        static List<(AnimationClip, Vector2)> StandNodes(CharacterClipSet s, float k) => new List<(AnimationClip, Vector2)>
        {
            (s.Get(N.Idle), Vector2.zero),
            (s.Get(N.Walk), new Vector2(0, 0.7f * 1.337f * k)), (s.Get(N.Walk), new Vector2(0, 1.4f * 1.337f * k)),
            (s.Get(N.Run), new Vector2(0, 3.035f * k)), (s.Get(N.Jog), new Vector2(0, 5f)), (s.Get(N.Sprint), new Vector2(0, 8f)),
            (s.Get(N.RunBack), new Vector2(0, -0.7f * 3.035f * k)), (s.Get(N.RunBack), new Vector2(0, -3.035f * k)), (s.Get(N.JogBack), new Vector2(0, -5f)), (s.Get(N.SprintBack), new Vector2(0, -8f)),
            (s.Get(N.RunLeft), new Vector2(-0.7f * 3.035f * k, 0)), (s.Get(N.RunLeft), new Vector2(-3.035f * k, 0)), (s.Get(N.JogLeft), new Vector2(-5f, 0)), (s.Get(N.SprintLeft), new Vector2(-8f, 0)),
            (s.Get(N.RunRight), new Vector2(0.7f * 3.035f * k, 0)), (s.Get(N.RunRight), new Vector2(3.035f * k, 0)), (s.Get(N.JogRight), new Vector2(5f, 0)), (s.Get(N.SprintRight), new Vector2(8f, 0)),
        };

        static List<(AnimationClip, Vector2)> CrouchNodes(CharacterClipSet s) => new List<(AnimationClip, Vector2)>
        {
            (s.Get(N.CrouchIdle), Vector2.zero),
            (s.Get(N.CrouchWalk), new Vector2(0, 1.25f)), (s.Get(N.CrouchWalk), new Vector2(0, 2.5f)),
            (s.Get(N.CrouchWalkBack), new Vector2(0, -1.25f)), (s.Get(N.CrouchWalkBack), new Vector2(0, -2.5f)),
            (s.Get(N.CrouchWalkLeft), new Vector2(-1.25f, 0)), (s.Get(N.CrouchWalkLeft), new Vector2(-2.5f, 0)),
            (s.Get(N.CrouchWalkRight), new Vector2(1.25f, 0)), (s.Get(N.CrouchWalkRight), new Vector2(2.5f, 0)),
        };
    }
}
