using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Perf
{
    /// <summary>
    /// W5/W10 probe: in the built player, puts the main camera at w-artist's W5 look poses (BellsBendWaterLook.Shots), captures
    /// each frame as a PNG next to Player.log, logs the mean RGB of the water patch at each target, then quits. Inert unless the
    /// player starts with -waterLookShots. With -bbFpsWalk or -waterFpsWalk too, the shots run after the walk (BellsBendFpsWalk calls Run).
    /// Heights are relative to the ground or to the water quads' Y, so no water level is typed in here.
    /// </summary>
    public class WaterLookShots : MonoBehaviour
    {
        public const string Arg = "-waterLookShots";
        const int Width = 1920, Height = 1080, Settle = 5, Patch = 64;
        const float Fov = 60f;

        enum Ref { Ground, Water }

        // w-artist's frozen W5 poses (2026-10-06; lip and west neck re-aimed from the water). The editor derives the XZ from
        // LevelMaps and the landmarks, which the player doesn't load. Heights are above the ground or the water quads, as frozen.
        static readonly (string name, Vector2 eye, Ref eyeRef, float eyeUp, Vector2 target, float targetUp)[] Poses =
        {
            ("bank", new Vector2(-980.9f, 514.0f), Ref.Ground, 1.7f, new Vector2(-1026.5f, 508.3f), 0f),
            ("bluff-top", new Vector2(413.8f, 1793.2f), Ref.Ground, 1.7f, new Vector2(457.2f, 1768.3f), 0f),
            ("water-level", new Vector2(-1001.7f, 511.4f), Ref.Water, 0.15f, new Vector2(-1001.2f, 571.9f), 0.3f),
            ("shallow-edge", new Vector2(1159.2f, -1416.5f), Ref.Water, 5f, new Vector2(1164.7f, -1418.9f), 0f),
            ("lip", new Vector2(1170.2f, -1421.3f), Ref.Water, 0.6f, new Vector2(1162.9f, -1418.1f), 0.2f),
            ("west-neck-step", new Vector2(-1448.9f, 1970.1f), Ref.Water, 2f, new Vector2(-1419.5f, 1986.0f), 1f),
            ("north-line-east-channel", new Vector2(584.9f, 1986.4f), Ref.Ground, 1.7f, new Vector2(681.3f, 1986.7f), 0f),
        };

        public static bool Requested => System.Environment.GetCommandLineArgs().Contains(Arg);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            // With either walk, BellsBendFpsWalk runs the shots at its end.
            if (!args.Contains(Arg) || args.Contains(BellsBendFpsWalk.Arg) || args.Contains(BellsBendFpsWalk.ShoreArg)) return;
            new GameObject("WaterLookShots").AddComponent<WaterLookShots>();
        }

        IEnumerator Start()
        {
            Application.runInBackground = true;
            Screen.SetResolution(Width, Height, FullScreenMode.Windowed);
            yield return null;
            yield return null;
            yield return Run();
            Application.Quit();
        }

        /// <summary>Captures every pose; safe to call from another probe's coroutine.</summary>
        public static IEnumerator Run()
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player)
            {
                player.enabled = false;
                var cc = player.GetComponent<CharacterController>();
                if (cc) cc.enabled = false;
            }
            var cam = Camera.main;
            var dir = Path.GetDirectoryName(Application.consoleLogPath);
            if (string.IsNullOrEmpty(dir)) dir = Application.persistentDataPath;
            var log = new List<string>();
            var water = GameObject.Find("Water");
            if (!cam || !water || water.transform.childCount == 0)
            {
                log.Add($"FAILED: camera={(cam ? cam.name : "none")} waterRoot={(water ? water.transform.childCount + " tiles" : "none")}");
                Write(dir, log);
                yield break;
            }
            float w = water.transform.GetChild(0).position.y;
            cam.fieldOfView = Fov;
            // Each step is guarded so a failed shot is logged and the caller (the FPS walk) still reaches Application.Quit.
            foreach (var p in Poses)
            {
                Vector3 eye = default, target = default;
                try
                {
                    eye = new Vector3(p.eye.x, 0f, p.eye.y);
                    eye.y = (p.eyeRef == Ref.Water ? w : TerrainQuery.Height(eye)) + p.eyeUp;
                    target = new Vector3(p.target.x, w + p.targetUp, p.target.y);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                }
                catch (System.Exception e) { log.Add($"{p.name}: FAILED placing camera: {e.Message}"); continue; }
                for (int i = 0; i < Settle; i++) yield return null;
                yield return new WaitForEndOfFrame();
                log.Add(Capture(cam, dir, p.name, eye, target));
            }
            Write(dir, log);
        }

        static string Capture(Camera cam, string dir, string name, Vector3 eye, Vector3 target)
        {
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                // The water writes premultiplied alpha into the backbuffer; save opaque so the PNG shows what was on screen.
                var px = tex.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i].a = 255;
                tex.SetPixels32(px);
                tex.Apply(false);
                var file = $"water-look-{name}.png";
                File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
                var sp = cam.WorldToScreenPoint(target);
                var rgb = MeanRgb(tex, (int)sp.x, (int)sp.y);
                return $"{file}: {tex.width}x{tex.height} eye ({eye.x:F1},{eye.y:F2},{eye.z:F1}) -> ({target.x:F1},{target.y:F2},{target.z:F1}) " +
                       $"targetPatch{Patch}px RGB=({rgb.r:F3},{rgb.g:F3},{rgb.b:F3}) blueOverGreen={(rgb.g > 0f ? rgb.b / rgb.g : 0f):F3}";
            }
            catch (System.Exception e) { return $"{name}: FAILED capture: {e.Message}"; }
            finally { if (tex) Destroy(tex); }
        }

        static Color MeanRgb(Texture2D tex, int cx, int cy)
        {
            int x0 = Mathf.Clamp(cx - Patch / 2, 0, tex.width - Patch), y0 = Mathf.Clamp(cy - Patch / 2, 0, tex.height - Patch);
            var px = tex.GetPixels(x0, y0, Patch, Patch);
            float r = 0f, g = 0f, b = 0f;
            foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
            return new Color(r / px.Length, g / px.Length, b / px.Length);
        }

        static void Write(string dir, List<string> lines)
        {
            var text = $"water look shots ({Application.platform}, {SystemInfo.graphicsDeviceType}, {Screen.width}x{Screen.height}, fov {Fov}):\n" +
                       string.Join("\n", lines) + "\ndone\n";
            Debug.Log("[WaterLookShots] " + text);
            File.WriteAllText(Path.Combine(dir, "water-look-shots.txt"), text);
        }
    }
}
