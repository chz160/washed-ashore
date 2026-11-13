using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WashedAshore.Perf.Editor
{
    /// <summary>
    /// Issue #9 ridge_top candidates (technical-director's pose addendum on #25), read-only and fixed before the pose check.
    /// Ground-level eyes (1.7 m) on a 10 m grid within 150 m of the LookoutTower site, outside the tower's renderer bounds
    /// (+5 m), not within 2 m of a terrain tree, and on the ridge: ground no more than 15 m below the site's ground (a pose
    /// down the flank would not be a ridge top; the first pass without this band picked eyes 60-80 m lower). For each eye and each yaw (5 degree steps), along the centre ray:
    ///   skyline_m     distance of the terrain sample that sets the highest elevation angle out to 2000 m (the skyline)
    ///   far_visible   some terrain sample past 600 m is not hidden by nearer terrain
    ///   near_trees    terrain tree instances within 6 m of the ray between 2 and 200 m (trunks and crowns in the way)
    /// A yaw qualifies when far_visible or skyline_m &gt;= 600 and its centre ray doesn't cross the tower bounds (+5 m) within 200 m. Each eye keeps its qualifying yaw with the fewest near_trees
    /// (then the longest skyline). Eyes are ranked the same way; the top 3 at least 30 m apart are the candidates, in order.
    /// Candidate 0 (the current FrustumSpots ridge_top, yaw 225.3) is scored the same way for comparison.
    /// Terrain only: props and the barrier are not occluders here; the pose check's own-yaw still is the judge.
    ///   Unity.exe -batchmode -nographics -projectPath . -executeMethod WashedAshore.Perf.Editor.RidgeCandidates.RunBatch -logFile TestResults/9-ridge-candidates.log
    /// Writes TestResults/9-ridge-candidates.json. Never saves the scene.
    /// </summary>
    public static class RidgeCandidates
    {
        const float SiteX = 546.15f + 1.85f, SiteZ = -230.94f + 0.94f;   // FrustumSpots: LookoutTower x,z + site offset
        const float Radius = 150f, Grid = 10f, Eye = FrustumSpots.Eye, Far = 600f, MaxRay = 2000f, Step = 5f;
        const float TreeClear = 2f, TreeLateral = 6f, TreeRayMax = 200f, Spacing = 30f, TowerPad = 5f, RidgeBand = 15f;
        const string Out = "TestResults/9-ridge-candidates.json";

        public static void RunBatch()
        {
            try { Debug.Log("[Ridge] " + Run()); if (Application.isBatchMode) EditorApplication.Exit(0); }
            catch (System.Exception e) { Debug.LogError("[Ridge] FAILED " + e); if (Application.isBatchMode) EditorApplication.Exit(1); }
        }

        static string F(double v) => FrustumMath.F(v);

        struct Yaw { public float yaw, skyline; public bool farVisible; public int nearTrees; public bool Qualifies => farVisible || skyline >= Far; }

        public static string Run()
        {
            var scenePath = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).First();
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var tiles = roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).ToList();
            var tower = roots.FirstOrDefault(r => r.name == "LookoutTower");
            Bounds towerBounds = new Bounds(new Vector3(SiteX, 0f, SiteZ), Vector3.zero);
            if (tower)
            {
                var rs = tower.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0) { towerBounds = rs[0].bounds; foreach (var r in rs) towerBounds.Encapsulate(r.bounds); }
            }
            towerBounds.Expand(new Vector3(TowerPad * 2f, 1000f, TowerPad * 2f));

            var trees = new List<Vector2>();
            foreach (var t in tiles)
            {
                var d = t.terrainData;
                if (!d) continue;
                var o = t.transform.position;
                foreach (var inst in d.treeInstances)
                {
                    var p = o + Vector3.Scale(inst.position, d.size);
                    if (new Vector2(p.x - SiteX, p.z - SiteZ).magnitude < Radius + TreeRayMax + 10f) trees.Add(new Vector2(p.x, p.z));
                }
            }

            float H(float x, float z)
            {
                foreach (var t in tiles)
                {
                    var d = t.terrainData;
                    if (!d) continue;
                    var o = t.transform.position;
                    if (x >= o.x && x <= o.x + d.size.x && z >= o.z && z <= o.z + d.size.z) return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
                }
                return float.NaN;
            }

            Yaw Score(float x, float z, float eyeY, float yaw)
            {
                var dir = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
                float maxTan = float.NegativeInfinity, skyline = 0f;
                bool farVisible = false;
                for (float dist = Step; dist <= MaxRay; dist += Step)
                {
                    float h = H(x + dir.x * dist, z + dir.y * dist);
                    if (float.IsNaN(h)) break;   // off the map
                    float tan = (h - eyeY) / dist;
                    if (tan >= maxTan) { maxTan = tan; skyline = dist; if (dist > Far) farVisible = true; }
                }
                int near = 0;
                foreach (var tr in trees)
                {
                    var rel = tr - new Vector2(x, z);
                    float along = Vector2.Dot(rel, dir);
                    if (along < 2f || along > TreeRayMax) continue;
                    float lateral = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                    if (lateral <= TreeLateral) near++;
                }
                return new Yaw { yaw = yaw, skyline = skyline, farVisible = farVisible, nearTrees = near };
            }

            Yaw Best(float x, float z, float eyeY, out int qualifying)
            {
                Yaw best = default; bool any = false; qualifying = 0;
                for (float yaw = 0f; yaw < 360f; yaw += 5f)
                {
                    // The tower is not terrain: a yaw whose centre ray crosses its bounds (+5 m) within 200 m is out.
                    var dir3 = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
                    if (towerBounds.IntersectRay(new Ray(new Vector3(x, towerBounds.center.y, z), dir3), out float hit) && hit < TreeRayMax) continue;
                    var y = Score(x, z, eyeY, yaw);
                    if (!y.Qualifies) continue;
                    qualifying++;
                    if (!any || y.nearTrees < best.nearTrees || (y.nearTrees == best.nearTrees && y.skyline > best.skyline)) { best = y; any = true; }
                }
                if (!any) best.yaw = -1f;
                return best;
            }

            var eyes = new List<(float x, float z, float ground, Yaw best, int qualifying)>();
            int skippedTower = 0, skippedTree = 0, skippedNone = 0, skippedLow = 0;
            float siteGround = H(SiteX, SiteZ);
            for (float gx = -Radius; gx <= Radius; gx += Grid)
                for (float gz = -Radius; gz <= Radius; gz += Grid)
                {
                    if (gx * gx + gz * gz > Radius * Radius) continue;
                    float x = SiteX + gx, z = SiteZ + gz;
                    if (towerBounds.Contains(new Vector3(x, towerBounds.center.y, z))) { skippedTower++; continue; }
                    if (trees.Any(t => (t - new Vector2(x, z)).sqrMagnitude < TreeClear * TreeClear)) { skippedTree++; continue; }
                    float g = H(x, z);
                    if (float.IsNaN(g)) continue;
                    if (g < siteGround - RidgeBand) { skippedLow++; continue; }
                    var b = Best(x, z, g + Eye, out int q);
                    if (b.yaw < 0f) { skippedNone++; continue; }
                    eyes.Add((x, z, g, b, q));
                }
            var ranked = eyes.OrderBy(e => e.best.nearTrees).ThenByDescending(e => e.best.skyline).ThenByDescending(e => e.ground).ToList();
            var picks = new List<(float x, float z, float ground, Yaw best, int qualifying)>();
            foreach (var e in ranked)
            {
                if (picks.Any(p => new Vector2(p.x - e.x, p.z - e.z).magnitude < Spacing)) continue;
                picks.Add(e);
                if (picks.Count == 3) break;
            }

            // Candidate 0: the current pose and heading.
            var cur = FrustumSpots.Defaults(FarViewProbe.CabHeight).First(p => p.name == FrustumSpots.RidgeTop);
            float cg = H(cur.x, cur.z);
            var c0 = Score(cur.x, cur.z, cg + Eye, cur.yaw);

            string Row(string name, int order, float x, float z, float g, Yaw y, int q, string source) =>
                $"{{\"name\":\"{name}\",\"order\":{order},\"x\":{F(x)},\"z\":{F(z)},\"ground_y\":{F(g)},\"eye_y\":{F(g + Eye)},\"yaw\":{F(y.yaw)}," +
                $"\"skyline_m\":{F(y.skyline)},\"far_visible\":{(y.farVisible ? "true" : "false")},\"near_trees\":{y.nearTrees},\"qualifying_yaws\":{q}," +
                $"\"distance_from_site_m\":{F(new Vector2(x - SiteX, z - SiteZ).magnitude)},\"fcPose\":\"{name}={F(x)},{F(z)},{F(y.yaw)},0\",\"source\":\"{FrustumMath.Esc(source)}\"}}";
            var rows = new List<string> { Row("ridge_top", 0, cur.x, cur.z, cg, c0, c0.Qualifies ? 1 : 0, "current FrustumSpots.Defaults ridge_top at its own yaw " + F(cur.yaw)) };
            for (int i = 0; i < picks.Count; i++)
            {
                var e = picks[i];
                rows.Add(Row($"ridge_c{i + 1}", i + 1, e.x, e.z, e.ground, e.best, e.qualifying,
                    $"RidgeCandidates rank {i + 1} of {eyes.Count} qualifying eyes: 10 m grid within 150 m of the LookoutTower site (map_vectors + site offset), ground within 15 m of the site ground, fewest terrain trees within 6 m of the ray to 200 m, then longest terrain skyline"));
            }
            var json = $"{{\"issue\":9,\"rule\":\"#25 note_technical-director_plan_pose_addendum\",\"site\":[{F(SiteX)},{F(SiteZ)}],\"radius_m\":{F(Radius)},\"grid_m\":{F(Grid)},\"eye_m\":{F(Eye)}," +
                       $"\"far_m\":{F(Far)},\"tree_lateral_m\":{F(TreeLateral)},\"tree_ray_max_m\":{F(TreeRayMax)},\"tower_bounds\":[{F(towerBounds.min.x)},{F(towerBounds.min.z)},{F(towerBounds.max.x)},{F(towerBounds.max.z)}]," +
                       $"\"trees_considered\":{trees.Count},\"eyes_qualifying\":{eyes.Count},\"site_ground_y\":{F(siteGround)},\"ridge_band_m\":{F(RidgeBand)},\"skipped\":{{\"tower\":{skippedTower},\"tree\":{skippedTree},\"below_ridge_band\":{skippedLow},\"no_qualifying_yaw\":{skippedNone}}}," +
                       $"\"candidates\":[{string.Join(",", rows)}]}}";
            var root = Path.GetDirectoryName(Application.dataPath);
            File.WriteAllText(Path.Combine(root, Out), json + "\n");
            return $"written {Out}: {eyes.Count} qualifying eyes; candidate0 skyline {F(c0.skyline)} far_visible {c0.farVisible} near_trees {c0.nearTrees}; picks " +
                   string.Join(" | ", picks.Select(p => $"({F(p.x)},{F(p.z)}) yaw {F(p.best.yaw)} trees {p.best.nearTrees} skyline {F(p.best.skyline)}"));
        }
    }
}
