---
title: 'POC controller tuning + spawn area: Washed Ashore walk-around'
owner: systems-designer
consumers: gameplay-engineer, level-designer
created: '2026-10-04'
---

# Controller tuning (CharacterController + Input System)

Goal: grounded first-person walk. No jump, sprint, crouch or stamina (out of scope for the spike). All values are serialized fields with these defaults so they can be retuned without code changes.

## Capsule
| Param | Value | Note |
|---|---|---|
| `height` | 1.8 m | |
| `radius` | 0.4 m | |
| `center` | (0, 0.9, 0) | Transform pivot at the feet |
| `stepOffset` | 0.4 m | Must stay < height; clears small rocks / terrain lips |
| `slopeLimit` | 45° | Steeper terrain blocks the player — level keeps the spawn lane well under this |
| `skinWidth` | 0.05 m | ~10% of radius (Unity guidance) |
| `minMoveDistance` | 0 | Avoids dropped tiny moves at high frame rates in tests |

## Movement
| Param | Value | Note |
|---|---|---|
| `walkSpeed` | 5.0 m/s | 2 s of W ≈ 9.4 m with accel below — 3x margin over the 3 m test |
| `acceleration` | 20 m/s² | Full speed in 0.25 s |
| `deceleration` | 25 m/s² | |
| Move space | Yaw only (camera pitch ignored) | W is horizontal forward |
| Diagonal | Normalize input vector | No faster diagonals |

## Gravity / grounding
| Param | Value | Note |
|---|---|---|
| `gravity` | -20 m/s² | ~2x real; feels grounded, lands fast for test (c) |
| `groundedStickVelocity` | -2 m/s | Applied when `isGrounded` so the controller stays snapped on downhill |
| `terminalVelocity` | -50 m/s | |
| Fall-through guard | If player base y < `SampleHeight` - 2 m, teleport to `PlayerSpawn` and log a **warning** (not error — P9) | Safety net; should never fire |

## Look
| Param | Value | Note |
|---|---|---|
| Input | `Mouse.current.delta` via an Input Action (`Look`, Value/Vector2) | |
| `lookSensitivity` | 0.1 °/pixel | Do **not** multiply mouse delta by `deltaTime` |
| Yaw | Rotates player root | |
| Pitch | Rotates camera child | |
| `pitchClamp` | -85° to +85° | |
| Cursor | `Cursor.lockState = Locked`, `visible = false` on Start | |
| Camera local pos | (0, 1.65, 0) | Eye height |
| Camera | FOV 70, near 0.1, far 1000 | Far plane covers a 500 m+ terrain |

## Input bindings
`Move`: WASD (2D composite) + left stick. `Look`: mouse delta + right stick (stick sensitivity 120 °/s, scaled by `deltaTime`). Tests drive `Keyboard W` and `Mouse delta` through `InputTestFixture`.

## Spawn
- Player spawns at the `PlayerSpawn` transform, position y = `Terrain.SampleHeight(spawn) + terrain.y + 0.5 m`, rotation = `PlayerSpawn` rotation.
- Falls 0.5 m (≈0.22 s at -20 m/s²), so grounded well before the 3 s check and before test (b) starts its timer.

# Spawn area guidance (for level)

Test (b) holds W for 2 s along the spawn's forward; trees and rocks with colliders would block it.

| Rule | Value |
|---|---|
| Name | Empty GameObject `PlayerSpawn` at scene root |
| Location | Terrain-local (x, z) ≈ 40% / 40% of terrain size (e.g. (200, 200) on 500 m) — ≥ 50 m from any edge |
| Facing | Rotation y = 45° (toward terrain centre, toward a view of the hills) |
| Flat zone | Radius 25 m around spawn: slope ≤ 10°, height range ≤ 1.5 m |
| Blend | Smooth falloff from 25 m to 40 m into the hill noise; hills providing the ≥ 20 m Δheight live outside 40 m |
| Clear lane | No tree/rock/bush instances within 20 m of spawn, and none in a 6 m-wide strip 30 m long along spawn forward |
| Grass | Allowed everywhere incl. spawn (detail meshes have no colliders) |
| Water / holes | None within 40 m |
| Rocks/bushes | Place them as **terrain tree prototypes**, prefab names keeping the MegaKit FBX name (needed for qa's P4 check) |

Level should verify after placement with an eval: count `treeInstances` within 20 m of `PlayerSpawn` (world position = `inst.position * size + terrain.position`) — must be 0.
