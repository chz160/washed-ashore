---
type: pattern
status: active
tech: [physics]
area: [lookout-tower]
issues: [45, 40]
prs: [56, 57]
source: "PR #56 (body and review reply 2026-10-25); issue #45; issue #40 comment 2026-10-25"
created: 2026-10-30
updated: 2026-10-30
aliases: [hinged leaves, kinematic movers, prebaked collision]
---
# Moving colliders

_A collider that moves during play is switched off while it moves and back on at rest. The hatch and sashes don't use kinematic Rigidbodies, because a kinematic leaf would sweep into the `CharacterController` and embed the player. Mesh colliders are baked ahead of time, never at runtime._

## Problem
- `Hinge` rotated leaf transforms with no Rigidbody, which moves static colliders. PhysX then rebuilds its spatial structure on every move.
- With `autoSyncTransforms` off, a query in the same frame as a move sees the old pose.
- Mid-swing sash colliders lagged the mesh by up to 1.7 cm (PR #56).

## When it applies
Doors, hatches, sashes and gates, plus anything a script moves in play. Standing rules 9, 10, 19 and 21 in [physics-guidelines](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md).

## What to do
- **Switch colliders off while moving** (the `TrapDoor` pattern). `Hinge` turns every collider under a leaf off while it swings and on at rest. Call `Physics.SyncTransforms()` once at each settle.
- **Refuse a swing that would hit the player.** A sash won't swing while the player overlaps its swept volume, sampled across the whole arc. The hatch refuses to close while the player stands in the opening (PR #57).
- **Keep state replicable:** `HingeState` plus elapsed time. No physics forces or joints drive doors.
- **Declare movers.** A component that moves transforms lists them in `IMovingTransforms`. `MovingCollidersTests` fails on any collider under one that is neither switched off nor on a kinematic body.
- **Never move static colliders in play.** `BoundaryGate` refuses to move its leaves in play mode and logs once.
- **Bake mesh colliders ahead of time.** The tower's convex `MeshCollider`s set `cookingOptions` explicitly (30, the value the default always gave). Prebaking is on in `MC39_Lookout.fbx.meta` and `ProjectSettings.asset`.

## Evidence (PR #56)
- **Feel unchanged:** hatch 85 frames (1.42 s) to 92.0°, sashes 55 frames (0.92 s) to 35.0°, identical before and after. Forced swings moved the player 0.000 mm.
- **Shapes unchanged:** `TowerMeshTable` prints all 40 convex meshes with one hash, identical before and after the prebake change.
- **Build log:** the Windows build went from 1 "Pre-baked collision is disabled" line (naming 40 meshes) to 0.

## Open
- A hinge set straight to another rest state (a network snap or a late joiner) shows its colliders at the new pose without the swing blocker (#40 comment, #13).
- Camera near-plane clipping into a moving leaf and flicker were moved to #40's criteria. Whether they are now asserted there was not checked for this note.

## Does not apply when
A dynamic prop the server simulates. Use `NetworkRigidbody` (rule 12).

## Related
[[physics-layers-and-collision-masks]], [[lookout-tower-traversal-and-interactions]]
