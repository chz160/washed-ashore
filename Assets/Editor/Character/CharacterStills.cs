using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WashedAshore.Character.Appearance;

namespace WashedAshore.Character.Editor
{
    /// <summary>
    /// Stills of the assembled characters, rendered offscreen in a temporary additive scene (nothing is saved to a scene).
    /// Used for the H2 screenshots and the director's look-down / debug-camera stills of the default look.
    /// Menu: Washed Ashore > Art > Character > Capture Stills.  Batch: CharacterStills.CaptureAll("_bmad-output/poc/character-stills").
    /// Look-down camera: FOV 70, near 0.1, eye 1.65, body 0.10 m behind the pivot and 0.05 m down (engineer-2 research section 2, designer-2 section 4).
    /// </summary>
    public static class CharacterStills
    {
        const int W = 640, H = 800;

        [MenuItem("Washed Ashore/Art/Character/Capture Stills")]
        static void Menu() => Debug.Log(CaptureAll("_bmad-output/poc/character-stills"));

        public static void BatchCapture()
        {
            try { Debug.Log(CaptureAll("_bmad-output/poc/character-stills")); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
        }

        /// <summary>Debug-camera stills of the Jog and Sprint clips at four phases, side and front, of the default look (arm-into-body check).</summary>
        public static void BatchCaptureGait()
        {
            try { Debug.Log(CaptureGait("_bmad-output/poc/character-stills/gait")); EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
        }

        public static string CaptureGait(string outDir)
        {
            Directory.CreateDirectory(outDir);
            bool batch = Application.isBatchMode;
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var written = new System.Collections.Generic.List<string>();
            try
            {
                var stage = BuildStage();
                var appearance = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(CharacterAssetBuilder.DefaultAppearancePath);
                var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(appearance.bodyType));
                // warm-up render (the first frame of a new scene draws unlit), then a fresh body per phase: a re-sampled skinned mesh is not re-skinned in edit mode
                var warm = CharacterAssembler.Assemble(appearance);
                Shot(stage.cam, new Vector3(3.2f, 1.1f, 0f), new Vector3(0f, 0.95f, 0f), 30f, Path.Combine(outDir, "_warmup.png"));
                Object.DestroyImmediate(warm.gameObject);
                File.Delete(Path.Combine(outDir, "_warmup.png"));
                foreach (var clipName in new[] { "Sprint", "Jog" })
                {
                    var clip = set.Get(clipName);
                    for (int phase = 0; phase < 4; phase++)
                    {
                        var body = CharacterAssembler.Assemble(appearance);
                        clip.SampleAnimation(body.Animator.gameObject, clip.length * phase / 4f);
                        written.Add(Shot(stage.cam, new Vector3(3.2f, 1.1f, 0f), new Vector3(0f, 0.95f, 0f), 30f, Path.Combine(outDir, $"{clipName}_p{phase}_side.png")));
                        written.Add(Shot(stage.cam, new Vector3(0f, 1.1f, 3.2f), new Vector3(0f, 0.95f, 0f), 30f, Path.Combine(outDir, $"{clipName}_p{phase}_front.png")));
                        Object.DestroyImmediate(body.gameObject);
                    }
                }
                Object.DestroyImmediate(stage.root);
            }
            finally
            {
                if (!batch)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            AssetDatabase.Refresh();
            return "gait stills:" + System.Environment.NewLine + string.Join(System.Environment.NewLine, written);
        }

        public static string CaptureAll(string outDir)
        {
            Directory.CreateDirectory(outDir);
            // Batch mode has no scene of anyone's to keep (an additive scene needs a saved one); an open Editor gets an additive scene.
            bool batch = Application.isBatchMode;
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, batch ? NewSceneMode.Single : NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var written = new System.Collections.Generic.List<string>();
            try
            {
                var stage = BuildStage();
                foreach (BodyType type in System.Enum.GetValues(typeof(BodyType)))
                    foreach (var (folder, _) in CharacterAssetBuilder.Outfits[type])
                    {
                        var appearance = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(CharacterAssetBuilder.AppearancePath(type, folder));
                        var body = Pose(appearance, "Idle");
                        string name = appearance.name;
                        written.Add(Shot(stage.cam, new Vector3(1.6f, 1.25f, 2.6f), new Vector3(0f, 0.95f, 0f), 35f, Path.Combine(outDir, $"assembly-{name}.png")));
                        Object.DestroyImmediate(body.gameObject);
                    }
                var def = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(CharacterAssetBuilder.DefaultAppearancePath);
                var d = Pose(def, "Idle");
                // debug camera: behind-left of the body, 2.5 m away, head visible
                written.Add(Shot(stage.cam, new Vector3(-1.4f, 1.5f, -2.6f), new Vector3(0f, 1.0f, 0f), 40f, Path.Combine(outDir, "default-debug-camera.png")));
                written.Add(Shot(stage.cam, new Vector3(1.4f, 1.3f, 2.4f), new Vector3(0f, 0.95f, 0f), 40f, Path.Combine(outDir, "default-debug-camera-front.png")));
                // first-person look-down: camera at the pivot, body pushed back, head shadow-only
                d.transform.position = new Vector3(0f, -0.05f, -0.10f);
                foreach (var r in d.Renderers(CharacterSlot.Head)) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                foreach (var pitch in new[] { 30f, 55f, 85f })
                    written.Add(ShotFirstPerson(stage.cam, pitch, Path.Combine(outDir, $"default-look-down-{pitch:0}.png")));
                Object.DestroyImmediate(d.gameObject);
                Object.DestroyImmediate(stage.root);
            }
            finally
            {
                if (!batch)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            AssetDatabase.Refresh();
            return "stills:\n" + string.Join("\n", written);
        }

        static CharacterBody Pose(CharacterAppearance appearance, string clip)
        {
            var body = CharacterAssembler.Assemble(appearance);
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(appearance.bodyType));
            set.Get(clip).SampleAnimation(body.Animator.gameObject, 0f);
            return body;
        }

        static (GameObject root, Camera cam) BuildStage()
        {
            var root = new GameObject("StillsStage");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(2f, 1f, 2f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.SetParent(root.transform);
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            var cam = new GameObject("StillsCamera").AddComponent<Camera>();
            cam.transform.SetParent(root.transform);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.65f, 0.75f);
            cam.nearClipPlane = 0.1f;
            cam.enabled = false;
            return (root, cam);
        }

        static string Shot(Camera cam, Vector3 position, Vector3 target, float fov, string path)
        {
            cam.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
            cam.fieldOfView = fov;
            return Render(cam, path);
        }

        static string ShotFirstPerson(Camera cam, float pitchDown, string path)
        {
            cam.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, 0f), Quaternion.Euler(pitchDown, 0f, 0f));
            cam.fieldOfView = 70f;
            return Render(cam, path);
        }

        static string Render(Camera cam, string path)
        {
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(tex);
            }
            return path;
        }
    }
}
