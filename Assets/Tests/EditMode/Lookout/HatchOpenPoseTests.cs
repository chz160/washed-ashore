using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.Lookout.EditMode
{
    /// <summary>
    /// #40 R1f: the open hatch rests against the east wall and never in it. On the committed tower prefab the hatch is set to its open rest
    /// pose through its own state (the angle comes from LookoutTuning today); then the leaf's visible mesh must not lie past the outer face of the
    /// east wall's mesh (td part 19, T7.6), and stay under its top. Touching is accepted (director: resting against the wall).
    /// </summary>
    public class HatchOpenPoseTests
    {
        const string TowerPrefab = "Assets/World/Lookout/LookoutTower.prefab";
        const float Tolerance = 0.001f;

        const float CabSelMinX = 0.9786f, CabSelMaxX = 1.0668f, CabSelMinY = 26.3408f, CabSelMaxY = 27.3558f, CabSelMaxAbsZ = 1.0668f;

        /// <summary>
        /// td part 18/19, designer-40 T7.6, mesh level: the east wall's outer face is the greatest x (tower-local) of the vertices of the MC39_LOD0_Cab
        /// triangles whose centroid lies at x 0.9786-1.0668, y 26.3408-27.3558 and |z| <= 1.0668. No visible leaf vertex may lie beyond it by more than 1 mm,
        /// and the leaf's greatest y stays under 27.3558 (the cab wall's top).
        /// </summary>
        [Test]
        public void OpenPose_VisibleLeafMesh_NeverLiesBeyondTheEastWallsOuterFace_AndStaysUnderItsTop()
        {
            var root = PrefabUtility.LoadPrefabContents(TowerPrefab);
            try
            {
                var hatch = root.GetComponentInChildren<TrapDoor>(true);
                Assert.IsNotNull(hatch, "the built tower has no TrapDoor");
                hatch.SetState(HingeState.Open, 0f);
                var cab = root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.sharedMesh != null && (m.name.Contains("MC39_LOD0_Cab") || m.sharedMesh.name.Contains("MC39_LOD0_Cab")));
                Assert.IsNotNull(cab, "MC39_LOD0_Cab is not on the built tower");
                var mesh = cab.sharedMesh; var verts = mesh.vertices; var tris = mesh.triangles;
                float outer = float.MinValue; int count = 0;
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = root.transform.InverseTransformPoint(cab.transform.TransformPoint(verts[tris[i]]));
                    Vector3 b = root.transform.InverseTransformPoint(cab.transform.TransformPoint(verts[tris[i + 1]]));
                    Vector3 c = root.transform.InverseTransformPoint(cab.transform.TransformPoint(verts[tris[i + 2]]));
                    Vector3 g = (a + b + c) / 3f;
                    if (g.x < CabSelMinX || g.x > CabSelMaxX || g.y < CabSelMinY || g.y > CabSelMaxY || Mathf.Abs(g.z) > CabSelMaxAbsZ) continue;
                    count++;
                    outer = Mathf.Max(outer, a.x, b.x, c.x);
                }
                Assert.Greater(count, 0, "T7.6 selection: no MC39_LOD0_Cab triangle has its centroid in x 0.9786-1.0668, y 26.3408-27.3558");
                float leafMaxX = float.MinValue, leafMaxY = float.MinValue;
                foreach (var r in hatch.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!r.enabled || mf == null || mf.sharedMesh == null) continue;
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        Vector3 w = root.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                        leafMaxX = Mathf.Max(leafMaxX, w.x); leafMaxY = Mathf.Max(leafMaxY, w.y);
                    }
                }
                Assert.Greater(leafMaxX, float.MinValue, "the hatch leaf has no visible mesh");
                Debug.Log($"HatchOpenPoseTests: T7.6 selection {count} triangles, outer x {outer:F4}, leaf max x {leafMaxX:F4}, leaf max y {leafMaxY:F4} (limit {CabSelMaxY})");
                Assert.LessOrEqual(leafMaxX - outer, Tolerance, $"the open leaf's visible mesh goes {(leafMaxX - outer) * 1000f:F2} mm past the east wall's outer face");
                Assert.Less(leafMaxY, CabSelMaxY, "the open leaf's top is under the cab wall's top");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
