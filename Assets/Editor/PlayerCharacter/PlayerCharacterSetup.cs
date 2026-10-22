using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Stance;

namespace WashedAshore.EditorTools
{
    /// <summary>
    /// Puts the default character on the Player in the open scene: a PlayerCharacterView with the default appearance asset
    /// and a FirstPersonBody. Rerunnable; a part already there is reused. After it, grep the scene YAML for "fileID: 0" on
    /// the view's appearance field. Batch: -executeMethod WashedAshore.EditorTools.PlayerCharacterSetup.ApplyBatch
    /// </summary>
    public static class PlayerCharacterSetup
    {
        public const string DefaultAppearancePath = "Assets/Art/Character/Appearances/Men_Farmer.asset";
        const string ScenePath = "Assets/Scenes/World.unity";

        [MenuItem("Washed Ashore/Character/Apply Default Body To Player")]
        public static void Apply()
        {
            var appearance = AssetDatabase.LoadAssetAtPath<CharacterAppearance>(DefaultAppearancePath);
            if (appearance == null) throw new InvalidOperationException($"No appearance at {DefaultAppearancePath}");
            var tagged = GameObject.FindGameObjectsWithTag("Player");
            if (tagged.Length != 1) throw new InvalidOperationException($"Expected one Player in the open scene, found {tagged.Length}");
            var go = tagged[0];
            var view = go.GetComponent<PlayerCharacterView>() ?? go.AddComponent<PlayerCharacterView>();
            var fp = go.GetComponent<FirstPersonBody>() ?? go.AddComponent<FirstPersonBody>();
            var so = new SerializedObject(view);
            so.FindProperty("appearance").objectReferenceValue = appearance;
            so.FindProperty("firstPersonBody").objectReferenceValue = fp;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(view);
            EditorUtility.SetDirty(fp);
            var scene = go.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException($"Could not save {scene.path}");
            Debug.Log($"PlayerCharacterSetup: default body ({appearance.name}) on '{go.name}' in {scene.path}.");
        }

        public static void ApplyBatch()
        {
            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Apply();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("PlayerCharacterSetup failed: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
