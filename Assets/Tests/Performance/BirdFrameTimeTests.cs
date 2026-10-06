using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.PerformanceTesting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Birds;
using WashedAshore.Gameplay;
using WashedAshore.Wildlife;

namespace WashedAshore.Tests.Performance
{
    /// <summary>
    /// B8: frame-time cost of the birds, the WildlifeFrameTimeTests pattern (producer ruling: "the build without
    /// birds" = the Birds root toggled OFF in the same development player). The same scripted walk runs with the
    /// Birds root active and inactive (ON, OFF, ON, OFF; scene reloaded each run; wildlife ON throughout).
    /// Pass: pooled median ms ON &lt;= pooled median ms OFF / 0.9. Test assembly only.
    /// </summary>
    public class BirdFrameTimeTests
    {
        const string WorldScene = "World";
        const string BirdsRoot = "Birds";
        const float WalkSpeed = 5f;
        const float RunSeconds = 40f;
        const float WarmupSeconds = 3f;
        const int Runs = 4;
        const int MinSamples = 1000;
        const float MaxFpsDrop = 0.10f;
        const int Width = 1920, Height = 1080;

        class RunResult
        {
            public bool birds;
            public List<double> ms = new List<double>();
            public float duration;
            public string resolution;
            public int robinsActive, flockBirdsActive, birdAnimatorsEnabled;
        }

        int savedVSync, savedTargetFrameRate;
        bool savedRunInBackground;

        [SetUp]
        public void SetUp()
        {
            savedVSync = QualitySettings.vSyncCount;
            savedTargetFrameRate = Application.targetFrameRate;
            savedRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (!Application.isEditor) Screen.SetResolution(Width, Height, FullScreenMode.Windowed);
        }

        [TearDown]
        public void TearDown()
        {
            QualitySettings.vSyncCount = savedVSync;
            Application.targetFrameRate = savedTargetFrameRate;
            Application.runInBackground = savedRunInBackground;
            WildlifeRandom.OverrideSeed(null);
            BirdRandom.OverrideSeed(null);
        }

        [UnityTest, Performance, Timeout(900000)]
        public IEnumerator BirdsCostAtMostTenPercentFps()
        {
            var runs = new List<RunResult>();
            for (int i = 0; i < Runs; i++)
            {
                var r = new RunResult { birds = i % 2 == 0 };
                runs.Add(r);
                yield return Walk(r);
            }
            var on = runs.Where(r => r.birds).SelectMany(r => r.ms).ToList();
            var off = runs.Where(r => !r.birds).SelectMany(r => r.ms).ToList();
            double medOn = Percentile(on, 0.5), medOff = Percentile(off, 0.5);
            double limitMs = medOff / (1.0 - MaxFpsDrop);
            double drop = 1.0 - medOff / medOn;
            foreach (var v in on) Measure.Custom(new SampleGroup("FrameTime.BirdsOn", SampleUnit.Millisecond), v);
            foreach (var v in off) Measure.Custom(new SampleGroup("FrameTime.BirdsOff", SampleUnit.Millisecond), v);

            string json = "{" +
                $"\"measured_in\":\"{(Application.isEditor ? "editor" : "player")}\",\"platform\":\"{Application.platform}\"," +
                $"\"development\":{Bool(Debug.isDebugBuild)},\"gpu\":\"{SystemInfo.graphicsDeviceName}\"," +
                $"\"quality\":\"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\",\"runs\":[" +
                string.Join(",", runs.Select(Json)) + "]," +
                $"\"pooled\":{{\"on_median_ms\":{medOn:F3},\"off_median_ms\":{medOff:F3}," +
                $"\"on_median_fps\":{1000 / medOn:F1},\"off_median_fps\":{1000 / medOff:F1}," +
                $"\"limit_on_median_ms\":{limitMs:F3},\"fps_drop\":{drop:F4},\"pass\":{Bool(medOn <= limitMs)}}}" +
                "}";
            Debug.Log("[BirdFps] " + json);
            WriteResult(json);

            foreach (var r in runs)
            {
                Assert.GreaterOrEqual(r.ms.Count, MinSamples, $"Run ({(r.birds ? "ON" : "OFF")}) has too few samples");
                if (r.birds)
                {
                    Assert.Greater(r.robinsActive, 0, "ON run had no active robins");
                    Assert.Greater(r.flockBirdsActive, 0, "ON run had no active flock birds");
                }
                else Assert.AreEqual(0, r.birdAnimatorsEnabled, "OFF run still has enabled bird Animators");
            }
            Assert.LessOrEqual(medOn, limitMs, $"Median {medOn:F2} ms with birds vs {medOff:F2} ms without");
        }

        static IEnumerator Walk(RunResult r)
        {
            WildlifeRandom.OverrideSeed(null);
            BirdRandom.OverrideSeed(null); // baked layout, seed 101, identical in both ON runs
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;

            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var birds = roots.FirstOrDefault(g => g.name == BirdsRoot);
            Assert.IsNotNull(birds, $"No root object named {BirdsRoot} in {WorldScene}");
            var wildlife = Object.FindAnyObjectByType<WildlifePopulation>();
            Assert.IsNotNull(wildlife, "No WildlifePopulation");
            var route = WildlifePopulation.Route(wildlife.Tuning, wildlife.PlayerSpawn.position).Select(p => new Vector2(p.x, p.z)).ToArray();
            birds.SetActive(r.birds);

            var player = Object.FindAnyObjectByType<PlayerController>();
            Assert.IsNotNull(player, "No PlayerController");
            player.enabled = false;
            var cc = player.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            Terrain terrain = Terrain.activeTerrain;
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
            r.robinsActive = Object.FindObjectsByType<RobinAgent>(FindObjectsInactive.Exclude).Count(a => a.isActiveAndEnabled);
            r.flockBirdsActive = Object.FindObjectsByType<FlockBird>(FindObjectsInactive.Exclude).Count(a => a.isActiveAndEnabled);
            r.birdAnimatorsEnabled = birds.GetComponentsInChildren<Animator>(false).Count(a => a.isActiveAndEnabled);
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

        static float Ground(Terrain terrain, Vector3 p) => terrain.SampleHeight(p) + terrain.transform.position.y;

        static double Percentile(List<double> v, double q)
        {
            var s = v.OrderBy(x => x).ToList();
            return s[Mathf.Clamp((int)System.Math.Round(q * (s.Count - 1)), 0, s.Count - 1)];
        }

        static string Bool(bool b) => b ? "true" : "false";

        static string Json(RunResult r) =>
            $"{{\"birds\":{Bool(r.birds)},\"samples\":{r.ms.Count},\"duration_s\":{r.duration:F2}," +
            $"\"median_ms\":{Percentile(r.ms, 0.5):F3},\"p95_ms\":{Percentile(r.ms, 0.95):F3},\"resolution\":\"{r.resolution}\"," +
            $"\"robins_active\":{r.robinsActive},\"flock_birds_active\":{r.flockBirdsActive},\"bird_animators_enabled\":{r.birdAnimatorsEnabled}}}";

        static void WriteResult(string json)
        {
            string dir = Application.isEditor
                ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "TestResults")
                : Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "bird-fps.json"), json);
        }
    }
}
