---
type: decision
status: accepted
tech: [physics]
area: []
issues: [43]
prs: [50]
source: "studio:decisions gate/physics-layers-43; issue #43; PR #50"
decided_by: "game-director (gate, with conditions)"
created: 2026-10-30
updated: 2026-10-30
supersedes: ""
superseded_by: ""
---
# 0003 Player off Default: own layers and a minimal collision matrix

_Every kind of object gets its own physics layer, the player leaves `Default`, and the collision matrix turns off pairs that never interact. The director greenlit it on condition that nothing the player can see or feel changes._

## Context
Every layer collided with every other, and the player shared `Default` with the world. Wildlife and bird casts and robin landing checks hit the player (#43). This is groundwork for multiplayer hitboxes (#13).

## Decision
Game director, 2026-10-21 (`gate/physics-layers-43`):

> Director gate #43 (physics layers + minimal collision matrix), 2026-10-21: GREENLIT WITH CONDITIONS. No pillar impact if behaviour is unchanged; wildlife/bird sightlines are placement-time terrain-only checks that already ignore the player [...] so who-sees-whom does not change.

The six conditions, abridged (full text in the record):
- C1: the first-person look is unchanged.
- C2: robins still treat the player as an obstacle.
- C3: no feel-visible pair is removed. Any pair with an in-play effect comes to the director first.
- C4: queries keep ignoring the player's own colliders.
- C5: vendor and ThirdParty assets are not re-layered in the asset.
- C6: the tower walk time and speeds are unchanged.

> Hitbox/Ragdoll reserved only.

## Consequences
- Good, because each query states what it hits, through named masks in `WorldLayers`.
- Good, because the matrix is tested against the ruled table (`PhysicsLayersTests`).
- Bad, because a new object category now needs a layer, a mask and a matrix review in the same change.
- How to work with it: [[physics-layers-and-collision-masks]]. The standing rules (16, 22) and the per-pair table are in [physics-guidelines](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md).

## Related
[[moving-colliders]]
