using System.IO;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>Start placement, face finding, stopper attribution and CSV output for the B5/B6 sweep.</summary>
    public partial class WorldBoundsSweepTests
    {
        static readonly RaycastHit[] FaceHits = new RaycastHit[16];
        const string BarrierRoot = "NorthBarrier"; // level-2's BarrierBuilder.RootName (Editor assembly, so not referenced)

        /// <summary>
        /// designer-2: the start sits StartSouth from the line along the approach; if it isn't walkable land it slides
        /// along the ray toward the line to the first walkable point that still leaves MinRunUp before the first face.
        /// False (with the reason) when there is none.
        /// </summary>
        bool TryStart(float x, float yaw, out Vector3 start, out float pathLen, out string why)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var target = new Vector3(x, 0f, rule.LineZAt(x));
            pathLen = StartSouth / Mathf.Cos(yaw * Mathf.Deg2Rad);
            start = target - dir * pathLen;
            string first = null;
            for (float back = pathLen; back > 0f; back -= 1f)
            {
                var s = target - dir * back;
                if (FaceDistance(s, dir) < MinRunUp) break;
                if (Walkable(s, out why)) { start = s; pathLen = back; return true; }
                first ??= $"{why} at the {StartSouth:F0} m start";
            }
            why = (first ?? "no start") + $"; no walkable point with >= {MinRunUp:F0} m run-up";
            return false;
        }

        /// <summary>Lake bed and wall ends: the plain StartSouth start, no walkability slide (the lake bed is the point).</summary>
        bool FixedStart(float x, float yaw, out Vector3 start, out float pathLen, out string why)
        {
            pathLen = StartSouth / Mathf.Cos(yaw * Mathf.Deg2Rad);
            start = new Vector3(x, 0f, rule.LineZAt(x)) - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * pathLen;
            why = TerrainQuery.TryGroundHeight(start, out _) ? null : $"start {start:F0} is off the terrain";
            return why == null;
        }

        /// <summary>The ground from <paramref name="start"/> to the first face ahead is walkable: heightmap slope &lt;= MaxStartSlope
        /// every 1 m. Used only to pick replacement starts; the original point always runs.</summary>
        bool ApproachWalkable(Vector3 start, Vector3 dir)
        {
            float face = FaceDistance(start, dir);
            for (float d = 0f; d <= face; d += 1f)
                if (TerrainQuery.Steepness(start + dir * d) > MaxStartSlope) return false;
            return true;
        }

        bool Walkable(Vector3 p, out string why)
        {
            why = null;
            if (!TerrainQuery.TryGroundHeight(p, out float h)) why = "off the terrain";
            else if (h <= water + 0.5f) why = "not above WaterLevelY";
            else if (TerrainQuery.Steepness(p) > MaxStartSlope) why = $"slope > {MaxStartSlope}";
            return why == null;
        }

        /// <summary>Distance from <paramref name="from"/> along <paramref name="dir"/> to the first barrier face (non-terrain
        /// collider at knee or chest height) or the backstop's south face. Terrain trees and rocks are part of the
        /// TerrainCollider, so they are never a face.</summary>
        float FaceDistance(Vector3 from, Vector3 dir)
        {
            from.y = TerrainQuery.Height(from);
            return NearestFace(from, dir);
        }

        /// <summary>Distance along <paramref name="dir"/> from the player's feet to the next face ahead.</summary>
        float NextFace(Vector3 dir) => NearestFace(player.transform.position, dir);

        float NearestFace(Vector3 feet, Vector3 dir)
        {
            float best = (rule.LineZAt(feet.x) - wall - feet.z) / Mathf.Max(0.01f, dir.z);
            foreach (float h in new[] { 0.5f, 1.2f })
            {
                int n = Physics.RaycastNonAlloc(feet + Vector3.up * h, dir, FaceHits, Mathf.Max(0.1f, best), ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = FaceHits[i].collider;
                    if (c is TerrainCollider || c.transform.IsChildOf(player.transform)) continue;
                    best = Mathf.Min(best, FaceHits[i].distance);
                }
            }
            return best;
        }

        /// <summary>qa-2: what stopped a run short of its face. A TerrainCollider hit above the ground is a terrain
        /// tree or rock; at ground level it is the slope.</summary>
        /// <param name="barrierHit">Path of the nearest collider ahead if it belongs to the barrier or backstop (within reach), else null.</param>
        string Stopper(Vector3 dir, out string barrierHit)
        {
            var p = player.transform.position;
            string found = null;
            barrierHit = null;
            float bestDist = float.MaxValue;
            foreach (float h in new[] { 0.3f, 0.9f, 1.5f })
            {
                int n = Physics.RaycastNonAlloc(p + Vector3.up * h, dir, FaceHits, 3f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var hit = FaceHits[i];
                    if (hit.collider.transform.IsChildOf(player.transform) || hit.distance >= bestDist) continue;
                    bestDist = hit.distance;
                    if (hit.collider is TerrainCollider)
                    {
                        barrierHit = null;
                        found = hit.point.y > TerrainQuery.Height(hit.point) + 0.3f ? $"terrain tree/rock ({hit.collider.name})" : $"terrain slope ({TerrainQuery.Steepness(hit.point):F0} deg)";
                    }
                    else
                    {
                        string root = hit.collider.transform.root.name;
                        found = $"{hit.collider.name} ({root})";
                        barrierHit = hit.distance <= FaceSlack + cc.radius && (root == BarrierRoot || root == "WorldBounds") ? PathOf(hit.collider.transform) : null;
                    }
                }
            }
            return found ?? $"nothing within 3 m (slope {TerrainQuery.Steepness(p):F0} deg underfoot)";
        }

        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;

        /// <summary>Skip table (qa-2) to bb-b5-skips.csv and every run to bb-b5-runs.csv; both are appended, never deleted.</summary>
        void WriteCsv(string testCase, Tally tally)
        {
            Debug.Log($"WorldBoundsSweepTests[{testCase}] skip table (index,x,case,reason,replacementX):\n" +
                      (tally.skipRows.Count == 0 ? "(none)" : string.Join("\n", tally.skipRows)));
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "TestResults"));
            Directory.CreateDirectory(dir);
            string lineZ = rule.LineZAt(0f).ToString("F1");
            Append(Path.Combine(dir, "bb-b5-skips.csv"), "lineZ,index,x,case,reason,replacementX", lineZ, tally.skipRows);
            Append(Path.Combine(dir, "bb-b5-runs.csv"), "lineZ,case,index,x,valid,validity,maxZ,stopper,faceAhead,travel,clampSnaps,peakSpeed", lineZ, tally.runRows);
        }

        static void Append(string path, string header, string lineZ, System.Collections.Generic.List<string> rows)
        {
            if (!File.Exists(path)) File.WriteAllText(path, header + "\n");
            foreach (var row in rows) File.AppendAllText(path, $"{lineZ},{row}\n");
        }
    }
}
