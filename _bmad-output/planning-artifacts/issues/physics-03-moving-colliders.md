Stop moving colliders that have no Rigidbody, and make the tower's collider cooking settings explicit. Rules 9, 10 and 21 in [`docs/physics-guidelines.md`](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md).

## What we know (audit 2026-10-20)

- **`Hinge`** (`Assets/Scripts/Lookout/Hinge.cs:73-79`) rotates `leaf.localRotation` through its transform, with no Rigidbody.
  - **`TrapDoor`** works around this by switching the leaf collider off while it swings and switching on a swing blocker that's built in advance.
  - **`WindowSash`** (`WindowSash.cs:45-54`) rotates its transform with no collider handling.
- **`BoundaryGate`** (`Assets/Scripts/Level/BoundaryGate.cs:17-24`, `[ExecuteAlways]`) rotates gate leaves whose `LeafCollider` boxes are built `isStatic = true` (`BarrierBuilder.cs:466-487`). This happens only at configuration time today. If the gate ever opens during play, static colliders get moved.
- **`autoSyncTransforms` is off.** A query that runs in the same frame as a transform move sees the old pose.
- **The tower's convex `MeshCollider`s** from `WalkableTowerBuilder.cs:179-226` don't set `cookingOptions`, so they rely on the defaults.
- **#40:** the hatch, sashes and firefinder aren't wired up on the walkable tower yet. Fix this issue before or together with #40.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| M1 | **Every collider that moves during play has a kinematic Rigidbody moved with `MovePosition`/`MoveRotation`,** or is switched off while it moves, like `TrapDoor`. That covers the `Hinge` leaves, `WindowSash`, and `BoundaryGate` if it opens during play | Diff; an EditMode scan that fails on any collider under a moving transform without a Rigidbody |
| M2 | **Hinge state stays replicable,** as an enum or an angle and elapsed time, with no physics-driven door motion | Code review |
| M3 | **Explicit `cookingOptions`** on the tower MeshColliders, and 0 "not pre-baked" warnings in the build log (shared with #8 S7) | Build log |
| M4 | **No regressions:** TowerWalkTests passes (up time 26.6 s ±10%), the hatch and sash tests pass, and a swinging leaf can't push or trap the player | Test XML; PlayMode test |

## Automatic FAIL

- Doors or sashes driven by physics forces or joints.
- Changing tower walk speeds.

## Pod

**gameplay-engineer** (lead) and **qa-lead**: 2 agents.

Related: #8, #40.
