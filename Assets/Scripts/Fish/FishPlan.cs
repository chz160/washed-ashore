using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

// The F3 census reaches the test-only plan v1 switch (FishPlan.LegacyRelocation); nothing else may.
[assembly: InternalsVisibleTo("WashedAshore.Tests.Fish.PlayMode")]
[assembly: InternalsVisibleTo("WashedAshore.Fish.PlayModeTests")]   // the offline compile check's name for that assembly

namespace WashedAshore.Fish
{
    /// <summary>
    /// What the fish plan reads about the water (adr/fish-1 T2/T4). The scene implementation uses
    /// TerrainQuery.TryGroundHeight and the structure anchors; EditMode tests use an analytic bed. TryBed false means
    /// "no tile here" (or outside the fish area): the point counts as land. Bands are by bed depth
    /// (ruling/fish-depth-bands rev 4); shore distance only bounds a band with a finite maxShoreDistance (open: 50 m).
    /// </summary>
    public interface IFishWater
    {
        float WaterLevelY { get; }
        bool TryBed(float x, float z, out float bedY);
        /// <summary>
        /// Signed distance to the bank, metres, + inland (LevelMaps convention), so water is negative. Used ONLY for the
        /// outer edge of a band with a finite maxShoreDistance; exact south of the north line (guard test).
        /// </summary>
        float ShoreDistance(float x, float z);
        /// <summary>The structure type (FishTuning.structures index) at a water point and its anchor's area (bank length x reach), or -1.</summary>
        int StructureAt(float x, float z, out float anchorArea);
        /// <summary>The visible water surface at (x, z) now (WaterLevelY + WaterMotion offset).</summary>
        float SurfaceY(float x, float z);
    }

    /// <summary>One planned group: a species, an anchor and its member count (adr/fish-1 §5.5).</summary>
    public struct FishGroupPlan
    {
        public long cell;
        public int groupIndex;
        public int species;
        public int band;
        /// <summary>FishTuning.structures index at the anchor, or -1.</summary>
        public int structure;
        public Vector3 anchor;   // XZ home; Y = the bed under it
        public int members;
    }

    /// <summary>
    /// The virtual population: each cell's groups are a pure function of (seed, cell, tuning, water). Nothing is
    /// simulated here, so the F3 census runs over the whole map in EditMode and a re-entered cell regenerates exactly.
    /// Each cell is measured on 16 x 16 stratified-jittered points (one per 2 m square). A square of band b contributes
    /// adults at b's DensityPerM2 (x the structure's densityMultiplier inside an anchor), shared among ALL the band's
    /// species by their per-100-m weight (x the structure's prefs); structure-only extras and school units are added per
    /// area. Each species' band count is then placed only where it fits (relocation within the species, f-designer).
    /// Expected counts become groups by stochastic rounding (ruling fish-robertson-island-anchor; brief F1).
    /// </summary>
    public static class FishPlan
    {
        /// <summary>Samples per cell edge: 16 on a 32 m cell is the LevelMaps 2 m grid.</summary>
        public const int AreaSamples = 16;
        const int AnchorTries = 8;
        const int SampleCount = AreaSamples * AreaSamples;

        public static int BandOf(FishTuning t, float bedDepth)
        {
            for (int b = 0; b < t.bands.Length; b++)
                if (bedDepth >= t.bands[b].bedDepth.x && bedDepth < t.bands[b].bedDepth.y) return b;
            return -1;
        }

        /// <summary>
        /// The one band function for the plan and the F3 census (ruling/fish-depth-bands rev 4): the band by bed depth,
        /// then -1 if that band has an outer edge and the point is farther out (open water beyond 50 m holds no fish).
        /// </summary>
        public static int BandOf(FishTuning t, IFishWater w, float x, float z, float bedDepth)
        {
            int b = BandOf(t, bedDepth);
            if (b < 0) return -1;
            float edge = t.bands[b].maxShoreDistance;
            return edge > 0f && -w.ShoreDistance(x, z) > edge ? -1 : b;
        }

        /// <summary>
        /// Open-depth water beyond the open band's outer edge (BandAt is -1 there but the bed is as deep as the open band):
        /// it holds no fish; only the staged far-water signs use it.
        /// </summary>
        public static bool IsFarWater(FishTuning t, float bedDepth)
        {
            var open = t.bands[t.bands.Length - 1];
            return open.maxShoreDistance > 0f && bedDepth >= open.bedDepth.x && bedDepth < open.bedDepth.y;
        }

        /// <summary>Band of a water point, or -1 (land, no tile, or outside every band).</summary>
        public static int BandAt(FishTuning t, IFishWater w, float x, float z, out float bedDepth)
        {
            bedDepth = 0f;
            if (!w.TryBed(x, z, out float bed)) return -1;
            bedDepth = w.WaterLevelY - bed;
            return bedDepth <= 0f ? -1 : BandOf(t, w, x, z, bedDepth);
        }

        /// <summary>
        /// Expected adults of species <paramref name="s"/> per square metre at a point of band <paramref name="band"/>
        /// with structure <paramref name="st"/> (-1 = none), given the sum of all adult species' mix weights there.
        /// The F3 census uses the same function.
        /// </summary>
        public static float AdultDensity(FishTuning t, int s, int band, int st, float mixSum, float anchorArea)
        {
            var sp = t.species[s];
            if (sp.schoolUnit || band < 0) return 0f;
            var b = t.bands[band];
            float d = 0f;
            float mix = MixWeight(t, s, band, st);
            if (mix > 0f && mixSum > 0f) d += b.DensityPerM2 * Multiplier(t, st, band) * mix / mixSum;
            if (st >= 0 && t.structures[st].extraSpecies == s)
                d += t.structures[st].extraPer100mShore / (100f * Mathf.Max(1e-3f, t.structures[st].reachOut));
            return d;
        }

        /// <summary>Expected schools of school type <paramref name="s"/> per square metre at a point (band units + structure extras).</summary>
        public static float SchoolDensity(FishTuning t, int s, int band, int st, float anchorArea)
        {
            var sp = t.species[s];
            if (!sp.schoolUnit || band < 0) return 0f;
            float d = t.bands[band].PerM2(sp.schoolsPer100mByBand[band]);
            if (st >= 0 && t.structures[st].extraSchoolSpecies == s && anchorArea > 0f) d += t.structures[st].extraSchoolsPerAnchor / anchorArea;
            return d;
        }

        /// <summary>Adult density multiplier of a structure type in a band (1 outside structure).</summary>
        public static float Multiplier(FishTuning t, int st, int band) => st >= 0 ? t.structures[st].bandMultiplier[band] : 1f;

        /// <summary>Mix weight of an adult species at a point: its per-100-m share in the band, times the structure's preference.</summary>
        public static float MixWeight(FishTuning t, int s, int band, int st)
        {
            var sp = t.species[s];
            if (sp.schoolUnit) return 0f;
            float w = Mathf.Max(0f, sp.per100mByBand[band]);
            if (st >= 0) w *= Mathf.Max(0f, t.structures[st].prefs[s]);
            return w;
        }

        /// <summary>
        /// F3 diagnostics (f-td: root-cause the mix before any retune). When set (length species x bands x 4), Cell adds
        /// each species' expected count per band and class (outside structure / in structure): [i*4+0] raw brief share
        /// outside, [i*4+1] placed outside after relocation, [i*4+2] raw in structure, [i*4+3] placed in structure, with
        /// i = s * bands + b. Null in the game.
        /// </summary>
        public static float[] Diagnostics;

        /// <summary>
        /// Plan version: the groups a (seed, cell) yields changed at each bump (fishing keys the plan by it). 2 = relocation
        /// within (band, structure class) (f-td slot-E ruling); 1 = relocation within the band only.
        /// </summary>
        public const int Version = ClassRelocation ? 2 : 1;

        /// <summary>Plan v2's relocation within (band, structure class); false backs out to v1 (f-producer's gate rule).</summary>
        const bool ClassRelocation = false;   // slot E gate: mechanism not confirmed, plan v1 stays (v2 kept for review)

        /// <summary>
        /// Tests only: plan version 1's relocation (within the band, across the structure boundary), for the F3 diagnostic.
        /// Internal to the census test (f-td), which resets it in a finally and its TearDown; FishPopulation refuses to run
        /// with it set.
        /// </summary>
        internal static bool LegacyRelocation;

        /// <summary>
        /// Adults whose structure class had no fit square for any species in their cell and band, so they fell back to the
        /// band's other classes (f-td condition b: counted, never dropped). Summed over every Cell call; tests reset it.
        /// </summary>
        public static float FallbackAdults;

        /// <summary>School units moved to another class of their band because their own class had no fit square for them.</summary>
        public static float FallbackSchools;

        /// <summary>Area of one sample square, m².</summary>
        public static float SampleArea(FishTuning t) => (t.cellSize / AreaSamples) * (t.cellSize / AreaSamples);

        /// <summary>
        /// Sample <paramref name="k"/> of cell (cx, cz), the one the plan measures its areas on (and the F3 census uses
        /// to grade them, f-td: one band/area function): a stratified-jittered point (one seeded point per 2 m square, so
        /// a band's measured area is unbiased), its band (-1 = none), bed depth, and structure type and anchor area.
        /// </summary>
        public static int Sample(FishTuning t, IFishWater w, int seed, int cx, int cz, int k,
                                 out float x, out float z, out float bedDepth, out int structure, out float anchorArea)
        {
            long cell = FishRandom.CellId(cx, cz);
            float step = t.cellSize / AreaSamples;
            x = (cx * t.cellSize) + (k % AreaSamples + FishRandom.Value(seed, FishRandom.StreamPlan, cell, -3, (uint)(2 * k))) * step;
            z = (cz * t.cellSize) + (k / AreaSamples + FishRandom.Value(seed, FishRandom.StreamPlan, cell, -3, (uint)(2 * k + 1))) * step;
            int band = BandAt(t, w, x, z, out bedDepth);
            anchorArea = 0f;
            structure = band >= 0 ? w.StructureAt(x, z, out anchorArea) : -1;
            return band;
        }

        /// <summary>Appends cell (cx, cz)'s groups to <paramref name="into"/>. Same inputs, same groups, in the same order.</summary>
        public static void Cell(FishTuning t, IFishWater w, int seed, int cx, int cz, List<FishGroupPlan> into) =>
            Cell(t, w, seed, cx, cz, into, null);

        /// <summary>
        /// As <see cref="Cell(FishTuning, IFishWater, int, int, int, List{FishGroupPlan})"/>, and also fills
        /// <paramref name="signArea"/> (per band, m²) with the cell's wet band area weighted by each structure's density
        /// multiplier: the area the band-rate surface signs scale with (f-designer's surface-sign rule).
        /// <para>
        /// Each species keeps its per-band count (the brief's mix over the whole band area, ruling: relocate within the
        /// species) and is placed only on that band's squares where it fits (bed >= its minBedDepth). A species with no
        /// usable square of a band in this cell passes its count to the species that do fit there.
        /// </para>
        /// </summary>
        public static void Cell(FishTuning t, IFishWater w, int seed, int cx, int cz, List<FishGroupPlan> into, float[] signArea)
        {
            int ns = t.species.Length, nb = t.bands.Length;
            if (signArea != null) System.Array.Clear(signArea, 0, signArea.Length);
            if (nb == 0 || ns == 0) return;
            long cell = FishRandom.CellId(cx, cz);
            float step = t.cellSize / AreaSamples, area = step * step;
            if (expected.Length < ns) expected = new float[ns];
            int nc = Classes(t), n = ns * nb * nc;
            if (shareSum.Length < n) { shareSum = new float[n]; fitSum = new float[n]; factor = new float[n]; }
            System.Array.Clear(expected, 0, ns);
            System.Array.Clear(shareSum, 0, n);
            System.Array.Clear(fitSum, 0, n);

            bool any = false;
            for (int k = 0; k < SampleCount; k++)
            {
                sband[k] = Sample(t, w, seed, cx, cz, k, out sx[k], out sz[k], out sdepth[k], out sstruct[k], out sanchor[k]);
                if (sband[k] < 0)
                {
                    if (signArea != null && signArea.Length > nb && IsFarWater(t, sdepth[k])) signArea[nb] += area;
                    continue;
                }
                any = true;
                if (signArea != null && sband[k] < signArea.Length) signArea[sband[k]] += area * Multiplier(t, sstruct[k], sband[k]);
                // The band's mix over ALL adult species (not only those that fit here): each keeps its brief share.
                float mix = 0f;
                for (int s = 0; s < ns; s++) mix += MixWeight(t, s, sband[k], sstruct[k]);
                smix[k] = mix;
                for (int s = 0; s < ns; s++)
                {
                    float c = RawShare(t, s, k);
                    if (c <= 0f) continue;
                    if (Diagnostics != null) Diagnostics[(s * nb + sband[k]) * 4 + (sstruct[k] >= 0 ? 2 : 0)] += area * c;
                    int i = Slot(t, s, k);
                    shareSum[i] += c;
                    if (Fits(t.species[s], k)) fitSum[i] += c;
                }
            }
            if (!any) return;

            // Relocation factors (plan v2, f-td slot-E ruling): a species' count in a (band, structure class) moves onto the
            // squares of that band and class where it fits, so counts never cross the structure boundary. Adults with no
            // usable square of their (band, class) in this cell go to the adult species that do, in the same class. Only a
            // class where no adult species fits at all falls back to the band's other classes (FallbackAdults).
            for (int b = 0; b < nb; b++)
            {
                float fallback = 0f, keptBand = 0f;
                for (int c = 0; c < nc; c++)
                {
                    Kept(t, b, c, nb, nc, out float kept, out float lost);
                    if (kept > 0f) keptBand += kept; else fallback += lost;
                }
                if (keptBand <= 0f) fallback = 0f;   // nothing in the band fits anywhere: as v1, the count can't be placed
                FallbackAdults += fallback * area;   // shareSum is per m² summed over squares
                for (int s = 0; s < ns; s++)
                {
                    if (!t.species[s].schoolUnit) continue;
                    float kept = 0f, lost = 0f;
                    for (int c = 0; c < nc; c++)
                    {
                        int i = (s * nb + b) * nc + c;
                        if (fitSum[i] > 0f) kept += shareSum[i]; else lost += shareSum[i];
                    }
                    if (kept > 0f) FallbackSchools += lost * area;
                }
                for (int c = 0; c < nc; c++)
                {
                    Kept(t, b, c, nb, nc, out float kept, out float lost);
                    // The class's own lost adults, plus its share of the band's fallback, ride on its kept adults.
                    float boost = kept > 0f ? (kept + lost + fallback * kept / keptBand) / kept : 0f;
                    for (int s = 0; s < ns; s++)
                    {
                        int i = (s * nb + b) * nc + c;
                        factor[i] = fitSum[i] > 0f ? shareSum[i] / fitSum[i] * (t.species[s].schoolUnit ? SchoolBoost(t, s, b, nb, nc) : boost) : 0f;
                    }
                }
            }
            for (int k = 0; k < SampleCount; k++)
                if (sband[k] >= 0)
                    for (int s = 0; s < ns; s++)
                    {
                        float placed = area * Contribution(t, s, k);
                        expected[s] += placed;
                        if (Diagnostics != null) Diagnostics[(s * nb + sband[k]) * 4 + (sstruct[k] >= 0 ? 3 : 1)] += placed;
                    }

            uint draw = 0;
            int groupIndex = 0;
            for (int s = 0; s < ns; s++)
            {
                if (expected[s] <= 0f) continue;
                var sp = t.species[s];
                float units = sp.schoolUnit ? expected[s] : expected[s] / (0.5f * (sp.groupSize.x + sp.groupSize.y));
                int groups = StochasticRound(units, FishRandom.Value(seed, FishRandom.StreamPlan, cell, 64 + s, draw++));
                for (int g = 0; g < groups; g++)
                {
                    Vector3 anchor = Anchor(t, w, seed, cell, step, s, ref draw, out int k);
                    int members = sp.groupSize.x + Mathf.Min(sp.groupSize.y - sp.groupSize.x,
                        Mathf.FloorToInt(FishRandom.Value(seed, FishRandom.StreamPlan, cell, groupIndex, draw++) * (sp.groupSize.y - sp.groupSize.x + 1)));
                    into.Add(new FishGroupPlan { cell = cell, groupIndex = groupIndex++, species = s, band = sband[k], structure = sstruct[k], anchor = anchor, members = members });
                }
            }
        }

        // Per-cell sample scratch (main thread only; no allocation per cell).
        static readonly float[] sx = new float[SampleCount], sz = new float[SampleCount], sdepth = new float[SampleCount];
        static readonly float[] smix = new float[SampleCount], sanchor = new float[SampleCount];
        static readonly int[] sband = new int[SampleCount], sstruct = new int[SampleCount];
        static float[] expected = new float[16], shareSum = new float[64], fitSum = new float[64], factor = new float[64];

        /// <summary>Structure classes relocation keeps apart: outside structure, then each structure type (1 in v1).</summary>
        static int Classes(FishTuning t) => LegacyRelocation || !ClassRelocation ? 1 : 1 + t.structures.Length;

        /// <summary>Index of species s at sample k in the per-(species, band, class) relocation arrays.</summary>
        static int Slot(FishTuning t, int s, int k) =>
            (s * t.bands.Length + sband[k]) * Classes(t) + (Classes(t) == 1 || sstruct[k] < 0 ? 0 : 1 + sstruct[k]);

        /// <summary>
        /// A school type's units keep their class; units in a class with no square where they fit move to the band's other
        /// classes where they do (the caller counts them in FallbackSchools).
        /// </summary>
        static float SchoolBoost(FishTuning t, int s, int b, int nb, int nc)
        {
            float kept = 0f, lost = 0f;
            for (int c = 0; c < nc; c++)
            {
                int i = (s * nb + b) * nc + c;
                if (fitSum[i] > 0f) kept += shareSum[i]; else lost += shareSum[i];
            }
            return kept > 0f ? (kept + lost) / kept : 1f;
        }

        /// <summary>Adults of a (band, class) whose species fit somewhere in it (kept) and whose species don't (lost).</summary>
        static void Kept(FishTuning t, int b, int c, int nb, int nc, out float kept, out float lost)
        {
            kept = lost = 0f;
            for (int s = 0; s < t.species.Length; s++)
            {
                if (t.species[s].schoolUnit) continue;
                int i = (s * nb + b) * nc + c;
                if (fitSum[i] > 0f) kept += shareSum[i]; else lost += shareSum[i];
            }
        }

        static bool Fits(FishSpecies sp, int k) => sband[k] >= 0 && sdepth[k] >= sp.minBedDepth;

        /// <summary>Species s's count per square metre at sample k from the brief alone, before relocation.</summary>
        static float RawShare(FishTuning t, int s, int k) =>
            t.species[s].schoolUnit ? SchoolDensity(t, s, sband[k], sstruct[k], sanchor[k])
                                    : AdultDensity(t, s, sband[k], sstruct[k], smix[k], sanchor[k]);

        /// <summary>Species s's count per square metre placed at sample k: its raw share relocated onto squares where it fits.</summary>
        static float Contribution(FishTuning t, int s, int k) =>
            Fits(t.species[s], k) ? RawShare(t, s, k) * factor[Slot(t, s, k)] : 0f;

        static int StochasticRound(float v, float u) => Mathf.FloorToInt(v) + (u < v - Mathf.Floor(v) ? 1 : 0);

        /// <summary>
        /// A seeded anchor for species s: a sample square picked in proportion to its contribution (so structure and
        /// depth preferences place the groups where the counts came from), jittered inside the square (the sample
        /// point itself if no jitter fits). <paramref name="square"/> is the chosen square.
        /// </summary>
        static Vector3 Anchor(FishTuning t, IFishWater w, int seed, long cell, float step, int s, ref uint draw, out int square)
        {
            var sp = t.species[s];
            float total = 0f;
            for (int k = 0; k < SampleCount; k++) total += Contribution(t, s, k);
            float pick = FishRandom.Value(seed, FishRandom.StreamPlan, cell, -1, draw++) * total;
            square = -1;
            for (int k = 0; k < SampleCount; k++)
            {
                float c = Contribution(t, s, k);
                if (c <= 0f) continue;
                square = k;
                pick -= c;
                if (pick < 0f) break;
            }
            for (int tries = 0; tries < AnchorTries; tries++)
            {
                float x = sx[square] + (FishRandom.Value(seed, FishRandom.StreamPlan, cell, -1, draw++) - 0.5f) * step;
                float z = sz[square] + (FishRandom.Value(seed, FishRandom.StreamPlan, cell, -1, draw++) - 0.5f) * step;
                if (BandAt(t, w, x, z, out float depth) != sband[square] || depth < sp.minBedDepth) continue;
                if (w.StructureAt(x, z, out _) != sstruct[square]) continue;
                w.TryBed(x, z, out float bed);
                return new Vector3(x, bed, z);
            }
            w.TryBed(sx[square], sz[square], out float centreBed);
            return new Vector3(sx[square], centreBed, sz[square]);
        }
    }
}
