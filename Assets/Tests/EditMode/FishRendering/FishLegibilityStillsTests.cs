using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish.Editor;

namespace WashedAshore.Tests.EditMode.FishRendering
{
    /// <summary>
    /// Slot #15 shipped 715 sign stills identical to their twins. This re-checks the saved PNG pairs on their own (not the
    /// generator's numbers): each kind x eye control crop must differ from its twin by more than FishLookDev.LegControlMinPx
    /// px (f-qa gate b), every in-frame shot must have submitted its draw (gate a), and a row that claims changed pixels
    /// must have a pair that differs.
    /// </summary>
    public class FishLegibilityStillsTests
    {
        static string Dir => Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults", "fish-lookdev");

        static int ChangedPx(string a, string b)
        {
            var ta = new Texture2D(2, 2); var tb = new Texture2D(2, 2);
            try
            {
                Assert.IsTrue(ta.LoadImage(File.ReadAllBytes(Path.Combine(Dir, a))), a);
                Assert.IsTrue(tb.LoadImage(File.ReadAllBytes(Path.Combine(Dir, b))), b);
                Assert.AreEqual(ta.width, tb.width, a); Assert.AreEqual(ta.height, tb.height, a);
                var pa = ta.GetPixels32(); var pb = tb.GetPixels32();
                int n = 0;
                for (int i = 0; i < pa.Length; i++) if (Mathf.Abs(FishLookDev.LinearLuma(pa[i]) - FishLookDev.LinearLuma(pb[i])) >= 0.0015f) n++;
                return n;
            }
            finally { Object.DestroyImmediate(ta); Object.DestroyImmediate(tb); }
        }

        [Test]
        public void SavedSignStills_DifferFromTheirTwins()
        {
            string json = Path.Combine(Dir, "sign-legibility.json");
            if (!File.Exists(json)) Assert.Inconclusive("no sign-legibility.json: run FishLookDev.RunLegibilityBatch");
            var table = JsonUtility.FromJson<FishLookDev.LegTable>(File.ReadAllText(json));
            Assert.IsTrue(table.valid, "the generator marked the table invalid: " + table.invalidReason);
            var fails = new List<string>();
            foreach (var g in table.shots.Where(s => s.inFrame).GroupBy(s => (s.kind, s.eye)))
            {
                var control = g.OrderBy(s => s.distM).ThenBy(s => s.age).First();
                int n = ChangedPx(control.signFile, control.noSignFile);
                bool belowNOk = FishLookDev.LegBelowNAllowed(control.kind, control.eye) && control.submitted >= 1 && n > 0; // f-qa ruling (i)
                if (n <= FishLookDev.LegControlMinPx && !belowNOk) fails.Add($"control {control.signFile}: {n} px");
            }
            fails.AddRange(table.shots.Where(s => s.inFrame && s.submitted < 1).Select(s => $"{s.signFile}: no draw submitted"));
            foreach (var s in table.shots.Where(s => s.changedPx > 0))
                if (File.ReadAllBytes(Path.Combine(Dir, s.signFile)).SequenceEqual(File.ReadAllBytes(Path.Combine(Dir, s.noSignFile))))
                    fails.Add($"{s.signFile}: row says {s.changedPx} px changed, the saved pair is identical");
            Assert.IsEmpty(fails, string.Join("\n", fails.Take(20)));
        }
    }
}
