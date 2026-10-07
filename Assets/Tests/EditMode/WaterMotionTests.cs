using System;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// Spec W9: WaterMotion.Offset is the analytic sine sum the water shader evaluates, deterministic,
    /// and WaterBody puts it on MapConfig.WaterLevelY. The reference below is written out independently in
    /// double precision; the shader uses the same formula from the globals WaterBody pushes.
    /// </summary>
    public class WaterMotionTests
    {
        const string MotionPath = "Assets/World/Water/WaterMotion.asset";
        const string MapConfigPath = "Assets/World/MapConfig.asset";
        const int Samples = 1000;
        const int Seed = 1987;

        static readonly WaterWave[] TestWaves =
        {
            new WaterWave { amplitude = 0.03f, wavelength = 6f, directionDeg = 30f, speed = 0.8f, phase = 0.4f },
            new WaterWave { amplitude = 0.02f, wavelength = 3.5f, directionDeg = 75f, speed = 0.5f, phase = 2.1f },
            new WaterWave { amplitude = 0.01f, wavelength = 1.7f, directionDeg = -20f, speed = 0.3f, phase = 0f },
        };

        /// <summary>y = sum A sin(2pi/L (sin(dir) x + cos(dir) z - c t) + phi), in doubles.</summary>
        static double Analytic(WaterWave[] waves, double x, double z, double t)
        {
            double h = 0;
            for (int i = 0; i < Math.Min(waves.Length, WaterMotion.MaxWaves); i++)
            {
                var w = waves[i];
                if (w.wavelength <= 0f) continue;
                double rad = w.directionDeg * Math.PI / 180.0;
                h += w.amplitude * Math.Sin(2.0 * Math.PI / w.wavelength * (Math.Sin(rad) * x + Math.Cos(rad) * z - w.speed * t) + w.phase);
            }
            return h;
        }

        static WaterWave[] ProjectWaves()
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<WaterMotionSettings>(MotionPath);
            Assert.IsNotNull(asset, $"No WaterMotionSettings at {MotionPath}");
            return asset.waves;
#else
            return null;
#endif
        }

        static void CompareToAnalytic(WaterWave[] waves, string label, double maxTime = 7200)
        {
            var rng = new System.Random(Seed);
            double worst = 0;
            for (int i = 0; i < Samples; i++)
            {
                // The terrain extent (X -2087..2009, Z -2492..2628) and up to 2 hours of water time.
                float x = (float)(-2087 + rng.NextDouble() * 4096), z = (float)(-2492 + rng.NextDouble() * 5120);
                double t = rng.NextDouble() * maxTime;
                worst = Math.Max(worst, Math.Abs(WaterMotion.Offset(waves, x, z, t) - Analytic(waves, x, z, t)));
            }
            Debug.Log($"WaterMotionTests[{label}]: {Samples} samples (seed {Seed}), max |C# - analytic| = {worst:E2} m, amplitude <= {WaterMotion.MaxAmplitude(waves):F3} m");
            Assert.LessOrEqual(worst, 0.001, $"{label}: C# offset differs from the analytic formula");
        }

        [Test]
        public void Offset_MatchesAnalytic_TestWaves() => CompareToAnalytic(TestWaves, "test waves");

        [Test]
        public void Offset_MatchesAnalytic_ProjectWaves() => CompareToAnalytic(ProjectWaves(), "project WaterMotion.asset");

        /// <summary>w-artist: phases are wrapped in double, so a week-long session keeps the same precision.</summary>
        [Test]
        public void Offset_MatchesAnalytic_LongSession() => CompareToAnalytic(TestWaves, "test waves, t up to 7 days", 7 * 86400);

        [Test]
        public void Phase_IsWrapped()
        {
            var rng = new System.Random(Seed);
            for (int i = 0; i < Samples; i++)
            {
                float p = WaterMotion.Phase(TestWaves[i % TestWaves.Length], rng.NextDouble() * 1e6);
                Assert.That(p, Is.InRange(0f, 2f * Mathf.PI), "phase outside [0, 2pi]");
            }
        }

        static readonly WaterScrollLayer[] TestLayers =
        {
            new WaterScrollLayer { velocity = new Vector2(0.205f, -0.07f), tileSize = 4.1f },
            new WaterScrollLayer { velocity = new Vector2(-0.031f, 0.113f), tileSize = 1.7f },
        };

        static double CircularDelta(double a, double b, double period)
        {
            double d = Math.Abs(a - b) % period;
            return Math.Min(d, period - d);
        }

        /// <summary>
        /// w-td W9 (2): pushed scroll offsets and the flow phase are continuous across their wraps (sampled at t
        /// and t + 1 frame around 10 wrap points, out to a week) and always inside [0, period).
        /// </summary>
        static void CheckScroll(WaterScrollLayer[] layers, float flowPeriod, string label)
        {
            const double dt = 1.0 / 60.0, eps = 1e-3;
            int checkedWraps = 0;
            foreach (var layer in layers)
            {
                if (layer.tileSize <= 0f) continue;
                for (int axis = 0; axis < 2; axis++)
                {
                    double v = axis == 0 ? layer.velocity.x : layer.velocity.y;
                    if (Math.Abs(v) < 1e-6) continue;
                    double wrapEvery = layer.tileSize / Math.Abs(v);
                    for (int k = 0; k < 10; k++)
                    {
                        // Wraps spread from the first one out to about a week.
                        double n = Math.Max(1, Math.Floor(Math.Pow(10, k * 0.6) )), tw = n * wrapEvery;
                        foreach (double t in new[] { tw - dt, tw - dt * 0.5 })
                        {
                            Vector2 a = WaterMotion.ScrollOffset(layer, t), b = WaterMotion.ScrollOffset(layer, t + dt);
                            float av = axis == 0 ? a.x : a.y, bv = axis == 0 ? b.x : b.y;
                            Assert.That(av, Is.GreaterThanOrEqualTo(0f).And.LessThan(layer.tileSize), $"{label}: offset outside [0, tile)");
                            Assert.LessOrEqual(CircularDelta(av, bv, layer.tileSize), Math.Abs(v) * dt + eps, $"{label}: scroll pops at t={t:F3}");
                        }
                        checkedWraps++;
                    }
                }
            }
            if (flowPeriod > 0f)
                for (int k = 1; k <= 10; k++)
                {
                    double tw = Math.Floor(Math.Pow(10, k * 0.55)) * flowPeriod;
                    float a = WaterMotion.FlowPhase(flowPeriod, tw - dt * 0.5), b = WaterMotion.FlowPhase(flowPeriod, tw + dt * 0.5);
                    Assert.That(a, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f), $"{label}: flow phase outside [0, 1)");
                    Assert.LessOrEqual(CircularDelta(a, b, 1.0), dt / flowPeriod + eps, $"{label}: flow phase pops at t={tw:F3}");
                    checkedWraps++;
                }
            Debug.Log($"WaterMotionTests[{label}]: {checkedWraps} wrap points continuous");
        }

        [Test]
        public void Scroll_IsContinuousAcrossWraps_TestLayers() => CheckScroll(TestLayers, 20f, "test layers");

        [Test]
        public void Scroll_IsContinuousAcrossWraps_ProjectLayers()
        {
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<WaterMotionSettings>(MotionPath);
            Assert.IsNotNull(asset, $"No WaterMotionSettings at {MotionPath}");
            Assert.LessOrEqual(asset.scrollLayers.Length, WaterMotion.MaxScrollLayers, "the shader reads at most MaxScrollLayers layers");
            CheckScroll(asset.scrollLayers, asset.flowCyclePeriod, "project WaterMotion.asset");
#endif
        }

        [Test]
        public void Offset_IsDeterministic()
        {
            var rng = new System.Random(Seed);
            for (int i = 0; i < Samples; i++)
            {
                float x = (float)(rng.NextDouble() * 4000 - 2000), z = (float)(rng.NextDouble() * 5000 - 2500), t = (float)(rng.NextDouble() * 600);
                float a = WaterMotion.Offset(TestWaves, x, z, t), b = WaterMotion.Offset(TestWaves, x, z, t);
                Assert.AreEqual(BitConverter.SingleToInt32Bits(a), BitConverter.SingleToInt32Bits(b), $"not bit-identical at ({x},{z},{t})");
            }
        }

        [Test]
        public void Offset_StaysWithinAmplitude()
        {
            float max = WaterMotion.MaxAmplitude(TestWaves);
            var rng = new System.Random(Seed);
            for (int i = 0; i < Samples; i++)
                Assert.LessOrEqual(Mathf.Abs(WaterMotion.Offset(TestWaves, (float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f, (float)rng.NextDouble() * 100f)), max + 1e-5f);
        }

        [Test]
        public void Offset_IgnoresBadAndExtraWaves()
        {
            Assert.AreEqual(0f, WaterMotion.Offset(null, 1f, 2f, 3f));
            Assert.AreEqual(0f, WaterMotion.Offset(new[] { new WaterWave { amplitude = 1f, wavelength = 0f } }, 1f, 2f, 3f));
            var five = new WaterWave[5];
            for (int i = 0; i < 5; i++) five[i] = TestWaves[0];
            Assert.AreEqual(WaterMotion.Offset(five, 3f, 4f, 5f), 4f * WaterMotion.Offset(new[] { TestWaves[0] }, 3f, 4f, 5f), 1e-5f, "only MaxWaves waves count");
        }

        [Test]
        public void ProjectWaves_AreNearFlat()
        {
            var waves = ProjectWaves();
            float amp = WaterMotion.MaxAmplitude(waves);
            Debug.Log($"WaterMotionTests: project waves={waves.Length} max amplitude={amp:F3} m");
            Assert.LessOrEqual(waves.Length, WaterMotion.MaxWaves, "the shader reads at most MaxWaves waves");
            // Greenlight 2: near-flat. The quads are flat at W (normals only), so f must stay inside the W9 5 cm
            // tolerance of the drawn plane; w-artist's own cap is 3 cm.
            Assert.LessOrEqual(amp, 0.05f, "f must stay within 5 cm of the flat water quads");
            // water/tech-pick and w-artist's recipe: the shipped waves total 3 cm or less (w-td W9 condition 3).
            Assert.LessOrEqual(amp, 0.03f + 1e-5f, "shipped WaterMotion.asset total amplitude over the 3 cm recipe");
        }

        [Test]
        public void WaterBody_SurfaceY_IsWaterLevelPlusOffset()
        {
#if UNITY_EDITOR
            var cfg = UnityEditor.AssetDatabase.LoadAssetAtPath<MapConfig>(MapConfigPath);
            Assert.IsNotNull(cfg, $"No MapConfig at {MapConfigPath}");
            var motion = ScriptableObject.CreateInstance<WaterMotionSettings>();
            motion.waves = TestWaves;
            var go = new GameObject("WaterBodyTest");
            try
            {
                var body = go.AddComponent<WaterBody>();
                body.Configure(cfg, motion);
                Assert.AreEqual(cfg.WaterLevelY, body.Level);
                for (float t = 0f; t < 10f; t += 0.7f)
                    Assert.AreEqual(cfg.WaterLevelY + Analytic(TestWaves, 123.4, -567.8, t), body.SurfaceY(123.4f, -567.8f, t), 1e-4);

                // w-artist: Configure pushes at the WaterClock's time (edit mode has no Update to refresh it).
                WaterClock.Use(() => 4321.5);
                body.Configure(cfg, motion);
                Assert.AreEqual(4321.5, body.WaterTime, 1e-9);
                Assert.AreEqual(WaterMotion.Phase(TestWaves[0], 4321.5), Shader.GetGlobalVector("_WaterWaveSpeedPhase0").y, 1e-6f);
            }
            finally
            {
                WaterClock.Use(null);
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(motion);
            }
#endif
        }
    }
}
