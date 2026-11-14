using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace WashedAshore.Perf
{
    /// <summary>
    /// Memory and frame-timing recorder for one FrustumProbe window or route (Phase 0, harness gap I-4). Descriptive only:
    /// nothing here is part of S1 unless a plan pre-registers it with its own threshold.
    ///
    /// Frame timing (FrameTimingManager; needs PlayerSettings enableFrameTimingStats 1, on since Phase 0): every Sample()
    ///   captures the timings and reads the latest <see cref="TimingBatch"/>. Timings arrive in bursts, so every entry newer
    ///   than the last accepted frameStartTimestamp is taken, oldest first (<see cref="TimingFloor"/>). The floor is set on
    ///   the first Sample() that sees a timing, and that call accepts nothing, so a warm-up between construction and the
    ///   first timed frame (the route's 15 s) is never counted. Timings lag the frame by a few frames (about 2-4, not
    ///   measured here), so the accepted set is the timed window shifted by that lag: up to that many frames from just
    ///   before the window (the settle, or the last warm-up frames) are counted, and the same number of the window's last
    ///   frames are not. frames_accepted is about timed_frames - 1 whatever the lag, so it does not show the shift. A GPU
    ///   time of 0 means the API or driver gave none: it is counted in gpu_zero_frames and never stored, and with no GPU
    ///   sample at all gpu_frame_ms is null with a gpu_null_reason. GPU time is never reported as 0.
    ///   Sample() allocates nothing on the timing path (fixed buffers, lists pre-sized to <see cref="ListCapacity"/>), so
    ///   the recorder does not add to the gc_* figures it records beyond an occasional list growth on a long route.
    /// Memory (ProfilerRecorder, Memory category): Total, GC, Gfx and System used/reserved bytes per sampled frame. "Texture
    ///   Memory" and "Mesh Memory" are filled only in a development player; null otherwise. A counter whose name does not
    ///   exist gives an invalid recorder and null, never an error.
    /// Texture snapshot (TextureSnapshotJson): Unity's static Texture counters. Per Unity's scripting docs these cover
    ///   Texture2D and Cubemap instances only, not RenderTextures, MSAA targets, texture arrays or textures Unity makes
    ///   internally, so an aa= change shows in memory.gfx_used, not here. desired/target bytes mean something only with
    ///   mipmap streaming on, so they are null while QualitySettings.streamingMipmapsActive is false. Whether current bytes
    ///   follow globalTextureMipmapLimit with streaming off is not yet verified (first MIP1 launch checks it).
    /// </summary>
    public sealed class ResourceRecorder : System.IDisposable
    {
        public const int TimingBatch = 8;
        public const int ListCapacity = 4096;
        public const string ReasonStatsOff = "frame timing stats off";
        public const string ReasonNoFrames = "no frames captured";
        public const string ReasonGpuZero = "gpu time 0 on every frame (unsupported on this API or driver)";

        static readonly string[] MemoryCounters =
        {
            "Total Used Memory", "Total Reserved Memory", "GC Used Memory", "GC Reserved Memory", "Gfx Used Memory", "Gfx Reserved Memory",
            "System Used Memory", "Texture Memory", "Mesh Memory",
        };

        readonly ProfilerRecorder[] mem;
        readonly List<double>[] memVals;
        readonly FrameTiming[] buf = new FrameTiming[TimingBatch];
        readonly ulong[] stamps = new ulong[TimingBatch];
        readonly int[] order = new int[TimingBatch];
        readonly TimingFloor floor = new TimingFloor();
        readonly List<double> cpu = new List<double>(ListCapacity), main = new List<double>(ListCapacity),
                              render = new List<double>(ListCapacity), gpu = new List<double>(ListCapacity);
        readonly bool timingOn;
        int timedFrames, gpuZero;

        public ResourceRecorder()
        {
            mem = MemoryCounters.Select(c => ProfilerRecorder.StartNew(ProfilerCategory.Memory, c)).ToArray();
            memVals = mem.Select(_ => new List<double>(ListCapacity)).ToArray();
            timingOn = FrameTimingManager.IsFeatureEnabled();
        }

        /// <summary>Call once per timed frame. Allocation-free on the timing path.</summary>
        public void Sample()
        {
            timedFrames++;
            for (int i = 0; i < mem.Length; i++) if (mem[i].Valid && mem[i].LastValue > 0) memVals[i].Add(mem[i].LastValue);
            if (!timingOn) return;
            FrameTimingManager.CaptureFrameTimings();
            int n = System.Math.Min((int)FrameTimingManager.GetLatestTimings(TimingBatch, buf), buf.Length);
            for (int i = 0; i < n; i++) stamps[i] = buf[i].frameStartTimestamp;
            int k = floor.Feed(stamps, n, order);
            for (int j = 0; j < k; j++)
            {
                var t = buf[order[j]];
                cpu.Add(t.cpuFrameTime);
                main.Add(t.cpuMainThreadFrameTime);
                render.Add(t.cpuRenderThreadFrameTime);
                if (t.gpuFrameTime > 0) gpu.Add(t.gpuFrameTime); else gpuZero++;
            }
        }

        /// <summary>
        /// The frame-timing floor of one window (pure, for the EditMode tests). The first <see cref="Feed"/> that sees a
        /// timing sets the floor to the newest one and accepts nothing; every later call accepts the timings newer than the
        /// last accepted one, oldest first, each frame once.
        /// </summary>
        public sealed class TimingFloor
        {
            public bool IsSet { get; private set; }
            public ulong Last { get; private set; }

            /// <summary>Writes the accepted indices, oldest first, into <paramref name="outIdx"/> and returns how many.</summary>
            public int Feed(ulong[] frameStarts, int count, int[] outIdx)
            {
                count = System.Math.Min(count, frameStarts.Length);
                if (!IsSet)
                {
                    if (count <= 0) return 0;
                    ulong newest = 0;
                    for (int i = 0; i < count; i++) if (frameStarts[i] > newest) newest = frameStarts[i];
                    Last = newest;
                    IsSet = true;
                    return 0;
                }
                int k = NewerInOrder(Last, frameStarts, count, outIdx, out ulong newLast);
                Last = newLast;
                return k;
            }
        }

        /// <summary>The dedupe rule: a timing is new when its frame started after the last accepted one.</summary>
        public static bool Accept(ulong last, ulong candidate) => candidate > last;

        /// <summary>
        /// Indices (into the first <paramref name="count"/> of <paramref name="frameStarts"/>) of the timings newer than
        /// <paramref name="last"/>, oldest first, each frame once, written to <paramref name="outIdx"/>; returns how many.
        /// <paramref name="newLast"/> is the newest accepted start (or <paramref name="last"/> when none is new).
        /// Allocation-free (a plain duplicate scan and an insertion sort over at most <see cref="TimingBatch"/> entries).
        /// </summary>
        public static int NewerInOrder(ulong last, ulong[] frameStarts, int count, int[] outIdx, out ulong newLast)
        {
            int k = 0;
            int n = System.Math.Min(System.Math.Min(count, frameStarts.Length), outIdx.Length);
            for (int i = 0; i < n; i++)
            {
                ulong s = frameStarts[i];
                if (!Accept(last, s)) continue;
                bool dup = false;
                for (int j = 0; j < k; j++) if (frameStarts[outIdx[j]] == s) { dup = true; break; }
                if (dup) continue;
                int p = k++;
                while (p > 0 && frameStarts[outIdx[p - 1]] > s) { outIdx[p] = outIdx[p - 1]; p--; }
                outIdx[p] = i;
            }
            newLast = k > 0 ? frameStarts[outIdx[k - 1]] : last;
            return k;
        }

        /// <summary>The allocating form of <see cref="NewerInOrder(ulong, ulong[], int, int[], out ulong)"/>, for the tests.</summary>
        public static List<int> NewerInOrder(ulong last, ulong[] frameStarts, int count, out ulong newLast)
        {
            var idx = new int[System.Math.Max(0, System.Math.Min(count, frameStarts.Length))];
            int k = NewerInOrder(last, frameStarts, count, idx, out newLast);
            return new List<int>(idx.Take(k));
        }

        static string Key(string name) => name.Replace(" Memory", "").Replace(" ", "_").ToLowerInvariant() + "_bytes";

        /// <summary>"null" for no samples, else the FrustumMath summary object.</summary>
        public static string SummaryOrNull(IReadOnlyList<double> v) => v == null || v.Count == 0 ? "null" : FrustumMath.Json(FrustumMath.Summarize(v));

        /// <summary>
        /// The frame_timing object. GPU time is null (never 0) when stats are off, no frame was captured, or every captured
        /// frame had a GPU time of 0; gpu_null_reason says which.
        /// </summary>
        public static string FrameTimingJson(bool enabled, int timedFrames, IReadOnlyList<double> cpu, IReadOnlyList<double> main,
                                             IReadOnlyList<double> render, IReadOnlyList<double> gpu, int gpuZero)
        {
            int accepted = cpu?.Count ?? 0;
            string reason = !enabled ? ReasonStatsOff : accepted == 0 ? ReasonNoFrames : (gpu == null || gpu.Count == 0) ? ReasonGpuZero : null;
            return $"{{\"enabled\":{(enabled ? "true" : "false")},\"timed_frames\":{timedFrames},\"frames_accepted\":{accepted}," +
                   $"\"cpu_frame_ms\":{SummaryOrNull(cpu)},\"cpu_main_thread_ms\":{SummaryOrNull(main)},\"cpu_render_thread_ms\":{SummaryOrNull(render)}," +
                   $"\"gpu_frame_ms\":{(reason == null ? SummaryOrNull(gpu) : "null")},\"gpu_zero_frames\":{gpuZero}," +
                   $"\"gpu_null_reason\":{(reason == null ? "null" : "\"" + reason + "\"")}" + "}";
        }

        /// <summary>The GPU frame-time summary, or "null" (for counters.gpu_frame_ms).</summary>
        public string GpuFrameMsJson() => timingOn && cpu.Count > 0 && gpu.Count > 0 ? SummaryOrNull(gpu) : "null";

        /// <summary>Texture memory as Unity's static Texture counters report it now (scope in the class header).</summary>
        public static string TextureSnapshotJson()
        {
            bool streaming = QualitySettings.streamingMipmapsActive;
            string U(double v) => FrustumMath.F(v);   // every integer counter type converts to double
            return "{\"scope\":\"Texture2D and Cubemap instances (Unity docs); RenderTextures and MSAA targets are in memory.gfx_used\"," +
                   $"\"current_bytes\":{U(Texture.currentTextureMemory)},\"total_bytes\":{U(Texture.totalTextureMemory)},\"non_streaming_bytes\":{U(Texture.nonStreamingTextureMemory)}," +
                   $"\"desired_bytes\":{(streaming ? U(Texture.desiredTextureMemory) : "null")},\"target_bytes\":{(streaming ? U(Texture.targetTextureMemory) : "null")}," +
                   $"\"streaming_active\":{(streaming ? "true" : "false")},\"streaming_budget_mb\":{FrustumMath.F(QualitySettings.streamingMipmapsMemoryBudget)}," +
                   $"\"streaming_texture_count\":{U(Texture.streamingTextureCount)},\"non_streaming_texture_count\":{U(Texture.nonStreamingTextureCount)}," +
                   $"\"global_mip_limit\":{QualitySettings.globalTextureMipmapLimit}" + "}";
        }

        /// <summary>The "resources" object of a window or route row.</summary>
        public string Json(string textureStart, string textureEnd) =>
            $"{{\"frame_timing\":{FrameTimingJson(timingOn, timedFrames, cpu, main, render, gpu, gpuZero)}," +
            "\"memory\":{" + string.Join(",", MemoryCounters.Select((c, i) => $"\"{Key(c)}\":{SummaryOrNull(memVals[i])}")) + "}," +
            $"\"texture_start\":{textureStart ?? "null"},\"texture_end\":{textureEnd ?? "null"}" + "}";

        public void Dispose()
        {
            foreach (var r in mem) r.Dispose();
        }
    }
}
