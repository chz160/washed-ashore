using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using WashedAshore.Fish.Editor;

namespace WashedAshore.Tests.EditMode.FishRendering
{
    /// <summary>
    /// Slot #17: World.unity shipped FishSurfaceFx with no FishRenderSet, so the game drew no surface sign and nothing failed.
    /// f-td: one test for the whole bug class. Every serialized object reference on every fish component (any script in a
    /// WashedAshore.Fish* assembly) in an enabled build scene must be assigned, and both fish renderers must point at
    /// FishRenderSet.asset. Reads the scene YAML as text: no scene is opened, so nothing can be saved.
    /// </summary>
    public class FishSceneWiringTests
    {
        static readonly Regex Ref = new Regex(@"^\s*-?\s*(\w+): \{fileID: (-?\d+)(?:, guid: (\w+))?", RegexOptions.Multiline);

        static IEnumerable<(string scene, string type, string doc)> FishComponents()
        {
            foreach (var scene in EditorBuildSettings.scenes.Where(s => s.enabled))
                foreach (var doc in Regex.Split(File.ReadAllText(scene.path), @"^--- ", RegexOptions.Multiline))
                {
                    var id = Regex.Match(doc, @"m_EditorClassIdentifier: (WashedAshore\.Fish[\w.]*)::([\w.]+)");
                    if (id.Success) yield return (scene.path, id.Groups[2].Value, doc.Substring(id.Index + id.Length));
                }
        }

        [Test]
        public void EveryFishComponent_HasAllItsReferencesAssigned()
        {
            var comps = FishComponents().ToList();
            Assert.IsNotEmpty(comps, "no fish components in any enabled build scene");
            var missing = new List<string>();
            foreach (var (scene, type, fields) in comps)
                foreach (Match m in Ref.Matches(fields))
                    if (m.Groups[2].Value == "0") missing.Add($"{scene}: {type}.{m.Groups[1].Value} is unassigned (fileID 0)");
            Assert.IsEmpty(missing, string.Join("\n", missing));
        }

        [TestCase("WashedAshore.Fish.Rendering.FishSurfaceFx")]
        [TestCase("WashedAshore.Fish.Rendering.FishRenderer")]
        public void FishRenderers_ReferenceTheRenderSet(string type)
        {
            string setGuid = AssetDatabase.AssetPathToGUID(FishVatBake.RenderSetPath);
            Assert.IsNotEmpty(setGuid, FishVatBake.RenderSetPath);
            var comps = FishComponents().Where(c => c.type == type).ToList();
            Assert.IsNotEmpty(comps, $"no {type} in any enabled build scene");
            foreach (var (scene, _, fields) in comps)
            {
                var set = Regex.Match(fields, @"^\s*set: \{fileID: (-?\d+)(?:, guid: (\w+))?", RegexOptions.Multiline);
                Assert.IsTrue(set.Success, $"{scene}: {type} has no serialized set field");
                Assert.AreEqual(setGuid, set.Groups[2].Value, $"{scene}: {type}.set is not {FishVatBake.RenderSetPath}: nothing it owns will be drawn");
            }
        }
    }
}
