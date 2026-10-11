using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Fish;
using WashedAshore.Fish.Rendering;
using WashedAshore.Gameplay;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.FishRendering
{
    /// <summary>
    /// Slot #17 (team-lead / f-td): do surface signs draw in the real game path? World.unity is loaded in play mode (never
    /// saved), time is frozen, the camera stands at a low-bank shore pose, and FishSurfaceFx is handed a forced Rise and then
    /// a NervousWaterOrFlip patch. Each frame is diffed against the frame before it (its sign-free twin) inside the sign's
    /// projected bounds. Reports f-td's four items to TestResults/fish-sign-ingame.json (plus the frames as PNG), then
    /// asserts more than MinPx changed pixels per sign.
    /// </summary>
    public class FishSignInGameTests
    {
        const int Wd = 1920, H = 1080, MinPx = 20;
        const float Eye = 1.7f, ChangedLuma = 0.0015f;

#pragma warning disable 0649
        [Serializable] class Station { public int i; public float x, z; public bool lowBank; public string excluded; }
        [Serializable] class StationList { public Station[] s; }
#pragma warning restore 0649

        [Serializable] public class SignCheck { public string kind; public Vector3 position; public float quadM; public int changedInBounds, changedOutside, drawnLastFrame; public string boundsPx; }
        [Serializable]
        public class Report
        {
            public string scene = "World (play mode, not saved)", pose;
            public bool setAssigned, ringAssigned, meshAssigned;
            public int waterQueue = -1, fishQueue = -1, ringQueue = -1;
            public int ringLayer = -1, cameraCullingMask; public bool layerInCullingMask;
            public string worldBounds = ""; public uint renderingLayerMask; public string motionVectorMode = "", shadowCastingMode = "";
            public int urpTransparentLayerMask = -1, urpOpaqueLayerMask = -1; public bool layerInUrpTransparentMask;
            public int drawCallsBefore, drawCallsAfter;
            public SignCheck rise = new SignCheck(), patch = new SignCheck();
        }

        RenderTexture rt;
        Camera cam;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            WaterClock.Use(null);
            if (cam) cam.targetTexture = null;
            if (rt) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest]
        public IEnumerator SurfaceSigns_DrawInTheWorldScene()
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            LogAssert.ignoreFailingMessages = true; // a missing set logs an error: report it instead of aborting
            SceneManager.LoadScene("World", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var r = new Report();
            var fx = FishSurfaceFx.Active ? FishSurfaceFx.Active : UnityEngine.Object.FindAnyObjectByType<FishSurfaceFx>(FindObjectsInactive.Include);
            Assert.IsNotNull(fx, "no FishSurfaceFx in World");
            var set = fx.set;
            r.setAssigned = set; r.ringAssigned = set && set.ring; r.meshAssigned = set && set.ringMesh;
            if (!fx.isActiveAndEnabled) // a missing reference turns it off (OnEnable): report the wiring and stop
            {
                File.WriteAllText(Path.Combine(ResultsDir, "fish-sign-ingame.json"), JsonUtility.ToJson(r, true));
                Assert.Fail("FishSurfaceFx is disabled (missing FishRenderSet / ring / mesh): no sign can draw");
            }
            if (set && set.ring) r.ringQueue = set.ring.renderQueue;
            if (set && set.models.Length > 0 && set.models[0].material) r.fishQueue = set.models[0].material.renderQueue;
            var water = UnityEngine.Object.FindObjectsByType<Renderer>().Select(x => x.sharedMaterial)
                .FirstOrDefault(m => m && m.shader && m.shader.name == "WashedAshore/BellsBendWater");
            if (water) r.waterQueue = water.renderQueue;

            var player = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
            if (player) { player.enabled = false; var cc = player.GetComponent<CharacterController>(); if (cc) cc.enabled = false; }
            cam = Camera.main;
            Assert.IsNotNull(cam, "no main camera");
            float W = FishPopulation.Active ? FishPopulation.Active.Water.WaterLevelY : 0f;
            Assert.IsTrue(Pose(W, out var eye, out var dir, out var at), "no low-bank station with water 15 m out");
            r.pose = $"eye ({eye.x:F1},{eye.y:F2},{eye.z:F1}) looking ({dir.x:F2},{dir.z:F2}), rise at ({at.x:F1},{at.z:F1})";
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
            if (FishPopulation.Active) FishPopulation.Active.PlayerOverride = new FishPlayer { position = eye - Vector3.up * Eye, mode = WaterMode.Dry };

            double now = WaterClock.Now;
            WaterClock.Use(() => now);
            Time.timeScale = 0f;
            rt = new RenderTexture(Wd, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            yield return null;
            r.drawCallsBefore = fx.DrawCallsIssued;

            yield return null; // batchmode never evokes WaitForEndOfFrame: a second frame, so the RT holds the frame whose LateUpdate drew
            var twin = Grab("fish-sign-ingame-twin.png");
            var rise = Event(FishSurfaceKind.Rise, at, dir, now, 0.5f);
            fx.InjectForTests(rise);
            yield return null;
            yield return null; // batchmode never evokes WaitForEndOfFrame: a second frame, so the RT holds the frame whose LateUpdate drew
            var withRise = Grab("fish-sign-ingame-rise.png");
            Measure(r.rise, rise, twin, withRise, fx);

            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var patch = Event(FishSurfaceKind.NervousWaterOrFlip, at + dir * 10f + side * 4f, dir, now, 2f);
            fx.InjectForTests(patch);
            yield return null;
            yield return null; // batchmode never evokes WaitForEndOfFrame: a second frame, so the RT holds the frame whose LateUpdate drew
            var withPatch = Grab("fish-sign-ingame-patch.png");
            Measure(r.patch, patch, withRise, withPatch, fx);
            r.drawCallsAfter = fx.DrawCallsIssued;

            var rp = fx.LastParams;
            r.ringLayer = rp.layer; r.cameraCullingMask = cam.cullingMask; r.layerInCullingMask = (cam.cullingMask & (1 << rp.layer)) != 0;
            r.worldBounds = rp.worldBounds.ToString("F2"); r.renderingLayerMask = rp.renderingLayerMask;
            r.motionVectorMode = rp.motionVectorMode.ToString(); r.shadowCastingMode = rp.shadowCastingMode.ToString();
            UrpMasks(r);

            string dir0 = ResultsDir;
            File.WriteAllText(Path.Combine(dir0, "fish-sign-ingame.json"), JsonUtility.ToJson(r, true));
            Assert.IsTrue(r.setAssigned && r.ringAssigned && r.meshAssigned, "FishSurfaceFx has no FishRenderSet / ring / mesh: no sign can draw");
            Assert.Greater(r.drawCallsAfter, r.drawCallsBefore, "FishSurfaceFx issued no draw call");
            Assert.Greater(r.rise.changedInBounds, MinPx, "the forced Rise changed too few pixels inside its bounds");
            Assert.Greater(r.patch.changedInBounds, MinPx, "the forced NervousWater patch changed too few pixels inside its bounds");
        }

        static string ResultsDir
        {
            get { string d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults")); Directory.CreateDirectory(d); return d; }
        }

        static FishSurfaceEvent Event(FishSurfaceKind kind, Vector3 p, Vector3 heading, double now, float size)
        {
            p.y = WaterBody.Active ? WaterBody.Active.SurfaceY(p.x, p.z) : p.y;
            // Mid-life (age 0.35 of a 2 s Rise; 1.4 s of a 6 s patch), spontaneous, no live fish to follow.
            float life = kind == FishSurfaceKind.Rise ? 2f : 6f;
            return new FishSurfaceEvent { kind = kind, position = p, size = size, heading = heading, fishId = -1, species = -1,
                                          startTime = now - 0.35 * life, duration = life, spontaneous = true };
        }

        Color32[] Grab(string file)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(Wd, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Wd, H), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(ResultsDir, file), tex.EncodeToPNG());
            var px = tex.GetPixels32();
            UnityEngine.Object.Destroy(tex);
            return px;
        }

        void Measure(SignCheck c, FishSurfaceEvent e, Color32[] before, Color32[] after, FishSurfaceFx fx)
        {
            c.kind = e.kind.ToString(); c.position = e.position; c.drawnLastFrame = fx.DrawnLastFrame;
            c.quadM = FishSurfaceFx.QuadSizeMetres(e);
            float reach = 0.5f * Mathf.Max(c.quadM, 0.5f) * 1.9f;
            var pts = new[] { new Vector3(-reach, 0, -reach), new Vector3(reach, 0, -reach), new Vector3(-reach, 0, reach), new Vector3(reach, 0, reach) }
                .Select(o => cam.WorldToScreenPoint(e.position + o)).ToArray();
            int x0 = Mathf.Clamp((int)pts.Min(q => q.x), 0, Wd - 1), x1 = Mathf.Clamp((int)pts.Max(q => q.x), 0, Wd - 1);
            int y0 = Mathf.Clamp((int)pts.Min(q => q.y), 0, H - 1), y1 = Mathf.Clamp((int)pts.Max(q => q.y), 0, H - 1);
            c.boundsPx = $"x {x0}-{x1}, y {y0}-{y1} (bottom-left origin)";
            for (int i = 0; i < before.Length; i++)
            {
                if (Mathf.Abs(Luma(after[i]) - Luma(before[i])) < ChangedLuma) continue;
                int x = i % Wd, y = i / Wd;
                if (x >= x0 && x <= x1 && y >= y0 && y <= y1) c.changedInBounds++; else c.changedOutside++;
            }
        }

        static float Luma(Color32 c)
        {
            float Lin(byte b) { float v = b / 255f; return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        // A low-bank, non-excluded bank station with open water 15 m out along some direction: eye 1 m back from it.
        static bool Pose(float W, out Vector3 eye, out Vector3 dir, out Vector3 at)
        {
            string text = File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Data/terrain/build/bank_stations.json")));
            var list = JsonUtility.FromJson<StationList>("{\"s\":" + text + "}").s;
            foreach (var st in list.Where(s => s.lowBank && string.IsNullOrEmpty(s.excluded)).Skip(list.Length / 8))
            {
                var p = new Vector3(st.x, 0f, st.z);
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    bool open = true;
                    for (float m = 5f; m <= 25f && open; m += 2.5f) open = W - TerrainQuery.Height(p + d * m) >= 0.5f;
                    if (!open || TerrainQuery.Height(p - d) <= W) continue;
                    var feet = p - d;
                    eye = new Vector3(feet.x, TerrainQuery.Height(feet) + Eye, feet.z);
                    dir = d; at = new Vector3((p + d * 15f).x, W, (p + d * 15f).z);
                    return true;
                }
            }
            eye = dir = at = Vector3.zero;
            return false;
        }

        // The active URP renderer's opaque/transparent layer filters (private fields; read for the report only).
        static void UrpMasks(Report r)
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)) return;
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(asset) as ScriptableRendererData[];
            var index = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(asset);
            if (list == null || !(index is int i) || i < 0 || i >= list.Length || !(list[i] is UniversalRendererData data)) return;
            r.urpOpaqueLayerMask = data.opaqueLayerMask;
            r.urpTransparentLayerMask = data.transparentLayerMask;
            r.layerInUrpTransparentMask = r.ringLayer >= 0 && (data.transparentLayerMask & (1 << r.ringLayer)) != 0;
        }
    }
}
