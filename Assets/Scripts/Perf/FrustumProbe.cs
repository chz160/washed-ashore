using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WashedAshore.Birds;
using WashedAshore.Fish;
using WashedAshore.Gameplay;
using WashedAshore.Wildlife;
using Debug = UnityEngine.Debug;

namespace WashedAshore.Perf
{
    /// <summary>
    /// Frustum-culling harness for issue #9 (and the shared #10/#11 protocol), shaped by technical-director's #25 plan.
    /// Inert unless a -fc argument is on the player's command line, and refused outright in the Editor (Boot), so its
    /// asset and QualitySettings writes only ever touch a built player's memory. Trials are runtime overrides for the session only:
    /// camera fields, Terrain component fields (never terrainData) and, for the asset keys only, the active URP asset, its
    /// UniversalRendererData (depth priming) and QualitySettings (mip limit, lodBias) in memory. Those writes are refused in
    /// the Editor (where they would persist to disk) and put back at quit and on destroy. Nothing is saved to World.unity,
    /// prefabs, URP assets, terrain data or ProjectSettings.
    ///
    /// Spot run: -fcSpots spot[,spot]  (one spot per launch in the #25 plan; default all five) with -fcVariant id:k=v,..
    ///   repeated (TrialVariant). Per spot: -fcWarmup s of A (default 15, discarded), then the windows A V1 A V2 ... Vk A,
    ///   A being the authored settings. Each window:
    ///   apply, -fcSettle s (default 5, discarded), then one 360 degree yaw from the pose heading at -fcYawSpeed deg/s
    ///   (default 15, so 24 s) timed on unscaledDeltaTime with the counters sampled every timed frame; then, frozen and
    ///   outside the timing, the census and a 1920x1080 still at compass headings 0/90/180/270 and at the pose's own yaw
    ///   (label "own", technical-director's pose addendum); a trial window adds, per
    ///   heading, a same-frame repeat (noise floor) and the in-frame authored twin with its change mask. Engine values are read
    ///   back after the settle and again after the timed yaw; a requested value that does not read back makes the window
    ///   valid:false. Rows go to frustum-windows.jsonl as each window ends and to frustum-spots.json at the end, with
    ///   progress.txt (last line DONE) beside Player.log; then the player quits. Wildlife, bird and fish seeds are fixed at 101.
    /// Route run: -bbFpsWalk with the global trial flags (one variant per launch): the walk runs unchanged; this applies
    ///   the variant, samples the counters on the walk's timed frames (BellsBendFpsWalk.TimedFrame) and writes
    ///   frustum-route.json at quit.
    /// Global trial flags (route, or a spot run with no -fcVariant, where they form variant "cli"): -fcFar m, -fcFarCab m,
    ///   -fcLayerCull Layer=m (repeat), -fcLayerCullSpherical, -fcDetail m, -fcShadowsOff, -fcShadowDist m, -fcGrdOcc on|off,
    ///   -fcSmallMesh percent, -fcMipLimit 0-3, -fcLodBias x, -fcAA off|fxaa|smaa|taa|stp|msaa2|msaa4|msaa8,
    ///   -fcDepthPrime off|auto|forced, -fcPost on|off (TrialVariant keys mipLimit, lodBias, aa, depthPrime, post). In a spot
    ///   run with -fcVariant the global trial flags would be ignored, so that is an argument error.
    /// AA and depth-priming keys (Phase 0): aa= sets the camera's AA mode, the URP asset's MSAA count and (stp) its upscaling
    ///   filter; fxaa/smaa/taa/stp need camera post-processing, which they turn on unless post= is given (post_implied).
    ///   depthPrime= sets every UniversalRendererData of the asset (SetDirty rebuilds the renderer: a hitch inside the
    ///   settle). Readback adds a problem when MSAA is asked for and Camera.allowMSAA is false, when the asset's isStpUsed does
    ///   not match the requested filter, or when depth priming is asked for while MSAA &gt; 1 (URP turns priming off then).
    ///   mipLimit= re-uploads textures (a spike inside the settle); which textures it reaches (terrain layers included or not)
    ///   is not verified here. For mipLimit, depthPrime and aa variants the in-frame authored twin still is skipped
    ///   (twin_skipped): switching back and forth would re-upload or rebuild between grabs. For taa and stp every still waits
    ///   16 frames for the temporal history instead of 2.
    /// Options: -fcRun n (run index, echoed), -fcArm id, -fcPose name=x,z,yaw,pitch[,height], -fcCabHeight m, -fcNoShots,
    ///   -fcEdgeShots (for a variant with layer culls or a detail distance: a still aimed at the cull edge, culled vs shown).
    ///
    /// Fish sample (addendum 5, #25 note_technical-director_fish_sample): at a spot whose name starts with "fish_", every
    ///   timed frame records the fish the population drew (FishPopulation draw modes); the window row gets "fish_sample":
    ///   frames, share of frames with at least one fish, max and mean. Null at every other spot.
    /// Q1 census (per heading, frozen; primary fields are technical-director's #25 definitions):
    ///   enabled      enabled renderers on active objects, forceRenderingOff excluded
    ///   submitted    enabled, bounds meet the frustum (GeometryUtility.TestPlanesAABB, far plane in force) and not past a
    ///                per-layer cull distance in force (planar: nearest view depth; spherical: nearest distance)
    ///   fog_visible  submitted and nearest view depth &lt; fog end (URP linear fog is view-depth based)
    ///   bands        submitted by nearest view depth: 0-120, 120-520, 520-far (fog start / fog end as read)
    ///   by_type      MeshRenderer (the type GRD can draw; exclusion reasons are not readable in a release player) vs other
    ///   Occlusion is not counted in these fields. Extras, labelled and outside the primary fields: LOD levels hidden or
    ///   culled by Unity's relative-height rule, a terrain-only occlusion estimate (box under 50 m whose centre and 8
    ///   corners pulled 10% inward are all hidden by TerrainCollider.Raycast; a per-collider test, no scene query),
    ///   Renderer.isVisible, shadow-caster candidates (casting renderers in the frustum cut at the shadow distance) and
    ///   terrain tree instances within the tree distance, in the frustum and past fog end.
    /// Counters per timed frame (ProfilerRecorder, Render): batches, SetPass calls, draw calls, triangles, vertices, shadow
    ///   casters, visible skinned meshes; extra: CPU "Main Thread" ms (Internal) and gpu_frame_ms from FrameTimingManager
    ///   (enableFrameTimingStats 1 since Phase 0; null, never 0, when the API reports no GPU time). GRD counters exist only in
    ///   a development player with the profiler on; null otherwise. Each as median/p95/p99/max/mean, nearest rank as
    ///   BellsBendFpsWalk.
    /// Resources per window and route (ResourceRecorder, descriptive, not part of S1 unless a plan pre-registers it):
    ///   FrameTimingManager CPU/main/render/GPU frame ms, ProfilerRecorder memory (total, GC, Gfx, System used/reserved),
    ///   and Texture memory snapshots after the settle and after the timing. Machine() records the authored lodBias and mip
    ///   limit (captured before any trial), so the header is the same in every arm; the live values are in the readback.
    /// GRD's InstanceCuller tests only the camera's cullingLayerMask, not layerCullDistances (core package,
    ///   Runtime/GPUDriven/Culling/InstanceCuller.cs), so a layer cull is expected to cut only non-GRD renderers; the
    ///   census counts what the camera settings ask for, and the counters and edge stills show what the engine did.
    /// </summary>
    public class FrustumProbe : MonoBehaviour
    {
        const string WarmupArg = "-fcWarmup", SpotsArg = "-fcSpots", VariantArg = "-fcVariant", RunArg = "-fcRun", ArmArg = "-fcArm", SettleArg = "-fcSettle", YawSpeedArg = "-fcYawSpeed";
        const string PoseArg = "-fcPose", CabHeightArg = "-fcCabHeight", NoShotsArg = "-fcNoShots", EdgeShotsArg = "-fcEdgeShots";
        const string FarArg = "-fcFar", FarCabArg = "-fcFarCab", LayerCullArg = "-fcLayerCull", SphericalArg = "-fcLayerCullSpherical", DetailArg = "-fcDetail";
        const string ShadowsOffArg = "-fcShadowsOff", ShadowDistArg = "-fcShadowDist", GrdOccArg = "-fcGrdOcc", SmallMeshArg = "-fcSmallMesh";
        const string MipLimitArg = "-fcMipLimit", LodBiasArg = "-fcLodBias", AaArg = "-fcAA", DepthPrimeArg = "-fcDepthPrime", PostArg = "-fcPost";
        const int GrabFrames = 2, TemporalGrabFrames = 16;
        const string TwinSkipped = "mipLimit, depthPrime and aa variants: switching to authored and back would re-upload textures or rebuild the renderer between grabs";
        const int Width = 1920, Height = 1080, Seed = 101;   // BellsBendFpsWalk.FixedSeed
        const float AuthoredFarExpected = 1000f, OccluderMaxSize = 50f, CornerPull = 0.1f, TreeRadius = 5f, EdgeWindow = 0.25f, Tolerance = 0.01f;
        static readonly int[] StillHeadings = { 0, 90, 180, 270 };
        public const string OwnLabel = "own";            // the pose's own yaw, frozen beside the compass headings (td pose addendum, #25)
        public const string FishSpotPrefix = "fish_";   // spots whose windows count fish drawn per timed frame (addendum 5)

        static readonly string[] RenderCounters = { "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Triangles Count", "Vertices Count", "Shadow Casters Count", "Visible Skinned Meshes Count" };
        static readonly string[] GrdCounters = { "Visible Instances", "Frustum Culled", "Layer Culled", "LOD Group Culled", "Total Instances" };

        // ----- parsed arguments -----
        static bool active, spotMode, walkMode, shots = true, edgeShots;
        static float warmup = 15f, settle = 5f, yawSpeed = 15f, cabHeight = FarViewProbe.CabHeight;
        static int run = 1;
        static string arm = "", cabHeightSource = "default FarViewProbe.CabHeight";
        static TrialVariant cli = new TrialVariant { id = "cli" };
        static readonly List<TrialVariant> Variants = new List<TrialVariant>();
        static readonly List<string> SpotNames = new List<string>();
        static readonly List<SpotPose> PoseOverrides = new List<SpotPose>();
        static readonly List<string> ArgErrors = new List<string>();

        // ----- authored values, captured before the first change -----
        static bool authoredCaptured, authoredSpherical, authoredRenderShadows = true, authoredGrdOcc, authoredFishActive;
        static GameObject fishRoot;
        const string FishRootName = "Fish";
        static float authoredFar = -1f, authoredShadowDist = -1f, authoredSmallMesh = -1f;
        static float[] authoredLayerCull;
        static readonly Dictionary<Terrain, float> AuthoredDetail = new Dictionary<Terrain, float>();
        static UniversalRenderPipelineAsset asset;
        static bool assetTouched;
        static int authoredMipLimit = -1, authoredMsaa = 1;
        static float authoredLodBias = -1f;
        static bool authoredPost, qualityTouched;
        static AntialiasingMode authoredCamAa;
        static UpscalingFilterSelection authoredUpscaling;
        static UniversalRendererData[] authoredRenderers;   // copied from the asset's rendererDataList (a span) in CaptureAuthored
        static DepthPrimingMode[] authoredDepthPrime;

        // ----- state -----
        static TrialVariant current = TrialVariant.Authored();
        static string currentSpot = "", exeSha = "";
        static int farReapplied;
        static readonly List<double> RouteMs = new List<double>(), LookMs = new List<double>();
        static CounterSet routeCounters;
        static ResourceRecorder routeResources;
        static bool routeWritten;
        static string routeReadbackStart = "null", routeTextureStart = "null";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            active = spotMode = walkMode = edgeShots = false; shots = true;
            warmup = 15f; settle = 5f; yawSpeed = 15f; cabHeight = FarViewProbe.CabHeight; run = 1;
            arm = ""; cabHeightSource = "default FarViewProbe.CabHeight";
            cli = new TrialVariant { id = "cli" };
            Variants.Clear(); SpotNames.Clear(); PoseOverrides.Clear(); ArgErrors.Clear();
            authoredCaptured = authoredSpherical = authoredGrdOcc = authoredFishActive = false; authoredRenderShadows = true; fishRoot = null;
            authoredFar = authoredShadowDist = authoredSmallMesh = -1f; authoredLayerCull = null;
            AuthoredDetail.Clear(); asset = null; assetTouched = false;
            authoredMipLimit = -1; authoredMsaa = 1; authoredLodBias = -1f; authoredPost = qualityTouched = false;
            authoredCamAa = AntialiasingMode.None; authoredUpscaling = UpscalingFilterSelection.Auto; authoredRenderers = null; authoredDepthPrime = null;
            current = TrialVariant.Authored(); currentSpot = ""; exeSha = ""; farReapplied = 0;
            RouteMs.Clear(); LookMs.Clear(); routeCounters = null; routeResources = null; routeWritten = false; routeReadbackStart = "null"; routeTextureStart = "null";
            tileColliders = null; treePositions = null;
            routeProblems.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void FixSeeds()
        {
            if (!System.Environment.GetCommandLineArgs().Contains(SpotsArg)) return;   // the walk fixes its own
            WildlifeRandom.OverrideSeed(Seed);
            BirdRandom.OverrideSeed(Seed);
            FishRandom.OverrideSeed(Seed);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Parse(System.Environment.GetCommandLineArgs());
            if (!active) return;
            // B1 (technical-director, #23 review): the probe writes the URP asset (three keys) and QualitySettings.vSyncCount,
            // which in an Editor session could outlive play mode. It runs in a built player only; Parse stays testable.
            if (Application.isEditor) { Debug.LogWarning("[Frustum] REFUSED in the Editor: the probe runs only in a built player."); active = false; return; }
            if (spotMode && walkMode) { Debug.LogError("[Frustum] REFUSED: -fcSpots with -bbFpsWalk/-waterFpsWalk; run them as separate launches."); return; }
            var go = new GameObject("FrustumProbe");
            go.AddComponent<FishDrawCounter>();   // counts fish after the renderer (order 10000)
            go.AddComponent<FrustumProbe>();
        }

        /// <summary>Reads the -fc arguments. Public for the EditMode tests; touches no scene state.</summary>
        public static void Parse(string[] args)
        {
            ResetStatics();
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (!a.StartsWith("-fc")) continue;
                active = true;
                string next = i + 1 < args.Length ? args[i + 1] : null;
                bool hasValue = next != null && !next.StartsWith("-");
                float Num()
                {
                    if (hasValue && float.TryParse(next, NumberStyles.Float, inv, out float v) && !float.IsNaN(v) && !float.IsInfinity(v)) { i++; return v; }
                    ArgErrors.Add($"{a} needs a number");
                    return float.NaN;
                }
                string Text() { if (hasValue) { i++; return next; } ArgErrors.Add($"{a} needs a value"); return null; }
                switch (a)
                {
                    case SpotsArg: spotMode = true; if (hasValue) { SpotNames.AddRange(FrustumMath.ParseList(next)); i++; } break;
                    case VariantArg:
                    {
                        var t = Text();
                        if (t == null) break;
                        if (TrialVariant.TryParse(t, out var v, out string err))
                        {
                            if (Variants.Any(x => x.id == v.id)) ArgErrors.Add($"-fcVariant: duplicate id '{v.id}'"); else Variants.Add(v);
                        }
                        else ArgErrors.Add("-fcVariant " + err);
                        break;
                    }
                    case RunArg: { float v = Num(); if (v >= 1f) run = (int)v; } break;
                    case ArmArg: { var t = Text(); if (t != null) arm = t; } break;
                    case SettleArg: { float v = Num(); if (v >= 0f) settle = v; } break;
                    case WarmupArg: { float v = Num(); if (v >= 0f) warmup = v; } break;
                    case YawSpeedArg: { float v = Num(); if (v > 0f) yawSpeed = v; } break;
                    case CabHeightArg: { float v = Num(); if (v > 0f) { cabHeight = v; cabHeightSource = "command line -fcCabHeight"; } } break;
                    case NoShotsArg: shots = false; break;
                    case EdgeShotsArg: edgeShots = true; break;
                    case PoseArg:
                    {
                        var t = Text();
                        if (t == null) break;
                        if (FrustumMath.TryParsePose(t, FrustumSpots.Eye, out var pose, out string err)) PoseOverrides.Add(pose); else ArgErrors.Add("-fcPose " + err);
                        break;
                    }
                    case FarArg: { float v = Num(); if (v > 0f) cli.far = v; } break;
                    case FarCabArg: { float v = Num(); if (v > 0f) cli.farCab = v; } break;
                    case DetailArg: { float v = Num(); if (v >= 0f) cli.detail = v; } break;
                    case ShadowDistArg: { float v = Num(); if (v >= 0f) cli.shadowDist = v; } break;
                    case SmallMeshArg: { float v = Num(); if (v >= 0f && v <= 20f) cli.smallMesh = v; else if (!float.IsNaN(v)) ArgErrors.Add("-fcSmallMesh is 0-20"); } break;
                    case SphericalArg: cli.spherical = true; break;
                    case ShadowsOffArg: cli.shadowsOff = true; break;
                    case GrdOccArg:
                    {
                        var t = Text();
                        if (t == "on") cli.grdOcclusion = 1; else if (t == "off") cli.grdOcclusion = 0; else if (t != null) ArgErrors.Add("-fcGrdOcc on|off");
                        break;
                    }
                    case LayerCullArg:
                    {
                        var t = Text();
                        if (t == null) break;
                        if (FrustumMath.TryParseLayerCull(t, out string layer, out float metres, out string err)) cli.layerCull[layer] = metres; else ArgErrors.Add("-fcLayerCull " + err);
                        break;
                    }
                    case MipLimitArg: { float v = Num(); if (v >= 0f && v <= 3f && v == Mathf.Floor(v)) cli.mipLimit = (int)v; else if (!float.IsNaN(v)) ArgErrors.Add("-fcMipLimit is an integer 0-3"); } break;
                    case LodBiasArg: { float v = Num(); if (v > 0f && v <= 10f) cli.lodBias = v; else if (!float.IsNaN(v)) ArgErrors.Add("-fcLodBias is > 0 and <= 10"); } break;
                    case AaArg:
                    {
                        var t = Text();
                        if (t == null) break;
                        if (TrialVariant.AaModes.Contains(t)) cli.aa = t; else ArgErrors.Add("-fcAA " + string.Join("|", TrialVariant.AaModes));
                        break;
                    }
                    case DepthPrimeArg:
                    {
                        var t = Text();
                        if (t == null) break;
                        if (TrialVariant.DepthPrimeModes.Contains(t)) cli.depthPrime = t; else ArgErrors.Add("-fcDepthPrime off|auto|forced");
                        break;
                    }
                    case PostArg:
                    {
                        var t = Text();
                        if (t == "on") cli.post = 1; else if (t == "off") cli.post = 0; else if (t != null) ArgErrors.Add("-fcPost on|off");
                        break;
                    }
                    default: ArgErrors.Add($"unknown argument {a}"); break;
                }
            }
            walkMode = args.Contains(BellsBendFpsWalk.Arg) || args.Contains(BellsBendFpsWalk.ShoreArg);
            if (spotMode && SpotNames.Count == 0) SpotNames.AddRange(FrustumSpots.StaticSpots);
            string combo = TrialVariant.CombinationError(cli);
            if (combo != null) ArgErrors.Add("global trial flags: " + combo);
            if (spotMode && Variants.Count > 0 && !cli.IsAuthored) ArgErrors.Add("global trial flags are ignored in a spot run with -fcVariant; put the keys in the variant");
            if (spotMode && Variants.Count == 0 && !cli.IsAuthored) Variants.Add(cli);
        }

        // Read-only views for the EditMode tests.
        public static bool Active => active;
        public static bool SpotMode => spotMode;
        public static int Run => run;
        public static float Warmup => warmup;
        public static TrialVariant Cli => cli;
        public static IReadOnlyList<TrialVariant> VariantList => Variants;
        public static IReadOnlyList<string> Spots => SpotNames;
        public static IReadOnlyList<string> Errors => ArgErrors;
        public static IReadOnlyList<SpotPose> Overrides => PoseOverrides;

        // ----- apply, read back, revert -------------------------------------------------------------------------

        static void CaptureAuthored(Camera cam)
        {
            if (authoredCaptured || !cam) return;
            authoredFar = cam.farClipPlane;
            authoredLayerCull = (float[])cam.layerCullDistances.Clone();
            authoredSpherical = cam.layerCullSpherical;
            var data = cam.GetUniversalAdditionalCameraData();
            authoredRenderShadows = data ? data.renderShadows : true;
            if (data) { authoredCamAa = data.antialiasing; authoredPost = data.renderPostProcessing; }
            authoredMipLimit = QualitySettings.globalTextureMipmapLimit;
            authoredLodBias = QualitySettings.lodBias;
            foreach (var t in Terrain.activeTerrains) AuthoredDetail[t] = t.detailObjectDistance;
            fishRoot = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == FishRootName);
            authoredFishActive = fishRoot && fishRoot.activeSelf;
            asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset)
            {
                authoredShadowDist = asset.shadowDistance;
                authoredSmallMesh = asset.smallMeshScreenPercentage;
                authoredGrdOcc = asset.gpuResidentDrawerEnableOcclusionCullingInCameras;
                authoredMsaa = asset.msaaSampleCount;
                authoredUpscaling = asset.upscalingFilter;
                // rendererDataList is a ReadOnlySpan: copy it here (a plain method); coroutines and lambdas use only the copy.
                var list = asset.rendererDataList;
                authoredRenderers = new UniversalRendererData[list.Length];
                authoredDepthPrime = new DepthPrimingMode[list.Length];
                for (int i = 0; i < list.Length; i++)
                {
                    authoredRenderers[i] = list[i] as UniversalRendererData;
                    authoredDepthPrime[i] = authoredRenderers[i] ? authoredRenderers[i].depthPrimingMode : DepthPrimingMode.Disabled;
                }
            }
            authoredCaptured = true;
        }

        static float WantFar(TrialVariant v) { float f = v.FarFor(currentSpot == FrustumSpots.Cab); return f > 0f ? f : authoredFar; }
        static int WantMip(TrialVariant v) => v.mipLimit >= 0 ? v.mipLimit : authoredMipLimit;
        static float WantLodBias(TrialVariant v) => v.lodBias > 0f ? v.lodBias : authoredLodBias;
        static int WantMsaa(TrialVariant v) => v.aa != null ? TrialVariant.MsaaFor(v.aa) : authoredMsaa;
        static AntialiasingMode WantCamAa(TrialVariant v) => v.aa != null ? TrialVariant.CameraAaFor(v.aa) : authoredCamAa;
        static bool WantPost(TrialVariant v) => v.post >= 0 ? v.post == 1 : (TrialVariant.AaNeedsPost(v.aa) || authoredPost);

        /// <summary>stp asks for the STP filter; another aa mode leaves the authored filter, or Auto if the authored one is STP.</summary>
        static UpscalingFilterSelection WantUpscaling(TrialVariant v) =>
            v.aa == "stp" ? UpscalingFilterSelection.STP :
            v.aa != null && authoredUpscaling == UpscalingFilterSelection.STP ? UpscalingFilterSelection.Auto : authoredUpscaling;

        static DepthPrimingMode WantDepthPrime(TrialVariant v, int renderer) =>
            v.depthPrime != null ? TrialVariant.DepthPrimingFor(v.depthPrime) : authoredDepthPrime[renderer];

        static float[] WantLayerCull(TrialVariant v, List<string> problems)
        {
            var d = (float[])authoredLayerCull.Clone();
            foreach (var kv in v.layerCull)
            {
                int layer = LayerMask.NameToLayer(kv.Key);
                if (layer < 0) { problems?.Add($"unknown layer '{kv.Key}'"); continue; }
                d[layer] = kv.Value;
            }
            return d;
        }

        /// <summary>Puts a variant on the camera, the terrain tiles and (player only) the URP asset; authored for fields it leaves unset.</summary>
        static void Apply(Camera cam, TrialVariant v)
        {
            CaptureAuthored(cam);
            current = v;
            cam.farClipPlane = WantFar(v);
            cam.layerCullDistances = WantLayerCull(v, null);
            cam.layerCullSpherical = v.spherical || authoredSpherical;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data)
            {
                data.renderShadows = !v.shadowsOff && authoredRenderShadows;
                var camAa = WantCamAa(v);
                if (data.antialiasing != camAa) data.antialiasing = camAa;
                bool post = WantPost(v);
                if (data.renderPostProcessing != post) data.renderPostProcessing = post;
            }
            foreach (var t in Terrain.activeTerrains)
                t.detailObjectDistance = v.detail >= 0f ? v.detail : (AuthoredDetail.TryGetValue(t, out float a) ? a : t.detailObjectDistance);
            // FISHOFF: the population is built in Awake, so toggling the root only pauses and hides it (OnEnable re-registers).
            if (fishRoot && fishRoot.activeSelf != (authoredFishActive && !v.fishOff)) fishRoot.SetActive(authoredFishActive && !v.fishOff);
            if (Application.isEditor)
            {
                if (v.WritesAsset) Debug.LogError($"[Frustum] variant {v.id} writes the URP asset or QualitySettings; refused in the Editor (it would persist). The window is invalid.");
                return;   // never write the asset or QualitySettings in the Editor
            }
            int mip = WantMip(v);
            if (mip >= 0 && QualitySettings.globalTextureMipmapLimit != mip) { QualitySettings.globalTextureMipmapLimit = mip; qualityTouched = true; }
            float lodBias = WantLodBias(v);
            if (lodBias > 0f && !Mathf.Approximately(QualitySettings.lodBias, lodBias)) { QualitySettings.lodBias = lodBias; qualityTouched = true; }
            if (!asset) return;
            float sd = v.shadowDist >= 0f ? v.shadowDist : authoredShadowDist;
            float sm = v.smallMesh >= 0f ? v.smallMesh : authoredSmallMesh;
            bool occ = v.grdOcclusion >= 0 ? v.grdOcclusion == 1 : authoredGrdOcc;
            if (!Mathf.Approximately(asset.shadowDistance, sd)) { asset.shadowDistance = sd; assetTouched = true; }
            if (!Mathf.Approximately(asset.smallMeshScreenPercentage, sm)) { asset.smallMeshScreenPercentage = sm; assetTouched = true; }
            if (asset.gpuResidentDrawerEnableOcclusionCullingInCameras != occ) { asset.gpuResidentDrawerEnableOcclusionCullingInCameras = occ; assetTouched = true; }
            int msaa = WantMsaa(v);
            if (asset.msaaSampleCount != msaa) { asset.msaaSampleCount = msaa; assetTouched = true; }
            var up = WantUpscaling(v);
            if (asset.upscalingFilter != up) { asset.upscalingFilter = up; assetTouched = true; }
            for (int i = 0; authoredRenderers != null && i < authoredRenderers.Length; i++)
            {
                var r = authoredRenderers[i];
                var want = WantDepthPrime(v, i);
                if (r && r.depthPrimingMode != want) { r.depthPrimingMode = want; assetTouched = true; }   // the setter calls SetDirty: renderer rebuilt
            }
        }

        /// <summary>Puts the URP asset back (the one change that could outlive a session).</summary>
        static void RestoreAsset()
        {
            if (!asset || !assetTouched) return;
            asset.shadowDistance = authoredShadowDist;
            asset.smallMeshScreenPercentage = authoredSmallMesh;
            asset.gpuResidentDrawerEnableOcclusionCullingInCameras = authoredGrdOcc;
            asset.msaaSampleCount = authoredMsaa;
            asset.upscalingFilter = authoredUpscaling;
            for (int i = 0; authoredRenderers != null && i < authoredRenderers.Length; i++)
                if (authoredRenderers[i] && authoredRenderers[i].depthPrimingMode != authoredDepthPrime[i]) authoredRenderers[i].depthPrimingMode = authoredDepthPrime[i];
            assetTouched = false;
        }

        /// <summary>Puts QualitySettings' mip limit and lodBias back (they would outlive a session in the Editor).</summary>
        static void RestoreQuality()
        {
            if (!qualityTouched) return;
            if (authoredMipLimit >= 0) QualitySettings.globalTextureMipmapLimit = authoredMipLimit;
            if (authoredLodBias > 0f) QualitySettings.lodBias = authoredLodBias;
            qualityTouched = false;
        }

        void OnDestroy() { RestoreAsset(); RestoreQuality(); }

        static string F(float v) => FrustumMath.F(v);
        static string F(double v) => FrustumMath.F(v);
        static string S(string v) => "\"" + FrustumMath.Esc(v) + "\"";
        static string B(bool b) => b ? "true" : "false";

        /// <summary>The values read back from the engine, as a JSON object, with every mismatch against the variant added to <paramref name="problems"/>.</summary>
        static string Readback(Camera cam, TrialVariant v, List<string> problems)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            var tiles = Terrain.activeTerrains;
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool grdOccLive = GPUResidentDrawer.IsInstanceOcclusionCullingEnabled();
            float wantFar = WantFar(v);
            if (Mathf.Abs(cam.farClipPlane - wantFar) > Tolerance) problems.Add($"far {cam.farClipPlane} != {wantFar}");
            var wantCull = WantLayerCull(v, problems);
            var cull = cam.layerCullDistances;
            for (int i = 0; i < cull.Length && i < wantCull.Length; i++)
                if (Mathf.Abs(cull[i] - wantCull[i]) > Tolerance) problems.Add($"layerCullDistances[{i}] {cull[i]} != {wantCull[i]}");
            if (cam.layerCullSpherical != (v.spherical || authoredSpherical)) problems.Add("layerCullSpherical not as requested");
            bool wantShadows = !v.shadowsOff && authoredRenderShadows;
            if (!data) { if (v.shadowsOff) problems.Add("no UniversalAdditionalCameraData to read renderShadows from"); }
            else if (data.renderShadows != wantShadows) problems.Add($"renderShadows {data.renderShadows} != {wantShadows}");
            foreach (var t in tiles)
            {
                float want = v.detail >= 0f ? v.detail : (AuthoredDetail.TryGetValue(t, out float a) ? a : t.detailObjectDistance);
                if (Mathf.Abs(t.detailObjectDistance - want) > Tolerance) { problems.Add($"detailObjectDistance on {t.name} {t.detailObjectDistance} != {want}"); break; }
            }
            bool wantFish = authoredFishActive && !v.fishOff;
            if (v.fishOff && !fishRoot) problems.Add("no Fish root to deactivate");
            else if (fishRoot && fishRoot.activeSelf != wantFish) problems.Add($"Fish root active {fishRoot.activeSelf} != {wantFish}");
            if (!data) { if (v.aa != null || v.post >= 0) problems.Add("no UniversalAdditionalCameraData to set aa or post on"); }
            else
            {
                var camAa = WantCamAa(v);
                if (data.antialiasing != camAa) problems.Add($"camera antialiasing {data.antialiasing} != {camAa}");
                bool post = WantPost(v);
                if (data.renderPostProcessing != post) problems.Add($"renderPostProcessing {data.renderPostProcessing} != {post}");
            }
            if (TrialVariant.MsaaFor(v.aa) > 1 && !cam.allowMSAA) problems.Add($"aa={v.aa} but Camera.allowMSAA is false: MSAA cannot run");
            bool refused = v.WritesAsset && Application.isEditor;
            if (!refused)
            {
                int mip = WantMip(v);
                if (mip >= 0 && QualitySettings.globalTextureMipmapLimit != mip) problems.Add($"globalTextureMipmapLimit {QualitySettings.globalTextureMipmapLimit} != {mip}");
                float lodBias = WantLodBias(v);
                if (lodBias > 0f && Mathf.Abs(QualitySettings.lodBias - lodBias) > Tolerance) problems.Add($"lodBias {QualitySettings.lodBias} != {lodBias}");
            }
            if (refused) problems.Add("asset-writing variant refused in the Editor");
            else if (rp)
            {
                float sd = v.shadowDist >= 0f ? v.shadowDist : authoredShadowDist;
                float sm = v.smallMesh >= 0f ? v.smallMesh : authoredSmallMesh;
                bool occ = v.grdOcclusion >= 0 ? v.grdOcclusion == 1 : authoredGrdOcc;
                if (Mathf.Abs(rp.shadowDistance - sd) > Tolerance) problems.Add($"shadowDistance {rp.shadowDistance} != {sd}");
                if (Mathf.Abs(rp.smallMeshScreenPercentage - sm) > Tolerance) problems.Add($"smallMeshScreenPercentage {rp.smallMeshScreenPercentage} != {sm}");
                if (rp.gpuResidentDrawerEnableOcclusionCullingInCameras != occ) problems.Add($"asset GRD occlusion {rp.gpuResidentDrawerEnableOcclusionCullingInCameras} != {occ}");
                if (v.grdOcclusion >= 0 && grdOccLive != occ) problems.Add($"GPUResidentDrawer.IsInstanceOcclusionCullingEnabled() {grdOccLive} != {occ}");
                int msaa = WantMsaa(v);
                if (rp.msaaSampleCount != msaa) problems.Add($"msaaSampleCount {rp.msaaSampleCount} != {msaa}");
                var up = WantUpscaling(v);
                if (rp.upscalingFilter != up) problems.Add($"upscalingFilter {rp.upscalingFilter} != {up}");
                // isStpUsed is the asset's own answer (it reads the upscaler name when ENABLE_UPSCALER_FRAMEWORK is defined).
                if (rp.isStpUsed != (up == UpscalingFilterSelection.STP)) problems.Add($"isStpUsed {rp.isStpUsed} != {up == UpscalingFilterSelection.STP}");
                for (int i = 0; authoredRenderers != null && i < authoredRenderers.Length; i++)
                {
                    var r = authoredRenderers[i];
                    if (r && r.depthPrimingMode != WantDepthPrime(v, i)) problems.Add($"depthPrimingMode on {r.name} {r.depthPrimingMode} != {WantDepthPrime(v, i)}");
                }
                if (v.depthPrime != null && v.depthPrime != "off" && msaa > 1 && cam.allowMSAA)
                    problems.Add($"depthPrime={v.depthPrime} with MSAA {msaa}: URP turns depth priming off when MSAA > 1");
            }
            else if (v.WritesAsset) problems.Add("no URP asset to write");
            var culls = new List<string>();
            for (int i = 0; i < cull.Length; i++) if (cull[i] > 0f) culls.Add($"{S(LayerName(i))}:{F(cull[i])}");
            return $"{{\"camera_far\":{F(cam.farClipPlane)},\"camera_near\":{F(cam.nearClipPlane)},\"fov\":{F(cam.fieldOfView)},\"aspect\":{F(cam.aspect)}," +
                   $"\"layer_cull_distances\":[{string.Join(",", cull.Select(F))}],\"layer_cull_nonzero\":{{{string.Join(",", culls)}}},\"layer_cull_spherical\":{B(cam.layerCullSpherical)}," +
                   $"\"render_shadows\":{(data ? B(data.renderShadows) : "null")},\"shadow_distance\":{F(rp ? rp.shadowDistance : -1f)},\"shadow_cascades\":{(rp ? rp.shadowCascadeCount : -1)}," +
                   $"\"shadowmap_resolution\":{(rp ? rp.mainLightShadowmapResolution : -1)},\"gpu_resident_drawer\":{S(rp ? rp.gpuResidentDrawerMode.ToString() : "none")}," +
                   $"\"grd_occlusion_asset\":{(rp ? B(rp.gpuResidentDrawerEnableOcclusionCullingInCameras) : "null")},\"grd_occlusion_live\":{B(grdOccLive)}," +
                   $"\"small_mesh_percent\":{F(rp ? rp.smallMeshScreenPercentage : -1f)},\"render_pipeline\":{S(rp ? rp.name : "none")}," +
                   $"\"fog\":{B(RenderSettings.fog)},\"fog_mode\":{S(RenderSettings.fogMode.ToString())},\"fog_start\":{F(RenderSettings.fogStartDistance)},\"fog_end\":{F(RenderSettings.fogEndDistance)}," +
                   $"\"detail_min\":{F(tiles.Length > 0 ? tiles.Min(t => t.detailObjectDistance) : -1f)},\"detail_max\":{F(tiles.Length > 0 ? tiles.Max(t => t.detailObjectDistance) : -1f)}," +
                   $"\"tree_distance\":{F(tiles.Length > 0 ? tiles.Max(t => t.treeDistance) : -1f)},\"tiles\":{tiles.Length}," +
                   $"\"fish_root_active\":{(fishRoot ? B(fishRoot.activeSelf) : "null")},\"fish_drawn\":{FishDrawn()},\"fish_counter_frames\":{FishDrawCounter.Frames}," +
                   $"\"texture_mip_limit\":{QualitySettings.globalTextureMipmapLimit},\"lod_bias\":{F(QualitySettings.lodBias)},\"msaa\":{(rp ? rp.msaaSampleCount : -1)}," +
                   $"\"upscaling_filter\":{S(rp ? rp.upscalingFilter.ToString() : "none")},\"stp_used\":{(rp ? B(rp.isStpUsed) : "null")},\"render_scale\":{F(rp ? rp.renderScale : -1f)}," +
                   $"\"camera_aa\":{(data ? S(data.antialiasing.ToString()) : "null")},\"render_post_processing\":{(data ? B(data.renderPostProcessing) : "null")},\"allow_msaa\":{B(cam.allowMSAA)}," +
                   $"\"depth_priming\":[{string.Join(",", (authoredRenderers ?? new UniversalRendererData[0]).Select(r => r ? S(r.depthPrimingMode.ToString()) : "null"))}]," +
                   $"\"depth_priming_not_blocked_by_msaa\":{B(!(rp && rp.msaaSampleCount > 1 && cam.allowMSAA))}}}";
        }

        /// <summary>
        /// Fish drawn in the previous frame, counted after the fish renderer by FishDrawCounter (order 10000). Reading
        /// FishPopulation.DrawModes here, in the coroutine, would always give 0: they are cleared in Update and refilled in
        /// FishRenderer.LateUpdate (technical-director's fish-counter ruling, #26). 0 when the root is off.
        /// </summary>
        static int FishDrawn() => FishDrawCounter.Last;

        static string LayerName(int l) { var n = LayerMask.LayerToName(l); return string.IsNullOrEmpty(n) ? "layer" + l : n; }

        static string Machine() =>
            $"\"platform\":{S(Application.platform.ToString())},\"development\":{B(Debug.isDebugBuild)},\"exe_sha256\":{S(exeSha)}," +
            $"\"gpu\":{S(SystemInfo.graphicsDeviceName)},\"graphics_api\":{S(SystemInfo.graphicsDeviceType.ToString())},\"cpu\":{S(SystemInfo.processorType)}," +
            $"\"quality\":{S(QualitySettings.names[QualitySettings.GetQualityLevel()])},\"lod_bias\":{F(authoredCaptured ? authoredLodBias : QualitySettings.lodBias)},\"maximum_lod_level\":{QualitySettings.maximumLODLevel}," +
            $"\"texture_mip_limit\":{(authoredCaptured ? authoredMipLimit : QualitySettings.globalTextureMipmapLimit)},\"texture_streaming\":{B(QualitySettings.streamingMipmapsActive)}," +
            $"\"graphics_memory_mb\":{SystemInfo.graphicsMemorySize},\"system_memory_mb\":{SystemInfo.systemMemorySize}," +
            $"\"resolution\":\"{Screen.width}x{Screen.height}\",\"window_mode\":{S(Screen.fullScreenMode.ToString())},\"vsync\":{QualitySettings.vSyncCount}," +
            $"\"frame_timing_stats\":{B(FrameTimingManager.IsFeatureEnabled())},\"seed\":{Seed},\"arm\":{S(arm)},\"run\":{run},\"argument_errors\":[{string.Join(",", ArgErrors.Select(S))}]";

        static string ExeSha()
        {
            if (Application.isEditor) return "editor";
            try
            {
                var path = Process.GetCurrentProcess().MainModule.FileName;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var f = File.OpenRead(path))
                    return string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
            }
            catch (System.Exception e) { return "unreadable: " + e.Message; }
        }

        static string LogDir()
        {
            var dir = Path.GetDirectoryName(Application.consoleLogPath);
            return string.IsNullOrEmpty(dir) ? Application.persistentDataPath : dir;
        }

        static void Progress(string line)
        {
            if (Application.isEditor) return;
            File.AppendAllText(Path.Combine(LogDir(), "progress.txt"), System.DateTime.UtcNow.ToString("o") + " " + line + "\n");
        }

        // ----- counters ----------------------------------------------------------------------------------------

        sealed class CounterSet : System.IDisposable
        {
            readonly ProfilerRecorder[] render, grd;
            readonly List<double>[] renderVals, grdVals;
            ProfilerRecorder mainThread;
            readonly List<double> mainMs = new List<double>();

            public CounterSet()
            {
                render = RenderCounters.Select(c => ProfilerRecorder.StartNew(ProfilerCategory.Render, c)).ToArray();
                var grdCat = new ProfilerCategory("GPU Resident Drawer");
                grd = GrdCounters.Select(c => ProfilerRecorder.StartNew(grdCat, c)).ToArray();
                renderVals = render.Select(_ => new List<double>()).ToArray();
                grdVals = grd.Select(_ => new List<double>()).ToArray();
                mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            }

            public void Sample()
            {
                for (int i = 0; i < render.Length; i++) if (render[i].Valid) renderVals[i].Add(render[i].LastValue);
                for (int i = 0; i < grd.Length; i++) if (grd[i].Valid) grdVals[i].Add(grd[i].LastValue);
                if (mainThread.Valid && mainThread.LastValue > 0) mainMs.Add(mainThread.LastValue / 1e6);
            }

            static string Key(string name) => name.Replace(" Count", "").Replace(" ", "_").ToLowerInvariant();
            static string Sum(List<double> v) => v.Count > 0 ? FrustumMath.Json(FrustumMath.Summarize(v)) : "null";

            /// <param name="gpuFrameMs">ResourceRecorder.GpuFrameMsJson(): a summary, or "null" when the API gave no GPU time.</param>
            public string Json(string gpuFrameMs) =>
                "{" + string.Join(",", RenderCounters.Select((c, i) => $"\"{Key(c)}\":{Sum(renderVals[i])}")) +
                ",\"grd\":{" + string.Join(",", GrdCounters.Select((c, i) => $"\"{Key(c)}\":{Sum(grdVals[i])}")) + "}" +
                $",\"extra_cpu_main_thread_ms\":{Sum(mainMs)},\"gpu_frame_ms\":{gpuFrameMs ?? "null"}" + "}";

            public void Dispose()
            {
                foreach (var r in render) r.Dispose();
                foreach (var r in grd) r.Dispose();
                mainThread.Dispose();
            }
        }

        // ----- census (Q1) -------------------------------------------------------------------------------------

        sealed class Tally
        {
            public int enabled, submitted, fogVisible, band0, band1, band2;
            public int lodHidden, lodCulled, submittedAfterLod, terrainOccluded, occlusionUntestedLarge, isVisible, shadowCandidates;
            public string Json() =>
                $"{{\"enabled\":{enabled},\"submitted\":{submitted},\"fog_visible\":{fogVisible},\"band_0_fogstart\":{band0},\"band_fogstart_fogend\":{band1},\"band_fogend_far\":{band2}," +
                $"\"extra\":{{\"lod_hidden_level\":{lodHidden},\"lod_culled\":{lodCulled},\"submitted_after_lod\":{submittedAfterLod},\"terrain_occluded\":{terrainOccluded}," +
                $"\"occlusion_untested_large\":{occlusionUntestedLarge},\"is_visible_flag\":{isVisible},\"shadow_caster_candidates\":{shadowCandidates}}}" + "}";
        }

        static Plane[] Planes(Camera cam, float farPlane)
        {
            var proj = Matrix4x4.Perspective(cam.fieldOfView, cam.aspect, cam.nearClipPlane, Mathf.Max(cam.nearClipPlane + 0.01f, farPlane));
            return GeometryUtility.CalculateFrustumPlanes(proj * cam.worldToCameraMatrix);
        }

        static List<(TerrainCollider col, Bounds b)> tileColliders;
        static List<Vector3> treePositions;

        static bool HiddenByTerrain(Vector3 eye, Vector3 point)
        {
            var dir = point - eye;
            float dist = dir.magnitude;
            if (dist < 0.5f) return false;
            var ray = new Ray(eye, dir / dist);
            foreach (var (col, b) in tileColliders)
            {
                if (!b.IntersectRay(ray, out float enter) || enter > dist) continue;
                if (col.Raycast(ray, out _, dist - 0.25f)) return true;
            }
            return false;
        }

        static bool TerrainOccluded(Bounds b, Vector3 eye)
        {
            if (!HiddenByTerrain(eye, b.center)) return false;
            for (int i = 0; i < 8; i++)
                if (!HiddenByTerrain(eye, Vector3.Lerp(FrustumMath.Corner(b, i), b.center, CornerPull))) return false;
            return true;
        }

        static void PrepareStatics()
        {
            if (tileColliders == null)
            {
                tileColliders = new List<(TerrainCollider, Bounds)>();
                foreach (var t in Terrain.activeTerrains)
                {
                    var c = t.GetComponent<TerrainCollider>();
                    if (c && c.enabled) tileColliders.Add((c, c.bounds));
                }
            }
            if (treePositions == null)
            {
                treePositions = new List<Vector3>();
                foreach (var t in Terrain.activeTerrains)
                {
                    var d = t.terrainData;   // read only
                    if (!d) continue;
                    var o = t.transform.position;
                    foreach (var inst in d.treeInstances) treePositions.Add(o + Vector3.Scale(inst.position, d.size));
                }
            }
        }

        /// <summary>Q1 census for the camera's current pose; definitions in the class header.</summary>
        static string Census(Camera cam)
        {
            var sw = Stopwatch.StartNew();
            PrepareStatics();
            var eye = cam.transform.position;
            var fwd = cam.transform.forward;
            float farNow = cam.farClipPlane;
            var planesNow = Planes(cam, farNow);
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var data = cam.GetUniversalAdditionalCameraData();
            bool shadowsDrawn = rp && rp.shadowDistance > 0f && (!data || data.renderShadows);
            var planesShadow = shadowsDrawn ? Planes(cam, Mathf.Min(rp.shadowDistance, farNow)) : null;
            bool fog = RenderSettings.fog;
            float fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;
            var cullNow = cam.layerCullDistances;
            bool spherical = cam.layerCullSpherical;

            var lodState = new Dictionary<Renderer, int>();   // 1 drawn, 0 hidden level, -1 culled
            float lodBias = QualitySettings.lodBias;
            int maxLevel = QualitySettings.maximumLODLevel;
            foreach (var lg in Object.FindObjectsByType<LODGroup>())
            {
                if (!lg.enabled) continue;
                var lods = lg.GetLODs();
                if (lods.Length == 0) continue;
                var tr = lg.transform;
                var s = tr.lossyScale;
                float size = lg.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
                float dist = Vector3.Distance(eye, tr.TransformPoint(lg.localReferencePoint));
                int level = FrustumMath.LodIndex(FrustumMath.RelativeHeight(size, dist, cam.fieldOfView, lodBias), lods.Select(l => l.screenRelativeTransitionHeight).ToArray());
                if (level >= 0) level = Mathf.Max(level, Mathf.Min(maxLevel, lods.Length - 1));
                for (int i = 0; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r) lodState[r] = level < 0 ? -1 : (i == level ? 1 : 0);
            }

            var total = new Tally();
            var byLayer = new SortedDictionary<string, Tally>();
            var byType = new SortedDictionary<string, Tally>();
            var byRoot = new SortedDictionary<string, Tally>();
            foreach (var r in Object.FindObjectsByType<Renderer>())
            {
                if (!r.enabled || r.forceRenderingOff) continue;
                int layer = r.gameObject.layer;
                string ln = LayerName(layer);
                string type = r is MeshRenderer ? "MeshRenderer" : "other";
                if (!byLayer.TryGetValue(ln, out var tl)) byLayer[ln] = tl = new Tally();
                if (!byType.TryGetValue(type, out var tt)) byType[type] = tt = new Tally();
                string rootName = r.transform.root.name;
                if (!byRoot.TryGetValue(rootName, out var tr2)) byRoot[rootName] = tr2 = new Tally();
                var b = r.bounds;
                bool inFrustum = GeometryUtility.TestPlanesAABB(planesNow, b);
                bool layerCut = FrustumMath.LayerCulled(b, eye, fwd, layer < cullNow.Length ? cullNow[layer] : 0f, spherical);
                bool submitted = inFrustum && !layerCut;
                float depth = submitted ? FrustumMath.MinViewDepth(b, eye, fwd) : 0f;
                bool fogVisible = submitted && (!fog || depth < fogEnd);
                int lod = lodState.TryGetValue(r, out int ls) ? ls : 1;
                bool afterLod = submitted && lod == 1;
                bool big = b.size.x > OccluderMaxSize || b.size.y > OccluderMaxSize || b.size.z > OccluderMaxSize;
                bool occluded = afterLod && fogVisible && !big && TerrainOccluded(b, eye);
                bool shadow = planesShadow != null && r.shadowCastingMode != ShadowCastingMode.Off && GeometryUtility.TestPlanesAABB(planesShadow, b);
                foreach (var t in new[] { total, tl, tt, tr2 })
                {
                    t.enabled++;
                    if (submitted) t.submitted++;
                    if (fogVisible) t.fogVisible++;
                    if (submitted && depth < fogStart) t.band0++;
                    else if (submitted && depth < fogEnd) t.band1++;
                    else if (submitted) t.band2++;
                    if (submitted && lod == 0) t.lodHidden++;
                    if (submitted && lod == -1) t.lodCulled++;
                    if (afterLod) t.submittedAfterLod++;
                    if (occluded) t.terrainOccluded++;
                    if (afterLod && fogVisible && big) t.occlusionUntestedLarge++;
                    if (r.isVisible) t.isVisible++;
                    if (shadow) t.shadowCandidates++;
                }
            }

            int tilesIn = 0;
            foreach (var t in Terrain.activeTerrains)
            {
                var d = t.terrainData;
                if (d && GeometryUtility.TestPlanesAABB(planesNow, new Bounds(t.transform.position + d.bounds.center, d.bounds.size))) tilesIn++;
            }
            float treeDist = Terrain.activeTerrains.Length > 0 ? Terrain.activeTerrains.Max(t => t.treeDistance) : 0f;
            int treesNear = 0, treesFrustum = 0, treesFogged = 0;
            foreach (var p in treePositions)
            {
                if ((p - eye).sqrMagnitude > treeDist * treeDist) continue;
                treesNear++;
                bool inside = true;
                foreach (var pl in planesNow) if (pl.GetDistanceToPoint(p) < -TreeRadius) { inside = false; break; }
                if (!inside) continue;
                treesFrustum++;
                if (fog && Vector3.Dot(p - eye, fwd) - TreeRadius >= fogEnd) treesFogged++;
            }
            sw.Stop();
            return $"{{\"census_ms\":{F(sw.Elapsed.TotalMilliseconds)},\"camera_far\":{F(farNow)},\"fog_start\":{F(fogStart)},\"fog_end\":{F(fogEnd)},\"total\":{total.Json()}," +
                   $"\"by_type\":{{{string.Join(",", byType.Select(kv => S(kv.Key) + ":" + kv.Value.Json()))}}}," +
                   $"\"by_layer\":{{{string.Join(",", byLayer.Select(kv => S(kv.Key) + ":" + kv.Value.Json()))}}}," +
                   $"\"by_root\":{{{string.Join(",", byRoot.Select(kv => S(kv.Key) + ":" + kv.Value.Json()))}}}," +
                   $"\"terrain\":{{\"tiles\":{Terrain.activeTerrains.Length},\"tiles_in_frustum\":{tilesIn},\"tree_distance\":{F(treeDist)},\"tree_instances\":{treePositions.Count}," +
                   $"\"trees_within_tree_distance\":{treesNear},\"trees_within_tree_distance_in_frustum\":{treesFrustum},\"trees_past_fog_end\":{treesFogged}}}" + "}";
        }

        // ----- stills -------------------------------------------------------------------------------------------

        static IEnumerator Grab(System.Action<Texture2D> got, int frames = GrabFrames)
        {
            for (int i = 0; i < frames; i++) yield return null;
            yield return new WaitForEndOfFrame();
            got(ScreenCapture.CaptureScreenshotAsTexture());
        }

        static void Save(Texture2D t, string file) => File.WriteAllBytes(Path.Combine(LogDir(), file), t.EncodeToPNG());

        // The latest A window's still per heading: every window is compared with it (A against A is the noise floor).
        static readonly Dictionary<string, Texture2D> LastA = new Dictionary<string, Texture2D>();

        /// <summary>Q3: per layer cull and detail distance in the variant, a still aimed at the cull edge, culled against shown.</summary>
        static IEnumerator EdgeStills(Camera cam, TrialVariant v, string prefix, System.Action<string> result)
        {
            var eye = cam.transform.position;
            var home = cam.transform.rotation;
            var rows = new List<string>();
            foreach (var kv in v.layerCull)
            {
                int layer = LayerMask.NameToLayer(kv.Key);
                float d = kv.Value;
                if (layer < 0 || d <= 0f) { rows.Add($"{{\"layer\":{S(kv.Key)},\"found\":false,\"why\":\"unknown layer or no distance\"}}"); continue; }
                Renderer best = null; float bestGap = float.MaxValue;
                foreach (var r in Object.FindObjectsByType<Renderer>())
                {
                    if (!r.enabled || r.gameObject.layer != layer) continue;
                    float gap = Mathf.Abs(Vector3.Distance(eye, r.bounds.center) - d);
                    if (gap < bestGap) { bestGap = gap; best = r; }
                }
                if (!best || bestGap > d * EdgeWindow) { rows.Add($"{{\"layer\":{S(kv.Key)},\"cull_m\":{F(d)},\"found\":false,\"why\":\"no renderer on the layer within 25% of the distance\"}}"); continue; }
                var target = best.bounds.center;
                cam.transform.rotation = Quaternion.LookRotation(target - eye);
                Texture2D culled = null, shown = null;
                yield return Grab(t => culled = t);
                var dist = cam.layerCullDistances;
                float keep = dist[layer];
                dist[layer] = authoredLayerCull[layer];
                cam.layerCullDistances = dist;
                yield return Grab(t => shown = t);
                dist[layer] = keep;
                cam.layerCullDistances = dist;
                string file = $"{prefix}-edge-{kv.Key}";
                Save(culled, file + "-culled.png"); Save(shown, file + "-shown.png");
                float depth = Vector3.Dot(target - eye, cam.transform.forward);
                rows.Add($"{{\"layer\":{S(kv.Key)},\"cull_m\":{F(d)},\"found\":true,\"renderer\":{S(HierarchyPath(best.transform))},\"type\":{S(best.GetType().Name)},\"distance_m\":{F(Vector3.Distance(eye, target))}," +
                         $"\"fog_fraction\":{F(FrustumMath.FogFraction(depth, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance))}," +
                         $"\"culled\":{S(file + "-culled.png")},\"shown\":{S(file + "-shown.png")},\"changed\":{FarViewProbe.Changed(culled, shown, Path.Combine(LogDir(), file + "-mask.png"))}" + "}");
                Object.Destroy(culled); Object.Destroy(shown);
            }
            if (v.detail >= 0f)
            {
                var flat = Quaternion.Euler(0f, home.eulerAngles.y, 0f) * Vector3.forward;
                var ground = eye + flat * Mathf.Max(1f, v.detail);
                var tile = TerrainQuery.TileAtOrNearest(ground);
                ground.y = tile ? tile.SampleHeight(ground) + tile.transform.position.y : eye.y - FrustumSpots.Eye;
                cam.transform.rotation = Quaternion.LookRotation(ground - eye);
                Texture2D culled = null, shown = null;
                yield return Grab(t => culled = t);
                foreach (var t in Terrain.activeTerrains) if (AuthoredDetail.TryGetValue(t, out float a)) t.detailObjectDistance = a;
                yield return Grab(t => shown = t);
                foreach (var t in Terrain.activeTerrains) t.detailObjectDistance = v.detail;
                string file = $"{prefix}-edge-details";
                Save(culled, file + "-culled.png"); Save(shown, file + "-shown.png");
                float depth = Vector3.Dot(ground - eye, cam.transform.forward);
                rows.Add($"{{\"layer\":\"terrain details\",\"cull_m\":{F(v.detail)},\"found\":true,\"aim\":[{F(ground.x)},{F(ground.y)},{F(ground.z)}]," +
                         $"\"fog_fraction\":{F(FrustumMath.FogFraction(depth, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance))}," +
                         $"\"culled\":{S(file + "-culled.png")},\"shown\":{S(file + "-shown.png")},\"changed\":{FarViewProbe.Changed(culled, shown, Path.Combine(LogDir(), file + "-mask.png"))}" + "}");
                Object.Destroy(culled); Object.Destroy(shown);
            }
            if (v.smallMesh >= 0f && asset && !Application.isEditor)
            {
                // GRD's screen-size cull: minScreenRelativeHeight = smallMesh * 0.01 (InstanceCuller.cs:2218). Aim at the MeshRenderer
                // whose relative height (bounds extent, Unity's LOD formula, lodBias 1) is closest to it from this eye.
                float threshold = v.smallMesh * 0.01f;
                Renderer best = null; float bestGap = float.MaxValue, bestRh = 0f;
                foreach (var r in Object.FindObjectsByType<MeshRenderer>())
                {
                    if (!r.enabled) continue;
                    var b = r.bounds;
                    float dist = Vector3.Distance(eye, b.center);
                    if (dist < 1f || dist > cam.farClipPlane) continue;
                    float rh = FrustumMath.RelativeHeight(b.size.magnitude, dist, cam.fieldOfView, 1f);
                    float gap = Mathf.Abs(rh - threshold);
                    if (gap < bestGap) { bestGap = gap; best = r; bestRh = rh; }
                }
                if (!best) rows.Add("{\"lever\":\"smallMesh\",\"found\":false}");
                else
                {
                    var target = best.bounds.center;
                    cam.transform.rotation = Quaternion.LookRotation(target - eye);
                    Texture2D culled = null, shown = null;
                    yield return Grab(t => culled = t);
                    asset.smallMeshScreenPercentage = authoredSmallMesh;
                    yield return Grab(t => shown = t);
                    asset.smallMeshScreenPercentage = v.smallMesh;
                    yield return null;
                    string file = $"{prefix}-edge-smallMesh";
                    Save(culled, file + "-culled.png"); Save(shown, file + "-shown.png");
                    float depth = Vector3.Dot(target - eye, cam.transform.forward);
                    rows.Add($"{{\"lever\":\"smallMesh\",\"percent\":{F(v.smallMesh)},\"found\":true,\"renderer\":{S(HierarchyPath(best.transform))},\"relative_height_estimate\":{F(bestRh)}," +
                             $"\"distance_m\":{F(Vector3.Distance(eye, target))},\"fog_fraction\":{F(FrustumMath.FogFraction(depth, RenderSettings.fogStartDistance, RenderSettings.fogEndDistance))}," +
                             $"\"culled\":{S(file + "-culled.png")},\"shown\":{S(file + "-shown.png")},\"changed\":{FarViewProbe.Changed(culled, shown, Path.Combine(LogDir(), file + "-mask.png"))}" + "}");
                    Object.Destroy(culled); Object.Destroy(shown);
                }
            }
            cam.transform.rotation = home;
            result("[" + string.Join(",", rows) + "]");
        }

        /// <summary>The frozen still views of a window: compass 000/090/180/270, then "own" at the pose yaw (label, yaw in degrees).</summary>
        public static List<(string label, float yaw)> StillViews(float poseYaw)
        {
            var v = StillHeadings.Select(h => (h.ToString("000"), (float)h)).ToList();
            v.Add((OwnLabel, poseYaw));
            return v;
        }

        static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        // ----- sessions -----------------------------------------------------------------------------------------

        IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (spotMode) Screen.SetResolution(Width, Height, FullScreenMode.Windowed);
            Application.quitting += OnQuit;
            exeSha = ExeSha();   // before any timing
            yield return null;
            yield return null;
            var cam = Camera.main;
            if (!cam) { Debug.LogError("[Frustum] no main camera; nothing applied."); Progress("FAILED no main camera"); yield break; }
            CaptureAuthored(cam);
            if (!Mathf.Approximately(authoredFar, AuthoredFarExpected)) Debug.LogWarning($"[Frustum] authored far plane is {authoredFar}, not {AuthoredFarExpected}.");
            foreach (var e in ArgErrors) Debug.LogError("[Frustum] argument: " + e);
            Progress("START " + (spotMode ? "spots " + string.Join(",", SpotNames) + " variants " + string.Join(",", Variants.Select(v => v.id)) : "route " + cli.id) + $" run={run} arm={arm}");
            if (walkMode)
            {
                Apply(cam, cli);
                yield return null;
                var problems = new List<string>();
                routeReadbackStart = Readback(cam, cli, problems);
                Debug.Log($"[Frustum] route variant {cli.Json()} readback {routeReadbackStart} problems [{string.Join("; ", problems)}]");
                routeProblems.AddRange(problems);
                routeCounters = new CounterSet();
                routeTextureStart = ResourceRecorder.TextureSnapshotJson();
                routeResources = new ResourceRecorder();
                BellsBendFpsWalk.TimedFrame += OnTimedFrame;
                yield break;   // the walk drives the session and quits; OnQuit writes the result
            }
            yield return RunSpots(cam);
        }

        static readonly List<string> routeProblems = new List<string>();

        void LateUpdate()
        {
            // Hold the window's far plane against any other writer (counted per session).
            var cam = Camera.main;
            if (!cam || !authoredCaptured) return;
            float want = WantFar(current);
            if (!Mathf.Approximately(cam.farClipPlane, want)) { cam.farClipPlane = want; farReapplied++; }
        }

        static void OnTimedFrame(string leg, double ms)
        {
            if (leg == "lookdown") { LookMs.Add(ms); return; }
            RouteMs.Add(ms);
            routeCounters?.Sample();
            routeResources?.Sample();
        }

        static void OnQuit()
        {
            BellsBendFpsWalk.TimedFrame -= OnTimedFrame;
            try
            {
                if (walkMode && !routeWritten && !Application.isEditor)
                {
                    routeWritten = true;
                    var cam = Camera.main;
                    string end = cam ? Readback(cam, cli, routeProblems) : "null";
                    bool valid = routeProblems.Count == 0 && RouteMs.Count > 0 && ArgErrors.Count == 0;
                    var json = "{" + Machine() + $",\"mode\":\"route\",\"route\":\"bb\",\"variant\":{cli.Json()},\"valid\":{B(valid)},\"problems\":[{string.Join(",", routeProblems.Select(S))}]," +
                               $"\"readback_start\":{routeReadbackStart},\"readback_end\":{end},\"far_reapplied\":{farReapplied}," +
                               $"\"frame_ms\":{FrustumMath.Json(FrustumMath.Summarize(RouteMs))},\"lookdown_ms\":{FrustumMath.Json(FrustumMath.Summarize(LookMs))}," +
                               $"\"counters\":{(routeCounters != null ? routeCounters.Json(routeResources?.GpuFrameMsJson()) : "null")}," +
                               $"\"resources\":{(routeResources != null ? routeResources.Json(routeTextureStart, ResourceRecorder.TextureSnapshotJson()) : "null")}" + "}";
                    File.WriteAllText(Path.Combine(LogDir(), "frustum-route.json"), json);
                    Debug.Log("[FRUSTUMJSON]" + json + "[/FRUSTUMJSON]");
                    Progress(RouteMs.Count > 0 ? "DONE route" : "FAILED route: no timed frames (is -bbFpsWalk on?)");
                }
                routeCounters?.Dispose();
                routeResources?.Dispose();
            }
            finally { RestoreAsset(); RestoreQuality(); }
        }

        IEnumerator RunSpots(Camera cam)
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player)
            {
                player.enabled = false;
                var cc = player.GetComponent<CharacterController>();
                if (cc) cc.enabled = false;
            }
            var mover = player ? player.transform : cam.transform;
            var poses = FrustumSpots.WithOverrides(FrustumSpots.Defaults(cabHeight), PoseOverrides);
            var windows = TrialVariant.Windows(Variants);
            var rows = new List<string>();
            string jsonl = Path.Combine(LogDir(), "frustum-windows.jsonl");
            foreach (var name in SpotNames)
            {
                int pi = poses.FindIndex(p => p.name == name);
                if (pi < 0) { ArgErrors.Add($"no pose named '{name}'"); Progress("SKIP " + name + " no such pose"); continue; }
                var pose = poses[pi];
                currentSpot = name;
                var ground = new Vector3(pose.x, 0f, pose.z);
                var tile = TerrainQuery.TileAtOrNearest(ground);
                float gy = tile ? tile.SampleHeight(ground) + tile.transform.position.y : 0f;
                var eye = new Vector3(pose.x, gy + pose.heightAboveGround, pose.z);
                string poseJson = $"{{\"x\":{F(pose.x)},\"z\":{F(pose.z)},\"yaw\":{F(pose.yaw)},\"pitch\":{F(pose.pitch)},\"height_above_ground\":{F(pose.heightAboveGround)}," +
                                  $"\"ground_y\":{F(gy)},\"eye\":[{F(eye.x)},{F(eye.y)},{F(eye.z)}],\"tile\":{S(tile ? tile.name : "none")},\"source\":{S(pose.source)}" +
                                  (name == FrustumSpots.Cab ? $",\"cab_height_source\":{S(cabHeightSource)}" : "") + "}";
                foreach (var t in LastA.Values) Destroy(t);
                LastA.Clear();
                // Warm-up: A for -fcWarmup seconds at the pose heading, discarded.
                Apply(cam, TrialVariant.Authored());
                Progress($"WARMUP {name} {warmup}s");
                for (float wt = 0f; wt < warmup; wt += Time.unscaledDeltaTime)
                {
                    mover.SetPositionAndRotation(eye, Quaternion.Euler(0f, pose.yaw, 0f));
                    cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(pose.pitch, pose.yaw, 0f));
                    yield return null;
                }
                for (int w = 0; w < windows.Count; w++)
                {
                    var v = windows[w];
                    string row = null;
                    yield return Window(cam, mover, name, pose, eye, poseJson, w, v, r => row = r);
                    rows.Add(row);
                    if (!Application.isEditor) File.AppendAllText(jsonl, row.Replace("\n", " ") + "\n");
                }
                Progress("SPOT-DONE " + name);
            }
            currentSpot = "";
            Apply(cam, TrialVariant.Authored());
            RestoreAsset();
            RestoreQuality();
            var json = "{" + Machine() + $",\"mode\":\"spots\",\"warmup_s\":{F(warmup)},\"settle_s\":{F(settle)},\"yaw_speed_deg_s\":{F(yawSpeed)},\"still_headings\":[\"000\",\"090\",\"180\",\"270\",\"own\"]," +
                       $"\"variants\":[{string.Join(",", Variants.Select(x => x.Json()))}],\"far_reapplied\":{farReapplied},\"windows\":[{string.Join(",", rows)}]" + "}";
            if (!Application.isEditor) File.WriteAllText(Path.Combine(LogDir(), "frustum-spots.json"), json);
            Debug.Log("[FRUSTUMJSON]" + json + "[/FRUSTUMJSON]");
            foreach (var t in LastA.Values) Destroy(t);
            LastA.Clear();
            Progress(ArgErrors.Count == 0 ? "DONE spots" : "DONE spots with argument errors: " + string.Join("; ", ArgErrors));
            Application.Quit();
        }

        IEnumerator Window(Camera cam, Transform mover, string spot, SpotPose pose, Vector3 eye, string poseJson, int index, TrialVariant v, System.Action<string> result)
        {
            bool isA = v.IsAuthored;
            string prefix = $"{spot}-r{run}-w{index:00}-{v.id}";
            void Pose(float yaw, float pitch)
            {
                mover.SetPositionAndRotation(eye, Quaternion.Euler(0f, yaw, 0f));
                cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(pitch, yaw, 0f));
            }
            Pose(pose.yaw, pose.pitch);
            Apply(cam, v);
            Progress($"WINDOW {index} {spot} {v.id}");

            float t = 0f;
            while (t < settle) { yield return null; Pose(pose.yaw, pose.pitch); t += Time.unscaledDeltaTime; }
            var problems = new List<string>();
            string readStart = Readback(cam, v, problems);
            string textureStart = ResourceRecorder.TextureSnapshotJson();

            var ms = new List<double>();
            // Addendum 5 (#25 note_technical-director_fish_sample): at a fish_* spot, fish drawn on every timed frame.
            bool countFish = spot.StartsWith(FishSpotPrefix);
            var fishPerFrame = countFish ? new List<int>() : null;
            string counters, resources;
            using (var set = new CounterSet())
            using (var res = new ResourceRecorder())
            {
                float angle = 0f;
                while (angle < 360f)
                {
                    Pose(pose.yaw + angle, pose.pitch);
                    yield return null;
                    float dt = Time.unscaledDeltaTime;
                    ms.Add(dt * 1000.0);
                    set.Sample();
                    res.Sample();
                    if (countFish) fishPerFrame.Add(FishDrawn());
                    angle += yawSpeed * dt;
                }
                counters = set.Json(res.GpuFrameMsJson());
                resources = res.Json(textureStart, ResourceRecorder.TextureSnapshotJson());
            }
            string readEnd = Readback(cam, v, problems);
            bool valid = problems.Count == 0;
            Debug.Log($"[Frustum] window {index} spot {spot} variant {v.id} valid={valid} readback {readEnd} problems [{string.Join("; ", problems)}]");

            // Frozen, outside the timing: census and still per compass heading, and at the pose's own yaw ("own").
            Time.timeScale = 0f;
            var heads = new List<string>();
            foreach (var (label, yaw) in StillViews(pose.yaw))
            {
                Pose(yaw, pose.pitch);
                yield return null;
                string census = Census(cam);
                string still = "null";
                if (shots)
                {
                    Texture2D shot = null;
                    yield return Grab(x => shot = x, v.Temporal ? TemporalGrabFrames : GrabFrames);
                    string file = $"{prefix}-h{label}.png";
                    Save(shot, file);
                    string cmp = "null";
                    if (LastA.TryGetValue(label, out var a)) cmp = FarViewProbe.Changed(a, shot, null);
                    string twin = "";
                    if (!isA && v.HeavyTwin)
                    {
                        // Same frozen frame: the repeat only (noise floor); the authored twin is skipped (class header).
                        yield return new WaitForEndOfFrame();
                        var rep = ScreenCapture.CaptureScreenshotAsTexture();
                        twin = $",\"repeat_same_frame\":{FarViewProbe.Changed(shot, rep, null)},\"authored\":null,\"twin_skipped\":{S(TwinSkipped)}";
                        Destroy(rep);
                    }
                    else if (!isA)
                    {
                        // Same frozen frame: a repeat (noise floor), then the authored settings (the in-frame twin), then back.
                        yield return new WaitForEndOfFrame();
                        var rep = ScreenCapture.CaptureScreenshotAsTexture();
                        Apply(cam, TrialVariant.Authored());
                        Texture2D auth = null;
                        yield return Grab(x => auth = x);
                        var twinProblems = new List<string>();
                        Readback(cam, TrialVariant.Authored(), twinProblems);
                        Apply(cam, v);
                        yield return null;
                        Save(auth, $"{prefix}-h{label}-authored.png");
                        twin = $",\"repeat_same_frame\":{FarViewProbe.Changed(shot, rep, null)},\"authored\":{S($"{prefix}-h{label}-authored.png")}," +
                               $"\"authored_readback_ok\":{B(twinProblems.Count == 0)},\"trial_vs_authored\":{FarViewProbe.Changed(shot, auth, Path.Combine(LogDir(), $"{prefix}-h{label}-mask.png"))}";
                        Destroy(rep); Destroy(auth);
                    }
                    still = $"{{\"file\":{S(file)},\"vs_previous_A\":{cmp}{twin}}}";
                    if (isA) { if (LastA.TryGetValue(label, out var old)) Destroy(old); LastA[label] = shot; }
                    else Destroy(shot);
                }
                heads.Add($"{{\"heading\":{S(label)},\"yaw\":{F(yaw)},\"census\":{census},\"still\":{still}}}");
            }
            string edges = "null";
            if (shots && edgeShots && (v.layerCull.Count > 0 || v.detail >= 0f || v.smallMesh >= 0f))
            {
                Pose(pose.yaw, pose.pitch);
                yield return EdgeStills(cam, v, prefix, r => edges = r);
            }
            Time.timeScale = 1f;

            result($"{{\"spot\":{S(spot)},\"run\":{run},\"window\":{index},\"variant_id\":{S(v.id)},\"variant\":{v.Json()},\"valid\":{B(valid)},\"problems\":[{string.Join(",", problems.Select(S))}]," +
                   $"\"exe_sha256\":{S(exeSha)},\"seed\":{Seed},\"pose\":{poseJson},\"settle_s\":{F(settle)},\"yaw_speed_deg_s\":{F(yawSpeed)}," +
                   $"\"readback_after_settle\":{readStart},\"readback_after_timing\":{readEnd},\"frame_ms\":{FrustumMath.Json(FrustumMath.Summarize(ms))}," +
                   $"\"counters\":{counters},\"resources\":{resources},\"fish_sample\":{(countFish ? FrustumMath.CountJson(fishPerFrame, 1) : "null")}," +
                   $"\"headings\":[{string.Join(",", heads)}],\"edge_stills\":{edges}" + "}");
        }
    }
}
