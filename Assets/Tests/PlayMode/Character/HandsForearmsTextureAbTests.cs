#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// qa-49, pod 49, td-49 F10: hands and forearms A/B of the Survivor's baseColor at 4096 vs 2048 (protocol: pod49 art-import-standard.md section 8).
    /// Explicit; runs in the Editor (no player launch) only when named. Twins (4096, 2048, a second 4096 instance, a blurred 4096) are rendered in the SAME
    /// frame through the live view camera with no time step. Inputs (environment): AB_TEX4K and AB_TEX2K (asset paths, two imports of one PNG),
    /// AB_OUT (an EMPTY folder for stills and results), AB_KEY_DIR (where the side key is written; not the runner's folder), optional AB_SEED, AB_APPEARANCE.
    /// Writes nothing into Assets/. Pairs go to art-49 blind (neutral names under AB_OUT/blind); the key is withheld in AB_KEY_DIR and its SHA-256 is in the results.
    /// </summary>
    public partial class HandsForearmsTextureAbTests : StanceFixture
    {
        const float MinDelta = 4f;              // art-49's choice: |delta| >= 4/255 on any channel
        const float MeasuredPct = 1f;           // art-49's choice: >= 1% of ROI pixels
        const int MeasuredStills = 2;           // art-49's choice: >= 2 of 8 stills
        const int MinRoiPixels = 200;           // qa-49's choice: a smaller hands/forearms area is not counted and is reported
        const int MaxTinyRoi = 4;               // qa-49's choice: more than 4 of 8 stills with a tiny ROI voids the run
        const int MaskLayer = 31;               // drawn only into the mask camera, never by the player camera

        struct PoseDef { public string name; public float pitch; public bool crouch, walk; }
        static readonly PoseDef[] Poses =
        {
            new PoseDef { name = "Stand45", pitch = 45f },
            new PoseDef { name = "Stand70", pitch = 70f },
            new PoseDef { name = "Walk30", pitch = 30f, walk = true },   // plain W, frozen where the right wrist is furthest forward
            new PoseDef { name = "Crouch60", pitch = 60f, crouch = true },
        };

        class Still
        {
            public string pose, yaw; public int w, h;
            public int roi, x0, y0, x1, y1; public float abPct, aaPct, net, maxD, idPct, blurNet, shipNet; public bool control;
            public Color32[] pa, pb;
        }

        PlayerCharacterView view;
        FirstPersonBody fp;
        readonly List<Renderer> rends = new List<Renderer>();
        readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
        string outDir, keyDir, seedText;
        int walkHash; float walkT = -1f;
        readonly List<Still> stills = new List<Still>();
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> voids = new List<string>();

        /// <summary>True when child is the same folder as, or below, parent (full paths, case-insensitive).</summary>
        static bool IsInside(string child, string parent)
        {
            string c = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
        }

        // NUnit does not fill optional parameters, so the path of this file comes from a helper (the compiler fills [CallerFilePath] at the call site, in this file).
        static string SelfPath([CallerFilePath] string p = "") => p;

        static string Env(string n) => Environment.GetEnvironmentVariable(n);

        static string GitBlobHash(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var head = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
            var all = new byte[head.Length + bytes.Length];
            Buffer.BlockCopy(head, 0, all, 0, head.Length);
            Buffer.BlockCopy(bytes, 0, all, head.Length, bytes.Length);
            using (var sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(all)).Replace("-", "").ToLowerInvariant();
        }

        [UnityTest, Explicit("pod 49 F10 A/B: needs AB_TEX4K, AB_TEX2K, AB_OUT (empty), AB_KEY_DIR; run by engineer-49 unmodified, by hash"), Timeout(3600000)]
        public IEnumerator HandsForearms_4096_vs_2048()
        {
            string p4 = Env("AB_TEX4K"), p2 = Env("AB_TEX2K");
            outDir = Env("AB_OUT"); keyDir = Env("AB_KEY_DIR");
            Assert.IsFalse(string.IsNullOrEmpty(p4) || string.IsNullOrEmpty(p2) || string.IsNullOrEmpty(outDir) || string.IsNullOrEmpty(keyDir), "set AB_TEX4K, AB_TEX2K, AB_OUT and AB_KEY_DIR");
            string project = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));
            Assert.IsFalse(IsInside(outDir, project) || IsInside(keyDir, project), "AB_OUT and AB_KEY_DIR must be outside the project folder " + project);
            Assert.IsFalse(IsInside(keyDir, outDir) || IsInside(outDir, keyDir), "AB_KEY_DIR and AB_OUT must not contain each other");
            Directory.CreateDirectory(outDir);
            Assert.IsEmpty(Directory.GetFileSystemEntries(outDir), "AB_OUT must be empty: " + outDir);
            Directory.CreateDirectory(keyDir);
            Assert.IsFalse(File.Exists(Path.Combine(keyDir, "ab4k-key.txt")), "a key file already exists in AB_KEY_DIR");
            Directory.CreateDirectory(Path.Combine(outDir, "blind"));
            string self = SelfPath();
            int seed = int.TryParse(Env("AB_SEED"), out int sd) ? sd : new System.Random().Next();
            seedText = seed.ToString();
            var rng = new System.Random(seed);

            var t4 = AssetDatabase.LoadAssetAtPath<Texture2D>(p4);
            var t2 = AssetDatabase.LoadAssetAtPath<Texture2D>(p2);
            Assert.IsNotNull(t4, "no texture at " + p4); Assert.IsNotNull(t2, "no texture at " + p2);
            DumpImport(p4, t4, 4096); DumpImport(p2, t2, 2048);

            PinTicks();
            yield return LoadWorld();
            yield return OnFloor();
            view = player.GetComponent<PlayerCharacterView>();
            string ap = Env("AB_APPEARANCE") ?? "Assets/Art/Character/Appearances/Men_Survivor.asset";
            var app = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(ap);
            Assert.IsNotNull(app, "no appearance at " + ap);
            if (view.Body.Appearance != app) { view.RebuildWith(app); yield return null; yield return null; yield return Settle(); }
            fp = view.FirstPerson;
            Assert.IsTrue(fp != null && fp.HasViewBody, "no view body");
            LogSettings();
            Assert.IsNotNull(view.Body.Bone("Wrist.R"), "no Wrist.R bone on the body");

            foreach (var r in fp.ViewRenderers) if (r != null && r.enabled) { rends.Add(r); originals[r] = r.sharedMaterials; }
            Assert.IsNotEmpty(rends, "no view renderers");
            var sun = RenderSettings.sun != null ? RenderSettings.sun : GameObject.Find("Sun")?.GetComponent<Light>();
            Assert.IsNotNull(sun, "no sun");
            Vector3 sf = Vector3.ProjectOnPlane(sun.transform.forward, Vector3.up);
            Assert.Greater(sf.magnitude, 0.1f, "sun is nearly vertical: no 'facing the sun' yaw");
            float yawSun = Mathf.Atan2(-sf.x, -sf.z) * Mathf.Rad2Deg;
            log.AppendLine($"script {Path.GetFileName(self)} git-blob {GitBlobHash(self)}; {Path.GetFileName(CapturePath())} git-blob {GitBlobHash(CapturePath())}; sun rotation live {sun.transform.rotation.x:F8},{sun.transform.rotation.y:F8},{sun.transform.rotation.z:F8},{sun.transform.rotation.w:F8} (World.unity line 50039: 0.38791165,-0.27470842,0.12230807,0.8712638); sun yaw-to-face {yawSun:F1}");
            log.AppendLine($"textures: 4096 asset {p4} {t4.width}x{t4.height}, memory {Profiler.GetRuntimeMemorySizeLong(t4)}; 2048 asset {p2} {t2.width}x{t2.height}, memory {Profiler.GetRuntimeMemorySizeLong(t2)}");

            RenderTexture small = RenderTexture.GetTemporary(512, 512, 0);
            Graphics.Blit(t4, small);
            var blur = new RenderTexture(t4.width, t4.height, 0) { useMipMap = true, autoGenerateMips = true };
            Graphics.Blit(small, blur);
            RenderTexture.ReleaseTemporary(small);

            var variants = new Dictionary<string, Dictionary<Renderer, Material[]>>
            {
                ["4k"] = Make(t4), ["2k"] = Make(t2), ["4kB"] = Make(t4), ["blur"] = Make(blur),
                ["shipped"] = new Dictionary<Renderer, Material[]>(originals),   // art-49 control: the shipped Survivor.mat with the real texture
            };
            var order = new List<int>(); for (int i = 0; i < 8; i++) order.Add(i);
            try
            {
                // Forced-failure controls first (qa condition 7): identical pair must read 0; a blurred 4096 must trip Measured. Either misbehaving voids the run.
                yield return Shoot(Poses[0], "sun", yawSun, variants, true);
                // Real pairs: 4 poses x 2 yaws, 1920x1080 decides; 2560x1440 is a labelled variant.
                foreach (var pose in Poses)
                {
                    if (pose.crouch) { yield return Go(StanceKey.Crouch, 4f); Assert.AreEqual(WashedAshore.Gameplay.Stance.Crouch, player.Stance, "could not crouch"); }
                    foreach (var yn in new[] { "sun", "back" })
                    {
                        float yaw = yn == "sun" ? yawSun : yawSun + 180f;
                        yield return Shoot(pose, yn, yaw, variants, false);
                    }
                }
            }
            finally { Restore(); }
            WriteResults(rng, order, t4, t2);
            Assert.IsEmpty(voids, "RUN VOID: " + string.Join("; ", voids));
        }

        void LogSettings()
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            string msaa = rp == null ? "n/a" : (rp.GetType().GetProperty("msaaSampleCount")?.GetValue(rp)?.ToString() ?? "unreadable");
            int lim = QualitySettings.globalTextureMipmapLimit;
            log.AppendLine($"settings: quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}', global mipmap limit {lim}, aniso {QualitySettings.anisotropicFiltering}, streaming mipmaps {QualitySettings.streamingMipmapsActive}, QualitySettings.antiAliasing {QualitySettings.antiAliasing}, pipeline {(rp == null ? "built-in" : rp.name)} msaa {msaa}, GPU {SystemInfo.graphicsDeviceName}, screen {Screen.width}x{Screen.height}");
            if (lim != 0) voids.Add($"global mipmap limit is {lim}, not 0 (the 4096 map is already reduced)");
        }

        void DumpImport(string path, Texture2D tex, int expectMax)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(imp, path + " has no TextureImporter");
            var s = imp.GetDefaultPlatformTextureSettings();
            log.AppendLine($"import {path}: maxSize {imp.maxTextureSize}, format {s.format}, compression {imp.textureCompression}, mips {imp.mipmapEnabled}, aniso {imp.anisoLevel}, filter {imp.filterMode}, sRGB {imp.sRGBTexture}, wrap {imp.wrapMode}, readable {imp.isReadable}; loaded {tex.width}x{tex.height} format {tex.format} mipCount {tex.mipmapCount}");
            if (imp.maxTextureSize != expectMax || tex.width != expectMax) voids.Add($"{path}: maxTextureSize {imp.maxTextureSize}/loaded width {tex.width}, expected {expectMax}");
            var prev = importKey; importKey = $"{imp.textureCompression}|{imp.mipmapEnabled}|{imp.anisoLevel}|{imp.filterMode}|{imp.sRGBTexture}|{imp.wrapMode}|{s.format}";
            if (prev != null && prev != importKey) voids.Add("the two textures differ in an import setting other than size");
        }
        string importKey;

        Dictionary<Renderer, Material[]> Make(Texture tex)
        {
            var d = new Dictionary<Renderer, Material[]>();
            foreach (var r in rends)
            {
                var src = originals[r]; var m = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    m[i] = new Material(src[i]);
                    if (m[i].HasProperty("_BaseMap")) m[i].SetTexture("_BaseMap", tex);
                    if (m[i].HasProperty("_MainTex")) m[i].SetTexture("_MainTex", tex);
                }
                d[r] = m;
            }
            return d;
        }

        void Use(Dictionary<Renderer, Material[]> v) { foreach (var kv in v) kv.Key.sharedMaterials = kv.Value; }
        void Restore() { foreach (var kv in originals) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value; }
    }
}
#endif
