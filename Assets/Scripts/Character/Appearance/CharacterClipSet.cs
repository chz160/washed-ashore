using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Character.Appearance
{
    /// <summary>
    /// The animation clips of one body type under stable names (pack clips and the clips authored by
    /// tools/blender/character), so the Animator and the tests never depend on FBX take names.
    /// Names: see _bmad-output/poc/character-clip-manifest.md.
    /// </summary>
    [CreateAssetMenu(menuName = "Washed Ashore/Character Clip Set", fileName = "CharacterClips")]
    public sealed class CharacterClipSet : ScriptableObject
    {
        [Serializable]
        public struct NamedClip
        {
            public string name;
            public AnimationClip clip;
            [Tooltip("Ground speed of the planted foot in m/s ON THE BODY (authored speed x skeleton scale; locomotion clips only, 0 otherwise). Measured, see the manifest. Do not scale it again.")]
            public float speed;
        }

        public BodyType bodyType;
        public List<NamedClip> clips = new List<NamedClip>();

        public AnimationClip Get(string clipName)
        {
            foreach (var c in clips)
                if (c.name == clipName) return c.clip;
            return null;
        }

        /// <summary>On-body ground speed in m/s (already includes the skeleton scale): never multiply it by the scale again.</summary>
        public float Speed(string clipName)
        {
            foreach (var c in clips)
                if (c.name == clipName) return c.speed;
            return 0f;
        }

        // SPEED CONVENTION (td): `NamedClip.speed` / Speed(name) is ON THE BODY (authored speed x the skeleton scale, from the manifest).
        // The node clip speeds below are the UNSCALED authored speeds; LocomotionRate multiplies them by the skeleton scale argument.
        // Both node tables store the UNSCALED AUTHORED speed of each clip: StandNodes the measured values, CrouchNodes the designed on-body
        // 1.7857 divided by the body scale (the authoring script divides by the same scale), i.e. 1.734 for men and 1.7857 for women.
        // The 0.70 solo edge is inclusive: crouch 1.25 m/s is rate 0.700 and plays CrouchWalk alone (>= 0.695).
        // The Sprint node follows the crouch convention: the designed ON-BODY stance-foot speed (5.80 m/s, director amend8: rate 1.379 at 8 m/s) divided by the scale.
        // So the driver passes body.SkeletonScale to LocomotionRate and never rescales Speed(). Test: LocomotionRate(0, 5, s) == 5 / Speed("Jog").
        //
        // Locomotion blend nodes: (position = the game speed at which the clip is shown, the clip's authored ground speed).
        // The first moving clip of a family plays ALONE from 0.7 x its on-body speed up to 1.4 x (two nodes, same clip), rate = speed / clip
        // speed, so a planted foot stays planted; it blends toward Idle only below the 0.7 rate edge (the idle blend always slides).
        // Jog and Sprint sit at the game speeds 5 and 8 so those play alone (rates 1.12 and 1.11 at the men's scale).

        /// <summary>
        /// Stand blend nodes. Forward family: Walk alone from 0.7 to 1.4 x its on-body speed, then Run, Jog, Sprint. Strafe and back family
        /// (<paramref name="forwardFamily"/> false; the pack has no walk-speed strafe): Run_Left, Run_Right and Run_Back alone from 0.7 to 1.0
        /// x their speed, then Jog and Sprint. Positions of the first nodes are in on-body speed. Clip speeds are authored (unscaled).
        /// </summary>
        public static (float position, float clipSpeed)[] StandNodes(float scale = 1f, bool forwardFamily = true) => forwardFamily
            ? new[] { (0f, 0f), (0.7f * 1.337f * scale, 1.337f), (1.4f * 1.337f * scale, 1.337f), (3.035f * scale, 3.035f), (5f, 4.348f), (8f, 5.80f / scale) }
            : new[] { (0f, 0f), (0.7f * 3.035f * scale, 3.035f), (3.035f * scale, 3.035f), (5f, 4.348f), (8f, 5.80f / scale) };

        /// <summary>Crouch nodes: CrouchWalk alone from 1.25 to 2.5 m/s (rate 0.70 to 1.40; the edge is exactly 0.70 at 1.25), Idle below.
        /// Positions are game speeds; the clip is authored at 1.7857 / scale so its on-body speed is 1.7857.</summary>
        public static (float position, float clipSpeed)[] CrouchNodes(float scale = 1f) =>
            new[] { (0f, 0f), (1.25f, 1.7857f / scale), (2.5f, 1.7857f / scale) };

        /// <summary>
        /// Playback-rate multiplier for the locomotion blend at <paramref name="speed"/> m/s: speed divided by the weight-blended on-body
        /// ground speed of the clips in the blend, so a planted foot moves at the player's speed. 1 for stationary stances and below 0.05 m/s.
        /// Stance: 0 stand, 1 crouch, 2 squat, 3 sit, 4 sleep. <paramref name="skeletonScale"/> is <see cref="CharacterBody.SkeletonScale"/>
        /// (the node clip speeds are unscaled and multiplied here, once). <paramref name="sidewaysWeight"/> is 0 moving forward and 1 moving
        /// purely sideways or backwards: the rate is blended by it, so the switch between the two families never pops (derive it from the
        /// movement direction, for example 1 - max(0, cos(angle from forward)); do not switch at a hard angle). Written to LocoRate by the driver.
        /// </summary>
        public static float LocomotionRate(int stance, float speed, float skeletonScale = 1f, float sidewaysWeight = 0f)
        {
            if (speed < 0.05f || stance < 0 || stance > 1) return 1f;
            if (stance == 0 && sidewaysWeight > 0f)
            {
                float w = Mathf.Clamp01(sidewaysWeight);
                return Mathf.Lerp(Rate(StandNodes(skeletonScale, true), speed, skeletonScale), Rate(StandNodes(skeletonScale, false), speed, skeletonScale), w);
            }
            return Rate(stance == 0 ? StandNodes(skeletonScale, true) : CrouchNodes(skeletonScale), speed, skeletonScale);
        }

        static float Rate((float position, float clipSpeed)[] nodes, float speed, float skeletonScale)
        {
            var last = nodes[nodes.Length - 1];
            if (speed >= last.position) return speed / (last.clipSpeed * skeletonScale);
            for (int i = 1; i < nodes.Length; i++)
            {
                if (speed > nodes[i].position) continue;
                float t = (speed - nodes[i - 1].position) / (nodes[i].position - nodes[i - 1].position);
                float blended = Mathf.Lerp(nodes[i - 1].clipSpeed, nodes[i].clipSpeed, t) * skeletonScale;
                return blended > 0.01f ? speed / blended : 1f;
            }
            return 1f;
        }

        /// <summary>Canonical clip names the stance state machine uses.</summary>
        public static class Names
        {
            /// <summary>Suffix of the full-swing Jog and Sprint clips (the shadow body's; the capped clips have no suffix and drive the view body).</summary>
            public const string FullSuffix = "_Full";
            public const string Idle = "Idle", Walk = "Walk", Run = "Run", RunBack = "Run_Back", RunLeft = "Run_Left", RunRight = "Run_Right";
            public const string Jog = "Jog", JogBack = "JogBack", JogLeft = "JogLeft", JogRight = "JogRight";
            public const string Sprint = "Sprint", SprintBack = "SprintBack", SprintLeft = "SprintLeft", SprintRight = "SprintRight";
            public const string CrouchIdle = "Crouch_Idle", CrouchWalk = "CrouchWalk", CrouchWalkBack = "CrouchWalkBack";
            public const string CrouchWalkLeft = "CrouchWalkLeft", CrouchWalkRight = "CrouchWalkRight";
            public const string SquatEnter = "Squat_Enter", SquatHold = "Squat_Hold", SquatExit = "Squat_Exit";
            public const string SitEnter = "Sit_Enter", SitLoop = "Sit_Loop", SitExit = "Sit_Exit";
            public const string SleepDown = "Sleep_Down", SleepLoop = "Sleep_Loop", SleepWake = "Sleep_Wake";
            public const string JumpStart = "Jump_Start", JumpAir = "Jump_Air", JumpLand = "Jump_Land";

            /// <summary>Every clip the stance set needs (spec H3).</summary>
            public static readonly string[] Required =
            {
                Idle, Walk, Run, RunBack, RunLeft, RunRight, Jog, JogBack, JogLeft, JogRight,
                Sprint, SprintBack, SprintLeft, SprintRight,
                CrouchIdle, CrouchWalk, CrouchWalkBack, CrouchWalkLeft, CrouchWalkRight,
                SquatEnter, SquatHold, SquatExit, SitEnter, SitLoop, SitExit,
                SleepDown, SleepLoop, SleepWake, JumpStart, JumpAir, JumpLand,
            };
        }
    }
}
