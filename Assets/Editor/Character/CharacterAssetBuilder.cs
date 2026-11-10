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
        public const string DefaultAppearancePath = AppearanceFolder + "/Men_Survivor.asset"; // the Survivor is the player since pod 49 (the Farmer was the default before)

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

        // The Men clip family is the Survivor's since pod 49 (P4): the retargeted pack clips and the clips authored on Skeleton_Human. The Women keep the Quaternius sources until they are removed.
        public static string AnimationsPath(BodyType type) => type == BodyType.Men ? $"{CharacterImportSettings.ClipFolder}/Pack_Retargeted.fbx" : $"{CharacterImportSettings.PackRoot}/{type}/Animations.fbx";
        public static string AuthoredPath(BodyType type) => type == BodyType.Men ? $"{CharacterImportSettings.ClipFolder}/Authored_Survivor.fbx" : $"{CharacterImportSettings.ClipFolder}/Authored_{type}.fbx";
        public static string SkeletonHumanPrefabPath => $"{ArtRoot}/Skeleton_Human.prefab";
        public static string SurvivorAppearancePath => $"{AppearanceFolder}/Men_Survivor.asset";
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
                Directory.CreateDirectory("TestResults/character");
                File.WriteAllText("TestResults/character/character-appearance-table.md", ContentTable());
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
                if (type == BodyType.Women)
                {
                    // The retired low-poly women (skeleton, clips, controllers, appearances, pose bounds) are not rebuilt: their files stay byte-unchanged until the removal commit (item 6).
                    log.Add("Women: left untouched (retired low-poly set, removed in item 6)");
                    continue;
                }
                var clips = BuildClipSet(type, log);
                var controller = CharacterAnimatorBuilder.Build(type, clips);
                CharacterAnimatorBuilder.BuildRangeEnds(type, controller, clips);
                var shadowController = CharacterAnimatorBuilder.BuildShadowOverride(type, controller, clips);
                if (type == BodyType.Men)
                {
                    // The Men clip set and controllers are the Survivor's. The retired low-poly Men outfits, Skeleton_Men.prefab and their pose bounds are NOT rebuilt:
                    // their files stay byte-unchanged until the removal commit (td-49 C1). The Survivor is built by BuildSurvivor below.
                    log.Add($"{type}: {clips.clips.Count} clips (the Survivor's); low-poly outfits, Skeleton_Men.prefab and pose bounds left untouched");
                    continue;
                }
                var skeleton = BuildSkeleton(type);
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
            // pose bounds: the Men's are the Survivor's own (BuildSurvivor); the retired low-poly sets keep theirs
            BuildSurvivor(log);
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        /// <summary>Union of every stance clip's posed mesh over all outfits of one body type, in body-root space, padded 0.1 m.</summary>
        static void ComputePoseBounds(BodyType type, List<string> log)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(ClipSetPath(type));
            var appearances = Outfits[type].Select(o => AssetDatabase.LoadAssetAtPath<CharacterAppearance>(AppearancePath(type, o.folder))).ToList();
            ComputePoseBoundsOf(set, appearances, type.ToString(), log);
        }

        /// <summary>The clips the pose bounds must cover: every required stance clip, the range-end and full-swing variants, and Stand_Idle (the Stand blend tree's zero node). The Survivor tests use the same predicate (td-49 C3).</summary>
        public static bool IsPoseBoundsClip(string name) =>
            Array.IndexOf(CharacterClipSet.Names.Required, name) >= 0 || name == CharacterClipSet.Names.StandIdle || name.EndsWith("_Low") || name.EndsWith("_High") || name.EndsWith("_Full");

        /// <summary>The union of every stance clip's posed mesh over the given appearances, written to each of them (the Survivor is measured on its own, never with the low-poly outfits).</summary>
        static void ComputePoseBoundsOf(CharacterClipSet set, List<CharacterAppearance> appearances, string label, List<string> log)
        {
            var clips = set.clips.Where(c => IsPoseBoundsClip(c.name)).ToList();
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
            log.Add($"{label}: pose bounds center {padded.center} size {padded.size} over {clips.Count} clips x 5 samples x {appearances.Count} outfits");
        }

        static GameObject BuildSkeleton(BodyType type)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PartPath(type, "Worker", "Worker", CharacterSlot.Body));
            if (model == null) throw new InvalidOperationException($"{type}: Worker_Body.fbx is not imported; run build_character_assets.ps1 first");
            return BuildSkeletonPrefab(model, $"Skeleton_{type}", SkeletonScaleOf(type), SkeletonPath(type));
        }

        /// <summary>Skeleton_Human.prefab: the armature of Skeleton_Human.fbx under the Survivor's uniform scale (speeds-Survivor.json), an Animator on the armature node.</summary>
        static GameObject BuildSkeletonHuman()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImportSettings.SkeletonHumanPath);
            if (model == null) throw new InvalidOperationException($"{CharacterImportSettings.SkeletonHumanPath} is not imported");
            return BuildSkeletonPrefab(model, "Skeleton_Human", SkeletonScaleOf(BodyType.Men), SkeletonHumanPrefabPath);
        }

        static GameObject BuildSkeletonPrefab(GameObject model, string name, float scale, string prefabPath)
        {
            var go = UnityEngine.Object.Instantiate(model);
            go.name = name;
            // Skeleton_Human.fbx has the armature as its only top node, so Unity makes that node the prefab root (named after the file, import scale 100, child "Root").
            // Wrap it so the prefab has the same shape as Skeleton_Men: root (scale) > CharacterArmature (import scale 100) > Root.
            if (go.transform.Find("CharacterArmature") == null && go.transform.Find("Root") != null)
            {
                var wrapper = new GameObject(name);
                go.transform.SetParent(wrapper.transform, false);
                go.name = "CharacterArmature";
                go = wrapper;
            }
            go.transform.localScale = Vector3.one * scale; // uniform, feet stay at the origin (td ruling)
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                UnityEngine.Object.DestroyImmediate(smr.gameObject);
            // The pack's clips are keyed from the armature node down (paths start at "Root"), so the Animator goes on that node.
            // No avatar: Generic clips bind by path, and the part avatars are rooted one level higher.
            var rootAnimator = go.GetComponent<Animator>();
            if (rootAnimator != null) UnityEngine.Object.DestroyImmediate(rootAnimator);
            var armature = go.transform.Find("CharacterArmature");
            if (armature == null) throw new InvalidOperationException($"{name}: no CharacterArmature node in {model.name}");
            // The wrapped import root carries the model's Animator (with its avatar); the rule is no avatar, one Animator.
            var inherited = armature.GetComponent<Animator>();
            if (inherited != null) UnityEngine.Object.DestroyImmediate(inherited);
            var animator = armature.gameObject.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // the shadow keeps animating when the body is offscreen
            Directory.CreateDirectory(ArtRoot);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>
        /// The Survivor appearance (pod 49 P4): Head + Body parts only (Legs and Feet explicitly empty), Skeleton_Human.prefab, the Men controller family built from the
        /// regenerated clips. Everything numeric is read from data of record: skeletonScale and the Walk/Run clip speeds from speeds-Survivor.json (via the Men speeds file),
        /// the knee-shape target flex from the shape names on the imported body mesh (Knee_L_90 / Knee_R_90 = 90 degrees), pose bounds measured here. Nothing is typed in.
        /// </summary>
        static void BuildSurvivor(List<string> log)
        {
            var skeleton = BuildSkeletonHuman();
            var a = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(SurvivorAppearancePath);
            if (a == null) { a = ScriptableObject.CreateInstance<CharacterAppearance>(); AssetDatabase.CreateAsset(a, SurvivorAppearancePath); }
            a.id = Path.GetFileNameWithoutExtension(SurvivorAppearancePath);
            a.bodyType = BodyType.Men;
            a.skeleton = skeleton;
            a.skeletonScale = SkeletonScaleOf(BodyType.Men);
            a.walkClipSpeed = WalkClipSpeedOf(BodyType.Men);
            a.runClipSpeed = RunClipSpeedOf(BodyType.Men);
            a.controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterAnimatorBuilder.ControllerPath(BodyType.Men));
            a.shadowController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CharacterAnimatorBuilder.ShadowControllerPath(BodyType.Men));
            a.rangeLowView = CharacterAnimatorBuilder.RangeControllerFor(BodyType.Men, false, false);
            a.rangeHighView = CharacterAnimatorBuilder.RangeControllerFor(BodyType.Men, true, false);
            a.rangeLowShadow = CharacterAnimatorBuilder.RangeControllerFor(BodyType.Men, false, true);
            a.rangeHighShadow = CharacterAnimatorBuilder.RangeControllerFor(BodyType.Men, true, true);
            a.head = AssetDatabase.LoadAssetAtPath<GameObject>(SurvivorAssetBuilder.HeadPath);
            a.body = AssetDatabase.LoadAssetAtPath<GameObject>(SurvivorAssetBuilder.BodyPath);
            a.legs = null;
            a.feet = null;
            a.emptySlots = new List<CharacterSlot> { CharacterSlot.Legs, CharacterSlot.Feet };
            a.tints.Clear();
            if (a.head == null || a.body == null) throw new InvalidOperationException("Survivor_Head.fbx or Survivor_Body.fbx is not imported");
            a.kneeShapeStartDegrees = 20f;
            a.kneeShapeTargetDegrees = KneeShapeTargetDegrees(a.body);
            EditorUtility.SetDirty(a);
            ComputePoseBoundsOf(AssetDatabase.LoadAssetAtPath<CharacterClipSet>(ClipSetPath(BodyType.Men)), new List<CharacterAppearance> { a }, "Survivor", log);
            log.Add($"appearance {SurvivorAppearancePath}: scale {a.skeletonScale:F4}, walk clip {a.walkClipSpeed:F4}, run clip {a.runClipSpeed:F4}, knee shape {a.kneeShapeStartDegrees:F0}..{a.kneeShapeTargetDegrees:F0} deg");
        }

        /// <summary>The flex the knee shapes were authored at, read from their names ("Knee_L_90" is 90 degrees); both sides must agree.</summary>
        static float KneeShapeTargetDegrees(GameObject body)
        {
            var mesh = body.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMesh).FirstOrDefault(m => m != null && m.GetBlendShapeIndex(string.Format(KneeCorrectiveDriver.ShapeFormat, "L")) >= 0)
                       ?? throw new InvalidOperationException("the Survivor body has no knee shapes");
            float Read(string side)
            {
                string name = string.Format(KneeCorrectiveDriver.ShapeFormat, side);
                if (mesh.GetBlendShapeIndex(name) < 0) throw new InvalidOperationException("missing knee shape " + name);
                return float.Parse(name.Substring(name.LastIndexOf('_') + 1), System.Globalization.CultureInfo.InvariantCulture);
            }
            float l = Read("L"), r = Read("R");
            if (Math.Abs(l - r) > 1e-4f) throw new InvalidOperationException($"the knee shapes were authored at different flex: L {l}, R {r}");
            return l;
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

        /// <summary>Per-clip speed and eye-point data read by the builder (data of record, not a report).</summary>
        public const string ClipDataDir = "Data/character/clips";

        [Serializable] class SpeedEntry { public string name; public float speed; }
        [Serializable] class SpeedFile { public float scale = 1f; public List<SpeedEntry> clips; }
        // for validation only: no initializer, so a file without a "scale" key reads 0 and is refused (JsonUtility runs SpeedFile's scale = 1f for a missing key)
        [Serializable] class StrictSpeedFile { public float scale; public List<SpeedEntry> clips; }

        static SpeedFile LoadSpeedFile(BodyType type)
        {
            var file = Path.Combine(ClipDataDir, type == BodyType.Men ? "speeds-Survivor.json" : $"speeds-{type}.json");   // the Men family is the Survivor's
            if (type == BodyType.Men)
            {
                // strict: a missing or broken file must stop the build, not fall back to scale 1 and the Quaternius speeds (td-49 C2)
                if (!File.Exists(file)) throw new InvalidOperationException($"{file} is missing");
                string error = ValidateSurvivorSpeeds(File.ReadAllText(file));
                if (error != null) throw new InvalidOperationException($"{file}: {error}");
            }
            return File.Exists(file) ? JsonUtility.FromJson<SpeedFile>(File.ReadAllText(file)) : new SpeedFile { clips = new List<SpeedEntry>() };
        }

        /// <summary>Null when the speeds JSON is usable for the Survivor, else why not: unreadable, scale not above 0, or no Walk or Run entry above 0.</summary>
        public static string ValidateSurvivorSpeeds(string json)
        {
            StrictSpeedFile f;
            try { f = JsonUtility.FromJson<StrictSpeedFile>(json); } catch (Exception e) { return "unreadable: " + e.Message; }
            if (f == null || f.clips == null) return "no clips list";
            if (!(f.scale > 0f)) return $"scale {f.scale} is not above 0";
            foreach (var clip in new[] { "Walk", "Run" })
                if (!f.clips.Any(c => c.name == clip && c.speed > 0f)) return $"no {clip} speed above 0";
            return null;
        }

        /// <summary>Uniform skeleton scale for a body type, from the authoring report (men: td's s; women: 1).</summary>
        public static float SkeletonScaleOf(BodyType type) => LoadSpeedFile(type).scale;

        /// <summary>Walk and Run clip speeds on the UNSCALED skeleton (on-body speed from the speeds file divided by its scale); the Quaternius defaults when the file has no entry.</summary>
        public static float WalkClipSpeedOf(BodyType type) => ClipSpeedOf(type, "Walk", CharacterClipSet.DefaultWalkClipSpeed);
        public static float RunClipSpeedOf(BodyType type) => ClipSpeedOf(type, "Run", CharacterClipSet.DefaultRunClipSpeed);

        static float ClipSpeedOf(BodyType type, string clip, float fallback)
        {
            var file = LoadSpeedFile(type);
            foreach (var e in file.clips) if (e.name == clip && e.speed > 0f) return e.speed / (file.scale > 0f ? file.scale : 1f);
            return fallback;
        }

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
            a.walkClipSpeed = WalkClipSpeedOf(type);
            a.runClipSpeed = RunClipSpeedOf(type);
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
