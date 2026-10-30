---
type: pattern
status: active
tech: [physics]
area: []
issues: [43, 44]
prs: [50, 54]
source: "PR #50 and #54; issues #43, #44; studio:decisions gate/physics-layers-43"
created: 2026-10-30
updated: 2026-10-30
aliases: [layers, collision matrix, WorldLayers, NonAlloc]
---
# Physics layers and collision masks

_Every kind of object has its own layer. The collision matrix turns off pairs that never interact, and every query uses a named mask from `WorldLayers` with no-allocation variants. The player is on its own layer, not `Default`._

## Problem
Before #43 every layer collided with every other, and the player shared `Default` with the world.
- Wildlife and bird sightline casts and robin landing checks hit the player. Filtering out its own colliders (`IsOwn`) was the only defence.
- Masks were built by hand and didn't agree with each other. The climb fit test used `~controller.excludeLayers`; the stance code used `DefaultRaycastLayers & ~(Water|WorldBounds)`.

## When it applies
Any new object category, collider, raycast, cast or overlap.

The standing rules and the per-pair matrix table live in [physics-guidelines](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md), rules 16, 17 and 22 and the "Layers and the Layer Collision Matrix" section. This note does not copy them.

## What was done
- **Layers (PR #50):** Player (10), Wildlife (11), Interactable (12), Prop (13). Hitbox (14) and Ragdoll (15) are reserved only.
- **Runtime layering:** the player and wildlife are put on their layers at runtime. No scene, prefab or ThirdParty asset is re-layered.
- **Named masks:** `PlayerCollisionMask`, `SightMask`, `RobinObstacleMask` and others live in `WorldLayers`, which throws if a layer is missing.
- **Allowed `~0`:** five test lines keep `~0` on purpose as "is anything there" probes (director, `gate/physics-layers-43-amend1-l3`).
- **No allocations in per-frame code (PR #54):**
  - The bounds clamp no longer calls `FindObjectsByType<Rigidbody>()` every `LateUpdate`. Bodies register through `WorldBoundsBody`.
  - The robin planner uses `SphereCastNonAlloc` with a static 64-hit buffer. A full buffer counts as blocked (fail closed) and is counted.

## Evidence
- All 90 Broadphase and Narrowphase rows identical before and after (Editor counts, PR #50).
- `PhysicsLayersTests` checks the matrix against the ruled table, and was shown failing once on purpose.
- Robin paths byte-identical before and after (`robin-paths.csv`, seeds 101/202/303). `MaxCastHits` stayed at 0 of 64 over real plans (PR #54 comment).
- Allocation tests use NUnit's `Is.Not.AllocatingGCMemory`. `GC.GetAllocatedBytesForCurrentThread` reads 0 in this Editor even for allocating code, so don't use it (PR #54).

## What to do
- **New object category:** give it a layer, add its mask to `WorldLayers`, and review the matrix and its table in the same change.
- **Every query:** pass an explicit mask, a finite distance and `QueryTriggerInteraction.Ignore`. The #64 probe's `Detail()` still breaks this (#81).
- **Rigidbodies:** a Rigidbody created only in code gets no `WorldBoundsBody` automatically. Add it in the same change (PR #54 follow-up).
- **Turning a pair off:** say whether it has any visible in-play effect today. A pair with one goes to the director first (gate C3).

## Does not apply when
ThirdParty assets. Layer them at runtime or in the scene, never in the asset (gate C5).

## Related
[[0003-player-off-default-layer]], [[moving-colliders]]
