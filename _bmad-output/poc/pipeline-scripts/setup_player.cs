var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
if (scene.path != "Assets/Scenes/World.unity")
    scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/World.unity");

var spawn = UnityEngine.GameObject.Find("PlayerSpawn");
if (spawn == null) return "ERROR: PlayerSpawn not found";
var pcType = System.Type.GetType("WashedAshore.Gameplay.PlayerController, WashedAshore.Gameplay");
if (pcType == null) return "ERROR: PlayerController type not compiled";

var oldCam = UnityEngine.GameObject.Find("Main Camera");
if (oldCam != null) UnityEngine.Object.DestroyImmediate(oldCam);
var existing = UnityEngine.GameObject.Find("Player");
if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

var player = new UnityEngine.GameObject("Player");
player.tag = "Player";
var terrain = UnityEngine.Terrain.activeTerrain;
var pos = spawn.transform.position;
if (terrain != null) pos.y = terrain.SampleHeight(pos) + terrain.transform.position.y + 0.5f;
player.transform.SetPositionAndRotation(pos, UnityEngine.Quaternion.Euler(0f, spawn.transform.eulerAngles.y, 0f));

var cc = player.AddComponent<UnityEngine.CharacterController>();
cc.height = 1.8f; cc.radius = 0.4f; cc.center = new UnityEngine.Vector3(0f, 0.9f, 0f);
cc.stepOffset = 0.4f; cc.slopeLimit = 45f; cc.skinWidth = 0.05f; cc.minMoveDistance = 0f;

var pivot = new UnityEngine.GameObject("CameraPivot");
pivot.transform.SetParent(player.transform, false);
pivot.transform.localPosition = new UnityEngine.Vector3(0f, 1.65f, 0f);
pivot.tag = "MainCamera";
var cam = pivot.AddComponent<UnityEngine.Camera>();
cam.fieldOfView = 70f; cam.nearClipPlane = 0.1f; cam.farClipPlane = 1000f;
pivot.AddComponent<UnityEngine.AudioListener>();

var pc = player.AddComponent(pcType);
var so = new UnityEditor.SerializedObject(pc);
so.FindProperty("cameraPivot").objectReferenceValue = pivot.transform;
so.FindProperty("spawnPoint").objectReferenceValue = spawn.transform;
so.ApplyModifiedPropertiesWithoutUndo();

UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
bool saved = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
var roots = new System.Collections.Generic.List<string>();
foreach (var go in scene.GetRootGameObjects()) roots.Add(go.name);
return "saved=" + saved + " playerPos=" + pos + " yaw=" + spawn.transform.eulerAngles.y + " terrain=" + (terrain != null ? terrain.name : "none") + " roots=[" + string.Join(",", roots) + "]";
