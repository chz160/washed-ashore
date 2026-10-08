using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Fish
{
    /// <summary>What the sim needs to know about the player each tick (F6).</summary>
    public struct FishPlayer
    {
        public Vector3 position;     // feet
        public WaterMode mode;
        /// <summary>Dry and within FishScatter.bankWalkLipDistance of the waterline (the bank-walk trigger).</summary>
        public bool nearWaterline;
    }

    /// <summary>Why Clear() refused a pose (f-td C2 review 1: the blocker of each long stuck run).</summary>
    public enum FishBlock : byte { None, Land, Shallow, Band, Dry, Mate, Body }

    /// <summary>
    /// The live fish (adr/fish-1 §5.3, §5.6): fixed-capacity arrays, no GameObjects, no allocation after construction.
    /// Groups are added from <see cref="FishGroupPlan"/>s and removed whole; members are a per-group linked list.
    /// Decisions run only in <see cref="Tick"/> at the fixed step; <see cref="Render"/> interpolates for the frame.
    /// Every body is the measured clip-max box from <see cref="FishBodies"/> (mandatory in the game; the analytic test
    /// path alone may use a stand-in box). Steering is in FishSimWorld.Steer.cs, surface events in .Events.cs.
    /// </summary>
    public sealed partial class FishSimWorld
    {
        struct Agent
        {
            public bool live, school;
            public long fishId;
            public int group, next, species, variant;
            public float scale, length;
            public Vector3 pos, prevPos;
            public float yaw, prevYaw, renderedYaw, speed;
            public float hold;              // Mid/NearSurface: centre depth below the surface; NearBed: bottom height above the bed
            public Vector3 slot;            // offset from the group target
            public FishMode mode;
            public float timer;             // seconds left in the current reaction / burst / event phase
            public float sinceScatter;      // seconds since the last scatter began (return/settle schedule)
            public float fleeDistance, burstSeconds, burstSpeed;
            public float stuck;             // seconds in a row its wanted step was refused by Clear()
            public FishBlock block;         // what refused it last
            public long blockOther;         // the fish whose body refused it last (diagnostics)
            public float escape;            // seconds in the current escape episode (turning / backing out of a dead end)
            public Vector3 escapeFrom;      // where that episode began
            public Vector3 fleeFrom;
            public FishSurfaceKind eventKind;
            public float eventLength, eventAge, jumpHeight;
            public float animPhase, animRate, flash;
            public FishDraws draws;
        }

        struct Group
        {
            public bool live, alarmed;
            public int first, count, species, band, structure;
            public long cell;
            public int planIndex;
            public Vector3 home, target;
            public float legTimer;
            public bool holding;
            public FishDraws draws;
        }

        /// <summary>Stand-in body for the analytic EditMode path only: 1 m long at scale 1, 0.2 m tall.</summary>
        static readonly FishBody TestBody = new FishBody { variant = "test", noseToTail = 1f, clipBounds = new Bounds(Vector3.zero, new Vector3(0.2f, 0.2f, 1f)) };

        readonly FishTuning t;
        readonly FishBodies bodies;
        readonly IFishWater water;
        readonly int seed;
        readonly float waveAmplitude;
        readonly Agent[] fish;
        readonly Group[] groups;
        int liveIndividuals, liveSchoolMembers, liveGroups;

        /// <param name="bodies">Required; null only on the analytic test path (<paramref name="testBodies"/> true).</param>
        /// <param name="waveAmplitude">WaterMotion.MaxAmplitude of the shipped waves (the trough below WaterLevelY).</param>
        public FishSimWorld(FishTuning tuning, FishBodies bodies, IFishWater water, int seed, float waveAmplitude, bool testBodies = false)
        {
            if (!bodies && !testBodies) throw new System.ArgumentNullException(nameof(bodies), "FishBodies is required (clip-max body boxes, adr/fish-1)");
            t = tuning;
            this.bodies = bodies;
            this.water = water;
            this.seed = seed;
            this.waveAmplitude = waveAmplitude;
            fish = new Agent[tuning.MaxLive];
            groups = new Group[tuning.MaxLive];
            Step = 1f / tuning.simHz;
            StuckLongestBySpecies = new float[tuning.species.Length];
            EscapeLongestBySpecies = new float[tuning.species.Length];
            InitGrid();
        }

        public float Step { get; }
        public int LiveFish => liveIndividuals + liveSchoolMembers;
        public int LiveGroups => liveGroups;
        public int GroupSlots => groups.Length;

        /// <summary>Stable id of a fish: (cell, group, member) packed, independent of activation history (fishing note).</summary>
        public static long FishId(long cell, int groupIndex, int member) =>
            (long)FishRandom.Hash(0, 0xF1D, cell, groupIndex, (uint)member) & long.MaxValue;

        /// <summary>
        /// Adds a planned group if the individual or school-member cap has room for all its members. Members start at
        /// seeded slots around the anchor at their held depth. Returns the group slot or -1 (the cell stays dormant).
        /// </summary>
        public int AddGroup(FishGroupPlan plan)
        {
            var sp = t.species[plan.species];
            PlannedMembers += plan.members;
            if (sp.schoolUnit ? liveSchoolMembers + plan.members > t.maxSchoolMembers : liveIndividuals + plan.members > t.maxIndividuals) return -1;
            int g = FreeGroup();
            if (g < 0) return -1;
            groups[g] = new Group
            {
                live = true, first = -1, species = plan.species, band = plan.band, structure = plan.structure, cell = plan.cell,
                planIndex = plan.groupIndex, home = plan.anchor, target = plan.anchor, holding = true,
                draws = new FishDraws(seed, FishRandom.StreamSteer, plan.cell, plan.groupIndex),
            };
            groups[g].legTimer = groups[g].draws.Range(sp.holdSeconds);
            for (int m = 0; m < plan.members; m++)
            {
                int f = FreeFish();
                var md = MemberDraws(seed, plan, m, t, out int variant, out float length);
                var a = new Agent
                {
                    live = true, school = sp.schoolUnit, fishId = FishId(plan.cell, plan.groupIndex, m), group = g, species = plan.species,
                    variant = variant, length = length, scale = length / Body(variant).noseToTail, next = groups[g].first,
                    hold = sp.depthMode == FishDepthMode.NearBed ? md.Range(sp.bedOffset) : md.Range(sp.depthRange),
                    yaw = md.Range(0f, 360f), mode = sp.grouping == FishGrouping.School ? FishMode.SchoolFollow : FishMode.Hold,
                    animPhase = md.Next(), sinceScatter = float.MaxValue,
                };
                a.animRate = t.idleAnimRate;
                a.live = false;                      // not visible to Clear() until it is placed
                fish[f] = a;
                if (!Place(f, g, sp, plan.anchor, ref md)) { SpawnSkips++; continue; }
                fish[f].draws = md;
                fish[f].live = true;
                fish[f].prevPos = fish[f].pos;
                fish[f].prevYaw = fish[f].renderedYaw = fish[f].yaw;
                groups[g].first = f;
                groups[g].count++;
                if (sp.schoolUnit) liveSchoolMembers++; else liveIndividuals++;
            }
            liveGroups++;
            return g;
        }

        /// <summary>
        /// A member's own decision stream with its variant and length already drawn (the spawn uses it; the F2/F3 census
        /// reports the same lengths without simulating). Most adults are in the common range; lengthTailShare are the
        /// rare large ones up to lengthTailMax.
        /// </summary>
        public static FishDraws MemberDraws(int seed, in FishGroupPlan plan, int member, FishTuning t, out int variant, out float length)
        {
            var sp = t.species[plan.species];
            var md = new FishDraws(seed, FishRandom.StreamSteer, plan.cell, plan.groupIndex * 256 + member + 1);
            variant = sp.variants[Mathf.Min(sp.variants.Length - 1, (int)(md.Next() * sp.variants.Length))];
            length = md.Next() < t.lengthTailShare ? md.Range(sp.lengthRange.y, sp.lengthTailMax) : md.Range(sp.lengthRange);
            return md;
        }

        /// <summary>Members not spawned because no clear spot was found near the anchor (diagnostic; the plan counts them).</summary>
        public int SpawnSkips { get; private set; }
        /// <summary>Members of every group offered to AddGroup (the denominator for SpawnSkips; f-td slot-B condition 3).</summary>
        public int PlannedMembers { get; private set; }

        /// <summary>
        /// A spawn spot for fish slot <paramref name="f"/>: its seeded slot (or the anchor), then seeded spots in the group
        /// radius, each at a few headings, at its held depth, until one is <see cref="Clear"/> (water under the whole
        /// body, inside its band, apart from groupmates and clear of every other body). False if none is.
        /// </summary>
        bool Place(int f, int g, FishSpecies sp, Vector3 anchor, ref FishDraws md)
        {
            ref Agent a = ref fish[f];
            for (int k = 0; k < 12; k++)
            {
                Vector3 p = k == 0 ? anchor + (sp.grouping == FishGrouping.Solitary ? Vector3.zero : Slot(sp, ref md, g, a.length))
                                   : anchor + Disc(ref md) * Mathf.Max(sp.groupRadius, a.length);
                if (k == 0) a.slot = p - anchor;
                for (int h = 0; h < 4; h++)
                {
                    float yaw = a.yaw + h * 90f;
                    if (!water.TryBed(p.x, p.z, out float bed)) continue;
                    var probe = new Vector3(p.x, 0f, p.z);
                    a.pos = probe;
                    a.yaw = yaw;
                    a.pos.y = HeldY(ref a, sp, p.x, p.z, bed);
                    if (!Clear(f, g, sp, a.pos, yaw, spawning: true)) continue;
                    if (k > 0) a.slot = p - anchor;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Removes a group and all its members.</summary>
        public void RemoveGroup(int g)
        {
            if (!GroupLive(g)) return;
            for (int f = groups[g].first; f >= 0; f = fish[f].next)
            {
                fish[f].live = false;
                if (fish[f].school) liveSchoolMembers--; else liveIndividuals--;
            }
            groups[g].live = false;
            liveGroups--;
        }

        public bool GroupLive(int g) => g >= 0 && g < groups.Length && groups[g].live;
        public long GroupCell(int g) => groups[g].cell;
        public int GroupPlanIndex(int g) => groups[g].planIndex;
        public Vector3 GroupHome(int g) => groups[g].home;
        public int GroupSpecies(int g) => groups[g].species;
        public int GroupBand(int g) => groups[g].band;
        public int GroupStructure(int g) => groups[g].structure;
        public int GroupFirstFish(int g) => groups[g].first;
        public int NextFish(int f) => fish[f].next;

        /// <summary>The live group slot planned as (cell, groupIndex), or -1.</summary>
        public int FindGroup(long cell, int groupIndex)
        {
            for (int g = 0; g < groups.Length; g++) if (groups[g].live && groups[g].cell == cell && groups[g].planIndex == groupIndex) return g;
            return -1;
        }

        /// <summary>False when the terrain tile under the group's home is gone (F9: the group must go).</summary>
        public bool GroupHasTile(int g) => water.TryBed(groups[g].home.x, groups[g].home.z, out _);

        /// <summary>True when any member of the group could be seen (the spawn/despawn rule, N7).</summary>
        public bool GroupVisible(int g, in FishView view)
        {
            for (int f = groups[g].first; f >= 0; f = fish[f].next)
                if (view.CouldSee(BodyExtent(f).y, fish[f].pos, water.SurfaceY(fish[f].pos.x, fish[f].pos.z), fish[f].length, fish[f].mode)) return true;
            return false;
        }

        /// <summary>World-space Y extent of fish slot <paramref name="f"/> at its current (sim) pose.</summary>
        public Vector2 BodyExtent(int f) => Extent(fish[f].variant, fish[f].pos, fish[f].yaw, fish[f].scale);

        FishBody Body(int variant) => bodies ? bodies.bodies[variant] : TestBody;

        Vector2 Extent(int variant, Vector3 centre, float yaw, float scale)
        {
            if (!bodies)
            {
                float h = 0.5f * TestBody.clipBounds.size.y * scale;
                return new Vector2(centre.y - h, centre.y + h);
            }
            return bodies.VerticalExtent(variant, centre, Quaternion.LookRotation(FishSteering.Heading(yaw), Vector3.up), scale);
        }

        Vector3 Slot(FishSpecies sp, ref FishDraws d, int g, float length)
        {
            slotScratch.Clear();
            for (int f = groups[g].first; f >= 0; f = fish[f].next) slotScratch.Add(fish[f].slot);
            var slot = PickSlot(sp, ref d, slotScratch, length, out bool fellBack);
            if (fellBack) SlotFallbacks++;
            return slot;
        }

        /// <summary>Slots taken by PickSlot's fallback (no try cleared the body-feasible distance); f-td: should be 0.</summary>
        public int SlotFallbacks { get; private set; }

        readonly System.Collections.Generic.List<Vector3> slotScratch = new System.Collections.Generic.List<Vector3>(64);

        /// <summary>
        /// Slot centres must be at least this many body lengths apart (f-td slot-E review): two padded clip boxes can't
        /// overlap at ANY pair of yaws once their centres are twice the circumscribed radius apart. The radius is about the
        /// fish's pivot, and the shipped clip boxes sit 0.125-0.165 BL behind it, so the bound is 1.494 BL (shad, sunfish);
        /// FishSlotFeasibilityTests asserts it for every variant. The slot plan then never assigns a pose the body guard
        /// forbids while the neighbour holds still.
        /// </summary>
        public const float BodyFeasibleSlotLengths = 1.5f;

        /// <summary>
        /// A seeded slot inside the group radius, at least max(target spacing, the body-feasible distance) from every taken
        /// slot (lengths from the larger of this fish and the species' longest adult, so any mate fits); if no try clears,
        /// the try farthest from its nearest taken slot (<paramref name="fellBack"/>). Pure, for the test as well.
        /// </summary>
        public static Vector3 PickSlot(FishSpecies sp, ref FishDraws d, System.Collections.Generic.IReadOnlyList<Vector3> taken, float length, out bool fellBack)
        {
            float l = MaxLength(sp, length);
            float spacing = Mathf.Max(sp.spacingBodyLengths.y * length, BodyFeasibleSlotLengths * l);
            Vector3 best = Vector3.zero;
            float bestGap = -1f;
            for (int tries = 0; tries < SlotTries; tries++)
            {
                float ang = d.Range(0f, 2f * Mathf.PI), r = sp.groupRadius * Mathf.Sqrt(d.Next());
                var c = new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                float gap = float.MaxValue;
                foreach (var t in taken) gap = Mathf.Min(gap, (t - c).sqrMagnitude);
                if (gap > bestGap) { bestGap = gap; best = c; }
                if (gap >= spacing * spacing) { fellBack = false; return c; }
            }
            fellBack = true;
            return best;
        }

        /// <summary>Seeded tries per slot: 128 leaves no fallback in 1,000 simulated largest schools of every species (spawn only).</summary>
        const int SlotTries = 128;

        /// <summary>The length slot spacing is measured in: this fish or the species' longest adult (the tail), whichever is longer.</summary>
        public static float MaxLength(FishSpecies sp, float length) => Mathf.Max(length, Mathf.Max(sp.lengthRange.y, sp.lengthTailMax));

        int FreeFish()
        {
            for (int i = 0; i < fish.Length; i++) if (!fish[i].live) return i;
            return -1;
        }

        int FreeGroup()
        {
            for (int i = 0; i < groups.Length; i++) if (!groups[i].live) return i;
            return -1;
        }

        /// <summary>
        /// The column is deep enough for the species here, there's a tile, and the point is inside a band (so a fish never
        /// leaves the open band's 50 m edge; ruling/fish-depth-bands rev 4).
        /// </summary>
        bool Usable(FishSpecies sp, float x, float z, out float bed)
        {
            if (!water.TryBed(x, z, out bed) || water.WaterLevelY - bed < sp.minBedDepth) return false;
            return FishPlan.BandAt(t, water, x, z, out _) >= 0;
        }

        /// <summary>
        /// A pose (centre, yaw) fish slot <paramref name="self"/> may take (F4, F5, N7): its centre column is usable, its nose
        /// and tail are over water, it is at least the minimum spacing (spacingBodyLengths.x of the pair's mean length)
        /// from every groupmate, and its body box overlaps no other live fish's body box. Every move and turn passes
        /// this, so overlaps never arise and nothing is pushed apart after the fact.
        /// </summary>
        bool Clear(int self, int g, FishSpecies sp, Vector3 centre, float yaw, bool spawning = false)
        {
            ref Agent a = ref fish[self];
            lastBlock = ColumnBlock(sp, centre.x, centre.z);
            if (lastBlock != FishBlock.None) return false;
            Vector3 fwd = FishSteering.Heading(yaw) * (0.5f * a.length);
            lastBlock = FishBlock.Dry;
            if (!Wet(centre + fwd) || !Wet(centre - fwd)) return false;
            lastBlock = FishBlock.Mate;
            for (int m = groups[g].first; m >= 0; m = fish[m].next)
            {
                if (m == self || !fish[m].live) continue;
                float need = sp.spacingBodyLengths.x * 0.5f * (a.length + fish[m].length);
                // Horizontal distance: depth eases after the pose is cleared, so a vertical gap can't be relied on.
                // Relief (f-td C2 review 1): a pair already too close may only move apart, never closer.
                if (TooClose(fish[m].pos, centre, a.pos, need)) return false;
                if (TooClose(fish[m].prevPos, centre, a.pos, need)) return false;   // render interpolates from prevPos
            }
            // Clearance: the box grows by BoxClearance of the body length (plus its own step when moving), so the poses
            // render draws between ticks (interpolated centre, capped yaw) never graze another body.
            var mine = Box(a.variant, a.scale, centre, yaw, BoxClearance * a.length + (spawning ? 0f : a.speed * Step));
            lastBlock = FishBlock.Body;
            if (!spawning) return !AnyOverlap(self, mine, centre, yaw, a.length);
            // Spawning happens between ticks, after the grid was built: scan every live fish (rare, a few per second).
            for (int o = 0; o < fish.Length; o++)
            {
                if (o == self || !fish[o].live) continue;
                float reach = 0.5f * (a.length + fish[o].length);
                if (FlatSq(fish[o].pos, centre) > reach * reach * 1.5f) continue;   // horizontal, like the guard
                if (mine.OverlapsXZ(Box(fish[o].variant, fish[o].scale, fish[o].pos, fish[o].yaw))) return false;
            }
            return true;
        }

        FishBlock lastBlock;

        /// <summary>
        /// Closer than <paramref name="need"/> to <paramref name="other"/> at the candidate centre, unless the fish is already
        /// that close where it is and the candidate moves it away (so a pair that ended up too close can separate).
        /// </summary>
        static bool TooClose(Vector3 other, Vector3 centre, Vector3 now, float need)
        {
            float cand = FlatSq(other, centre);
            if (cand >= need * need) return false;
            float cur = FlatSq(other, now);
            return !(cur < need * need && cand > cur + ReliefMin);
        }

        /// <summary>Squared-distance gain a relief move must make (1 cm at 1 m).</summary>
        const float ReliefMin = 1e-4f;

        /// <summary>Usable() with its reason: land (no bed), shallower than the species' minimum, or outside every band.</summary>
        FishBlock ColumnBlock(FishSpecies sp, float x, float z)
        {
            if (!water.TryBed(x, z, out float bed) || bed >= water.WaterLevelY) return FishBlock.Land;
            if (water.WaterLevelY - bed < sp.minBedDepth) return FishBlock.Shallow;
            return FishPlan.BandAt(t, water, x, z, out _) >= 0 ? FishBlock.None : FishBlock.Band;
        }

        static float FlatSq(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }

        /// <summary>Clearance kept between body boxes, as a fraction of the moving fish's length.</summary>
        const float BoxClearance = 0.05f;

        bool Wet(Vector3 p) => water.TryBed(p.x, p.z, out float bed) && bed < water.WaterLevelY;

        /// <summary>A fish's clip-max body box in the world: an XZ rectangle turned by yaw, plus its Y extent.</summary>
        FishBox Box(int variant, float scale, Vector3 centre, float yaw, float pad = 0f)
        {
            var b = bodies ? bodies.bodies[variant].clipBounds : TestBody.clipBounds;
            var q = Quaternion.Euler(0f, yaw, 0f);
            var c = centre + q * (b.center * scale);
            var e = Extent(variant, centre, yaw, scale);
            return new FishBox(new Vector2(c.x, c.z), FishSteering.Heading(yaw), b.extents.x * scale + pad, b.extents.z * scale + pad, e);
        }

        /// <summary>
        /// The Y band for the body centre here: the body's lowest point at bed + clearance up to its top at the moving
        /// surface - margin (F4, the clip-max box, not the pivot). The margin is never less than the wave amplitude.
        /// </summary>
        Vector2 CentreBand(ref Agent a, FishSpecies sp, float bed, float surface)
        {
            var e = Extent(a.variant, new Vector3(a.pos.x, 0f, a.pos.z), a.yaw, a.scale); // offsets from a centre at y = 0
            return FishSteering.DepthBand(bed - e.x, surface - e.y, sp.bedClearance, Mathf.Max(sp.surfaceMargin, waveAmplitude));
        }

        /// <summary>Where the fish holds: centre at its depth below the surface, or its bottom at its height above the bed.</summary>
        float HeldY(ref Agent a, FishSpecies sp, float x, float z, float bedFallback)
        {
            float surface = water.SurfaceY(x, z);
            if (!water.TryBed(x, z, out float bed)) bed = bedFallback;
            var band = CentreBand(ref a, sp, bed, surface);
            float y = sp.depthMode == FishDepthMode.NearBed
                ? bed + a.hold - Extent(a.variant, Vector3.zero, a.yaw, a.scale).x
                : surface - a.hold;
            return Mathf.Clamp(y, band.x, band.y);
        }
    }

    /// <summary>What the main camera can see, for the no-pop rule (gate/fish-technique addendum, condition 4).</summary>
    public readonly struct FishView
    {
        public readonly Vector3 camera;
        public readonly Plane[] frustum;   // null = no camera: nothing is visible
        public readonly FishTuning tuning;   // null: a fixed drawDistance for every body
        public readonly float drawDistance;
        public readonly FishMurk murk;

        /// <summary>The game's view: draw radii from <see cref="FishTuning.BodyDrawRadius"/> (ruling/fish-jump-body-radius).</summary>
        public FishView(Vector3 camera, Plane[] frustum, FishTuning tuning, FishMurk murk)
        {
            this.camera = camera; this.frustum = frustum; this.tuning = tuning; drawDistance = tuning.bodyDrawDistance; this.murk = murk;
        }

        /// <summary>A view with one fixed body draw distance (kept for callers written before the jump radius).</summary>
        public FishView(Vector3 camera, Plane[] frustum, float drawDistance, FishMurk murk)
        {
            this.camera = camera; this.frustum = frustum; tuning = null; this.drawDistance = drawDistance; this.murk = murk;
        }

        /// <summary>CouldSee for a fish that isn't jumping.</summary>
        public bool CouldSee(float bodyTopY, Vector3 centre, float surfaceY, float bodyLength) =>
            CouldSee(bodyTopY, centre, surfaceY, bodyLength, FishMode.Hold);

        /// <summary>
        /// Drawable for this camera AND within draw distance AND inside the frustum padded by one body length. A fish
        /// may only appear or vanish when this is false.
        /// </summary>
        public bool CouldSee(float bodyTopY, Vector3 centre, float surfaceY, float bodyLength, FishMode mode)
        {
            if (frustum == null) return false;
            float drawDistance = tuning ? tuning.BodyDrawRadius(mode, surfaceY - bodyTopY) : this.drawDistance;
            if ((centre - camera).sqrMagnitude > (drawDistance + bodyLength) * (drawDistance + bodyLength)) return false;
            if (!murk.Drawable(surfaceY, bodyTopY, centre, camera)) return false;
            var padded = new Bounds(centre, Vector3.one * (2f * Mathf.Max(bodyLength, 0.1f)));
            return GeometryUtility.TestPlanesAABB(frustum, padded);
        }
    }
}
