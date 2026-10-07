using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Perf
{
    /// <summary>
    /// R2 probe: a scripted walk from the park (PlayerSpawn -> LM_OutdoorCenter) up to the highest ground within
    /// RidgeSearch metres, at 1920x1080 windowed with vSync off, then writes bells-bend-fps.json next to Player.log
    /// and quits. Inert unless the player is started with -bbFpsWalk, so a normal launch never sees it.
    /// -waterFpsWalk instead walks water W10's shore route (park -> the east low bank -> McCord Bluff) and writes
    /// bells-bend-fps-shore.json; the R2 route stays the -bbFpsWalk default.
    /// </summary>
    public class BellsBendFpsWalk : MonoBehaviour
    {
        public const string Arg = "-bbFpsWalk", ShoreArg = "-waterFpsWalk";
        // Optional A8/B8-style arms: deactivate the scene roots before the warm-up.
        const string WildlifeOffArg = "-bbWildlifeOff", BirdsOffArg = "-bbBirdsOff";
        static bool wildlifeOff, birdsOff, shore;
        const float Speed = 5f, Eye = 1.7f, Warmup = 4f, RidgeSearch = 1500f, Grid = 10f;
        const int Width = 1920, Height = 1080;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (!args.Contains(Arg) && !args.Contains(ShoreArg)) return;
            shore = args.Contains(ShoreArg);
            wildlifeOff = args.Contains(WildlifeOffArg);
            birdsOff = args.Contains(BirdsOffArg);
            new GameObject("BellsBendFpsWalk").AddComponent<BellsBendFpsWalk>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Screen.SetResolution(Width, Height, FullScreenMode.Windowed);
            yield return null;
            yield return null;

            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (wildlifeOff && root.name == "Wildlife") root.SetActive(false);
                if (birdsOff && root.name == "Birds") root.SetActive(false);
            }
            var player = FindAnyObjectByType<PlayerController>();
            if (player)
            {
                player.enabled = false;
                var cc = player.GetComponent<CharacterController>();
                if (cc) cc.enabled = false;
            }
            var cam = Camera.main;
            var mover = player ? player.transform : cam.transform;

            var route = shore ? ShoreRoute() : Route();
            float total = 0f;
            for (int i = 1; i < route.Count; i++) total += Vector3.Distance(route[i - 1], route[i]);
            var ms = new List<double>();
            var legMs = new List<double>(); // shore route only: frames from the first bank waypoint on (w-qa W10)
            float legStart = shore ? ShoreLegStart(route) : float.MaxValue;
            float t = -Warmup;
            while (true)
            {
                float d = Mathf.Max(0f, t) * Speed;
                if (d >= total) break;
                Place(mover, route, d);
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (t >= 0f)
                {
                    ms.Add(dt * 1000.0);
                    if (d >= legStart) legMs.Add(dt * 1000.0);
                }
                t += dt;
            }
            Write(ms, route, total, legMs);
            if (WaterLookShots.Requested) yield return WaterLookShots.Run(); // W5/W10 look shots ride on the same launch
            Application.Quit();
        }

        static List<Vector3> Route()
        {
            var pts = new List<Vector3>();
            var spawn = GameObject.Find("PlayerSpawn");
            var centre = GameObject.Find("LM_OutdoorCenter");
            if (spawn) pts.Add(spawn.transform.position);
            if (centre) pts.Add(centre.transform.position);
            var from = pts.Count > 0 ? pts[pts.Count - 1] : Vector3.zero;
            // The ridge: the highest land within RidgeSearch of the park, sampled on a coarse grid.
            Vector3 best = from;
            float bestY = float.MinValue;
            for (float z = -RidgeSearch; z <= RidgeSearch; z += Grid)
                for (float x = -RidgeSearch; x <= RidgeSearch; x += Grid)
                {
                    if (x * x + z * z > RidgeSearch * RidgeSearch) continue;
                    var p = from + new Vector3(x, 0f, z);
                    var tile = TerrainQuery.TileAt(p);
                    if (!tile) continue;
                    float y = tile.SampleHeight(p) + tile.transform.position.y;
                    if (y > bestY) { bestY = y; best = p; }
                }
            pts.Add(best);
            return pts;
        }

        // Water W10 shore route: bank_stations 1410..1611 (the east low-bank run up to McCord Bluff), every 5th station,
        // 6 m inland; generated offline into TestResults/water-w4/support/shore-route.json. Ends on the McCord Bluff landmark.
        static readonly Vector2[] Shore =
        {
            new Vector2(1077.9f, -49.6f), new Vector2(1062.1f, -28.8f), new Vector2(1043.8f, -6.9f), new Vector2(1027.2f, 13.9f),
            new Vector2(1010.6f, 34.5f), new Vector2(993.4f, 53.8f), new Vector2(974.9f, 72.9f), new Vector2(956.5f, 91.9f),
            new Vector2(938.0f, 109.3f), new Vector2(919.0f, 126.6f), new Vector2(900.0f, 143.8f), new Vector2(881.0f, 161.1f),
            new Vector2(862.1f, 178.3f), new Vector2(843.1f, 195.6f), new Vector2(824.0f, 211.7f), new Vector2(803.8f, 227.2f),
            new Vector2(783.6f, 242.7f), new Vector2(763.2f, 258.2f), new Vector2(743.1f, 273.7f), new Vector2(722.9f, 289.2f),
            new Vector2(702.7f, 304.7f), new Vector2(682.4f, 320.2f), new Vector2(661.8f, 336.1f), new Vector2(644.3f, 358.4f),
            new Vector2(629.2f, 380.7f), new Vector2(614.7f, 401.8f), new Vector2(600.2f, 423.0f), new Vector2(585.6f, 444.3f),
            new Vector2(573.1f, 466.7f), new Vector2(561.5f, 488.9f), new Vector2(549.9f, 511.1f), new Vector2(538.3f, 533.3f),
            new Vector2(526.7f, 555.5f), new Vector2(515.1f, 577.6f), new Vector2(503.5f, 599.8f), new Vector2(492.1f, 621.6f),
            new Vector2(477.4f, 641.5f), new Vector2(462.4f, 661.8f), new Vector2(447.5f, 682.1f), new Vector2(429.2f, 701.1f),
            new Vector2(413.6f, 722.0f),
        };
        static readonly Vector2 McCordBluff = new Vector2(401.36f, 717.27f);

        static List<Vector3> ShoreRoute()
        {
            var pts = new List<Vector3>();
            var spawn = GameObject.Find("PlayerSpawn");
            var centre = GameObject.Find("LM_OutdoorCenter");
            if (spawn) pts.Add(spawn.transform.position);
            if (centre) pts.Add(centre.transform.position);
            foreach (var p in Shore) pts.Add(new Vector3(p.x, 0f, p.y));
            pts.Add(new Vector3(McCordBluff.x, 0f, McCordBluff.y));
            return pts;
        }

        /// <summary>Walked (XZ, as Place measures it) distance to the first bank waypoint.</summary>
        static float ShoreLegStart(List<Vector3> route)
        {
            int first = route.Count - Shore.Length - 1;
            float d = 0f;
            for (int i = 1; i <= first; i++)
                d += Vector2.Distance(new Vector2(route[i - 1].x, route[i - 1].z), new Vector2(route[i].x, route[i].z));
            return d;
        }

        static void Place(Transform mover, List<Vector3> route, float distance)
        {
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 a = route[i - 1], b = route[i];
                float seg = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
                if (seg <= 0f) continue;
                if (distance > seg) { distance -= seg; continue; }
                var p = Vector3.Lerp(a, b, distance / seg);
                var tile = TerrainQuery.TileAtOrNearest(p);
                p.y = (tile ? tile.SampleHeight(p) + tile.transform.position.y : p.y) + Eye;
                var dir = b - a;
                dir.y = 0f;
                mover.SetPositionAndRotation(p, Quaternion.LookRotation(dir));
                return;
            }
        }

        static string LegStats(List<double> ms)
        {
            var s = ms.OrderBy(v => v).ToList();
            double P(double q) => s.Count == 0 ? 0 : s[Mathf.Clamp((int)System.Math.Round(q * (s.Count - 1)), 0, s.Count - 1)];
            double share = s.Count == 0 ? 0 : s.Count(v => v <= 1000.0 / 60.0) / (double)s.Count;
            // The closing brace is appended separately: "{share:F4}}}" would end the format string at the wrong brace.
            return $"{{\"samples\":{ms.Count},\"duration_s\":{ms.Sum() / 1000:F1},\"median_ms\":{P(0.5):F2},\"p95_ms\":{P(0.95):F2}," +
                   $"\"p99_ms\":{P(0.99):F2},\"max_ms\":{(s.Count > 0 ? s[s.Count - 1] : 0):F2},\"median_fps\":{1000 / System.Math.Max(0.01, P(0.5)):F1}," +
                   $"\"share_frames_at_or_above_60\":{share:F4}" + "}";
        }

        static void Write(List<double> ms, List<Vector3> route, float metres, List<double> legMs)
        {
            var s = ms.OrderBy(v => v).ToList();
            double P(double q) => s.Count == 0 ? 0 : s[Mathf.Clamp((int)System.Math.Round(q * (s.Count - 1)), 0, s.Count - 1)];
            int trees = Terrain.activeTerrains.Sum(t => t.terrainData.treeInstanceCount);
            var json = "{" +
                $"\"platform\":\"{Application.platform}\",\"development\":{(Debug.isDebugBuild ? "true" : "false")}," +
                $"\"gpu\":\"{SystemInfo.graphicsDeviceName}\",\"graphics_api\":\"{SystemInfo.graphicsDeviceType}\",\"cpu\":\"{SystemInfo.processorType}\"," +
                $"\"quality\":\"{QualitySettings.names[QualitySettings.GetQualityLevel()]}\",\"resolution\":\"{Screen.width}x{Screen.height}\"," +
                $"\"window_mode\":\"{Screen.fullScreenMode}\",\"vsync\":{QualitySettings.vSyncCount},\"speed_mps\":{Speed},\"route_m\":{metres:F0}," +
                $"\"route\":[{string.Join(",", route.Select(p => $"[{p.x:F0},{p.y:F0},{p.z:F0}]"))}],\"samples\":{ms.Count},\"duration_s\":{ms.Sum() / 1000:F1}," +
                $"\"median_ms\":{P(0.5):F2},\"p95_ms\":{P(0.95):F2},\"p99_ms\":{P(0.99):F2},\"max_ms\":{(s.Count > 0 ? s[s.Count - 1] : 0):F2}," +
                $"\"median_fps\":{1000 / System.Math.Max(0.01, P(0.5)):F1},\"p95_fps\":{1000 / System.Math.Max(0.01, P(0.95)):F1}," +
                $"\"share_frames_at_or_above_60\":{(s.Count == 0 ? 0 : s.Count(v => v <= 1000.0 / 60.0) / (double)s.Count):F4}," +
                $"\"wildlife\":{(wildlifeOff ? "false" : "true")},\"birds\":{(birdsOff ? "false" : "true")}," +
                $"\"tiles\":{Terrain.activeTerrains.Length},\"tree_instances\":{trees}" + (shore ? ",\"route_name\":\"shore\",\"shore_leg\":" + LegStats(legMs) + "}" : "}");
            Debug.Log("[BellsBendFps] " + json);
            var dir = Path.GetDirectoryName(Application.consoleLogPath);
            if (string.IsNullOrEmpty(dir)) dir = Application.persistentDataPath;
            var tag = (shore ? "-shore" : "") + (wildlifeOff ? "-nowildlife" : "") + (birdsOff ? "-nobirds" : "");
            File.WriteAllText(Path.Combine(dir, $"bells-bend-fps{tag}.json"), json);
        }
    }
}
