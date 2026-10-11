using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Wildlife.Editor
{
    /// <summary>
    /// Technical-art import fix (WL-BUG-7). Many vendor rigs are authored facing -Z. For every rigged FBX this
    /// measures which way the skeleton faces at rest and, only when it faces -Z, turns the model an exact 180
    /// degrees about the root's Y axis, so "forward is +Z" holds with no runtime yaw offset.
    /// Detection (spec Amendment 3): primary is the left/right bone rule, mean X of the left bones minus mean X
    /// of the right bones in root space (left bones at +X means the rig faces -Z). Fallback 1 is head minus
    /// hips on Z; fallback 2 warns and leaves the model alone. Humanoid rigs and non-FBX files are skipped.
    /// The decision is made once per import from the unmodified hierarchy and the model and clip passes share
    /// it, so a reimport can never double-flip. Clips key the turned nodes (Quaternius keys AnimalArmature in
    /// every clip), so their curves get the same turn or they would undo it.
    /// SyncPrefabs() applies the same rule to unpacked prefab copies; FacingReport() lists every rigged model.
    /// Run via: unity command eval "return WashedAshore.Wildlife.Editor.ModelFacingPostprocessor.FacingReport();"
    /// </summary>
    public class ModelFacingPostprocessor : AssetPostprocessor
    {
        // Bump to force every model to reimport with the current rules.
        const uint Version = 2;

        // Side measure must clear this share of the skeleton's horizontal width (spec Amendment 3).
        public const float SideEpsilonRatio = 0.05f;
        // Head-bone and hip-bone candidates, best first, compared after NormaliseBoneName.
        // "face" last: the Quaternius fish rigs (Fish1/Fish2) have no head or neck bone, only Face.
        public static readonly string[] HeadNames = { "head", "head_jnt", "headjoint", "neck", "face" };
        public static readonly string[] HipNames = { "hips", "pelvis", "body", "spine" };
        public const float FlipBelowZ = -0.5f;
        public const float KeepAboveZ = 0.5f;
        // A head (nearly) straight above the hips (upright bipeds) says nothing about facing.
        public const float MinHorizontalRatio = 0.2f;

        public const string RecordName = "ModelFacingRecord";
        public const string ReportPath = "TestResults/model-facing-report.json";

        public enum Decision { Flip, Keep, Ambiguous, Unrigged, Skipped }

        [Serializable]
        public class Result
        {
            public string decision;
            public string decidedBy = "none";   // "LR", "head" or "none"
            public string lrForm = "none";      // "L-R", "L-only" or "none"
            public float lrMeasure, epsilon;
            public string headBone;
            public float x, z;                  // head minus hips, normalised XZ
            public string reason, skipReason;
            public string[] turnedPaths = new string[0];
            public bool applied;
            public Decision Value => (Decision)Enum.Parse(typeof(Decision), decision);
        }

        static readonly Quaternion Yaw180 = Quaternion.Euler(0f, 180f, 0f);
        static readonly Dictionary<string, Result> Pending = new Dictionary<string, Result>();

        public override uint GetVersion() => Version;

        void OnPreprocessModel() => Pending.Remove(assetPath);

        public static string SkipReason(string path, AssetImporter importer) =>
            SkipReason(path, importer is ModelImporter mi ? mi.animationType : ModelImporterAnimationType.None);

        // Testable without touching a real importer (setting animationType dirties its avatar settings).
        public static string SkipReason(string path, ModelImporterAnimationType animationType)
        {
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return "not FBX";
            if (animationType == ModelImporterAnimationType.Human) return "Humanoid rig";
            return null;
        }

        // Whichever pass runs first decides, from the hierarchy as the importer built it.
        Result Decide(GameObject root)
        {
            if (Pending.TryGetValue(assetPath, out var r)) return r;
            string skip = SkipReason(assetPath, assetImporter);
            r = skip == null ? Detect(root.transform) : new Result { decision = nameof(Decision.Skipped), skipReason = skip };
            Pending[assetPath] = r;
            if (skip == "Humanoid rig" && root.GetComponentInChildren<SkinnedMeshRenderer>(true))
                Debug.Log($"{assetPath}: facing check skipped (Humanoid rig).");
            if (r.Value == Decision.Ambiguous)
                context.LogImportWarning($"{assetPath}: facing is ambiguous ({r.reason}); left unchanged. " + Describe(r));
            else if (!string.IsNullOrEmpty(r.reason))
                context.LogImportWarning($"{assetPath}: {r.reason}. " + Describe(r));
            return r;
        }

        public static string Describe(Result r) =>
            $"L/R {r.lrForm} {r.lrMeasure:F4} vs eps {r.epsilon:F4}; head '{r.headBone}' (x {r.x:F2}, z {r.z:F2}); decided by {r.decidedBy}";

        void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            var r = Decide(root);
            if (r.Value != Decision.Flip) return;
            foreach (var error in TurnClip(clip, r.turnedPaths))
                context.LogImportError($"{assetPath}/{clip.name}: {error}");
        }

        void OnPostprocessModel(GameObject root)
        {
            var r = Decide(root);
            if (r.Value == Decision.Unrigged || r.Value == Decision.Skipped) return;
            if (r.Value == Decision.Flip)
            {
                TurnNodes(root.transform, r.turnedPaths);
                r.applied = true;
            }
            var record = new TextAsset(JsonUtility.ToJson(r)) { name = RecordName, hideFlags = HideFlags.HideInHierarchy };
            context.AddObjectToAsset(RecordName, record);
        }

        // ---- Detection -------------------------------------------------------------------------------

        static string StripNamespace(string name)
        {
            int cut = Math.Max(name.LastIndexOf(':'), name.LastIndexOf('|'));
            return cut >= 0 ? name.Substring(cut + 1) : name;
        }

        public static string NormaliseBoneName(string name)
        {
            string n = StripNamespace(name).ToLowerInvariant();
            foreach (var prefix in new[] { "def-", "def_", "bip01 ", "bip001 ", "bip01_", "bip001_" })
                if (n.StartsWith(prefix)) n = n.Substring(prefix.Length);
            return n.Trim();
        }

        /// <summary>-1 left, +1 right, 0 neither: "*.L", "*_L", "Left*", "Bip01 L Thigh" and the mirrored forms.</summary>
        public static int BoneSide(string name)
        {
            string n = StripNamespace(name).Trim();
            if (n.EndsWith("_end", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            if (n.EndsWith(".L") || n.EndsWith("_L") || n.EndsWith(".l") || n.EndsWith("_l") || SideWord(n, "Left") || n.Contains(" L ")) return -1;
            if (n.EndsWith(".R") || n.EndsWith("_R") || n.EndsWith(".r") || n.EndsWith("_r") || SideWord(n, "Right") || n.Contains(" R ")) return 1;
            return 0;
        }

        // "LeftArm", "Left_Hand", "Left.003", "Left" match; "Leftover" does not.
        static bool SideWord(string n, string word)
        {
            if (!n.StartsWith(word)) return false;
            if (n.Length == word.Length) return true;
            char c = n[word.Length];
            return char.IsUpper(c) || char.IsDigit(c) || c == '_' || c == '.';
        }

        static Transform FindNamed(IEnumerable<Transform> bones, string[] names)
        {
            var ordered = bones.OrderBy(Depth).ToList();
            foreach (var candidate in names)
            {
                var hit = ordered.FirstOrDefault(t => NormaliseBoneName(t.name) == candidate);
                if (hit) return hit;
            }
            return null;
        }

        public static Transform FindHead(Transform modelRoot) =>
            FindNamed(modelRoot.GetComponentsInChildren<Transform>(true).Where(t => t != modelRoot), HeadNames);

        static int Depth(Transform t) { int d = 0; for (; t.parent; t = t.parent) d++; return d; }

        /// <summary>Measures facing in modelRoot's space. Pure: never changes the hierarchy.</summary>
        public static Result Detect(Transform modelRoot)
        {
            var r = new Result();
            var skinned = modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var anyBone = skinned.Select(s => s.rootBone ? s.rootBone : s.bones.FirstOrDefault(b => b)).FirstOrDefault(b => b);
            var armature = anyBone ? TopChild(modelRoot, anyBone) : null;
            if (!armature) { r.decision = nameof(Decision.Unrigged); return r; }

            var bones = armature.GetComponentsInChildren<Transform>(true).Where(t => t != armature).ToList();
            Vector3 Local(Transform t) => modelRoot.InverseTransformPoint(t.position);

            // Primary: left/right bones. Left at +X means the rig faces -Z.
            if (bones.Count > 0)
            {
                float minX = bones.Min(t => Local(t).x), maxX = bones.Max(t => Local(t).x);
                float minZ = bones.Min(t => Local(t).z), maxZ = bones.Max(t => Local(t).z);
                r.epsilon = SideEpsilonRatio * Mathf.Max(maxX - minX, maxZ - minZ);
            }
            var left = bones.Where(t => BoneSide(t.name) < 0).ToList();
            var right = bones.Where(t => BoneSide(t.name) > 0).ToList();
            if (left.Count > 0)
            {
                float meanL = left.Average(t => Local(t).x);
                r.lrForm = right.Count > 0 ? "L-R" : "L-only";
                r.lrMeasure = right.Count > 0 ? meanL - right.Average(t => Local(t).x) : meanL;
            }

            // Fallback 1: head minus hips (or the armature root) on Z.
            string headVerdict = null;
            var head = FindNamed(bones, HeadNames);
            if (head)
            {
                r.headBone = head.name;
                var hips = FindNamed(bones.Where(t => t != head), HipNames) ?? armature;
                var d = Local(head) - Local(hips);
                var flat = new Vector2(d.x, d.z);
                if (flat.magnitude >= 1e-5f && flat.magnitude >= MinHorizontalRatio * d.magnitude)
                {
                    flat.Normalize();
                    r.x = flat.x;
                    r.z = flat.y;
                    headVerdict = r.z < FlipBelowZ ? nameof(Decision.Flip) : r.z > KeepAboveZ ? nameof(Decision.Keep) : null;
                }
            }

            if (r.lrForm != "none" && Mathf.Abs(r.lrMeasure) > r.epsilon)
            {
                r.decidedBy = "LR";
                r.decision = r.lrMeasure > 0f ? nameof(Decision.Flip) : nameof(Decision.Keep);
                if (headVerdict != null && headVerdict != r.decision)
                    r.reason = $"left/right rule says {r.decision} but the head rule says {headVerdict}; left/right wins";
            }
            else if (headVerdict != null)
            {
                r.decidedBy = "head";
                r.decision = headVerdict;
            }
            else
            {
                r.decision = nameof(Decision.Ambiguous);
                r.reason = r.lrForm == "none" ? "no left/right bones and no clear head direction"
                                              : "left/right offset within epsilon and no clear head direction";
            }

            // The armature root plus every root child holding a skinned mesh.
            var turned = new List<Transform> { armature };
            foreach (var s in skinned)
            {
                var top = TopChild(modelRoot, s.transform);
                if (top && !turned.Contains(top)) turned.Add(top);
            }
            r.turnedPaths = turned.Select(t => AnimationUtility.CalculateTransformPath(t, modelRoot)).ToArray();
            return r;
        }

        static Transform TopChild(Transform modelRoot, Transform t)
        {
            while (t && t.parent != modelRoot) t = t.parent;
            return t;
        }

        // ---- The 180-degree turn ---------------------------------------------------------------------

        public static void TurnNodes(Transform modelRoot, IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                var t = modelRoot.Find(path);
                if (!t) continue;
                var p = t.localPosition;
                t.SetLocalPositionAndRotation(new Vector3(-p.x, p.y, -p.z), Yaw180 * t.localRotation);
            }
        }

        /// <summary>Gives the clip's curves on the turned nodes the same turn. Returns errors, never skips.</summary>
        public static List<string> TurnClip(AnimationClip clip, ICollection<string> paths)
        {
            var errors = new List<string>();
            var bindings = AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.type == typeof(Transform) && paths.Contains(b.path)).ToList();
            foreach (var group in bindings.GroupBy(b => b.path))
            {
                var byName = group.ToDictionary(b => b.propertyName);
                var set = new Dictionary<EditorCurveBinding, AnimationCurve>();
                AnimationCurve Get(string n) => byName.TryGetValue(n, out var b) ? AnimationUtility.GetEditorCurve(clip, b) : null;

                foreach (var rot in new[] { "m_LocalRotation", "localRotation" })
                {
                    if (!byName.Keys.Any(n => n.StartsWith(rot + "."))) continue;
                    AnimationCurve qx = Get(rot + ".x"), qy = Get(rot + ".y"), qz = Get(rot + ".z"), qw = Get(rot + ".w");
                    if (qx == null || qy == null || qz == null || qw == null)
                    {
                        errors.Add($"'{group.Key}' has a partial {rot} curve set; cannot turn it exactly.");
                        continue;
                    }
                    // Yaw180 * (x, y, z, w) = (z, w, -x, -y).
                    set[byName[rot + ".x"]] = qz;
                    set[byName[rot + ".y"]] = qw;
                    set[byName[rot + ".z"]] = Scaled(qx, -1f, 0f);
                    set[byName[rot + ".w"]] = Scaled(qy, -1f, 0f);
                }
                // Unity Euler order applies Y last, so a parent-side yaw just adds to the Y channel.
                foreach (var b in group.Where(b => b.propertyName.StartsWith("localEulerAngles") ||
                                                   b.propertyName.StartsWith("m_LocalEulerAngles")))
                    if (b.propertyName.EndsWith(".y")) set[b] = Scaled(AnimationUtility.GetEditorCurve(clip, b), 1f, 180f);
                foreach (var pos in new[] { "m_LocalPosition", "localPosition" })
                {
                    if (byName.ContainsKey(pos + ".x")) set[byName[pos + ".x"]] = Scaled(Get(pos + ".x"), -1f, 0f);
                    if (byName.ContainsKey(pos + ".z")) set[byName[pos + ".z"]] = Scaled(Get(pos + ".z"), -1f, 0f);
                }
                foreach (var b in group.Where(b => !set.ContainsKey(b) && !b.propertyName.Contains("Scale")
                                                   && !(b.propertyName.StartsWith("m_LocalPosition.y") || b.propertyName.StartsWith("localPosition.y"))
                                                   && !b.propertyName.Contains("EulerAngles")))
                    errors.Add($"'{group.Key}' curve '{b.propertyName}' is not handled by the turn.");
                foreach (var kv in set) AnimationUtility.SetEditorCurve(clip, kv.Key, kv.Value);
            }
            return errors;
        }

        static AnimationCurve Scaled(AnimationCurve c, float scale, float add)
        {
            var keys = c.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value = keys[i].value * scale + add;
                keys[i].inTangent *= scale;
                keys[i].outTangent *= scale;
            }
            return new AnimationCurve(keys) { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode };
        }

        // ---- Unpacked prefab copies ------------------------------------------------------------------

        [MenuItem("Washed Ashore/Art/Sync Prefab Facing")]
        static void SyncMenu() => Debug.Log(SyncPrefabs());

        /// <summary>
        /// Applies the same detection and turn to prefabs that hold their own copy of a rig (not nested model
        /// prefabs, which inherit the import). Only the armature root and mesh-node transforms change. Once
        /// turned the rig faces +Z, so a second run changes nothing. Variants are checked for armature/mesh
        /// transform overrides, which would hide the fix.
        /// </summary>
        public static string SyncPrefabs()
        {
            var report = new StringBuilder();
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!asset || !asset.GetComponentInChildren<SkinnedMeshRenderer>(true)) continue;
                var type = PrefabUtility.GetPrefabAssetType(asset);
                if (type == PrefabAssetType.Variant) { CheckVariant(path, asset, report); continue; }
                if (type != PrefabAssetType.Regular) continue;
                var animator = asset.GetComponentInChildren<Animator>(true);
                if (animator && animator.avatar && animator.avatar.isHuman)
                {
                    report.AppendLine($"{path}: Skipped (Humanoid rig)");
                    continue;
                }

                var go = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var modelRoot = RigRoot(go.GetComponentInChildren<SkinnedMeshRenderer>(true));
                    var r = Detect(modelRoot);
                    var nodes = r.turnedPaths.Select(p => modelRoot.Find(p)).Where(t => t).ToList();
                    if (r.Value == Decision.Flip && nodes.Any(PrefabUtility.IsPartOfPrefabInstance))
                    {
                        report.AppendLine($"{path}: rig is a nested model prefab; it inherits the import, skipped");
                        continue;
                    }
                    if (r.Value == Decision.Ambiguous || !string.IsNullOrEmpty(r.reason))
                        Debug.LogWarning($"{path}: {(r.Value == Decision.Ambiguous ? "facing is ambiguous, left unchanged" : "facing")} ({r.reason}). {Describe(r)}");
                    if (r.Value == Decision.Flip)
                    {
                        TurnNodes(modelRoot, r.turnedPaths);
                        PrefabUtility.SaveAsPrefabAsset(go, path);
                    }
                    report.AppendLine($"{path}: {r.decision}. {Describe(r)}" +
                                      (r.Value == Decision.Flip ? $"; turned [{string.Join(", ", r.turnedPaths)}]" : ""));
                }
                finally { PrefabUtility.UnloadPrefabContents(go); }
            }
            return report.ToString();
        }

        // The node the rig hangs from: the lowest common ancestor of the skinned mesh and its root bone.
        static Transform RigRoot(SkinnedMeshRenderer smr)
        {
            var bone = smr.rootBone ? smr.rootBone : smr.bones.FirstOrDefault(b => b);
            if (!bone) return smr.transform.parent ? smr.transform.parent : smr.transform;
            var chain = new HashSet<Transform>();
            for (var t = smr.transform; t; t = t.parent) chain.Add(t);
            for (var t = bone; t; t = t.parent)
                if (chain.Contains(t)) return t == smr.transform && t.parent ? t.parent : t;
            return smr.transform.root;
        }

        // Flags overrides on the base's armature root or mesh node (direct children of the base root). The
        // variant root's own pose override is normal and ignored.
        static void CheckVariant(string path, GameObject asset, StringBuilder report)
        {
            var mods = PrefabUtility.GetPropertyModifications(asset) ?? new PropertyModification[0];
            foreach (var m in mods)
            {
                if (!(m.target is Transform t) || !t.parent || t.parent.parent) continue;
                if (!m.propertyPath.StartsWith("m_LocalRotation") && !m.propertyPath.StartsWith("m_LocalPosition")
                    && !m.propertyPath.StartsWith("m_LocalEulerAnglesHint")) continue;
                string msg = $"{path}: variant overrides {m.propertyPath} on '{t.name}', which hides the import facing fix.";
                Debug.LogError(msg);
                report.AppendLine("ERROR " + msg);
            }
        }

        // ---- Report ----------------------------------------------------------------------------------

        [Serializable]
        public class ReportEntry
        {
            public string path, kind, decision, decidedBy, lrForm, headBone, reason, skipReason;
            public float lrMeasure, epsilon, headX, headZ;
            public string currentDecision;
            public float currentLrMeasure, currentHeadX, currentHeadZ;
            public bool applied;
        }

        [Serializable]
        class ReportFile { public ReportEntry[] models; }

        [MenuItem("Washed Ashore/Art/Model Facing Report")]
        static void ReportMenu() => Debug.Log(FacingReport(ReportPath));

        /// <summary>
        /// Every rigged model and rigged unpacked prefab under Assets/. The un-prefixed fields are the import-time
        /// decision (models: from the import record; prefabs: measured now); current* is the hierarchy as it is
        /// now, so a flipped model reads Keep there. Writes JSON when jsonPath is set.
        /// </summary>
        public static string FacingReport(string jsonPath = null)
        {
            var entries = new List<ReportEntry>();
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!go) continue;
                var now = Detect(go.transform);
                if (now.Value == Decision.Unrigged) continue;
                var record = AssetDatabase.LoadAllAssetsAtPath(path).OfType<TextAsset>().FirstOrDefault(a => a.name == RecordName);
                string skip = SkipReason(path, AssetImporter.GetAtPath(path));
                var src = record ? JsonUtility.FromJson<Result>(record.text) : now;
                var e = Entry(path, "model", src, now);
                if (skip != null) { e.decision = nameof(Decision.Skipped); e.skipReason = skip; }
                else if (!record) e.decision = "unrecorded";
                e.applied = record && src.applied;
                entries.Add(e);
            }
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var smr = go ? go.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                if (!smr || PrefabUtility.GetPrefabAssetType(go) != PrefabAssetType.Regular) continue;
                var now = Detect(RigRoot(smr));
                entries.Add(Entry(path, "prefab", now, now));
            }
            string json = JsonUtility.ToJson(new ReportFile { models = entries.ToArray() }, true);
            if (!string.IsNullOrEmpty(jsonPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonPath)));
                File.WriteAllText(jsonPath, json);
            }
            return json;
        }

        static ReportEntry Entry(string path, string kind, Result src, Result now) => new ReportEntry
        {
            path = path, kind = kind, decision = src.decision, decidedBy = src.decidedBy, lrForm = src.lrForm,
            headBone = src.headBone, reason = src.reason, skipReason = src.skipReason, lrMeasure = src.lrMeasure,
            epsilon = src.epsilon, headX = src.x, headZ = src.z, currentDecision = now.decision,
            currentLrMeasure = now.lrMeasure, currentHeadX = now.x, currentHeadZ = now.z,
        };
    }
}
