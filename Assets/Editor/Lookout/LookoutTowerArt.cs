using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WashedAshore.Lookout.Editor
{
    /// <summary>
    /// Technical-art half of the lookout tower build: the four materials on the one generated atlas, and the LODGroup.
    /// Called by TowerBuilder.Build (platform-tools): EnsureMaterials(), then ApplyLods(root). Re-runnable; nothing here
    /// is set by hand in the Editor. Material names are the ones in the manifest's objects[].materials.
    /// </summary>
    public static class LookoutTowerArt
    {
        public const string Folder = "Assets/World/Lookout";
        public const string Albedo = Folder + "/MC39_Lookout_Albedo.png";
        public const string Normal = Folder + "/MC39_Lookout_Normal.png";
        public const string Mask = Folder + "/MC39_Lookout_Mask.png";      // R metallic, G occlusion, A smoothness

        public const string Opaque = "MC39_Opaque", Cutout = "MC39_Cutout", Glass = "MC39_Glass", Impostor = "MC39_Impostor";
        public const string FarCardShader = Folder + "/LookoutFarCard.shader";
        const float AlphaClip = 0.5f;
        // far card: first values, to be tuned against the 1 km stills (G4); the haze cap stays below 1
        const float FarCoverage = 1f, FarFadeFrom = 350f, FarFadeFull = 520f, FarHazeFrom = 400f, FarHazeFull = 1600f, FarHazeMax = 0.72f;

        public static IReadOnlyDictionary<string, Material> EnsureMaterials()
        {
            var albedo = Load<Texture2D>(Albedo);
            var normal = Load<Texture2D>(Normal);
            var mask = Load<Texture2D>(Mask);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var farCard = AssetDatabase.LoadAssetAtPath<Shader>(FarCardShader);
            if (!lit) throw new InvalidOperationException("URP Lit shader not found");
            if (!farCard) throw new FileNotFoundException("lookout far-card shader missing: " + FarCardShader);

            var result = new Dictionary<string, Material>
            {
                [Opaque] = Ensure(Opaque, lit, m => { Lit(m, albedo, normal, mask); Surface(m, false, false, false); }),
                // perforated treads and leaf litter: alpha tested, seen from both sides
                [Cutout] = Ensure(Cutout, lit, m => { Lit(m, albedo, normal, mask); Surface(m, false, true, true); }),
                // panes and shards: alpha blended, both sides; smoothness comes from the mask's alpha
                [Glass] = Ensure(Glass, lit, m => { Lit(m, albedo, normal, mask); Surface(m, true, false, true); }),
                // far card: yaw-facing, alpha blended, its own haze toward the fog colour (tower-td-note section 6, item 5)
                [Impostor] = Ensure(Impostor, farCard, m =>
                {
                    m.SetTexture("_BaseMap", albedo);
                    m.SetColor("_Tint", Color.white);
                    m.SetFloat("_Coverage", FarCoverage);
                    m.SetVector("_FadeRange", new Vector4(FarFadeFrom, FarFadeFull, 0f, 0f));
                    m.SetVector("_HazeRange", new Vector4(FarHazeFrom, FarHazeFull, 0f, 0f));
                    m.SetFloat("_HazeMax", FarHazeMax);
                    m.renderQueue = (int)RenderQueue.Transparent + 5;
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }),
            };
            AssetDatabase.SaveAssets();
            return result;
        }

        /// <summary>One LODGroup on root: LOD0 = the LOD0 parts and the movers, then MC39_LOD1, MC39_LOD2, MC39_Impostor.</summary>
        public static void ApplyLods(GameObject root)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Renderer[] Named(Func<string, bool> pick) => renderers.Where(r => pick(r.gameObject.name)).ToArray();

            var lod0 = Named(n => n.StartsWith("MC39_LOD0_", StringComparison.Ordinal) || n == "MC39_Hatch"
                               || n.StartsWith("MC39_Sash_", StringComparison.Ordinal) || n.StartsWith("MC39_Firefinder_", StringComparison.Ordinal));
            var lod1 = Named(n => n == "MC39_LOD1");
            var lod2 = Named(n => n == "MC39_LOD2");
            var card = Named(n => n == "MC39_Impostor");
            if (lod0.Length == 0) throw new InvalidOperationException("no MC39_LOD0_* renderers under " + root.name);
            foreach (var (name, set) in new[] { ("MC39_LOD1", lod1), ("MC39_LOD2", lod2), ("MC39_Impostor", card) })
                if (set.Length != 1) throw new InvalidOperationException($"expected one renderer named {name}, found {set.Length}");

            var heights = ScreenHeights();
            if (heights.Length != 4) throw new InvalidOperationException("manifest lod.screen_heights must have 4 entries");
            var group = root.GetComponent<LODGroup>();
            if (!group) group = root.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.CrossFade;
            group.animateCrossFading = true;
            group.SetLODs(new[]
            {
                new LOD(heights[0], lod0), new LOD(heights[1], lod1), new LOD(heights[2], lod2), new LOD(heights[3], card),
            });
            group.RecalculateBounds();
            foreach (var r in card) { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; }
        }

        /// <summary>lod.screen_heights from the manifest beside the FBX (the build's parameter table sets them).</summary>
        public static float[] ScreenHeights()
        {
            var text = File.ReadAllText(LookoutManifest.AssetPath);
            var m = Regex.Match(text, "\"screen_heights\"\\s*:\\s*\\[([^\\]]*)\\]");
            if (!m.Success) throw new InvalidOperationException("screen_heights not found in " + LookoutManifest.AssetPath);
            return m.Groups[1].Value.Split(',').Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
        }

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new FileNotFoundException("lookout tower asset missing: " + path);
            return asset;
        }

        static Material Ensure(string name, Shader shader, Action<Material> setup)
        {
            var path = $"{Folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader) mat.shader = shader;
            setup(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void Lit(Material m, Texture albedo, Texture normal, Texture mask)
        {
            m.SetTexture("_BaseMap", albedo);
            m.SetTexture("_MainTex", albedo);          // legacy mirror, kept in step so a headless re-save can't reset it
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_Color", Color.white);
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat("_WorkflowMode", 1f);           // metallic
            m.SetTexture("_MetallicGlossMap", mask);   // R metallic, A smoothness
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", 1f);             // scale on the map's alpha
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetTexture("_OcclusionMap", mask);       // G occlusion
            m.SetFloat("_OcclusionStrength", 1f);
            m.EnableKeyword("_OCCLUSIONMAP");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;   // nothing emissive (director note N5)
        }

        static void Surface(Material m, bool transparent, bool alphaClip, bool twoSided)
        {
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);                                  // alpha
            m.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
            m.SetFloat("_Cutoff", AlphaClip);
            m.SetFloat("_Cull", twoSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.SetFloat("_SrcBlend", (float)(transparent ? BlendMode.SrcAlpha : BlendMode.One));
            m.SetFloat("_DstBlend", (float)(transparent ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : alphaClip ? "TransparentCutout" : "Opaque");
            if (transparent) m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); else m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (alphaClip) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = transparent ? (int)RenderQueue.Transparent : alphaClip ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
        }
    }
}
