using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// #46 P4 (studio:engineering adr/46-p4-grep-scope): no runtime code finds "the" player by tag or by name. Reads every .cs file under
    /// Assets/Scripts (Editor folders and Perf/ excluded) and fails on any hit, naming file:line.
    /// </summary>
    public class PlayerLookupScanTests
    {
        const string PerfFolder = "Assets/Scripts/Perf/";
        static readonly Regex Pattern = new Regex(@"FindGameObjectWithTag|FindWithTag|FindGameObjectsWithTag|GameObject\.Find\(");

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>Every in-scope file as a project-relative path; <paramref name="skipPerf"/> false puts Perf/ back in (KF-C4).</summary>
        static List<string> Files(bool skipPerf)
        {
            var list = new List<string>();
            foreach (var full in Directory.GetFiles(Path.Combine(ProjectRoot, "Assets/Scripts"), "*.cs", SearchOption.AllDirectories))
            {
                string rel = full.Substring(ProjectRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                if (("/" + rel).Contains("/Editor/")) continue;
                if (skipPerf && rel.StartsWith(PerfFolder)) continue;
                list.Add(rel);
            }
            return list;
        }

        static List<string> Scan(bool skipPerf, out int fileCount)
        {
            var hits = new List<string>();
            var files = Files(skipPerf);
            fileCount = files.Count;
            foreach (var rel in files)
            {
                var lines = File.ReadAllLines(Path.Combine(ProjectRoot, rel));
                for (int i = 0; i < lines.Length; i++)
                    if (Pattern.IsMatch(lines[i])) hits.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
            }
            return hits;
        }

        [Test]
        public void RuntimeCode_HasNoPlayerLookupByTagOrName()
        {
            var hits = Scan(true, out int files);
            Assert.Greater(files, 100, "the scan read too few files; its root is wrong");
            Assert.IsEmpty(hits, $"{hits.Count} player lookup(s) in runtime code (scanned {files} files):\n" + string.Join("\n", hits));
        }

        /// <summary>KF-C4: the same scan with Perf/ put back must fail and name the Perf lines. It also shows the scanner reads that folder.</summary>
        [Test, Explicit("Known-fail: must FAIL"), Category("KnownFail46")]
        public void KnownFail_C4_ScanWithPerfIncluded_NamesPerfLines()
        {
            var hits = Scan(false, out int files);
            Assert.IsEmpty(hits, $"{hits.Count} lookup(s), scanned {files} files:\n" + string.Join("\n", hits));
        }

        /// <summary>adr/46-p4-grep-scope condition: no runtime assembly references the Perf assembly, so a player build can leave it out.</summary>
        [Test]
        public void NoAssemblyReferencesPerf()
        {
            string perfGuid = null;
            string perfMeta = Path.Combine(ProjectRoot, "Assets/Scripts/Perf/WashedAshore.Perf.asmdef.meta");
            Assert.IsTrue(File.Exists(perfMeta), "the Perf asmdef meta is missing");
            var m = Regex.Match(File.ReadAllText(perfMeta), @"guid: ([0-9a-f]{32})");
            if (m.Success) perfGuid = m.Groups[1].Value;
            var offenders = new List<string>();
            foreach (var full in Directory.GetFiles(Path.Combine(ProjectRoot, "Assets"), "*.asmdef", SearchOption.AllDirectories))
            {
                string rel = full.Substring(ProjectRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
                if (rel =="Assets/Scripts/Perf/WashedAshore.Perf.asmdef") continue;
                string text = File.ReadAllText(full);
                if (text.Contains("\"WashedAshore.Perf\"") || (perfGuid != null && text.Contains("GUID:" + perfGuid))) offenders.Add(rel);
            }
            Assert.IsEmpty(offenders, "assemblies that reference WashedAshore.Perf:\n" + string.Join("\n", offenders));
        }
    }
}
