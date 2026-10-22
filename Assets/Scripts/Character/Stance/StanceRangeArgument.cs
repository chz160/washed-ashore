#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Gameplay;

namespace WashedAshore.Character.Stance
{
    /// <summary>
    /// H9 retune launch argument (td, player-stances.md section 3b), dev builds and the Editor only: the whole class is behind the define, so a release build has no such
    /// type and ignores the argument (qa-2's strip check greps the release assembly for "StanceRangeArgument").
    /// Usage: -bbStance crouch=low,squat=record,sit=high,sleep=high,crouchspeed=high  (keys crouch, squat, sit, sleep, crouchspeed; values low, record, high;
    /// sleep low is the record value). Each named stance gets the recorded end's eye and capsule (capsule = eye + 0.15, sleep at the 0.80 floor, set by the stance code from
    /// the eye) and the end's clips on BOTH Animators: the VIEW on the base controller plus the view range clips, the SHADOW on the shadow controller plus the shadow range
    /// clips (its eight capped Jog and Sprint clips stay mapped to _Full). The combination is validated against the eye-ordering rule (each stance's eye at least 0.10 above
    /// the next lower stance's, epsilon 0.0001): if any pair fails the WHOLE argument is refused, every stance stays at record, and one error names the pair.
    /// </summary>
    public static class StanceRangeArgument
    {
        public const string Arg = "-bbStance";
        public const float OrderingGap = 0.10f, OrderingEpsilon = 0.0001f;

        /// <summary>The applied argument as typed (for the startup [Stance] log line); null when none was applied or the argument was refused.</summary>
        public static string Applied { get; private set; }

        /// <summary>Forgets a previous application (a fresh Player starts at record when the launch has no -bbStance).</summary>
        public static void ClearApplied() => Applied = null;

        public enum End { Low, Record, High }

        public struct Settings
        {
            public End crouch, squat, sit, sleep, crouchSpeed;
            public bool AnyChange => crouch != End.Record || squat != End.Record || sit != End.Record || sleep != End.Record || crouchSpeed != End.Record;
        }

        // Recorded values (player-stances.md section 3 and 3a).
        public static float CrouchEye(End e) => e == End.Low ? 1.10f : e == End.High ? 1.30f : 1.20f;
        public static float SquatEye(End e) => e == End.Low ? 0.90f : e == End.High ? 1.10f : 1.00f;
        public static float SitEye(End e) => e == End.Low ? 0.75f : e == End.High ? 0.90f : 0.80f;
        public static float SleepEye(End e) => e == End.High ? 0.25f : 0.20f;
        public static float CrouchSpeed(End e) => e == End.Low ? 1.25f : e == End.High ? 2.50f : 1.75f;

        /// <summary>The value of "-bbStance X" from a command line, or null when the argument is absent.</summary>
        public static string FromCommandLine(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == Arg) return args[i + 1];
            return null;
        }

        /// <summary>Parses "crouch=low,sit=high,...". Returns false with the reason when a key or value is unknown.</summary>
        public static bool TryParse(string value, out Settings settings, out string error)
        {
            settings = default;
            settings.crouch = settings.squat = settings.sit = settings.sleep = settings.crouchSpeed = End.Record;
            error = null;
            if (string.IsNullOrWhiteSpace(value)) { error = "empty -bbStance value"; return false; }
            foreach (var part in value.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) { error = $"'{part}' is not key=value"; return false; }
                string key = kv[0].Trim().ToLowerInvariant(), val = kv[1].Trim().ToLowerInvariant();
                End end;
                if (val == "low") end = End.Low; else if (val == "record" || val == "default") end = End.Record; else if (val == "high") end = End.High;
                else { error = $"'{val}' is not low, record or high"; return false; }
                switch (key)
                {
                    case "crouch": settings.crouch = end; break;
                    case "squat": settings.squat = end; break;
                    case "sit": settings.sit = end; break;
                    case "sleep": settings.sleep = end; break;
                    case "crouchspeed": settings.crouchSpeed = end; break;
                    default: error = $"unknown key '{key}' (crouch, squat, sit, sleep, crouchspeed)"; return false;
                }
            }
            return true;
        }

        /// <summary>Null when the combination passes the eye-ordering rule; else the text naming the breaking pair.</summary>
        public static string ValidateOrdering(Settings s)
        {
            float crouch = CrouchEye(s.crouch), squat = SquatEye(s.squat), sit = SitEye(s.sit);
            if (crouch - squat < OrderingGap - OrderingEpsilon) return $"crouch={s.crouch} ({crouch:F2}) and squat={s.squat} ({squat:F2}) are only {crouch - squat:F2} apart (need at least {OrderingGap:F2})";
            if (squat - sit < OrderingGap - OrderingEpsilon) return $"squat={s.squat} ({squat:F2}) and sit={s.sit} ({sit:F2}) are only {squat - sit:F2} apart (need at least {OrderingGap:F2})";
            return null;
        }

        /// <summary>
        /// Applies the argument to the player: validates, sets the stance tuning, composes the per-stance runtime override controllers and assigns them to both Animators.
        /// Returns null on success or when nothing changes, else the refusal text (already logged as one error; nothing was changed).
        /// </summary>
        public static string Apply(string value, PlayerController player, CharacterBody shadow, CharacterViewCopy viewCopy)
        {
            Applied = null;
            if (!TryParse(value, out var s, out string parseError))
            {
                Debug.LogError($"-bbStance refused: {parseError}. Every stance stays at record.");
                return parseError;
            }
            string pair = ValidateOrdering(s);
            if (pair != null)
            {
                Debug.LogError($"-bbStance refused: {pair}. The whole argument is rejected and every stance stays at record.");
                return pair;
            }
            if (!s.AnyChange) return null;
            var appearance = shadow.Appearance;
            var tuning = player.StanceTuning;
            tuning.crouchEye = CrouchEye(s.crouch);
            tuning.squatEye = SquatEye(s.squat);
            tuning.sitEye = SitEye(s.sit);
            tuning.sleepEye = SleepEye(s.sleep);
            tuning.crouchSpeed = CrouchSpeed(s.crouchSpeed);
            // Per body: the base is the body's own controller; every override for a changed stance's clip names comes from that end's range controller of that body.
            if (shadow.Animator != null) shadow.Animator.runtimeAnimatorController = Compose(appearance.shadowController != null ? appearance.shadowController : appearance.controller, appearance, s, true);
            if (viewCopy != null && viewCopy.ViewAnimator != null) viewCopy.ViewAnimator.runtimeAnimatorController = Compose(appearance.controller, appearance, s, false);
            Applied = value;
            Debug.Log($"-bbStance applied: crouch {s.crouch} (eye {tuning.crouchEye:F2}), squat {s.squat} ({tuning.squatEye:F2}), sit {s.sit} ({tuning.sitEye:F2}), sleep {s.sleep} ({tuning.sleepEye:F2}), crouch speed {s.crouchSpeed} ({tuning.crouchSpeed:F2} m/s)");
            return null;
        }

        /// <summary>Which stance a clip belongs to, by name (Crouch_Idle and CrouchWalk*, Squat_*, Sit_*, Sleep_*); null for every other clip.</summary>
        public static string StanceOfClip(string clipName)
        {
            if (clipName.StartsWith("Crouch")) return "crouch";
            if (clipName.StartsWith("Squat")) return "squat";
            if (clipName.StartsWith("Sit")) return "sit";
            if (clipName.StartsWith("Sleep")) return "sleep";
            return null;
        }

        static RuntimeAnimatorController Compose(RuntimeAnimatorController start, CharacterAppearance appearance, Settings s, bool forShadow)
        {
            var startOverride = start as AnimatorOverrideController;
            var baseController = startOverride != null ? startOverride.runtimeAnimatorController : start;
            var composed = new AnimatorOverrideController(baseController) { name = (start != null ? start.name : "Controller") + "_bbStance" };
            var list = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            composed.GetOverrides(list);
            var map = new Dictionary<AnimationClip, AnimationClip>();
            foreach (var kv in list) map[kv.Key] = null;
            // 1. the body's own overrides (the shadow's eight capped clips mapped to _Full)
            if (startOverride != null)
            {
                var own = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                startOverride.GetOverrides(own);
                foreach (var kv in own) if (kv.Value != null) map[kv.Key] = kv.Value;
            }
            // 2. every override of each changed stance's clip names, from that end's range controller of this body (holds, enters, exits, walks)
            void AddStance(string stance, End end)
            {
                if (end == End.Record) return;
                var rc = appearance.RangeController(end == End.High, forShadow) as AnimatorOverrideController;
                if (rc == null) { Debug.LogError($"-bbStance: the appearance has no {(forShadow ? "shadow" : "view")} range controller for {end}"); return; }
                var ov = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                rc.GetOverrides(ov);
                foreach (var kv in ov)
                    if (kv.Value != null && kv.Key != null && StanceOfClip(kv.Key.name) == stance) map[kv.Key] = kv.Value;
            }
            AddStance("crouch", s.crouch);
            AddStance("squat", s.squat);
            AddStance("sit", s.sit);
            AddStance("sleep", s.sleep);
            var final = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            foreach (var kv in map) final.Add(new KeyValuePair<AnimationClip, AnimationClip>(kv.Key, kv.Value));
            composed.ApplyOverrides(final);
            return composed;
        }
    }
}
#endif
