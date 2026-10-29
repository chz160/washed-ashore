using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.Support
{
    /// <summary>
    /// Pins <c>Time.captureDeltaTime</c> to the player's tick step (#46 PR B, td-46 adr/46-b-test-pin-pattern) for a test that waits N frames or
    /// reads a per-frame speed: with one tick per frame, "N frames" is N ticks and root delta / dt is the real speed. Begin sets the capture time;
    /// Track names the player whose span is checked (call it again after a scene reload); End puts the capture time back and asserts that the
    /// pinned span ran exactly one tick per frame and dropped none, so the pin cannot silently mean something else. Follows LookoutMovers.cs:36-43.
    /// </summary>
    public sealed class TickRatePin
    {
        float saved;
        PlayerController player;
        int startTick, startFrame;
        long startDropped;

        public void Begin()
        {
            saved = Time.captureDeltaTime;
            Time.captureDeltaTime = PlayerController.TickDt;
        }

        /// <summary>Starts counting for <paramref name="p"/>; a span of an earlier (still alive) player is checked first.</summary>
        public void Track(PlayerController p)
        {
            Verify();
            player = p;
            startTick = p.Tick;
            startFrame = Time.frameCount;
            startDropped = p.DroppedTicks;
        }

        /// <summary>Restores the capture time, then checks the span.</summary>
        public void End()
        {
            Time.captureDeltaTime = saved;
            Verify();
            player = null;
        }

        void Verify()
        {
            if (player == null) return;   // destroyed by a scene reload: nothing to check
            Assert.AreEqual(Time.frameCount - startFrame, player.Tick - startTick, "TickRatePin: the pinned span did not run exactly one tick per frame (ticks vs frames)");
            Assert.AreEqual(startDropped, player.DroppedTicks, "TickRatePin: ticks were dropped in the pinned span");
        }
    }
}
