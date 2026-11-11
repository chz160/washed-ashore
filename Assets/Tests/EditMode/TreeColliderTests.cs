using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// Issue 21, criteria T2 and T3: every Bells Bend tree LOD prefab carries exactly one CapsuleCollider on its root,
    /// copied from the MegaKit prefab (axis, height, centre) and sized to the trunk, not the canopy. The trunk is
    /// measured here from the MegaKit mesh with its own code, so this does not trust the build script's number.
    /// </summary>
    public class TreeColliderTests
    {
        const string LodRoot = "Assets/World/BellsBend/Art/TreeLods";
        const string KitRoot = "Assets/ThirdParty/Quaternius/NatureMegaKit";

        // The 11 prototypes of BellsBendVegetation.TreeSpecs (field cedar reuses Pine_1 and Pine_3 at another scale).
        static readonly string[] Prototypes =
        {
            "CommonTree_1", "CommonTree_2", "CommonTree_3", "CommonTree_4", "CommonTree_5",
            "Pine_1", "Pine_3", "TwistedTree_1", "DeadTree_1", "DeadTree_2", "DeadTree_3",
        };

        static GameObject Kit(string name) => AssetDatabase.FindAssets($"{name} t:Prefab", new[] { KitRoot })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileNameWithoutExtension(p) == name)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>).First();

        // Median horizontal distance from the centroid of the mesh vertices in the first of the bands 0.3-1, 1-2, 2-3 m
        // above the root that has at least 20 vertices.
        static float MeasuredTrunkRadius(GameObject kit)
        {
            var mf = kit.GetComponentsInChildren<MeshFilter>().First(f => f.sharedMesh);
            var m = kit.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var vs = mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            foreach (var (lo, hi) in new[] { (0.3f, 1f), (1f, 2f), (2f, 3f) })
            {
                var band = vs.Where(v => v.y >= lo && v.y < hi).ToArray();
                if (band.Length < 20) continue;
                float cx = band.Average(v => v.x), cz = band.Average(v => v.z);
                var d = band.Select(v => Mathf.Sqrt((v.x - cx) * (v.x - cx) + (v.z - cz) * (v.z - cz))).OrderBy(x => x).ToArray();
                return d[d.Length / 2];
            }
            Assert.Fail($"{kit.name}: no trunk band");
            return 0f;
        }

        // T2: in the player's band (0-1.8 m above the root, 0.3 m slices) the capsule's footprint may stand out at most 0.2 m
        // beyond the mesh's own extent in that slice, on each side. Slices with fewer than 8 vertices are skipped.
        // Only prototypes that BellsBendTreeLods.TrunkFit has fitted to the trunk are held to the limit (#21, team-lead ruling);
        // the worst stand-out of the others is written to the test output so it stays visible.
        // The fitted prototypes are the keys of BellsBendTreeLods.TrunkFit, read by reflection (the test assembly cannot reference the
        // editor assembly), so this list cannot drift from the build script. The limit is 0.2 m; DeadTree_2 keeps 0.25 m because its
        // remaining stand-out is taper that a centre shift cannot remove.
        static readonly System.Collections.Generic.Dictionary<string, float> LimitOverride =
            new System.Collections.Generic.Dictionary<string, float> { ["DeadTree_2"] = 0.25f };

        static System.Collections.Generic.Dictionary<string, float> FittedToBand
        {
            get
            {
                var type = System.Type.GetType("BellsBendTreeLods, Assembly-CSharp-Editor");
                Assert.IsNotNull(type, "BellsBendTreeLods not found in Assembly-CSharp-Editor");
                var field = type.GetField("TrunkFit", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                Assert.IsNotNull(field, "BellsBendTreeLods.TrunkFit not found");
                var keys = ((System.Collections.IDictionary)field.GetValue(null)).Keys;
                var result = new System.Collections.Generic.Dictionary<string, float>();
                foreach (string k in keys) result[k] = LimitOverride.TryGetValue(k, out float l) ? l : 0.2f;
                return result;
            }
        }

        [Test]
        public void CapsuleDoesNotStandOutFromTheTrunkInThePlayersBand([ValueSource(nameof(Prototypes))] string name)
        {
            var cap = AssetDatabase.LoadAssetAtPath<GameObject>($"{LodRoot}/{name}.prefab").GetComponent<CapsuleCollider>();
            var kit = Kit(name);
            var mf = kit.GetComponentsInChildren<MeshFilter>().First(f => f.sharedMesh);
            var m = kit.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var vs = mf.sharedMesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            float cx0 = cap.center.x - cap.radius, cx1 = cap.center.x + cap.radius, cz0 = cap.center.z - cap.radius, cz1 = cap.center.z + cap.radius;
            float worst = 0f; string worstAt = "none";
            for (int i = 0; i < 6; i++)
            {
                var band = vs.Where(v => v.y >= i * 0.3f && v.y < (i + 1) * 0.3f).ToArray();
                if (band.Length < 8) continue;
                float sx0 = band.Min(v => v.x), sx1 = band.Max(v => v.x), sz0 = band.Min(v => v.z), sz1 = band.Max(v => v.z);
                foreach (var (side, d) in new[] { ("-x", sx0 - cx0), ("+x", cx1 - sx1), ("-z", sz0 - cz0), ("+z", cz1 - sz1) })
                    if (d > worst) { worst = d; worstAt = $"{side} in slice {i * 0.3f:F1}-{(i + 1) * 0.3f:F1} m"; }
            }
            TestContext.Out.WriteLine($"STANDOUT {name}: worst {worst:F2} m ({worstAt})");
            if (FittedToBand.TryGetValue(name, out float limit))
                Assert.That(worst, Is.LessThanOrEqualTo(limit), $"{name}: capsule stands out {worst:F2} m on {worstAt} (limit {limit:F2})");
        }

        [Test]
        public void EveryTreeLodPrefabHasOneCapsuleOnItsRoot([ValueSource(nameof(Prototypes))] string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{LodRoot}/{name}.prefab");
            Assert.IsNotNull(prefab, $"{name} LOD prefab missing");
            var all = prefab.GetComponentsInChildren<Collider>(true);
            Assert.AreEqual(1, all.Length, $"{name}: expected one collider, found {all.Length}");
            var cap = prefab.GetComponent<CapsuleCollider>();
            Assert.IsNotNull(cap, $"{name}: the collider must be a CapsuleCollider on the prefab root");
            Assert.IsFalse(cap.isTrigger, $"{name}: a trigger would not block the player");
        }

        [Test]
        public void CapsuleIsCopiedFromKitAndSizedToTrunk([ValueSource(nameof(Prototypes))] string name)
        {
            var cap = AssetDatabase.LoadAssetAtPath<GameObject>($"{LodRoot}/{name}.prefab").GetComponent<CapsuleCollider>();
            var kit = Kit(name);
            var kitCap = kit.GetComponent<CapsuleCollider>();
            Assert.AreEqual(kitCap.direction, cap.direction, $"{name} axis");
            Assert.AreEqual(kitCap.height, cap.height, 0.001f, $"{name} height");
            Assert.AreEqual(kitCap.center.y, cap.center.y, 0.001f, $"{name} centre y");
            // Prototypes fitted to an off-centre trunk (BellsBendTreeLods.TrunkFit) may move sideways, but only a little; all others keep the kit centre.
            float shift = Vector2.Distance(new Vector2(kitCap.center.x, kitCap.center.z), new Vector2(cap.center.x, cap.center.z));
            if (FittedToBand.ContainsKey(name)) Assert.That(shift, Is.LessThanOrEqualTo(0.5f), $"{name}: centre moved {shift:F2} m sideways");
            else Assert.That(shift, Is.EqualTo(0f).Within(0.001f), $"{name}: centre must stay at the kit centre");

            float trunk = MeasuredTrunkRadius(kit);
            Assert.That(cap.radius, Is.EqualTo(trunk).Within(trunk * 0.2f),
                $"{name}: radius {cap.radius:F3} vs measured trunk {trunk:F3} (+-20%)");
            Assert.That(cap.radius, Is.InRange(0.2f, 1.2f), $"{name}: radius outside the plausible trunk range");
            Assert.That(cap.height, Is.GreaterThanOrEqualTo(2f * cap.radius), $"{name}: capsule height below its diameter");
        }
    }
}
