using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace WashedAshore.Perf.Editor
{
    /// <summary>
    /// Issue #9 measurement players (task #26). Same scenes and options as BellsBendWindowsBuild (Assets/Editor/Art), into
    /// pod folders so the existing Builds/Windows-BellsBend evidence stays untouched:
    ///   release: Builds/Windows-BellsBend-frustum9/WashedAshorePOC.exe      (the timed runs)
    ///   dev:     Builds/Windows-BellsBend-frustum9-dev/WashedAshorePOC.exe  (optional counter pass only; never timed)
    /// Each writes Builds/&lt;dir&gt;.provenance.json with the commit, the dirty files and the exe SHA-256.
    ///   Unity.exe -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod WashedAshore.Perf.Editor.Frustum9Build.BuildBatch -logFile TestResults/9-build.log
    ///   Unity.exe -batchmode -nographics -projectPath . -buildTarget Win64 -executeMethod WashedAshore.Perf.Editor.Frustum9Build.BuildDevBatch -logFile TestResults/9-build-dev.log
    /// Exit code 0 built, 1 failed.
    /// </summary>
    public static class Frustum9Build
    {
        const string Dir = "Builds/Windows-BellsBend-frustum9", DevDir = "Builds/Windows-BellsBend-frustum9-dev", Exe = "WashedAshorePOC.exe";

        public static void BuildBatch() => Exit(Build(false));
        public static void BuildDevBatch() => Exit(Build(true));

        static void Exit(bool ok) { if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1); }

        static string Git(string root, string args)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("git", args) { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return o.Trim();
            }
            catch (System.Exception e) { return "git failed: " + e.Message; }
        }

        static string J(string s) => "\"" + FrustumMath.Esc(s) + "\"";

        public static bool Build(bool dev)
        {
            var root = Path.GetDirectoryName(Application.dataPath);
            var dir = dev ? DevDir : Dir;
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            string commit = Git(root, "rev-parse HEAD"), branch = Git(root, "rev-parse --abbrev-ref HEAD");
            var dirty = Git(root, "status --porcelain").Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
            var started = System.DateTime.UtcNow;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(root, dir, Exe),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            });
            var s = report.summary;
            bool ok = s.result == BuildResult.Succeeded;
            string sha = "";
            var exePath = Path.Combine(root, dir, Exe);
            if (ok && File.Exists(exePath))
                using (var h = System.Security.Cryptography.SHA256.Create())
                using (var f = File.OpenRead(exePath))
                    sha = string.Concat(h.ComputeHash(f).Select(b => b.ToString("x2")));
            // The exe is a stub; the code is in the managed DLLs, so their hashes identify the harness build.
            var perfDll = Path.Combine(root, dir, "WashedAshorePOC_Data", "Managed", "WashedAshore.Perf.dll");
            string perfSha = "";
            if (ok && File.Exists(perfDll))
                using (var h = System.Security.Cryptography.SHA256.Create())
                using (var f = File.OpenRead(perfDll))
                    perfSha = string.Concat(h.ComputeHash(f).Select(b => b.ToString("x2")));
            var notes = report.steps.SelectMany(st => st.messages).Where(m => m.type == LogType.Warning || m.type == LogType.Error)
                .Select(m => J(m.type + ": " + m.content.Replace("\n", " ").Replace("\r", "")));
            var json = "{" +
                $"\"issue\":9,\"task\":26,\"result\":{J(s.result.ToString())},\"errors\":{s.totalErrors},\"warnings\":{s.totalWarnings},\"size_bytes\":{s.totalSize}," +
                $"\"started_utc\":{J(started.ToString("O"))},\"ended_utc\":{J(System.DateTime.UtcNow.ToString("O"))},\"output\":{J(dir + "/" + Exe)}," +
                $"\"exe_sha256\":{J(sha)},\"perf_dll_sha256\":{J(perfSha)},\"development\":{(dev ? "true" : "false")},\"unity\":{J(Application.unityVersion)}," +
                $"\"commit\":{J(commit)},\"branch\":{J(branch)},\"dirty_files\":[{string.Join(",", dirty.Select(J))}]," +
                $"\"scenes\":[{string.Join(",", scenes.Select(J))}],\"messages\":[{string.Join(",", notes)}]}}";
            File.WriteAllText(Path.Combine(root, dir + ".provenance.json"), json + "\n");
            Debug.Log($"[Frustum9Build] {(ok ? "OK" : "FAILED")} {dir} sha256={sha}");
            return ok;
        }
    }
}
