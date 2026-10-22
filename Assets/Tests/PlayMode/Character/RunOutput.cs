using System;
using System.IO;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// Every run writes into its own folder, TestResults/player-character/runs/RUN/SUB, so a later run never overwrites or deletes an earlier run's evidence.
    /// RUN is the PC_RUN environment variable when set, else the start time of the run (chosen once per run).
    /// </summary>
    public static class RunOutput
    {
        static string run;

        public static string RunName => run ??= Environment.GetEnvironmentVariable("PC_RUN") is string r && r.Length > 0 ? r : DateTime.Now.ToString("yyyyMMdd-HHmmss");

        public static string Dir(string sub)
        {
            string d = $"TestResults/player-character/runs/{RunName}/{sub}";
            Directory.CreateDirectory(d);
            return d;
        }
    }
}
