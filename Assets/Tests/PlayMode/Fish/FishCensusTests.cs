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
    /// Spec F3 (ruling/fish-depth-bands rev 4; f-qa review 1-2): the census of the whole virtual population in World.unity
    /// from the plan the game uses (FishPlan.Cell over every cell of the terrain south of the line), graded per m² inside
    /// each band with the same band function as the plan (FishPlan.BandAt on the plan's 2 m grid):
    ///  - adults/m² outside structure per band against bands[b].DensityPerM2, +/-25% per seed;
    ///  - school units/m² per band, pooled over the 3 seeds, +/-25% or +/-2 schools;
    ///  - species mix per band outside structure, in fish against brief share x band density x band area: per seed
    ///    (+/-25%) where expected groups per seed >= 10, else pooled over the seeds at +/-max(25%, 2 sigma), at most
    ///    +/-50% (f-qa's reading; sigma from a compound Poisson of groups x uniform group size);
    ///  - structure: adults/m² per type and band against multiplier x band density (+ extras), pooled over seeds; types with
    ///    fewer than 20 expected adults in the pool are listed as ungraded;
    ///  - no group in the open band farther than its 50 m edge, and none shallower than its species' minimum.
    /// Per-100 m figures are reported only. Writes TestResults/fish-census.json.
    /// </summary>
    public class FishCensusTests
    {
        const float Tol = 0.25f;
        static readonly float[] DepthEdges = { 0.35f, 1f, 2f, 5f, 9.9f };
        static readonly float[] OutEdges = { 7f, 20f, 50f, 100f };

        class Tally
        {
            public float[] bandArea, adults, schools, schoolExtras;
            public float[,] mix, structArea, structAdults;
            public int[,] hist;
            public int tooShallow, beyondEdge;
            public List<float>[] lengths;
            public float[] belowMinAny;
            public float[] diag;                                                    // FishPlan.Diagnostics summed over the map
            public float fallbackAdults, fallbackSchools;                           // FishPlan's class fallbacks over the map                                            // in-band area shallower than every species' minimum
            public readonly SortedDictionary<string, float> excluded = new SortedDictionary<string, float>(); // no-band area by reason
        }

        /// <summary>Why a plan sample has no band (f-qa's reconciliation), by area.</summary>
        static void Exclude(Tally c, FishTuning t, float northZ, float x, float z, float area)
        {
            string why;
            var p = new Vector3(x, 0f, z);
            if (!TerrainQuery.TryGroundHeight(p, out float g)) why = "noTile";
            else
            {
                float depth = FishPopulation.Active.Water.WaterLevelY - g;
                if (depth <= 0f) why = "land";
                else if (z > northZ) why = "northOfLineWet";
                else if (depth < t.bands[0].bedDepth.x) why = "shallowerThanShelfMin";
                else if (FishPlan.IsFarWater(t, depth)) why = "beyondOpenEdge";
                else why = "outsideEveryBand";
            }
            c.excluded[why] = (c.excluded.TryGetValue(why, out float v) ? v : 0f) + area;
        }

        [TearDown]
        public void TearDown()
        {
            FishPlan.LegacyRelocation = false;
            FishTestKit.Restore();
        }

        [UnityTest]
        public IEnumerator F3_Census_AtThreeSeeds_MatchesTheBrief()
        {
            var tallies = new List<Tally>();
            foreach (int seed in FishTestKit.Seeds)
            {
                yield return FishTestKit.Load(seed, "f3");
                tallies.Add(Count(seed));
            }
            // f-td: the diagnostic before and after the plan-v2 relocation fix. Plan v1 (relocation across the structure
            // boundary) is counted on the same water and seeds for the report only; nothing is graded on it.
            var legacy = new List<Tally>();
            FishPlan.LegacyRelocation = true;
            try { foreach (int seed in FishTestKit.Seeds) legacy.Add(Count(seed)); }
            finally { FishPlan.LegacyRelocation = false; }
            var pop = FishPopulation.Active;
            var t = pop.Tuning;
            int nb = t.bands.Length, ns = t.species.Length, nst = t.structures.Length;
            var failures = new List<string>();
            var sb = new StringBuilder($"{{\"brief\":\"{t.briefRevision}\",\"briefSha16\":\"{t.briefSha16}\",\"seeds\":[{string.Join(",", FishTestKit.Seeds)}],\"bands\":[");

            for (int b = 0; b < nb; b++)
            {
                var band = t.bands[b];
                float schoolTarget = 0f;
                foreach (var sp in t.species) if (sp.schoolUnit) schoolTarget += band.PerM2(sp.schoolsPer100mByBand[b]);
                float mixSum = 0f;
                for (int s = 0; s < ns; s++) if (!t.species[s].schoolUnit) mixSum += t.species[s].per100mByBand[b];
                float excluded = 0f;
                for (int s = 0; s < nst; s++) excluded += tallies[0].structArea[s, b];
                sb.Append(b > 0 ? "," : "").Append($"{{\"band\":\"{band.name}\",\"targetPerM2\":{band.DensityPerM2:F6},\"structureAreaExcludedM2\":{excluded:F0},\"perSeed\":[");
                float pooledSchools = 0f, pooledArea = 0f;
                for (int k = 0; k < tallies.Count; k++)
                {
                    var c = tallies[k];
                    float density = c.adults[b] / Mathf.Max(1f, c.bandArea[b]);
                    bool ok = Within(density, band.DensityPerM2);
                    if (!ok) failures.Add($"seed {FishTestKit.Seeds[k]} {band.name}: {density:F5} adults/m2 vs {band.DensityPerM2:F5}");
                    pooledSchools += c.schools[b];
                    pooledArea += c.bandArea[b];
                    float structHere = 0f;
                    for (int st = 0; st < nst; st++) structHere += c.structArea[st, b];
                    // Areas are south of the line only; outside + structure = the band's whole area (f-qa's check).
                    sb.Append(k > 0 ? "," : "").Append($"{{\"areaM2\":{c.bandArea[b]:F0},\"structureAreaM2\":{structHere:F0},\"totalAreaM2\":{c.bandArea[b] + structHere:F0},\"belowMinBedDepthAnyM2\":{c.belowMinAny[b]:F0},\"adults\":{c.adults[b]},\"adultsPerM2\":{density:F6},\"pass\":{B(ok)}}}");
                }
                // Species mix (f-qa reading): expected fish = brief share x band density x band area (outside structure);
                // expected groups lambda = E / mean group size; sigma from a compound Poisson (uniform group size). Graded
                // per seed (+/-25%) where lambda >= 10 per seed, else pooled over the seeds at +/-max(25%, 2 sigma), at
                // most +/-50%.
                sb.Append("],\"mix\":[");
                for (int s = 0, firstM = 1; s < ns; s++)
                {
                    var sp = t.species[s];
                    if (sp.schoolUnit || mixSum <= 0f || sp.per100mByBand[b] <= 0f) continue;
                    float brief = sp.per100mByBand[b] / mixSum, meanGroup = 0.5f * (sp.groupSize.x + sp.groupSize.y);
                    float meanSq = 0f;
                    for (int g = sp.groupSize.x; g <= sp.groupSize.y; g++) meanSq += g * g;
                    meanSq /= sp.groupSize.y - sp.groupSize.x + 1;
                    float pooledE = 0f, pooledGot = 0f, pooledVar = 0f;
                    bool perSeed = true;
                    var seedJson = new List<string>();
                    for (int k = 0; k < tallies.Count; k++)
                    {
                        var c = tallies[k];
                        float e = brief * band.DensityPerM2 * c.bandArea[b], lambda = e / meanGroup, got = c.mix[s, b];
                        perSeed &= lambda >= 10f;
                        pooledE += e; pooledGot += got; pooledVar += lambda * meanSq;
                        seedJson.Add($"{{\"expected\":{e:F1},\"lambda\":{lambda:F1},\"sigma\":{Mathf.Sqrt(lambda * meanSq):F1},\"got\":{got}}}");
                    }
                    bool pass;
                    string mode;
                    if (perSeed)
                    {
                        mode = "perSeed";
                        pass = true;
                        for (int k = 0; k < tallies.Count; k++)
                        {
                            float e = brief * band.DensityPerM2 * tallies[k].bandArea[b], got = tallies[k].mix[s, b];
                            if (Mathf.Abs(got - e) > Tol * e) { pass = false; failures.Add($"seed {FishTestKit.Seeds[k]} {band.name} {sp.name}: {got} fish vs {e:F1}"); }
                        }
                    }
                    else
                    {
                        mode = "pooled";
                        float tol = Mathf.Min(0.5f * pooledE, Mathf.Max(Tol * pooledE, 2f * Mathf.Sqrt(pooledVar)));
                        pass = Mathf.Abs(pooledGot - pooledE) <= tol;
                        if (!pass) failures.Add($"{band.name} {sp.name} (pooled): {pooledGot} fish vs {pooledE:F1} +/- {tol:F1}");
                    }
                    // Relocation diagnostics (pooled over seeds): expected fish from the brief share before relocation
                    // and after it, outside and inside structure; got is the census count outside structure.
                    float rawOut = 0f, placedOut = 0f, rawIn = 0f, placedIn = 0f, v1PlacedOut = 0f, v1PlacedIn = 0f, v1Got = 0f;
                    int di = (s * nb + b) * 4;
                    foreach (var tl in tallies) { rawOut += tl.diag[di]; placedOut += tl.diag[di + 1]; rawIn += tl.diag[di + 2]; placedIn += tl.diag[di + 3]; }
                    foreach (var tl in legacy) { v1PlacedOut += tl.diag[di + 1]; v1PlacedIn += tl.diag[di + 3]; v1Got += tl.mix[s, b]; }
                    sb.Append(firstM == 1 ? "" : ",").Append($"{{\"species\":\"{sp.name}\",\"briefShare\":{brief:F3},\"mode\":\"{mode}\",\"pass\":{B(pass)},")
                      .Append($"\"planRawOutside\":{rawOut:F1},\"planPlacedOutside\":{placedOut:F1},\"planRawStructure\":{rawIn:F1},\"planPlacedStructure\":{placedIn:F1},")
                      .Append($"\"v1PlacedOutside\":{v1PlacedOut:F1},\"v1PlacedStructure\":{v1PlacedIn:F1},\"v1GotOutside\":{v1Got},")
                      .Append($"\"pooledExpected\":{pooledE:F1},\"pooledGot\":{pooledGot},\"pooledSigma\":{Mathf.Sqrt(pooledVar):F1},\"seeds\":[").Append(string.Join(",", seedJson)).Append("]}");
                    firstM = 0;
                }
                // School units aren't structure-scaled (structure adds only per-anchor extras), so their area is the whole band.
                foreach (var c in tallies) for (int s = 0; s < nst; s++) pooledArea += c.structArea[s, b];
                float extras = 0f;
                foreach (var c in tallies) extras += c.schoolExtras[b];
                float schoolDensity = pooledSchools / Mathf.Max(1f, pooledArea), expectedSchools = schoolTarget * pooledArea + extras;
                bool schoolsOk = Mathf.Abs(pooledSchools - expectedSchools) <= Mathf.Max(2f, Tol * expectedSchools);
                if (!schoolsOk) failures.Add($"{band.name} schools (pooled): {pooledSchools} vs {expectedSchools:F1}");
                sb.Append($"],\"schoolsPooled\":{pooledSchools},\"schoolsExpected\":{expectedSchools:F1},\"schoolsPerM2\":{schoolDensity:F7},\"schoolsPass\":{B(schoolsOk)}}}");
            }

            sb.Append("],\"structure\":[");
            var ungraded = new List<string>();
            bool firstS = true;
            for (int s = 0; s < nst; s++)
                for (int b = 0; b < nb; b++)
                {
                    float area = 0f, adults = 0f;
                    foreach (var c in tallies) { area += c.structArea[s, b]; adults += c.structAdults[s, b]; }
                    if (area <= 0f) continue;
                    var st = t.structures[s];
                    float target = t.bands[b].DensityPerM2 * st.bandMultiplier[b];
                    if (st.extraSpecies >= 0) target += st.extraPer100mShore / (100f * st.reachOut);
                    bool graded = target * area >= 20f, ok = !graded || Within(adults / area, target);
                    if (!graded) ungraded.Add($"{st.kind}/{t.bands[b].name}");
                    if (!ok) failures.Add($"{st.kind}/{t.bands[b].name} (pooled): {adults / area:F5} adults/m2 vs {target:F5}");
                    float v1Adults = 0f;
                    foreach (var tl in legacy) v1Adults += tl.structAdults[s, b];
                    sb.Append(firstS ? "" : ",").Append($"{{\"type\":\"{st.kind}\",\"band\":\"{t.bands[b].name}\",\"areaM2Pooled\":{area:F0},\"adultsPooled\":{adults},\"v1AdultsPooled\":{v1Adults},")
                      .Append($"\"perM2\":{adults / area:F6},\"targetPerM2\":{target:F6},\"graded\":{B(graded)},\"pass\":{B(ok)}}}");
                    firstS = false;
                }
            sb.Append("],\"ungradedStructure\":[").Append(string.Join(",", ungraded.ConvertAll(u => $"\"{u}\""))).Append("],\"perSeed\":[");
            for (int k = 0; k < tallies.Count; k++)
            {
                var c = tallies[k];
                if (c.tooShallow > 0) failures.Add($"seed {FishTestKit.Seeds[k]}: {c.tooShallow} groups shallower than their species' minimum");
                if (c.beyondEdge > 0) failures.Add($"seed {FishTestKit.Seeds[k]}: {c.beyondEdge} groups beyond the open band's edge");
                var excl = new List<string>();
                foreach (var kv in c.excluded) excl.Add($"\"{kv.Key}\":{kv.Value:F0}");
                sb.Append(k > 0 ? "," : "").Append($"{{\"seed\":{FishTestKit.Seeds[k]},\"noBandAreaM2ByReason\":{{{string.Join(",", excl)}}},\"planVersion\":{FishPlan.Version},\"fallbackAdults\":{c.fallbackAdults:F1},\"fallbackSchools\":{c.fallbackSchools:F2},\"tooShallow\":{c.tooShallow},\"beyondEdge\":{c.beyondEdge},\"adultsPer100mReported\":[");
                float shore = ShorelineMetres(pop);
                for (int b = 0; b < nb; b++) sb.Append(b > 0 ? "," : "").Append($"{c.adults[b] / (shore / 100f):F2}");
                sb.Append("],\"histogram\":{\"depthEdges\":[").Append(string.Join(",", DepthEdges)).Append("],\"outEdges\":[").Append(string.Join(",", OutEdges)).Append("],\"counts\":[");
                for (int d = 0; d <= DepthEdges.Length; d++)
                {
                    sb.Append(d > 0 ? ",[" : "[");
                    for (int o = 0; o <= OutEdges.Length; o++) sb.Append(o > 0 ? "," : "").Append(c.hist[d, o]);
                    sb.Append(']');
                }
                sb.Append("]}}");
            }
            sb.Append("],\"failures\":").Append(failures.Count).Append('}');
            FishTestKit.Write("fish-census.json", sb.ToString());
            // F2 size spread: every planned fish's length, per seed and species (the same draws the spawn uses).
            var lens = new StringBuilder("{\"seeds\":[");
            for (int k = 0; k < tallies.Count; k++)
            {
                lens.Append(k > 0 ? "," : "").Append($"{{\"seed\":{FishTestKit.Seeds[k]},\"species\":{{");
                for (int s = 0; s < ns; s++)
                    lens.Append(s > 0 ? "," : "").Append($"\"{t.species[s].name}\":[")
                        .Append(string.Join(",", tallies[k].lengths[s].ConvertAll(v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))).Append(']');
                lens.Append("}}");
            }
            FishTestKit.Write("fish-census-lengths.json", lens.Append("]}").ToString());
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        static string B(bool v) => v ? "true" : "false";
        static bool Within(float v, float target) => target <= 0f ? v == 0f : Mathf.Abs(v - target) <= Tol * target;

        static Tally Count(int seed)
        {
            var pop = FishPopulation.Active;
            var t = pop.Tuning;
            var w = pop.Water;
            int nb = t.bands.Length, ns = t.species.Length, nst = t.structures.Length;
            var c = new Tally
            {
                bandArea = new float[nb], adults = new float[nb], schools = new float[nb], schoolExtras = new float[nb], mix = new float[ns, nb],
                structArea = new float[nst, nb], structAdults = new float[nst, nb], hist = new int[DepthEdges.Length + 1, OutEdges.Length + 1],
                lengths = new List<float>[ns], belowMinAny = new float[nb],
            };
            for (int s = 0; s < ns; s++) c.lengths[s] = new List<float>();
            float northZ = pop.Map.northLineZ;
            Bounds world = default;
            bool first = true;
            foreach (var tile in Terrain.activeTerrains)
            {
                var b = new Bounds(tile.transform.position + tile.terrainData.size * 0.5f, tile.terrainData.size);
                if (first) { world = b; first = false; } else world.Encapsulate(b);
            }
            int cx0 = Mathf.FloorToInt(world.min.x / t.cellSize), cx1 = Mathf.FloorToInt(world.max.x / t.cellSize);
            int cz0 = Mathf.FloorToInt(world.min.z / t.cellSize), cz1 = Mathf.FloorToInt(Mathf.Min(world.max.z, northZ) / t.cellSize);
            var plan = new List<FishGroupPlan>(64);
            c.diag = new float[ns * nb * 4];
            FishPlan.Diagnostics = c.diag;
            FishPlan.FallbackAdults = FishPlan.FallbackSchools = 0f;
            float minAny = float.MaxValue;
            foreach (var sp in t.species) minAny = Mathf.Min(minAny, sp.minBedDepth);
            float edge = t.bands[nb - 1].maxShoreDistance;
            for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    plan.Clear();
                    FishPlan.Cell(t, w, seed, cx, cz, plan);
                    foreach (var g in plan)
                    {
                        var sp = t.species[g.species];
                        float depth = w.WaterLevelY - g.anchor.y;
                        if (depth < sp.minBedDepth - 1e-3f) c.tooShallow++;
                        if (edge > 0f && g.band == nb - 1 && -w.ShoreDistance(g.anchor.x, g.anchor.z) > edge) c.beyondEdge++;
                        int count = sp.schoolUnit ? 1 : g.members;
                        for (int m = 0; m < g.members; m++)
                        {
                            FishSimWorld.MemberDraws(seed, g, m, t, out _, out float len);
                            c.lengths[g.species].Add(len);
                        }
                        c.hist[Bin(depth, DepthEdges), Bin(-w.ShoreDistance(g.anchor.x, g.anchor.z), OutEdges)] += count;
                        if (sp.schoolUnit) { c.schools[g.band] += 1f; continue; }
                        if (g.structure >= 0) { c.structAdults[g.structure, g.band] += g.members; continue; }
                        c.adults[g.band] += g.members;
                        c.mix[g.species, g.band] += g.members;
                    }
                    // The plan's own samples and band/structure function (f-td: plan and census share ONE function).
                    float a2 = FishPlan.SampleArea(t);
                    for (int k = 0; k < FishPlan.AreaSamples * FishPlan.AreaSamples; k++)
                    {
                        int band = FishPlan.Sample(t, w, seed, cx, cz, k, out float x, out float z, out float bedDepth, out int st, out float anchorArea);
                        if (band < 0) { Exclude(c, t, northZ, x, z, a2); continue; }
                        if (bedDepth < minAny) c.belowMinAny[band] += a2;
                        // Structure squares are graded per type, so they leave the band's adult area.
                        if (st < 0) { c.bandArea[band] += a2; continue; }
                        c.structArea[st, band] += a2;
                        // Expected per-anchor extra schools, spread over the anchor's area as the plan does.
                        if (t.structures[st].extraSchoolSpecies >= 0 && anchorArea > 0f)
                            c.schoolExtras[band] += a2 * t.structures[st].extraSchoolsPerAnchor / anchorArea;
                    }
                }
            FishPlan.Diagnostics = null;
            c.fallbackAdults = FishPlan.FallbackAdults;
            c.fallbackSchools = FishPlan.FallbackSchools;
            return c;
        }

        static int Bin(float v, float[] edges)
        {
            int i = 0;
            while (i < edges.Length && v >= edges[i]) i++;
            return i;
        }

        static float ShorelineMetres(FishPopulation pop)
        {
            var r = JsonUtility.FromJson<Stations>("{\"s\":" + System.IO.File.ReadAllText(System.IO.Path.Combine(Application.dataPath, "..", FishTestKit.StationsPath)) + "}");
            float m = 0f, north = pop.Map.northLineZ;
            for (int i = 1; i < r.s.Length; i++)
                if (r.s[i].z <= north && r.s[i - 1].z <= north) m += Vector2.Distance(new Vector2(r.s[i].x, r.s[i].z), new Vector2(r.s[i - 1].x, r.s[i - 1].z));
            return m;
        }

#pragma warning disable 0649
        [System.Serializable] class Station { public float x, z; }
        [System.Serializable] class Stations { public Station[] s; }
#pragma warning restore 0649
    }
}
