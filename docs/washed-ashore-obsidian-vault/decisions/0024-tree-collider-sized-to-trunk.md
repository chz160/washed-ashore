---
type: decision
status: accepted
tech: [collider-geometry, physics]
area: [terrain]
issues: [21]
prs: []
source: "task #14 (table, DEAD TREES FIT, STANDOUT TABLE, QA lines, technical-director reviews); TestResults/21-l2-after-editmode.xml"
decided_by: "team-lead (#21 scope ruling), reviewed by technical-director"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0024 Tree colliders are sized to the trunk, refit only for lean

_Each tree LOD prefab gets one capsule whose radius is the measured trunk. A prototype is refitted only where the capsule stands out above 0.4 m because the trunk leans. The remaining 0.24 to 0.40 m stand-out is a known limit._

## Context
The LOD prefabs had no collider, and the kit capsules (radius 0.35 to 0.90 m) did not always match the trunk. TwistedTree_1's kit radius of 0.90 m left a capsule that stood out 0.50 m beyond its trunk; DeadTree_1 and DeadTree_2 stood out 0.43 and 0.52 m because they lean.

## Options considered
- Copy the kit capsule unchanged: rejected for TwistedTree_1 (stand-out 0.50 m).
- Fit every prototype to every height slice: not done; larger scope than #21.
- Fit only where it matters: chosen.

## Decision
- Radius is the median horizontal distance of the LOD0 vertices from their centroid in the first height band (0.3-1, 1-2 or 2-3 m) with at least 20 vertices. Height and Y centre come from the kit capsule.
- Refit only where the overhang is above 0.4 m and comes from lean: TwistedTree_1 (radius 0.95, centre x -0.3, z 0), DeadTree_1 (centre -0.25, -0.07) and DeadTree_2 (centre -0.28, 0.31), the last two with the radius kept. Entries live in `BellsBendTreeLods.TrunkFit`.
- Stand-out at or below 0.4 m stays, recorded as a known limit: CommonTree 0.30-0.40 m, Pine 0.24-0.27 m (`CapsuleDoesNotStandOutFromTheTrunkInThePlayersBand`, `TestResults/21-l2-after-editmode.xml`). After the fits: TwistedTree_1 0.15, DeadTree_1 0.19, DeadTree_2 0.21 m, held to limits 0.2 / 0.2 / 0.25.
- Prototypes not in `TrunkFit` must keep the kit centre exactly (within 0.001).

## Consequences
- Good, because the player is stopped at roughly the trunk and a 1.20 m gap between two trees passes (`21-bush-tree-playmode-final4.xml`).
- Bad, because an invisible edge up to 0.40 m outside the trunk remains on unfitted trees, TwistedTree_1's root flare clips up to 0.71 m, the stand-out test measures only the four axis directions (technical-director L3), and `TrunkFit` values are entered by hand (L4).
- A visual playtest is still needed (Studio Head).

## Related
Reviews: technical-director's two #14 reviews, the TwistedTree_1 fix and the dead-tree fit with L2 (the second covered by `21-bush-tree-playmode-final4.xml`), cited from its messages to team-lead (20:40) because the task text was overwritten later. QA: qa-lead's #14 lines. See [[tree-collision-and-bush-slowdown]], [[collider-geometry-for-a-steered-capsule]], [[physics-layers-and-collision-masks]]. Issue: https://github.com/chz160/washed-ashore/issues/21
