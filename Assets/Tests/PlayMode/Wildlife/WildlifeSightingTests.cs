using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Wildlife;
using static WashedAshore.Tests.Wildlife.WildlifeTestKit;

namespace WashedAshore.Tests.Wildlife
{
    /// <summary>
    /// A6: the scripted 3-minute walk (density brief 3.4/3.5), once per seed. Writes
    /// TestResults/wildlife-sightings.json with every run so far. Gating seeds (101/202/303) assert the
    /// bands; the R1-A robustness seeds (404-808) are recorded in a separate array and never assert.
    /// The visibility test (WildlifeTestKit.IsVisible) and RunResult.Score are unchanged from R0.
    /// </summary>
    public class WildlifeSightingTests
    {
        // Game time is what's sampled; running it faster only shortens the wall clock.
        const float TimeScale = 3f;
        static readonly int[] Seeds = { 101, 202, 303 };
        static readonly int[] RobustnessSeeds = { 404, 505, 606, 707, 808 };
        static readonly List<RunResult> Runs = new List<RunResult>();
        static readonly List<RunResult> Robustness = new List<RunResult>();
        static readonly List<RunResult> Fidelity = new List<RunResult>();

        [TearDown]
        public void TearDown()
        {
            WildlifeRandom.OverrideSeed(null);
            Time.timeScale = 1f;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator A6_SightingWalkLandsInsideTheBriefBands([ValueSource(nameof(Seeds))] int seed)
        {
            var holder = new RunResult[1];
            yield return Walk(seed, true, holder);
            var run = holder[0];
            var s = Population().Tuning.sighting;
            Assert.IsNull(run.placementError, $"Seed {seed} failed placement: {run.placementError}");
            Assert.IsTrue(run.plannedAnchorsMatchScene, $"Seed {seed}: re-planning the seed did not reproduce the scene's anchors");

            Assert.IsTrue(run.pass.sightingRate, $"Sighting rate {run.sightingRate:P1} outside {s.sightingRateMin:P0}-{s.sightingRateMax:P0}");
            Assert.IsTrue(run.pass.longestGap, $"Longest gap {run.longestGapSec} s > {s.maxGapSeconds} s");
            Assert.IsTrue(run.pass.firstSighting, $"First sighting {run.firstSightingSec} s > {s.maxFirstSightingSeconds} s");
            Assert.IsTrue(run.pass.maxVisible, $"Max in view {run.maxVisible} outside {s.maxVisibleMin}-{s.maxVisibleMax}");
            Assert.IsTrue(run.pass.busy, $"{run.pctSamplesGe5:P1} of samples had >= {s.busyCount} visible");
            Assert.IsTrue(run.pass.perSpecies, $"Species not seen for {s.perSpeciesMinConsecutive} samples: {JsonUtility.ToJson(run.perSpeciesMaxConsecutive)}");
        }

        /// <summary>Non-gating (R1-A): recorded under "robustness" in the JSON; never fails on bands.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator A6r_RobustnessSeedsAreRecorded([ValueSource(nameof(RobustnessSeeds))] int seed)
        {
            var holder = new RunResult[1];
            yield return Walk(seed, false, holder);
            var run = holder[0];
            Debug.Log($"A6r seed {seed} (non-gating): placementError={run.placementError ?? "none"} all={run.pass.all}");
        }

        /// <summary>Non-gating fidelity check (wl-qa): seed 101 at real-time speed, written to its own file
        /// (TestResults/wildlife-sightings-fidelity.json) so the gating evidence file is not touched.</summary>
        /// <remarks>Cases are "seed@timeScale"; more can be added (e.g. "202@3") to replay a seed into this file
        /// without rewriting the gating evidence.</remarks>
        static readonly string[] FidelityCases = { "101@1" };

        [UnityTest, Timeout(900000)]
        public IEnumerator A6f_Fidelity([ValueSource(nameof(FidelityCases))] string fidelityCase)
        {
            var parts = fidelityCase.Split('@');
            int seed = int.Parse(parts[0]);
            float timeScale = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
            var holder = new RunResult[1];
            yield return Walk(seed, false, holder, timeScale, fidelity: true);
            var run = holder[0];
            Debug.Log($"A6f seed {seed} timeScale={timeScale} (non-gating): rate={run.sightingRate:P1} all={run.pass.all} " +
                      $"stalls={run.stallRecoveries} {string.Join("; ", run.stallEvents.Select(e => JsonUtility.ToJson(e)))}");
        }

        IEnumerator Walk(int seed, bool gating, RunResult[] holder, float timeScale = TimeScale, bool fidelity = false)
        {
            // A seed that can't satisfy the placement rules must appear in the JSON as a failed run, so the
            // placer's LogError is captured here instead of aborting the test before the file is written.
            var loadErrors = new List<string>();
            void Capture(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception) loadErrors.Add(msg);
            }
            Application.logMessageReceived += Capture;
            LogAssert.ignoreFailingMessages = true;
            try { yield return LoadWorld(seed); }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                Application.logMessageReceived -= Capture;
            }
            var pop = Population();
            Assert.AreEqual(seed, pop.ActiveSeed);
            var tuning = pop.Tuning;
            var s = tuning.sighting;
            var run = new RunResult { seed = seed, gating = gating, fidelity = fidelity, timeScale = timeScale, briefRevision = tuning.briefRevision };
            holder[0] = run;

            // Re-plan the seed independently (twice) for the R1-A log and as a determinism proof against the
            // anchors the scene actually holds (baked for 101, re-planned at load for the rest).
            var route = WildlifePopulation.Route(tuning, pop.PlayerSpawn.position);
            var plan = WildlifeRules.PlanGroups(tuning, seed, Terrain.activeTerrain, pop.PlayerSpawn.position, route, out string planError, out var log);
            var again = WildlifeRules.PlanGroups(tuning, seed, Terrain.activeTerrain, pop.PlayerSpawn.position, route, out _, out _);
            run.assignmentDraws = log.draws;
            run.validAssignments = log.validAssignments;
            run.redraws = log.redraws;
            run.finalAssignment = log.finalAssignment != null ? log.finalAssignment.ToList() : new List<int>();
            run.replanDeterministic = plan != null && again != null && plan.Zip(again, (a, b) => a.anchor == b.anchor && a.arc == b.arc).All(x => x);

            if (pop.PlacementError != null || plan == null)
            {
                run.placementError = pop.PlacementError ?? planError;
                Record(run, s);
                yield break;
            }
            Assert.IsEmpty(loadErrors, "Errors while loading World");

            var player = Player();
            yield return WaitGrounded(player);
            yield return new WaitForSeconds(s.startDelayAfterGrounded);

            var agents = WildlifeAgent.All.ToList();
            var herds = pop.GetComponentsInChildren<WildlifeHerd>();
            run.plannedAnchorsMatchScene = herds.Length == plan.Count;
            foreach (var h in herds)
            {
                var p = plan.FirstOrDefault(g => g.name == h.name);
                bool match = p != null && Vector3.Distance(p.anchor, h.Anchor) < 0.01f && p.arc == h.RouteArc;
                run.plannedAnchorsMatchScene &= match;
                run.groups.Add(new GroupInfo
                {
                    species = h.Species.ToString(), size = h.Members.Count, anchor = h.Anchor, routeArc = h.RouteArc,
                    routeDistance_m = p != null ? p.routeDistance : -1f,
                    sightlineBack_m = p != null ? p.sightlineBack : -1f,
                    sightlineFromRouteS_m = p != null ? p.sightlineFromS : -1f,
                    sightlineFromSegment = p != null ? p.sightlineFromSegment : -1,
                    plannedAnchorMatches = match,
                });
            }
            foreach (var a in agents) run.population.Add(a.Species, 1);

            var cam = player.GetComponentInChildren<Camera>();
            Assert.IsNotNull(cam, "Player has no camera");
            // Batchmode has no real screen, so the aspect follows a default window. The brief's view model
            // (3.4) and the A7 build are 16:9; pin it and record what the runner had.
            run.cameraFov = cam.fieldOfView;
            run.runnerAspect = cam.aspect;
            cam.aspect = 16f / 9f;
            int mask = OccluderMask();
            var foliage = new WildlifeFoliage(Terrain.activeTerrain);
            var walker = new Walker(player, s.walkSpeed);
            int leg = 1;
            walker.SetGoal(route[leg]);
            walker.GoalLabel = "W" + leg;

            int samples = Mathf.RoundToInt(s.walkSeconds / s.sampleInterval);
            var visible = new List<WildlifeAgent>();
            Time.timeScale = timeScale;
            float t = 0f, nextSample = 0f;
            while (run.perSample.Count < samples)
            {
                if (walker.Step(Time.deltaTime))
                {
                    leg = leg % (route.Count - 1) + 1; // closed loop, resumes at W1
                    walker.SetGoal(route[leg]);
                    walker.GoalLabel = "W" + leg;
                }
                walker.Face(s.scanAmplitudeDegrees * Mathf.Sin(2f * Mathf.PI * t / s.scanPeriodSeconds));

                if (t >= nextSample)
                {
                    var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                    visible.Clear();
                    int inRange = 0, inFrame = 0, foliageVisible = 0;
                    Vector3 eye = cam.transform.position;
                    foreach (var a in agents)
                    {
                        if (!a) continue;
                        // Diagnostics only (not scored): absence vs occlusion.
                        var b = a.VisualBounds;
                        if (Vector3.Distance(eye, b.center) <= s.viewDistance)
                        {
                            inRange++;
                            if (GeometryUtility.TestPlanesAABB(planes, b)) inFrame++;
                        }
                        if (IsVisible(cam, planes, a, s.viewDistance, mask, player.transform))
                        {
                            visible.Add(a);
                            if (foliage.SeesThrough(eye, b)) foliageVisible++; // non-gating
                        }
                    }
                    run.perSample.Add(new Sample
                    {
                        t = nextSample, visible = visible.Count, inRange80 = inRange, inFrame80 = inFrame,
                        visibleFoliage = foliageVisible,
                        species = string.Join(",", visible.Select(a => a.Species.ToString()).OrderBy(x => x)),
                    });
                    nextSample += s.sampleInterval;
                }
                yield return null;
                t += Time.deltaTime;
            }
            Time.timeScale = 1f;
            run.stallRecoveries = walker.StallRecoveries;
            run.stallEvents = walker.StallLog;
            run.Score(s);
            run.sightingRateFoliage = (float)run.perSample.Count(x => x.visibleFoliage > 0) / run.samples;
            Record(run, s);
            Debug.Log($"A6 seed {seed} gating={gating}: {JsonUtility.ToJson(run.pass)} rate={run.sightingRate:P1} gap={run.longestGapSec}s " +
                      $"first={run.firstSightingSec}s max={run.maxVisible} busy={run.pctSamplesGe5:P1} foliage={run.sightingRateFoliage:P1} " +
                      $"draws={run.assignmentDraws} stalls={run.stallRecoveries}");
        }

        static void Record(RunResult run, SightingTargets s)
        {
            if (run.fidelity)
            {
                Fidelity.RemoveAll(r => r.seed == run.seed && r.timeScale == run.timeScale);
                Fidelity.Add(run);
                string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults", "wildlife-sightings-fidelity.json"));
                if (File.Exists(path)) // survive a domain reload between cases, as Write() does
                {
                    var old = JsonUtility.FromJson<Report>(File.ReadAllText(path));
                    if (old?.runs != null) Fidelity.AddRange(old.runs.Where(o => Fidelity.All(r => r.seed != o.seed || r.timeScale != o.timeScale)));
                }
                File.WriteAllText(path, JsonUtility.ToJson(new Report
                {
                    generated = DateTime.Now.ToString("o"),
                    brief = "_bmad-output/poc/wildlife-density-brief.md sections 3.4-3.6",
                    notes = "Non-gating fidelity check (wl-qa): same walk and scoring as A6. 101 at timeScale 1, and the " +
                            "gating seeds replayed at timeScale 3 with the walker stall log. wildlife-sightings.json is not touched.",
                    bands = s, runs = Fidelity.OrderBy(r => r.seed).ThenBy(r => r.timeScale).ToList(), robustness = new List<RunResult>(),
                }, true));
                return;
            }
            var list = run.gating ? Runs : Robustness;
            list.RemoveAll(r => r.seed == run.seed);
            list.Add(run);
            Write(s);
        }

        static void Write(SightingTargets s)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            string file = Path.Combine(dir, "wildlife-sightings.json");
            Directory.CreateDirectory(dir);
            // Merge by seed so a domain reload between cases can't drop earlier runs; each run keeps its own timestamp.
            var runs = new List<RunResult>(Runs);
            var robust = new List<RunResult>(Robustness);
            if (File.Exists(file))
            {
                var old = JsonUtility.FromJson<Report>(File.ReadAllText(file));
                if (old?.runs != null) runs.AddRange(old.runs.Where(o => runs.All(r => r.seed != o.seed)));
                if (old?.robustness != null) robust.AddRange(old.robustness.Where(o => robust.All(r => r.seed != o.seed)));
            }
            var report = new Report
            {
                generated = DateTime.Now.ToString("o"),
                brief = "_bmad-output/poc/wildlife-density-brief.md sections 3.4-3.6",
                notes = "routeArc, finalAssignment: 0-based arcs from W0 (brief numbers arcs 1-6). runs = gating seeds; " +
                        "robustness = non-gating R1-A seeds. sightingRateFoliage and inRange80/inFrame80/visibleFoliage are non-gating.",
                bands = s,
                runs = runs.OrderBy(r => r.seed).ToList(),
                robustness = robust.OrderBy(r => r.seed).ToList(),
            };
            File.WriteAllText(file, JsonUtility.ToJson(report, true));
        }

        [Serializable]
        class Report
        {
            public string generated;
            public string brief;
            public string notes;
            public SightingTargets bands;
            public List<RunResult> runs;
            public List<RunResult> robustness;
        }

        [Serializable]
        class SpeciesCounts
        {
            public int Deer, Stag, Fox, Wolf;

            public void Add(WildlifeSpecies s, int n) => Set(s, Get(s) + n);
            public void Max(WildlifeSpecies s, int n) => Set(s, Mathf.Max(Get(s), n));
            public int Get(WildlifeSpecies s) => s switch
            {
                WildlifeSpecies.Deer => Deer, WildlifeSpecies.Stag => Stag, WildlifeSpecies.Fox => Fox, _ => Wolf,
            };

            void Set(WildlifeSpecies s, int n)
            {
                switch (s)
                {
                    case WildlifeSpecies.Deer: Deer = n; break;
                    case WildlifeSpecies.Stag: Stag = n; break;
                    case WildlifeSpecies.Fox: Fox = n; break;
                    default: Wolf = n; break;
                }
            }
        }

        [Serializable]
        class GroupInfo
        {
            public string species;
            public int size;
            public Vector3 anchor;
            public int routeArc; // 0-based from W0 (R0)
            public float routeDistance_m; // R1: 15-35
            public float sightlineBack_m; // R1: clear route point this far before the anchor (40-80)
            public float sightlineFromRouteS_m; // arc length from W0 of that route point
            public int sightlineFromSegment; // route segment Wi -> Wi+1 holding it
            public bool plannedAnchorMatches;
        }

        [Serializable]
        class Sample
        {
            public float t;
            public int visible;
            public string species;
            public int inRange80; // diagnostics: within 80 m in any direction
            public int inFrame80; // diagnostics: within 80 m and in the frustum, before the occlusion raycasts
            public int visibleFoliage; // non-gating: visible animals not hidden by a trunk/canopy (2 m, below crown)
        }

        [Serializable]
        class Pass
        {
            public bool sightingRate, longestGap, firstSighting, maxVisible, busy, perSpecies, all;
        }

        [Serializable]
        class RunResult
        {
            public int seed;
            public bool gating;
            public bool fidelity;
            public string ranAt = DateTime.Now.ToString("o");
            public string briefRevision;
            public string placementError;
            public int validAssignments;
            public int assignmentDraws;
            public List<string> redraws = new List<string>();
            public List<int> finalAssignment = new List<int>();
            public bool replanDeterministic;
            public bool plannedAnchorsMatchScene;
            public int samples;
            public float timeScale;
            public float cameraFov;
            public float runnerAspect;
            public float aspect = 16f / 9f;
            public float sightingRate;
            public float sightingRateFoliage;
            public float longestGapSec;
            public float firstSightingSec;
            public int maxVisible;
            public float pctSamplesGe5;
            public SpeciesCounts perSpeciesMaxConsecutive = new SpeciesCounts();
            public SpeciesCounts population = new SpeciesCounts();
            public List<GroupInfo> groups = new List<GroupInfo>();
            public int stallRecoveries;
            public List<StallEvent> stallEvents = new List<StallEvent>();
            public Pass pass = new Pass();
            public List<Sample> perSample = new List<Sample>();

            public void Score(SightingTargets s)
            {
                samples = perSample.Count;
                int seen = perSample.Count(x => x.visible > 0);
                sightingRate = (float)seen / samples;
                maxVisible = perSample.Max(x => x.visible);
                pctSamplesGe5 = (float)perSample.Count(x => x.visible >= s.busyCount) / samples;

                int first = perSample.FindIndex(x => x.visible > 0);
                firstSightingSec = (first < 0 ? samples : first) * s.sampleInterval;
                int gap = 0, longest = 0;
                foreach (var x in perSample)
                {
                    gap = x.visible == 0 ? gap + 1 : 0;
                    longest = Mathf.Max(longest, gap);
                }
                longestGapSec = longest * s.sampleInterval;

                foreach (WildlifeSpecies sp in Enum.GetValues(typeof(WildlifeSpecies)))
                {
                    int run = 0;
                    foreach (var x in perSample)
                    {
                        run = x.species.Split(',').Contains(sp.ToString()) ? run + 1 : 0;
                        perSpeciesMaxConsecutive.Max(sp, run);
                    }
                }

                pass.sightingRate = sightingRate >= s.sightingRateMin && sightingRate <= s.sightingRateMax;
                pass.longestGap = longestGapSec <= s.maxGapSeconds;
                pass.firstSighting = firstSightingSec <= s.maxFirstSightingSeconds;
                pass.maxVisible = maxVisible >= s.maxVisibleMin && maxVisible <= s.maxVisibleMax;
                pass.busy = pctSamplesGe5 <= s.busySampleShareMax;
                pass.perSpecies = Enum.GetValues(typeof(WildlifeSpecies)).Cast<WildlifeSpecies>()
                    .All(sp => perSpeciesMaxConsecutive.Get(sp) >= s.perSpeciesMinConsecutive);
                pass.all = pass.sightingRate && pass.longestGap && pass.firstSighting && pass.maxVisible && pass.busy && pass.perSpecies;
            }
        }
    }
}
