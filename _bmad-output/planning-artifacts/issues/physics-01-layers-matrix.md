Give every kind of object its own physics layer, and switch off the layer pairs that never interact. Today **every layer collides with every other**, and the player shares `Default` with the world. Rules 16 and 22 in [`docs/physics-guidelines.md`](https://github.com/chz160/washed-ashore/blob/main/docs/physics-guidelines.md).

## What we know (audit 2026-10-20)

- **Layers in `ProjectSettings/TagManager.asset`:** Default, TransparentFX, Ignore Raycast, Water (4), UI, Birds (8), WorldBounds (9). There are **no Player, Wildlife, Interactable, Hitbox, Prop or Ragdoll layers**.
- **`DynamicsManager.asset`'s `m_LayerCollisionMatrix` is all `ff…`:** every pair is on.
- **The player's controller and body are on `Default`.** So the player gets hit by the wildlife and bird sightline casts, which `WildlifeRules.cs:276` acknowledges, and by the robin landing checks. The only defence is filtering out its own colliders (`IsOwn`).
- **Masks are built by name in `WorldLayers.cs`,** which throws if a layer is missing. That's the right place to add new ones.
- **The climb fit test** (`PlayerController.Climb.cs:276`) uses `~controller.excludeLayers`, which takes in WorldBounds and Ignore Raycast. The stance code uses `DefaultRaycastLayers & ~(Water|WorldBounds)`. The two are inconsistent.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| L1 | **New layers:** at least Player, Wildlife, Interactable and Prop, with Hitbox and Ragdoll reserved. Each has a named mask in `WorldLayers`. The player's controller and body are on Player | TagManager diff; `WorldLayers` diff |
| L2 | **A minimal collision matrix.** Only pairs that gameplay needs are on, and each one is written up in a table in `docs/physics-guidelines.md` | DynamicsManager diff; the table |
| L3 | **Every query uses a named `WorldLayers` mask.** The climb `~excludeLayers` mask is replaced, and a grep of `Assets/Scripts` and `Assets/Editor` finds no `~0` and no `~excludeLayers`. Five test lines keep `~0` on purpose, as "is anything there" probes (director, `gate/physics-layers-43-amend1-l3`): `QaLanding6StillsTests:183`, `TowerWalkTests:153`, `WaterSwimAroundTests:289`, `WorldBoundsSweepTests.Support:89` and `:111`. Any other `~0` in tests needs its own ruling | grep output |
| L4 | **No behaviour change.** EditMode and PlayMode suites pass (apart from the known #19 failures), TowerWalkTests up time is 26.6 s ±10%, and wildlife, birds and fish tests pass | Test XML |
| L5 | **Physics Profiler before and after** at the perf protocol spots: Broadphase and Narrowphase counts don't go up | Profiler capture |

## Automatic FAIL

- Changing tuned movement speeds.
- Changing the layers on vendor or ThirdParty assets without writing down why.

## Pod

**gameplay-engineer** (lead) and **qa-lead**: 2 agents.

Related: #13 (multiplayer hitboxes), #21 (tree collision).
