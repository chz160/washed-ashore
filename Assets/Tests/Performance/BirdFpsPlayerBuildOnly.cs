#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.TestTools;
using WashedAshore.Tests.Performance;

[assembly: TestPlayerBuildModifier(typeof(BirdFpsPlayerBuildOnly))]

namespace WashedAshore.Tests.Performance
{
    /// <summary>
    /// B8: lets the birds ON/OFF frame-time test player be built ahead of the console session without running it
    /// ("split build and run"). Off unless <see cref="Key"/> is set in SessionState, so every other test run, including
    /// the WL4-style TestRunnerApi run at the console, builds and auto-runs as before. When on, the player is written to
    /// Builds/BirdsFpsTestPlayer/ and, launched by hand, runs its tests and writes its results next to its Player.log.
    /// </summary>
    public class BirdFpsPlayerBuildOnly : ITestPlayerBuildModifier
    {
        public const string Key = "WashedAshore.Birds.FpsPlayerBuildOnly";
        public const string Folder = "Builds/BirdsFpsTestPlayer";

        public BuildPlayerOptions ModifyOptions(BuildPlayerOptions options)
        {
            if (!SessionState.GetBool(Key, false)) return options;
            options.options &= ~(BuildOptions.AutoRunPlayer | BuildOptions.ConnectToHost);
            string dir = Path.GetFullPath(Folder);
            Directory.CreateDirectory(dir);
            options.locationPathName = Path.Combine(dir, "BirdsFpsTestPlayer.exe");
            return options;
        }
    }
}
#endif
