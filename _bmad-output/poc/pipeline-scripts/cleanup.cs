System.IO.Directory.CreateDirectory(@"E:\GitHub\washed-ashore\_staging\screenshots");
System.IO.File.Copy(@"E:\GitHub\washed-ashore\Assets\Screenshots\level_spawn.png", @"E:\GitHub\washed-ashore\_staging\screenshots\level_spawn.png", true);
bool del = UnityEditor.AssetDatabase.DeleteAsset("Assets/Screenshots");
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
if (scene.isDirty) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
return "deletedScreenshotsFolder=" + del + " scene=" + scene.path + " dirty=" + scene.isDirty;
