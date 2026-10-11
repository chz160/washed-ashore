using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>
    /// The shipped fish data: the tuning imported from fish-targets.json passes Validate against f-artist's measured
    /// bodies and the shipped waves' amplitude (one source for the waves: WaterMotion.asset, adr/fish-1 review 3 N4),
    /// and the import is reproducible from the JSON.
    /// </summary>
    public class FishShippedAssetsTests
    {
        const string WaterMotionPath = "Assets/World/Water/WaterMotion.asset";

        [Test]
        public void ShippedTuning_PassesValidate_WithBodiesAndShippedWaves()
        {
            var t = AssetDatabase.LoadAssetAtPath<FishTuning>(FishTuningImport.AssetPath);
            var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(FishTuningImport.BodiesPath);
            var waves = AssetDatabase.LoadAssetAtPath<WaterMotionSettings>(WaterMotionPath);
            Assert.IsNotNull(t, FishTuningImport.AssetPath);
            Assert.IsNotNull(bodies, FishTuningImport.BodiesPath);
            Assert.IsNotNull(waves, WaterMotionPath);
            Assert.IsNull(t.Validate(bodies, WaterMotion.MaxAmplitude(waves.waves)));
            // F16 fields reach the asset, not defaults (f-producer): far-water rate on the open band, reaction turn, multiplier.
            Assert.Greater(t.bands[t.bands.Length - 1].farSignsPerM2PerMin, 0f, "far-water rate not imported");
            Assert.IsTrue(t.FarWaterSigns, "far-water signs off");
            Assert.Greater(t.scatter.reactionTurnRate, 0f, "reaction turn rate not imported");
            Assert.AreEqual(1f, t.surfaceEventMultiplier, 1e-6f, "band-rate multiplier");
        }

        [Test]
        public void ShippedTuning_MatchesAFreshImportOfTheJson()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<FishTuning>(FishTuningImport.AssetPath);
            var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(FishTuningImport.BodiesPath);
            byte[] bytes = System.IO.File.ReadAllBytes(FishTuningImport.JsonPath);
            var src = JsonUtility.FromJson<FishTuningImport.Targets>(System.Text.Encoding.UTF8.GetString(bytes));
            Assert.AreEqual(FishTuningImport.Sha16(bytes), shipped.briefSha16, $"shipped tuning is from another brief file (now {src.briefRevision}); rerun the import");
            var fresh = ScriptableObject.CreateInstance<FishTuning>();
            fresh.name = shipped.name;
            FishTuningImport.Fill(fresh, src, bodies);
            fresh.briefSha16 = shipped.briefSha16;
            Assert.AreEqual(EditorJsonUtility.ToJson(fresh), EditorJsonUtility.ToJson(shipped), "rerun Washed Ashore/Fish/Import Fish Tuning");
            Object.DestroyImmediate(fresh);
        }
    }
}
