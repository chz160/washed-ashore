using System.Collections.Generic;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Every player that exists, in registration order (#46 P4). A PlayerController adds itself in Awake and leaves in OnDestroy, so a
    /// system that must reach "every player" reads this list instead of searching the scene by tag or name, and a player that is
    /// switched off (PlayerController disabled, capsule still in the world) is still found, as the tag search found it. Readers skip
    /// players whose GameObject is not active in the hierarchy (<see cref="IsLive"/>, what the tag search skipped); the world-bounds
    /// clamp, which used to drop a player on OnDisable, also requires <c>isActiveAndEnabled</c>. The list holds no per-player data (that lives on the player) and,
    /// like <see cref="WorldBoundsClamp"/>'s body list, never allocates after warm-up. Enter Play Mode runs with domain reload off:
    /// destroying the scene removes every entry (OnDestroy), and Register drops entries whose object is already gone.
    /// </summary>
    public static class PlayerRegistry
    {
        static readonly List<PlayerController> players = new List<PlayerController>();

        /// <summary>Live players by registration order. Iterate by index; do not hold the list across frames.</summary>
        public static IReadOnlyList<PlayerController> All => players;
        public static int Count => players.Count;
        /// <summary>Bumped on every Register, Unregister and clear. A reader that caches a pick compares it to see that the list changed (td-46 adr/46-a-birdfps/ruling, D6).</summary>
        public static int Version { get; private set; }

        /// <summary>A player a system should act on: exists and its GameObject is active in the hierarchy (the tag search found exactly these).</summary>
        public static bool IsLive(PlayerController p) => p != null && p.gameObject.activeInHierarchy;

        /// <summary>The live player nearest to <paramref name="from"/> on the ground plane (x, z), or null when none is live.</summary>
        public static PlayerController NearestFlat(UnityEngine.Vector3 from)
        {
            PlayerController best = null;
            float bestSqr = float.PositiveInfinity;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (!IsLive(p)) continue;
                Vector3Flat(p.transform.position, from, out float sqr);
                if (sqr < bestSqr) { bestSqr = sqr; best = p; }
            }
            return best;
        }

        static void Vector3Flat(UnityEngine.Vector3 a, UnityEngine.Vector3 b, out float sqr)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            sqr = dx * dx + dz * dz;
        }

        /// <summary>Domain reload may be off: start every Play session with an empty list (td-46 adr/46-a-departures-3/d5-final).</summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnPlayStart() { players.Clear(); Version++; }

        internal static void Register(PlayerController p)
        {
            if (p == null) return;
            Version++;
            for (int i = players.Count - 1; i >= 0; i--)
                if (players[i] == null) players.RemoveAt(i);
            if (!players.Contains(p)) players.Add(p);
        }

        internal static void Unregister(PlayerController p) { players.Remove(p); Version++; }
    }
}
