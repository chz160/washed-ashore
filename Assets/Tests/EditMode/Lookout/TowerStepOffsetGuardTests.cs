using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.Lookout.EditMode
{
    /// <summary>
    /// #41 guard (td-46 adr/41-stair-step-acceptance): the tower prefab serializes TowerWalkZones.upperStepOffset, and WalkableTowerBuilder adds
    /// a fresh TowerWalkZones without setting it (so a rebuild takes the C# default). The two must agree, or the live tower and a rebuilt one
    /// differ. The prefab text is read as it is on disk (no builder run, no asset import side effects).
    /// </summary>
    public class TowerStepOffsetGuardTests
    {
        const string TowerPrefab = "Assets/World/Lookout/LookoutTower.prefab";
        static readonly Regex Field = new Regex(@"(?m)^\s*upperStepOffset:\s*([-+0-9.eE]+)\s*$");

        static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        /// <summary>The upperStepOffset values a prefab text serializes (one per TowerWalkZones).</summary>
        static float[] PrefabValues(string prefabText)
        {
            var found = new System.Collections.Generic.List<float>();
            foreach (Match m in Field.Matches(prefabText)) found.Add(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            return found.ToArray();
        }

        static float CodeDefault()
        {
            var go = new GameObject("TowerStepOffsetGuard");
            try { return go.AddComponent<TowerWalkZones>().upperStepOffset; }
            finally { Object.DestroyImmediate(go); }
        }

        static void AssertMatchesDefault(string prefabText, string label)
        {
            var values = PrefabValues(prefabText);
            Assert.AreEqual(1, values.Length, $"{label}: expected exactly one serialized upperStepOffset, found {values.Length}");
            float want = CodeDefault();
            Assert.AreEqual(want, values[0], 1e-6f, $"{label}: the prefab serializes upperStepOffset {values[0]} but the C# default is {want}");
        }

        [Test]
        public void TowerPrefab_UpperStepOffset_EqualsTheCodeDefault()
        {
            AssertMatchesDefault(File.ReadAllText(Path.Combine(ProjectRoot, TowerPrefab)), "LookoutTower.prefab");
        }

        /// <summary>
        /// Known-fail control: a scratch copy of the prefab with the old value 0.05 (written under Logs/, never under Assets/) must FAIL the same
        /// check, and print the two values. Run on its own.
        /// </summary>
        [Test, Explicit("Known-fail: must FAIL"), Category("KnownFail41")]
        public void KnownFail_ScratchPrefabCopyAt005()
        {
            string text = File.ReadAllText(Path.Combine(ProjectRoot, TowerPrefab));
            string scratch = Field.Replace(text, "  upperStepOffset: 0.05");
            string dir = Path.Combine(ProjectRoot, "Logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "TowerStepOffsetGuard-scratch.prefab");
            File.WriteAllText(path, scratch);
            AssertMatchesDefault(File.ReadAllText(path), "scratch copy at 0.05");
        }
    }
}
