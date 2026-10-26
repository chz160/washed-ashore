using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WashedAshore.Character.Appearance;

namespace WashedAshore.Character.Editor
{
    /// <summary>
    /// Builds the character assets from the imported FBX files, with no hand steps (spec H1/H2/H3): one skeleton prefab and one
    /// clip set per body type, and one CharacterAppearance asset per outfit. Rerunnable; run it after
    /// tools/blender/character/build_character_assets.ps1 and a refresh.
    /// Menu: Washed Ashore > Art > Character > Build Character Assets.  Batch: CharacterAssetBuilder.BuildAll().
    /// </summary>
    public static class CharacterAssetBuilder
    {
        public const string ArtRoot = "Assets/Art/Character";
        public const string AppearanceFolder = ArtRoot + "/Appearances";
        public const string DefaultAppearancePath = AppearanceFolder + "/Men_Farmer.asset"; // gate ruling: Worker first, Farmer if anything reads modern; the Worker's hi-vis vest and hard hat do (producer-2)

        /// <summary>folder name inside the pack, file prefix inside that folder. Mirrors build_character_assets.ps1.</summary>
        public static readonly Dictionary<BodyType, (string folder, string prefix)[]> Outfits = new Dictionary<BodyType, (string, string)[]>
        {
            { BodyType.Men, new[] { ("Worker", "Worker"), ("Farmer", "Farmer"), ("Casual_Hoodie", "Casual"), ("Casual_2", "Casual2"), ("Adventurer", "Adventurer") } },
            { BodyType.Women, new[] { ("Worker", "Worker"), ("Casual", "Casual"), ("Adventurer", "Adventurer"), ("Formal", "Formal") } },
        };

        /// <summary>The bones a Generic clip, the assembler and the first-person body rely on.</summary>
        public static readonly string[] RequiredBones =
        {
            "Root", "Body", "Hips", "Abdomen", "Torso", "Chest", "Neck", "Head",
            "Shoulder.L", "UpperArm.L", "LowerArm.L", "Wrist.L", "Shoulder.R", "UpperArm.R", "LowerArm.R", "Wrist.R",
            "UpperLeg.L", "LowerLeg.L", "Foot.L", "UpperLeg.R", "LowerLeg.R", "Foot.R",
        };

        public static string PartPath(BodyType type, string folder, string prefix, CharacterSlot slot) =>
            $"{CharacterImportSettings.PackRoot}/{type}/Parts/{folder}/{prefix}_{slot}.fbx";

        public static string AnimationsPath(BodyType type) => $"{CharacterImportSettings.PackRoot}/{type}/Animations.fbx";
        public static string AuthoredPath(BodyType type) => $"{CharacterImportSettings.ClipFolder}/Authored_{type}.fbx";
        public static string SkeletonPath(BodyType type) => $"{ArtRoot}/Skeleton_{type}.prefab";
        public static string ClipSetPath(BodyType type) => $"{ArtRoot}/CharacterClips_{type}.asset";
        public static string AppearancePath(BodyType type, string folder) => $"{AppearanceFolder}/{type}_{folder.Replace("_Hoodie", "").Replace("_", "")}.asset";

        [MenuItem("Washed Ashore/Art/Character/Build Character Assets")]
        static void BuildMenu() => Debug.Log(BuildAll());

        /// <summary>Batch entry point: unity run . -- -executeMethod WashedAshore.Character.Editor.CharacterAssetBuilder.BatchBuild</summary>
        public static void BatchBuild()
        {
            try
            {
                Debug.Log(BuildAll());
                File.WriteAllText("_bmad-output/poc/character-appearance-table.md", ContentTable());
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
        }

        /// <summary>One row per appearance asset: the part model in each slot and the tints, to check each asset matches its outfit name.</summary>
        public static string ContentTable()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("| Asset | Head | Body | Legs | Feet | Tints | Pose bounds | Controller | Shadow controller |").Append('\n');
            sb.Append("|---|---|---|---|---|---|---|---|---|").Append('\n');
            foreach (var kv in Outfits)
                foreach (var o in kv.Value)
                {
                    var a = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(AppearancePath(kv.Key, o.folder));
                    string Part(CharacterSlot s) => a.GetPart(s) ? a.GetPart(s).name : "MISSING";
                    string tints = a.tints.Count == 0 ? "none" : string.Join("; ", a.tints.Select(t => $"{t.slot}/{t.materialName}={t.color}"));
                    sb.Append($"| {Path.GetFileName(AssetDatabase.GetAssetPath(a))} | {Part(CharacterSlot.Head)} | {Part(CharacterSlot.Body)} | {Part(CharacterSlot.Legs)} | {Part(CharacterSlot.Feet)} | {tints} | {(a.hasPoseBounds ? a.poseBounds.size.ToString("F2") : "none")} | {(a.controller ? a.controller.name : "none")} | {(a.shadowController ? a.shadowController.name : "none")} |").Append('\n');
                }
            return sb.ToString();
        }

        public static string BuildAll()
        {
            AssetDatabase.Refresh();
            var log = new List<string>();
            Directory.CreateDirectory(AppearanceFolder);
            foreach (BodyType type in Enum.GetValues(typeof(BodyType)))
            {
                var skeleton = BuildSkeleton(type);
                var clips = BuildClipSet(type, log);
                var controller = CharacterAnimatorBuilder.Build(type, clips);
                CharacterAnimatorBuilder.BuildRangeEnds(type, controller, clips);
                var shadowController = CharacterAnimatorBuilder.BuildShadowOverride(type, controller, clips);
                foreach (var (folder, prefix) in Outfits[type])
                {
                    var a = BuildAppearance(type, folder, prefix, skeleton, controller, shadowController);
                    a.rangeLowView = CharacterAnimatorBuilder.RangeControllerFor(type, false, false);
                    a.rangeHighView = CharacterAnimatorBuilder.RangeControllerFor(type, true, false);
                    a.rangeLowShadow = CharacterAnimatorBuilder.RangeControllerFor(type, false, true);
                    a.rangeHighShadow = CharacterAnimatorBuilder.RangeControllerFor(type, true, true);
                    EditorUtility.SetDirty(a);
                    log.Add($"appearance {AssetDatabase.GetAssetPath(a)}");
                }
                log.Add($"{type}: skeleton {AssetDatabase.GetAssetPath(skeleton)}, {clips.clips.Count} clips");
            }
            foreach (BodyType type in Enum.GetValues(typeof(BodyType))) ComputePoseBounds(type, log);
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        /// <summary>Union of every stance clip's posed mesh over all outfits of one body type, in body-root space, padded 0.1 m.</summary>
        static void ComputePoseBounds(BodyType type, List<string> log)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(ClipSetPath(type));
            var appearances = Outfits[type].Select(o => AssetDatabase.LoadAssetAtPath<CharacterAppearance>(AppearancePath(type, o.folder))).ToList();
            var clips = set.clips.Where(c => Array.IndexOf(CharacterClipSet.Names.Required, c.name) >= 0 || c.name.EndsWith("_Low") || c.name.EndsWith("_High") || c.name.EndsWith("_Full")).ToList();
            Bounds? union = null;
            foreach (var a in appearances)
            {
                a.hasPoseBounds = false;
                var body = CharacterAssembler.Assemble(a);
                foreach (var c in clips)
                    for (int s = 0; s < 5; s++)
                    {
                        c.clip.SampleAnimation(body.Animator.gameObject, c.clip.length * s / 4f);
                        foreach (CharacterSlot slot in Enum.GetValues(typeof(CharacterSlot)))
                            foreach (var smr in body.Renderers(slot))
                            {
                                var mesh = new Mesh();
                                smr.BakeMesh(mesh, false);
                                var mb = mesh.bounds;
                                for (int i = 0; i < 8; i++)
                                {
                                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                                    var p = body.transform.InverseTransformPoint(smr.transform.position + smr.transform.rotation * corner);
                                    if (union == null) union = new Bounds(p, Vector3.zero); else { var u = union.Value; u.Encapsulate(p); union = u; }
                                }
                                UnityEngine.Object.DestroyImmediate(mesh);
                            }
                    }
                UnityEngine.Object.DestroyImmediate(body.gameObject);
            }
            var padded = union.Value;
            padded.Expand(0.2f);
            foreach (var a in appearances) { a.poseBounds = padded; a.hasPoseBounds = true; EditorUtility.SetDirty(a); }
            log.Add($"{type}: pose bounds center {padded.center} size {padded.size} over {clips.Count} clips x 5 samples x {appearances.Count} outfits");
        }

        static GameObject BuildSkeleton(BodyType type)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PartPath(type, "Worker", "Worker", CharacterSlot.Body));
            if (model == null) throw new InvalidOperationException($"{type}: Worker_Body.fbx is not imported; run build_character_assets.ps1 first");
            var go = UnityEngine.Object.Instantiate(model);
            go.name = $"Skeleton_{type}";
            go.transform.localScale = Vector3.one * SkeletonScaleOf(type); // uniform, feet stay at the origin (td ruling)
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                UnityEngine.Object.DestroyImmediate(smr.gameObject);
            // The pack's clips are keyed from the armature node down (paths start at "Root"), so the Animator goes on that node.
            // No avatar: Generic clips bind by path, and the part avatars are rooted one level higher.
            var rootAnimator = go.GetComponent<Animator>();
            if (rootAnimator != null) UnityEngine.Object.DestroyImmediate(rootAnimator);
            var armature = go.transform.Find("CharacterArmature");
            if (armature == null) throw new InvalidOperationException($"{type}: no CharacterArmature node in Worker_Body.fbx");
            var animator = armature.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // the shadow keeps animating when the body is offscreen
            Directory.CreateDirectory(ArtRoot);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, SkeletonPath(type));
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        static CharacterClipSet BuildClipSet(BodyType type, List<string> log)
        {
            var path = ClipSetPath(type);
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(path);
            if (set == null) { set = ScriptableObject.CreateInstance<CharacterClipSet>(); AssetDatabase.CreateAsset(set, path); }
            set.bodyType = type;
            set.clips.Clear();
            var speeds = LoadSpeeds(type);
            foreach (var fbx in new[] { AnimationsPath(type), AuthoredPath(type) })
            {
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name, StringComparer.Ordinal))
                {
                    if (set.clips.Any(c => c.name == clip.name)) { log.Add($"WARNING duplicate clip name {clip.name} in {fbx}"); continue; }
                    speeds.TryGetValue(clip.name, out float speed);
                    set.clips.Add(new CharacterClipSet.NamedClip { name = clip.name, clip = clip, speed = speed });
                }
            }
            foreach (var required in CharacterClipSet.Names.Required)
                if (set.Get(required) == null) log.Add($"ERROR {type}: clip '{required}' missing");
            EditorUtility.SetDirty(set);
            return set;
        }

        [Serializable] class SpeedEntry { public string name; public float speed; }
        [Serializable] class SpeedFile { public float scale = 1f; public List<SpeedEntry> clips; }

        static SpeedFile LoadSpeedFile(BodyType type)
        {
            var file = Path.Combine("_bmad-output", "poc", "character-clips", $"speeds-{type}.json");
            return File.Exists(file) ? JsonUtility.FromJson<SpeedFile>(File.ReadAllText(file)) : new SpeedFile { clips = new List<SpeedEntry>() };
        }

        /// <summary>Uniform skeleton scale for a body type, from the authoring report (men: td's s; women: 1).</summary>
        public static float SkeletonScaleOf(BodyType type) => LoadSpeedFile(type).scale;

        static Dictionary<string, float> LoadSpeeds(BodyType type)
        {
            var map = new Dictionary<string, float>();
            foreach (var e in LoadSpeedFile(type).clips) map[e.name] = e.speed; // on-body speeds (scaled)
            return map;
        }

        static CharacterAppearance BuildAppearance(BodyType type, string folder, string prefix, GameObject skeleton, RuntimeAnimatorController controller, RuntimeAnimatorController shadowController)
        {
            var path = AppearancePath(type, folder);
            var a = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(path);
            if (a == null) { a = ScriptableObject.CreateInstance<CharacterAppearance>(); AssetDatabase.CreateAsset(a, path); }
            a.id = Path.GetFileNameWithoutExtension(path);
            a.bodyType = type;
            a.skeleton = skeleton;
            a.skeletonScale = SkeletonScaleOf(type);
            a.controller = controller;
            a.shadowController = shadowController;
            a.tints.Clear();
            foreach (CharacterSlot slot in Enum.GetValues(typeof(CharacterSlot)))
                a.SetPart(slot, AssetDatabase.LoadAssetAtPath<GameObject>(PartPath(type, folder, prefix, slot)));
            EditorUtility.SetDirty(a);
            return a;
        }
    }
}
