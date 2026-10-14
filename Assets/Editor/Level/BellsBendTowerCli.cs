using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Level-designer: headless entry points for the lookout tower site, one per batchmode run:
//   Unity -batchmode -projectPath <p> -executeMethod BellsBendTowerCli.Site    (pad, marker, zones, road map; tree snapshots; art repaint; compare; P1 report)
//   Unity -batchmode -projectPath <p> -executeMethod BellsBendTowerCli.Place   (tower prefab + TowerBaseSpawn, P1 report)
//   Unity -batchmode -projectPath <p> -executeMethod BellsBendTowerCli.View    (P3 probe)
// Each writes TestResults/tower-level/cli-<name>.txt as it goes and exits 0, or 1 if a step failed.
public static class BellsBendTowerCli
{
    static void Run(string name, System.Func<System.Action<string>, bool> body)
    {
        var path = Path.Combine(BellsBendTowerSite.EvidenceDir, $"cli-{name}.txt");
        var log = new StringBuilder($"BellsBendTowerCli.{name} {System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
        void Say(string s) { log.AppendLine(s); log.AppendLine(); File.WriteAllText(path, log.ToString()); }
        bool ok = false;
        try { ok = body(Say); }
        catch (System.Exception e) { Say("EXCEPTION: " + e); }
        Say(ok ? "RESULT: OK" : "RESULT: FAILED");
        if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
    }

    /// <summary>Art's repaint lives in Assembly-CSharp-Editor, which this assembly can't reference: call it by name.</summary>
    static string Repaint()
    {
        var type = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("BellsBendGround")).FirstOrDefault(t => t != null);
        var method = type?.GetMethod("BuildTowerSite", System.Type.EmptyTypes);
        if (method == null) return "error: BellsBendGround.BuildTowerSite() not found";
        return (string)method.Invoke(null, null);
    }

    public static void Site() => Run("site", say =>
    {
        string build = BellsBendTowerSite.Build();
        say(build);
        if (!build.TrimEnd().EndsWith("done")) return false;
        say(BellsBendTowerDiff.Snapshot("before"));
        string paint = Repaint();
        say("repaint: " + paint);
        if (paint.StartsWith("error")) return false;
        say(BellsBendTowerDiff.Snapshot("after"));
        string cmp = BellsBendTowerDiff.Compare("before", "after");
        say(cmp);
        say(BellsBendTowerSite.Report());
        return cmp.Contains("\"pass\": true");
    });

    public static void Place() => Run("place", say =>
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BellsBendLevel.ScenePath);
        string r = BellsBendTowerSite.Place();
        say(r);
        say(BellsBendTowerSite.Report());
        return r.StartsWith("tower: instantiated") || r.StartsWith("tower: already placed");
    });

    public static void View() => Run("view", say =>
    {
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BellsBendLevel.ScenePath);
        say(BellsBendTowerView.Test());
        return true;
    });
}
