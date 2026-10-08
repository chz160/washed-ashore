using UnityEngine;

namespace WashedAshore.Fish.Rendering
{
    /// <summary>
    /// How a fish may be drawn (ruling/fish-visibility-tiers, ruling/fish-roster-look; F7). Pure, so tests and f-qa's probe
    /// apply the same rule the renderer does. "Drawn" = FishMurk.Drawable at the body's shallowest point, within
    /// the draw radius of the camera, inside the camera frustum, then the tier. The radius is FishTuning.BodyDrawRadius(mode,
    /// top depth), the one function FishView.CouldSee also uses (R_sub 25 m; a jumping body at or above the surface to the
    /// jump radius, ruling/fish-jump-body-radius), so the drawn set stays inside CouldSee (f-td review 3a). Then the tier:
    /// V3 (and any neverDrawBody species) never; V2 only as a shadow; V1 as a body only while its top is within
    /// bodyTierDepth of the surface. Tiers, R_sub and the frustum only ever remove fish.
    /// </summary>
    public static class FishDrawRule
    {
        /// <summary>Brief visibility.subSurfaceFadeBand (3 m): bodies fade out over the last 3 m before their draw radius.</summary>
        public const float RangeFadeBand = 3f;

        /// <summary>
        /// Alpha factor for the draw-radius edge: 1 up to radius - band, ramping linearly to 0 at the radius. It's only ever
        /// applied to fish Decide allows (distance <= radius), so it stays inside CouldSee and never adds a fish.
        /// </summary>
        public static float RangeFade(float cameraDistance, float radius, float band = RangeFadeBand) =>
            Mathf.Clamp01((radius - cameraDistance) / Mathf.Max(band, 1e-3f));

        public static FishDrawMode Decide(FishTier tier, bool neverDrawBody, float topDepth, float cameraDistance, float visibility,
                                          float murkFloor, float bodyTierDepth, float bodyDrawDistance, bool airborneBody = false)
        {
            // ruling/fish-airborne-jumps-skipjack: an airborneBodyAllowed species (carp, skipjack) shows its body only while it
            // is at or above the surface in its own jump; underwater its tier rule stands (V3 never, etc.).
            if (airborneBody && topDepth <= 0f)
                return visibility >= murkFloor && cameraDistance <= bodyDrawDistance ? FishDrawMode.Body : FishDrawMode.None;
            if (neverDrawBody || tier == FishTier.V3) return FishDrawMode.None;
            if (visibility < murkFloor) return FishDrawMode.None;
            if (cameraDistance > bodyDrawDistance) return FishDrawMode.None;
            if (tier == FishTier.V2) return FishDrawMode.Shadow;
            return topDepth <= bodyTierDepth ? FishDrawMode.Body : FishDrawMode.None;
        }

        /// <summary>
        /// The renderer's whole per-fish decision: murk visibility (written to FishPopulation.Visibility), the rule above
        /// and the frustum against the body's box (centre, horizontal half extent, height).
        /// </summary>
        public static FishDrawMode Decide(in FishMurk murk, Plane[] frustum, Vector3 eye, FishTier tier, bool neverDrawBody, float surfaceY,
                                          float bodyMinY, float bodyMaxY, Vector3 centre, float halfLength, float bodyTierDepth, float bodyDrawDistance,
                                          out float visibility, out Bounds box, bool airborneBody = false)
        {
            Vector3 toEye = eye - centre;
            float dist = toEye.magnitude;
            float topDepth = surfaceY - bodyMaxY;
            visibility = murk.Visibility(topDepth, dist > 1e-5f ? toEye.y / dist : 1f);
            box = new Bounds(centre, new Vector3(2f * halfLength, Mathf.Max(0.05f, bodyMaxY - bodyMinY), 2f * halfLength));
            var mode = Decide(tier, neverDrawBody, topDepth, dist, visibility, murk.floor, bodyTierDepth, bodyDrawDistance, airborneBody);
            if (mode != FishDrawMode.None && (frustum == null || !GeometryUtility.TestPlanesAABB(frustum, box))) mode = FishDrawMode.None;
            return mode;
        }
    }
}
