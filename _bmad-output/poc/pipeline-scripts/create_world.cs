var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
    UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects,
    UnityEditor.SceneManagement.NewSceneMode.Single);
bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/Scenes/World.unity");
bool deletedSample = UnityEditor.AssetDatabase.DeleteAsset("Assets/Scenes/SampleScene.unity");
UnityEditor.EditorBuildSettings.scenes = new[] { new UnityEditor.EditorBuildSettingsScene("Assets/Scenes/World.unity", true) };
UnityEditor.PlayerSettings.SplashScreen.show = false;
UnityEditor.AssetDatabase.SaveAssets();
var names = new System.Collections.Generic.List<string>();
foreach (var go in scene.GetRootGameObjects()) names.Add(go.name);
var bs = new System.Collections.Generic.List<string>();
foreach (var s in UnityEditor.EditorBuildSettings.scenes) bs.Add(s.path + ":" + s.enabled);
return "saved=" + saved + " deletedSample=" + deletedSample + " buildScenes=[" + string.Join(",", bs) + "] roots=[" + string.Join(",", names) + "] splash=" + UnityEditor.PlayerSettings.SplashScreen.show + " activeInput=" + UnityEditor.EditorUserBuildSettings.activeBuildTarget;
