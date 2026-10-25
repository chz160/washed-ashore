using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Tests.Lookout.EditMode
{
    /// <summary>
    /// #45 M3 (td R6c, technical artist condition d): the 40 convex tower collision meshes, one line each with mesh guid, fileID,
    /// vertex count and triangle count, and one hash of the table. Uses nothing #45 adds, so it runs unchanged on main (BEFORE) and
    /// after the pre-bake import change (AFTER); the two tables must be equal. Reading only; nothing is written.
    /// </summary>
    public class TowerMeshTable
    {
        const string TowerPrefab = "Assets/World/Lookout/LookoutTower.prefab";

        [Test]
        public void ConvexCollisionMeshes_PrintGuidFileIdVerticesAndTriangles()
        {
            var root = PrefabUtility.LoadPrefabContents(TowerPrefab);
            try
            {
                var colliders = root.GetComponentsInChildren<MeshCollider>(true).Where(m => m.convex).OrderBy(m => m.name, System.StringComparer.Ordinal).ToList();
                Assert.AreEqual(40, colliders.Count, "the tower has 40 convex MeshColliders");
                var sb = new StringBuilder();
                foreach (var c in colliders)
                {
                    var mesh = c.sharedMesh;
                    Assert.IsNotNull(mesh, c.name + " has no mesh");
                    Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long fileId), c.name + ": no asset identity");
                    long tris = 0;
                    for (int i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
                    Assert.Greater(mesh.vertexCount, 0, c.name + " has no vertices");
                    Assert.Greater(tris, 0, c.name + " has no triangles");
                    sb.AppendLine($"TowerMesh {c.name} mesh={mesh.name} guid={guid} fileID={fileId} verts={mesh.vertexCount} tris={tris} convex={c.convex}");
                }
                string table = sb.ToString();
                using (var sha = SHA256.Create())
                {
                    string hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(table)).Select(b => b.ToString("x2")));
                    Debug.Log($"TowerMeshTable: {colliders.Count} meshes\n{table}TowerMeshTable sha256={hash}");
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
