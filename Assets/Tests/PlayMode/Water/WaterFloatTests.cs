using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// Spec W9: a floating test crate in open water stays within 5 cm of the surface the shader draws,
    /// f(x, z, t) on MapConfig.WaterLevelY, for 30 s at a pinned 60 fps. The expected height is the
    /// shader's own sum, evaluated here from the globals the shader was given this frame (_WaterLevelY,
    /// _WaterWave{i}, _WaterWaveSpeedPhase{i}.y), not from WaterBody.SurfaceY, so a drift between the pushed
    /// globals and the C# sample would show up.
    /// </summary>
    public class WaterFloatTests
    {
        const string WorldScene = "World";
        const float Seconds = 30f;
        const float Tolerance = 0.05f;

        Camera disabledCamera;
        GameObject crate;

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (crate) Object.Destroy(crate);
            if (disabledCamera) disabledCamera.enabled = true;
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator Crate_BobsWithin5cmOfSurface()
        {
            SceneManager.LoadScene(WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            disabledCamera = Camera.main;
            if (disabledCamera) disabledCamera.enabled = false;
            var water = WaterTestKit.Water();
            float w = water.Config.WaterLevelY;
            var rule = WorldBoundsTestKit.Clamp().Rule;

            // Open water: the deepest of the first low-bank stations' offshore points, well south of the line.
            var low = WaterTestKit.LowBank(WaterTestKit.Stations(), rule, 200f);
            Vector3 at = default;
            bool found = false;
            foreach (var s in low)
                if (WaterTestKit.Offshore(s.Position, w, 60f, out var dir) && WaterTestKit.Ground(s.Position + dir * 60f, out float h) && w - h > 5f)
                {
                    at = s.Position + dir * 60f;
                    found = true;
                    break;
                }
            Assert.IsTrue(found, "No open water 60 m off a low bank");

            crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crate.name = "W9TestCrate";
            // Spawned the way a game spawns a floating body: placed first, then floated by WaterFloat.OnEnable.
            crate.transform.position = at;
            crate.AddComponent<Rigidbody>();
            var floater = crate.AddComponent<WaterFloat>();
            // w-td: a body is floated in the frame it spawns, before any physics step.
            Assert.LessOrEqual(Mathf.Abs(crate.transform.position.y + floater.Draft - water.SurfaceY(at.x, at.z, WaterClock.Now)), 0.05f,
                "crate drawn at its spawn height in its first frame");
            // Wait one physics step before sampling, so the samples never depend on frame/physics timing.
            yield return new WaitForFixedUpdate();
            yield return null;

            var waves = water.Motion ? water.Motion.waves : new WaterWave[0];
            var waveIds = new int[WaterMotion.MaxWaves];
            var phaseIds = new int[WaterMotion.MaxWaves];
            for (int i = 0; i < WaterMotion.MaxWaves; i++)
            {
                waveIds[i] = Shader.PropertyToID($"_WaterWave{i}");
                phaseIds[i] = Shader.PropertyToID($"_WaterWaveSpeedPhase{i}");
            }
            // WaterSurface.hlsl: h = sum A sin(k (d . xz) - phi).
            float ShaderHeight(float x, float z)
            {
                float h = Shader.GetGlobalFloat(WaterBody.LevelId);
                for (int i = 0; i < WaterMotion.MaxWaves; i++)
                {
                    Vector4 a = Shader.GetGlobalVector(waveIds[i]);
                    h += a.x * Mathf.Sin(a.y * (a.z * x + a.w * z) - Shader.GetGlobalVector(phaseIds[i]).y);
                }
                return h;
            }
            float worst = 0f, minY = float.MaxValue, maxY = float.MinValue;
            int frames = 0;
            for (float t = 0f; t < Seconds; t += Time.deltaTime, frames++)
            {
                yield return null;
                var p = crate.transform.position;
                float expected = ShaderHeight(p.x, p.z);
                worst = Mathf.Max(worst, Mathf.Abs(p.y + floater.Draft - expected));
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            Debug.Log($"WaterFloatTests: crate at ({at.x:F1},{at.z:F1}) frames={frames} worst |crate - surface| = {worst:F4} m, " +
                      $"bob range {maxY - minY:F3} m, wave amplitude <= {WaterMotion.MaxAmplitude(waves):F3} m");
            Assert.LessOrEqual(worst, Tolerance, "crate off the visual surface");
            // It actually bobs: a crate parked at W would pass the 5 cm bar on flat-ish water too.
            float amp = WaterMotion.MaxAmplitude(waves);
            if (amp > 0f) Assert.Greater(maxY - minY, 0.25f * amp, "crate does not bob with the waves");
            Assert.AreEqual(Shader.GetGlobalFloat(WaterBody.LevelId), w, 1e-6f, "shader water level");
        }
    }
}
