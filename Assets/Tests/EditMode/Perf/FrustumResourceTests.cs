using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.Rendering.Universal;
using WashedAshore.Perf;

namespace WashedAshore.Tests.EditMode.Perf
{
    /// <summary>
    /// Phase 0 harness additions (I-4): the mipLimit, lodBias, aa, depthPrime and post variant keys and their global flags,
    /// and the pure parts of ResourceRecorder (frame-timing dedupe, null-not-zero GPU time). The other harness tests are in
    /// Assets/Editor/Perf/Tests/FrustumHarnessTests.cs.
    /// </summary>
    public class FrustumResourceTests
    {
        [TearDown]
        public void ClearProbe() => FrustumProbe.Parse(new string[0]);

        [Test]
        public void Variant_ParsesResourceKeys()
        {
            Assert.IsTrue(TrialVariant.TryParse("Q:mipLimit=1,lodBias=1.5,aa=msaa4,depthPrime=off,post=on", out var v, out var e), e);
            Assert.AreEqual(1, v.mipLimit);
            Assert.AreEqual(1.5f, v.lodBias);
            Assert.AreEqual("msaa4", v.aa);
            Assert.AreEqual("off", v.depthPrime);
            Assert.AreEqual(1, v.post);
            Assert.IsTrue(v.WritesAsset);
            Assert.IsFalse(v.IsAuthored);
            Assert.IsFalse(v.PostImplied);
            var json = v.Json();
            StringAssert.Contains("\"texture_mip_limit\":1", json);
            StringAssert.Contains("\"lod_bias\":1.5", json);
            StringAssert.Contains("\"aa\":\"msaa4\"", json);
            StringAssert.Contains("\"depth_priming\":\"off\"", json);
            StringAssert.Contains("\"post\":1", json);
            StringAssert.Contains("\"post_implied\":false", json);

            foreach (var text in new[] { "M:mipLimit=0", "L:lodBias=2", "P:depthPrime=forced" })
            {
                Assert.IsTrue(TrialVariant.TryParse(text, out v, out e), e);
                Assert.IsTrue(v.WritesAsset, text);
                Assert.IsFalse(v.IsAuthored, text);
            }
            Assert.IsTrue(TrialVariant.TryParse("POST:post=on", out v, out e), e);
            Assert.IsFalse(v.WritesAsset, "post is a camera field");
            Assert.IsFalse(v.IsAuthored);

            Assert.IsTrue(TrialVariant.TryParse("empty", out v, out e), e);
            json = v.Json();
            StringAssert.StartsWith("{\"id\":\"empty\"", json);
            StringAssert.Contains("\"texture_mip_limit\":-1", json);
            StringAssert.Contains("\"aa\":null", json);
            StringAssert.Contains("\"depth_priming\":null", json);
            StringAssert.Contains("\"post\":-1", json);
            StringAssert.Contains("\"post_implied\":false", json);
        }

        [Test]
        public void Variant_AaThatNeedsPostImpliesIt()
        {
            Assert.IsTrue(TrialVariant.TryParse("AA:aa=taa", out var v, out var e), e);
            Assert.IsTrue(v.PostImplied);
            StringAssert.Contains("\"post_implied\":true", v.Json());
            Assert.IsTrue(TrialVariant.TryParse("AA:aa=taa,post=on", out v, out e), e);
            Assert.IsFalse(v.PostImplied);
            Assert.IsTrue(TrialVariant.TryParse("AA:aa=msaa4", out v, out e), e);
            Assert.IsFalse(v.PostImplied, "MSAA does not run in post");
            Assert.IsTrue(TrialVariant.TryParse("AA:aa=taa,depthPrime=forced", out v, out e), "priming with TAA is allowed (MSAA stays 1): " + e);
            Assert.IsTrue(TrialVariant.TryParse("AA:aa=msaa8,depthPrime=off", out v, out e), e);
        }

        [TestCase("x:mipLimit=-1")]
        [TestCase("x:mipLimit=4")]
        [TestCase("x:mipLimit=1.5")]
        [TestCase("x:mipLimit=one")]
        [TestCase("x:lodBias=0")]
        [TestCase("x:lodBias=11")]
        [TestCase("x:lodBias=x")]
        [TestCase("x:aa=msaa3")]
        [TestCase("x:aa=on")]
        [TestCase("x:aa=TAA")]
        [TestCase("x:depthPrime=maybe")]
        [TestCase("x:post=yes")]
        [TestCase("x:aa=msaa4,depthPrime=forced")]
        [TestCase("x:depthPrime=auto,aa=msaa2")]
        [TestCase("x:aa=taa,post=off")]
        [TestCase("x:aa=stp,post=off")]
        public void Variant_RejectsBadResourceKeys(string text)
        {
            Assert.IsFalse(TrialVariant.TryParse(text, out _, out var e));
            Assert.IsNotEmpty(e);
        }

        [Test]
        public void Aa_Mapping()
        {
            Assert.AreEqual(1, TrialVariant.MsaaFor(null));
            Assert.AreEqual(1, TrialVariant.MsaaFor("off"));
            Assert.AreEqual(2, TrialVariant.MsaaFor("msaa2"));
            Assert.AreEqual(4, TrialVariant.MsaaFor("msaa4"));
            Assert.AreEqual(8, TrialVariant.MsaaFor("msaa8"));
            Assert.AreEqual(1, TrialVariant.MsaaFor("taa"));
            Assert.AreEqual(1, TrialVariant.MsaaFor("stp"));
            Assert.AreEqual(AntialiasingMode.None, TrialVariant.CameraAaFor("off"));
            Assert.AreEqual(AntialiasingMode.FastApproximateAntialiasing, TrialVariant.CameraAaFor("fxaa"));
            Assert.AreEqual(AntialiasingMode.SubpixelMorphologicalAntiAliasing, TrialVariant.CameraAaFor("smaa"));
            Assert.AreEqual(AntialiasingMode.TemporalAntiAliasing, TrialVariant.CameraAaFor("taa"));
            Assert.AreEqual(AntialiasingMode.None, TrialVariant.CameraAaFor("stp"), "STP is the asset's upscaling filter, not a camera mode");
            Assert.AreEqual(AntialiasingMode.None, TrialVariant.CameraAaFor("msaa4"));
            Assert.IsTrue(TrialVariant.AaNeedsPost("fxaa") && TrialVariant.AaNeedsPost("smaa") && TrialVariant.AaNeedsPost("taa") && TrialVariant.AaNeedsPost("stp"));
            Assert.IsFalse(TrialVariant.AaNeedsPost("off") || TrialVariant.AaNeedsPost("msaa4") || TrialVariant.AaNeedsPost(null));
            Assert.AreEqual(DepthPrimingMode.Disabled, TrialVariant.DepthPrimingFor("off"));
            Assert.AreEqual(DepthPrimingMode.Auto, TrialVariant.DepthPrimingFor("auto"));
            Assert.AreEqual(DepthPrimingMode.Forced, TrialVariant.DepthPrimingFor("forced"));
        }

        [Test]
        public void Variant_TwinAndTemporalFlags()
        {
            TrialVariant.TryParse("a:far=600", out var v, out _);
            Assert.IsFalse(v.HeavyTwin); Assert.IsFalse(v.Temporal);
            TrialVariant.TryParse("a:lodBias=1.5", out v, out _);
            Assert.IsFalse(v.HeavyTwin, "lodBias only changes LOD selection; the twin stays");
            TrialVariant.TryParse("a:mipLimit=1", out v, out _);
            Assert.IsTrue(v.HeavyTwin);
            TrialVariant.TryParse("a:depthPrime=forced", out v, out _);
            Assert.IsTrue(v.HeavyTwin);
            TrialVariant.TryParse("a:aa=stp", out v, out _);
            Assert.IsTrue(v.HeavyTwin); Assert.IsTrue(v.Temporal);
            TrialVariant.TryParse("a:aa=fxaa", out v, out _);
            Assert.IsFalse(v.Temporal);
        }

        [Test]
        public void Probe_ParsesResourceFlags()
        {
            FrustumProbe.Parse(new[] { "Game.exe", "-bbFpsWalk", "-fcMipLimit", "1", "-fcLodBias", "1.5", "-fcAA", "taa", "-fcDepthPrime", "off", "-fcPost", "on" });
            Assert.IsEmpty(FrustumProbe.Errors, string.Join("; ", FrustumProbe.Errors));
            Assert.AreEqual(1, FrustumProbe.Cli.mipLimit);
            Assert.AreEqual(1.5f, FrustumProbe.Cli.lodBias);
            Assert.AreEqual("taa", FrustumProbe.Cli.aa);
            Assert.AreEqual("off", FrustumProbe.Cli.depthPrime);
            Assert.AreEqual(1, FrustumProbe.Cli.post);
            Assert.IsTrue(FrustumProbe.Cli.WritesAsset);

            FrustumProbe.Parse(new[] { "Game.exe", "-bbFpsWalk", "-fcMipLimit", "7" });
            Assert.AreEqual(1, FrustumProbe.Errors.Count, string.Join("; ", FrustumProbe.Errors));
            Assert.AreEqual(-1, FrustumProbe.Cli.mipLimit);
            FrustumProbe.Parse(new[] { "Game.exe", "-bbFpsWalk", "-fcMipLimit", "1.5", "-fcLodBias", "0", "-fcAA", "msaa3", "-fcDepthPrime", "yes", "-fcPost", "maybe" });
            Assert.AreEqual(5, FrustumProbe.Errors.Count, string.Join("; ", FrustumProbe.Errors));
            Assert.IsTrue(FrustumProbe.Cli.IsAuthored);
        }

        [Test]
        public void Probe_GlobalFlagCombinationsAreErrors()
        {
            FrustumProbe.Parse(new[] { "Game.exe", "-bbFpsWalk", "-fcAA", "msaa4", "-fcDepthPrime", "forced" });
            Assert.AreEqual(1, FrustumProbe.Errors.Count, string.Join("; ", FrustumProbe.Errors));
            FrustumProbe.Parse(new[] { "Game.exe", "-bbFpsWalk", "-fcAA", "smaa", "-fcPost", "off" });
            Assert.AreEqual(1, FrustumProbe.Errors.Count, string.Join("; ", FrustumProbe.Errors));
        }

        [Test]
        public void Probe_GlobalFlagsBesideVariantsAreAnError()
        {
            // With -fcVariant a spot run never uses the "cli" variant, so global trial flags would be silently ignored.
            FrustumProbe.Parse(new[] { "Game.exe", "-fcSpots", "park", "-fcVariant", "AA:aa=msaa4", "-fcAA", "taa" });
            CollectionAssert.AreEqual(new[] { "AA" }, FrustumProbe.VariantList.Select(x => x.id).ToArray());
            Assert.AreEqual(1, FrustumProbe.Errors.Count, string.Join("; ", FrustumProbe.Errors));
            // Without -fcVariant they form the one variant, as before.
            FrustumProbe.Parse(new[] { "Game.exe", "-fcSpots", "park", "-fcAA", "taa" });
            CollectionAssert.AreEqual(new[] { "cli" }, FrustumProbe.VariantList.Select(x => x.id).ToArray());
            Assert.IsEmpty(FrustumProbe.Errors, string.Join("; ", FrustumProbe.Errors));
        }

        [Test]
        public void FrameTimingJson_GpuIsNullNotZero()
        {
            var cpu = new List<double> { 8, 9, 10 };
            var none = new List<double>();
            string allZero = ResourceRecorder.FrameTimingJson(true, 120, cpu, cpu, cpu, none, 3);
            StringAssert.Contains("\"gpu_frame_ms\":null", allZero);
            StringAssert.Contains("\"gpu_zero_frames\":3", allZero);
            StringAssert.Contains("\"gpu_null_reason\":\"" + ResourceRecorder.ReasonGpuZero + "\"", allZero);
            StringAssert.Contains("\"timed_frames\":120,\"frames_accepted\":3", allZero);
            StringAssert.Contains("\"cpu_frame_ms\":{\"count\":3,\"median\":9,", allZero);

            string off = ResourceRecorder.FrameTimingJson(false, 120, none, none, none, none, 0);
            StringAssert.Contains("\"enabled\":false", off);
            StringAssert.Contains("\"cpu_frame_ms\":null", off);
            StringAssert.Contains("\"gpu_frame_ms\":null", off);
            StringAssert.Contains("\"gpu_null_reason\":\"" + ResourceRecorder.ReasonStatsOff + "\"", off);

            string empty = ResourceRecorder.FrameTimingJson(true, 120, none, none, none, none, 0);
            StringAssert.Contains("\"gpu_null_reason\":\"" + ResourceRecorder.ReasonNoFrames + "\"", empty);

            string ok = ResourceRecorder.FrameTimingJson(true, 120, cpu, cpu, cpu, new List<double> { 4, 5, 6 }, 1);
            StringAssert.Contains("\"gpu_frame_ms\":{\"count\":3,\"median\":5,\"p95\":6,", ok);
            StringAssert.Contains("\"gpu_null_reason\":null", ok);
            StringAssert.DoesNotContain("\"gpu_frame_ms\":0", ok + allZero + off + empty);
        }

        [Test]
        public void FrameTiming_DedupeTakesBurstsOldestFirst()
        {
            Assert.IsFalse(ResourceRecorder.Accept(10, 10));
            Assert.IsFalse(ResourceRecorder.Accept(10, 9));
            Assert.IsTrue(ResourceRecorder.Accept(10, 11));

            // GetLatestTimings fills newest first; a burst of four new frames arrives in one capture.
            var stamps = new ulong[] { 14, 13, 12, 11, 10, 9, 0, 0 };
            var idx = ResourceRecorder.NewerInOrder(10, stamps, 6, out ulong last);
            CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, idx.ToArray(), "11, 12, 13, 14 in that order");
            Assert.AreEqual(14UL, last);

            // The next capture overlaps the last one: only the new frames count.
            stamps = new ulong[] { 16, 15, 14, 13, 12, 11, 10, 9 };
            idx = ResourceRecorder.NewerInOrder(last, stamps, 8, out last);
            CollectionAssert.AreEqual(new[] { 1, 0 }, idx.ToArray());
            Assert.AreEqual(16UL, last);

            // Nothing new: the floor stays; a repeated stamp is taken once; entries past count are ignored.
            idx = ResourceRecorder.NewerInOrder(16, new ulong[] { 16, 15 }, 2, out last);
            Assert.IsEmpty(idx); Assert.AreEqual(16UL, last);
            idx = ResourceRecorder.NewerInOrder(16, new ulong[] { 18, 18, 17, 99 }, 3, out last);
            CollectionAssert.AreEqual(new[] { 2, 0 }, idx.ToArray());
            Assert.AreEqual(18UL, last);

            // The allocation-free form gives the same answer into a caller's buffer.
            var buf = new int[ResourceRecorder.TimingBatch];
            int k = ResourceRecorder.NewerInOrder(10, new ulong[] { 14, 13, 12, 11, 10, 9 }, 6, buf, out last);
            Assert.AreEqual(4, k);
            CollectionAssert.AreEqual(new[] { 3, 2, 1, 0 }, buf.Take(k).ToArray());
            Assert.AreEqual(14UL, last);
        }

        [Test]
        public void FrameTiming_FloorIsSetOnFirstSampleNotAtConstruction()
        {
            var floor = new ResourceRecorder.TimingFloor();
            var idx = new int[ResourceRecorder.TimingBatch];

            // No timing captured yet: nothing accepted and no floor.
            Assert.AreEqual(0, floor.Feed(new ulong[0], 0, idx));
            Assert.IsFalse(floor.IsSet);

            // First timed frame after a long warm-up: all 8 latest timings are warm-up frames. None is accepted; the
            // floor becomes the newest of them.
            var warmup = new ulong[] { 1008, 1007, 1006, 1005, 1004, 1003, 1002, 1001 };
            Assert.AreEqual(0, floor.Feed(warmup, 8, idx));
            Assert.IsTrue(floor.IsSet);
            Assert.AreEqual(1008UL, floor.Last);

            // Next frame: only the timing newer than the floor counts.
            int k = floor.Feed(new ulong[] { 1009, 1008, 1007, 1006, 1005, 1004, 1003, 1002 }, 8, idx);
            Assert.AreEqual(1, k);
            Assert.AreEqual(0, idx[0]);
            Assert.AreEqual(1009UL, floor.Last);

            // A burst of two, then nothing new.
            k = floor.Feed(new ulong[] { 1011, 1010, 1009 }, 3, idx);
            CollectionAssert.AreEqual(new[] { 1, 0 }, idx.Take(k).ToArray());
            Assert.AreEqual(0, floor.Feed(new ulong[] { 1011, 1010 }, 2, idx));
            Assert.AreEqual(1011UL, floor.Last);
        }

        [Test]
        public void Summary_NullWhenEmptyAndBytesPrintExactly()
        {
            Assert.AreEqual("null", ResourceRecorder.SummaryOrNull(new List<double>()));
            Assert.AreEqual("null", ResourceRecorder.SummaryOrNull(null));
            StringAssert.StartsWith("{\"count\":1,\"median\":3221225472,", ResourceRecorder.SummaryOrNull(new List<double> { 3221225472d }));
            Assert.AreEqual("3221225472", FrustumMath.F(3221225472d));
            Assert.AreEqual("17179869184", FrustumMath.F((double)(16UL << 30)));
        }
    }
}
