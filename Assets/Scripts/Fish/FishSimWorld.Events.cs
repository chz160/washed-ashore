using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Fish
{
    public sealed partial class FishSimWorld
    {
        // Active-sign end times (maxSurfaceEvents at once) and recent jump times (maxJumpsPerMin), pre-sized.
        readonly double[] eventEnds = new double[FishTuning.AdrMaxEvents];
        readonly bool[] eventFar = new bool[FishTuning.AdrMaxEvents];
        readonly double[] jumpTimes = new double[16];
        int jumpCursor;

        const float Gravity = 9.81f;

        static bool IsSurfaceEvent(FishMode m) => m == FishMode.Rise || m == FishMode.Jump || m == FishMode.Bask;

        /// <summary>Height the centre climbs in a jump: from the band top until the body's bottom clears the water by jumpHeight.</summary>
        float JumpRise(ref Agent a, FishSpecies sp)
        {
            float surface = water.SurfaceY(a.pos.x, a.pos.z);
            water.TryBed(a.pos.x, a.pos.z, out float bed);
            var band = CentreBand(ref a, sp, bed, surface);
            var e = Extent(a.variant, Vector3.zero, a.yaw, a.scale);
            return surface - e.x + a.jumpHeight - band.y;
        }

        /// <summary>Signs on the water right now (F7 counter and the cap).</summary>
        public int ActiveSurfaceEvents
        {
            get
            {
                double now = WaterClock.Now;
                int n = 0;
                for (int i = 0; i < t.maxSurfaceEvents; i++) if (eventEnds[i] > now) n++;
                return n;
            }
        }

        /// <summary>
        /// Starts a scripted surface event on live fish slot <paramref name="f"/> (the scheduler's spontaneous signs).
        /// Rise-type signs bring the body's top to the surface and back; a jump arcs above it; a bask holds at the
        /// surface. Only these modes may break the surface margin (F4). Returns false when the caps refuse it.
        /// </summary>
        public bool StartSurfaceEvent(int f, FishSurfaceKind kind)
        {
            ref Agent a = ref fish[f];
            if (!a.live || IsSurfaceEvent(a.mode) || a.mode == FishMode.Scatter || a.mode == FishMode.Flee) return false;
            var sp = t.species[a.species];
            if (kind == FishSurfaceKind.Jump && !sp.canJump) return false;   // ruling/fish-airborne-jumps
            bool bask = kind == FishSurfaceKind.GarGulpOrBask && a.draws.Next() >= t.garGulpShare;
            float sign = a.draws.Range(bask ? t.baskSeconds : t.SignSeconds(kind));
            float body = sign;
            if (kind == FishSurfaceKind.Jump)
            {
                // Ballistic: the centre climbs from the band top to clear the water by the jump height, and falls back.
                a.jumpHeight = a.draws.Range(t.jumpHeightBodyLengths) * a.length;
                body = 2f * Mathf.Sqrt(2f * Mathf.Max(0.05f, JumpRise(ref a, sp)) / Gravity);
            }
            if (!RaiseEvent(f, sp, kind, sign, true, bask)) return false;
            a.mode = kind == FishSurfaceKind.Jump ? FishMode.Jump : bask ? FishMode.Bask : FishMode.Rise;
            a.eventKind = kind;
            a.eventLength = body;
            a.eventAge = 0f;
            return true;
        }

        /// <summary>A sign with no live body (its cell is dormant; at event range no body would be drawn).</summary>
        public bool RaiseSign(FishSurfaceKind kind, Vector3 at, float size, int species, bool far = false)
        {
            float length = t.SignSeconds(kind).y;
            if (!Admit(kind, length, far)) return false;
            FishEvents.Raise(new FishSurfaceEvent
            {
                kind = kind, position = new Vector3(at.x, water.SurfaceY(at.x, at.z), at.z), size = size, heading = Vector3.forward,
                fishId = -1, species = species, startTime = WaterClock.Now, duration = length, spontaneous = true, farWater = far,
            });
            return true;
        }

        bool RaiseEvent(int f, FishSpecies sp, FishSurfaceKind kind, float length, bool spontaneous, bool bask = false)
        {
            if (!Admit(kind, length)) return false;
            ref Agent a = ref fish[f];
            FishEvents.Raise(new FishSurfaceEvent
            {
                kind = kind, position = new Vector3(a.pos.x, water.SurfaceY(a.pos.x, a.pos.z), a.pos.z), size = a.length,
                heading = FishSteering.Heading(a.yaw), fishId = a.fishId, species = a.species, startTime = WaterClock.Now,
                duration = length, spontaneous = spontaneous, bask = bask,
            });
            return true;
        }

        /// <summary>Signs admitted, and refused by the live-sign cap or the jump cap (f-td: report the binding share).</summary>
        public int SignsAdmitted { get; private set; }
        public int SignsRefusedLiveCap { get; private set; }
        public int JumpsRefusedCap { get; private set; }
        /// <summary>Live-cap refusals by origin: far-water signs, and near signs (fish, dormant groups, flush wakes).</summary>
        public int FarRefusedLiveCap { get; private set; }
        public int NearRefusedLiveCap { get; private set; }
        /// <summary>Near signs refused while at least one live slot was held by a far-water sign (a finding, brief F16).</summary>
        public int NearRefusedWhileFarHeld { get; private set; }

        /// <summary>The caps: at most maxSurfaceEvents signs at once, and at most maxJumpsPerMin jumps in any minute (N4).</summary>
        bool Admit(FishSurfaceKind kind, float length, bool far = false)
        {
            double now = WaterClock.Now;
            int free = -1;
            bool farHeld = false;
            for (int i = 0; i < t.maxSurfaceEvents; i++)
            {
                if (eventEnds[i] <= now) { if (free < 0) free = i; }
                else farHeld |= eventFar[i];
            }
            if (free < 0)
            {
                SignsRefusedLiveCap++;
                if (far) FarRefusedLiveCap++;
                else { NearRefusedLiveCap++; if (farHeld) NearRefusedWhileFarHeld++; }
                return false;
            }
            if (kind == FishSurfaceKind.Jump)
            {
                int recent = 0;
                for (int i = 0; i < jumpTimes.Length; i++) if (jumpTimes[i] > 0 && now - jumpTimes[i] < 60.0) recent++;
                if (recent + 1 > t.maxJumpsPerMin) { JumpsRefusedCap++; return false; }
                jumpTimes[jumpCursor] = now;
                jumpCursor = (jumpCursor + 1) % jumpTimes.Length;
            }
            eventEnds[free] = now + length;
            eventFar[free] = far;
            SignsAdmitted++;
            return true;
        }

        void SurfaceEventStep(ref Agent a, FishSpecies sp, float dt, out Vector3 dir, out float targetSpeed)
        {
            a.eventAge += dt;
            dir = FishSteering.Heading(a.yaw);
            targetSpeed = a.mode == FishMode.Jump ? sp.burstSpeed : a.mode == FishMode.Bask ? 0f : 0.5f * sp.cruiseSpeed;
            if (a.eventAge >= a.eventLength)
            {
                a.mode = FishMode.Hover;
                a.eventLength = 0f;
            }
        }

        /// <summary>
        /// Centre height during a surface event. Each curve starts and ends at the band top (surface - margin for the
        /// body), so the clamp takes over again without a jump: a rise lifts the top just through the surface, a jump arcs
        /// jumpHeightBodyLengths above it, a bask eases in, holds the top at the surface and eases out.
        /// </summary>
        float SurfaceEventY(ref Agent a, FishSpecies sp, float surface, Vector2 band)
        {
            if (!IsSurfaceEvent(a.mode)) return Mathf.Clamp(a.pos.y, band.x, band.y);
            float p = Mathf.Clamp01(a.eventAge / Mathf.Max(1e-3f, a.eventLength));
            var e = Extent(a.variant, Vector3.zero, a.yaw, a.scale);
            float topAtSurface = surface - e.y;                     // centre height that puts the body's top on the surface
            switch (a.mode)
            {
                case FishMode.Jump:
                    return band.y + 4f * p * (1f - p) * JumpRise(ref a, sp);
                case FishMode.Bask:
                    float ease = Mathf.Clamp01(Mathf.Min(a.eventAge, a.eventLength - a.eventAge) / 1f);
                    return Mathf.Lerp(band.y, topAtSurface, ease);
                default:
                    return Mathf.Lerp(band.y, topAtSurface + 0.25f * (e.y - e.x), Mathf.Sin(Mathf.PI * p));
            }
        }
    }
}
