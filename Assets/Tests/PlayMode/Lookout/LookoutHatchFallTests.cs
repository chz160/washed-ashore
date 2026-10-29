using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using WashedAshore.Lookout;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Lookout
{
    /// <summary>
    /// #8 S2, no drop off the flight across the open hatch (qa-8, reworded after Noah's ruling: walking level into the opening goes down flight 8, which is the stairs).
    /// Hatch open, the player on the cab floor 0.5 to 1.3 m from the opening's centre, W held for 4 s toward it from 8 compass directions (the flight direction plus k x 45
    /// degrees), once walking and once with Shift, level pitch; plus the look-down descent row. A row that cannot start (a wall or the cab band within the capsule, or no
    /// floor) is reported with the blocking colliders and is not a drop.
    /// DROP: any frame where the player's lower sphere is more than 0.10 m clear of the nearest player-mask surface below it, measured along the surface normal (a sphere cast
    /// down from 0.3 m above its resting place, gap = travel x normal.y; the plain ray under the feet reads about 0.21 m on a 52 degree ramp because the round capsule rides
    /// above the surface under its centre, so it is printed only as information), or a row that ends with nothing under it. The old "feet below the cab floor top - 0.10" is
    /// kept as the column "descended". Per-frame trace of every started row: Logs/hatch-fall-*-trace.csv. Table: Logs/hatch-fall-*.txt.
    /// </summary>
    public class LookoutHatchFallTests : LookoutFixture
    {
        const float DropGap = 0.10f, DescendedDepth = 0.10f, RowSeconds = 4f, CastLift = 0.3f;

        class Row
        {
            public string id, why = "", endSurface = "-"; public bool shift, kfRow, valid = true;
            public float yaw, dist, minRel = float.MaxValue, endRel, maxAir, maxGap = float.NegativeInfinity, maxRay = float.NegativeInfinity, restGap = float.NaN, secDrop;
            public int noSurfaceFrames;
            public bool inOpening, endNoSurface;
            public readonly HashSet<string> surfaces = new HashSet<string>();
            public bool Drop => valid && (maxGap > DropGap || noSurfaceFrames > 0 || endNoSurface);
            public bool Descended => valid && (minRel < -DescendedDepth || endRel < -DescendedDepth);
        }

        readonly StringBuilder trace = new StringBuilder();

        /// <summary>Explicit: it measures the open-hatch walk on main, where the 3 startable headings walk down flight 8 (no drop expected) and 5 headings have no start by geometry.</summary>
        [UnityTest, Timeout(600000), Explicit("S2 measuring test: walking level into the open hatch goes down flight 8 (the stairs); 5 of 8 headings cannot start in the cab corner; trap door / opening geometry is gate item 6, deferred")]
        public IEnumerator S2_NoDropOffTheFlightAcrossTheOpenHatch() => Measure("main", false);

        /// <summary>Known fail: one extra row starts with the feet 0.9 m above the opening, so the player really falls; it must read DROP and trip the drop assert.</summary>
        [UnityTest, Timeout(600000), Explicit("known-fail: a row that starts above the opening must read DROP"), Category("KnownFail")]
        public IEnumerator KF_S2_StartAboveTheOpeningReadsDrop() => Measure("kf", true);

        /// <summary>The lower sphere's gap to the nearest player-mask surface below it along the surface normal, that surface's name, and the plain ray clearance under the feet.</summary>
        float Gap(out string surface, out float rayClear)
        {
            Vector3 p = player.transform.position;
            float r = capsule.radius;
            Vector3 lower = p + capsule.center - Vector3.up * (capsule.height * 0.5f - r);
            float best = float.MaxValue, gap = float.NaN; surface = "none";
            foreach (var h in Physics.SphereCastAll(lower + Vector3.up * CastLift, r, Vector3.down, 4f, WorldLayers.PlayerCollisionMask, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(player.transform) && h.distance > 0f && h.distance < best) { best = h.distance; gap = (h.distance - CastLift) * Mathf.Max(h.normal.y, 0.01f); surface = h.collider.name; }
            rayClear = float.NaN; float rb = float.MaxValue;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * CastLift, Vector3.down, 4f, WorldLayers.PlayerCollisionMask, QueryTriggerInteraction.Ignore))
                if (!h.collider.transform.IsChildOf(player.transform) && h.distance < rb) { rb = h.distance; rayClear = p.y - h.point.y; }
            return gap;
        }

        IEnumerator Measure(string tag, bool withKnownFail)
        {
            yield return ReachCab();   // hatch open, player on the cab floor clear of the hatch
            trace.Clear();
            trace.AppendLine("row,t,feetX,feetY,feetZ,grounded,surface,sphereGap(normal),rayClearUnderFeet");
            var opening = hatch.OpeningBounds;
            float floorTop = opening.center.y;
            var top = ReadManifest().flights.Last();
            Vector3 foot = tower.TransformPoint(new Vector3(top.foot[0], top.foot[1], top.foot[2]));
            Vector3 head = tower.TransformPoint(new Vector3(top.head[0], top.head[1], top.head[2]));
            Vector3 dirH = Flat(head - foot).normalized;
            float baseYaw = Mathf.Atan2(dirH.x, dirH.z) * Mathf.Rad2Deg;
            var rows = new List<Row>();
            Vector3 centre = new Vector3(opening.center.x, floorTop, opening.center.z);

            foreach (bool shift in new[] { false, true })
                for (int k = 0; k < 8; k++)
                {
                    var row = new Row { id = $"dir{k * 45}{(shift ? "+shift" : "")}", shift = shift, yaw = baseYaw + 45f * k };
                    Vector3 toward = Quaternion.Euler(0f, row.yaw, 0f) * Vector3.forward;
                    Vector3 start = default;
                    row.valid = false;
                    foreach (float d in new[] { 1.3f, 1.1f, 0.9f, 0.7f, 0.5f })
                    {
                        start = centre - toward * d + Vector3.up * 0.05f;
                        string why = StartProblem(start, floorTop);
                        row.why += $"{d:F2}:{why ?? "clear"} ";
                        if (why == null) { row.dist = d; row.valid = true; break; }
                    }
                    if (row.valid) yield return Walk(row, start, floorTop, opening, 0f);
                    rows.Add(row);
                }

            var descent = new Row { id = "descent(look down 40, W)", yaw = baseYaw + 180f, dist = 0.15f };
            yield return Walk(descent, head - dirH * 0.15f + Vector3.up * 0.05f, floorTop, opening, 40f);
            rows.Add(descent);

            if (withKnownFail)
            {
                var kf = new Row { id = "KF start 0.9 m above the opening", kfRow = true, yaw = baseYaw, dist = 0f };
                yield return Walk(kf, centre + Vector3.up * 0.9f, floorTop, opening, 0f, noKeys: true);
                rows.Add(kf);
            }

            var report = new StringBuilder($"S2 {tag}: hatch {hatch.State}, cab floor top y {floorTop:F3}; DROP = lower sphere more than {DropGap:F2} m above the surface under it (along its normal) at any frame, or nothing under it; {RowSeconds:F0} s per row at {1f / DeltaTime:F0} fps\n");
            report.AppendLine("row | yaw | start dist | shift | DROP verdict | max sphere gap (rest gap at row start) | seconds over the drop gap | frames with no surface | surfaces stood on | end surface | max ray clearance under the feet | descended: min feet rel floor top / end rel | longest !grounded s | feet over opening | start check (distance: result)");
            int drops = 0, noStart = 0;
            foreach (var r in rows)
            {
                string verdict = !r.valid ? "NO START (geometry)" : r.Drop ? "DROP" : "no drop";
                if (!r.valid) noStart++; else if (r.Drop) drops++;
                report.AppendLine($"{r.id} | {r.yaw:F0} | {r.dist:F2} | {r.shift} | {verdict} | {(r.valid ? r.maxGap.ToString("F3") : "-")} ({r.restGap:F3}) | {r.secDrop:F2} s | {r.noSurfaceFrames} | {string.Join(" ", r.surfaces)} | {r.endSurface} | {(r.valid ? r.maxRay.ToString("F3") : "-")} | {(r.valid ? r.minRel.ToString("F3") + " / " + r.endRel.ToString("F3") : "-")}{(r.Descended ? " (descended)" : "")} | {r.maxAir:F2} | {r.inOpening} | {r.why}");
            }
            string failure = drops > 0 ? $"{drops} row(s) with a DROP: {string.Join(", ", rows.Where(r => r.Drop).Select(r => r.id))}" : null;
            report.AppendLine($"started rows {rows.Count - noStart} of {rows.Count}; {noStart} could not start by geometry (see the start check column)");
            report.AppendLine($"RESULT {tag} {(failure == null ? "PASS (no drop)" : "FAIL: " + failure)}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText($"Logs/hatch-fall-{tag}.txt", report.ToString());
            File.WriteAllText($"Logs/hatch-fall-{tag}-trace.csv", trace.ToString());
            Debug.Log(report.ToString());
            Assert.IsNull(failure, failure);
        }

        /// <summary>The feet at <paramref name="feet"/> overlap only the cab floor, and floor lies under them.</summary>
        string StartProblem(Vector3 feet, float floorTop)
        {
            float r = capsule.radius, h = capsule.height;
            var hits = Physics.OverlapCapsule(feet + Vector3.up * r, feet + Vector3.up * (h - r), r + 0.02f, ~0, QueryTriggerInteraction.Ignore)
                .Where(c => !c.transform.IsChildOf(player.transform) && !c.name.StartsWith("COL_BOX_CabFloor")).ToArray();
            if (hits.Length > 0) return "overlaps " + string.Join("+", hits.Select(c => c.name).Distinct().Take(3));
            if (!Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out var hit, 0.6f, ~0, QueryTriggerInteraction.Ignore)) return "no floor below";
            return hit.point.y > floorTop - 0.1f ? null : $"floor below at {hit.point.y - floorTop:F2} ({hit.collider.name})";
        }

        IEnumerator Walk(Row row, Vector3 start, float floorTop, Bounds opening, float pitch, bool noKeys = false)
        {
            LetAll();
            if (row.kfRow) { TestSpawnReposition(start, row.yaw); watch.Forget(); }
            else { yield return StartAt(start, row.yaw); player.SetLook(row.yaw, pitch); yield return Wait(0.3f); }
            if (!noKeys)
            {
                Hold(Key.W);
                yield return null;
                if (row.shift) { Hold(Key.LeftShift); yield return null; }
            }
            float air = 0f, t0 = Time.time;
            for (float t = 0f; t < RowSeconds; t += Time.deltaTime)
            {
                if (!row.kfRow) player.SetLook(row.yaw, pitch);
                yield return null;
                var p = player.transform.position;
                float gap = Gap(out string surface, out float ray);
                if (float.IsNaN(row.restGap) && player.IsGrounded) row.restGap = gap;
                trace.AppendLine($"{row.id},{Time.time - t0:F3},{p.x:F3},{p.y:F3},{p.z:F3},{(player.IsGrounded ? 1 : 0)},{surface},{gap:F3},{ray:F3}");
                if (float.IsNaN(gap)) row.noSurfaceFrames++;
                else
                {
                    row.maxGap = Mathf.Max(row.maxGap, gap); row.surfaces.Add(surface);
                    if (gap > DropGap) row.secDrop += Time.deltaTime;
                }
                if (!float.IsNaN(ray)) row.maxRay = Mathf.Max(row.maxRay, ray);
                row.minRel = Mathf.Min(row.minRel, p.y - floorTop);
                if (!player.IsGrounded) { air += Time.deltaTime; row.maxAir = Mathf.Max(row.maxAir, air); } else air = 0f;
                if (p.x > opening.min.x && p.x < opening.max.x && p.z > opening.min.z && p.z < opening.max.z) row.inOpening = true;
            }
            row.endRel = player.transform.position.y - floorTop;
            float endGap = Gap(out row.endSurface, out _);
            row.endNoSurface = float.IsNaN(endGap) || endGap > DropGap;
            LetAll();
            yield return null;
        }

        void TestSpawnReposition(Vector3 feet, float yaw) => WashedAshore.Tests.Support.TestSpawn.Reposition(player, feet, yaw);
    }
}
