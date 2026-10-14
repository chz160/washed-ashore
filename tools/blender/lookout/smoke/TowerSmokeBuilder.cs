using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WashedAshore.Lookout.Editor;

/// <summary>
/// Smoke project only, copied in when smoke_import.ps1 is given -Builder: runs the real TowerBuilder against the inert
/// stand-ins of SmokeStandIns.cs and reports how far it gets. An exception is a finding and is reported as text; nothing
/// is relaxed to get further. TowerSmoke.Report finds this class by name, so the importer-only form compiles without it.
/// </summary>
public static class TowerSmokeBuilder
{
    static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

    /// <summary>One JSON object: the single-build result, then the build-twice byte comparison.</summary>
    public static string Section()
    {
        string buildLog = "", buildError = "", stableLog = "", stableError = "";
        bool built = false, stable = false;
        try { buildLog = TowerBuilder.Build(); built = true; }
        catch (Exception e) { buildError = e.Message; }
        if (built)
        {
            try { stableLog = TowerBuilder.VerifyStableRebuild(); stable = true; }
            catch (Exception e) { stableError = e.Message; }
        }
        long bytes = File.Exists(TowerBuilder.PrefabPath) ? new FileInfo(TowerBuilder.PrefabPath).Length : -1;
        return "{\"heading\":\"builder with stand-ins, not the real rebuild check\"," +
               "\"labels\":\"Real TowerBuilder.cs, unchanged. LookoutManifest, LookoutGameplaySetup and LookoutTowerArt are inert smoke stand-ins: the gameplay step adds nothing, " +
               "materials are plain Standard materials and there is one LOD, so material and LOD results are the stand-ins'. VerifyStableRebuild here says nothing about " +
               "stability once the real gameplay setup and art are applied.\"," +
               $"\"build_completed\":{(built ? "true" : "false")},\"build_stopped_with\":{Q(buildError)},\"build_log\":{Q(buildLog)}," +
               $"\"rebuild_ran\":{(built ? "true" : "false")},\"prefab_byte_identical_across_two_builds\":{(stable ? "true" : "false")}," +
               $"\"rebuild_stopped_with\":{Q(stableError)},\"rebuild_log\":{Q(stableLog)},\"prefab_bytes\":{bytes}}}";
    }
}
