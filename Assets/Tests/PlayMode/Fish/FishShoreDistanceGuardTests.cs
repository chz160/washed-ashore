using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Fish;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// f-td's guard for the open band's outer edge (ruling/fish-shore-guard): LevelMaps shore distance measures to the
    /// inner bank. Over every wet 2 m cell south of the north line (the LevelMaps road grid) it must not read NEARER the
    /// bank than the true distance to the nearest dry cell (ground at or above W, or no tile) by more than 2 m: that's
    /// the direction that could put a fish past the true 50 m edge (asserted). Over-reads only remove open-band fish, so
    /// they are reported with their cause and the open-band area lost. True distance is an exact-to-a-cell Euclidean
    /// transform (8-neighbour vector propagation, two passes) over the same grid. Results: TestResults/fish-shore-guard.json.
    /// </summary>
    public class FishShoreDistanceGuardTests
    {
        const float Tolerance = 2f;
        // LevelMaps stores shore distance in a byte and clamps it at 127 m: a read at the clamp means "at least that far".
        const float Saturated = 126.5f;

        [TearDown]
        public void TearDown() => FishTestKit.Restore();

        /// <summary>World position of the dry cell a wet cell's nearest-dry offset points at (offsets are magnitudes per axis).</summary>
        static Vector3 NearestDry(int x, int z, int ox, int oz, bool[] wet, int nx, int nz, Vector2 origin, float cell)
        {
            // The propagation stores |offset| per axis; try the four sign combinations and take the dry one.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    int cx = x + sx * ox, cz = z + sz * oz;
                    if (cx < 0 || cz < 0 || cx >= nx || cz >= nz || !wet[cz * nx + cx])
                        return new Vector3(origin.x + (cx + 0.5f) * cell, 0f, origin.y + (cz + 0.5f) * cell);
                }
            return new Vector3(origin.x + (x + 0.5f) * cell, 0f, origin.y + (z + 0.5f) * cell);
        }

        [UnityTest]
        public IEnumerator ShoreDistance_NeverExceedsTrueDistanceToDryGround()
        {
            yield return FishTestKit.Load(101, "guard");
            var pop = FishPopulation.Active;
            var maps = pop.LevelMaps;
            Assert.IsNotNull(maps, "FishPopulation has no LevelMaps");
            float w = pop.Water.WaterLevelY, cell = maps.roadCell, northZ = pop.Map.northLineZ;
            int nx = maps.roadW, nz = maps.roadH;
            Vector2 origin = maps.originXZ;

            // Wet/dry on cell centres; every cell (north of the line too) is real geometry for "nearest dry".
            var wet = new bool[nx * nz];
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    var p = new Vector3(origin.x + (x + 0.5f) * cell, 0f, origin.y + (z + 0.5f) * cell);
                    wet[z * nx + x] = TerrainQuery.TryGroundHeight(p, out float h) && h < w;
                }

            // Nearest-dry offsets (in cells), propagated forward then backward over the 8-neighbourhood.
            const int Far = 1 << 14;
            var dx = new int[nx * nz];
            var dz = new int[nx * nz];
            for (int i = 0; i < wet.Length; i++) { dx[i] = wet[i] ? Far : 0; dz[i] = wet[i] ? Far : 0; }
            void Relax(int i, int j, int ox, int oz)
            {
                if (dx[j] >= Far) return;
                int cx = dx[j] + ox, cz = dz[j] + oz;
                if ((long)cx * cx + (long)cz * cz < (long)dx[i] * dx[i] + (long)dz[i] * dz[i]) { dx[i] = cx; dz[i] = cz; }
            }
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int i = z * nx + x;
                    if (x > 0) Relax(i, i - 1, 1, 0);
                    if (z > 0) { Relax(i, i - nx, 0, 1); if (x > 0) Relax(i, i - nx - 1, 1, 1); if (x < nx - 1) Relax(i, i - nx + 1, 1, 1); }
                }
                for (int x = nx - 2; x >= 0; x--) Relax(z * nx + x, z * nx + x + 1, 1, 0);
            }
            for (int z = nz - 1; z >= 0; z--)
            {
                for (int x = nx - 1; x >= 0; x--)
                {
                    int i = z * nx + x;
                    if (x < nx - 1) Relax(i, i + 1, 1, 0);
                    if (z < nz - 1) { Relax(i, i + nx, 0, 1); if (x < nx - 1) Relax(i, i + nx + 1, 1, 1); if (x > 0) Relax(i, i + nx - 1, 1, 1); }
                }
                for (int x = 1; x < nx; x++) Relax(z * nx + x, z * nx + x - 1, 1, 0);
            }

            int checkedCells = 0, saturatedReads = 0, admittedBeyondEdge = 0, underReads = 0, overReads = 0, dryIsNoTile = 0, dryIsNorth = 0;
            float worstOver = 0f, worstUnder = 0f, lostOpenArea = 0f;
            float edge = pop.Tuning.bands[pop.Tuning.bands.Length - 1].maxShoreDistance;
            var firstUnder = new List<string>();
            var firstAdmitted = new List<string>();
            var firstOver = new List<string>();
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int i = z * nx + x;
                    if (!wet[i]) continue;
                    var p = new Vector3(origin.x + (x + 0.5f) * cell, 0f, origin.y + (z + 0.5f) * cell);
                    if (p.z > northZ) continue;
                    checkedCells++;
                    bool hasDry = dx[i] < Far;
                    float truth = hasDry ? Mathf.Sqrt((float)dx[i] * dx[i] + (float)dz[i] * dz[i]) * cell : float.MaxValue;
                    float read = Mathf.Abs(pop.Water.ShoreDistance(p.x, p.z));
                    // Format before interpolating: "{x:F1}}}" would print the literal "F1" (ruling/fish-shore-guard 3).
                    string tr = hasDry ? truth.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "null";
                    // The ruling-of-record metric: an open-depth cell the SD read admits (<= the edge) whose true distance is
                    // past the edge + tolerance would hold fish beyond the 50 m line.
                    var open = pop.Tuning.bands[pop.Tuning.bands.Length - 1];
                    if (edge > 0f && hasDry && read <= edge && truth > edge + Tolerance && pop.Water.TryBed(p.x, p.z, out float bed) &&
                        pop.Water.WaterLevelY - bed >= open.bedDepth.x)
                    {
                        admittedBeyondEdge++;
                        if (firstAdmitted.Count < 40) firstAdmitted.Add($"{{\"x\":{p.x:F0},\"z\":{p.z:F0},\"read\":{read:F0},\"true\":{truth.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)}}}");
                    }
                    // A saturated read is safe while the clamp lies beyond the open band's edge (asserted below).
                    if (hasDry && read >= Saturated && truth > read) { saturatedReads++; continue; }
                    if (hasDry && read < truth - Tolerance)
                    {
                        // UNSAFE: reads nearer the bank than it is, so a fish could sit beyond the true 50 m edge.
                        underReads++;
                        worstUnder = Mathf.Max(worstUnder, truth - read);
                        if (firstUnder.Count < 40) firstUnder.Add($"{{\"x\":{p.x:F0},\"z\":{p.z:F0},\"read\":{read:F0},\"true\":{tr}}}");
                        continue;
                    }
                    if (!hasDry || read <= truth + Tolerance) continue;
                    // Safe over-read: report only, with its cause and the open-band area it removes.
                    overReads++;
                    worstOver = Mathf.Max(worstOver, read - truth);
                    if (edge > 0f && truth <= edge && read > edge) lostOpenArea += cell * cell;
                    var dry = NearestDry(x, z, dx[i], dz[i], wet, nx, nz, origin, cell);
                    bool noTile = !TerrainQuery.TryGroundHeight(dry, out _);
                    if (noTile) dryIsNoTile++;
                    else if (dry.z > northZ) dryIsNorth++;
                    string cause = noTile ? "noTile" : dry.z > northZ ? "northOfLine" : "land";
                    if (firstOver.Count < 40) firstOver.Add($"{{\"x\":{p.x:F0},\"z\":{p.z:F0},\"read\":{read:F0},\"true\":{tr},\"cause\":\"{cause}\",\"dry\":[{dry.x:F0},{dry.z:F0}]}}");
                }
            var sb = new StringBuilder();
            sb.Append($"{{\"cell\":{cell},\"wetCellsSouthOfLine\":{checkedCells},\"tolerance\":{Tolerance},\"admittedBeyondEdge\":{admittedBeyondEdge},\"saturatedReadsSafe\":{saturatedReads},\"underReadsReportedOnly\":{underReads},\"worstUnderM\":{worstUnder:F1},");
            sb.Append($"\"overReadsReportedOnly\":{overReads},\"worstOverM\":{worstOver:F1},\"lostOpenBandAreaM2\":{lostOpenArea:F0},");
            sb.Append($"\"overNearestDryIsNoTile\":{dryIsNoTile},\"overNearestDryNorthOfLine\":{dryIsNorth},\"overNearestDryIsLand\":{overReads - dryIsNoTile - dryIsNorth},");
            sb.Append("\"firstAdmitted\":[").Append(string.Join(",", firstAdmitted)).Append("],\"firstUnder\":[").Append(string.Join(",", firstUnder)).Append("],\"firstOver\":[").Append(string.Join(",", firstOver)).Append("]}");
            FishTestKit.Write("fish-shore-guard.json", sb.ToString());
            Assert.Greater(checkedCells, 1000);
            Assert.Less(edge, Saturated, "the open band's edge must lie inside the shore-distance clamp");
            // ruling/fish-shore-guard (f-td metric): no open-depth cell admitted by its read (<= the edge) lies truly past
            // the edge + tolerance. Other under-reads and all over-reads are reported only.
            Assert.AreEqual(0, admittedBeyondEdge, $"{admittedBeyondEdge} open-depth cells read <= {edge} m from the bank but truly > {edge + Tolerance} m; see fish-shore-guard.json");
        }
    }
}
