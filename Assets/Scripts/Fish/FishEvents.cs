using System;
using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>Surface signs (brief surfaceEvent kinds). The sim decides when; the renderer decides how they look.</summary>
    public enum FishSurfaceKind : byte
    {
        None, Dimple, Swirl, Roll, RollOrTail, GarGulpOrBask, Busting, Wake, NervousWaterOrFlip, Jump, FleeWake, Rise,
    }

    /// <summary>One surface sign. A sign from a dormant cell has fishId -1 (no live body; at that range none is drawn).</summary>
    public struct FishSurfaceEvent
    {
        public FishSurfaceKind kind;
        public Vector3 position;     // on the surface (WaterLevelY + WaterMotion at the time)
        public float size;           // body length of the fish (or school radius), metres
        public Vector3 heading;      // unit, horizontal
        public long fishId;
        public int species;
        public double startTime;     // WaterClock time
        public float duration;
        public bool spontaneous;     // false = flush sign from a scatter
        public bool farWater;        // a sign-only event beyond the open band's edge (no fish live there)
        public bool bask;            // GarGulpOrBask: a bask (the gar holding at the surface), not a gulp (F17 legibility tells them apart)
    }

    /// <summary>
    /// The sim's surface signs (adr/fish-1 §5.5). WashedAshore.Fish.Rendering subscribes; Fish never references it.
    /// Tests and f-qa's probe subscribe too.
    /// </summary>
    public static class FishEvents
    {
        public static event Action<FishSurfaceEvent> Surface;

        internal static void Raise(in FishSurfaceEvent e) => Surface?.Invoke(e);

        /// <summary>Tests: drop every subscriber.</summary>
        public static void ClearForTests() => Surface = null;
    }
}
