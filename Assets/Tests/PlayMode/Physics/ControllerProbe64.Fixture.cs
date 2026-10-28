using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>Fixture cases F0-F5 and F5t (spec-64 s4, td M2): primitive geometry on a raised slab west of the tower, one case's geometry at a time.</summary>
    public partial class ControllerProbe64
    {
        Vector3 origin;          // O: slab centre, top face at origin.y
        readonly List<GameObject> built = new List<GameObject>();
        GameObject slabGo;
        static readonly float[] Thetas = { 10f, 20f, 30f };

        // ---- geometry helpers ------------------------------------------------------------------------------------------

        GameObject Box(string name, Vector3 centreLocal, Vector3 size, Quaternion? rot = null)
        {
            var go = new GameObject("PROBE64_" + name) { layer = 0 };
            go.transform.SetPositionAndRotation(origin + centreLocal, rot ?? Quaternion.identity);
            var bc = go.AddComponent<BoxCollider>(); bc.size = size;
            built.Add(go);
            return go;
        }

        void ClearBuilt()
        {
            foreach (var g in built) if (g) Object.DestroyImmediate(g);
            built.Clear();
        }

        /// <summary>A non-convex mesh box (6 faces); the -x face is a grid of 1 m quads (F1m). Front faces are clockwise from outside.</summary>
        GameObject MeshWall(Vector3 centreLocal, Vector3 size)
        {
            var verts = new List<Vector3>(); var tris = new List<int>();
            Vector3 h = size * 0.5f;
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int i = verts.Count; verts.AddRange(new[] { a, b, c, d });
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) > 0f) tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                else tris.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
            }
            for (int zi = 0; zi < Mathf.RoundToInt(size.z); zi++)
                for (int yi = 0; yi < Mathf.RoundToInt(size.y); yi++)
                {
                    float z0 = -h.z + zi, y0 = -h.y + yi;
                    Quad(new Vector3(-h.x, y0, z0), new Vector3(-h.x, y0 + 1, z0), new Vector3(-h.x, y0 + 1, z0 + 1), new Vector3(-h.x, y0, z0 + 1), Vector3.left);
                }
            Quad(new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(h.x, -h.y, h.z), Vector3.right);
            Quad(new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.up);
            Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z), Vector3.down);
            Quad(new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z), Vector3.back);
            Quad(new Vector3(-h.x, -h.y, h.z), new Vector3(h.x, -h.y, h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z), Vector3.forward);
            var mesh = new Mesh { name = "PROBE64_wallmesh" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            var go = new GameObject("PROBE64_MeshWall") { layer = 0 };
            go.transform.position = origin + centreLocal;
            var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = false;
            built.Add(go);
            return go;
        }

        /// <summary>A flight: ramp box (top face foot-&gt;head, along +z of the slab), two vertical-faced walls aligned to the ramp, a landing past the head.</summary>
        void BuildFlight(Vector3 footLocal, Vector3 headLocal, float rampW, float rampT, float face, float wallT, float wallH)
        {
            Vector3 run3 = headLocal - footLocal, flat = new Vector3(run3.x, 0f, run3.z).normalized;
            Quaternion rot = Quaternion.LookRotation(run3.normalized, Vector3.up);
            Vector3 mid = (footLocal + headLocal) * 0.5f, up = rot * Vector3.up, right = rot * Vector3.right;
            Box("Ramp", mid - up * (rampT * 0.5f), new Vector3(rampW, rampT, run3.magnitude), rot);
            foreach (float sgn in new[] { 1f, -1f })
                Box(sgn > 0 ? "WallR" : "WallL", mid + right * (sgn * (face + wallT * 0.5f)) + up * (wallH * 0.5f), new Vector3(wallT, wallH, run3.magnitude), rot);
            Box("Landing", headLocal + flat * 0.75f - Vector3.up * 0.15f, new Vector3(3f, 0.3f, 1.5f));
        }

        // ---- validity before the first run -----------------------------------------------------------------------------

        void FixtureChecks()
        {
            var pts = new List<Vector3> { origin };
            foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) pts.Add(origin + new Vector3(sx * 20f, 0, sz * 20f));
            var water = WaterBody.Active;
            foreach (var p in pts)
            {
                if (!TerrainQuery.TryGroundHeight(p, out float g)) { validity.Add($"no terrain at {p:F1}"); continue; }
                if (water != null && water.SurfaceY(p.x, p.z) > g - 1f) validity.Add($"water {water.SurfaceY(p.x, p.z):F2} within 1 m of terrain {g:F2} at {p:F1}");
            }
            if (Mathf.Abs(cc.radius - 0.4f) > 1e-3f || Mathf.Abs(cc.height - 1.8f) > 1e-3f || Mathf.Abs(cc.stepOffset - 0.4f) > 1e-3f || Mathf.Abs(cc.skinWidth - 0.05f) > 1e-3f || Mathf.Abs(cc.slopeLimit - 45f) > 1e-3f)
                validity.Add($"capsule is not the scene capsule (r {cc.radius}, h {cc.height}, step {cc.stepOffset}, skin {cc.skinWidth}, slope {cc.slopeLimit})");
            EmitOverlap("before");
        }

        void EmitOverlap(string when)
        {
            var others = Physics.OverlapBox(origin + Vector3.up * 1.5f, new Vector3(20f, 2.5f, 20f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(player.transform) && !c.name.StartsWith("PROBE64_")).Select(c => c.name).Distinct().ToList();
            if (others.Count > 0) validity.Add($"foreign colliders over the slab ({when}): {string.Join(", ", others)}");
        }

        // ---- run builders ----------------------------------------------------------------------------------------------

        ProbeRun FRun(string cs, Vector3 startL, float yaw, float yawOff, Vector3 d, Vector3 refL, Vector3 toWall, float aEnd, float runEnd, float ideal, string dir = "-")
        {
            var s = new ProbeRun { caseName = cs, dir = dir, wallCols = WallColliders(), yawOff = yawOff, cap = 5f, r = 0.4f, expectedWalkCap = 0f, start = origin + startL, yaw = yaw, d = d, refPt = origin + refL, toWall = toWall, aEnd = aEnd, runEnd = runEnd, idealPath = ideal, useAt = true };
            s.aStart = Along(s, s.start);
            return s;
        }

        /// <summary>Every probe-built box or mesh except the floors (ramp, landing/deck); the slab is not in the list.</summary>
        List<Collider> WallColliders() => built.Where(g => g && !g.name.EndsWith("Ramp") && !g.name.EndsWith("Landing")).Select(g => g.GetComponent<Collider>()).Where(c => c).ToList();

        static float Cos(float deg) => Mathf.Cos(deg * Mathf.Deg2Rad);

        IEnumerator RunSet(IEnumerable<ProbeRun> runs, Dictionary<string, ProbeResult> refs)
        {
            foreach (var s in runs) yield return Go(s, refs);
        }

        IEnumerable<ProbeRun> CasesFlatWalls()
        {
            Vector3 z = Vector3.forward, x = Vector3.right;
            foreach (float th in new[] { 0f, 30f }) { var s = FRun("F0", Vector3.zero, th, th, z, Vector3.zero, x, 7f, 8f, Cos(th)); s.control = true; yield return s; }
            Box("FX", new Vector3(0, 1.5f, 3.1f), new Vector3(6, 3, 0.2f));
            var fx = FRun("FX", Vector3.zero, 0f, 0f, z, Vector3.zero, z, 3f, 3f, 0f); fx.control = true; fx.endTime = 2f; fx.noYaw = true;
            yield return fx;
            ClearBuilt();

            Box("F1", new Vector3(1.1f, 1.5f, 6f), new Vector3(0.2f, 3, 16));
            foreach (float th in Thetas) { var s = FRun("F1", new Vector3(0.53f, 0, 0), th, th, z, Vector3.zero, x, 7f, 8f, Cos(th)); yield return s; }
            ClearBuilt();
            MeshWall(new Vector3(1.1f, 1.5f, 6f), new Vector3(0.2f, 3, 16));
            foreach (float th in Thetas) { var s = FRun("F1m", new Vector3(0.53f, 0, 0), th, th, z, Vector3.zero, x, 7f, 8f, Cos(th)); s.walls.Add(new ProbeWall { dir = x, face = 1f, aMin = -2f, aMax = 14f }); s.lateralThrough = true; yield return s; }
            ClearBuilt();
        }

        IEnumerable<ProbeRun> CasesCorner()
        {
            Vector3 z = Vector3.forward, x = Vector3.right;
            Box("F2A", new Vector3(1.1f, 1.5f, 1.6f), new Vector3(0.2f, 3, 7.2f));
            Box("F2B", new Vector3(-0.9f, 1.5f, 5.1f), new Vector3(4f, 3, 0.2f));
            bool Seated(Vector3 p)
            {
                float dA = origin.x + 1f - p.x, dB = origin.z + 5f - p.z;
                return dA >= 0.40f - 1e-3f && dA <= 0.46f + 1e-3f && dB >= 0.40f - 1e-3f && dB <= 0.46f + 1e-3f;
            }
            foreach (float th in Thetas)
            {
                var s = FRun("F2i", new Vector3(0.53f, 0, 0), th, th, z, Vector3.zero, x, 4.45f, 4.45f, Cos(th));
                s.afterEnd = 0.5f; s.seated = Seated;
                yield return s;
            }
            foreach (float th in Thetas)
            {
                var s = FRun("F2o", new Vector3(0.53f, 0, 4.53f), 180f - th, th, -z, new Vector3(0, 0, 4.53f), x, 3.5f, 4f, Cos(th));
                yield return s;
            }
            ClearBuilt();
        }

        IEnumerable<ProbeRun> CasesDoorAndLips()
        {
            Vector3 z = Vector3.forward, x = Vector3.right;
            Box("F3R", new Vector3(2f, 1.1f, 4.1f), new Vector3(3f, 2.2f, 0.2f));
            Box("F3L", new Vector3(-2f, 1.1f, 4.1f), new Vector3(3f, 2.2f, 0.2f));
            Box("F3Lintel", new Vector3(0f, 2.6f, 4.1f), new Vector3(7f, 0.8f, 0.2f));
            foreach (float th in new[] { 0f, 10f, 20f, 30f })
            {
                float xs = 0.10f - 4f * Mathf.Tan(th * Mathf.Deg2Rad);
                var s = FRun("F3", new Vector3(xs, 0, 0), th, th, z, Vector3.zero, x, 5.7f, 6f, float.NaN);
                yield return s;
            }
            ClearBuilt();
            foreach (float h in new[] { 0.20f, 0.35f })
            {
                Box("F4u", new Vector3(0, h * 0.5f, 6f), new Vector3(6, h, 6));
                foreach (float th in new[] { 0f, 10f, 20f, 30f })
                { var s = FRun("F4u", Vector3.zero, th, th, z, Vector3.zero, x, 5f, 5.5f, Cos(th)); s.note = h.ToString("F2"); s.refKey = "F4u" + h; s.isRef = th == 0f; yield return s; }
                ClearBuilt();
                Box("F4d", new Vector3(0, h * 0.5f, 0), new Vector3(6, h, 6));
                foreach (float th in new[] { 0f, 10f, 20f, 30f })
                { var s = FRun("F4d", new Vector3(0, h, 0), th, th, z, Vector3.zero, x, 5f, 6f, Cos(th)); s.note = h.ToString("F2"); s.refKey = "F4d" + h; s.isRef = th == 0f; yield return s; }
                ClearBuilt();
            }
        }

        /// <summary>One flight on the slab, runs up and down at theta 0/10/20/30 on the right wall (F5: 30 degrees, normal capsule; F5t: the tower flight F1 and the tower walk values).</summary>
        IEnumerable<ProbeRun> CasesFlight(bool tower, bool upOnly = false)
        {
            string cs = tower ? "F5t" : "F5";
            float L = tower ? 3.172f : 8f, rise = tower ? 4.115f : 8f * Mathf.Tan(30f * Mathf.Deg2Rad);
            float rampW = tower ? 0.70f : 1.088f, face = tower ? 0.406f : 0.60f, wallT = tower ? 0.01f : 0.2f, wallH = 2.2f;
            Vector3 foot = new Vector3(0, 0, 1f), head = foot + new Vector3(0, rise, L);
            BuildFlight(foot, head, rampW, 0.3f, face, wallT, wallH);
            float cap = tower ? 1.15f : 5f, r = tower ? 0.25f : 0.4f, lat0 = tower ? 0.05f : 0.07f, slope = rise / L;
            foreach (bool up in upOnly ? new[] { true } : new[] { true, false })
                foreach (float th in new[] { 0f, 10f, 20f, 30f })
                {
                    Vector3 d = up ? Vector3.forward : Vector3.back, toWall = up ? Vector3.right : Vector3.left;
                    Vector3 refL = up ? foot : head, refFlat = new Vector3(refL.x, 0, refL.z);
                    float a0 = 0.35f, lat = th > 0f ? lat0 : 0f;
                    Vector3 pos = refFlat + d * a0 + toWall * lat;
                    pos.y = (up ? foot.y + a0 * slope : head.y - a0 * slope) + 0.05f;
                    float yaw = (up ? 0f : 180f) + th;
                    var s = FRun(cs, pos, yaw, th, d, refFlat, toWall, L - 0.3f, L + 0.1f, Cos(th), up ? "up" : "down");
                    s.cap = cap; s.r = r; s.side = "R"; s.refKey = cs + (up ? "up" : "down"); s.isRef = th == 0f; s.logContacts = up && tower;
                    s.expectedWalkCap = tower ? 1.15f : 0f;
                    s.walls.Add(new ProbeWall { dir = toWall, face = face, aMin = 0f, aMax = L });   // along the flight only (part10 P1)
                    s.lateralThrough = tower;   // td addendum part4: F5t also gets the lateral test (0.01 m walls can hide a pass-through from ComputePenetration)
                    s.aStart = a0;
                    s.useAt = !tower;   // At resets the stance and could undo the slim capsule; Reposition keeps it
                    yield return s;
                }
            ClearBuilt();
        }

        // ---- the test -------------------------------------------------------------------------------------------------

        [UnityTest, Explicit("#64 controller probe: fails on main by design"), Category("ControllerProbe64"), Timeout(3600000)]
        public IEnumerator Probe64_Fixture()
        {
            yield return Begin("Probe64_Fixture", 1f / 60f);
            ProbeTowerStateSetter setter = null;
            float sR = 0, sH = 0, sStep = 0, sSlope = 0;
            bool flightSetup = false;
            try
            {
                yield return null;
                yield return null;
                Vector3 c = new Vector3(508f, 0f, -230f);
                float maxG = float.MinValue;
                foreach (var p in new[] { c, c + new Vector3(20, 0, 20), c + new Vector3(-20, 0, 20), c + new Vector3(20, 0, -20), c + new Vector3(-20, 0, -20) })
                    if (TerrainQuery.TryGroundHeight(p, out float g)) maxG = Mathf.Max(maxG, g);
                origin = new Vector3(c.x, maxG + 30f, c.z);
                var slab = new GameObject("PROBE64_Slab") { layer = 0 };
                slab.transform.position = origin - Vector3.up * 0.5f;
                slab.AddComponent<BoxCollider>().size = new Vector3(40, 1, 40);
                slabGo = slab;
                yield return new WaitForFixedUpdate();
                // The Player starts inside the tower footprint with the slim capsule: move it onto the slab first so the header and the capsule check see the scene capsule.
                yield return TestSpawn.At(origin + new Vector3(0f, 0.05f, -8f), 0f, false);
                for (int i = 0; i < 5; i++) yield return null;
                WriteHeader("Probe64_Fixture");
                Emit($"fixture O {origin:F2} (y_O = max terrain {maxG:F2} + 30); slab 40 x 1 x 40, top face at y_O");
                FixtureChecks();

                var refs = new Dictionary<string, ProbeResult>();
                yield return RunSet(CasesFlatWalls(), refs);
                yield return RunSet(CasesCorner(), refs);
                yield return RunSet(CasesDoorAndLips(), refs);
                yield return RunSet(CasesFlight(false), refs);

                sR = cc.radius; sH = player.StandingHeight; sStep = cc.stepOffset; sSlope = cc.slopeLimit;
                flightSetup = true;
                TestSpawn.Reposition(player, origin + new Vector3(0, 1f, -8f), 0f);
                Emit($"F5t saved tower state: r {sR:F3} h {sH:F3} step {sStep:F3} slope {sSlope:F0} WalkSpeedCap {player.WalkSpeedCap:F3} snap {player.GroundSnapDistance:F2}");
                player.SetWalkCapsule(0.25f, 1.6f); cc.stepOffset = 0.05f; cc.slopeLimit = 80f;
                setter = player.gameObject.AddComponent<ProbeTowerStateSetter>(); setter.player = player;   // runs at order -50, after TowerWalkZones zeroed the two values
                yield return null;
                Emit($"F5t tower state as set: r {cc.radius:F3} h {cc.height:F3} step {cc.stepOffset:F3} slope {cc.slopeLimit:F0} WalkSpeedCap {player.WalkSpeedCap:F3} snap {player.GroundSnapDistance:F2}");
                yield return RunSet(CasesFlight(true), refs);

                ClearBuilt();
                yield return new WaitForFixedUpdate();
                EmitOverlap("after");
                Summary("Probe64_Fixture", false);
            }
            finally
            {
                ClearBuilt();
                if (slabGo) Object.DestroyImmediate(slabGo);
                if (setter) Object.Destroy(setter);
                if (flightSetup && player)
                {
                    player.SetWalkCapsule(sR, sH); cc.stepOffset = sStep; cc.slopeLimit = sSlope;
                    player.WalkSpeedCap = 0f; player.GroundSnapDistance = 0f;
                    Emit($"F5t tower state as restored: r {cc.radius:F3} h {cc.height:F3} step {cc.stepOffset:F3} slope {cc.slopeLimit:F0} WalkSpeedCap {player.WalkSpeedCap:F3} snap {player.GroundSnapDistance:F2}");
                }
                End();
            }
            Verdict("Probe64_Fixture");
        }
    }
}
