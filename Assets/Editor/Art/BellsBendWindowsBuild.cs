using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Technical-artist R2 tool: release Windows build from the live Editor into Builds/Windows-BellsBend/
// (the birds-pod evidence in Builds/Windows stays untouched). Writes Builds/Windows-BellsBend.provenance.json.
// Run via: unity command eval --detach "return BellsBendWindowsBuild.Build();"
// or batchmode: Unity.exe -batchmode -projectPath . -buildTarget Win64 -executeMethod BellsBendWindowsBuild.BuildBatch -logFile Builds/build-windows-bellsbend.log
public static class BellsBendWindowsBuild
{
    const string Dir = "Builds/Windows-BellsBend";
    const string DevDir = "Builds/Windows-BellsBend-dev";
    const string Exe = "WashedAshorePOC.exe";

    public static string Switch()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64) return "already StandaloneWindows64";
        bool ok = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
        return $"switched={ok} active={EditorUserBuildSettings.activeBuildTarget}";
    }

    public static void BuildBatch() => Build();

    /// <summary>Development build for the H9 feel check (the F9 debug camera exists only here): Builds/Windows-BellsBend-dev/, its own provenance file with "development":true. The release BuildBatch is untouched.</summary>
    public static void BuildDevBatch() => Build(true);

    public static string Build() => Build(false);

    static string Build(bool dev)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var root = Path.GetDirectoryName(Application.dataPath);
        var started = System.DateTime.UtcNow;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(root, dev ? DevDir : Dir, Exe),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = dev ? BuildOptions.Development : BuildOptions.None,
        });
        var s = report.summary;
        var notes = report.steps.SelectMany(st => st.messages)
            .Where(m => m.type == LogType.Warning || m.type == LogType.Error)
            .Select(m => $"\"{m.type}: {m.content.Replace("\\", "/").Replace("\"", "'").Replace("\n", " ").Replace("\r", "")}\"");
        var json = "{" +
            $"\"result\":\"{s.result}\",\"errors\":{s.totalErrors},\"warnings\":{s.totalWarnings},\"size_bytes\":{s.totalSize}," +
            $"\"started_utc\":\"{started:O}\",\"ended_utc\":\"{System.DateTime.UtcNow:O}\",\"output\":\"{(dev ? DevDir : Dir)}/{Exe}\"," +
            $"\"scenes\":[{string.Join(",", scenes.Select(x => $"\"{x}\""))}],\"development\":{(dev ? "true" : "false")},\"messages\":[{string.Join(",", notes)}]}}";
        File.WriteAllText(Path.Combine(root, dev ? "Builds/Windows-BellsBend-dev.provenance.json" : "Builds/Windows-BellsBend.provenance.json"), json);
        // -executeMethod in batchmode: make the process exit code follow the build result.
        if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        return json;
    }
}
