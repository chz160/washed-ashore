using System.Collections.Generic;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// A world system that acts on a player inside the player's tick (#46 PR B): <see cref="BeforeMove"/> runs before the stance and move
    /// step, <see cref="AfterMove"/> after it. Per-player state lives on the player, never on the hook. Orders are the old execution
    /// orders: TowerWalkZones -100, TrapDoor 0, WorldBoundsClamp 10000.
    /// </summary>
    public interface IPlayerTickHook
    {
        int Order { get; }
        void BeforeMove(PlayerController p);
        void AfterMove(PlayerController p);
    }

    /// <summary>The registered hooks, sorted by Order (then registration order) when they register, called by index: no allocation per tick.</summary>
    public static class PlayerTickHooks
    {
        static readonly List<IPlayerTickHook> hooks = new List<IPlayerTickHook>();

        public static int Count => hooks.Count;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ClearOnPlayStart() => hooks.Clear();

        public static void Register(IPlayerTickHook hook)
        {
            if (hook == null || hooks.Contains(hook)) return;
            int i = hooks.Count;
            while (i > 0 && hooks[i - 1].Order > hook.Order) i--;
            hooks.Insert(i, hook);
        }

        public static void Unregister(IPlayerTickHook hook) => hooks.Remove(hook);

        internal static void RunBefore(PlayerController p)
        {
            for (int i = 0; i < hooks.Count; i++) hooks[i].BeforeMove(p);
        }

        internal static void RunAfter(PlayerController p)
        {
            for (int i = 0; i < hooks.Count; i++) hooks[i].AfterMove(p);
        }
    }
}
