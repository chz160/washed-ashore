using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace WashedAshore.Perf
{
    /// <summary>
    /// Near-plane regression stills of FarViewProbe (technical-director's near-plane proposal for the climb area): what a
    /// smaller near plane does to the picture on existing scenes. For each pose the frozen frame is captured at the
    /// authored near plane twice (the noise floor) and once at each test value, and each test still is compared with the
    /// authored one from the same pose and frame state. Poses: the cab position looking N, E, S, W and 60 degrees down,
    /// and the tower's foot at eye height looking at the horizon. The authored near plane is restored afterwards.
    /// </summary>
    static class FarViewNear
    {
        const float EyeHeight = 1.65f, DownPitch = 60f;

        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public static IEnumerator Stills(Camera cam, Transform mover, Vector3 cab, float ground, IList<float> values, string dir, Action<string> done)
        {
            float authored = cam.nearClipPlane;
            var foot = new Vector3(cab.x, ground + EyeHeight, cab.z);
            var poses = new List<(string name, Vector3 position, Quaternion rotation)>
            {
                ("cab-north", cab, Quaternion.Euler(0f, 0f, 0f)), ("cab-east", cab, Quaternion.Euler(0f, 90f, 0f)),
                ("cab-south", cab, Quaternion.Euler(0f, 180f, 0f)), ("cab-west", cab, Quaternion.Euler(0f, 270f, 0f)),
                ("cab-down60", cab, Quaternion.Euler(DownPitch, 0f, 0f)), ("foot-horizon", foot, Quaternion.Euler(0f, 0f, 0f)),
            };
            var rows = new List<string>();
            foreach (var pose in poses)
            {
                cam.nearClipPlane = authored;
                mover.SetPositionAndRotation(pose.position, Quaternion.Euler(0f, pose.rotation.eulerAngles.y, 0f));
                cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();
                var reference = ScreenCapture.CaptureScreenshotAsTexture();
                yield return new WaitForEndOfFrame();
                var repeat = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, $"near-{pose.name}-authored.png"), reference.EncodeToPNG());
                string noise = FarViewProbe.Changed(reference, repeat, null);
                UnityEngine.Object.Destroy(repeat);

                var tested = new List<string>();
                foreach (float value in values)
                {
                    cam.nearClipPlane = value;
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    var still = ScreenCapture.CaptureScreenshotAsTexture();
                    string tag = $"near-{pose.name}-{F(value)}";
                    File.WriteAllBytes(Path.Combine(dir, tag + ".png"), still.EncodeToPNG());
                    tested.Add($"{{\"near_requested\":{F(value)},\"near_applied\":{F(cam.nearClipPlane)},\"far\":{F(cam.farClipPlane)}," +
                               $"\"vs_authored\":{FarViewProbe.Changed(reference, still, Path.Combine(dir, tag + "-changed-mask.png"))}" + "}");
                    UnityEngine.Object.Destroy(still);
                }
                UnityEngine.Object.Destroy(reference);
                var e = pose.rotation.eulerAngles;
                rows.Add($"{{\"pose\":\"{pose.name}\",\"position\":[{F(pose.position.x)},{F(pose.position.y)},{F(pose.position.z)}],\"euler\":[{F(e.x)},{F(e.y)},{F(e.z)}]," +
                         $"\"repeat_same_frame\":{noise},\"values\":[{string.Join(",", tested)}]" + "}");
            }
            cam.nearClipPlane = authored;
            done($"{{\"authored_near\":{F(authored)},\"near_restored\":{F(cam.nearClipPlane)},\"fog_start\":{F(RenderSettings.fogStartDistance)},\"fog_end\":{F(RenderSettings.fogEndDistance)}," +
                 $"\"reversed_z\":{(SystemInfo.usesReversedZBuffer ? "true" : "false")},\"graphics_api\":\"{SystemInfo.graphicsDeviceType}\"," +
                 "\"depth_buffer_format\":\"not readable from here: the render pipeline asset owns it; the Player.log names the graphics device\"," +
                 $"\"poses\":[{string.Join(",", rows)}]" + "}");
        }
    }
}
