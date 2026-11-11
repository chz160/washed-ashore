using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Gameplay;

// Technical-artist tool (#21 / #20): creates Assets/Settings/BushSlowdownTuning.asset with the values of
// docs/design/bush-slowdown.md section 4 (N_L from section 7), checks each field against that table, then links the asset
// to the player's PlayerController.bushTuning in Assets/Scenes/World.unity through SerializedObject.
// Run: Unity -batchmode -quit -executeMethod BushTuningAssetSetup.Run
public static class BushTuningAssetSetup
{
    const string AssetPath = "Assets/Settings/BushSlowdownTuning.asset";
    const string ScenePath = "Assets/Scenes/World.unity";

    // name, slows, M_walk, M_sprint, N_L, max height (spec section 4 and 7)
    static readonly (string n, bool slows, float walk, float sprint, int full, float h)[] Spec =
    {
        ("Broomsedge", false, 1.00f, 1.00f, 1, 1.1f),
        ("Briar", true, 0.70f, 0.50f, 1, 1.6f),
        ("ShrubThicket", true, 0.55f, 0.40f, 1, 2.6f),
        ("KudzuMound", true, 0.50f, 0.35f, 1, 3.2f),
        ("KudzuLeaf", true, 0.90f, 0.80f, 2, 0.9f),
        ("Fern", false, 1.00f, 1.00f, 1, 0.9f),
        ("CrackGrass", false, 1.00f, 1.00f, 1, 0.5f),
        ("BankGrass", false, 1.00f, 1.00f, 1, 1.1f),
    };

    public static void Run()
    {
        var log = new System.Text.StringBuilder();
        var tuning = AssetDatabase.LoadAssetAtPath<BushSlowdownTuning>(AssetPath);
        if (!tuning)
        {
            tuning = ScriptableObject.CreateInstance<BushSlowdownTuning>();
            AssetDatabase.CreateAsset(tuning, AssetPath);
        }
        tuning.layers = new BushSlowdownTuning.Layer[Spec.Length];
        for (int i = 0; i < Spec.Length; i++)
        {
            var s = Spec[i];
            tuning.layers[i] = new BushSlowdownTuning.Layer(s.n, s.slows, s.walk, s.sprint, s.full, s.h);
        }
        EditorUtility.SetDirty(tuning);
        AssetDatabase.SaveAssets();

        // Read back and compare every field against the table.
        var back = AssetDatabase.LoadAssetAtPath<BushSlowdownTuning>(AssetPath);
        int bad = 0;
        for (int i = 0; i < Spec.Length; i++)
        {
            var l = back.layers[i]; var s = Spec[i];
            bool ok = l.name == s.n && l.slows == s.slows && l.walkMultiplier == s.walk && l.sprintMultiplier == s.sprint && l.fullCount == s.full && l.maxHeight == s.h;
            if (!ok) bad++;
            log.AppendLine($"[{i}] {l.name} slows={l.slows} walk={l.walkMultiplier:F2} sprint={l.sprintMultiplier:F2} fullCount={l.fullCount} maxHeight={l.maxHeight:F1} {(ok ? "matches spec" : "MISMATCH")}");
        }
        log.AppendLine($"Validate(5,8) = {back.Validate(5f, 8f) ?? "null (rules hold)"}; mismatches={bad}");

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var players = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include);
        log.AppendLine($"PlayerController objects in World.unity: {players.Length}");
        if (players.Length == 1)
        {
            var so = new SerializedObject(players[0]);
            var prop = so.FindProperty("bushTuning");
            var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetPath);
            prop.objectReferenceValue = asset;
            log.AppendLine($"prop.type={prop.type} assetType={asset.GetType().FullName} refAfterAssign={(prop.objectReferenceValue ? prop.objectReferenceValue.name : "null")}");
            so.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine($"re-read ref={(new SerializedObject(players[0]).FindProperty("bushTuning").objectReferenceValue ? "set" : "null")}");
            log.AppendLine($"Linked {players[0].name}.PlayerController.bushTuning -> {AssetPath} in {ScenePath}");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        else log.AppendLine("NOT LINKED: expected exactly one PlayerController");
        File.WriteAllText("TestResults/21-bush-tuning-asset.txt", log.ToString());
    }
}
