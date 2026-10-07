using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

namespace WashedAshore.Tests.EditMode.Level
{
    /// <summary>
    /// Water pod W1-W3 (td-note §3 checklist). Reads the built World scene; build first with
    /// BellsBendWater.Build(). Guard tests feed deliberately wrong inputs and never change the scene.
    /// </summary>
    public class WaterBuildTests
    {
        // Spec terrain extent (W2), independent of the manifest the build reads.
        const float SpecMinX = -2087f, SpecMaxX = 2009f, SpecMinZ = -2492f, SpecMaxZ = 2628f;

        MapConfig cfg;
        BellsBendData.Manifest m;
        GameObject root;
        MeshRenderer[] quads;

        [OneTimeSetUp]
        public void OpenWorld()
        {
            if (SceneManager.GetActiveScene().path != BellsBendLevel.ScenePath)
                EditorSceneManager.OpenScene(BellsBendLevel.ScenePath, OpenSceneMode.Single);
            cfg = BellsBendData.LoadConfig();
            m = BellsBendData.LoadManifest(cfg);
            root = GameObject.Find(BellsBendWater.RootName);
            Assert.IsNotNull(root, "No Water root; run BellsBendWater.Build()");
            quads = root.GetComponentsInChildren<MeshRenderer>();
        }

        [Test]
        public void W1_PlaneYEqualsWaterLevelY()
        {
            Assert.AreEqual(m.tilesX * m.tilesZ, quads.Length, "one quad per tile");
            foreach (var r in quads)
            {
                Assert.AreEqual(cfg.WaterLevelY, r.transform.position.y, 1e-5f, r.name);
                Assert.AreEqual(Vector3.one, r.transform.lossyScale, r.name);
                Assert.AreEqual(Quaternion.identity, r.transform.rotation, r.name);
                var b = r.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.AreEqual(0f, b.min.y, 0f, r.name + " mesh not flat at local y 0");
                Assert.AreEqual(0f, b.max.y, 0f, r.name + " mesh not flat at local y 0");
                Assert.AreEqual(cfg.WaterLevelY, r.bounds.center.y, 1e-4f, r.name + " world bounds");
            }
            Assert.AreEqual(cfg.WaterLevelY, root.GetComponent<BellsBendWaterInfo>().waterLevelY, 1e-5f);
        }

        [Test]
        public void W1_WaterBodyLevelEqualsWaterLevelY()
        {
            var type = System.Type.GetType("WashedAshore.World.WaterBody, WashedAshore.World");
            if (type == null) Assert.Inconclusive("WaterBody type not landed yet (w-engineer)");
            var bodies = Object.FindObjectsByType(type);
            Assert.AreEqual(1, bodies.Length, "exactly one WaterBody in the scene");
            var body = (Component)bodies[0];
            Assert.AreSame(root, body.gameObject, "WaterBody is on the Water root");
            Assert.AreEqual(cfg.WaterLevelY, (float)type.GetProperty("Level").GetValue(body), 1e-5f);
        }

        /// <summary>W1 grep: the water height is never typed into code or shaders.</summary>
        [Test]
        public void W1_NoLiteralWaterHeight()
        {
            var project = BellsBendData.ProjectRoot;
            var literal = cfg.WaterLevelY.ToString("0.00", CultureInfo.InvariantCulture);
            var rx = new Regex($@"(?<![\w.]){Regex.Escape(literal)}0*(?![\d])"); // trailing zeros count too
            var code = new[] { ".cs", ".shader", ".hlsl", ".cginc", ".py" };
            var yaml = new[] { ".mat", ".asset" }; // text-serialized only; binary TerrainData tiles are skipped
            var hits = new List<string>();
            foreach (var dir in new[] { "Assets", "tools" })
                foreach (var f in Directory.EnumerateFiles(Path.Combine(project, dir), "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (!code.Contains(ext) && !(yaml.Contains(ext) && IsYaml(f))) continue;
                    var lines = File.ReadAllLines(f);
                    for (int i = 0; i < lines.Length; i++)
                        if (rx.IsMatch(lines[i])) hits.Add($"{f.Substring(project.Length + 1)}:{i + 1}: {lines[i].Trim()}");
                }
            Assert.IsEmpty(hits, $"literal water height {literal} found:\n" + string.Join("\n", hits));
        }

        static bool IsYaml(string path)
        {
            var head = new byte[5];
            using (var fs = File.OpenRead(path)) if (fs.Read(head, 0, 5) < 5) return false;
            return System.Text.Encoding.ASCII.GetString(head) == "%YAML";
        }

        [Test]
        public void W2_QuadsAreWaterLayerNoColliderNoShadows()
        {
            int water = WorldLayers.Water;
            Assert.AreEqual(WorldLayers.BuiltInWater, water, "built-in layer 4 is Water");
            Assert.AreEqual(water, root.layer);
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>(), "water quads carry no colliders");
            var names = new HashSet<string>();
            foreach (var r in quads)
            {
                Assert.AreEqual(water, r.gameObject.layer, r.name);
                Assert.AreEqual(ShadowCastingMode.Off, r.shadowCastingMode, r.name);
                Assert.IsFalse(r.receiveShadows, r.name);
                Assert.AreEqual(BellsBendWater.MaterialPath, UnityEditor.AssetDatabase.GetAssetPath(r.sharedMaterial), r.name);
                Assert.IsTrue(names.Add(r.name), "duplicate " + r.name);
            }
            foreach (var t in m.tiles) Assert.IsTrue(names.Contains($"Water_x{t.ix}_z{t.iz}"), $"no quad for tile x{t.ix}_z{t.iz}");
        }

        Rect[] Rects() => quads.Select(r => { var b = r.bounds; return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z); }).ToArray();

        static bool Covered(Rect[] rects, float x, float z)
        {
            foreach (var r in rects) if (x >= r.xMin && x <= r.xMax && z >= r.yMin && z <= r.yMax) return true;
            return false;
        }

        /// <summary>W2: every 2 m sample of the spec extent, plus every seam line at 0.25 m, lies on a quad.</summary>
        [Test]
        public void W2_CoverageScanWholeExtent()
        {
            var rects = Rects();
            Assert.AreEqual(SpecMinX, rects.Min(r => r.xMin), 1e-3f); Assert.AreEqual(SpecMaxX, rects.Max(r => r.xMax), 1e-3f);
            Assert.AreEqual(SpecMinZ, rects.Min(r => r.yMin), 1e-3f); Assert.AreEqual(SpecMaxZ, rects.Max(r => r.yMax), 1e-3f);
            var gaps = new List<Vector2>(); long n = 0;
            void Probe(float x, float z) { n++; if (!Covered(rects, x, z) && gaps.Count < 20) gaps.Add(new Vector2(x, z)); }
            for (float z = SpecMinZ; z <= SpecMaxZ; z += 2f)
                for (float x = SpecMinX; x <= SpecMaxX; x += 2f) Probe(x, z);
            for (int i = 1; i < m.tilesX; i++)
                for (float z = SpecMinZ; z <= SpecMaxZ; z += 0.25f) { float x = m.gridOrigin.x + i * m.tileSize; Probe(x - 1e-3f, z); Probe(x, z); Probe(x + 1e-3f, z); }
            for (int j = 1; j < m.tilesZ; j++)
                for (float x = SpecMinX; x <= SpecMaxX; x += 0.25f) { float z = m.gridOrigin.y + j * m.tileSize; Probe(x, z - 1e-3f); Probe(x, z); Probe(x, z + 1e-3f); }
            TestContext.WriteLine($"W2 coverage: {n} samples, {gaps.Count} uncovered");
            Assert.IsEmpty(gaps, "uncovered: " + string.Join(" ", gaps));
        }

        /// <summary>W2: neighbouring quads share their edge exactly (no gap, no overlap) and sit at the same Y.</summary>
        [Test]
        public void W2_SeamsShareEdgesExactly()
        {
            var byName = quads.ToDictionary(r => r.name, r => r.bounds);
            float worst = 0f; int pairs = 0;
            for (int iz = 0; iz < m.tilesZ; iz++)
                for (int ix = 0; ix < m.tilesX; ix++)
                {
                    var a = byName[$"Water_x{ix}_z{iz}"];
                    if (ix + 1 < m.tilesX)
                    {
                        var e = byName[$"Water_x{ix + 1}_z{iz}"]; pairs++;
                        worst = Mathf.Max(worst, Mathf.Abs(a.max.x - e.min.x), Mathf.Abs(a.min.z - e.min.z), Mathf.Abs(a.max.z - e.max.z), Mathf.Abs(a.center.y - e.center.y));
                    }
                    if (iz + 1 < m.tilesZ)
                    {
                        var e = byName[$"Water_x{ix}_z{iz + 1}"]; pairs++;
                        worst = Mathf.Max(worst, Mathf.Abs(a.max.z - e.min.z), Mathf.Abs(a.min.x - e.min.x), Mathf.Abs(a.max.x - e.max.x), Mathf.Abs(a.center.y - e.center.y));
                    }
                }
            TestContext.WriteLine($"W2 seams: {pairs} shared edges, worst mismatch {worst} m");
            Assert.AreEqual((m.tilesX - 1) * m.tilesZ + m.tilesX * (m.tilesZ - 1), pairs);
            Assert.AreEqual(0f, worst, 1e-4f);
        }

        /// <summary>W2: the river carved north of the line (terrain below W) is under water too.</summary>
        [Test]
        public void W2_NorthOfLineRiverCovered()
        {
            Assert.IsNotEmpty(Terrain.activeTerrains, "No terrain tiles in World");
            var rects = Rects(); int wet = 0, dry = 0;
            for (float z = cfg.northLineZ + 4f; z <= SpecMaxZ; z += 8f)
                for (float x = SpecMinX; x <= SpecMaxX; x += 8f)
                {
                    if (!TerrainQuery.TryGroundHeight(new Vector3(x, 0f, z), out float h) || h >= cfg.WaterLevelY) continue;
                    wet++;
                    if (!Covered(rects, x, z)) dry++;
                }
            TestContext.WriteLine($"W2 north of line: {wet} below-water samples, {dry} uncovered");
            Assert.Greater(wet, 0, "expected river carve north of the line");
            Assert.AreEqual(0, dry);
        }

        /// <summary>W3 for the built state: the saved scene's water matches the terrain on disk now (catches a terrain
        /// rebuild nobody followed with a water rebuild).</summary>
        [Test]
        public void W3_BuiltWaterMatchesCurrentTerrain()
        {
            var info = root.GetComponent<BellsBendWaterInfo>();
            Assert.IsNotNull(info, "Water root has no BellsBendWaterInfo");
            Assert.AreEqual("8f8a637cd4657d82", BellsBendWater.ApprovedLakeMask, "approved hash (terrain rc4) changed");
            Assert.AreEqual(BellsBendWater.ApprovedLakeMask, info.lakeMaskSha16, "water was built against another lake mask");
            Assert.AreEqual(info.lakeMaskSha16, BellsBendWater.ReportLakeMask(BellsBendWater.LoadReport(cfg)),
                "terrain on disk differs from the one the water was built against; rerun BellsBendWater.Build()");
            Assert.AreEqual(cfg.WaterLevelY, info.waterLevelY, 1e-5f, "MapConfig water level changed since the water build");
        }

        [Test]
        public void W3_GuardPassesOnCurrentTerrain()
        {
            Assert.DoesNotThrow(() => BellsBendWater.Guard(cfg, m, BellsBendWater.LoadReport(cfg)));
        }

        /// <summary>The build throws WaterBuildException and leaves the existing Water root untouched.</summary>
        void AssertBuildFails(MapConfig c, Dictionary<string, object> report, string expect)
        {
            var before = GameObject.Find(BellsBendWater.RootName);
            int quadsBefore = before.transform.childCount;
            var e = Assert.Throws<WaterBuildException>(() => BellsBendWater.Build(c, m, report, false));
            StringAssert.Contains(expect, e.Message);
            var roots = SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == BellsBendWater.RootName).ToArray();
            Assert.AreEqual(1, roots.Length, "failed build created a Water root");
            Assert.AreSame(before, roots[0], "failed build replaced the Water root");
            Assert.AreEqual(quadsBefore, before.transform.childCount, "failed build changed the quads");
        }

        [Test]
        public void W3_LakeMaskMismatchFailsBuild()
        {
            var report = BellsBendWater.LoadReport(cfg);
            report["lakeMaskSha256"] = "0123456789abcdef" + ((string)report["lakeMaskSha256"]).Substring(16);
            AssertBuildFails(cfg, report, "lake mask");
        }

        [Test]
        public void W3_MissingLakeMaskFailsBuild()
        {
            var report = BellsBendWater.LoadReport(cfg);
            report.Remove("lakeMaskSha256");
            AssertBuildFails(cfg, report, "lake mask missing");
        }

        [Test]
        public void W3_ReportSaysLineMovedFailsBuild()
        {
            var report = BellsBendWater.LoadReport(cfg);
            ((Dictionary<string, object>)report["northLineMoved"])["moved"] = true;
            AssertBuildFails(cfg, report, "north line moved");
        }

        [TestCase(1.5f)]
        [TestCase(-1.5f)]
        [TestCase(30f)]
        public void W3_MovedNorthLineFailsBuild(float shift)
        {
            var moved = Object.Instantiate(cfg);
            try
            {
                moved.northLineZ = cfg.northLineZ + shift;
                var report = BellsBendWater.LoadReport(cfg);
                Assert.IsFalse((bool)((Dictionary<string, object>)report["northLineMoved"])["moved"], "control: report flag is false");
                AssertBuildFails(moved, report, "north line moved");
            }
            finally { Object.DestroyImmediate(moved); }
        }

        /// <summary>W3: the full level build stops with a hard error on a moved line, before touching tiles or the scene.</summary>
        [Test]
        public void W3_BuildAllThrowsOnMovedLine()
        {
            var tileDir = Path.Combine(BellsBendData.ProjectRoot, BellsBendData.WorldRoot, "Tiles");
            var stamps = Directory.GetFiles(tileDir, "*.asset").ToDictionary(f => f, File.GetLastWriteTimeUtc);
            Assert.IsNotEmpty(stamps, "no tile assets to watch");
            var before = GameObject.Find(BellsBendWater.RootName);
            int quadsBefore = before.transform.childCount;
            var moved = Object.Instantiate(cfg);
            try
            {
                moved.northLineZ = cfg.northLineZ + 30f;
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new Regex("north line moved"));
                var e = Assert.Throws<WaterBuildException>(() => BellsBendLevel.BuildAll(moved, false));
                StringAssert.Contains("north line moved", e.Message);
            }
            finally { Object.DestroyImmediate(moved); }
            var roots = SceneManager.GetActiveScene().GetRootGameObjects().Where(g => g.name == BellsBendWater.RootName).ToArray();
            Assert.AreEqual(1, roots.Length);
            Assert.AreSame(before, roots[0], "BuildAll replaced the Water root");
            Assert.AreEqual(quadsBefore, before.transform.childCount);
            foreach (var kv in stamps) Assert.AreEqual(kv.Value, File.GetLastWriteTimeUtc(kv.Key), "tile asset rewritten: " + kv.Key);
        }

        static string FileSha(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }

        public static void StubHookThatThrows() => throw new System.InvalidOperationException("stub hook failure");

        /// <summary>w-td (b): a throwing WaterBody/WaterMaps hook surfaces as WaterBuildException with the original inside.</summary>
        [Test]
        public void W3_HookFailureIsWaterBuildException()
        {
            var e = Assert.Throws<WaterBuildException>(() => BellsBendWater.Invoke(typeof(WaterBuildTests).GetMethod(nameof(StubHookThatThrows)), null));
            StringAssert.Contains("water hook WaterBuildTests.StubHookThatThrows failed: stub hook failure", e.Message);
            Assert.IsInstanceOf<System.InvalidOperationException>(e.InnerException);
        }

        /// <summary>A failure after the guard (e.g. a WaterBody/WaterMaps hook throwing) surfaces as WaterBuildException,
        /// so BuildAll fails hard. Injects a broken manifest, then reloads the saved scene (nothing is saved).</summary>
        [Test]
        public void W3_PostGuardFailureIsWaterBuildException()
        {
            var broken = new BellsBendData.Manifest
            {
                dir = m.dir, tileSize = m.tileSize, res = m.res, tilesX = m.tilesX, tilesZ = m.tilesZ, gridOrigin = m.gridOrigin,
                northLineZData = m.northLineZData, waterLevelY = m.waterLevelY, tiles = null,
            };
            var scenePath = Path.Combine(BellsBendData.ProjectRoot, BellsBendLevel.ScenePath);
            var sceneBefore = FileSha(scenePath);
            try
            {
                var e = Assert.Throws<WaterBuildException>(() => BellsBendWater.Build(cfg, broken, BellsBendWater.LoadReport(cfg), false));
                StringAssert.Contains("build step failed", e.Message);
                Assert.IsNotNull(e.InnerException, "original error kept as InnerException");
                Assert.AreEqual(sceneBefore, FileSha(scenePath), "failed build saved World.unity");
            }
            finally
            {
                EditorSceneManager.OpenScene(BellsBendLevel.ScenePath, OpenSceneMode.Single);
                root = GameObject.Find(BellsBendWater.RootName);
                quads = root.GetComponentsInChildren<MeshRenderer>();
            }
        }
    }
}
