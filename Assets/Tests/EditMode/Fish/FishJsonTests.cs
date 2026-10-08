using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>
    /// The JSON format bug (literal "F1" in guard / calibration / legibility files): the FishJson gate, culture-proof
    /// numbers, a source check for the interpolation trap, and an audit that every fish result file on disk parses.
    /// </summary>
    public class FishJsonTests
    {
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("{\"a\":1,\"b\":[1.5,-2e-3,true,false,null],\"c\":{\"d\":\"x\\\"y\\u00e9\"}}")]
        [TestCase("  {\"a\" : 0.25 }  ")]
        public void Validate_AcceptsJson(string json) => Assert.IsTrue(FishJson.TryValidate(json, out string e), e);

        [TestCase("{\"x\":F1}")]           // the trap's output
        [TestCase("{\"x\":1.5F1}")]
        [TestCase("{\"a\":1,}")]
        [TestCase("[1,2,]")]
        [TestCase("{\"a\":1.}")]
        [TestCase("{\"a\":NaN}")]
        [TestCase("{\"a\":1,5}")]          // a comma decimal
        [TestCase("{\"a\":01}")]
        [TestCase("{\"a\":1}}")]
        [TestCase("{a:1}")]
        [TestCase("")]
        public void Validate_RejectsMalformed(string json) => Assert.IsFalse(FishJson.TryValidate(json, out _), json);

        [Test]
        public void WriteFile_RefusesMalformed_AndWritesNothing()
        {
            string path = Path.Combine(Path.GetTempPath(), "fishjson-test-" + System.Guid.NewGuid().ToString("N") + ".json");
            Assert.Throws<System.FormatException>(() => FishJson.WriteFile(path, "{\"x\":F3}"));
            Assert.IsFalse(File.Exists(path));
            FishJson.WriteFile(path, "{\"x\":" + FishJson.Num(1.25, 2) + ",\"s\":" + FishJson.Str("a\"b\n") + "}");
            Assert.IsTrue(FishJson.TryValidate(File.ReadAllText(path), out string e), e);
            File.Delete(path);
        }

        [Test]
        public void Num_IsInvariant_UnderACommaCulture()
        {
            var was = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.AreEqual("1.500", FishJson.Num(1.5));
                Assert.AreEqual("-0.25", FishJson.Num(-0.25, 2));
                Assert.AreEqual("null", FishJson.Num(double.NaN));
            }
            finally { CultureInfo.CurrentCulture = was; }
        }

        [Test]
        public void Lines_AreCheckedOneByOne()
        {
            Assert.IsTrue(FishJson.TryValidateLines("{\"a\":1}\r\n{\"b\":2}\n\n", out string e), e);
            Assert.IsFalse(FishJson.TryValidateLines("{\"a\":1}\n{\"b\":F1}\n", out e));
            StringAssert.Contains("line 2", e);
        }

        // A format specifier directly followed by "}}" in an interpolated string ("{x:F1}}}") is the trap: the braces
        // become part of the format and the number is lost. Comments are skipped (they quote the trap).
        static readonly Regex Trap = new Regex(@"\{[^{}""\s]+:[A-Za-z]\d{0,2}\}\}");

        static IEnumerable<string> FishSources()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var dirs = new[] { "Assets/Scripts/Fish", "Assets/Tests/EditMode/Fish", "Assets/Tests/PlayMode/Fish" };
            foreach (var d in dirs)
            {
                string full = Path.Combine(root, d);
                if (!Directory.Exists(full)) continue;
                foreach (var f in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories)) yield return f;
            }
            string perf = Path.Combine(root, "Assets/Scripts/Perf/BellsBendFpsWalk.cs");
            if (File.Exists(perf)) yield return perf;
        }

        [Test]
        public void Sources_HaveNoFormatThenDoubleBrace()
        {
            var hits = new List<string>();
            foreach (var f in FishSources())
            {
                var lines = File.ReadAllLines(f);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].TrimStart().StartsWith("//")) continue;
                    if (Trap.IsMatch(lines[i])) hits.Add($"{Path.GetFileName(f)}:{i + 1}: {lines[i].Trim()}");
                }
            }
            Assert.IsEmpty(hits, "format specifier followed by '}}' (format the number first, or use FishJson.Num):\n" + string.Join("\n", hits));
        }

        /// <summary>Audit of the evidence on disk: every TestResults fish*.json parses, and every line of fish*.jsonl.</summary>
        [Test]
        public void ResultFiles_OnDisk_AllParse()
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            if (!Directory.Exists(dir)) Assert.Ignore("no TestResults folder");
            var bad = new List<string>();
            int n = 0;
            foreach (var f in Directory.GetFiles(dir, "fish*.json*", SearchOption.TopDirectoryOnly))
            {
                n++;
                string text = File.ReadAllText(f);
                bool ok = f.EndsWith(".jsonl") ? FishJson.TryValidateLines(text, out string e) : FishJson.TryValidate(text, out e);
                if (!ok) bad.Add($"{Path.GetFileName(f)}: {e}");
            }
            Assert.IsEmpty(bad, $"{bad.Count} of {n} fish result files don't parse:\n" + string.Join("\n", bad));
        }
    }
}
