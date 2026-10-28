using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>Cross-log reading: the Fixture, Tower and DtSweep logs print each other's rows (moved out of ControllerProbe64.cs to keep it under 500 lines).</summary>
    public partial class ControllerProbe64
    {
        /// <summary>The Tower method's CSV rows (from its log, if present): columns by header name.</summary>
        List<Dictionary<string, string>> ReadCsv(string method)
        {
            string path = LogDir + "controller-probe-64-" + method + ".csv";
            var rows = new List<Dictionary<string, string>>();
            if (!File.Exists(path)) return rows;
            var lines = File.ReadAllLines(path); if (lines.Length < 2) return rows;
            var head = lines[0].Split(',');
            foreach (var l in lines.Skip(1)) { var c = l.Split(','); if (c.Length != head.Length) continue; var d = new Dictionary<string, string>(); for (int i = 0; i < head.Length; i++) d[head[i]] = c[i]; rows.Add(d); }
            return rows;
        }

        /// <summary>Matching 1/60 Tower row for a sweep row: kept_path and stops, or "-" when the Tower log is absent.</summary>
        string TowerRef(ProbeRun s)
        {
            var hit = ReadCsv("Probe64_Tower").FirstOrDefault(d => d["case"] == "T1" && d["flight"] == s.flight && d["dir"] == s.dir && d["side"] == s.side && d["yaw_off"] == F(s.yawOff, 0));
            return hit == null ? "-" : $"kept_path {hit["kept_path"]} stops {hit["stops"]}";
        }

        /// <summary>TowerStep: the matching Probe64_Tower row (same id and identity) from its log: stops, kept_path, rise_max and the through: detail line; "-" when the log is absent.</summary>
        string TowerRowSummary(ProbeResult r)
        {
            var row = ReadCsv("Probe64_Tower").FirstOrDefault(d => d["id"] == r.id.ToString() && d["case"] == r.run.caseName && d["flight"] == r.run.flight && d["dir"] == r.run.dir && d["side"] == r.run.side);
            if (row == null) return "-";
            string through = "-";
            string txt = LogDir + "controller-probe-64-Probe64_Tower.txt";
            if (File.Exists(txt))
            {
                var lines = File.ReadAllLines(txt); string key = r.id + " | ";
                int at = System.Array.FindIndex(lines, l => l.StartsWith(key));
                if (at >= 0) for (int i = at + 1; i < lines.Length && lines[i].StartsWith(" "); i++) if (lines[i].TrimStart().StartsWith("through:")) { through = lines[i].Trim(); break; }
            }
            return $"stops {row["stops"]} kept_path {row["kept_path"]} rise_max {row["rise_max"]} flags {row["flags"]}; {through}";
        }

        void SideBySide()
        {
            var tower = ReadCsv("Probe64_Tower");
            Emit("F5 vs F5t vs T1 F1 (tower log " + (tower.Count > 0 ? "present" : "absent") + "): dir theta: stops / kept_path");
            foreach (string dir in new[] { "up", "down" })
                foreach (float th in new[] { 0f, 10f, 20f, 30f })
                {
                    string Cell(string cs) { var r = results.FirstOrDefault(x => x.run.caseName == cs && x.run.dir == dir && x.run.yawOff == th); return r == null || r.invalid != null ? "-" : $"{r.stops}/{F(r.keptPath, 2)}"; }
                    var t1 = tower.FirstOrDefault(d => d["case"] == "T1" && d["flight"] == "1" && d["dir"] == dir && d["yaw_off"] == F(th, 0) && (th == 0f || d["side"] == "R"));
                    Emit($"  {dir} {th:F0}: F5 {Cell("F5")}  F5t {Cell("F5t")}  T1F1 {(t1 == null ? "-" : t1["stops"] + "/" + t1["kept_path"])}");
                }
        }
    }
}
