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
                $"\"route_speed_mps\":{WalkSpeed},\"warmup_s\":{WarmupSeconds},\"runs\":[" +
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
                    Assert.AreEqual(0, r.agentsActive, "OFF run still has active NavMeshAgents");
                    Assert.AreEqual(0, r.animatorsEnabled, "OFF run still has enabled Animators");
                }
            }
            Assert.AreEqual(0, agentCreateFailures, "Player logged 'Failed to create agent' (WL-BUG-1)");
            Assert.LessOrEqual(medOn, limitMs, $"Median {medOn:F2} ms with wildlife vs {medOff:F2} ms without");
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
            // Scripted walk: the same path every run, independent of input and physics.
            player.enabled = false;
            var cc = player.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            Terrain terrain = Terrain.activeTerrain;
            Assert.IsNotNull(terrain, "No active Terrain");
            float eye = player.transform.position.y - Ground(terrain, player.transform.position);

            float total = 0f;
            for (int i = 1; i < route.Length; i++) total += Vector2.Distance(route[i - 1], route[i]);

            float t = -WarmupSeconds;
            while (t < RunSeconds)
            {
                Place(player.transform, terrain, route, Mathf.Repeat(Mathf.Max(0f, t) * WalkSpeed, total), eye);
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
            // Animal Animators only: the Birds root (added after this test) stays on in both runs and is not wildlife.
            r.animatorsEnabled = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude)
                .Count(a => a.isActiveAndEnabled && a.GetComponentInParent<WashedAshore.Birds.BirdPopulation>() == null);
        }

        static void Place(Transform player, Terrain terrain, Vector2[] route, float distance, float eye)
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
                player.SetPositionAndRotation(pos, Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)));
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
