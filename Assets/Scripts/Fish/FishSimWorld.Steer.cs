using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Fish
{
    public sealed partial class FishSimWorld
    {
        static readonly float[] ProbeTurns = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        const float ArriveRadius = 0.5f;
        const float ProbeAhead = 1.5f;
        const float StuckEscapeSeconds = 2f;
        const float StuckReportSeconds = 5f;

        /// <summary>Per FishBlock: the longest stuck run it ended (or holds), and how many runs passed 5 s (f-td C2 review 1).</summary>
        public readonly float[] StuckLongestByBlock = new float[7];
        public readonly int[] StuckOver5ByBlock = new int[7];

        /// <summary>
        /// Escape episodes (f-td C2 guard: a fish rocking in a dead end is still visibly stuck): the longest, its net
        /// displacement so far, how many ended, and the smallest net displacement of any that lasted 1 s or more.
        /// </summary>
        public float EscapeLongestSec, EscapeLongestNetM, EscapeWorstNetOver1sM = float.MaxValue;
        public int EscapeEpisodes;

        /// <summary>Longest stuck run and escape episode per species (ruling/fish-stuck-bar-scope: the bar gates drawable tiers).</summary>
        public float[] StuckLongestBySpecies { get; private set; }
        public float[] EscapeLongestBySpecies { get; private set; }

        /// <summary>A live fish's current stuck run and its blocker (tests).</summary>
        public bool StuckOf(long fishId, out float seconds, out FishBlock block)
        {
            for (int f = 0; f < fish.Length; f++)
                if (fish[f].live && fish[f].fishId == fishId) { seconds = fish[f].stuck; block = fish[f].block; return true; }
            seconds = 0f; block = FishBlock.None;
            return false;
        }

        /// <summary>Live fish whose wanted step has been refused for more than 2 s (f-td slot-B condition 2), and the longest run.</summary>
        public int StuckCount(out float longest)
        {
            int n = 0;
            longest = 0f;
            for (int f = 0; f < fish.Length; f++)
            {
                if (!fish[f].live) continue;
                longest = Mathf.Max(longest, fish[f].stuck);
                if (fish[f].stuck > StuckEscapeSeconds) n++;
            }
            return n;
        }

        /// <summary>
        /// One fixed step for every live fish: group legs, player scatter and settle (F6), steering with separation and
        /// the shallows probe, the turn cap (F5), surface events, and the depth clamp between bed + clearance and the
        /// moving surface - margin (F4).
        /// </summary>
        public void Tick(in FishPlayer player)
        {
            float dt = Step;
            BuildGrid();
            for (int g = 0; g < groups.Length; g++)
            {
                if (!groups[g].live) continue;
                var sp = t.species[groups[g].species];
                UpdateGroup(ref groups[g], sp, player, dt);
                for (int f = groups[g].first; f >= 0; f = fish[f].next) SteerFish(f, g, sp, player.position, dt);
            }
        }

        /// <summary>True when the player's position and water state would scatter this fish (brief scatter triggers).</summary>
        bool Threatens(in Agent a, FishSpecies sp, in FishPlayer p)
        {
            var s = t.scatter;
            if (sp.bottom) return p.mode == WaterMode.Swim && (a.pos - p.position).magnitude < s.bottomTriggerSwim3D;
            float d = Flat(a.pos, p.position);
            switch (p.mode)
            {
                case WaterMode.Swim: return d < s.triggerSwimming;
                case WaterMode.Wade: return d < s.triggerWading;
                default:
                    // A dry player on the bank only flushes shelf fish, and only from near the waterline.
                    return p.nearWaterline && d < s.triggerBankWalk && water.TryBed(a.pos.x, a.pos.z, out float bed) &&
                           water.WaterLevelY - bed < t.bands[0].bedDepth.y;
            }
        }

        void UpdateGroup(ref Group grp, FishSpecies sp, in FishPlayer player, float dt)
        {
            // Any member threatened alarms the whole group; each member reacts after its own seeded delay.
            bool threatened = false;
            for (int f = grp.first; f >= 0 && !threatened; f = fish[f].next) threatened = Threatens(fish[f], sp, player);
            if (threatened && !grp.alarmed)
                for (int f = grp.first; f >= 0; f = fish[f].next) BeginScatter(ref fish[f], player.position);
            grp.alarmed = threatened;

            grp.legTimer -= dt;
            if (grp.legTimer > 0f) return;
            Vector3 c = Centroid(grp);
            // Hold or move: idleShare of bouts are spent holding where the group is (N6 slow and economical).
            grp.holding = grp.draws.Next() < sp.idleShare;
            grp.legTimer = grp.draws.Range(grp.holding ? sp.holdSeconds : sp.wanderSeconds);
            if (grp.holding)
            {
                grp.target = FishPlan.BandAt(t, water, c.x, c.z, out _) == grp.band ? c : grp.home;
                return;
            }
            for (int i = 0; i < 6; i++)
            {
                Vector3 cand = Flat(c, grp.home) > sp.homeRadius * 0.8f
                    ? grp.home + Disc(ref grp.draws) * (sp.homeRadius * 0.4f)       // drift back home after a flee
                    : c + Disc(ref grp.draws) * (sp.homeRadius * 0.5f);
                if (Flat(cand, grp.home) > sp.homeRadius) continue;
                if (FishPlan.BandAt(t, water, cand.x, cand.z, out _) != grp.band) continue;
                if (!Usable(sp, cand.x, cand.z, out _)) continue;
                grp.target = cand;
                return;
            }
            grp.target = grp.home;
        }

        void BeginScatter(ref Agent a, Vector3 player)
        {
            if (a.mode == FishMode.Jump) return;      // a jump finishes its arc
            if (IsSurfaceEvent(a.mode)) a.eventLength = 0f;
            a.mode = FishMode.Scatter;
            a.timer = -a.draws.Range(t.scatter.reactionSeconds); // negative = still reacting
            a.sinceScatter = 0f;
            a.fleeFrom = player;     // the flee distance is measured from the threat
            a.fleeDistance = a.draws.Range(t.scatter.fleeDistance);
            a.burstSeconds = 0f; // set when the reaction delay ends
        }

        void SteerFish(int self, int g, FishSpecies sp, Vector3 player, float dt)
        {
            ref Agent a = ref fish[self];
            ref Group grp = ref groups[g];
            a.prevPos = a.pos;
            a.prevYaw = a.yaw;
            a.flash = Mathf.MoveTowards(a.flash, 0f, dt * 2f);
            if (a.sinceScatter < float.MaxValue) a.sinceScatter += dt;
            var sc = t.scatter;

            float targetSpeed, turnRate = sp.turnRateCruise;
            Vector3 dir;
            if (a.mode == FishMode.Scatter)
            {
                // Reaction: stop and turn away (a C-start at the brief's reaction turn rate), so the first motion is away.
                // Then a burst away (down the bed gradient), then flee at cruise to the flee distance.
                a.timer += dt;
                // Away from where the threat was when the scatter began (fleeFrom), not from the player now.
                dir = FishSteering.FleeDirection(a.pos, a.fleeFrom, DeeperDirection(a.pos), 1f);
                // The startle turn rate is the brief's (reactionTurnRate), or the burst rate if none is set; never silently faster.
                turnRate = a.timer < 0f && sc.reactionTurnRate > 0f ? sc.reactionTurnRate : sp.turnRateBurst;
                if (a.timer < 0f) targetSpeed = 0f;
                else
                {
                    if (a.burstSeconds <= 0f) StartFlee(self, sp);
                    targetSpeed = a.timer < a.burstSeconds ? Mathf.Min(sp.burstSpeed, a.burstSpeed) : sc.fleeCruiseSpeed;
                    if (Flat(a.pos, a.fleeFrom) >= a.fleeDistance) { a.mode = FishMode.Flee; targetSpeed = 0f; }
                }
            }
            else if (a.mode == FishMode.Flee)
            {
                // Hold away from the player until the return starts, then settle back home.
                dir = FishSteering.Heading(a.yaw);
                targetSpeed = 0f;
                if (a.sinceScatter >= sc.returnStartSeconds) { a.mode = FishMode.Settle; a.timer = sc.settleSeconds; }
            }
            else if (IsSurfaceEvent(a.mode))
            {
                SurfaceEventStep(ref a, sp, dt, out dir, out targetSpeed);
            }
            else
            {
                if (a.mode == FishMode.Settle)
                {
                    a.timer -= dt;
                    if (a.timer <= 0f || Flat(a.pos, grp.home + a.slot) < ArriveRadius * 2f)
                        a.mode = sp.grouping == FishGrouping.School ? FishMode.SchoolFollow : FishMode.Hold;
                }
                Vector3 goal = (a.mode == FishMode.Settle ? grp.home : grp.target) + a.slot;
                Vector3 to = goal - a.pos;
                to.y = 0f;
                float dist = to.magnitude;
                dir = dist > 1e-4f ? to / dist : FishSteering.Heading(a.yaw);
                bool arrived = dist < ArriveRadius;
                targetSpeed = arrived ? 0f : Mathf.Min(sp.cruiseSpeed, dist / Mathf.Max(dt, 1e-3f));
                if (a.mode != FishMode.Settle && a.mode != FishMode.SchoolFollow)
                    a.mode = arrived ? (grp.holding ? FishMode.Hold : FishMode.Hover) : FishMode.Cruise;
            }

            // Separation from groupmates only (no cross-group search; adr/fish-1 §5.3), at the minimum body-length spacing.
            Vector3 push = Vector3.zero;
            float spacing = sp.spacingBodyLengths.x * a.length;
            for (int m = grp.first; m >= 0; m = fish[m].next)
            {
                if (m == self) continue;
                Vector3 d = a.pos - fish[m].pos;
                d.y = 0f;
                float len = d.magnitude, need = Mathf.Max(spacing, sp.spacingBodyLengths.x * fish[m].length);
                if (len < need) push += (len > 1e-4f ? d / len : FishSteering.Heading((a.fishId & 0xFFFF) * 137.5f)) * (1f - len / need);
            }
            // Personal space: never hold at the player's feet (N2, N7).
            Vector3 fromPlayer = a.pos - player;
            fromPlayer.y = 0f;
            float pd = fromPlayer.magnitude;
            if (pd < t.personalSpace)
            {
                push += (pd > 1e-4f ? fromPlayer / pd : Vector3.forward) * 2f;
                targetSpeed = Mathf.Max(targetSpeed, sp.cruiseSpeed);
            }
            if (push.sqrMagnitude > 0f)
            {
                targetSpeed = Mathf.Max(targetSpeed, sp.cruiseSpeed * Mathf.Min(1f, push.magnitude));
                dir += push;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
            }

            float desiredYaw = targetSpeed > 0f || push.sqrMagnitude > 0f || a.mode == FishMode.Scatter ? FishSteering.Yaw(dir) : a.yaw;
            desiredYaw = AvoidShallows(sp, a.pos, desiredYaw);
            // Stuck escape (f-td slot-B condition 2): blocked for over StuckEscapeSeconds, swing the other way round.
            if (a.stuck > StuckEscapeSeconds) desiredYaw = a.yaw + ((a.fishId & 1) == 0 ? 120f : -120f);
            float newYaw = FishSteering.TurnToward(a.yaw, desiredYaw, turnRate, dt, t.maxTurnPerFrame);
            bool bursting = a.mode == FishMode.Scatter || a.mode == FishMode.Jump;
            a.speed = FishSteering.Ease(a.speed, targetSpeed, bursting ? t.accelBurst : t.accelCruise, dt);

            // Move and turn only into a clear pose (water under the whole body, inside the band, apart from mates, no body
            // overlap); else keep the turn without the step, else hold still. The speed that drives the tail is the step taken.
            Vector3 next = a.pos + FishSteering.Heading(newYaw) * (a.speed * dt);
            bool wanted = a.speed > 0f || Mathf.Abs(Mathf.DeltaAngle(newYaw, a.yaw)) > 0.01f;
            bool refused = false;
            Vector3 mid = 0.5f * (a.pos + next);
            float midYaw = FishSteering.LerpYaw(a.yaw, newYaw, 0.5f);
            FishBlock block = FishBlock.None;
            if (Clear(self, g, sp, next, newYaw) && Clear(self, g, sp, mid, midYaw)) a.yaw = newYaw;
            else
            {
                block = lastBlock;
                if (Clear(self, g, sp, a.pos, newYaw) && Clear(self, g, sp, a.pos, midYaw)) { a.yaw = newYaw; next = a.pos; refused = a.speed > 0f; a.speed = 0f; }
                else { block = lastBlock; next = a.pos; a.speed = 0f; refused = wanted; }
            }
            // An escaping fish moves every tick, so it isn't frozen: its run holds just past the escape threshold, and it
            // keeps escaping until a normal step or turn is clear again.
            // A startled fish doesn't wait: a blocked scatter step looks for another way at once.
            float escapeAfter = a.mode == FishMode.Scatter ? 0f : StuckEscapeSeconds;
            bool escaped = refused && a.stuck >= escapeAfter && Escape(self, g, sp, ref a, ref next, dt);
            // A scatter that can't get anywhere ends: the fish holds where it is (Flee) and returns on schedule.
            if (a.mode == FishMode.Scatter && a.stuck > 2f * StuckEscapeSeconds) a.mode = FishMode.Flee;
            // Re-slot (f-td C2 review 1): a groupmate held off its slot by a body it can't get round takes the spot it
            // holds as its slot, so it stops wanting the blocked step instead of straining at it.
            if (refused && !escaped && a.stuck > StuckEscapeSeconds &&
                (a.mode == FishMode.SchoolFollow || a.mode == FishMode.Cruise || a.mode == FishMode.Settle))
            {
                Vector3 bas = a.mode == FishMode.Settle ? grp.home : grp.target;
                a.slot = new Vector3(a.pos.x - bas.x, a.slot.y, a.pos.z - bas.z);
            }
            if (refused) { a.block = block; a.blockOther = block == FishBlock.Body ? lastOther : -1; }
            NoteStuck(ref a, escaped ? a.stuck : refused ? a.stuck + dt : 0f);
            NoteEscape(ref a, escaped, refused, next, dt);
            water.TryBed(next.x, next.z, out float bed);
            float surface = water.SurfaceY(next.x, next.z);
            var band = CentreBand(ref a, sp, bed, surface);
            if (IsSurfaceEvent(a.mode)) next.y = SurfaceEventY(ref a, sp, surface, band);
            else
            {
                // Vertical: ease toward the held depth (the bed side while fleeing), then clamp (F4).
                float holdY = a.mode == FishMode.Scatter ? band.x : HeldY(ref a, sp, next.x, next.z, bed);
                float maxVy = Mathf.Max(0.05f, 0.5f * (a.mode == FishMode.Scatter ? sp.burstSpeed : sp.cruiseSpeed));
                next.y = Mathf.Clamp(Mathf.MoveTowards(a.pos.y, holdY, maxVy * dt), band.x, band.y);
            }
            a.pos = next;
            a.animRate = t.AnimRate(a.speed, a.length);
        }

        /// <summary>The burst starts: speed and length drawn from the brief's ranges, and a flee wake if near the surface.</summary>
        void StartFlee(int self, FishSpecies sp)
        {
            ref Agent a = ref fish[self];
            a.flash = 1f;
            a.burstSeconds = a.draws.Range(t.scatter.burstSeconds);
            a.burstSpeed = a.draws.Range(t.scatter.burstSpeed);
            if (water.SurfaceY(a.pos.x, a.pos.z) - BodyExtent(self).y < t.scatter.fleeSurfaceSignDepth)
                RaiseEvent(self, sp, FishSurfaceKind.FleeWake, a.draws.Range(t.SignSeconds(FishSurfaceKind.FleeWake)), false);
        }

        /// <summary>Records a stuck run's length per blocker as it grows (the stats tests report).</summary>
        void NoteStuck(ref Agent a, float stuck)
        {
            if (stuck > 0f)
            {
                int b = (int)a.block;
                StuckLongestByBlock[b] = Mathf.Max(StuckLongestByBlock[b], stuck);
                StuckLongestBySpecies[a.species] = Mathf.Max(StuckLongestBySpecies[a.species], stuck);
                if (a.stuck <= StuckReportSeconds && stuck > StuckReportSeconds) StuckOver5ByBlock[b]++;
            }
            a.stuck = stuck;
        }

        /// <summary>An episode runs from the first escape move until a normal step or turn is clear again.</summary>
        void NoteEscape(ref Agent a, bool escaped, bool refused, Vector3 next, float dt)
        {
            if (escaped || (refused && a.escape > 0f))
            {
                if (a.escape <= 0f) a.escapeFrom = a.prevPos;
                a.escape += dt;
                if (a.escape > EscapeLongestSec) { EscapeLongestSec = a.escape; EscapeLongestNetM = Flat(next, a.escapeFrom); }
                EscapeLongestBySpecies[a.species] = Mathf.Max(EscapeLongestBySpecies[a.species], a.escape);
                return;
            }
            if (a.escape <= 0f) return;
            EscapeEpisodes++;
            if (a.escape >= 1f) EscapeWorstNetOver1sM = Mathf.Min(EscapeWorstNetOver1sM, Flat(next, a.escapeFrom));
            a.escape = 0f;
        }

        static readonly float[] EscapeTurns = { 180f, 90f, -90f };

        /// <summary>
        /// A fish boxed in for over StuckEscapeSeconds turns in place toward the reverse heading or either side, whichever
        /// first gives a clear pose, at the per-tick turn cap (f-td slot-B condition 2). If no turn is clear (nose in a
        /// dead end, a body ahead), it backs out slowly along its own axis (f-td C2 review 1), the way the tail came in.
        /// True if it moved.
        /// </summary>
        bool Escape(int self, int g, FishSpecies sp, ref Agent a, ref Vector3 next, float dt)
        {
            // Turns only when nothing turned this tick, so the whole tick's turn (prevYaw -> yaw) is the one checked,
            // including the halfway pose render draws; the slow steps below check that halfway pose too.
            bool turned = !Mathf.Approximately(a.yaw, a.prevYaw);
            foreach (float turn in EscapeTurns)
            {
                if (turned) break;
                float yaw = FishSteering.TurnToward(a.yaw, a.yaw + turn, t.maxTurnPerFrame / dt, dt, t.maxTurnPerFrame);
                if (!Clear(self, g, sp, a.pos, yaw) || !Clear(self, g, sp, a.pos, FishSteering.LerpYaw(a.yaw, yaw, 0.5f))) continue;
                a.yaw = yaw;
                return true;
            }
            // Back out, or (blocked behind) ease forward at the same slow speed.
            a.speed = BackOutShare * sp.cruiseSpeed;
            foreach (float sign in BackThenAhead)
            {
                Vector3 step = a.pos + FishSteering.Heading(a.yaw) * (sign * a.speed * dt);
                float midYaw = FishSteering.LerpYaw(a.prevYaw, a.yaw, 0.5f);
                if (Clear(self, g, sp, step, a.yaw) && Clear(self, g, sp, 0.5f * (a.pos + step), midYaw)) { next = step; return true; }
            }
            a.speed = 0f;
            return false;
        }

        static readonly float[] BackThenAhead = { -1f, 1f };

        /// <summary>Back-out speed as a share of cruise: a slow fin-backing drift, not a swim.</summary>
        const float BackOutShare = 0.25f;

        float AvoidShallows(FishSpecies sp, Vector3 p, float desiredYaw)
        {
            float bestYaw = desiredYaw, bestDepth = float.NegativeInfinity;
            for (int i = 0; i < ProbeTurns.Length; i++)
            {
                float y = desiredYaw + ProbeTurns[i];
                Vector3 q = p + FishSteering.Heading(y) * ProbeAhead;
                if (!Usable(sp, q.x, q.z, out float bed)) continue;
                if (i == 0) return desiredYaw;
                float depth = water.WaterLevelY - bed;
                if (depth > bestDepth) { bestDepth = depth; bestYaw = y; }
            }
            return bestYaw;
        }

        Vector3 DeeperDirection(Vector3 p)
        {
            const float h = 2f;
            float Bed(float x, float z) => water.TryBed(x, z, out float b) ? b : float.PositiveInfinity;
            float dx = Bed(p.x - h, p.z) - Bed(p.x + h, p.z), dz = Bed(p.x, p.z - h) - Bed(p.x, p.z + h);
            if (float.IsNaN(dx) || float.IsInfinity(dx)) dx = 0f;
            if (float.IsNaN(dz) || float.IsInfinity(dz)) dz = 0f;
            var d = new Vector3(dx, 0f, dz);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.zero;
        }

        Vector3 Centroid(in Group grp)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int f = grp.first; f >= 0; f = fish[f].next) { sum += fish[f].pos; n++; }
            return n > 0 ? sum / n : grp.home;
        }

        static Vector3 Disc(ref FishDraws d)
        {
            float ang = d.Range(0f, 2f * Mathf.PI), r = Mathf.Sqrt(d.Next());
            return new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
