using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;
using WashedAshore.Wildlife;

namespace WashedAshore.Tests.Performance
{
    /// <summary>
    /// A8: frame-time cost of the wildlife. The same scripted walk runs with the Wildlife root
    /// active and inactive (ON, OFF, ON, OFF; scene reloaded each run). The OFF runs stand in
    /// for "the build without animals". Pass: pooled median ms ON &lt;= pooled median ms OFF / 0.9.
    /// Test assembly only (UNITY_INCLUDE_TESTS), so none of this reaches a release player.
    /// </summary>
    public class WildlifeFrameTimeTests
    {
        const string WorldScene = "World";
        const string WildlifeRoot = "Wildlife";
        const float WalkSpeed = 5f; // same pace as the A6 sighting walk
        const float RunSeconds = 40f;
        const float WarmupSeconds = 3f;
        const int Runs = 4;
        const int MinSamples = 1000;
        const float MaxFpsDrop = 0.10f;
        const int Width = 1920, Height = 1080;
        /// <summary>The start point of the walk: the test measures the wildlife system on the route laid out from PlayerSpawn (option a). Spec: spec-playmode-spawn-19.</summary>
        const string SpecStart = TestSpawn.PlayerSpawnPoint;


        class RunResult
        {
            public bool wildlife;
            public List<double> ms = new List<double>();
            public float duration;
            public string resolution;
            public string windowMode;
            public int vSync;
            public int agentsActive;
            public int agentsOnNavMesh;
            public int animatorsEnabled;
            public List<string> excludedPlayerAnimators = new List<string>();   // the player's view and shadow body Animators, left out of animatorsEnabled
            public int seed;
        }

        int savedVSync;
        int savedTargetFrameRate;
        bool savedRunInBackground;
        int agentCreateFailures;

        void CountAgentFailures(string message, string stack, LogType type)
        {
            if (message.Contains("Failed to create agent")) agentCreateFailures++;
        }

        [SetUp]
        public void SetUp()
        {
            savedVSync = QualitySettings.vSyncCount;
            savedTargetFrameRate = Application.targetFrameRate;
            savedRunInBackground = Application.runInBackground;
            agentCreateFailures = 0;
            Application.logMessageReceived += CountAgentFailures;
            // A focus change (e.g. a firewall prompt) must not pause or throttle a run.
            Application.runInBackground = true;
            // Uncapped, so a 60 Hz vsync pin can't hide the difference between runs.
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (!Application.isEditor)
                Screen.SetResolution(Width, Height, FullScreenMode.Windowed);
        }

        [TearDown]
        public void TearDown()
        {
            QualitySettings.vSyncCount = savedVSync;
            Application.targetFrameRate = savedTargetFrameRate;
            Application.runInBackground = savedRunInBackground;
            Application.logMessageReceived -= CountAgentFailures;
            WildlifeRandom.OverrideSeed(null);
        }

        [UnityTest, Performance, Timeout(900000)]
        public IEnumerator WildlifeCostsAtMostTenPercentFps()
        {
            var runs = new List<RunResult>();
            for (int i = 0; i < Runs; i++)
            {
                var r = new RunResult { wildlife = i % 2 == 0 };
                runs.Add(r);
                yield return Walk(r);
            }

            var on = runs.Where(r => r.wildlife).SelectMany(r => r.ms).ToList();
            var off = runs.Where(r => !r.wildlife).SelectMany(r => r.ms).ToList();
            double medOn = Percentile(on, 0.5), medOff = Percentile(off, 0.5);
            double limitMs = medOff / (1.0 - MaxFpsDrop);
            double drop = 1.0 - medOff / medOn;

            foreach (var v in on) Measure.Custom(new SampleGroup("FrameTime.WildlifeOn", SampleUnit.Millisecond), v);
            foreach (var v in off) Measure.Custom(new SampleGroup("FrameTime.WildlifeOff", SampleUnit.Millisecond), v);

            string json = "{" +
                $"\"measured_in\":\"{(Application.isEditor ? "editor" : "player")}\",\"platform\":\"{Application.platform}\"," +
                $"\"development\":{Bool(Debug.isDebugBuild)},\"gpu\":\"{SystemInfo.graphicsDeviceName}\"," +
                $"\"graphics_api\":\"{SystemInfo.graphicsDeviceType}\"," +
                $"\"quality\":\"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\"," +
                $"\"failed_to_create_agent\":{agentCreateFailures}," +
                $"\"spawn\":\"{TestSpawn.FrameTimeStart(SpecStart)}\",\"route_speed_mps\":{WalkSpeed},\"warmup_s\":{WarmupSeconds},\"runs\":[" +
                string.Join(",", runs.Select(Json)) + "]," +
                $"\"pooled\":{{\"on_median_ms\":{medOn:F3},\"off_median_ms\":{medOff:F3}," +
                $"\"on_median_fps\":{1000 / medOn:F1},\"off_median_fps\":{1000 / medOff:F1}," +
                $"\"limit_on_median_ms\":{limitMs:F3},\"fps_drop\":{drop:F4},\"pass\":{Bool(medOn <= limitMs)}}}" +
                "}";
            Debug.Log("[WildlifeFps] " + json);
            WriteResult(json);

            foreach (var r in runs)
            {
                Assert.GreaterOrEqual(r.ms.Count, MinSamples, $"Run ({(r.wildlife ? "ON" : "OFF")}) has too few samples");
                if (r.wildlife)
                {
                    Assert.Greater(r.animatorsEnabled, 0, "ON run had no enabled animal Animators");
                    // Animals stuck off the NavMesh stand still and would understate the cost.
                    Assert.AreEqual(r.agentsActive, r.agentsOnNavMesh, "ON run has agents that are not on the NavMesh");
                }
                else
                {
                    string excludedBad = CheckExcludedPlayerAnimators(r.excludedPlayerAnimators, ExpectedPlayerAnimators);
                    Assert.IsNull(excludedBad, excludedBad);
                    Assert.AreEqual(0, r.agentsActive, "OFF run still has active NavMeshAgents");
                    Assert.AreEqual(0, r.animatorsEnabled, "OFF run still has enabled Animators");
                }
            }
            Assert.AreEqual(0, agentCreateFailures, "Player logged 'Failed to create agent' (WL-BUG-1)");
            Assert.LessOrEqual(medOn, limitMs, $"Median {medOn:F2} ms with wildlife vs {medOff:F2} ms without");
        }

        /// <summary>The men's rig has exactly two player Animators (view body and shadow body). A filter that drops more would hide animals; fewer means the filter missed the player.</summary>
        const int ExpectedPlayerAnimators = 2;

        static string CheckExcludedPlayerAnimators(IReadOnlyList<string> names, int expected) =>
            names.Count == expected ? null : $"The player-Animator filter excluded {names.Count} Animators, expected exactly {expected} on the men's rig: [{string.Join(", ", names)}]";

        [Test]
        public void ExcludedPlayerAnimators_WrongCount_TurnsRed()
        {
            var two = new List<string> { "Player/ViewBody", "Player/ShadowBody" };
            Assert.IsNull(CheckExcludedPlayerAnimators(two, ExpectedPlayerAnimators), "two named player Animators must pass");
            Assert.IsNotNull(CheckExcludedPlayerAnimators(two, 3), "forced failure: expected 3 with 2 found must fail");
            Assert.IsNotNull(CheckExcludedPlayerAnimators(two.GetRange(0, 1), ExpectedPlayerAnimators), "a filter that finds only one must fail");
            Assert.IsNotNull(CheckExcludedPlayerAnimators(new List<string>(), ExpectedPlayerAnimators), "a filter that finds none (the R3 fault) must fail");
            Assert.IsNotNull(CheckExcludedPlayerAnimators(new List<string> { "Player/ViewBody", "Player/ShadowBody", "Deer/Animator" }, ExpectedPlayerAnimators), "a filter that also swallows an animal must fail");
        }

        static IEnumerator Walk(RunResult r)
        {
            // No override: the baked placement (tuning default seed), identical in both ON runs.
            WildlifeRandom.OverrideSeed(null);
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;

            var wildlife = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == WildlifeRoot);
            Assert.IsNotNull(wildlife, $"No root object named {WildlifeRoot} in {WorldScene}");
            var population = wildlife.GetComponent<WildlifePopulation>();
            Assert.IsNotNull(population, "Wildlife root has no WildlifePopulation");
            r.seed = population.ActiveSeed;
            // The A6 sighting route (tuning offsets from PlayerSpawn, snapped to the NavMesh).
            var route = WildlifePopulation.Route(population.Tuning, population.PlayerSpawn.position)
                .Select(p => new Vector2(p.x, p.z)).ToArray();
            Assert.GreaterOrEqual(route.Length, 2, "Sighting route is empty");
            wildlife.SetActive(r.wildlife);

            var player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "No PlayerController");
            // Start point first (before warm-up, outside the measured windows), then park for the scripted walk: the same path every run,
            // independent of input and physics. Rail per studio:decisions gate/playmode-spawn-19-amend1 and studio:engineering adr/playmode-spawn-19/part5:
            // Parked.Move writes the transform each frame with PlayerController and CharacterController off throughout.
            yield return TestSpawn.At(TestSpawn.FrameTimeStart(SpecStart));
            var parked = TestSpawn.Park(player);
            Terrain terrain = Terrain.activeTerrain;
            Assert.IsNotNull(terrain, "No active Terrain");
            float eye = player.transform.position.y - Ground(terrain, player.transform.position);

            float total = 0f;
            for (int i = 1; i < route.Length; i++) total += Vector2.Distance(route[i - 1], route[i]);

            float t = -WarmupSeconds;
            while (t < RunSeconds)
            {
                Place(parked, terrain, route, Mathf.Repeat(Mathf.Max(0f, t) * WalkSpeed, total), eye);
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (t >= 0f) r.ms.Add(dt * 1000.0);
                t += dt;
            }

            r.duration = t;
            r.resolution = $"{Screen.width}x{Screen.height}";
            r.windowMode = Screen.fullScreenMode.ToString();
            r.vSync = QualitySettings.vSyncCount;
            // Counted across the whole scene, so a manager outside the Wildlife root shows up too.
            var agents = Object.FindObjectsByType<NavMeshAgent>(FindObjectsInactive.Exclude).Where(a => a.isActiveAndEnabled).ToList();
            r.agentsActive = agents.Count;
            r.agentsOnNavMesh = agents.Count(a => a.isOnNavMesh);
            // Animal Animators only: the Birds root (added after this test) stays on in both runs and is not wildlife. The player's view and shadow body
            // Animators (added by the player-character pod) are on in both runs too; they are excluded by name here and the names are printed and counted.
            var enabledAnimators = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude)
                .Where(a => a.isActiveAndEnabled && a.GetComponentInParent<WashedAshore.Birds.BirdPopulation>() == null).ToList();
            r.excludedPlayerAnimators = enabledAnimators.Where(a => a.GetComponentInParent<PlayerController>() != null)
                .Select(a => $"{a.transform.root.name}/{a.name}").ToList();
            r.animatorsEnabled = enabledAnimators.Count - r.excludedPlayerAnimators.Count;
            Debug.Log($"[WildlifeFps] {(r.wildlife ? "ON" : "OFF")} run: {r.excludedPlayerAnimators.Count} player Animators excluded from the animal count: {string.Join(", ", r.excludedPlayerAnimators)}");
        }

        static void Place(TestSpawn.Parked player, Terrain terrain, Vector2[] route, float distance, float eye)
        {
            for (int i = 1; i < route.Length; i++)
            {
                Vector2 a = route[i - 1], b = route[i];
                if (a == b) continue;
                float seg = Vector2.Distance(a, b);
                if (distance > seg) { distance -= seg; continue; }
                Vector2 p = Vector2.Lerp(a, b, distance / seg);
                var pos = new Vector3(p.x, 0f, p.y);
                pos.y = Ground(terrain, pos) + eye;
                Vector2 dir = b - a;
                player.Move(pos, Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)));
                return;
            }
        }

        static float Ground(Terrain terrain, Vector3 p) => WashedAshore.Gameplay.TerrainQuery.Height(p); // tiled terrain: the tile under p

        static double Percentile(List<double> v, double q)
        {
            var s = v.OrderBy(x => x).ToList();
            return s[Mathf.Clamp((int)System.Math.Round(q * (s.Count - 1)), 0, s.Count - 1)];
        }

        static string Bool(bool b) => b ? "true" : "false";

        static string Json(RunResult r) =>
            $"{{\"wildlife\":{Bool(r.wildlife)},\"samples\":{r.ms.Count},\"duration_s\":{r.duration:F2}," +
            $"\"median_ms\":{Percentile(r.ms, 0.5):F3},\"p95_ms\":{Percentile(r.ms, 0.95):F3}," +
            $"\"resolution\":\"{r.resolution}\",\"window_mode\":\"{r.windowMode}\",\"vsync\":{r.vSync}," +
            $"\"agents_active\":{r.agentsActive},\"agents_on_navmesh\":{r.agentsOnNavMesh},\"animators_enabled\":{r.animatorsEnabled},\"seed\":{r.seed}}}";

        static void WriteResult(string json)
        {
            // In the Editor this lands in <project>/TestResults; in a test player it lands in
            // persistentDataPath and the [WildlifeFps] log line carries it into the NUnit XML.
            string dir = Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "TestResults")
                : Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "wildlife-fps.json"), json);
        }
    }
}
