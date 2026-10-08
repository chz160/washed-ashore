using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Fish;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// Spec F4-F6 in World.unity with the shipped tuning, bodies and anchors, at 3 seeds. F4/F5: a scripted 120 s walk on
    /// the brief's shore route, wading into the shelf for 10 s each minute, with <see cref="FishChecks"/> on every frame.
    /// F6: four triggers on four fish far apart (wading, swimming, a bank walk at the lip, a swimmer over a bottom fish),
    /// each fish held to f-qa's bars: first motion away within the reaction time, 8 m from the trigger point within the
    /// time its burst and flee speeds allow, never more than 18 m from its start, no return before returnStartSeconds,
    /// and back in its own band (and near home for structure fish) by 90 s; F4/F5 checks run throughout.
    /// Results: TestResults/fish-f4f5-&lt;seed&gt;.json and fish-f6-&lt;seed&gt;.json; probe dumps alongside.
    /// </summary>
    public class FishBehaviourTests
    {
        static IEnumerable<int> Seeds() => FishTestKit.Seeds;

        [TearDown]
        public void TearDown() => FishTestKit.Restore();

        [UnityTest]
        public IEnumerator F4F5_ShoreWalk120s([ValueSource(nameof(Seeds))] int seed)
        {
            yield return FishTestKit.Load(seed, "f4f5");
            var pop = FishPopulation.Active;
            var checks = new FishChecks();
            int liveMax = 0, stuckMax = 0;
            float stuckLongest = 0f, stuckSum = 0f;
            string stuckDebug = null;
            var simNs = new List<long>();
            var eventsNs = new List<long>();
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Fish.Sim");
            using var eventsRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Fish.Events");
            for (float time = 0f; time < 120f; time += FishTestKit.Step)
            {
                FishTestKit.OnRoute(time * FishTestKit.WalkSpeed, out var feet, out var look);
                bool wade = time % 60f > 40f && time % 60f < 50f;
                var at = wade ? WadePoint(pop, feet) : feet;
                FishTestKit.Place(at, look, wade ? WaterMode.Wade : WaterMode.Dry);
                yield return null;
                liveMax = Mathf.Max(liveMax, pop.Count);
                checks.Frame(pop, time);
                int stuck = pop.World.StuckCount(out float longest);
                stuckMax = Mathf.Max(stuckMax, stuck);
                stuckSum += stuck;
                if (longest > 5f && stuckDebug == null) stuckDebug = pop.World.DebugLongestStuck();
                stuckLongest = Mathf.Max(stuckLongest, longest);
                if (recorder.Valid && recorder.LastValue > 0) simNs.Add(recorder.LastValue);
                if (eventsRecorder.Valid) eventsNs.Add(eventsRecorder.LastValue);
            }
            simNs.Sort();
            float simMedianMs = simNs.Count > 0 ? simNs[simNs.Count / 2] / 1e6f : -1f;
            eventsNs.Sort();
            float eventsMedianMs = eventsNs.Count > 0 ? eventsNs[eventsNs.Count / 2] / 1e6f : -1f;
            // Events are rare, so the median hides their cost: mean and p99 too (f-td C2 review 2).
            double eventsSum = 0;
            foreach (long ns in eventsNs) eventsSum += ns;
            float eventsMeanMs = eventsNs.Count > 0 ? (float)(eventsSum / eventsNs.Count / 1e6) : -1f;
            float eventsP99Ms = eventsNs.Count > 0 ? eventsNs[Mathf.Min(eventsNs.Count - 1, (int)(eventsNs.Count * 0.99f))] / 1e6f : -1f;
            // ruling/fish-stuck-bar-scope: the 5 s bar gates the drawable tiers (V1, V2); V3 / neverDrawBody fish are never drawn,
            // so they are reported, not gated.
            float drawStuck = 0f, drawEscape = 0f, v3Stuck = 0f, v3Escape = 0f;
            var bySpecies = new List<string>();
            for (int s = 0; s < pop.Tuning.species.Length; s++)
            {
                var sp = pop.Tuning.species[s];
                bool drawable = sp.tier != FishTier.V3 && !sp.neverDrawBody;
                float st = pop.World.StuckLongestBySpecies[s], es = pop.World.EscapeLongestBySpecies[s];
                if (drawable) { drawStuck = Mathf.Max(drawStuck, st); drawEscape = Mathf.Max(drawEscape, es); }
                else { v3Stuck = Mathf.Max(v3Stuck, st); v3Escape = Mathf.Max(v3Escape, es); }
                if (st > 0f || es > 0f)
                    bySpecies.Add($"{{\"species\":\"{sp.name}\",\"tier\":\"{sp.tier}\",\"drawable\":{(drawable ? "true" : "false")},\"stuckLongestSec\":{st.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},\"escapeLongestSec\":{es.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}}}");
            }
            var byBlock = new List<string>();
            for (int b = 1; b < pop.World.StuckLongestByBlock.Length; b++)
                byBlock.Add($"\"{(FishBlock)b}\":{{\"longestSec\":{pop.World.StuckLongestByBlock[b]:F1},\"runsOver5s\":{pop.World.StuckOver5ByBlock[b]}}}");
            float skipShare = pop.World.PlannedMembers > 0 ? pop.World.SpawnSkips / (float)pop.World.PlannedMembers : 0f;
            float corr = checks.speeds.Count > 1 ? FishTestKit.Correlation(checks.speeds, checks.rates) : 0f;
            FishTestKit.Write($"fish-f4f5-{seed}.json", $"{{\"seed\":{seed},\"brief\":\"{pop.Tuning.briefRevision}\",\"briefSha16\":\"{pop.Tuning.briefSha16}\",\"seconds\":120,\"liveMax\":{liveMax}," +
                $"\"fishSimMedianMs\":{simMedianMs:F3},\"fishEventsMedianMs\":{eventsMedianMs:F3},\"fishEventsMeanMs\":{eventsMeanMs:F4},\"fishEventsP99Ms\":{eventsP99Ms:F3},\"fishSimFrames\":{simNs.Count},\"stuckOver2sMax\":{stuckMax},\"stuckOver2sMean\":{stuckSum / Mathf.Max(1, checks.frames):F3},\"stuckLongestSec\":{stuckLongest:F1},\"stuckByBlocker\":{{{string.Join(",", byBlock)}}}," +
                $"\"drawableStuckLongestSec\":{drawStuck:F2},\"drawableEscapeLongestSec\":{drawEscape:F2},\"v3StuckLongestSec\":{v3Stuck:F2},\"v3EscapeLongestSec\":{v3Escape:F2}," +
                $"\"stuckBySpecies\":[{string.Join(",", bySpecies)}]," +
                $"\"escapeLongestSec\":{pop.World.EscapeLongestSec:F2},\"escapeLongestNetM\":{pop.World.EscapeLongestNetM:F2},\"escapeEpisodes\":{pop.World.EscapeEpisodes}," +
                $"\"escapeWorstNetOver1sM\":{(pop.World.EscapeWorstNetOver1sM == float.MaxValue ? "null" : pop.World.EscapeWorstNetOver1sM.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))}," +
                $"\"slotFallbacks\":{pop.World.SlotFallbacks},\"plannedMembers\":{pop.World.PlannedMembers},\"spawnSkips\":{pop.World.SpawnSkips},\"spawnSkipShare\":{skipShare:F4},{checks.Json()}}}");

            Assert.Greater(checks.samples, 1000, "too few fish along the route to test F4");
            Assert.AreEqual(0, checks.violations.Count, checks.violations.Count > 0 ? checks.violations[0] : "");
            Assert.LessOrEqual(checks.worstTurn, pop.Tuning.maxTurnPerFrame + 1e-3f, "F5 rendered turn per frame");
            Assert.GreaterOrEqual(checks.SpeedCv(), 0.3f, "F5: too little speed variation for the correlation to mean anything");
            Assert.GreaterOrEqual(corr, 0.8f, "F5 clip rate vs rendered swim speed");
            Assert.GreaterOrEqual(checks.RateRuleShare, 0.95f, "F5 clip rate follows the brief's rule within 2%");
            Assert.GreaterOrEqual(checks.worstSpacing, 1f - 1e-3f, "F5 groupmates closer than the brief's minimum spacing");
            Assert.AreEqual(0, checks.boxOverlaps, "F5 two fish bodies overlap");
            Assert.LessOrEqual(checks.headingMisses, checks.movingSamples / 100, "F5/AF3 fish sliding sideways or backwards");
            Assert.LessOrEqual(drawStuck, 5f, "f-td bar (drawable tiers): no V1/V2 fish frozen for over 5 s; see stuckBySpecies. " + stuckDebug);
            Assert.LessOrEqual(drawEscape, 5f, "f-td bar (drawable tiers): no V1/V2 fish rocking in a dead end for over 5 s");
        }

        /// <summary>A wet point a few metres out from the feet, shallow enough to wade (the bed above the swim depth).</summary>
        static Vector3 WadePoint(FishPopulation pop, Vector3 feet)
        {
            var w = pop.Water;
            for (float r = 2f; r <= 12f; r += 1f)
                for (int a = 0; a < 16; a++)
                {
                    float ang = a * Mathf.PI / 8f;
                    var p = feet + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    if (TerrainQuery.TryGroundHeight(p, out float g) && g < w.WaterLevelY && w.WaterLevelY - g < 0.9f) { p.y = g; return p; }
                }
            return feet;
        }

        class Tracked
        {
            public string trigger;
            public long id;
            public int group;
            public float t0, burst, budget, firstAway = -1f, reached8 = -1f, maxFromStart, minAfterReach = float.MaxValue;
            public Vector3 trigger3, start;
            public bool done;
            public float lastDist = -1f, distAtAway = -1f;
            public bool awayConfirmed;
            public bool backInBand, nearHome;
            public float stuckMax;
            public int missingFrames;
            public string end = "null";   // band, group band, distance home and mode at the 90 s check
            public FishBlock stuckBlock;
        }

        [UnityTest]
        public IEnumerator F6_FourTriggers_EachFishFleesAndSettles([ValueSource(nameof(Seeds))] int seed)
        {
            yield return FishTestKit.Load(seed, "f6");
            var pop = FishPopulation.Active;
            var t = pop.Tuning;
            var sc = t.scatter;
            var checks = new FishChecks();
            float time = 0f;
            // Walk at least 150 m so a good stretch of shore is live, and on until a shelf fish sits within the bank-walk
            // trigger of a lip spot (they're scarce: the shelf's shallowest metre holds none), then park there.
            float walked = 0f;
            var none = new List<Vector3>();
            for (; walked < 150f || (walked < FishTestKit.RouteLength() - 50f && !PickTrigger(pop, "bank", none, out _, out _, out _));
                 time += FishTestKit.Step, walked = time * FishTestKit.WalkSpeed)
            {
                FishTestKit.OnRoute(walked, out var f0, out var l0);
                FishTestKit.Place(f0, l0, WaterMode.Dry);
                yield return null;
                checks.Frame(pop, time);
            }
            string bankSearch = BankSearch(pop, walked);
            FishTestKit.OnRoute(walked, out var park, out var look);
            // Park a little inland, so the parked player is never inside a trigger or near the lip.
            park -= look * Vector3.forward * 8f;
            if (TerrainQuery.TryGroundHeight(park, out float parkY)) park.y = parkY;

            var tracked = new List<Tracked>();
            var used = new List<Vector3>();
            var missing = new List<string>();
            // The bank trigger first: its shelf fish are scarce, and the walk above stopped where one exists; then the
            // scarce shallow bottom fish. One trigger at a time, each followed for 90 s with the player parked on dry
            // ground near it, so the fish stays in the live set (parking far away despawned and respawned it).
            foreach (var kind in new[] { "bank", "bottom", "wade", "swim" })
            {
                // Walk on along the route until a fish for this trigger is live (bottom fish within a swimmer's reach are scarce).
                for (; walked < FishTestKit.RouteLength() - 10f && !PickTrigger(pop, kind, used, out _, out _, out _);
                     time += FishTestKit.Step, walked += FishTestKit.Step * FishTestKit.WalkSpeed)
                {
                    FishTestKit.OnRoute(walked, out var f1, out look);
                    park = f1;
                    FishTestKit.Place(f1, look, WaterMode.Dry);
                    yield return null;
                    checks.Frame(pop, time);
                }
                if (!PickTrigger(pop, kind, used, out int idx, out Vector3 at, out WaterMode mode))
                {
                    missing.Add($"{{\"trigger\":\"{kind}\",\"liveBottomFish\":{BottomStats(pop, out float shallowest)},\"shallowestBottomBelowSurfaceM\":{(shallowest == float.MaxValue ? "null" : shallowest.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))}}}");
                    Debug.LogWarning($"F6 seed {seed}: no fish for the {kind} trigger");
                    continue;
                }
                var s = pop.States[idx];
                used.Add(s.position);
                var sp = t.species[s.speciesIndex];
                var tr = new Tracked { trigger = kind, id = s.fishId, group = s.group, t0 = time, trigger3 = at, start = s.position, burst = sp.burstSpeed };
                tr.budget = sc.reactionSeconds.y + sc.burstSeconds.x + Mathf.Max(0f, sc.fleeDistance.x - sp.burstSpeed * sc.burstSeconds.x) / sc.fleeCruiseSpeed;
                tracked.Add(tr);
                // The player is there for half a second, then parks on dry ground near it (clear of every trigger).
                for (int k = 0; k < 30; k++)
                {
                    FishTestKit.Place(at, look, mode);
                    yield return null;
                    time += FishTestKit.Step;
                    Track(pop, tracked, time);
                    checks.Frame(pop, time);
                }
                // No dry ground near (mid-river): wait in the water 15 m beyond the trigger spot, on the far side from the fish.
                var nearMode = WaterMode.Dry;
                if (!NearPark(pop.Water, at, sc.bankWalkLipDistance, out var near))
                {
                    var away = at - s.position;
                    away.y = 0f;
                    near = at + (away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward) * 15f;
                    float dn = Depth(pop.Water, near);
                    nearMode = dn >= 1.35f ? WaterMode.Swim : dn > 0f ? WaterMode.Wade : WaterMode.Dry;
                    near.y = nearMode == WaterMode.Swim ? pop.Water.WaterLevelY - 0.3f : near.y;
                }
                float end = tr.t0 + 90f;
                for (; time < end; time += FishTestKit.Step)
                {
                    FishTestKit.Place(near, look, nearMode);
                    yield return null;
                    Track(pop, tracked, time);
                    checks.Frame(pop, time);
                }
                // Final band check at 90 s after its trigger.
                for (int i = 0; i < pop.Count; i++)
                {
                    if (pop.States[i].fishId != tr.id) continue;
                    var fs = pop.States[i];
                    tr.backInBand = FishPlan.BandAt(t, pop.Water, fs.position.x, fs.position.z, out _) == pop.World.GroupBand(fs.group);
                    tr.nearHome = pop.World.GroupStructure(fs.group) < 0 || FishTestKit.Flat(fs.position, pop.World.GroupHome(fs.group)) <= t.species[fs.speciesIndex].homeRadius;
                    float homeD = FishTestKit.Flat(fs.position, pop.World.GroupHome(fs.group));
                    tr.end = $"{{\"band\":{FishPlan.BandAt(t, pop.Water, fs.position.x, fs.position.z, out float endDepth)},\"groupBand\":{pop.World.GroupBand(fs.group)}," +
                             $"\"bedDepthM\":{endDepth.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)},\"homeM\":{homeD.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},\"mode\":\"{fs.mode}\"}}";
                }
                tr.done = true;
            }
            Assert.Greater(tracked.Count, 0, "no fish found for any trigger");
            var failures = new List<string>();
            var sb = new StringBuilder($"{{\"seed\":{seed},\"brief\":\"{t.briefRevision}\",\"fish\":[");
            foreach (var tr in tracked)
            {
                if (tr.missingFrames > 0) failures.Add($"{tr.trigger} {tr.id}: left the live set for {tr.missingFrames} frames");
                string tag = $"{tr.trigger} {tr.id}";
                if (!tr.awayConfirmed || tr.firstAway - tr.t0 > sc.reactionSeconds.y + FishTestKit.Step) failures.Add($"{tag}: first motion away at {tr.firstAway - tr.t0:F2} s (confirmed {tr.awayConfirmed})");
                if (tr.reached8 < 0f || tr.reached8 - tr.t0 > tr.budget) failures.Add($"{tag}: 8 m from the trigger at {(tr.reached8 < 0 ? -1 : tr.reached8 - tr.t0):F1} s (budget {tr.budget:F1} s)");
                if (tr.maxFromStart > 18f) failures.Add($"{tag}: {tr.maxFromStart:F1} m from its start");
                if (tr.minAfterReach < sc.fleeDistance.x - 0.5f) failures.Add($"{tag}: came back to {tr.minAfterReach:F1} m before {sc.returnStartSeconds} s");
                if (!tr.backInBand || !tr.nearHome) failures.Add($"{tag}: not back in its band/home by 90 s");
                sb.Append(tr == tracked[0] ? "" : ",").Append($"{{\"trigger\":\"{tr.trigger}\",\"id\":{tr.id},\"firstAwaySec\":{tr.firstAway - tr.t0:F2},\"reached8mSec\":{(tr.reached8 < 0 ? -1 : tr.reached8 - tr.t0):F1},")
                  .Append($"\"budgetSec\":{tr.budget:F1},\"maxFromStartM\":{tr.maxFromStart:F1},\"minBeforeReturnM\":{(tr.minAfterReach == float.MaxValue ? -1 : tr.minAfterReach):F1},\"backInBand\":{(tr.backInBand ? "true" : "false")},\"nearHome\":{(tr.nearHome ? "true" : "false")},")
                  .Append($"\"stuckMaxSec\":{tr.stuckMax:F2},\"stuckBlocker\":\"{tr.stuckBlock}\",\"missingFrames\":{tr.missingFrames},\"at90s\":{tr.end}}}");
            }
            sb.Append("],\"noCandidate\":[").Append(string.Join(",", missing)).Append("],\"bankSearch\":").Append(bankSearch).Append(',').Append(checks.Json()).Append('}');
            FishTestKit.Write($"fish-f6-{seed}.json", sb.ToString());
            Assert.AreEqual(4, tracked.Count, "a trigger found no fish");
            Assert.IsEmpty(failures, string.Join("\n", failures));
            Assert.AreEqual(0, checks.violations.Count, checks.violations.Count > 0 ? checks.violations[0] : "");
            Assert.AreEqual(0, checks.boxOverlaps, "F5 body overlap during the scatter");
        }

        static void Track(FishPopulation pop, List<Tracked> tracked, float time)
        {
            var sc = pop.Tuning.scatter;
            foreach (var tr in tracked)
            {
                if (tr.done) continue;
                bool found = false;
                for (int i = 0; i < pop.Count; i++)
                {
                    if (pop.States[i].fishId != tr.id) continue;
                    found = true;
                    var p = pop.States[i].position;
                    float d = FishTestKit.Flat(p, tr.trigger3);
                    // First motion away: the first frame the distance grows, confirmed by >= 0.1 m net within the next 0.5 s
                    // (f-qa guard against jitter); an unconfirmed start is discarded and the search goes on.
                    if (tr.firstAway >= 0f && !tr.awayConfirmed)
                    {
                        if (d >= tr.distAtAway + 0.1f) tr.awayConfirmed = true;
                        else if (time - tr.firstAway > 0.5f) tr.firstAway = -1f;
                    }
                    if (tr.lastDist >= 0f && d > tr.lastDist + 1e-4f && tr.firstAway < 0f) { tr.firstAway = time; tr.distAtAway = tr.lastDist; }
                    tr.lastDist = d;
                    if (pop.World.StuckOf(tr.id, out float stuck, out var block) && stuck > tr.stuckMax) { tr.stuckMax = stuck; tr.stuckBlock = block; }
                    if (d >= sc.fleeDistance.x && tr.reached8 < 0f) tr.reached8 = time;
                    tr.maxFromStart = Mathf.Max(tr.maxFromStart, FishTestKit.Flat(p, tr.start));
                    if (tr.reached8 >= 0f && time - tr.t0 < sc.returnStartSeconds) tr.minAfterReach = Mathf.Min(tr.minAfterReach, d);
                }
                if (!found) tr.missingFrames++;
            }
        }

        /// <summary>Live bottom fish (not in a surface event) and the shallowest one's depth below the surface.</summary>
        static int BottomStats(FishPopulation pop, out float shallowest)
        {
            int n = 0;
            shallowest = float.MaxValue;
            for (int i = 0; i < pop.Count; i++)
            {
                var s = pop.States[i];
                if (s.inSurfaceEvent || !pop.Tuning.species[s.speciesIndex].bottom) continue;
                n++;
                shallowest = Mathf.Min(shallowest, FishTestKit.Surface(s.position) - s.position.y);
            }
            return n;
        }

        /// <summary>
        /// Dry ground 10-44 m from the trigger spot (inside the live radius with the flee added) and clear of the lip (lip + 1.5 m from water), so the parked player
        /// triggers nothing and the tracked fish stays live; false if there is none.
        /// </summary>
        static bool NearPark(IFishWater w, Vector3 at, float lip, out Vector3 park)
        {
            for (float r = 10f; r <= 44f; r += 2f)
                for (int a = 0; a < 24; a++)
                {
                    var p = at + new Vector3(Mathf.Cos(a * Mathf.PI / 12f), 0f, Mathf.Sin(a * Mathf.PI / 12f)) * r;
                    if (Depth(w, p) > 0f || NearWater(w, p, lip + 1.5f)) continue;
                    if (TerrainQuery.TryGroundHeight(p, out float g)) p.y = g;
                    park = p;
                    return true;
                }
            park = at;
            return false;
        }

        /// <summary>A live fish for a trigger kind, at least 25 m from earlier targets, and where the player stands for it.</summary>
        static bool PickTrigger(FishPopulation pop, string kind, List<Vector3> used, out int idx, out Vector3 at, out WaterMode mode)
        {
            var t = pop.Tuning;
            var w = pop.Water;
            var sc = t.scatter;
            idx = -1; at = default; mode = WaterMode.Dry;
            for (int i = 0; i < pop.Count; i++)
            {
                var s = pop.States[i];
                var sp = t.species[s.speciesIndex];
                if (s.inSurfaceEvent || (kind == "bottom") != sp.bottom) continue;
                bool far = true;
                foreach (var u in used) far &= FishTestKit.Flat(u, s.position) > 25f;
                if (!far) continue;
                float surface = FishTestKit.Surface(s.position);
                switch (kind)
                {
                    case "bottom":
                        // A swimmer at the surface right above it, inside the 3D trigger.
                        if (surface - s.position.y > sc.bottomTriggerSwim3D * 0.8f) continue;
                        at = new Vector3(s.position.x, surface - 0.3f, s.position.z);
                        mode = WaterMode.Swim;
                        break;
                    case "swim":
                        if (!Spot(w, s.position, 0.6f * sc.triggerSwimming, p => Depth(w, p) >= 1.2f, out at)) continue;
                        mode = WaterMode.Swim;
                        break;
                    case "wade":
                        if (!Spot(w, s.position, 0.7f * sc.triggerWading, p => Depth(w, p) > 0.2f && Depth(w, p) < 0.9f, out at)) continue;
                        mode = WaterMode.Wade;
                        break;
                    default: // bank (f-qa's method): the player moves to the lip point nearest the shelf fish, dry, within
                             // the bank-walk trigger of it and the lip distance of the water; no fish is ever moved
                        if (Depth(w, s.position) >= t.bands[0].bedDepth.y) continue;
                        if (!Spot(w, s.position, 0.95f * sc.triggerBankWalk, p => Depth(w, p) <= 0f && NearWater(w, p, sc.bankWalkLipDistance), out at, true)) continue;
                        mode = WaterMode.Dry;
                        break;
                }
                idx = i;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Can a bank-walk trigger ever reach a shelf fish here (f-td C2 review)? For every live non-bottom fish over the
        /// shelf: the horizontal distance to the nearest dry ground within the lip distance of water (24-point rings,
        /// 0.25 m steps to 8 m), against the 2.5 m trigger radius.
        /// </summary>
        static string BankSearch(FishPopulation pop, float walked)
        {
            var t = pop.Tuning;
            var w = pop.Water;
            int shelf = 0, within = 0;
            float nearest = float.MaxValue;
            for (int i = 0; i < pop.Count; i++)
            {
                var s = pop.States[i];
                if (s.inSurfaceEvent || t.species[s.speciesIndex].bottom || Depth(w, s.position) >= t.bands[0].bedDepth.y) continue;
                shelf++;
                float d = -1f;
                for (float r = 0.25f; r <= 8f && d < 0f; r += 0.25f)
                    for (int a = 0; a < 24 && d < 0f; a++)
                    {
                        var p = s.position + new Vector3(Mathf.Cos(a * Mathf.PI / 12f), 0f, Mathf.Sin(a * Mathf.PI / 12f)) * r;
                        if (Depth(w, p) <= 0f && NearWater(w, p, t.scatter.bankWalkLipDistance)) d = r;
                    }
                if (d < 0f) continue;
                nearest = Mathf.Min(nearest, d);
                if (d < t.scatter.triggerBankWalk) within++;
            }
            string near = nearest == float.MaxValue ? "null" : nearest.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            return $"{{\"walkedM\":{walked:F0},\"routeM\":{FishTestKit.RouteLength():F0},\"liveShelfFish\":{shelf},\"nearestDryM\":{near},\"withinTrigger\":{within}}}";
        }

        /// <summary>The population's own bank-walk test: water within <paramref name="lip"/> on an 8-point ring or at the feet.</summary>
        static bool NearWater(IFishWater w, Vector3 p, float lip)
        {
            for (int i = 0; i <= 8; i++)
            {
                float a = i * Mathf.PI * 0.25f, r = i == 8 ? 0f : lip;
                if (Depth(w, p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r)) > 0f) return true;
            }
            return false;
        }

        static float Depth(IFishWater w, Vector3 p) => TerrainQuery.TryGroundHeight(p, out float g) ? w.WaterLevelY - g : -99f;

        static bool Spot(IFishWater w, Vector3 around, float within, System.Func<Vector3, bool> ok, out Vector3 at, bool nearestFirst = false)
        {
            // Farthest ring first (out of the fish's way) unless the nearest point is wanted (the bank trigger).
            int rings = Mathf.FloorToInt((within - 0.5f) / 0.5f) + 1;
            for (int ring = 0; ring < rings; ring++)
            {
                float r = nearestFirst ? 0.5f + ring * 0.5f : within - ring * 0.5f;
                for (int a = 0; a < 24; a++)
                {
                    float ang = a * Mathf.PI / 12f;
                    var p = around + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    if (!ok(p)) continue;
                    p.y = TerrainQuery.TryGroundHeight(p, out float g) ? g : p.y;
                    at = p;
                    return true;
                }
            }
            at = default;
            return false;
        }
    }
}
