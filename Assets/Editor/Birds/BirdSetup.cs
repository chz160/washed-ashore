using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.AssetImporters;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Wildlife.Editor;

namespace WashedAshore.Birds.Editor
{
    /// <summary>
    /// Technical-art setup for the birds (spec B2/B9): URP Lit materials (Robin, Pigeon and the dark Crow
    /// variant of Pigeon; the source textures are never edited), one Animator controller per bird with one
    /// state per clip, and one prefab variant per bird. Prefabs are not placed in any scene.
    /// Re-runnable: rebuilds controllers in place (GUIDs stay) and overwrites its own materials and prefabs.
    /// Run via: unity command eval "return WashedAshore.Birds.Editor.BirdSetup.Build();"
    /// Check via: unity command eval "return WashedAshore.Birds.Editor.BirdSetup.Verify();"
    /// </summary>
    public static class BirdSetup
    {
        public const string Root = "Assets/World/Birds";
        public const string RobinPrefab = Root + "/Prefabs/Bird_Robin.prefab";
        public const string CrowPrefab = Root + "/Prefabs/Bird_Crow.prefab";
        public const string RobinController = Root + "/Animators/Bird_Robin.controller";
        public const string PigeonController = Root + "/Animators/Bird_Pigeon.controller";
        const string RobinMat = Root + "/Materials/Robin.mat";
        const string PigeonMat = Root + "/Materials/Pigeon.mat";
        const string CrowMat = Root + "/Materials/Crow.mat";
        const string RobinTex = "Assets/ThirdParty/SoltorchGames/AmericanRobin/American_Robin_Tex.png";
        const string PigeonTex = "Assets/ThirdParty/PeripheralArbor/Pigeon/Pigeon_Texture.jpg";

        // Multiplies the grey pigeon texture down to crow/raven black with a faint blue cast; a little
        // smoothness gives the feather sheen. art-director owns the final value.
        public static readonly Color CrowTint = new Color(0.16f, 0.16f, 0.19f, 1f);
        const float CrowSmoothness = 0.45f;

        public const string AnimSpeedParam = "AnimSpeed";
        const string RobinDefault = "A_Idle_01";
        const string PigeonDefault = "Gliding";

        [MenuItem("Washed Ashore/Birds/Build Bird Assets")]
        static void BuildMenu() => Debug.Log(Build());

        [MenuItem("Washed Ashore/Birds/Verify Bird Imports")]
        static void VerifyMenu() => Debug.Log(Verify());

        public static string Build()
        {
            foreach (var d in new[] { "Prefabs", "Animators", "Materials" }) Directory.CreateDirectory($"{Root}/{d}");
            AssetDatabase.Refresh();
            ConfigureTexture(RobinTex, 256);
            ConfigureTexture(PigeonTex, 512);

            var robinMat = LitMaterial(RobinMat, RobinTex, 0.1f);
            var pigeonMat = LitMaterial(PigeonMat, PigeonTex, 0.25f);
            var crowMat = CrowVariant(pigeonMat);

            var report = new StringBuilder();
            var robinCtrl = BuildController(RobinController, BirdImportSettings.RobinFbx, RobinDefault);
            var pigeonCtrl = BuildController(PigeonController, BirdImportSettings.PigeonFbx, PigeonDefault);
            report.AppendLine(BuildPrefab(BirdImportSettings.RobinFbx, RobinPrefab, "Bird_Robin", robinCtrl, robinMat));
            report.AppendLine(BuildPrefab(BirdImportSettings.PigeonFbx, CrowPrefab, "Bird_Crow", pigeonCtrl, crowMat));
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        static void ConfigureTexture(string path, int maxSize)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti.maxTextureSize == maxSize && ti.sRGBTexture && ti.mipmapEnabled) return;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = maxSize;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SaveAndReimport();
        }

        static Material LitMaterial(string path, string texPath, float smoothness)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        // A Material Variant: inherits everything from Pigeon.mat, overrides only colour and smoothness.
        static Material CrowVariant(Material parent)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(CrowMat);
            if (!m)
            {
                m = new Material(parent);
                AssetDatabase.CreateAsset(m, CrowMat);
            }
            m.parent = parent;
            m.SetColor("_BaseColor", CrowTint);
            m.SetFloat("_Smoothness", CrowSmoothness);
            EditorUtility.SetDirty(m);
            return m;
        }

        public static List<AnimationClip> Clips(string fbx) => AssetDatabase.LoadAllAssetsAtPath(fbx)
            .OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).OrderBy(c => c.name).ToList();

        // One state per clip, named after the clip; gameplay code drives them with CrossFade. "AnimSpeed"
        // scales every state so instances can be desynchronised.
        static AnimatorController BuildController(string path, string fbx, string defaultState)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!c) c = AnimatorController.CreateAnimatorControllerAtPath(path);
            else
            {
                foreach (var p in c.parameters) c.RemoveParameter(p);
                var old = c.layers[0].stateMachine;
                foreach (var st in old.states) old.RemoveState(st.state);
            }
            c.AddParameter(new AnimatorControllerParameter
                { name = AnimSpeedParam, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            var sm = c.layers[0].stateMachine;
            var clips = Clips(fbx);
            if (clips.Count == 0) throw new FileNotFoundException($"{fbx} has no clips");
            for (int i = 0; i < clips.Count; i++)
            {
                var st = sm.AddState(clips[i].name, new Vector3(300f + 220f * (i % 4), 60f * (i / 4), 0f));
                st.motion = clips[i];
                st.speedParameterActive = true;
                st.speedParameter = AnimSpeedParam;
                if (clips[i].name == defaultState) sm.defaultState = st;
            }
            EditorUtility.SetDirty(c);
            return c;
        }

        static string BuildPrefab(string fbx, string path, string name, AnimatorController ctrl, Material mat)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
                go.name = name;
                var animator = go.GetComponent<Animator>();
                if (!animator) animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = ctrl;
                animator.applyRootMotion = false;
                // Off-screen birds (most of a flock, most of the time) skip pose evaluation.
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    smr.sharedMaterials = Enumerable.Repeat(mat, smr.sharedMaterials.Length).ToArray();
                    smr.updateWhenOffscreen = false;
                }
                PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
                var smr0 = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                return $"{name}: {(ok ? "ok" : "FAILED")} {path} controller={AssetDatabase.GetAssetPath(ctrl)} " +
                       $"material={AssetDatabase.GetAssetPath(mat)} bones={smr0.bones.Length} size={smr0.bounds.size.ToString("F3")}";
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        /// <summary>
        /// Evidence for B2: rig type, bone count, clip names, import-log errors/warnings, the facing decision,
        /// and the head direction at mid-clip for every clip (head minus the bone centroid, model space; +Z
        /// means the head leads).
        /// </summary>
        public static string Verify()
        {
            var sb = new StringBuilder();
            foreach (var fbx in new[] { BirdImportSettings.RobinFbx, BirdImportSettings.PigeonFbx })
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                var smr = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var logs = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<ImportLog>().SelectMany(l => l.logEntries).ToList();
                int errors = logs.Count(e => (e.flags & ImportLogFlags.Error) != 0);
                int warnings = logs.Count(e => (e.flags & ImportLogFlags.Warning) != 0);
                var clips = Clips(fbx);
                sb.AppendLine($"{fbx}: rig={mi.animationType} bones={smr.bones.Length} clips={clips.Count} " +
                              $"importErrors={errors} importWarnings={warnings}");
                foreach (var e in logs) sb.AppendLine($"  log {e.flags}: {e.message}");
                sb.AppendLine("  clipNames: " + string.Join(", ", clips.Select(c => c.name)));

                var go = Object.Instantiate(model);
                try
                {
                    var head = ModelFacingPostprocessor.FindHead(go.transform);
                    var bones = go.GetComponentInChildren<SkinnedMeshRenderer>(true).bones.Where(b => b).ToList();
                    float minZ = float.MaxValue;
                    foreach (var clip in clips)
                    {
                        clip.SampleAnimation(go, clip.length * 0.5f);
                        var centroid = bones.Aggregate(Vector3.zero, (a, b) => a + go.transform.InverseTransformPoint(b.position)) / bones.Count;
                        var d = go.transform.InverseTransformPoint(head.position) - centroid;
                        var flat = new Vector2(d.x, d.z).normalized;
                        minZ = Mathf.Min(minZ, flat.y);
                        sb.AppendLine($"  midclip {clip.name}: head '{head.name}' dir xz=({flat.x:F2}, {flat.y:F2})");
                    }
                    sb.AppendLine($"  headAtPlusZ={(minZ > 0.5f ? "PASS" : "FAIL")} (min z {minZ:F2})");
                }
                finally { Object.DestroyImmediate(go); }
            }
            return sb.ToString();
        }
    }
}
