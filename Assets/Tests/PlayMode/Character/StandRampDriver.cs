using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// Speed-ramp source for the director's blend captures (qa-49 T14/T21): ramps the planar velocity the PlayerCharacterView reads (through the development-only
    /// PlayerCharacterView.TestLocalVelocityOverride), so MoveX, MoveZ, Speed and LocoRate follow the real formula and both Animators get the same values through
    /// the view's own Set(). It never writes Animator parameters. Time is pinned to the player tick (Time.captureDeltaTime = PlayerController.TickDt, 60 fps) and the ramp advances by frame count, so the
    /// same ramp twice gives the same frames. Each frame is logged: frame, t, speed, MoveX, MoveZ, LocoRate, stance, and the active clips with their weights.
    /// Test code only: it lives in the PlayMode test assembly and is absent from a player build.
    /// </summary>
    [DefaultExecutionOrder(40)]   // before PlayerCharacterView (50): the override is in place when the view reads the velocity
    public sealed class StandRampDriver : MonoBehaviour
    {
        /// <summary>Frames per second of the pinned run: one player tick per frame (PlayerController.TickDt, 60). The 30 fps contact sheets take every second row.</summary>
        public static int Fps => Mathf.RoundToInt(1f / PlayerController.TickDt);

        PlayerCharacterView view;
        float from, to, rate;
        Vector2 direction;       // x = right, y = forward (player-local)
        int frame;
        bool running;
        float savedCaptureDelta;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> rows = new List<string>();

        public bool Running => running;
        public int Frame => frame;
        public IReadOnlyList<string> Rows => rows;

        /// <summary>Starts a ramp of the local speed from <paramref name="fromSpeed"/> to <paramref name="toSpeed"/> at <paramref name="ratePerSecond"/> m/s per second along <paramref name="localDirection"/> (x right, y forward).</summary>
        public void Begin(PlayerCharacterView target, float fromSpeed, float toSpeed, float ratePerSecond, Vector2 localDirection)
        {
            view = target;
            from = fromSpeed;
            to = toSpeed;
            rate = Mathf.Abs(ratePerSecond) * Mathf.Sign(toSpeed - fromSpeed);
            direction = localDirection.sqrMagnitude > 0f ? localDirection.normalized : Vector2.up;
            frame = 0;
            rows.Clear();
            log.Clear();
            log.AppendLine("frame\tt_s\tspeed_cmd\tSpeed\tMoveX\tMoveZ\tLocoRate\tstance\tclips(weight)");
            savedCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = PlayerController.TickDt;   // pinned to the player tick (one tick per frame, as StanceFixture.PinTicks asserts): repeatable frames
            running = true;
        }

        /// <summary>The commanded speed at this frame (also the last frame's value when the ramp has finished).</summary>
        public float CommandedSpeed
        {
            get
            {
                float v = from + rate * (frame / (float)Fps);
                return rate >= 0f ? Mathf.Min(v, to) : Mathf.Max(v, to);
            }
        }

        void Update()
        {
            if (!running) return;
            float s = CommandedSpeed;
            PlayerCharacterView.TestLocalVelocityOverride = new Vector3(direction.x * s, 0f, direction.y * s);
        }

        void LateUpdate()
        {
            if (!running || view == null) return;
            var a = view.Body != null ? view.Body.Animator : null;
            if (a != null)
            {
                var sb = new StringBuilder();
                foreach (var info in a.GetCurrentAnimatorClipInfo(0))
                    if (info.weight > 0.001f) sb.Append(sb.Length > 0 ? "+" : "").Append(info.clip.name.Split('|')[^1]).Append('(').Append(info.weight.ToString("F2")).Append(')');
                string row = $"{frame}\t{frame / (float)Fps:F3}\t{CommandedSpeed:F3}\t{a.GetFloat("Speed"):F3}\t{a.GetFloat("MoveX"):F3}\t{a.GetFloat("MoveZ"):F3}\t{a.GetFloat("LocoRate"):F3}\t{a.GetInteger("Stance")}\t{sb}";
                rows.Add(row);
                log.AppendLine(row);
            }
            bool done = (rate >= 0f && CommandedSpeed >= to) || (rate < 0f && CommandedSpeed <= to);
            frame++;
            if (done) End();
        }

        /// <summary>Stops the ramp, clears the override and restores the frame time.</summary>
        public void End()
        {
            if (!running) return;
            running = false;
            PlayerCharacterView.TestLocalVelocityOverride = null;
            Time.captureDeltaTime = savedCaptureDelta;
        }

        /// <summary>Writes the per-frame log (tab separated) to <paramref name="path"/>.</summary>
        public void WriteLog(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, log.ToString());
        }

        void OnDestroy()
        {
            if (running) End();
        }
    }
}
