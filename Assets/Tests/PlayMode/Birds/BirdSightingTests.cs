using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Birds;
using WashedAshore.Tests.Wildlife;
using WashedAshore.Wildlife;
using static WashedAshore.Tests.Birds.BirdTestKit;

namespace WashedAshore.Tests.Birds
{
    /// <summary>
    /// B6: the wildlife harness's scripted 3-minute walk, unchanged (route, walker, scan, 16:9 camera, 360 samples at
    /// 0.5 s from grounded + 1 s), once per seed, with the bird visibility and metrics of bird-density-brief.md 3.4.
    /// Writes TestResults/bird-sightings.json (3.5 fields, merged by seed). Gating seeds 101/202/303 assert every band;
    /// robustness seeds 404-808 are recorded under "robustness" and never assert.
    /// </summary>
    public class BirdSightingTests
    {
        const float TimeScale = 3f; // game time is sampled; this only shortens the wall clock
        const float CheckWindow = 0.1f;
        static readonly int[] Seeds = { 101, 202, 303 };
        static readonly int[] RobustnessSeeds = { 404, 505, 606, 707, 808 };
        static readonly List<BirdSightingRun> Runs = new List<BirdSightingRun>(), Robustness = new List<BirdSightingRun>();

        [SetUp]
        public void SetUp() => WildlifeTestKit.PinFrameStep();

        [TearDown]
        public void TearDown()
        {
            Reset();
            WildlifeTestKit.UnpinFrameStep();
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator B6_BirdSightingWalkLandsInsideTheBriefBands([ValueSource(nameof(Seeds))] int seed)
        {
            var holder = new BirdSightingRun[1];
            yield return Walk(seed, true, holder);
            var run = holder[0];
            Assert.IsNull(run.placementError, $"Seed {seed} failed bird placement: {run.placementError}");
            var s = Population().Tuning.sighting;
            var p = run.pass;
            Assert.IsTrue(p.flockInView, $"Flock in view {run.flockInViewPct:P1} outside {s.flockInView.x:P0}-{s.flockInView.y:P0}");
            Assert.IsTrue(p.emptySky, $"Longest empty sky {run.longestEmptySkySec} s > {s.maxEmptySkySeconds} s");
            Assert.IsTrue(p.firstFlock, $"First flock sighting {run.firstFlockSec} s > {s.firstFlockMaxSeconds} s (R1)");
            Assert.IsTrue(p.groundMet, $"Ground birds met {run.groundMetPerMin:F2}/min outside {s.groundMetPerMinute.x}-{s.groundMetPerMinute.y}");
            Assert.IsTrue(p.flushes, $"{run.flushEvents} flushes outside {s.flushesPerWalk.x}-{s.flushesPerWalk.y}");
            Assert.IsTrue(p.flushSeen, $"Flushes seen {run.flushSeenPct:P0} < {s.flushSeenMin:P0}");
            Assert.IsTrue(p.maxInView, $"Max in view {run.maxBirdsInView} outside {s.maxInView.x}-{s.maxInView.y}");
            Assert.IsTrue(p.busy, $"{run.pctSamplesGe15:P1} of samples had >= {s.busyCount} birds in view (> {s.busyShareMax:P0})");
            Assert.IsTrue(p.glideShare, $"Glide share {run.glideShare:P1} outside {s.glideShare.x:P0}-{s.glideShare.y:P0}");
            Assert.IsTrue(p.altitude, $"Flock altitude {run.altitudeMinAGL:F1}-{run.altitudeMaxAGL:F1} m AGL outside the A2 gate");
            Assert.IsTrue(p.crown, $"Flock {run.minCrownClearance:F1} m above a crown (< {s.crownMin} m)");
            Assert.IsTrue(p.fidBand, $"A player-triggered flush outside the A1 band: {string.Join(", ", run.flushLog.Where(f => !f.social).Select(f => f.distance.ToString("F2")))}");
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator B6r_RobustnessSeedsAreRecorded([ValueSource(nameof(RobustnessSeeds))] int seed)
        {
            var holder = new BirdSightingRun[1];
            yield return Walk(seed, false, holder);
            Debug.Log($"B6r seed {seed} (non-gating): placementError={holder[0].placementError ?? "none"} all={holder[0].pass.all}");
        }

        IEnumerator Walk(int seed, bool gating, BirdSightingRun[] holder)
        {
            yield return LoadWorld(seed);
            var wild = WildlifeTestKit.Population();
            var birds = UnityEngine.Object.FindAnyObjectByType<BirdPopulation>();
            Assert.IsNotNull(birds, "No BirdPopulation in World");
            var tuning = birds.Tuning;
            var s = tuning.sighting;
            var run = new BirdSightingRun { seed = seed, gating = gating, timeScale = TimeScale, briefRevision = tuning.briefRevision };
            holder[0] = run;
            if (birds.PlacementError != null || birds.Plan == null)
            {
                run.placementError = birds.PlacementError ?? "no plan";
                Record(run, tuning);
                yield break;
            }
            run.flocks = birds.Plan.flocks;
            run.patches = birds.Plan.patches;
            run.redraws = birds.Plan.redraws;

            var player = WildlifeTestKit.Player();
            yield return WildlifeTestKit.WaitGrounded(player);
            var ws = wild.Tuning.sighting;
            yield return new WaitForSeconds(ws.startDelayAfterGrounded);
            var route = WildlifePopulation.Route(wild.Tuning, wild.PlayerSpawn.position);
            var cam = player.GetComponentInChildren<Camera>();
            Assert.IsNotNull(cam, "Player has no camera");
            run.runnerAspect = cam.aspect;
            cam.aspect = 16f / 9f;
            int mask = ObstacleMask();
            var foliage = new WildlifeFoliage(WildlifeTestKit.Ground());
            var crowns = new TestCrowns(WildlifeTestKit.Ground());
            var walker = new WildlifeTestKit.Walker(player, ws.walkSpeed);
            var arcs = new BirdPlacementRules.Arcs(route, 8); // non-gating per-arc sky cut
            int leg = 1;
            walker.SetGoal(route[leg]);

            var flocks = birds.Flocks;
            var check = new FlockCheck(flocks.SelectMany(f => f.Birds), tuning, crowns);
            var consecutive = birds.Robins.ToDictionary(r => r, r => 0);
            var consecutiveNoFoliage = birds.Robins.ToDictionary(r => r, r => 0);
            var met = new HashSet<RobinAgent>();
            var metNoFoliage = new HashSet<RobinAgent>();
            var pending = new List<(FlushEvent e, RobinAgent r, int checks)>();
            var perRobinFlushes = birds.Robins.ToDictionary(r => r, r => 0);
            float t = 0f, next = 0f, window = 0f;
            int flushesSinceSample = 0;
            void OnFlush(RobinAgent r)
            {
                var e = new FlushEvent { robin = r.name, t = t, social = r.FlushedSocially, distance = WildlifeTestKit.Flat(player.transform.position, r.transform.position) };
                run.flushLog.Add(e);
                pending.Add((e, r, 0));
                perRobinFlushes[r]++;
                flushesSinceSample++;
            }
            RobinAgent.Flushed += OnFlush;
            int samples = Mathf.RoundToInt(s.walkSeconds / s.sampleInterval);
            Time.timeScale = TimeScale;
            try
            {
                while (run.perSample.Count < samples)
                {
                    if (walker.Step(Time.deltaTime)) { leg = leg % (route.Count - 1) + 1; walker.SetGoal(route[leg]); }
                    walker.Face(ws.scanAmplitudeDegrees * Mathf.Sin(2f * Mathf.PI * t / ws.scanPeriodSeconds));
                    if (window >= CheckWindow) { check.Sample(window); window = 0f; }
                    if (t >= next)
                    {
                        var sample = TakeSample(next, cam, mask, player.transform, foliage, crowns, birds, s);
                        sample.flushes = flushesSinceSample;
                        sample.arc = arcs.ArcOf(new Vector2(player.transform.position.x, player.transform.position.z));
                        flushesSinceSample = 0;
                        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                        Vector3 eye = cam.transform.position;
                        foreach (var r in birds.Robins)
                        {
                            bool vis = !r.Despawned && Visible(eye, planes, r.VisualBounds, s.robinViewDistance, mask, player.transform, foliage);
                            bool visNo = !r.Despawned && Visible(eye, planes, r.VisualBounds, s.robinViewDistance, mask, player.transform, null);
                            consecutive[r] = vis ? consecutive[r] + 1 : 0;
                            consecutiveNoFoliage[r] = visNo ? consecutiveNoFoliage[r] + 1 : 0;
                            if (consecutive[r] >= s.metConsecutiveSamples) met.Add(r);
                            if (consecutiveNoFoliage[r] >= s.metConsecutiveSamples) metNoFoliage.Add(r);
                        }
                        // Seen: visible (robin rules) in the flush sample or the next.
                        for (int i = pending.Count - 1; i >= 0; i--)
                        {
                            var (e, r, checks) = pending[i];
                            if (!r.Despawned && Visible(eye, planes, r.VisualBounds, s.robinViewDistance, mask, player.transform, foliage)) e.seen = true;
                            if (!e.seen && checks >= 1) e.unseenWhy = WhyUnseen(r, eye, planes, s.robinViewDistance, mask, player.transform, foliage);
                            if (e.seen || checks >= 1) pending.RemoveAt(i);
                            else pending[i] = (e, r, checks + 1);
                        }
                        run.perSample.Add(sample);
                        next += s.sampleInterval;
                    }
                    yield return null;
                    t += Time.deltaTime;
                    window += Time.deltaTime;
                }
            }
            finally
            {
                RobinAgent.Flushed -= OnFlush;
                Time.timeScale = 1f;
            }
            run.stallRecoveries = walker.StallRecoveries;
            run.sightSaturations = BirdPlacementRules.Site.SightSaturations;
            run.sightMaxHits = BirdPlacementRules.Site.SightMaxHits;
            run.wildlifeSightSaturations = WashedAshore.Wildlife.WildlifeRules.SightSaturations;
            run.wildlifeSightMaxHits = WashedAshore.Wildlife.WildlifeRules.SightMaxHits;
            run.maxFlushesPerRobin = perRobinFlushes.Values.DefaultIfEmpty(0).Max();
            run.Score(tuning, met.Count, metNoFoliage.Count, check.RuleCompliance);
            foreach (var smp in run.perSample)
                for (int k = 0; k < smp.flockWhy.Count; k++) run.sky.Add(smp.flockDist[k], smp.flockWhy[k]);
            run.sky.AddArcs(run.perSample);
            Record(run, tuning);
            var unseen = run.flushLog.Where(f => !f.seen).ToList();
            Debug.Log($"B6 seed {seed} unseen flushes {unseen.Count}/{run.flushLog.Count}: " +
                      string.Join(" ; ", unseen.Select(f => $"{f.robin} t={f.t:F1}s d={f.distance:F1}m {(f.social ? "social" : "player")}: {f.unseenWhy ?? "pending at walk end"}")));
            Debug.Log($"B6 seed {seed} gating={gating}: {JsonUtility.ToJson(run.pass)} flockInView={run.flockInViewPct:P1} emptySky={run.longestEmptySkySec}s " +
                      $"first={run.firstFlockSec}s met={run.groundMetCount} ({run.groundMetPerMin:F2}/min) flushes={run.flushEvents} seen={run.flushSeenPct:P0} " +
                      $"maxInView={run.maxBirdsInView} ge15={run.pctSamplesGe15:P1} glide={run.glideShare:P1} flapRule={run.flapRuleCompliance:P1} " +
                      $"agl={run.altitudeMinAGL:F1}-{run.altitudeMaxAGL:F1} crown={run.minCrownClearance:F1} spacing={run.minFlockSpacing:F2} stalls={run.stallRecoveries} | {check}");
            Debug.Log($"W-QA-1 bird sightline {(gating ? "GATING" : "ROBUSTNESS")} seed {seed}: sightSaturations={run.sightSaturations} " +
                      $"sightMaxHits={run.sightMaxHits} wildlifeSightSaturations={run.wildlifeSightSaturations} wildlifeSightMaxHits={run.wildlifeSightMaxHits}");
        }

        /// <summary>designer-2: why a robin was not visible at a flush check: out of the view cone, beyond the robin view distance,
        /// terrain, a trunk (tree collider above the ground) or foliage (with the blocking prototype).</summary>
        static string WhyUnseen(RobinAgent r, Vector3 eye, Plane[] planes, float range, int mask, Transform player, WildlifeFoliage foliage)
        {
            if (r.Despawned) return "despawned";
            var b = r.VisualBounds;
            if (!GeometryUtility.TestPlanesAABB(planes, b)) return "out of view cone";
            float d = Vector3.Distance(eye, b.center);
            if (d > range) return $"beyond {range:F0} m ({d:F0} m)";
            if (!Visible(eye, planes, b, range, mask, player, null))
            {
                Vector3 dir = b.center - eye;
                foreach (var hit in Physics.RaycastAll(eye, dir.normalized, dir.magnitude, mask, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    if (hit.collider.transform.IsChildOf(player) || hit.collider.transform.IsChildOf(r.transform)) continue;
                    if (hit.collider is TerrainCollider)
                        return hit.point.y > WashedAshore.Gameplay.TerrainQuery.Height(hit.point) + 0.3f ? "trunk (terrain tree collider)" : "terrain";
                    return $"collider {hit.collider.name}";
                }
                return "terrain or trunk";
            }
            if (!Visible(eye, planes, b, range, mask, player, foliage)) return $"foliage ({foliage.FirstBlocker(eye, b.center) ?? foliage.FirstBlocker(eye, new Vector3(b.center.x, b.max.y, b.center.z)) ?? "?"})";
            return "visible at check";
        }

        static BirdSample TakeSample(float at, Camera cam, int mask, Transform player, WildlifeFoliage foliage, TestCrowns crowns,
            BirdPopulation birds, BirdSightingTargets s)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            Vector3 eye = cam.transform.position;
            var x = new BirdSample { t = at, minAgl = float.MaxValue, maxAgl = float.MinValue, minCrownClearance = float.MaxValue, minSpacing = float.MaxValue };
            foreach (var r in birds.Robins)
            {
                if (r.Despawned) continue;
                if (Visible(eye, planes, r.VisualBounds, s.robinViewDistance, mask, player, foliage)) x.robins++;
                if (Visible(eye, planes, r.VisualBounds, s.robinViewDistance, mask, player, null)) x.robinsNoFoliage++;
                if (Visible(eye, planes, r.VisualBounds, s.flockViewDistance, mask, player, foliage)) x.inView++;
            }
            foreach (var f in birds.Flocks)
            {
                int vis = 0, visNo = 0;
                for (int i = 0; i < f.Birds.Count; i++)
                {
                    var b = f.Birds[i];
                    Vector3 p = b.transform.position;
                    if (Visible(eye, planes, b.VisualBounds, s.flockViewDistance, mask, player, foliage)) vis++;
                    if (Visible(eye, planes, b.VisualBounds, s.flockViewDistance, mask, player, null)) visNo++;
                    float agl = p.y - TerrainY(p);
                    x.minAgl = Mathf.Min(x.minAgl, agl);
                    x.maxAgl = Mathf.Max(x.maxAgl, agl);
                    float crown = crowns.TopWithin(p, FlockCheck.CrownSearch);
                    if (!float.IsNegativeInfinity(crown)) x.minCrownClearance = Mathf.Min(x.minCrownClearance, p.y - crown);
                    x.flying++;
                    if (IsGlide(Dominant(b.Animator).clip)) x.gliding++;
                    for (int j = i + 1; j < f.Birds.Count; j++) x.minSpacing = Mathf.Min(x.minSpacing, Vector3.Distance(p, f.Birds[j].transform.position));
                }
                x.flockBirds.Add(vis);
                x.flockBirdsNoFoliage.Add(visNo);
                x.inView += vis;
                Diagnose(x, cam, f, vis >= s.flockInViewMinBirds, s.flockViewDistance);
            }
            return x;
        }

        /// <summary>Non-gating: where the flock centroid sits relative to the camera, and the first reason it isn't in view.</summary>
        static void Diagnose(BirdSample x, Camera cam, Flock f, bool inView, float range)
        {
            Vector3 c = Vector3.zero;
            foreach (var b in f.Birds) c += b.transform.position;
            c /= Mathf.Max(f.Birds.Count, 1);
            Vector3 to = c - cam.transform.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            float dist = flat.magnitude;
            float elev = Mathf.Atan2(to.y, dist) * Mathf.Rad2Deg;
            float azim = Vector3.SignedAngle(fwd, flat, Vector3.up);
            float halfV = cam.fieldOfView * 0.5f;
            float halfH = Mathf.Atan(Mathf.Tan(halfV * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg;
            string why = inView ? "in" : to.magnitude > range ? "far" : Mathf.Abs(azim) > halfH ? "azimuth" : elev > halfV ? "above" : "occluded";
            x.flockDist.Add(dist);
            x.flockElev.Add(elev);
            x.flockAzim.Add(azim);
            x.flockWhy.Add(why);
        }

        static void Record(BirdSightingRun run, BirdTuning tuning)
        {
            var list = run.gating ? Runs : Robustness;
            list.RemoveAll(r => r.seed == run.seed);
            list.Add(run);
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            string file = Path.Combine(dir, "bird-sightings.json");
            Directory.CreateDirectory(dir);
            var runs = new List<BirdSightingRun>(Runs);
            var robust = new List<BirdSightingRun>(Robustness);
            if (File.Exists(file)) // merge by seed so a domain reload between cases can't drop earlier runs
            {
                var old = JsonUtility.FromJson<Report>(File.ReadAllText(file));
                if (old?.runs != null) runs.AddRange(old.runs.Where(o => runs.All(r => r.seed != o.seed)));
                if (old?.robustness != null) robust.AddRange(old.robustness.Where(o => robust.All(r => r.seed != o.seed)));
            }
            File.WriteAllText(file, JsonUtility.ToJson(new Report
            {
                generated = DateTime.Now.ToString("o"),
                brief = "_bmad-output/poc/bird-density-brief.md sections 3.3-3.5 (final B1 ruling, A1-A4)",
                notes = "Wildlife 3.4 walk unchanged (route, walker, scan, 16:9, 360 x 0.5 s). Visible = frustum, range (robins 30 m, " +
                        "flock birds 200 m), a ray to bounds centre or top clear of terrain/Default AND passing WildlifeFoliage (gating). " +
                        "*NoFoliage fields are the non-gating comparison. flockInView = >= 2 birds of one flock visible; empty sky = 0 flock " +
                        "birds visible. Met = visible in >= 2 consecutive samples, once per robin. Flushes = robins entering Flutter (player or " +
                        "social), distance measured by the test; seen = visible in that sample or the next. inView = every bird in frame, " +
                        "<= 200 m, unoccluded. flapRuleCompliance is sampled every 0.1 s game time (vz over 0.2 s). runs = gating seeds; " +
                        "robustness = 404-808, non-gating.",
                bands = tuning.sighting,
                altitudeBand = tuning.flock.altitudeBand,
                fid = tuning.robin.flightInitiationDistance,
                runs = runs.OrderBy(r => r.seed).ToList(),
                robustness = robust.OrderBy(r => r.seed).ToList(),
            }, true));
        }

        [Serializable]
        class Report
        {
            public string generated, brief, notes;
            public BirdSightingTargets bands;
            public Vector2 altitudeBand;
            public float fid;
            public List<BirdSightingRun> runs;
            public List<BirdSightingRun> robustness;
        }

        // ---- Positive controls ----------------------------------------------------------------------

        /// <summary>The scoring must fail an empty sky and a crowd: every band except the walk-independent ones.</summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator B6c_ScoringFailsAnEmptySkyAndACrowd()
        {
            yield return LoadWorld(101);
            var tuning = Population().Tuning;
            var run = new BirdSightingRun();
            for (int i = 0; i < 360; i++)
                run.perSample.Add(new BirdSample { t = i * 0.5f, flockBirds = new List<int> { 0, 0, 0 }, flockBirdsNoFoliage = new List<int> { 0, 0, 0 },
                    robins = 30, inView = 30, gliding = 0, flying = 15, minAgl = 2f, maxAgl = 120f, minCrownClearance = -3f, minSpacing = 0.2f });
            for (int i = 0; i < 40; i++) run.flushLog.Add(new FlushEvent { distance = 20f, social = false, seen = false });
            run.Score(tuning, 1000, 1000, 0.1f);
            var p = run.pass;
            Assert.IsFalse(p.flockInView || p.emptySky || p.firstFlock || p.groundMet || p.flushes || p.flushSeen || p.maxInView || p.busy
                           || p.glideShare || p.altitude || p.crown || p.fidBand || p.all,
                $"Control: a broken walk passed a band: {JsonUtility.ToJson(p)}");
        }

        /// <summary>Visibility: a flock bird seen from 30 m reads visible, then hidden behind a wall, behind an injected
        /// foliage crown (foliage test only), and out of range.</summary>
        [UnityTest, Timeout(60000)]
        public IEnumerator B6v_VisibilityCatchesAWallFoliageAndRange()
        {
            yield return LoadWorld(101);
            var bird = Population().Flocks[0].Birds[0];
            var player = WildlifeTestKit.Player();
            var go = new GameObject("B6v camera");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            Vector3 c = bird.VisualBounds.center;
            go.transform.SetPositionAndRotation(c - Vector3.forward * 30f, Quaternion.LookRotation(Vector3.forward));
            int mask = ObstacleMask();
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var b = bird.VisualBounds;
            var foliage = new WildlifeFoliage(WildlifeTestKit.Ground());
            bool clear = Visible(cam.transform.position, planes, b, 200f, mask, player.transform, null);
            bool far = Visible(cam.transform.position, planes, b, 20f, mask, player.transform, null);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = c - Vector3.forward * 15f;
            wall.transform.localScale = new Vector3(20f, 20f, 1f);
            Physics.SyncTransforms();
            bool walled = Visible(cam.transform.position, planes, b, 200f, mask, player.transform, null);
            UnityEngine.Object.Destroy(wall);
            yield return null;

            // Foliage: look past a terrain tree 1 m beside its trunk at 3 m height (misses the trunk collider, inside
            // the 2 m foliage radius below the crown top). Only the foliage-aware rule may call it hidden.
            var tree = WildlifeTestKit.Ground().Trees().First(i => i.prefab && !i.prefab.name.StartsWith("Rock_")
                                                                   && !i.prefab.name.StartsWith("Bush_") && i.heightScale > 0.8f);
            Vector3 tp = tree.world;
            Vector3 from = tp + new Vector3(-8f, 3f, 1f), to = tp + new Vector3(8f, 3f, 1f);
            go.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
            planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var target = new Bounds(to, Vector3.one * 0.2f);
            bool treeNoFoliage = Visible(from, planes, target, 200f, mask, player.transform, null);
            bool treeFoliage = Visible(from, planes, target, 200f, mask, player.transform, foliage);
            UnityEngine.Object.Destroy(go);
            Assert.IsTrue(clear, "A bird 30 m straight ahead read as not visible");
            Assert.IsFalse(walled, "Control: a bird behind a wall read as visible");
            Assert.IsFalse(far, "Control: a bird beyond the range read as visible");
            Assert.IsFalse(treeFoliage, "Control: a target behind a tree's foliage read as visible with the foliage test");
            Debug.Log($"B6v tree at {tp}: visible without foliage test={treeNoFoliage} (trunk/terrain may also block), with={treeFoliage}");
        }
    }
}
