using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Character.Editor;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.EditMode.Character
{
    /// <summary>
    /// Forced cases F1 to F7 of the render-faithful Bar 1 rule (td-49 "BAR 1 ASSERT TEXT OF RECORD (R3)" B4, B8). Every case executes the intended clause by name and has a control that passes. The cases go through a small
    /// ray loop that is the same shape as the asserting loops (winner = nearest hit under MirrorTie.Replaces, plus the nearest FRONT hit tracked with BarOneRule.RayState, then BarOneRule.Judge) over synthetic triangles
    /// with named regions, so a verdict is not hand-fed where a ray can make it. The camera looks along +Z from the origin; a triangle whose geometric normal points along +Z is seen from behind (back face).
    /// </summary>
    public class BarOneRuleTests
    {
        struct T { public Vector3 a, b, c; public string region; }

        [TearDown]
        public void NoOverrideLeftBehind() { BarOneRule.GapOverrideForTest = null; }

        // A triangle at depth z facing the camera (front) or away from it (back): vertices wound so the geometric normal (e1 x e2) is -Z (front) or +Z (back).
        static T Tri(float z, bool back, string region)
        {
            var a = new Vector3(-1f, -1f, z); var b = new Vector3(1f, -1f, z); var c = new Vector3(0f, 1f, z);
            // (b-a) x (c-a) = (+Z) for this winding: back-facing to a ray along +Z
            return back ? new T { a = a, b = b, c = c, region = region } : new T { a = a, b = c, c = b, region = region };
        }

        static bool RayHit(Vector3 o, Vector3 d, T tri, out float t, out bool back)
        {
            t = 0f; back = false;
            Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a, p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = o - tri.a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            if (t < 0f) return false;
            back = Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f;
            return true;
        }

        /// <summary>The asserting loops' shape: winner under MirrorTie.Replaces, nearest front hit tracked separately, then Judge.</summary>
        static BarOneRule.Verdict Cast(params T[] tris)
        {
            Vector3 o = Vector3.zero, d = Vector3.forward;
            float best = float.MaxValue; bool backFace = false; T bestTri = default;
            var front = new BarOneRule.RayState();
            foreach (var tri in tris)
            {
                if (!RayHit(o, d, tri, out float t, out bool back)) continue;
                front.Note(back, t, tri.region);
                if (MirrorTie.Replaces(best < float.MaxValue, best, backFace, bestTri.a, bestTri.b, bestTri.c, t, back, tri.a, tri.b, tri.c)) { best = t; backFace = back; bestTri = tri; }
            }
            Assert.Less(best, float.MaxValue, "the synthetic ray hit nothing");
            return BarOneRule.Judge(backFace, best, bestTri.region, front.Found, front.T, front.Region);
        }

        const string LegL = "LEG.L", LegR = "LEG.R";

        [Test]
        public void F1_NoFront_ABackFaceWithNothingBehindIt()
        {
            Assert.AreEqual(BarOneRule.Verdict.NoFront, Cast(Tri(1f, true, LegL)));
            Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(Tri(1f, false, LegL)), "control: the same triangle flipped is a front face");
        }

        [Test]
        public void F2_GapOverG_Strict_AndTheBoundaryIsOk()
        {
            Assert.AreEqual(0.035f, BarOneRule.GapMetres("LEG.L"), 1e-9f); Assert.AreEqual(0.035f, BarOneRule.GapMetres("LEG.R"), 1e-9f);
            foreach (var r in new[] { "ARM.L", "ARM.R", "TORSO", "HEAD" }) Assert.AreEqual(0.010f, BarOneRule.GapMetres(r), 1e-9f, r);
            // LEG regions: G = 35 mm; ARM regions: G = 10 mm. Each is tested 2 mm beyond (GapOverG, naming the region) and 2 mm inside (control Ok).
            foreach (var (region, g) in new[] { ("LEG.L", 0.035f), ("LEG.R", 0.035f), ("ARM.L", 0.010f), ("ARM.R", 0.010f), ("TORSO", 0.010f), ("HEAD", 0.010f) })
            {
                Assert.AreEqual(BarOneRule.Verdict.GapOverG, Cast(Tri(1f, true, region), Tri(1f + g + 0.002f, false, region)), $"{region}: front 2 mm beyond G");
                Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(Tri(1f, true, region), Tri(1f + g - 0.002f, false, region)), $"{region} control: front 2 mm inside G");
            }
            // the per-region G is the winner's: a leg gap of 20 mm is Ok, the same gap on an arm is GapOverG
            Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(Tri(1f, true, LegL), Tri(1.020f, false, LegL)), "20 mm on a leg");
            Assert.AreEqual(BarOneRule.Verdict.GapOverG, Cast(Tri(1f, true, "ARM.L"), Tri(1.020f, false, "ARM.L")), "20 mm on an arm");
            // the failure text names the region and the G used
            var tally = default(BarOneRule.Tally);
            tally.Add(BarOneRule.Verdict.GapOverG, true, 0.020f, "ARM.L");
            StringAssert.Contains("ARM.L 10 mm x1", tally.Clauses);
            StringAssert.Contains("gap>G 1", tally.Clauses);
            // exactly G, in floats that subtract exactly: G = 0.0625 (override), depths 1.0 and 1.0625
            BarOneRule.GapOverrideForTest = 0.0625f;
            try
            {
                Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(Tri(1f, true, LegL), Tri(1.0625f, false, LegL)), "exactly G is Ok: the rule is a strict >");
                Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(Tri(1f, true, "ARM.L"), Tri(1.0625f, false, "ARM.L")), "the override covers every region");
                Assert.AreEqual(BarOneRule.Verdict.GapOverG, Cast(Tri(1f, true, "ARM.L"), Tri(1.0626f, false, "ARM.L")), "one tick beyond the override");
            }
            finally { BarOneRule.GapOverrideForTest = null; }
            Assert.IsFalse(BarOneRule.GapOverrideForTest.HasValue);
        }

        [Test]
        public void F3_OtherRegion_AndTheSameRegionControl()
        {
            string upperLegL = BarOneRule.Region("UpperLeg.L"), upperLegR = BarOneRule.Region("UpperLeg.R");
            Assert.AreEqual(LegL, upperLegL); Assert.AreEqual(LegR, upperLegR);
            Assert.AreEqual(BarOneRule.Verdict.OtherRegion, Cast(Tri(1f, true, upperLegL), Tri(1.005f, false, upperLegR)), "back on UpperLeg.L, front on UpperLeg.R");
            Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(Tri(1f, true, upperLegL), Tri(1.005f, false, upperLegL)), "control: both on UpperLeg.L");
            Assert.AreEqual(BarOneRule.Region("LowerLeg.L"), BarOneRule.Region("UpperLeg.L"), "B9: LowerLeg and UpperLeg of one side are ONE region");
            Assert.AreEqual(BarOneRule.Region("Foot.L"), BarOneRule.Region("LowerLeg.L"));
        }

        [Test]
        public void F4_UnknownRegion_FailsClosed()
        {
            Assert.AreEqual("UNKNOWN", BarOneRule.Region("NotABone"));
            Assert.AreEqual(BarOneRule.Verdict.UnknownRegion, Cast(Tri(1f, true, LegL), Tri(1.005f, false, BarOneRule.Region("NotABone"))), "front region from a bone that is not in the map");
            Assert.AreEqual(BarOneRule.Verdict.UnknownRegion, Cast(Tri(1f, true, BarOneRule.Region("NotABone")), Tri(1.500f, false, LegL)), "unknown fails closed before any gap test, even for a huge gap");
            Assert.AreEqual(BarOneRule.Verdict.UnknownRegion, Cast(Tri(1f, true, BarOneRule.Region("NotABone")), Tri(1.005f, false, LegL)), "winner region unknown");
            Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(Tri(1f, true, LegL), Tri(1.005f, false, LegL)), "control: known regions");
        }

        [Test]
        public void F5_MirrorTie_AnExactMirrorPairAtEqualDistanceIsOk()
        {
            var back = Tri(1f, true, LegL);
            var front = new T { a = back.a, b = back.c, c = back.b, region = LegL };   // the same three positions, opposite winding
            Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(back, front), "front wins the mirror tie");
            Assert.AreEqual(BarOneRule.Verdict.Ok, Cast(front, back), "order does not matter");
        }

        [Test]
        public void F7_OverrideHygiene_IsNullAfterEveryCase_EvenWhenAnAssertThrows()
        {
            Assert.IsFalse(BarOneRule.GapOverrideForTest.HasValue, "the override must start null");
            Assert.Throws<AssertionException>(() =>
            {
                try { BarOneRule.GapOverrideForTest = 0f; Assert.Fail("a forced case throws while the override is set"); }
                finally { BarOneRule.GapOverrideForTest = null; }
            });
            Assert.IsFalse(BarOneRule.GapOverrideForTest.HasValue, "the finally block reset the override");
            Assert.AreEqual(0.035f, BarOneRule.G("LEG.L"), 1e-9f, "G is the director-set per-region lookup");
            Assert.AreEqual(0.010f, BarOneRule.G("ARM.R"), 1e-9f);
        }

        // ---- F6 (td-49 B15 ruling): coverage of the Survivor's weighted bones; the map is PRINTED FIRST, then asserted, with no count threshold ----

        static readonly string[] SixRegions = { "HEAD", "TORSO", "ARM.L", "ARM.R", "LEG.L", "LEG.R" };

        class MeshWeights
        {
            public string path, mesh;
            public HashSet<string> weighted = new HashSet<string>();
            public List<string> zeroWeight = new List<string>();
        }

        static List<MeshWeights> ReadAssets(StringBuilder text)
        {
            var result = new List<MeshWeights>();
            foreach (var path in new[] { SurvivorAssetBuilder.BodyPath, SurvivorAssetBuilder.HeadPath })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(go, "the asset of record did not load (the coverage check must not fail open): " + path);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh;
                    if (mesh == null) continue;
                    var mw = new MeshWeights { path = path, mesh = mesh.name };
                    var bones = smr.bones;
                    foreach (var bw in mesh.GetAllBoneWeights())
                        if (bw.weight > 0f && bw.boneIndex >= 0 && bw.boneIndex < bones.Length && bones[bw.boneIndex] != null) mw.weighted.Add(bones[bw.boneIndex].name);
                    foreach (var b in bones) if (b != null && !mw.weighted.Contains(b.name) && !mw.zeroWeight.Contains(b.name)) mw.zeroWeight.Add(b.name);
                    result.Add(mw);
                }
            }
            return result;
        }

        static string RegionOfBoneName(string name)
        {
            // the same map the rays use; PT.* and *_end helpers resolve through their parent in the rays, by name here
            return BarOneRule.Region(name);
        }

        /// <summary>The regions of the six that no entry of the list maps to.</summary>
        static List<string> MissingRegions(IEnumerable<(string name, string region)> list)
        {
            var have = new HashSet<string>(list.Select(x => x.region));
            return SixRegions.Where(r => !have.Contains(r)).ToList();
        }

        /// <summary>The names that map to UNKNOWN in a list of (name, region).</summary>
        static List<string> Uncovered(IEnumerable<(string name, string region)> list) => list.Where(x => x.region == "UNKNOWN").Select(x => x.name).ToList();

        [Test]
        public void F6_BoneCoverage_EveryWeightedBoneMapsToARegion_AndAFakeBoneFails()
        {
            var text = new StringBuilder();
            var meshes = ReadAssets(text);
            var weighted = meshes.SelectMany(m => m.weighted.Select(n => (name: n, mesh: m.mesh))).GroupBy(x => x.name)
                .Select(g => (name: g.Key, region: RegionOfBoneName(g.Key), meshes: string.Join("+", g.Select(x => x.mesh).Distinct()))).OrderBy(x => x.name).ToList();

            // (b) PRINT FIRST: the map, the junction counts and the zero-weight bones by name, before any assert
            text.AppendLine($"weighted bones ({weighted.Count}) -> region (meshes that weight it):");
            foreach (var b in weighted) text.AppendLine($"  {b.name} -> {b.region} ({b.meshes})");
            foreach (var m in meshes)
            {
                text.AppendLine($"{m.path} mesh {m.mesh}: {m.weighted.Count} weighted bones; zero-weight bones ({m.zeroWeight.Count}): {string.Join(", ", m.zeroWeight.OrderBy(x => x))}");
            }
            bool fingersUnweighted = meshes.All(m => !m.weighted.Any(n => n.StartsWith("Thumb") || n.StartsWith("Index") || n.StartsWith("Middle") || n.StartsWith("Ring") || n.StartsWith("Pinky")))
                && meshes.Any(m => m.zeroWeight.Any(n => n.StartsWith("Index")));
            if (fingersUnweighted) text.AppendLine("FINGERS: mitten (all finger bones unweighted; qa F1k known fail)");
            var pairs = new SortedDictionary<string, int>();
            int mixed = 0, total = 0;
            foreach (var path in new[] { SurvivorAssetBuilder.BodyPath, SurvivorAssetBuilder.HeadPath })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(go, "the asset of record did not load (the junction count must not fail open): " + path);
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh; if (mesh == null) continue;
                    var bonesT = smr.bones;
                    var all = mesh.GetAllBoneWeights(); var per = mesh.GetBonesPerVertex();
                    var first = new int[mesh.vertexCount]; int run = 0;
                    for (int i = 0; i < first.Length; i++) { first[i] = run; run += per[i]; }
                    string Reg(int vi)
                    {
                        int bi = -1; float best = -1f;
                        for (int k = 0; k < per[vi]; k++) { var w = all[first[vi] + k]; if (w.weight > best) { best = w.weight; bi = w.boneIndex; } }
                        return bi >= 0 && bi < bonesT.Length && bonesT[bi] != null ? BarOneRule.Region(bonesT[bi]) : "UNKNOWN";
                    }
                    var tri = mesh.triangles;
                    for (int i = 0; i < tri.Length; i += 3)
                    {
                        total++;
                        string r0 = Reg(tri[i]), r1 = Reg(tri[i + 1]), r2 = Reg(tri[i + 2]);
                        var set = new SortedSet<string> { r0, r1, r2 };
                        if (set.Count < 2) continue;
                        mixed++;
                        string key = smr.name + ": " + string.Join(" / ", set);
                        pairs[key] = pairs.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }
            }
            text.AppendLine($"triangles whose three corners span two or more regions (junctions): {mixed} of {total}");
            foreach (var kv in pairs) text.AppendLine($"  {kv.Key}: {kv.Value}");
            Debug.Log("BarOneRuleTests F6 map:\n" + text);

            // (c) asserts, no count threshold
            Assert.IsNotEmpty(meshes, "no skinned meshes read from the assets of record");
            foreach (var m in meshes) Assert.IsNotEmpty(m.weighted, $"mesh {m.mesh} ({m.path}) has no weighted bone");
            var bonesList = weighted.Select(b => (b.name, b.region)).ToList();
            var uncovered = Uncovered(bonesList);
            Assert.IsEmpty(uncovered, "bones with weight that map to UNKNOWN (add them to BarOneRule.Region, never loosen the map): " + string.Join(", ", uncovered));
            var missing = MissingRegions(bonesList);
            Assert.IsEmpty(missing, "regions with no weighted bone: " + string.Join(", ", missing));

            // (d) forced: the real map minus the ARM.L bones must fail naming ARM.L; the fake bone case must flag exactly the fake bone
            var withoutArmL = bonesList.Where(b => b.region != "ARM.L").ToList();
            Assert.AreEqual(new[] { "ARM.L" }, MissingRegions(withoutArmL).ToArray(), "forced: dropping the ARM.L bones must fail the region check, naming ARM.L");
            var withFake = bonesList.Concat(new[] { ("FakeBone.Nope", BarOneRule.Region("FakeBone.Nope")) });
            Assert.AreEqual(new[] { "FakeBone.Nope" }, Uncovered(withFake).ToArray(), "forced: the coverage check must flag the fake bone");
        }
    }
}
