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
    /// </summary>
    public class BellsBendFpsWalk : MonoBehaviour
    {
        const string Arg = "-bbFpsWalk";
        // Optional A8/B8-style arms: deactivate the scene roots before the warm-up.
        const string WildlifeOffArg = "-bbWildlifeOff", BirdsOffArg = "-bbBirdsOff";
        static bool wildlifeOff, birdsOff;
        const float Speed = 5f, Eye = 1.7f, Warmup = 4f, RidgeSearch = 1500f, Grid = 10f;
        const int Width = 1920, Height = 1080;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (!args.Contains(Arg)) return;
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

            var route = Route();
            float total = 0f;
            for (int i = 1; i < route.Count; i++) total += Vector3.Distance(route[i - 1], route[i]);
            var ms = new List<double>();
            float t = -Warmup;
            while (true)
            {
                float d = Mathf.Max(0f, t) * Speed;
                if (d >= total) break;
                Place(mover, route, d);
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (t >= 0f) ms.Add(dt * 1000.0);
                t += dt;
            }
            Write(ms, route, total);
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

        static void Write(List<double> ms, List<Vector3> route, float metres)
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
                $"\"tiles\":{Terrain.activeTerrains.Length},\"tree_instances\":{trees}}}";
            Debug.Log("[BellsBendFps] " + json);
            var dir = Path.GetDirectoryName(Application.consoleLogPath);
            if (string.IsNullOrEmpty(dir)) dir = Application.persistentDataPath;
            var tag = (wildlifeOff ? "-nowildlife" : "") + (birdsOff ? "-nobirds" : "");
            File.WriteAllText(Path.Combine(dir, $"bells-bend-fps{tag}.json"), json);
        }
    }
}
