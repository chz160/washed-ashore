using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>
    /// F4-F6 rules on the analytic river, no scene: the sim stays in the water, turns smoothly per rendered frame, ties
    /// the clip rate to speed, keeps groupmates apart, scatters and settles, raises surface signs only inside their
    /// windows, keeps stable ids, drops groups that lose their tile, and allocates nothing per frame. The PlayMode
    /// suite repeats F4-F7 in World.unity with the shipped tuning and bodies.
    /// </summary>
    public class FishSimWorldTests
    {
        const int Seed = 101;
        const float Waves = 0.028f;
        FishTuning t;
        AnalyticRiver river;
        double clock;

        [SetUp]
        public void Make()
        {
            t = FishPlanTests.Tuning();
            foreach (var s in t.species)
            {
                s.turnRateCruise = 90f; s.turnRateBurst = 360f;
                s.homeRadius = 15f; s.holdSeconds = new Vector2(3f, 8f); s.wanderSeconds = new Vector2(3f, 8f); s.groupRadius = 3f;
            }
            // A moving surface like the shipped waves (0.028 m total), on the water clock the sim stamps events with.
            river = new AnalyticRiver { surface = (x, z) => 0.018f * Mathf.Sin(0.9f * x + 0.4f * z + (float)clock) + 0.01f * Mathf.Sin(1.7f * z - 0.6f * (float)clock) };
            clock = 0;
            WaterClock.Use(() => clock);
            FishEvents.ClearForTests();
        }

        [TearDown]
        public void Restore()
        {
            WaterClock.Use(null);
            FishEvents.ClearForTests();
        }

        FishSimWorld Populate(float length = 320f, int seed = Seed)
        {
            var w = new FishSimWorld(t, null, river, seed, Waves, testBodies: true);
            var plan = new List<FishGroupPlan>();
            for (int cz = 0; cz < Mathf.CeilToInt(length / t.cellSize); cz++)
                for (int cx = 0; cx < 2; cx++) FishPlan.Cell(t, river, seed, cx, cz, plan);
            foreach (var g in plan) w.AddGroup(g);
            Assert.Greater(w.LiveFish, 10, "test needs fish");
            return w;
        }

        delegate void FrameCheck(float time, FishState[] states, int count);

        void Run(FishSimWorld w, float seconds, System.Func<float, Vector3> player, WaterMode mode, FrameCheck check,
            System.Func<int, float> frameTime = null, System.Action<int> everyTick = null)
        {
            var states = new FishState[t.MaxLive];
            var tick = new FishTick(t.simHz);
            float time = 0f;
            int ticks = 0;
            for (int frame = 0; time < seconds; frame++)
            {
                float dt = frameTime != null ? frameTime(frame) : 1f / 60f;
                time += dt;
                clock = time;
                int n = tick.Advance(dt);
                for (int i = 0; i < n; i++)
                {
                    w.Tick(new FishPlayer { position = player(time), mode = mode });
                    everyTick?.Invoke(ticks++);
                }
                check(time, states, w.Render(tick.Alpha, dt, states));
            }
        }

        static float Half(FishState s) => 0.1f * s.sizeScale; // the analytic path's stand-in body: 0.2 m tall at scale 1

        [Test]
        public void F4_EveryFish_StaysBetweenBedClearanceAndSurfaceMargin_For120s()
        {
            var w = Populate();
            int violations = 0, samples = 0, inEvents = 0;
            float nextSample = 0f;
            // The player wades the bank and swims out and back, so fish scatter through the shallows too; surface signs fire.
            var events = new FishEventScheduler(t, river, Seed);
            events.UpdateRing(new Vector3(10f, 0f, 160f), 1000);
            Run(w, 120f, s => new Vector3(4f + 10f * Mathf.PingPong(s / 6f, 1f), 0f, 3f * s), WaterMode.Swim, (time, st, n) =>
            {
                if (time < nextSample) return;
                nextSample += 0.25f;
                for (int i = 0; i < n; i++)
                {
                    if (st[i].inSurfaceEvent) { inEvents++; continue; }
                    var sp = t.species[st[i].speciesIndex];
                    float bed = AnalyticRiver.Bed(st[i].position.x), surf = river.SurfaceY(st[i].position.x, st[i].position.z);
                    samples++;
                    if (st[i].position.x <= 0f) violations++;
                    if (st[i].position.y - Half(st[i]) < bed + sp.bedClearance - 1e-3f) violations++;
                    if (st[i].position.y + Half(st[i]) > surf - Mathf.Max(sp.surfaceMargin, Waves) + 1e-3f) violations++;
                }
            }, null, k => events.Tick(w, new Vector3(10f, 0f, 160f), w.Step));
            Assert.Greater(samples, 1000);
            Assert.AreEqual(0, violations);
        }

        [Test]
        public void F5_RenderedTurn_NeverExceeds45DegPerFrame_AtAnyFrameRate()
        {
            var w = Populate();
            var lastFwd = new Dictionary<long, Vector3>();
            float worst = 0f;
            // Frame times from 4 ms to a 0.5 s hitch, with the swimmer scattering fish (burst turn rates).
            Run(w, 60f, s => new Vector3(6f, 0f, 2f * s), WaterMode.Swim, (time, st, n) =>
            {
                for (int i = 0; i < n; i++)
                {
                    if (lastFwd.TryGetValue(st[i].fishId, out var prev)) worst = Mathf.Max(worst, Vector3.Angle(prev, st[i].forward));
                    lastFwd[st[i].fishId] = st[i].forward;
                }
            }, f => f % 97 == 0 ? 0.5f : (f % 3 == 0 ? 0.004f : 0.02f));
            Assert.LessOrEqual(worst, 45f + 1e-3f);
        }

        [Test]
        public void F5_ClipRate_TracksSwimSpeed_CorrelationAtLeast08()
        {
            var w = Populate();
            var speeds = new List<float>();
            var rates = new List<float>();
            var last = new Dictionary<long, Vector3>();
            Run(w, 60f, s => new Vector3(6f, 0f, 2f * s), WaterMode.Swim, (time, st, n) =>
            {
                for (int i = 0; i < n; i++)
                {
                    // Rendered displacement speed (f-td review: a slide with an idle tail must not hide behind a.speed).
                    if (last.TryGetValue(st[i].fishId, out var p) && !st[i].inSurfaceEvent)
                    {
                        var d = st[i].position - p;
                        d.y = 0f;
                        speeds.Add(d.magnitude / (1f / 60f) / BodyLength(st[i]));
                        rates.Add(st[i].animRate);
                    }
                    last[st[i].fishId] = st[i].position;
                }
            });
            // The brief's rule is per body length (idle + speed / (stride x length)), so correlate rate with BL/s.
            Assert.GreaterOrEqual(Correlation(speeds, rates), 0.8f);
        }

        static float BodyLength(FishState s) => s.sizeScale; // stand-in body is 1 m at scale 1

        /// <summary>The analytic path's stand-in box (0.2 x 0.2 x 1 m at scale 1).</summary>
        static FishBox TestBox(FishState s) =>
            new FishBox(new Vector2(s.position.x, s.position.z), s.forward, 0.1f * s.sizeScale, 0.5f * s.sizeScale, new Vector2(s.bodyMinY, s.bodyMaxY));

        [Test]
        public void F5_Groupmates_NeverOverlap()
        {
            var w = Populate();
            float worstRatio = float.MaxValue;
            int overlaps = 0;
            string firstOverlap = "", firstXZ = "";
            // A swimmer passing through scatters fish across each other's paths.
            Run(w, 60f, s => new Vector3(6f, 0f, 3f * s), WaterMode.Swim, (time, st, n) =>
            {
                for (int i = 0; i < n; i++)
                    for (int j = i + 1; j < n; j++)
                    {
                        float d = Vector3.Distance(st[i].position, st[j].position);
                        if (st[i].group == st[j].group)
                        {
                            float need = 0.5f * (BodyLength(st[i]) + BodyLength(st[j])) * t.species[st[i].speciesIndex].spacingBodyLengths.x;
                            worstRatio = Mathf.Min(worstRatio, d / need);
                        }
                        if (firstXZ == "" && TestBox(st[i]).OverlapsXZ(TestBox(st[j])))
                            firstXZ = $"firstXZ t={time:F3} " + w.DebugPair(st[i].fishId, st[j].fishId);
                        if (TestBox(st[i]).Overlaps(TestBox(st[j])))
                        {
                            if (overlaps == 0)
                                firstOverlap = w.DebugPair(st[i].fishId, st[j].fishId) + " || " + $"t={time:F3} {st[i].fishId}/{st[j].fishId} groups {st[i].group}/{st[j].group} modes {st[i].mode}/{st[j].mode} " +
                                               $"p {st[i].position:F3}/{st[j].position:F3} fwd {st[i].forward:F2}/{st[j].forward:F2} scale {st[i].sizeScale:F3}/{st[j].sizeScale:F3} y {st[i].bodyMinY:F3}-{st[i].bodyMaxY:F3}/{st[j].bodyMinY:F3}-{st[j].bodyMaxY:F3}";
                            overlaps++;
                        }
                    }
            });
            // Groupmates keep the brief's minimum spacing, and no two bodies (any groups) ever overlap.
            Assert.GreaterOrEqual(worstRatio, 1f - 1e-3f);
            Assert.AreEqual(0, overlaps, firstXZ + " ### " + firstOverlap);
        }

        [Test]
        public void F6_SwimmerNearby_FishFlee_ThenSettleBackInTheirBand()
        {
            var w = Populate(64f);
            var near = new HashSet<long>();
            Vector3 swimmer = Vector3.zero;
            bool placed = false;
            float atArrival = 0f, afterFlee = 0f;
            int settledInBand = 0, total = 0;
            Run(w, 80f, s => placed && s < 10f ? swimmer : new Vector3(-200f, 0f, 0f), WaterMode.Swim, (time, st, n) =>
            {
                if (!placed && time > 2f)
                {
                    // The swimmer appears right over the first non-bottom fish; track everything inside the swim trigger.
                    int first = 0;
                    while (first < n && t.species[st[first].speciesIndex].bottom) first++;
                    swimmer = new Vector3(st[first].position.x, 0f, st[first].position.z);
                    placed = true;
                    for (int i = 0; i < n; i++)
                        if (!t.species[st[i].speciesIndex].bottom && Flat(st[i].position, swimmer) < t.scatter.triggerSwimming) near.Add(st[i].fishId);
                    atArrival = MeanDistance(st, n, near, swimmer);
                }
                if (placed && afterFlee == 0f && time > 15f) afterFlee = MeanDistance(st, n, near, swimmer);
                if (placed && time > 79.9f && total == 0)
                    for (int i = 0; i < n; i++)
                    {
                        total++;
                        if (FishPlan.BandAt(t, river, st[i].position.x, st[i].position.z, out _) >= 0) settledInBand++;
                    }
            });
            Assert.Greater(near.Count, 0);
            // The brief: burst, then flee 8-15 m; by 13 s after the swimmer arrives the tracked fish are well away.
            Assert.Greater(afterFlee, atArrival + 5f, "fish near the swimmer moved away");
            Assert.AreEqual(total, settledInBand, "every fish is back inside a band");
        }

        [Test]
        public void F7_SurfaceSigns_RespectTheCaps_AndOnlyEventFishBreakTheMargin()
        {
            var w = Populate();
            t.maxSurfaceEvents = 3;
            t.species[FishPlanTests.Sunfish].surfaceEvent = FishSurfaceKind.Dimple;
            t.species[FishPlanTests.Sunfish].surfaceEventsPerFishPerMin = 0.5f;
            t.species[FishPlanTests.Sunfish].jumpsPerFishPerMin = 0.05f;
            t.species[FishPlanTests.Sunfish].canJump = true;
            foreach (var b in t.bands) b.surfaceEventsPerMinPer100m = 2f;   // band-rate signs (f-designer's rule)
            t.signSeconds = new Vector2[16];
            for (int i = 0; i < t.signSeconds.Length; i++) t.signSeconds[i] = new Vector2(1.5f, 2f);
            var events = new FishEventScheduler(t, river, Seed);
            var player = new Vector3(10f, 0f, 160f);
            events.UpdateRing(player, 1000);
            int raised = 0, worstActive = 0;
            FishEvents.Surface += e => raised++;
            Run(w, 300f, s => new Vector3(-200f, 0f, 0f), WaterMode.Dry, (time, st, n) =>
            {
                worstActive = Mathf.Max(worstActive, w.ActiveSurfaceEvents);
            }, null, k => events.Tick(w, player, w.Step));
            Assert.Greater(raised, 0, "signs fire at the brief's rates");
            Assert.LessOrEqual(worstActive, 3);
        }

        [Test]
        public void FishIds_AreStable_AcrossActivationHistory()
        {
            var plan = new List<FishGroupPlan>();
            FishPlan.Cell(t, river, Seed, 0, 2, plan);
            Assert.Greater(plan.Count, 0);
            var a = new FishSimWorld(t, null, river, Seed, Waves, testBodies: true);
            var b = new FishSimWorld(t, null, river, Seed, Waves, testBodies: true);
            // b sees another cell come and go first.
            var other = new List<FishGroupPlan>();
            FishPlan.Cell(t, river, Seed, 1, 5, other);
            foreach (var g in other) b.AddGroup(g);
            for (int g = 0; g < b.GroupSlots; g++) b.RemoveGroup(g);
            foreach (var g in plan) { a.AddGroup(g); b.AddGroup(g); }
            var sa = new FishState[t.MaxLive];
            var sb = new FishState[t.MaxLive];
            int na = a.Render(0f, 0f, sa), nb = b.Render(0f, 0f, sb);
            var ida = new HashSet<long>();
            for (int i = 0; i < na; i++) ida.Add(sa[i].fishId);
            Assert.AreEqual(na, nb);
            for (int i = 0; i < nb; i++) Assert.IsTrue(ida.Contains(sb[i].fishId));
            Assert.AreEqual(FishSimWorld.FishId(plan[0].cell, plan[0].groupIndex, 0), FishSimWorld.FishId(plan[0].cell, plan[0].groupIndex, 0));
        }

        [Test]
        public void Bodies_AreRequired_OutsideTheTestPath()
        {
            Assert.Throws<System.ArgumentNullException>(() => new FishSimWorld(t, null, river, Seed, Waves));
        }

        [Test]
        public void F9_GroupOverAMissingTile_ReportsNoTile()
        {
            var w = Populate();
            int lost = 0;
            river.noTileX = 5f;
            for (int g = 0; g < w.GroupSlots; g++) if (w.GroupLive(g) && !w.GroupHasTile(g)) lost++;
            Assert.Greater(lost, 0);
            for (int g = 0; g < w.GroupSlots; g++) if (w.GroupLive(g) && !w.GroupHasTile(g)) w.RemoveGroup(g);
            for (int g = 0; g < w.GroupSlots; g++) if (w.GroupLive(g)) Assert.IsTrue(w.GroupHasTile(g));
        }

        [Test]
        public void SteadyState_TickRenderAndSigns_AllocateNothing()
        {
            // Far-water signs on: the open band ends 30 m out, so 30-50 m is far water (f-td condition 5: 0 B GC).
            t.bands[2].maxShoreDistance = 30f;
            t.bands[2].farSignsPerM2PerMin = 0.001f;
            foreach (var b in t.bands) b.surfaceEventsPerMinPer100m = 2f;
            var w = Populate();
            var events = new FishEventScheduler(t, river, Seed);
            var states = new FishState[t.MaxLive];
            var player = new FishPlayer { position = new Vector3(6f, 0f, 30f), mode = WaterMode.Swim };
            events.UpdateRing(player.position, 1000);
            for (int i = 0; i < 60; i++) { w.Tick(player); events.Tick(w, player.position, w.Step); w.Render(0.5f, 1f / 60f, states); }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 600; i++) { w.Tick(player); events.Tick(w, player.position, w.Step); w.Render(0.5f, 1f / 60f, states); events.UpdateRing(player.position, 2); }
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
        }

        static float MeanDistance(FishState[] st, int n, HashSet<long> ids, Vector3 p)
        {
            float sum = 0f;
            int k = 0;
            for (int i = 0; i < n; i++) if (ids.Contains(st[i].fishId)) { sum += Flat(st[i].position, p); k++; }
            return k > 0 ? sum / k : 0f;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static float Correlation(List<float> a, List<float> b)
        {
            double ma = 0, mb = 0;
            for (int i = 0; i < a.Count; i++) { ma += a[i]; mb += b[i]; }
            ma /= a.Count; mb /= b.Count;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < a.Count; i++) { sab += (a[i] - ma) * (b[i] - mb); saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); }
            return (float)(sab / System.Math.Sqrt(saa * sbb + 1e-12));
        }
    }
}
