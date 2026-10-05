// Animals: Generic rig, looping locomotion clips, one AnimatorController (Speed blend: Idle/Walk/Gallop) and prefab per species.
var root = "Assets/ThirdParty/Quaternius/Animals";
foreach (var sub in new[]{"Controllers", "Prefabs"}) if (!UnityEditor.AssetDatabase.IsValidFolder(root + "/" + sub)) UnityEditor.AssetDatabase.CreateFolder(root, sub);
var loops = new System.Collections.Generic.HashSet<string>{"Walk","Idle","Idle_2","Gallop","Eating","Idle_Headlow","Idle_2_HeadLow"};
var sb = new System.Text.StringBuilder();
foreach (var stale in UnityEngine.Object.FindObjectsByType<UnityEngine.Transform>(UnityEngine.FindObjectsSortMode.None))
  if (stale != null && stale.parent == null && stale.name.StartsWith("Animal_")) { sb.AppendLine("removed stale scene object " + stale.name); UnityEngine.Object.DestroyImmediate(stale.gameObject); }
foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Model", new[]{ root + "/FBX" })) {
  var p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
  var name = System.IO.Path.GetFileNameWithoutExtension(p);
  var mi = (UnityEditor.ModelImporter)UnityEditor.AssetImporter.GetAtPath(p);
  mi.animationType = UnityEditor.ModelImporterAnimationType.Generic;
  mi.bakeAxisConversion = true;
  // source models are ~2.5-5x real size; scale to plausible heights (fox ~0.6 m, wolf ~0.9 m, horse ~1.8 m)
  var small = name == "Fox" || name == "Husky" || name == "ShibaInu";
  mi.globalScale = small ? 0.22f : (name == "Bull" ? 0.32f : 0.36f);
  var clips = mi.defaultClipAnimations;
  foreach (var c in clips) {
    var shortName = c.name.Contains("|") ? c.name.Substring(c.name.IndexOf('|') + 1) : c.name;
    c.name = shortName;
    c.loopTime = loops.Contains(shortName);
  }
  mi.clipAnimations = clips;
  mi.SaveAndReimport();
  var byName = new System.Collections.Generic.Dictionary<string, UnityEngine.AnimationClip>();
  foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(p)) { var c = o as UnityEngine.AnimationClip; if (c != null && !c.name.StartsWith("__preview")) byName[c.name] = c; }
  if (!byName.ContainsKey("Walk") || !byName.ContainsKey("Idle")) { sb.AppendLine("SKIP " + name + " (no Walk/Idle)"); continue; }
  var ctrlPath = root + "/Controllers/" + name + ".controller";
  UnityEditor.AssetDatabase.DeleteAsset(ctrlPath);
  var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
  ctrl.AddParameter("Speed", UnityEngine.AnimatorControllerParameterType.Float);
  UnityEditor.Animations.BlendTree tree;
  var state = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
  tree.blendParameter = "Speed"; tree.blendType = UnityEditor.Animations.BlendTreeType.Simple1D; tree.useAutomaticThresholds = false;
  tree.AddChild(byName["Idle"], 0f); tree.AddChild(byName["Walk"], 1.5f);
  if (byName.ContainsKey("Gallop")) tree.AddChild(byName["Gallop"], 6f);
  var model = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(p);
  var go = (UnityEngine.GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(model);
  UnityEditor.PrefabUtility.UnpackPrefabInstance(go, UnityEditor.PrefabUnpackMode.Completely, UnityEditor.InteractionMode.AutomatedAction);
  go.name = "Animal_" + name;
  var anim = go.GetComponent<UnityEngine.Animator>();
  if (anim == null) anim = go.AddComponent<UnityEngine.Animator>();
  anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false;
  anim.cullingMode = UnityEngine.AnimatorCullingMode.CullUpdateTransforms;
  var b = new UnityEngine.Bounds(go.transform.position, UnityEngine.Vector3.zero);
  foreach (var r in go.GetComponentsInChildren<UnityEngine.Renderer>()) b.Encapsulate(r.bounds);
  var prefPath = root + "/Prefabs/Animal_" + name + ".prefab";
  UnityEditor.PrefabUtility.SaveAsPrefabAsset(go, prefPath);
  UnityEngine.Object.DestroyImmediate(go);
  sb.Append(prefPath).Append(" size=").Append(b.size.ToString("F2")).Append(" clips=").Append(string.Join(",", byName.Keys)).AppendLine();
}
UnityEditor.AssetDatabase.SaveAssets();
return sb.ToString();
