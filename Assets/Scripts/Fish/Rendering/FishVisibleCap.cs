using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Fish.Rendering
{
    /// <summary>
    /// The visible-body cap, built to f-td's review (21:13Z; adopted by f-designer and team-lead). Brief maxVisibleBodiesShore 3.
    /// - No eviction: a shown fish stays shown until the draw rule stops allowing it (murk floor, R_sub, frustum); it then
    ///   leaves the shown set through that normal path.
    /// - Admission only while fewer than <c>soft</c> are shown. Among the waiting candidates the highest priority
    ///   (FishMurk alpha x projected size) is admitted, ties broken by the stable fishId; one admission per frame.
    /// - An admitted fish fades in over <see cref="FadeSeconds"/>: the brief's existing body fade-in (fish-targets sim.spawnRule,
    ///   "bodies fade in over 1 s"), on the same alpha path as the murk fade. A waiting (held) fish isn't drawn (alpha 0), so drawn stays inside FishView.CouldSee.
    /// - A fish that left the shown set can't be admitted again for <see cref="CooldownSeconds"/>.
    /// - Exempt fish (airborne jump bodies) are always drawn at alpha 1 and never counted.
    /// - Every admission and every removal is raised on <see cref="Changed"/> (fishId, admitted?) and listed for this frame in
    ///   <see cref="AdmittedThisFrame"/> / <see cref="RemovedThisFrame"/> / <see cref="NewlyHeldThisFrame"/> (f-qa's N7 log).
    /// No per-frame allocation (fixed dictionaries; the event is only invoked if someone subscribed).
    /// </summary>
    public sealed class FishVisibleCap
    {
        public const float FadeSeconds = 1f, CooldownSeconds = 3f;

        /// <summary>(fishId, true = admitted / false = removed from the shown set).</summary>
        public event Action<long, bool> Changed;

        public int Admissions { get; private set; }
        public readonly List<long> AdmittedThisFrame = new List<long>(8), RemovedThisFrame = new List<long>(16), NewlyHeldThisFrame = new List<long>(16);
        readonly HashSet<long> held = new HashSet<long>(), heldNow = new HashSet<long>();
        /// <summary>True if this fish is a candidate the cap is holding back (alpha 0) this frame.</summary>
        public bool IsHeld(long id) => held.Contains(id);
        public int Removals { get; private set; }

        readonly Dictionary<long, float> shown = new Dictionary<long, float>(64);     // fishId -> fade 0..1
        readonly Dictionary<long, float> leftAt = new Dictionary<long, float>(256);   // fishId -> clock when it left
        readonly HashSet<long> present = new HashSet<long>();
        readonly List<long> scratch = new List<long>(64);
        float clock;

        /// <summary>
        /// One frame: <paramref name="n"/> candidates the draw rule allows. Writes each one's alpha multiplier (0 = not drawn)
        /// and returns how many non-exempt fish are shown (alpha > 0).
        /// </summary>
        public int Apply(int n, long[] ids, float[] priority, bool[] exempt, float[] alpha, float dt, int soft)
        {
            clock += dt;
            AdmittedThisFrame.Clear(); RemovedThisFrame.Clear(); NewlyHeldThisFrame.Clear();
            present.Clear();
            for (int i = 0; i < n; i++) if (!exempt[i]) present.Add(ids[i]);

            // Shown fish the rule no longer allows leave (their own murk / range / frustum exit).
            scratch.Clear();
            foreach (var kv in shown) if (!present.Contains(kv.Key)) scratch.Add(kv.Key);
            foreach (var id in scratch)
            {
                shown.Remove(id);
                leftAt[id] = clock;
                Removals++;
                RemovedThisFrame.Add(id);
                Changed?.Invoke(id, false);
            }
            // Forget cooldowns that have run out.
            scratch.Clear();
            foreach (var kv in leftAt) if (clock - kv.Value >= CooldownSeconds) scratch.Add(kv.Key);
            foreach (var id in scratch) leftAt.Remove(id);

            // Admission: at most one per frame, only below soft.
            if (shown.Count < soft)
            {
                int best = -1;
                for (int i = 0; i < n; i++)
                {
                    if (exempt[i] || shown.ContainsKey(ids[i]) || leftAt.ContainsKey(ids[i])) continue;
                    if (best < 0 || priority[i] > priority[best] || (priority[i] == priority[best] && ids[i] < ids[best])) best = i;
                }
                if (best >= 0)
                {
                    shown[ids[best]] = 0f;
                    Admissions++;
                    AdmittedThisFrame.Add(ids[best]);
                    Changed?.Invoke(ids[best], true);
                }
            }

            float step = dt / FadeSeconds;
            int count = 0;
            heldNow.Clear();
            for (int i = 0; i < n; i++)
            {
                if (exempt[i]) { alpha[i] = 1f; continue; }
                if (!shown.TryGetValue(ids[i], out float f))
                {
                    alpha[i] = 0f;
                    heldNow.Add(ids[i]);
                    if (!held.Contains(ids[i])) NewlyHeldThisFrame.Add(ids[i]);
                    continue;
                }
                f = Mathf.Min(1f, f + step);
                shown[ids[i]] = f;
                alpha[i] = f;
                count++;
            }
            held.Clear();
            foreach (var id in heldNow) held.Add(id);
            return count;
        }
    }
}
