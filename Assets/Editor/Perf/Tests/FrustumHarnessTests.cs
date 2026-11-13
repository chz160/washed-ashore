using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace WashedAshore.Perf.Tests
{
    /// <summary>Pure logic of the #9 frustum harness: statistics, argument and pose parsing, LOD and fog maths, the pose table.</summary>
    public class FrustumHarnessTests
    {
        [TearDown]
        public void ClearProbe() => FrustumProbe.Parse(new string[0]);

        [Test]
        public void Percentile_IsNearestRankLikeTheWalk()
        {
            var s = new List<double> { 1, 2, 3, 4 };
            // index = round(q * (n - 1)), System.Math.Round (to even), as BellsBendFpsWalk computes it.
            Assert.AreEqual(3, FrustumMath.Percentile(s, 0.5));   // round(1.5) = 2
            Assert.AreEqual(4, FrustumMath.Percentile(s, 0.95));  // round(2.85) = 3
            Assert.AreEqual(1, FrustumMath.Percentile(s, 0.0));
            Assert.AreEqual(0, FrustumMath.Percentile(new List<double>(), 0.5));
        }

        [Test]
        public void Summarize_SortsAndReportsTheTail()
        {
            var samples = Enumerable.Range(1, 100).Select(i => (double)(101 - i)).ToList();   // 100 .. 1, unsorted
            var s = FrustumMath.Summarize(samples);
            Assert.AreEqual(100, s.count);
            Assert.AreEqual(51, s.median);    // round(49.5) = 50 (to even)
            Assert.AreEqual(95, s.p95);       // round(94.05) = 94
            Assert.AreEqual(99, s.p99);       // round(98.01) = 98
            Assert.AreEqual(100, s.max);
            Assert.AreEqual(50.5, s.mean, 1e-9);
            StringAssert.StartsWith("{\"count\":100,\"median\":51,", FrustumMath.Json(s));
            Assert.AreEqual(0, FrustumMath.Summarize(new double[0]).count);
        }

        [Test]
        public void Pose_ParsesWithAndWithoutHeight()
        {
            Assert.IsTrue(FrustumMath.TryParsePose("bank=1077.9,-49.6,322.8,5", 1.7f, out var p, out var e), e);
            Assert.AreEqual("bank", p.name);
            Assert.AreEqual(1077.9f, p.x, 1e-4f); Assert.AreEqual(-49.6f, p.z, 1e-4f);
            Assert.AreEqual(322.8f, p.yaw, 1e-4f); Assert.AreEqual(5f, p.pitch, 1e-4f);
            Assert.AreEqual(1.7f, p.heightAboveGround, 1e-6f);
            Assert.IsTrue(FrustumMath.TryParsePose("cab=548,-230,226.5,0,27.94", 1.7f, out p, out e), e);
            Assert.AreEqual(27.94f, p.heightAboveGround, 1e-4f);
        }

        [TestCase("")]
        [TestCase("noequals")]
        [TestCase("a=1,2,3")]
        [TestCase("a=1,2,3,4,5,6")]
        [TestCase("a=1,x,3,4")]
        [TestCase("a=1,2,3,4,0")]
        [TestCase("=1,2,3,4")]
        public void Pose_RejectsBadText(string text)
        {
            Assert.IsFalse(FrustumMath.TryParsePose(text, 1.7f, out _, out var e));
            Assert.IsNotEmpty(e);
        }

        [Test]
        public void LayerCull_Parses()
        {
            Assert.IsTrue(FrustumMath.TryParseLayerCull("Prop=150", out var l, out var m, out _));
            Assert.AreEqual("Prop", l); Assert.AreEqual(150f, m);
            Assert.IsTrue(FrustumMath.TryParseLayerCull("Ignore Raycast=0", out l, out m, out _));
            Assert.AreEqual("Ignore Raycast", l); Assert.AreEqual(0f, m);
            Assert.IsFalse(FrustumMath.TryParseLayerCull("Prop=-1", out _, out _, out _));
            Assert.IsFalse(FrustumMath.TryParseLayerCull("Prop", out _, out _, out _));
            Assert.IsFalse(FrustumMath.TryParseLayerCull("=5", out _, out _, out _));
        }

        [Test]
        public void YawToward_IsCompassDegrees()
        {
            Assert.AreEqual(0f, FrustumMath.YawToward(0, 0, 0, 10), 1e-4f);
            Assert.AreEqual(90f, FrustumMath.YawToward(0, 0, 10, 0), 1e-4f);
            Assert.AreEqual(180f, FrustumMath.YawToward(0, 0, 0, -10), 1e-4f);
            Assert.AreEqual(270f, FrustumMath.YawToward(0, 0, -10, 0), 1e-4f);
        }

        [Test]
        public void Lod_SelectsLevelOrCulls()
        {
            // 10 m object at 100 m, fov 60: 5 / (100 * tan 30) = 0.0866.
            float rh = FrustumMath.RelativeHeight(10f, 100f, 60f, 1f);
            Assert.AreEqual(0.0866f, rh, 1e-3f);
            Assert.AreEqual(2f * rh, FrustumMath.RelativeHeight(10f, 100f, 60f, 2f), 1e-5f);
            var t = new[] { 0.5f, 0.1f, 0.02f };
            Assert.AreEqual(0, FrustumMath.LodIndex(0.6f, t));
            Assert.AreEqual(2, FrustumMath.LodIndex(rh, t));
            Assert.AreEqual(-1, FrustumMath.LodIndex(0.01f, t));
            Assert.IsTrue(float.IsPositiveInfinity(FrustumMath.RelativeHeight(10f, 0f, 60f, 1f)));
        }

        [Test]
        public void Fog_FractionAndLayerCulling()
        {
            Assert.AreEqual(0f, FrustumMath.FogFraction(100f, 120f, 520f));
            Assert.AreEqual(0.5f, FrustumMath.FogFraction(320f, 120f, 520f), 1e-5f);
            Assert.AreEqual(1f, FrustumMath.FogFraction(900f, 120f, 520f));
            var box = new Bounds(new Vector3(30f, 0f, 200f), new Vector3(2f, 2f, 2f));
            // Planar: view depth along +Z is 199; spherical: nearest point is about 201 m away.
            Assert.IsFalse(FrustumMath.LayerCulled(box, Vector3.zero, Vector3.forward, 0f, false));
            Assert.IsFalse(FrustumMath.LayerCulled(box, Vector3.zero, Vector3.forward, 200f, false));
            Assert.IsTrue(FrustumMath.LayerCulled(box, Vector3.zero, Vector3.forward, 200f, true));
            Assert.IsTrue(FrustumMath.LayerCulled(box, Vector3.zero, Vector3.forward, 150f, false));
            Assert.AreEqual(199f, FrustumMath.MinViewDepth(box, Vector3.zero, Vector3.forward), 1e-4f);
        }

        [Test]
        public void Spots_DefaultsMatchTheProtocol()
        {
            var poses = FrustumSpots.Defaults(27.94f);
            CollectionAssert.AreEqual(FrustumSpots.StaticSpots, poses.Select(p => p.name).ToArray());
            var park = poses[0];
            Assert.AreEqual(-202.8f, park.x, 1e-4f); Assert.AreEqual(-942.1f, park.z, 1e-4f);   // issue #9 PlayerSpawn
            Assert.AreEqual(FrustumSpots.Eye, park.heightAboveGround);
            // Pose-check winners (#25 note_technical-director_pose_winners).
            var ridge = poses.Single(p => p.name == FrustumSpots.RidgeTop);
            Assert.AreEqual(568f, ridge.x, 1e-4f); Assert.AreEqual(-180f, ridge.z, 1e-4f); Assert.AreEqual(45f, ridge.yaw, 1e-4f);
            var bank = poses.Single(p => p.name == FrustumSpots.RiverBank);
            Assert.AreEqual(1027.2f, bank.x, 1e-4f); Assert.AreEqual(13.9f, bank.z, 1e-4f); Assert.AreEqual(321.137f, bank.yaw, 0.01f);
            var cab = poses.Single(p => p.name == FrustumSpots.Cab);
            Assert.AreEqual(27.94f, cab.heightAboveGround, 1e-5f);
            Assert.IsTrue(poses.All(p => !string.IsNullOrEmpty(p.source)));
            Assert.IsTrue(poses.All(p => p.yaw >= 0f && p.yaw < 360f));
            var moved = FrustumSpots.WithOverrides(poses, new[] { new SpotPose(FrustumSpots.Park, 1, 2, 3, 4, 5, "t"), new SpotPose("extra", 0, 0, 0, 0, 1.7f, "t") });
            Assert.AreEqual(6, moved.Count);
            Assert.AreEqual(1f, moved[0].x);
            Assert.AreEqual(-202.8f, poses[0].x, 1e-4f, "overrides must not change the defaults");
        }

        [Test]
        public void Variant_ParsesKeys()
        {
            Assert.IsTrue(TrialVariant.TryParse("far600:far=600,farCab=1150,cull.Wildlife=150,cull.Birds=200,detail=40,shadows=off,spherical=on", out var v, out var e), e);
            Assert.AreEqual("far600", v.id);
            Assert.AreEqual(600f, v.far); Assert.AreEqual(1150f, v.farCab);
            Assert.AreEqual(1150f, v.FarFor(true)); Assert.AreEqual(600f, v.FarFor(false));
            Assert.AreEqual(150f, v.layerCull["Wildlife"]); Assert.AreEqual(200f, v.layerCull["Birds"]);
            Assert.AreEqual(40f, v.detail);
            Assert.IsTrue(v.shadowsOff); Assert.IsTrue(v.spherical);
            Assert.IsFalse(v.WritesAsset);
            Assert.IsFalse(v.IsAuthored);
            Assert.IsTrue(TrialVariant.TryParse("occ:grdOcc=on,smallMesh=2.5,shadowDist=0", out v, out e), e);
            Assert.AreEqual(1, v.grdOcclusion); Assert.AreEqual(2.5f, v.smallMesh); Assert.AreEqual(0f, v.shadowDist);
            Assert.IsTrue(v.WritesAsset);
            Assert.IsTrue(TrialVariant.TryParse("FISHOFF:fishOff=on", out v, out e), e);
            Assert.IsTrue(v.fishOff); Assert.IsFalse(v.IsAuthored); Assert.IsFalse(v.WritesAsset);
            StringAssert.Contains("\"fish_off\":true", v.Json());
            Assert.IsFalse(TrialVariant.TryParse("x:fishOff=off", out _, out _));
            Assert.IsTrue(TrialVariant.TryParse("empty", out v, out e), e);
            Assert.IsTrue(v.IsAuthored);
            Assert.AreEqual(-1f, v.FarFor(true));
            StringAssert.StartsWith("{\"id\":\"empty\"", v.Json());
        }

        [TestCase("")]
        [TestCase(":far=600")]
        [TestCase("A:far=600")]
        [TestCase("x:far=0")]
        [TestCase("x:far=abc")]
        [TestCase("x:bogus=1")]
        [TestCase("x:shadows=on")]
        [TestCase("x:grdOcc=maybe")]
        [TestCase("x:smallMesh=25")]
        [TestCase("x:cull.=5")]
        [TestCase("x:cull.Prop=-1")]
        [TestCase("x:far")]
        public void Variant_RejectsBadText(string text)
        {
            Assert.IsFalse(TrialVariant.TryParse(text, out _, out var e));
            Assert.IsNotEmpty(e);
        }

        [Test]
        public void Variant_WindowsAreABA()
        {
            TrialVariant.TryParse("v1:far=600", out var v1, out _);
            TrialVariant.TryParse("v2:far=550", out var v2, out _);
            var w = TrialVariant.Windows(new[] { v1, v2 });
            CollectionAssert.AreEqual(new[] { "A", "v1", "A", "v2", "A" }, w.Select(x => x.id).ToArray());
            Assert.IsTrue(w[0].IsAuthored && w[2].IsAuthored && w[4].IsAuthored);
            Assert.AreEqual(1, TrialVariant.Windows(new TrialVariant[0]).Count);
        }

        [Test]
        public void Probe_ParsesSpotRun()
        {
            FrustumProbe.Parse(new[] { "Game.exe", "-fcSpots", "cab", "-fcVariant", "far1150:farCab=1150", "-fcVariant", "noshadow:shadows=off",
                "-fcRun", "2", "-fcArm", "q2", "-fcPose", "cab=548,-230,226.5,0,27.94", "-fcSettle", "3", "-fcYawSpeed", "30", "-fcWarmup", "15" });
            Assert.AreEqual(15f, FrustumProbe.Warmup);
            Assert.IsTrue(FrustumProbe.Active);
            Assert.IsTrue(FrustumProbe.SpotMode);
            CollectionAssert.AreEqual(new[] { "cab" }, FrustumProbe.Spots.ToArray());
            CollectionAssert.AreEqual(new[] { "far1150", "noshadow" }, FrustumProbe.VariantList.Select(v => v.id).ToArray());
            Assert.AreEqual(2, FrustumProbe.Run);
            Assert.AreEqual(1, FrustumProbe.Overrides.Count);
            Assert.IsEmpty(FrustumProbe.Errors, string.Join("; ", FrustumProbe.Errors));
        }

        [Test]
        public void Probe_GlobalFlagsFormTheCliVariant()
        {
            FrustumProbe.Parse(new[] { "Game.exe", "-bbFpsWalk", "-fcFar", "600", "-fcLayerCull", "Wildlife=150", "-fcShadowsOff", "-fcGrdOcc", "on", "-fcSmallMesh", "1" });
            Assert.IsTrue(FrustumProbe.Active);
            Assert.IsFalse(FrustumProbe.SpotMode);
            Assert.AreEqual(600f, FrustumProbe.Cli.far);
            Assert.AreEqual(150f, FrustumProbe.Cli.layerCull["Wildlife"]);
            Assert.IsTrue(FrustumProbe.Cli.shadowsOff);
            Assert.AreEqual(1, FrustumProbe.Cli.grdOcclusion);
            Assert.AreEqual(1f, FrustumProbe.Cli.smallMesh);
            Assert.IsEmpty(FrustumProbe.Errors);
            // A spot run with no -fcVariant uses the global flags as its one variant.
            FrustumProbe.Parse(new[] { "Game.exe", "-fcSpots", "park", "-fcFar", "600" });
            CollectionAssert.AreEqual(new[] { "cli" }, FrustumProbe.VariantList.Select(v => v.id).ToArray());
        }

        [Test]
        public void Probe_SpotListAndErrors()
        {
            FrustumProbe.Parse(new[] { "Game.exe", "-fcSpots", "park,cab", "-fcFar", "-fcBogus", "-fcLayerCull", "Prop", "-fcVariant", "v:far=1", "-fcVariant", "v:far=2" });
            CollectionAssert.AreEqual(new[] { "park", "cab" }, FrustumProbe.Spots.ToArray());
            Assert.AreEqual(-1f, FrustumProbe.Cli.far);
            Assert.AreEqual(4, FrustumProbe.Errors.Count, string.Join("; ", FrustumProbe.Errors));
        }

        [Test]
        public void StillViews_CompassThenOwnYaw()
        {
            var v = FrustumProbe.StillViews(225.3f);
            CollectionAssert.AreEqual(new[] { "000", "090", "180", "270", "own" }, v.Select(x => x.label).ToArray());
            Assert.AreEqual(90f, v[1].yaw);
            Assert.AreEqual(225.3f, v[4].yaw, 1e-4f);
        }

        [Test]
        public void CountJson_ShareMaxMean()
        {
            Assert.AreEqual("{\"frames\":5,\"share_at_least_1\":0.4,\"max\":3,\"mean\":0.8}", FrustumMath.CountJson(new[] { 0, 3, 0, 1, 0 }, 1));
            Assert.AreEqual("{\"frames\":0,\"share_at_least_1\":0,\"max\":0,\"mean\":0}", FrustumMath.CountJson(new int[0], 1));
            Assert.AreEqual("fish_", FrustumProbe.FishSpotPrefix);
        }

        [Test]
        public void FishDrawCounter_CountsDrawnModesOnly()
        {
            var modes = new[] { WashedAshore.Fish.FishDrawMode.None, WashedAshore.Fish.FishDrawMode.Body, WashedAshore.Fish.FishDrawMode.Shadow, WashedAshore.Fish.FishDrawMode.Body };
            Assert.AreEqual(3, FishDrawCounter.Count(modes, 4));
            Assert.AreEqual(1, FishDrawCounter.Count(modes, 2), "only the live entries count");
            Assert.AreEqual(3, FishDrawCounter.Count(modes, 99), "live beyond the array is clamped");
            Assert.AreEqual(0, FishDrawCounter.Count(null, 4));
        }

        [Test]
        public void FishDrawCounter_RunsAfterTheFishRenderer()
        {
            // DrawModes are cleared in FishPopulation.Update and refilled in FishRenderer.LateUpdate; the counter must run later.
            int Order(System.Type t) => ((DefaultExecutionOrder)System.Attribute.GetCustomAttribute(t, typeof(DefaultExecutionOrder)))?.order ?? 0;
            int renderer = Order(typeof(WashedAshore.Fish.Rendering.FishRenderer));
            int population = Order(typeof(WashedAshore.Fish.FishPopulation));
            Assert.AreEqual(FishDrawCounter.Order, Order(typeof(FishDrawCounter)));
            Assert.Greater(Order(typeof(FishDrawCounter)), renderer);
            Assert.Greater(renderer, population);
            Assert.IsNotNull(typeof(FishDrawCounter).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic), "counts in LateUpdate");
        }

        [Test]
        public void Probe_InertWithoutFlags()
        {
            FrustumProbe.Parse(new[] { "Game.exe", "-bbFpsWalk", "-fvFar", "1000" });
            Assert.IsFalse(FrustumProbe.Active);
            Assert.IsFalse(FrustumProbe.SpotMode);
        }
    }
}
