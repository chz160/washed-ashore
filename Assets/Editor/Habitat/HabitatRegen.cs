using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// R1: regenerates the wildlife and bird habitats on the current terrain in one step (part of qa-2's AF1 chain).
// Wildlife first (NavMesh bake over the habitat window + seeded population + verify), then birds (plan + verify);
// both save World.unity. Report: TestResults/habitat-regen.txt.
//   Unity.exe -batchmode -quit -projectPath <p> -executeMethod HabitatRegen.RunCli
//   or, in an open Editor: unity command eval --detach "return HabitatRegen.Run();"
public static class HabitatRegen
{
    const string ScenePath = "Assets/Scenes/World.unity";

    public static string Run()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string wildlife = WashedAshore.Wildlife.Level.WildlifePlacer.Run(WashedAshore.Wildlife.Level.WildlifePlacer.SceneSeed);
        string birds = WashedAshore.Birds.Editor.BirdPlacer.Run(WashedAshore.Birds.Editor.BirdPlacer.SceneSeed);
        string birdVerify = WashedAshore.Birds.Editor.BirdPlacer.Verify();
        string report = $"WILDLIFE\n{wildlife}\n\nBIRDS\n{birds}\n\nBIRDS VERIFY\n{birdVerify}\n";
        string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults", "habitat-regen.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, report);
        return report;
    }

    public static void RunCli() => Debug.Log(Run());
}
