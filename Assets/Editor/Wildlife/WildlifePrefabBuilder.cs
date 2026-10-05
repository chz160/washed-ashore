using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using WashedAshore.Wildlife;

namespace WashedAshore.Wildlife.Editor
{
    /// <summary>
    /// Gameplay-engineer tool: builds the wildlife tuning asset, one Animator controller per species
    /// (Idle/Walk/Gallop blend on "Speed" plus an Eating graze state) and one prefab variant per
    /// species (NavMeshAgent + Animator + WildlifeAgent) from the Quaternius prefabs.
    /// Re-runnable; overwrites its own controllers and variants, never the tuning values.
    /// Run via: unity command eval "return WashedAshore.Wildlife.Editor.WildlifePrefabBuilder.Build();"
    /// </summary>
    public static class WildlifePrefabBuilder
    {
        public const string Root = "Assets/World/Wildlife";
        public const string TuningPath = Root + "/WildlifeTuning.asset";
        const string VendorRoot = "Assets/ThirdParty/Quaternius/Animals";

        public static readonly WildlifeSpecies[] Species =
            { WildlifeSpecies.Deer, WildlifeSpecies.Stag, WildlifeSpecies.Fox, WildlifeSpecies.Wolf };

        public static string PrefabPath(WildlifeSpecies s) => $"{Root}/Prefabs/Wildlife_{s}.prefab";
        static string ControllerPath(WildlifeSpecies s) => $"{Root}/Animators/Wildlife_{s}.controller";

        [MenuItem("Washed Ashore/Wildlife/Build Prefabs")]
        static void BuildMenu() => Debug.Log(Build());

        [MenuItem("Washed Ashore/Wildlife/Reset Tuning To Brief")]
        static void ResetMenu()
        {
            var t = LoadOrCreateTuning();
            WildlifeBriefDefaults.Apply(t);
            EditorUtility.SetDirty(t);
            AssetDatabase.SaveAssets();
            Debug.Log("WildlifeTuning reset to the density brief values.");
        }

        public static string Build()
        {
            Directory.CreateDirectory(Root + "/Prefabs");
            Directory.CreateDirectory(Root + "/Animators");
            AssetDatabase.Refresh();

            var tuning = LoadOrCreateTuning();
            var report = new StringBuilder($"tuning={TuningPath} seed={tuning.defaultSeed}\n");
            foreach (var s in Species)
            {
                var controller = BuildController(s, tuning);
                report.AppendLine(BuildPrefab(s, tuning, controller));
            }
            AssetDatabase.SaveAssets();
            return report.ToString();
        }

        static WildlifeTuning LoadOrCreateTuning()
        {
            var t = AssetDatabase.LoadAssetAtPath<WildlifeTuning>(TuningPath);
            if (t) return t;
            Directory.CreateDirectory(Root);
            t = ScriptableObject.CreateInstance<WildlifeTuning>();
            WildlifeBriefDefaults.Apply(t);
            AssetDatabase.CreateAsset(t, TuningPath);
            return t;
        }

        static AnimationClip Clip(WildlifeSpecies s, string name)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{VendorRoot}/FBX/{s}.fbx")
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (!clip) throw new FileNotFoundException($"{s}.fbx has no clip '{name}'");
            return clip;
        }

        static AnimatorController BuildController(WildlifeSpecies s, WildlifeTuning tuning)
        {
            // Rebuild in place: deleting and recreating changes the GUID and leaves loaded prefabs and
            // scene instances holding a dead reference (null controller) until a reimport.
            string path = ControllerPath(s);
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (!c) c = AnimatorController.CreateAnimatorControllerAtPath(path);
            else
            {
                foreach (var p in c.parameters) c.RemoveParameter(p);
                var old = c.layers[0].stateMachine;
                foreach (var st in old.states) old.RemoveState(st.state);
                foreach (var bt in AssetDatabase.LoadAllAssetsAtPath(path).OfType<BlendTree>())
                    Object.DestroyImmediate(bt, true);
            }
            c.AddParameter("Speed", AnimatorControllerParameterType.Float);
            c.AddParameter("Grazing", AnimatorControllerParameterType.Bool);
            c.AddParameter(new AnimatorControllerParameter
                { name = "AnimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

            var loco = c.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip(s, "Idle"), 0f);
            tree.AddChild(Clip(s, "Walk"), tuning.walkClipSpeed);
            tree.AddChild(Clip(s, "Gallop"), tuning.gallopClipSpeed);
            loco.speedParameterActive = true;
            loco.speedParameter = "AnimSpeed";

            var sm = c.layers[0].stateMachine;
            var graze = sm.AddState("Graze");
            graze.motion = Clip(s, "Eating");
            var toGraze = loco.AddTransition(graze);
            toGraze.AddCondition(AnimatorConditionMode.If, 0f, "Grazing");
            toGraze.hasExitTime = false;
            toGraze.duration = 0.25f;
            var toLoco = graze.AddTransition(loco);
            toLoco.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grazing");
            toLoco.hasExitTime = false;
            toLoco.duration = 0.2f;
            sm.defaultState = loco;
            EditorUtility.SetDirty(c);
            return c;
        }

        static string BuildPrefab(WildlifeSpecies s, WildlifeTuning tuning, AnimatorController controller)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>($"{VendorRoot}/Prefabs/Animal_{s}.prefab");
            if (!source) return $"{s}: MISSING source prefab";

            // A preview scene keeps the open scene (World.unity) untouched.
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                go.name = $"Wildlife_{s}";
                var bounds = RendererBounds(go);

                var animator = go.GetComponentInChildren<Animator>();
                if (!animator) animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;

                // No ?? here: UnityEngine.Object fake-null defeats it (friction #9).
                var agent = go.GetComponent<NavMeshAgent>();
                if (!agent) agent = go.AddComponent<NavMeshAgent>();
                agent.agentTypeID = 0; // Humanoid: the type the NavMeshSurface bakes
                agent.baseOffset = 0f;
                // <= 0.4: the radius level baked agent type 0 with.
                agent.radius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * 0.5f, 0.2f, 0.4f);
                agent.height = Mathf.Max(0.4f, bounds.size.y);
                agent.stoppingDistance = 0.5f;
                agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
                agent.avoidancePriority = s == WildlifeSpecies.Wolf ? 40 : s == WildlifeSpecies.Fox ? 60 : 50;
                // WildlifeAgent.Start enables it once NavMeshSurface data is loaded (player load order).
                agent.enabled = false;

                var wa = go.GetComponent<WildlifeAgent>();
                if (!wa) wa = go.AddComponent<WildlifeAgent>();
                var so = new SerializedObject(wa);
                so.FindProperty("species").enumValueIndex = (int)s;
                so.FindProperty("tuning").objectReferenceValue = tuning;
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedPropertiesWithoutUndo();

                string path = PrefabPath(s);
                PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
                return $"{s}: {(ok ? "ok" : "FAILED")} {path} size={bounds.size.ToString("F2")} " +
                       $"agentRadius={agent.radius:F2} agentHeight={agent.height:F2}";
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }
    }
}
