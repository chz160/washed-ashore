using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>Per-fish behaviour state (adr/fish-1 §5.3).</summary>
    public enum FishMode : byte { Hold, Hover, Cruise, SchoolFollow, Scatter, Settle, Rise, Bask, Jump, Flee }

    /// <summary>How the renderer drew a fish this frame (ruling/fish-visibility-tiers); written by the renderer, logged by f-qa.</summary>
    public enum FishDrawMode : byte { None, Shadow, Body }

    /// <summary>
    /// One live fish as the renderer sees it (the seam with WashedAshore.Fish.Rendering). Written by FishPopulation
    /// each frame from the interpolated sim; read-only to everyone else. Rotation is LookRotation(forward, up) and
    /// nothing else.
    /// </summary>
    public struct FishState
    {
        /// <summary>Stable id from (cell, group, member): the same fish whenever its cell comes back (FishSimWorld.FishId).</summary>
        public long fishId;
        public bool active;
        public FishMode mode;
        public Vector3 position;
        /// <summary>Unit heading (with pitch) at the rendered frame; the only rotation source: LookRotation(forward, up).</summary>
        public Vector3 forward;
        public float speed;
        /// <summary>Swim-clip phase in [0, 1), integrated by the sim: phi += dt * animRate.</summary>
        public float animPhase;
        /// <summary>Swim-clip cycles per second actually fed to the renderer (F5, f-qa dump).</summary>
        public float animRate;
        /// <summary>Swim-stroke amplitude 0-1 from effort (speed / cruise speed, idle floor): the VAT blends rest + amplitude x (anim - rest).</summary>
        public float amplitude;
        /// <summary>
        /// Flank flash 0-1 (N1 "flash on the shelf", a fish rolling as it turns or bursts). Contract with the renderer: it
        /// may only lerp the albedo toward the species' dull-silver swatch inside the murk composite. Never emissive,
        /// never albedo above 1, no rim or glow (N5).
        /// </summary>
        public float flash;
        public int speciesIndex;
        /// <summary>The fish's group slot in FishSimWorld (groupmates share it while the group is live).</summary>
        public int group;
        public int variantIndex;
        public float sizeScale;
        /// <summary>World Y of the body's lowest and highest point this frame (clip-max box x TRS; F4, f-qa dump).</summary>
        public float bodyMinY, bodyMaxY;
        /// <summary>True while a scripted Rise, Bask or Jump may break the surface margin (F4 exemption).</summary>
        public bool inSurfaceEvent;
    }
}
