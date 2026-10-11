using System.IO;
using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.Fish.Editor;
using WashedAshore.Fish.Rendering;

namespace WashedAshore.Tests.EditMode.FishRendering
{
    /// <summary>
    /// f-td: Shape() is pinned to its pre-refactor numbers (the fade law and legacy radius never change); the F17 physical
    /// footprint shape follows R = core + speed x age, capped; and sign-legibility.json always round-trips as valid JSON.
    /// </summary>
    public class FishSignShapeTests
    {
        [TestCase(0f, 0.25f, 0.525f, 0.5625f)]
        [TestCase(0f, 1f, 0.9f, 0f)]
        [TestCase(0f, 0f, 0.15f, 1f)]
        [TestCase(1f, 0.5f, 1f, 0.25f)] // wake keeps its full shape
        public void Shape_PinnedToPreRefactorValues(float mode, float t, float radius, float strength)
        {
            FishSurfaceFx.Shape(mode, t, out float r, out float s);
            Assert.AreEqual(radius, r, 1e-5f);
            Assert.AreEqual(strength, s, 1e-5f, "fade law (1 - t)^2 unchanged");
        }

        [Test]
        public void Footprint_RingFrontMovesAtRingSpeed_CappedAtMaxRadius_FadeLawUnchanged()
        {
            var f = new FishRenderSet.Footprint { kind = FishSurfaceKind.Rise, coreRadius = 0.1f, ringSpeed = 0.25f, maxRingRadius = 1.5f, lifeSec = new Vector2(6f, 6f) };
            var look = FishSurfaceFx.Look(FishSurfaceKind.Rise);
            float quad = FishSurfaceFx.FootprintQuad(f, look, 0.5f);
            Assert.AreEqual(2f * 1.5f * 1.15f, quad, 1e-5f);
            FishSurfaceFx.ShapeFootprint(f, look.mode, 2f, 2f / 6f, quad, out float r, out float s);
            Assert.AreEqual(0.6f, r * quad * 0.5f, 1e-4f, "0.1 + 0.25 x 2 s");
            Assert.AreEqual((1f - 2f / 6f) * (1f - 2f / 6f), s, 1e-5f);
            FishSurfaceFx.ShapeFootprint(f, look.mode, 10f, 1f, quad, out r, out _);
            Assert.AreEqual(1.5f, r * quad * 0.5f, 1e-4f, "capped at maxRingRadius");
            Assert.AreEqual(6f, FishSurfaceFx.FootprintLife(f, 0.5f), 1e-5f);
        }

        [TestCase(0f, 147.5f)]
        [TestCase(27.7f, 147.5f)] // the slot E bluff case: 150.1 m in 3D, inside flat
        [TestCase(500f, 150f)]    // on the radius, any height
        public void EventRadius_IsFlat_SignWithinRadiusDrawsAtAnyEyeHeight(float eyeHeight, float flat)
        {
            var eye = new Vector3(10f, eyeHeight, 20f);
            var sign = eye + new Vector3(flat * 0.6f, -eyeHeight, flat * 0.8f);
            Assert.IsTrue(FishSurfaceFx.InEventRadius(sign, eye, 150f), $"flat {flat} m, eye {eyeHeight} m up");
            Assert.IsFalse(FishSurfaceFx.InEventRadius(eye + new Vector3(0f, -eyeHeight, 150.5f), eye, 150f), "beyond the radius flat");
        }

        [Test]
        public void FootprintHash_IsCanonical_LayoutFreeAndValueSensitive()
        {
            // f-td (a): same content in another key order and layout hashes the same; any changed value hashes differently.
            const string a = @"{""briefRevision"":""X"",""surfaceSigns"":{""footprints"":[{""kind"":""Rise"",""coreRadius"":0.1,""ringSpeed"":0.23,""lifeSec"":[2,4],""provenance"":{""ringSpeed"":""s""}}],""footprintsNote"":""n""}}";
            const string b = @"{ ""surfaceSigns"" : { ""footprintsNote"" : ""other"",
                ""footprints"" : [ { ""provenance"" : { ""ringSpeed"" : ""s"" }, ""lifeSec"" : [ 2.0, 4 ], ""ringSpeed"" : 0.23, ""coreRadius"" : 1e-1, ""kind"" : ""Rise"" } ] },
                ""briefRevision"" : ""Y"" }";
            const string c = @"{""surfaceSigns"":{""footprints"":[{""kind"":""Rise"",""coreRadius"":0.1,""ringSpeed"":0.23,""lifeSec"":[2,5],""provenance"":{""ringSpeed"":""s""}}]}}";
            Assert.AreEqual(FishFootprintHash.OfJson(a), FishFootprintHash.OfJson(b), "reordered keys / whitespace / number spelling / non-footprint fields must not matter");
            Assert.AreNotEqual(FishFootprintHash.OfJson(a), FishFootprintHash.OfJson(c), "a changed lifeSec bound must change the hash");
        }

        [Test]
        public void ShippedFootprints_EqualAFreshImportOfTheRecord()
        {
            // f-td: the guard must read the brief of record (fish-targets.json), never the draft. While the record has no
            // footprints the result is Inconclusive (not a pass) and names the source the asset actually carries.
            var set = UnityEditor.AssetDatabase.LoadAssetAtPath<FishRenderSet>(FishVatBake.RenderSetPath);
            if (!FishVatBake.RecordHasFootprints(out string revision))
                Assert.Inconclusive($"the brief of record ({revision}) has no surfaceSigns.footprints; the shipped asset carries {set.footprintsSource}");
            var fresh = FishVatBake.ReadFootprints(out string report, out _);
            // Bound to the record's footprint CONTENT (path + canonical footprints sha + values), not the whole brief file:
            // a revision that changes no footprint needs no re-import (#23, f-td / f-qa).
            StringAssert.StartsWith(FishLooks.TargetsPath + " ", set.footprintsSource, "the asset was not imported from the record: rerun FishVatBake.UpdateFootprints");
            Assert.AreEqual(FishFootprintHash.Of(FishLooks.TargetsPath), set.footprintsSha, "the record's footprints changed since the import: rerun FishVatBake.UpdateFootprints");
            Assert.IsNotEmpty(set.footprintsRev, "footprintsRev not recorded");
            Assert.AreEqual(fresh.Count, set.footprints.Length, report);
            for (int i = 0; i < fresh.Count; i++)
                Assert.AreEqual(UnityEngine.JsonUtility.ToJson(fresh[i]), UnityEngine.JsonUtility.ToJson(set.footprints[i]), $"footprint {i}: rerun FishVatBake.UpdateFootprints\n{report}");
        }

        [Test]
        public void ShippedFootprints_EqualAFreshImportOfTheirNamedSource()
        {
            // Draft or record: the asset must equal what its source file's footprints import to today (content, not file sha).
            var set = UnityEditor.AssetDatabase.LoadAssetAtPath<FishRenderSet>(FishVatBake.RenderSetPath);
            var fresh = FishVatBake.ReadFootprints(out string report, out string source);
            string path = source.Substring(0, source.IndexOf(' '));
            StringAssert.StartsWith(path + " ", set.footprintsSource, "the asset was imported from another file: rerun FishVatBake.UpdateFootprints");
            Assert.AreEqual(FishFootprintHash.Of(path), set.footprintsSha, "that file's footprints changed since the import: rerun FishVatBake.UpdateFootprints");
            Assert.AreEqual(fresh.Count, set.footprints.Length, report);
            for (int i = 0; i < fresh.Count; i++)
                Assert.AreEqual(JsonUtility.ToJson(fresh[i]), JsonUtility.ToJson(set.footprints[i]), $"footprint {i}\n{report}");
        }

        [Test]
        public void LegibilityTable_WritesAndParsesAsValidJson()
        {
            var t = new FishLookDev.LegTable { waterY = 2.5f, ringWidth = 0.13f };
            t.shots.Add(new FishLookDev.LegShotRecord { kind = "Rise", eye = "shore", distM = 30f, age = 0.25f, minorAxisPx = 2.5f, weberCrest = 0.031f });
            t.shots[0].crest.Add(new FishLookDev.LegPoint { x = 960, y = 540 });
            string json = JsonUtility.ToJson(t, true);
            StringAssert.DoesNotContain("F3", json);
            var back = JsonUtility.FromJson<FishLookDev.LegTable>(json);
            Assert.AreEqual(1, back.shots.Count);
            Assert.AreEqual(0.031f, back.shots[0].weberCrest, 1e-6f);
            Assert.AreEqual(960, back.shots[0].crest[0].x);
            // The shipped file, if a run has produced it, must parse too.
            string shipped = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults", "fish-lookdev", "sign-legibility.json");
            if (File.Exists(shipped)) Assert.DoesNotThrow(() => JsonUtility.FromJson<FishLookDev.LegTable>(File.ReadAllText(shipped)));
        }
    }
}
