using System.Collections.Generic;
using UnityEngine;
using WashedAshore.Fish;

namespace WashedAshore.Perf
{
    /// <summary>
    /// Fish actually drawn, counted after the fish renderer (issue #9, technical-director's fish-counter ruling on #26).
    /// FishPopulation.Update clears FishPopulation.DrawModes every frame, and FishRenderer.LateUpdate (execution order 1000)
    /// refills them with the mode drawn. A coroutine resumed by "yield return null" runs between the two and always sees
    /// the cleared array, so the count is taken here in LateUpdate at order 10000, after the renderer, as FishProbe does
    /// (FishProbe.cs, order 10000). FrustumProbe reads <see cref="Last"/> from its coroutine, so it sees the previous
    /// frame's count: a one-frame lag, the same in every window.
    /// </summary>
    [DefaultExecutionOrder(Order)]
    public class FishDrawCounter : MonoBehaviour
    {
        /// <summary>After FishRenderer (1000), with FishProbe (10000).</summary>
        public const int Order = 10000;

        /// <summary>Fish drawn (draw mode not None) in the last frame's LateUpdate; 0 when there is no population.</summary>
        public static int Last { get; private set; }

        /// <summary>Frames counted since start: proves the counter ran.</summary>
        public static int Frames { get; private set; }

        FishPopulation pop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Last = 0; Frames = 0; }

        void LateUpdate()
        {
            if (!pop || !pop.isActiveAndEnabled) pop = FishPopulation.Active;   // cached; looked up again only when lost
            Last = pop && pop.isActiveAndEnabled ? Count(pop.DrawModes, pop.Count) : 0;
            Frames++;
        }

        /// <summary>Draw modes other than None among the first <paramref name="live"/> entries (pure, for the tests).</summary>
        public static int Count(IReadOnlyList<FishDrawMode> modes, int live)
        {
            if (modes == null) return 0;
            int n = 0;
            for (int i = 0; i < live && i < modes.Count; i++) if (modes[i] != FishDrawMode.None) n++;
            return n;
        }
    }
}
