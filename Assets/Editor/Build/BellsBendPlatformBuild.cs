using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Batchmode release builds for Linux and macOS, modelled on BellsBendWindowsBuild.
// Run (no -buildTarget; the method checks the module itself and fails with a clear message):
//   Unity.exe -batchmode -nographics -projectPath . -executeMethod BellsBendLinuxBuild.BuildBatch  -logFile Builds/build-linux.log
//   Unity.exe -batchmode -nographics -projectPath . -executeMethod BellsBendMacBuild.BuildBatch    -logFile Builds/build-mac.log
// Output: Builds/Linux/WashedAshore.x86_64 and Builds/Mac/WashedAshore.app (what release-windows.ps1 packages),
// plus Builds/<Dir>.provenance.json. Exit codes: 0 built, 1 build failed, 2 Build Support module not installed.
public static class BellsBendLinuxBuild
{
    public static void BuildBatch() => BellsBendPlatformBuild.Run(BuildTarget.StandaloneLinux64, "Linux", "WashedAshore.x86_64", "Linux Build Support (Mono)");
    public static string Build() => BellsBendPlatformBuild.Run(BuildTarget.StandaloneLinux64, "Linux", "WashedAshore.x86_64", "Linux Build Support (Mono)");
}

// macOS: unsigned Mono player, universal (Intel + Apple Silicon) when the module exposes the architecture setting.
// The architecture setting lives in the Mac module's assembly, so it is set by reflection (this file must compile
// without the module). The outcome is written to the provenance JSON ("macos_architecture").
public static class BellsBendMacBuild
{
    public static void BuildBatch() => Build();

    public static string Build() => BellsBendPlatformBuild.Run(BuildTarget.StandaloneOSX, "Mac", "WashedAshore.app", "Mac Build Support (Mono)",
        SetUniversal, "tools/release/README-macOS.txt");

    static string SetUniversal()
    {
        try
        {
            var t = System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.OSXStandalone.UserBuildSettings")).FirstOrDefault(x => x != null);
            var p = t?.GetProperty("architecture");
            if (p == null) return "default (UserBuildSettings.architecture not found; not universal)";
            var names = System.Enum.GetNames(p.PropertyType);
            var want = names.FirstOrDefault(n => n.ToLower() == "x64arm64");
            if (want == null) return "default (no universal option in " + string.Join("/", names) + ")";
            p.SetValue(null, System.Enum.Parse(p.PropertyType, want));
            return "universal (" + want + ")";
        }
        catch (System.Exception e) { return "default (could not set universal: " + e.Message.Replace("\"", "'") + ")"; }
    }
}

static class BellsBendPlatformBuild
{
    internal static string Run(BuildTarget target, string dirName, string output, string moduleName,
        System.Func<string> prepare = null, string extraFile = null)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target))
        {
            var msg = $"BUILD BLOCKED: the {target} player cannot be built because the '{moduleName}' module is not installed in this Unity Editor. " +
                      "Install it from Unity Hub (Installs > Add modules), then run again. No build was attempted.";
            Debug.LogError(msg);
            if (Application.isBatchMode) EditorApplication.Exit(2);
            return msg;
        }

        var arch = prepare?.Invoke();
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var root = Path.GetDirectoryName(Application.dataPath);
        var started = System.DateTime.UtcNow;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(root, "Builds", dirName, output),
            target = target,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        });
        var s = report.summary;
        if (extraFile != null && s.result == BuildResult.Succeeded && File.Exists(Path.Combine(root, extraFile)))
            File.Copy(Path.Combine(root, extraFile), Path.Combine(root, "Builds", dirName, Path.GetFileName(extraFile)), true);
        var notes = report.steps.SelectMany(st => st.messages)
            .Where(m => m.type == LogType.Warning || m.type == LogType.Error)
            .Select(m => $"\"{m.type}: {m.content.Replace("\\", "/").Replace("\"", "'").Replace("\n", " ").Replace("\r", "")}\"");
        var archJson = arch == null ? "" : ",\"macos_architecture\":\"" + arch + "\"";
        var json = "{" +
            $"\"result\":\"{s.result}\",\"target\":\"{target}\",\"errors\":{s.totalErrors},\"warnings\":{s.totalWarnings},\"size_bytes\":{s.totalSize}," +
            $"\"started_utc\":\"{started:O}\",\"ended_utc\":\"{System.DateTime.UtcNow:O}\",\"output\":\"Builds/{dirName}/{output}\"," +
            $"\"scenes\":[{string.Join(",", scenes.Select(x => $"\"{x}\""))}],\"development\":false,\"scripting_backend\":\"Mono\"{archJson},\"messages\":[{string.Join(",", notes)}]}}";
        File.WriteAllText(Path.Combine(root, $"Builds/{dirName}.provenance.json"), json);
        if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        return json;
    }
}
