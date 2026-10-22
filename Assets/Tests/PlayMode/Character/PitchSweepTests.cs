using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// H5: the camera never shows the inside of the head or body at any stance or pitch from -85 to +85, and the stance eye matches
    /// the research within 3 cm. Per stance and pitch step, with the real animated pose baked to triangles:
    /// (1) no triangle lies between the camera and its near plane, inside the frustum;
    /// (2) no view ray's nearest hit is a back face (the camera would be looking at the inside of the shell, e.g. the neck opening).
    /// Result per stance: TestResults/player-character/sweep/sweep-STANCE.csv and stills at -85, 0 and +85.
    /// </summary>
    public class PitchSweepTests : StanceFixture
    {
        static string OutDir => RunOutput.Dir("sweep");
        const float EyeTolerance = 0.03f;
        // Hidden-head eye point in the Head bone rest frame (artist manifest, measured from Men_Farmer_Head; td ruling 2026-10-18).
        const float EyeUp = 0.1094f, EyeForward = 0.1011f;
        const int GridX = 9, GridY = 5;

        static readonly StanceKey[] Keys = { StanceKey.None, StanceKey.Crouch, StanceKey.Squat, StanceKey.Sit, StanceKey.Sleep };
        static readonly float[] StandEyes = { 1.65f, 1.20f, 1.00f, 0.80f, 0.20f };

        struct Tri { public Vector3 a, b, c; }

        [UnityTest, Timeout(1800000)]
        public IEnumerator EveryStance_PitchSweep_NoClipping()
        {
            yield return LoadWorld();
            yield return OnFloor();
            var view = player.GetComponent<PlayerCharacterView>();
            Assert.IsNotNull(view, "no PlayerCharacterView on the Player: run PlayerCharacterSetup");
            Assert.IsNotNull(view.Body, "the Player has no assembled body");
            var fp = view.FirstPerson;
            Directory.CreateDirectory(OutDir);
            var head = view.Body.Bone("Head");
            Assert.IsNotNull(head, "no Head bone");
            Assert.AreEqual(0, fp.ShadowRoot.GetComponentsInChildren<Collider>(true).Length, "the shadow body has a Collider");
            if (fp.ViewRoot != null) Assert.AreEqual(0, fp.ViewRoot.GetComponentsInChildren<Collider>(true).Length, "the view body has a Collider");
            var summary = new StringBuilder();
            var failures = new List<string>();
            Vector3 headLocalEye = default;

            for (int s = 0; s < Keys.Length; s++)
            {
                var stance = (Stance)s;
                if (Keys[s] != StanceKey.None) yield return Go(Keys[s], 6f);
                Assert.AreEqual(stance, player.Stance, $"could not reach {stance}: {player.EntryBlockedBy(stance)}");
                yield return new WaitForSeconds(2f);   // let the held pose and its loop settle
                if (s == 0)
                {
                    // The research eye point sits 0.103 up and 0.08 forward of the Head bone in the standing head frame.
                    Vector3 p = head.position + Vector3.up * EyeUp + player.transform.forward * EyeForward;
                    headLocalEye = head.InverseTransformPoint(p);
                }
                if (s == 0)
                {
                    // Controller hover h: capsule bottom minus the ground it rests on (flat test floor, grounded, idle 2 s). td ruling: body drop = h.
                    float floorTop = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
                    float capBottom = player.transform.position.y + body.center.y - body.height * 0.5f;
                    float lowest = float.MaxValue;
                    foreach (var tri in Bake(fp)) lowest = Mathf.Min(lowest, Mathf.Min(tri.a.y, Mathf.Min(tri.b.y, tri.c.y)));
                    Assert.AreEqual(0f, lowest - floorTop, 0.01f, "Stand idle: the lowest foot vertex is not on the floor");
                    summary.AppendLine($"Scene World, run {System.DateTime.Now:s}: controller hover h = {capBottom - floorTop:F4} m (capsule bottom minus floor top); player origin above floor {player.transform.position.y - floorTop:F4} m; body drop in use {fp.FootDrop:F4} m; lowest foot vertex above ground (Stand idle) {lowest - floorTop:F4} m");
                }
                float expectedEye = StandEyes[s];
                var csv = new StringBuilder("pitch,nearSlabTriangles,backFaceRays,minBackFaceDistance\n");
                int worstNear = 0, worstBack = 0;
                for (float pitch = -85f; pitch <= 85.01f; pitch += 5f)
                {
                    player.SetLook(player.Yaw, pitch);
                    yield return null;
                    yield return null;
                    var tris = Bake(fp);
                    Check(player.ViewCamera, tris, out int near, out int back, out float minBack);
                    worstNear = Mathf.Max(worstNear, near);
                    worstBack = Mathf.Max(worstBack, back);
                    csv.AppendLine($"{pitch:F0},{near},{back},{(back > 0 ? minBack.ToString("F3") : "-")}");
                    if (Mathf.Approximately(pitch, -85f) || Mathf.Approximately(pitch, 0f) || Mathf.Approximately(pitch, 85f))
                        yield return Still($"{OutDir}/{stance}_{pitch:+0;-0;0}.png");
                }
                File.WriteAllText($"{OutDir}/sweep-{stance}.csv", csv.ToString());

                Vector3 eyePoint = head.TransformPoint(headLocalEye);
                float floorTopY = floor.transform.position.y + floor.transform.localScale.y * 0.5f;
                float headAboveFloor = eyePoint.y - floorTopY, camAboveFloor = player.ViewCamera.transform.position.y - floorTopY;
                float horiz = new Vector2(eyePoint.x - player.ViewCamera.transform.position.x, eyePoint.z - player.ViewCamera.transform.position.z).magnitude;
                summary.AppendLine($"{stance}: camera {camAboveFloor:F3} and hidden-head eye point {headAboveFloor:F3} above the floor top (difference {camAboveFloor - headAboveFloor:F3}); pivot-local eye {Eye:F3} (record {expectedEye:F2}); horizontal gap {horiz:F3}; worst near-slab {worstNear}; worst back-face rays {worstBack}");
                if (Mathf.Abs(Eye - expectedEye) > 0.011f) failures.Add($"{stance}: pivot eye {Eye:F3} vs {expectedEye:F2}");
                if (Mathf.Abs(camAboveFloor - headAboveFloor) > EyeTolerance) failures.Add($"{stance}: camera {camAboveFloor:F3} vs hidden-head eye point {headAboveFloor:F3} above the floor (> 3 cm)");
                if (worstNear > 0) failures.Add($"{stance}: {worstNear} triangles in the near slab");
                if (worstBack > 0) failures.Add($"{stance}: up to {worstBack} view rays end on a back face (inside of the body)");
            }
            File.WriteAllText($"{OutDir}/sweep-summary.txt", summary.ToString());
            Debug.Log("PitchSweepTests:\n" + summary);
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>World-space triangles of every body renderer the camera can see (the head is shadow-only and left out).</summary>
        static List<Tri> Bake(FirstPersonBody fp)
        {
            var tris = new List<Tri>(40000);
            var mesh = new Mesh();
            foreach (var r in fp.ViewRenderers)
            {
                if (!r.enabled) continue;
                if (!(r is SkinnedMeshRenderer smr)) continue;
                smr.BakeMesh(mesh, false);
                var v = mesh.vertices;
                var t = mesh.triangles;
                Vector3 pos = smr.transform.position;
                Quaternion rot = smr.transform.rotation;
                for (int i = 0; i < t.Length; i += 3)
                    tris.Add(new Tri { a = pos + rot * v[t[i]], b = pos + rot * v[t[i + 1]], c = pos + rot * v[t[i + 2]] });
            }
            Object.Destroy(mesh);
            return tris;
        }

        static void Check(Camera cam, List<Tri> tris, out int nearSlab, out int backRays, out float minBackDistance)
        {
            Transform ct = cam.transform;
            Vector3 o = ct.position;
            float n = cam.nearClipPlane;
            float halfV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfH = halfV * (16f / 9f);
            nearSlab = backRays = 0;
            minBackDistance = float.MaxValue;

            // (1) Segments from the camera to a grid on the near plane: any triangle crossing one is inside the clipped slab.
            // (2) Rays through the same grid, to the far end of the body: the nearest hit must not be a back face.
            var dirs = new List<Vector3>();
            for (int iy = 0; iy < GridY; iy++)
                for (int ix = 0; ix < GridX; ix++)
                {
                    float x = (ix / (float)(GridX - 1) * 2f - 1f) * halfH, y = (iy / (float)(GridY - 1) * 2f - 1f) * halfV;
                    dirs.Add((ct.forward + ct.right * x + ct.up * y).normalized);
                }
            foreach (var d in dirs)
            {
                float nearestT = float.MaxValue;
                bool nearestBack = false;
                float nearLimit = n / Vector3.Dot(d, ct.forward);
                foreach (var tri in tris)
                {
                    if (!RayTriangle(o, d, tri, out float t, out bool backFacing)) continue;
                    if (t < nearestT) { nearestT = t; nearestBack = backFacing; }
                    if (t >= 0f && t <= nearLimit) nearSlab++;
                }
                if (nearestT < float.MaxValue && nearestBack) { backRays++; minBackDistance = Mathf.Min(minBackDistance, nearestT); }
            }
        }

        // Moller-Trumbore, two-sided; reports whether the ray hit the triangle's back (the ray runs along the geometric normal).
        static bool RayTriangle(Vector3 o, Vector3 d, Tri tri, out float t, out bool backFacing)
        {
            t = 0f;
            backFacing = false;
            Vector3 e1 = tri.b - tri.a, e2 = tri.c - tri.a;
            Vector3 p = Vector3.Cross(d, e2);
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
            // Unity front faces: Cross(b - a, c - a) points out of the front. A ray that travels along it hits the back.
            Vector3 normal = Vector3.Cross(e1, e2);
            backFacing = Vector3.Dot(normal, d) > 0f;
            return true;
        }

        IEnumerator Still(string path)
        {
            var cam = player.ViewCamera;
            var rt = new RenderTexture(960, 540, 24);
            var old = cam.targetTexture;
            Texture2D tex = null;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = old;
                RenderTexture.active = null;
                if (tex != null) Object.Destroy(tex);
                rt.Release();
                Object.Destroy(rt);
            }
            yield return null;
        }
    }
}
