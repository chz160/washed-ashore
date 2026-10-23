using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Fish;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using Debug = UnityEngine.Debug;

namespace WashedAshore.Perf
{
    /// <summary>
    /// Lookout-tower far-view measurement (tower-td-note.md section 7). Overrides the view distances for one run without
    /// saving anything into World.unity, and proves the arm by logging the values read back from the camera, the fog and
    /// the terrain tiles. Inert unless one of its arguments (or, in the Editor, environment variables) is present.
    ///   -fvFar m        camera far plane, applied to the main camera when its far plane is still the authored one
    ///   -fvTrees m      Terrain.treeDistance on every tile (the time the write takes is recorded)
    ///   -fvFogToFar     fog end = far plane, fog start = a quarter of it
    ///   -fvFogStart m / -fvFogEnd m   explicit linear fog distances (override -fvFogToFar)
    ///   -fvCab          cab run instead of a walk: camera at the tower summit, CabHeight above the ground, one slow
    ///                   360 degree yaw; writes farview-cab.json and the north stills beside Player.log, then quits
    ///   -fvRidge        (with -fvCab) afterwards puts fog and trees back to the authored values, sets the far plane to
    ///                   RidgeFar and takes ground stills from about 1 km looking at the summit, each with a
    ///                   summit-tile-hidden twin and the ridge-line measure (FarViewRidge)
    ///   -fvRidgeEye x z a viewpoint for a ridge still, in world metres; repeat for more. With none given, two are
    ///                   chosen from open ground by map zone and a tree-free sight line
    ///   -fvTowerHeight m  the tower's height from the parameter table, for the ridge-line window (no default)
    ///   -fvSummit x z   the tower site in world metres (default: the projected Central Ridge Summit, below)
    ///   -fvNear m       camera near plane for the whole session, applied like -fvFar and read back into the settings
    ///   -fvNearStills a,b  (with -fvCab) near-plane regression stills at these values against the authored one (FarViewNear)
    ///   -fvCabHeight m  camera height above the ground for the cab position (default CabHeight)
    /// Ground arms combine -fvFar with BellsBendFpsWalk's -waterFpsWalk, which runs unchanged. Editor test runs read
    /// WA_FV_FAR / WA_FV_TREES / WA_FV_FOGTOFAR instead, so a suite can run at an overridden distance.
    /// Still twin: the same frozen frame is captured twice (noise floor) and once with the NorthBarrier root off.
    /// </summary>
    public class FarViewProbe : MonoBehaviour
    {
        const string FarArg = "-fvFar", TreesArg = "-fvTrees", FogArg = "-fvFogToFar", CabArg = "-fvCab", SecondsArg = "-fvCabSeconds", RidgeArg = "-fvRidge", SummitArg = "-fvSummit", FogStartArg = "-fvFogStart", FogEndArg = "-fvFogEnd";
        const string RidgeEyeArg = "-fvRidgeEye", TowerHeightArg = "-fvTowerHeight";
        const string NearArg = "-fvNear", NearStillsArg = "-fvNearStills", CabHeightArg = "-fvCabHeight";
        static float near = -1f, authoredNear = -1f, cabHeight = CabHeight;
        static readonly List<float> NearValues = new List<float>();
        const int Buckets = 36;
        const float RidgeFar = 1200f, RidgeEye = 1.65f, RidgeAim = 13f;
        // Default site: Central Ridge Summit projected by tools/terrain (tower-td-note.md section 7). Once the
        // LookoutTower landmark exists, pass its position with -fvSummit instead of relying on these.
        public const float DefaultSummitX = 546.15f, DefaultSummitZ = -230.94f, CabHeight = 28f;
        const float Warmup = 4f, DefaultSeconds = 60f;
        const int Width = 1920, Height = 1080, HitchFrames = 10;
        const string BarrierRoot = "NorthBarrier";

        static float fogStart = -1f, fogEnd = -1f, towerHeight = -1f;
        // Where the runner read the site and the tower height from (file and hash), echoed into the JSON.
        static string summitSource = "", towerHeightSource = "";
        static string Clean(string s) => (s ?? "").Replace("\\", "/").Replace("\"", "'");
        static string Text(string[] args, string name) { int i = System.Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : ""; }
        static readonly List<Vector2> RidgeEyes = new List<Vector2>();
        static float far = -1f, trees = -1f, seconds = DefaultSeconds, summitX = DefaultSummitX, summitZ = DefaultSummitZ;
        static bool fogToFar, cab, ridge, fogCaptured;
        static float authoredFar = -1f, authoredFogStart, authoredFogEnd, authoredTrees = -1f;
        static double treeApplyMs = -1;
        static readonly HashSet<Camera> Seen = new HashSet<Camera>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            far = near = authoredNear = trees = fogStart = fogEnd = towerHeight = -1f; cabHeight = CabHeight; NearValues.Clear(); RidgeEyes.Clear(); seconds = DefaultSeconds; summitX = DefaultSummitX; summitZ = DefaultSummitZ; fogToFar = cab = ridge = fogCaptured = false; authoredFar = authoredTrees = -1f; treeApplyMs = -1; Seen.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            far = Number(args, FarArg, "WA_FV_FAR");
            trees = Number(args, TreesArg, "WA_FV_TREES");
            fogToFar = args.Contains(FogArg) || (Application.isEditor && System.Environment.GetEnvironmentVariable("WA_FV_FOGTOFAR") == "1");
            cab = args.Contains(CabArg);
            fogStart = Number(args, FogStartArg, null);
            fogEnd = Number(args, FogEndArg, null);
            ridge = args.Contains(RidgeArg);
            towerHeight = Number(args, TowerHeightArg, null);
            near = Number(args, NearArg, null);
            float ch = Number(args, CabHeightArg, null);
            if (ch > 0f) cabHeight = ch;
            // -fvNearStills a,b: near-plane values to compare with the authored one in a cab session.
            foreach (var part in Text(args, NearStillsArg).Split(','))
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float nv) && nv > 0f) NearValues.Add(nv);
            summitSource = Text(args, "-fvSummitSource");
            towerHeightSource = Text(args, "-fvTowerHeightSource");
            // -fvRidgeEye x z may be given more than once: one ridge still per pair.
            for (int i = 0; i + 2 < args.Length; i++)
                if (args[i] == RidgeEyeArg
                    && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float ex)
                    && float.TryParse(args[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float ez)) RidgeEyes.Add(new Vector2(ex, ez));
            float s = Number(args, SecondsArg, null);
            if (s > 0f) seconds = s;
            int si = System.Array.IndexOf(args, SummitArg);
            if (si >= 0 && si + 2 < args.Length
                && float.TryParse(args[si + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sx)
                && float.TryParse(args[si + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float sz)) { summitX = sx; summitZ = sz; }
            if (far <= 0f && near <= 0f && trees <= 0f && fogStart <= 0f && fogEnd <= 0f && !fogToFar && !cab) return;

            var go = new GameObject("FarViewProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<FarViewProbe>();
            SceneManager.sceneLoaded += (scene, mode) => Apply("scene " + scene.name);
            Application.quitting += () => Debug.Log("[FarView] at quit: " + Settings());
            Apply("boot");
        }

        static float Number(string[] args, string name, string env)
        {
            int i = System.Array.IndexOf(args, name);
            string text = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            if (text == null && env != null && Application.isEditor) text = System.Environment.GetEnvironmentVariable(env);
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : -1f;
        }

        /// <summary>Fog and trees, once per scene load. The camera is handled in LateUpdate (it can appear later).</summary>
        static void Apply(string when)
        {
            if ((fogToFar && far > 0f) || fogStart > 0f || fogEnd > 0f)
            {
                if (!fogCaptured) { authoredFogStart = RenderSettings.fogStartDistance; authoredFogEnd = RenderSettings.fogEndDistance; fogCaptured = true; }
                if (fogToFar && far > 0f) { RenderSettings.fogEndDistance = far; RenderSettings.fogStartDistance = far * 0.25f; }
                if (fogEnd > 0f) RenderSettings.fogEndDistance = fogEnd;
                if (fogStart > 0f) RenderSettings.fogStartDistance = fogStart;
            }
            if (trees > 0f && !cab) ApplyTrees();   // the cab run writes them itself, to time the hitch
            ApplyCamera();
            Debug.Log($"[FarView] applied at {when}: " + Settings());
        }

        static void ApplyTrees()
        {
            if (authoredTrees < 0f && Terrain.activeTerrains.Length > 0) authoredTrees = Terrain.activeTerrains[0].treeDistance;
            var sw = Stopwatch.StartNew();
            foreach (var t in Terrain.activeTerrains) t.treeDistance = trees;
            sw.Stop();
            treeApplyMs = sw.Elapsed.TotalMilliseconds;
        }

        static void ApplyCamera()
        {
            var cam = Camera.main;
            if ((far <= 0f && near <= 0f) || !cam || !Seen.Add(cam)) return;
            if (far > 0f)
            {
                if (authoredFar < 0f) authoredFar = cam.farClipPlane;
                // Only a camera still at the authored distance is moved: a test that sets its own far plane keeps it.
                if (Mathf.Approximately(cam.farClipPlane, authoredFar)) cam.farClipPlane = far;
            }
            if (near > 0f)
            {
                if (authoredNear < 0f) authoredNear = cam.nearClipPlane;
                if (Mathf.Approximately(cam.nearClipPlane, authoredNear)) cam.nearClipPlane = near;
            }
        }

        void LateUpdate() => ApplyCamera();

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Requested values and the values read back from the engine, as JSON members (no braces).</summary>
        static string Settings()
        {
            var cam = Camera.main;
            var tiles = Terrain.activeTerrains;
            return $"\"requested_far\":{F(far)},\"requested_trees\":{F(trees)},\"fog_to_far\":{(fogToFar ? "true" : "false")}," +
                   $"\"authored_far\":{F(authoredFar)},\"camera_far\":{F(cam ? cam.farClipPlane : -1f)}," +
                   $"\"requested_near\":{F(near)},\"authored_near\":{F(authoredNear)},\"camera_near\":{F(cam ? cam.nearClipPlane : -1f)}," +
                   $"\"fog_start\":{F(RenderSettings.fogStartDistance)},\"fog_end\":{F(RenderSettings.fogEndDistance)}," +
                   $"\"tree_distance_min\":{F(tiles.Length > 0 ? tiles.Min(t => t.treeDistance) : -1f)}," +
                   $"\"tree_distance_max\":{F(tiles.Length > 0 ? tiles.Max(t => t.treeDistance) : -1f)},\"tiles\":{tiles.Length}," +
                   $"\"tree_apply_ms\":{F(treeApplyMs)}";
        }

        static string LogDir()
        {
            var dir = Path.GetDirectoryName(Application.consoleLogPath);
            return string.IsNullOrEmpty(dir) ? Application.persistentDataPath : dir;
        }

        IEnumerator Start()
        {
            if (!cab)
            {
                // Ground arm: the walk (BellsBendFpsWalk) does the measuring; this only records the settings in force.
                yield return null;
                yield return null;
                if (!Application.isEditor) File.WriteAllText(Path.Combine(LogDir(), "farview-settings.json"), "{" + Settings() + "}");
                yield break;
            }

            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Screen.SetResolution(Width, Height, FullScreenMode.Windowed);
            yield return null;
            yield return null;

            var player = FindAnyObjectByType<PlayerController>();
            if (player)
            {
                player.enabled = false;
                var cc = player.GetComponent<CharacterController>();
                if (cc) cc.enabled = false;
            }
            var cam = Camera.main;
            var mover = player ? player.transform : cam.transform;
            var pos = new Vector3(summitX, 0f, summitZ);
            var tile = TerrainQuery.TileAtOrNearest(pos);
            float ground = tile ? tile.SampleHeight(pos) + tile.transform.position.y : 0f;
            pos.y = ground + cabHeight;
            mover.SetPositionAndRotation(pos, Quaternion.identity);
            cam.transform.SetPositionAndRotation(pos, Quaternion.identity);
            yield return null;

            // Tree distance on every tile, timed: the write itself and the worst of the frames that follow.
            double hitchMs = 0;
            if (trees > 0f)
            {
                ApplyTrees();
                for (int i = 0; i < HitchFrames; i++)
                {
                    yield return null;
                    hitchMs = System.Math.Max(hitchMs, Time.unscaledDeltaTime * 1000.0);
                }
            }

            var counters = new[] { "Draw Calls Count", "Batches Count", "SetPass Calls Count", "Triangles Count", "Vertices Count" };
            var recorders = counters.Select(c => ProfilerRecorder.StartNew(ProfilerCategory.Render, c)).ToArray();
            var counts = counters.Select(c => new List<long>()).ToArray();
            var ms = new List<double>();
            // Per 10 degrees of heading: which part of the yaw is expensive.
            var bucketMs = new List<double>[Buckets]; var bucketTris = new List<long>[Buckets];
            for (int i = 0; i < Buckets; i++) { bucketMs[i] = new List<double>(); bucketTris[i] = new List<long>(); }
            int trisIndex = System.Array.IndexOf(counters, "Triangles Count");
            float t = -Warmup;
            while (t < seconds)
            {
                float yaw = Mathf.Max(0f, t) / seconds * 360f;
                mover.rotation = Quaternion.Euler(0f, yaw, 0f);
                cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (t >= 0f)
                {
                    ms.Add(dt * 1000.0);
                    for (int i = 0; i < recorders.Length; i++) if (recorders[i].Valid) counts[i].Add(recorders[i].LastValue);
                    int bucket = Mathf.Clamp((int)(yaw / (360f / Buckets)), 0, Buckets - 1);
                    bucketMs[bucket].Add(dt * 1000.0);
                    if (recorders[trisIndex].Valid) bucketTris[bucket].Add(recorders[trisIndex].LastValue);
                }
                t += dt;
            }

            // North still and its twins, on a frozen frame.
            var barrier = GameObject.Find(BarrierRoot);
            float bearing = 0f, barrierDistance = -1f;
            if (barrier)
            {
                var rs = barrier.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    var to = b.ClosestPoint(pos) - pos;
                    barrierDistance = new Vector2(to.x, to.z).magnitude;
                    bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                }
            }
            mover.rotation = Quaternion.Euler(0f, bearing, 0f);
            cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, bearing, 0f));
            Time.timeScale = 0f;
            string dir = LogDir();
            // Four plain stills round the compass (no twins): an early look at the haze level.
            foreach (int heading in new[] { 0, 90, 180, 270 })
            {
                mover.rotation = Quaternion.Euler(0f, heading, 0f);
                cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, heading, 0f));
                yield return null;
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"heading-{heading:000}.png"), shot.EncodeToPNG());
                Destroy(shot);
            }
            mover.rotation = Quaternion.Euler(0f, bearing, 0f);
            cam.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, bearing, 0f));
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var withA = ScreenCapture.CaptureScreenshotAsTexture();
            yield return new WaitForEndOfFrame();
            var withB = ScreenCapture.CaptureScreenshotAsTexture();
            if (barrier) barrier.SetActive(false);
            yield return null;
            yield return new WaitForEndOfFrame();
            var without = ScreenCapture.CaptureScreenshotAsTexture();
            if (barrier) barrier.SetActive(true);

            File.WriteAllBytes(Path.Combine(dir, "north.png"), withA.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(dir, "north-repeat.png"), withB.EncodeToPNG());
            File.WriteAllBytes(Path.Combine(dir, "north-twin-no-barrier.png"), without.EncodeToPNG());
            string noise = Changed(withA, withB, null);
            string twin = Changed(withA, without, Path.Combine(dir, "north-changed-mask.png"));
            string cabSettings = Settings();   // before the ridge still changes them

            // Ground stills from 1 km: does the summit ridge read against the sky at the ground setting?
            string ridgeJson = "null";
            if (ridge)
            {
                if (fogCaptured) { RenderSettings.fogStartDistance = authoredFogStart; RenderSettings.fogEndDistance = authoredFogEnd; }
                if (authoredTrees > 0f) foreach (var tr in Terrain.activeTerrains) tr.treeDistance = authoredTrees;
                cam.farClipPlane = RidgeFar;
                var summit = new Vector3(summitX, ground, summitZ);
                var aim = summit + Vector3.up * RidgeAim;
                // The water surface is the top of whatever is drawn on the Water layer.
                float water = float.NegativeInfinity;
                int waterLayer = LayerMask.NameToLayer("Water");
                foreach (var r in FindObjectsByType<Renderer>())
                    if (r.gameObject.layer == waterLayer) water = Mathf.Max(water, r.bounds.max.y);
                // Viewpoints: the ones given with -fvRidgeEye, else chosen from open ground by map zone (FarViewRidge.Choose).
                int solid = WashedAshore.World.WorldLayers.SightMask;
                var chosen = FarViewRidge.Choose(summit, aim, RidgeEye, water, solid, RidgeEyes, out string chooseInfo);
                var views = new List<string>();
                for (int v = 0; v < chosen.Count; v++)
                {
                    var from = chosen[v].eye;
                    var look = Quaternion.LookRotation(aim - from);
                    mover.SetPositionAndRotation(from, Quaternion.Euler(0f, look.eulerAngles.y, 0f));
                    cam.transform.SetPositionAndRotation(from, look);
                    yield return null;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    var ridgeA = ScreenCapture.CaptureScreenshotAsTexture();
                    yield return new WaitForEndOfFrame();
                    var ridgeB = ScreenCapture.CaptureScreenshotAsTexture();
                    if (tile) tile.enabled = false;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    var ridgeOff = ScreenCapture.CaptureScreenshotAsTexture();
                    if (tile) tile.enabled = true;
                    string tag = $"ridge-{v + 1}";
                    // Where the tower's footprint falls in the frame (pixels, origin bottom-left) and how tall the tower is
                    // there. The height comes from the parameter table through -fvTowerHeight, never a literal.
                    var baseScreen = cam.WorldToScreenPoint(summit);
                    var topScreen = cam.WorldToScreenPoint(summit + Vector3.up * Mathf.Max(0f, towerHeight));
                    float towerPixels = Mathf.Abs(topScreen.y - baseScreen.y);
                    string line = towerHeight > 0f ? FarViewRidge.RidgeLine(ridgeA, ridgeOff, baseScreen, towerPixels) : "{\"error\":\"-fvTowerHeight not given\"}";
                    string lineNoise = towerHeight > 0f ? FarViewRidge.RidgeLine(ridgeA, ridgeB, baseScreen, towerPixels) : "null";
                    File.WriteAllBytes(Path.Combine(dir, tag + ".png"), ridgeA.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(dir, tag + "-repeat.png"), ridgeB.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(dir, tag + "-twin-no-summit-tile.png"), ridgeOff.EncodeToPNG());
                    views.Add($"{{\"name\":\"{tag}\",\"chosen_by\":\"{chosen[v].chosenBy}\",\"notes\":\"{Clean(chosen[v].notes)}\"," +
                              $"\"eye\":[{F(from.x)},{F(from.y)},{F(from.z)}],\"bearing_from_summit_deg\":{F(chosen[v].bearing)},\"distance_m\":{F(chosen[v].distance)}," +
                              $"\"zone\":\"{chosen[v].zone}\",\"shore_distance_m\":{F(chosen[v].shore)},\"tower_height_m\":{F(towerHeight)}," +
                              $"\"summit_ground_screen_xy\":[{F(baseScreen.x)},{F(baseScreen.y)}],\"tower_top_screen_xy\":[{F(topScreen.x)},{F(topScreen.y)}]," +
                              $"\"ridge_line\":{line},\"ridge_line_same_frame_repeat\":{lineNoise}," +
                              $"\"repeat_same_frame\":{Changed(ridgeA, ridgeB, null)},\"summit_tile_off\":{Changed(ridgeA, ridgeOff, Path.Combine(dir, tag + "-changed-mask.png"))}" + "}");
                    Destroy(ridgeA); Destroy(ridgeB); Destroy(ridgeOff);
                }
                ridgeJson = $"{{\"aim\":[{F(aim.x)},{F(aim.y)},{F(aim.z)}],\"eye_height\":{F(RidgeEye)},\"fov\":{F(cam.fieldOfView)},\"resolution\":\"{Screen.width}x{Screen.height}\",\"water_y\":{F(water)}," +
                            $"\"summit\":[{F(summitX)},{F(summitZ)}],\"summit_source\":\"{Clean(summitSource)}\",\"tower_height_m\":{F(towerHeight)},\"tower_height_source\":\"{Clean(towerHeightSource)}\"," +
                            $"\"summit_tile\":\"{(tile ? tile.name : "missing")}\"," + chooseInfo + "," + Settings() +
                            $",\"views\":[{string.Join(",", views)}]" + "}";
            }

            // Near-plane regression stills (FarViewNear), on the frozen frame, at whatever fog and far plane are in force.
            string nearJson = "null";
            if (NearValues.Count > 0) yield return FarViewNear.Stills(cam, mover, pos, ground, NearValues, dir, r => nearJson = r);

            var s = ms.OrderBy(v => v).ToList();
            double P(double q) => s.Count == 0 ? 0 : s[Mathf.Clamp((int)System.Math.Round(q * (s.Count - 1)), 0, s.Count - 1)];
            string CountJson(int i)
            {
                var c = counts[i].OrderBy(v => v).ToList();
                string key = counters[i].Replace(" Count", "").Replace(" ", "_").ToLowerInvariant();
                return c.Count == 0 ? $"\"{key}\":null" : $"\"{key}\":{{\"median\":{c[c.Count / 2]},\"max\":{c[c.Count - 1]}" + "}";
            }
            var headings = new List<string>();
            for (int i = 0; i < Buckets; i++)
            {
                var bm = bucketMs[i].OrderBy(v => v).ToList(); var bt = bucketTris[i].OrderBy(v => v).ToList();
                headings.Add($"{{\"deg\":{i * 360 / Buckets},\"frames\":{bm.Count},\"median_ms\":{F(bm.Count > 0 ? bm[bm.Count / 2] : 0)}," +
                             $"\"max_ms\":{F(bm.Count > 0 ? bm[bm.Count - 1] : 0)},\"median_triangles\":{(bt.Count > 0 ? bt[bt.Count / 2] : 0)}" + "}");
            }
            var json = "{" +
                $"\"platform\":\"{Application.platform}\",\"development\":{(Debug.isDebugBuild ? "true" : "false")}," +
                $"\"gpu\":\"{SystemInfo.graphicsDeviceName}\",\"cpu\":\"{SystemInfo.processorType}\"," +
                $"\"resolution\":\"{Screen.width}x{Screen.height}\",\"window_mode\":\"{Screen.fullScreenMode}\",\"vsync\":{QualitySettings.vSyncCount}," +
                $"\"quality\":\"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\",\"quality_level\":{QualitySettings.GetQualityLevel()},\"lod_bias\":{F(QualitySettings.lodBias)}," +
                $"\"maximum_lod_level\":{QualitySettings.maximumLODLevel},\"summit\":[{F(summitX)},{F(summitZ)}]," +
                $"\"camera\":[{F(pos.x)},{F(pos.y)},{F(pos.z)}],\"ground_y\":{F(ground)},\"height_above_ground\":{F(cabHeight)},\"fov\":{F(cam.fieldOfView)}," +
                cabSettings + $",\"tree_apply_hitch_ms\":{F(hitchMs)}," +
                $"\"yaw_seconds\":{F(seconds)},\"samples\":{ms.Count},\"duration_s\":{F(ms.Sum() / 1000)}," +
                $"\"median_ms\":{F(P(0.5))},\"p95_ms\":{F(P(0.95))},\"p99_ms\":{F(P(0.99))},\"max_ms\":{F(s.Count > 0 ? s[s.Count - 1] : 0)}," +
                string.Join(",", Enumerable.Range(0, counters.Length).Select(CountJson)) + "," +
                $"\"headings\":[{string.Join(",", headings)}]," +
                $"\"still\":{{\"barrier_root\":\"{(barrier ? BarrierRoot : "missing")}\",\"bearing_deg\":{F(bearing)},\"barrier_distance_m\":{F(barrierDistance)}," +
                $"\"repeat_same_frame\":{noise},\"barrier_off\":{twin}" + "},\"ridge_still\":" + ridgeJson + ",\"near_stills\":" + nearJson + "}";
            foreach (var r in recorders) r.Dispose();
            Debug.Log("[FARVIEWJSON]" + json + "[/FARVIEWJSON]");
            File.WriteAllText(Path.Combine(dir, "farview-cab.json"), json);
            Application.Quit();
        }

        /// <summary>Pixels that differ between two captures (any channel, and by more than 8/255), with their bounding box.</summary>
        internal static string Changed(Texture2D a, Texture2D b, string maskPath)
        {
            var pa = a.GetPixels32(); var pb = b.GetPixels32();
            if (pa.Length != pb.Length) return "{\"error\":\"size differs\"}";
            long sum = 0;
            int over3 = 0;
            int any = 0, strong = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, w = a.width;
            var mask = maskPath != null ? new Color32[pa.Length] : null;
            for (int i = 0; i < pa.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Max(Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b)));
                if (mask != null) mask[i] = d > 0 ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                if (d == 0) continue;
                any++;
                sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
                if (d > FarViewRidge.ChangedOver) over3++;
                if (d > FarViewRidge.StrongOver) strong++;
                int x = i % w, y = i / w;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            if (mask != null)
            {
                var tex = new Texture2D(a.width, a.height, TextureFormat.RGBA32, false);
                tex.SetPixels32(mask);
                File.WriteAllBytes(maskPath, tex.EncodeToPNG());
            }
            string box = any == 0 ? "null" : $"[{minX},{minY},{maxX},{maxY}]";
            // Mean absolute difference per channel on a 0-255 scale: over the changed pixels, and over every pixel of the box.
            string mean = any == 0 ? "0" : F(sum / (3.0 * any));
            string boxMean = any == 0 ? "0" : F(sum / (3.0 * (maxX - minX + 1) * (maxY - minY + 1)));
            return $"{{\"changed_px\":{any},\"changed_px_over_3\":{over3},\"changed_px_over_8\":{strong},\"total_px\":{pa.Length},\"mean_abs_diff_changed_0_255\":{mean}," +
                   $"\"mean_abs_diff_in_bbox_0_255\":{boxMean},\"bbox_xyxy\":{box}" + "}";
        }
    }
}
