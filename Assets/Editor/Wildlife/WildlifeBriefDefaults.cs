using UnityEngine;

namespace WashedAshore.Wildlife.Editor
{
    /// <summary>
    /// The approved numbers from _bmad-output/poc/wildlife-density-brief.md (game-director,
    /// 2026-10-05). Written into WildlifeTuning when it is created or reset; tune the asset, not this.
    /// </summary>
    public static class WildlifeBriefDefaults
    {
        const float PreyAngularSpeed = 600f;

        public static void Apply(WildlifeTuning t)
        {
            t.defaultSeed = 101;
            t.briefRevision = "R1";
            t.species = new[]
            {
                // 3.3: walk, run, idle bout, wander leg, leash (3.2), cohesion (3.1), alert, trigger, release, max flee, calm.
                // Max flee is retune R0 (director-approved 2026-10-05): deer/stag 10 -> 6 s, fox 8 -> 5 s.
                // Leash is retune R1 (director-approved 2026-10-05): deer 30 -> 20, stag 40 -> 25, fox 50 -> 30, wolf 60 -> 35 m.
                Prey(WildlifeSpecies.Deer, 1.3f, 9f, new Vector2(4f, 12f), new Vector2(5f, 20f), 20f, 12f, 50f, 35f, 70f, 6f, 6f, 0.7f),
                Prey(WildlifeSpecies.Stag, 1.4f, 9f, new Vector2(4f, 12f), new Vector2(8f, 25f), 25f, 15f, 55f, 40f, 75f, 6f, 6f, 0.7f),
                Prey(WildlifeSpecies.Fox, 1.2f, 7f, new Vector2(3f, 8f), new Vector2(10f, 30f), 30f, 12f, 35f, 25f, 50f, 5f, 4f, 0.4f),
                // Wolf: keeps >= 45 m, retreat ends at >= 60 m, then stops and faces the player (alert ring = 60 m).
                new SpeciesTuning
                {
                    species = WildlifeSpecies.Wolf, response = ThreatResponse.KeepDistance,
                    walkSpeed = 1.5f, runSpeed = 6f, idleSeconds = new Vector2(5f, 15f), wanderLeg = new Vector2(15f, 40f),
                    leashRadius = 35f, cohesionRadius = 10f, alertDistance = 60f, triggerDistance = 45f,
                    releaseDistance = 60f, maxFleeSeconds = 0f, calmDownSeconds = 4f, grazeChance = 0.3f,
                },
            };
            // WL-BUG-7: the Quaternius FBXs face -Z; ModelFacingPostprocessor turns them to +Z at import, so no
            // per-species model yaw is set here.
            // WL-BUG-7 option B: prey pivot to flee in ~0.3 s (180 degrees at 600 deg/s) instead of shuffling
            // sideways for ~0.6 s at the 300 deg/s default. Wolves keep the default (they back off, not bolt).
            foreach (var s in t.species)
                if (s.response == ThreatResponse.Flee) s.angularSpeed = PreyAngularSpeed;
            t.population = new[]
            {
                new GroupSpec { species = WildlifeSpecies.Deer, groupCount = 2, groupSize = 4 },
                new GroupSpec { species = WildlifeSpecies.Stag, groupCount = 1, groupSize = 3 },
                new GroupSpec { species = WildlifeSpecies.Fox, groupCount = 2, groupSize = 1 },
                new GroupSpec { species = WildlifeSpecies.Wolf, groupCount = 1, groupSize = 2 },
            };
            // R0: one route arc per group, wolf off the 2 arcs touching W0. R1/R1-A: 15-35 m from the route,
            // cyclic arc adjacency, approach sightline, up to 20 arc-assignment draws.
            t.placement = new PlacementRules
            {
                minFromRoute = 15f, maxFromRoute = 35f, maxAssignmentDraws = 20,
                deerArcsNotAdjacent = true, stagNotAdjacentToDeer = true, approachSightline = true,
            };
            t.sighting = new SightingTargets
            {
                routeOffsets = new[]
                {
                    new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(120f, 30f), new Vector2(170f, 100f),
                    new Vector2(110f, 170f), new Vector2(30f, 180f), new Vector2(-40f, 120f), new Vector2(-50f, 30f),
                    new Vector2(20f, -40f), new Vector2(110f, -30f), new Vector2(0f, 0f), // W10 = W0 closes the loop
                },
            };
        }

        static SpeciesTuning Prey(WildlifeSpecies s, float walk, float run, Vector2 idle, Vector2 leg, float leash,
            float cohesion, float alert, float trigger, float release, float maxFlee, float calm, float graze) =>
            new SpeciesTuning
            {
                species = s, response = ThreatResponse.Flee, walkSpeed = walk, runSpeed = run, idleSeconds = idle,
                wanderLeg = leg, leashRadius = leash, cohesionRadius = cohesion, alertDistance = alert,
                triggerDistance = trigger, releaseDistance = release, maxFleeSeconds = maxFlee,
                calmDownSeconds = calm, grazeChance = graze,
            };
    }
}
