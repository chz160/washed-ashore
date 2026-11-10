using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Character.Appearance;
using WashedAshore.Character.Editor;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>
    /// Pod 49 item 5, baton 2b: the Survivor files import as ruled (td-49 DK2 and F10/F11, art-49 import standard): Normals = Import on both FBX, only the knee shapes,
    /// the exact shared bone set, head and body renderer names, textures to the standard, one URP Lit material on both parts, and no Git LFS pointer in the working tree.
    /// Every loop over renderers first asserts exactly one renderer per part, so none can pass on an empty list.
    /// </summary>
    public class SurvivorImportTests
    {
        static readonly string[] Fbx = { SurvivorAssetBuilder.BodyPath, SurvivorAssetBuilder.HeadPath };

        static SkinnedMeshRenderer[] Renderers(string path)
        {
            var all = Load(path).GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.AreEqual(1, all.Length, path + ": exactly one SkinnedMeshRenderer");
            return all;
        }

        static GameObject Load(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(go, path + " is not imported");
            return go;
        }

        [TestCaseSource(nameof(Fbx))]
        public void NormalsAreImportedNeverCalculatedAndTheRigIsGenericWithTheFourBoneLimit(string path)
        {
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            Assert.IsNotNull(mi, path);
            Assert.AreEqual(ModelImporterNormals.Import, mi.importNormals, path + ": Normals must be Import (Calculate brings the faceting back)");
            Assert.AreEqual(CharacterImportSettings.SurvivorBlendShapeNormals, mi.importBlendShapeNormals, path + ": blend-shape normals mode");
            Assert.AreEqual(ModelImporterAnimationType.Generic, mi.animationType, path);
            Assert.IsFalse(mi.importAnimation, path + ": a part file carries no clips");
            Assert.IsFalse(mi.weldVertices, path + ": welding would merge the authored seams");
            Assert.AreEqual(ModelImporterSkinWeights.Standard, mi.skinWeights, path);
            Assert.IsFalse(mi.optimizeGameObjects, path + ": the assembler finds bones by name");
        }

        [Test]
        public void OnlyTheBodyCarriesShapesAndTheyAreTheKneePairWithNoElbows()
        {
            var body = Renderers(SurvivorAssetBuilder.BodyPath);
            var names = Enumerable.Range(0, body[0].sharedMesh.blendShapeCount).Select(i => body[0].sharedMesh.GetBlendShapeName(i)).ToList();
            CollectionAssert.AreEquivalent(new[] { "Knee_L_90", "Knee_R_90" }, names.Where(n => n != "Basis"), "shapes on the body: " + string.Join(", ", names));
            Assert.IsTrue(AssetImporter.GetAtPath(SurvivorAssetBuilder.BodyPath) is ModelImporter mi && mi.importBlendShapes);
            foreach (var smr in Renderers(SurvivorAssetBuilder.HeadPath))
                Assert.AreEqual(0, smr.sharedMesh.blendShapeCount, "the head carries no shapes");
        }

        [TestCaseSource(nameof(Fbx))]
        public void TheArmatureHasExactlyTheSharedSkeletonBoneNames(string path)
        {
            var go = Load(path);
            var armature = go.transform.Find("CharacterArmature");
            Assert.IsNotNull(armature, path + ": no CharacterArmature node");
            var part = armature.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToList();
            var skeleton = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterAssetBuilder.SkeletonHumanPrefabPath);
            var shared = skeleton.GetComponentsInChildren<Transform>(true).Where(t => t != skeleton.transform).Select(t => t.name).ToList();
            Assert.AreEqual(part.Count, new System.Collections.Generic.HashSet<string>(part).Count, path + ": duplicate bone names");
            CollectionAssert.AreEquivalent(shared, part, path + ": the bone set must equal the skeleton's (FitsSkeleton compares sets)");
            foreach (var smr in Renderers(path))
            {
                Assert.IsNotNull(smr.rootBone, path + ": no root bone");
                foreach (var bone in smr.bones) Assert.IsTrue(part.Contains(bone.name), path + ": skinned to a bone outside the armature: " + bone.name);
                var mesh = smr.sharedMesh;
                var perVertex = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                Assert.AreEqual(mesh.vertexCount, perVertex.Length, path + ": a bone count per vertex");
                int at = 0;
                for (int v = 0; v < perVertex.Length; v++)
                {
                    Assert.LessOrEqual(perVertex[v], 4, $"{path}: vertex {v} has more than 4 bone weights");
                    float sum = 0f;
                    for (int k = 0; k < perVertex[v]; k++) sum += weights[at++].weight;
                    Assert.AreEqual(1f, sum, 1e-3f, $"{path}: vertex {v} weights do not sum to 1");
                }
            }
        }

        [Test]
        public void TheHeadRendererIsNamedHeadAndTheBodyRendererIsNot()
        {
            foreach (var smr in Renderers(SurvivorAssetBuilder.HeadPath))
                StringAssert.Contains("head", smr.name.ToLowerInvariant(), "the head renderer must match FirstPersonBody's head token");
            foreach (var smr in Renderers(SurvivorAssetBuilder.BodyPath))
                StringAssert.DoesNotContain("head", smr.name.ToLowerInvariant(), "the body renderer must not match the head token (it would be hidden in first person)");
        }

        [Test]
        public void TexturesAreImportedToTheStandard()
        {
            var bc = AssetImporter.GetAtPath(SurvivorAssetBuilder.BaseColorPath) as TextureImporter;
            var mg = AssetImporter.GetAtPath(SurvivorAssetBuilder.MetallicGlossPath) as TextureImporter;
            Assert.IsNotNull(bc, "base colour not imported");
            Assert.IsNotNull(mg, "metallic gloss not imported");
            Assert.IsTrue(bc.sRGBTexture, "base colour is sRGB");
            Assert.IsFalse(mg.sRGBTexture, "metallic gloss is linear");
            foreach (var ti in new[] { bc, mg })
            {
                Assert.IsTrue(ti.mipmapEnabled, ti.assetPath);
                Assert.AreEqual(FilterMode.Trilinear, ti.filterMode, ti.assetPath);
                Assert.IsFalse(ti.isReadable, ti.assetPath);
                var s = ti.GetPlatformTextureSettings("Standalone");
                Assert.IsTrue(s.overridden, ti.assetPath + ": Standalone override");
                Assert.AreEqual(TextureImporterFormat.BC7, s.format, ti.assetPath);
            }
            Assert.AreEqual(CharacterImportSettings.SurvivorBaseColorMaxSize, bc.GetPlatformTextureSettings("Standalone").maxTextureSize, "base colour size (4096 provisional, pending the A/B)");
            Assert.AreEqual(CharacterImportSettings.SurvivorMetallicGlossMaxSize, mg.GetPlatformTextureSettings("Standalone").maxTextureSize);
            Assert.AreEqual(TextureImporterAlphaSource.FromInput, mg.alphaSource, "the MetallicGloss alpha is smoothness");
            Assert.IsFalse(mg.alphaIsTransparency);
            Assert.AreEqual(8, bc.anisoLevel);
        }

        [Test]
        public void OneUrpLitMaterialWithBothMapsIsOnEveryRendererOfBothParts()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(SurvivorAssetBuilder.MaterialPath);
            Assert.IsNotNull(mat, "Survivor.mat is missing (run SurvivorAssetBuilder.BatchBuild)");
            Assert.AreEqual(SurvivorAssetBuilder.ShaderName, mat.shader.name);
            Assert.AreEqual(SurvivorAssetBuilder.BaseColorPath, AssetDatabase.GetAssetPath(mat.GetTexture("_BaseMap")));
            Assert.AreEqual(SurvivorAssetBuilder.MetallicGlossPath, AssetDatabase.GetAssetPath(mat.GetTexture("_MetallicGlossMap")));
            Assert.IsTrue(mat.IsKeywordEnabled("_METALLICSPECGLOSSMAP"));
            Assert.AreEqual(0f, mat.GetFloat("_SmoothnessTextureChannel"), "Smoothness Source = Metallic Alpha");
            Assert.AreEqual(1f, mat.GetFloat("_Smoothness"), 1e-4f);
            Assert.AreEqual(0f, mat.GetFloat("_Surface"), "opaque");
            Assert.AreEqual(2f, mat.GetFloat("_Cull"), "render face Front");
            Assert.IsNull(mat.GetTexture("_BumpMap"), "no normal map");
            foreach (var path in Fbx)
                foreach (var smr in Renderers(path))
                {
                    Assert.Greater(smr.sharedMaterials.Length, 0);
                    foreach (var m in smr.sharedMaterials) Assert.AreSame(mat, m, path + " " + smr.name + ": every material slot is Survivor.mat");
                }
        }

        [Test]
        public void TheKneeShapesCarryRealPositionDeltasAndTheNormalDeltasArePrinted()
        {
            var smr0 = Renderers(SurvivorAssetBuilder.BodyPath)[0];
            var mesh = smr0.sharedMesh;
            // The FBX has a x100 node scale and Unity imports the mesh vertices at 0.01 (Convert Units): mesh-local numbers are 1/100 of metres, and the renderer node (lossyScale 100)
            // restores them. The shape deltas are in the same local numbers as the vertices (measured: delta/height identical to Blender), so metres = local x lossyScale.
            float toMetres = smr0.transform.lossyScale.x;
            Assert.AreEqual(100f, toMetres, 1e-3f, "the part's renderer node scale (x100 in the FBX)");
            var lines = new System.Text.StringBuilder("Survivor knee shapes (frame 0, weight 100): shape | vertices with a delta | max |dV| m | max |dN|\n");
            foreach (var name in new[] { "Knee_L_90", "Knee_R_90" })
            {
                int index = mesh.GetBlendShapeIndex(name);
                Assert.GreaterOrEqual(index, 0, name);
                Assert.AreEqual(1, mesh.GetBlendShapeFrameCount(index), name + ": one frame");
                var dv = new Vector3[mesh.vertexCount];
                var dn = new Vector3[mesh.vertexCount];
                var dt = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(index, 0, dv, dn, dt);
                float maxLocal = dv.Max(v => v.magnitude), maxN = dn.Max(v => v.magnitude);
                float maxV = maxLocal * toMetres;
                int moved = dv.Count(v => v.sqrMagnitude > 1e-10f);
                lines.AppendLine($"{name} | {moved} | {maxV:F4} m (mesh-local {maxLocal:F6}, x{toMetres:F0}) | {maxN:F4}");
                float expected = name == "Knee_L_90" ? 0.03792f : 0.02983f;   // the re-skin report's correctives.<shape>.max_delta_mm (37.92 and 29.83 mm): Unity must read the authored size
                Assert.AreEqual(expected, maxV, expected * 0.02f, name + ": max position delta within 2% of the authored 37.92 / 29.83 mm (td-49)");
                Assert.Greater(moved, 100, name + ": a sparse but real shape");
            }
            Directory.CreateDirectory("TestResults/pod49");
            File.WriteAllText("TestResults/pod49/knee-shape-deltas.txt", lines.ToString());
            TestContext.Out.WriteLine(lines.ToString());
        }

        /// <summary>
        /// Pod 49, td-49 item 4 (final11a): the neck cap is DOUBLE-SIDED, 52 triangles per winding around one centre vertex, each with its exact reverse. Blender's importer silently drops a face that
        /// duplicates another with reversed winding, so this proves Unity's import keeps all 104. Constants are ta-49's FBX-direct figures: Survivor_Body has 23,739 triangles in final4a and
        /// 23,791 (= + 52) in final9a to final11a, 104 of them at the cap centre. The centre is found as the position shared by the most triangle corners, not by an index (Unity splits vertices).
        /// </summary>
        [Test]
        public void TheDoubleSidedNeckCapSurvivesImportAsOneHundredAndFourTrianglesWithTheirReverses()
        {
            var mesh = Renderers(SurvivorAssetBuilder.BodyPath)[0].sharedMesh;
            var v = mesh.vertices;
            var t = mesh.triangles;
            Assert.AreEqual(23791, t.Length / 3, "Survivor_Body triangle total (final4a 23,739 + 52 mirrored cap faces; ta-49, FBX-direct)");
            Vector3Int Q(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1e6f), Mathf.RoundToInt(p.y * 1e6f), Mathf.RoundToInt(p.z * 1e6f));
            var corners = new Dictionary<Vector3Int, int>();
            Vector3Int centre = default; int most = 0;
            for (int i = 0; i < t.Length; i++)
            {
                var k = Q(v[t[i]]);
                int n = corners.TryGetValue(k, out int c0) ? c0 + 1 : 1;
                corners[k] = n;
                if (n > most) { most = n; centre = k; }
            }
            Assert.AreEqual(104, most, "triangle corners at the cap centre position (the next busiest vertex is far lower)");
            var cap = new List<int>();   // first corner index of every triangle touching the centre
            for (int i = 0; i < t.Length; i += 3)
                if (Q(v[t[i]]) == centre || Q(v[t[i + 1]]) == centre || Q(v[t[i + 2]]) == centre) cap.Add(i);
            Assert.AreEqual(104, cap.Count, "cap triangles touching the centre");
            Vector3 Normal(int i) => Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            Vector3 n0 = Normal(cap[0]);
            int same = 0, opposite = 0;
            foreach (int i in cap) { if (Vector3.Dot(Normal(i), n0) > 0f) same++; else opposite++; }
            Assert.AreEqual(52, same, "cap triangles winding one way");
            Assert.AreEqual(52, opposite, "cap triangles winding the other way");
            string Key(int i)
            {
                var ks = new[] { Q(v[t[i]]), Q(v[t[i + 1]]), Q(v[t[i + 2]]) };
                System.Array.Sort(ks, (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y != b.y ? a.y.CompareTo(b.y) : a.z.CompareTo(b.z));
                return ks[0] + "|" + ks[1] + "|" + ks[2];
            }
            var byKey = new Dictionary<string, List<int>>();
            foreach (int i in cap) { var k = Key(i); if (!byKey.TryGetValue(k, out var l)) byKey[k] = l = new List<int>(); l.Add(i); }
            Assert.AreEqual(52, byKey.Count, "distinct cap triangles by vertex positions");
            foreach (var kv in byKey)
            {
                Assert.AreEqual(2, kv.Value.Count, "each cap triangle has exactly one mirror: " + kv.Key);
                Assert.Less(Vector3.Dot(Normal(kv.Value[0]), Normal(kv.Value[1])), 0f, "the mirror winds the opposite way: " + kv.Key);
            }
        }

        [Test]
        public void NoSurvivorFileIsAGitLfsPointer()
        {
            foreach (var path in new[] { SurvivorAssetBuilder.BodyPath, SurvivorAssetBuilder.HeadPath, SurvivorAssetBuilder.BaseColorPath, SurvivorAssetBuilder.MetallicGlossPath })
            {
                Assert.IsTrue(File.Exists(path), path);
                var info = new FileInfo(path);
                Assert.Greater(info.Length, 100 * 1024, path + ": too small to be the real file (" + info.Length + " bytes)");
                var head = new byte[40];
                using (var fs = File.OpenRead(path)) fs.Read(head, 0, head.Length);
                StringAssert.DoesNotStartWith("version https://git-lfs", System.Text.Encoding.ASCII.GetString(head), path + " is a bare LFS pointer");
            }
        }

        [TestCase("CharacterArmature|Walk.001", "Walk")]
        [TestCase("Walk.001", "Walk")]
        [TestCase("Idle_Neutral.012", "Idle_Neutral")]
        [TestCase("Walk", "Walk")]
        [TestCase("Jog_Full", "Jog_Full")]
        [TestCase("Walk.01", "Walk.01")]
        public void ShortNameStripsTheBlenderDuplicateSuffixOnly(string take, string expected)
        {
            Assert.AreEqual(expected, CharacterImportSettings.ShortName(take));
        }

        [Test]
        public void MenClipNamesAreUniqueAndNoneKeepsABlenderDuplicateSuffix()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterClipSet>(CharacterAssetBuilder.ClipSetPath(BodyType.Men));
            Assert.IsNotNull(set, "CharacterClips_Men.asset is missing");
            Assert.Greater(set.clips.Count, 0, "empty clip set");
            var names = set.clips.Select(c => c.name).ToList();
            var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dupes, "duplicate clip names: " + string.Join(", ", dupes));
            var suffixed = names.Where(n => System.Text.RegularExpressions.Regex.IsMatch(n, @"\.\d{3}$")).ToList();
            Assert.IsEmpty(suffixed, "clip names still carry a .NNN suffix: " + string.Join(", ", suffixed));
            foreach (var n in new[] { "Walk", "Run", "Run_Back", "Run_Left", "Run_Right", "Idle", "Idle_Neutral", "Interact", "Stand_Idle" })
                Assert.IsNotNull(set.Get(n), "clip '" + n + "' is missing");
        }
    }
}
