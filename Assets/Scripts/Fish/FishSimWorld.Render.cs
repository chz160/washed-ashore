using UnityEngine;

namespace WashedAshore.Fish
{
    public sealed partial class FishSimWorld
    {
        /// <summary>
        /// Writes the frame's interpolated pose of every live fish into <paramref name="states"/> (the renderer seam) and
        /// returns how many were written. Yaw is interpolated between the last two ticks, then capped against last
        /// frame's rendered yaw (F5 is per rendered frame, hitches included). Y is clamped again at the interpolated XZ
        /// against the current surface, so the rendered body never leaves the band between ticks (F4). The Swim phase
        /// advances by the frame time at the fed rate; nothing here makes a decision.
        /// </summary>
        public int Render(float alpha, float frameDelta, FishState[] states)
        {
            int n = 0;
            for (int f = 0; f < fish.Length && n < states.Length; f++)
            {
                ref Agent a = ref fish[f];
                if (!a.live) continue;
                var sp = t.species[a.species];
                Vector3 p = Vector3.Lerp(a.prevPos, a.pos, alpha);
                a.renderedYaw = FishSteering.RenderedYaw(a.renderedYaw, FishSteering.LerpYaw(a.prevYaw, a.yaw, alpha), t.maxTurnPerFrame);
                bool surfaceEvent = a.mode == FishMode.Rise || a.mode == FishMode.Jump || a.mode == FishMode.Bask;
                if (!surfaceEvent && water.TryBed(p.x, p.z, out float bed))
                {
                    var band = CentreBand(ref a, sp, bed, water.SurfaceY(p.x, p.z));
                    p.y = Mathf.Clamp(p.y, band.x, band.y);
                }
                float speed = a.speed;
                a.animPhase = Mathf.Repeat(a.animPhase + Mathf.Max(0f, frameDelta) * a.animRate, 1f);
                // Level heading: no pitch, so the clip-max box the clamp uses is the box that's drawn.
                Vector3 fwd = FishSteering.Heading(a.renderedYaw);
                states[n++] = new FishState
                {
                    fishId = a.fishId, active = true, mode = a.mode, position = p, forward = fwd, speed = speed,
                    animPhase = a.animPhase, animRate = a.animRate,
                    amplitude = Mathf.Clamp(speed / Mathf.Max(sp.cruiseSpeed, 1e-3f), t.amplitudeIdle, 1f),
                    flash = a.flash, speciesIndex = a.species, group = a.group, variantIndex = a.variant, sizeScale = a.scale,
                    inSurfaceEvent = surfaceEvent,
                };
                var e = Extent(a.variant, p, a.renderedYaw, a.scale);
                states[n - 1].bodyMinY = e.x;
                states[n - 1].bodyMaxY = e.y;
            }
            return n;
        }
    }
}
