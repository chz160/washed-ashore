---
type: feature
status: in-progress
tech: [character-controller, collider-geometry, physics]
area: [terrain, player]
issues: [21]
prs: []
source: "issue #21; pod tasks #12-#21 (studio task list, 2026-11-11); qa-lead's #21 verdict message; docs/design/bush-slowdown.md"
created: 2026-11-11
updated: 2026-11-11
---
# Tree collision and bush slowdown

_Bells Bend trees block the player again, and walking through shrub cover slows you by cover type. Both passed QA on 2026-11-11 on branch `trees-bushes-21`; status stays in-progress until the work is committed and a person has playtested it._

## Shipped (working tree, not committed)

**Tree colliders.** The LOD prefabs built by `BellsBendTreeLods.Ensure` had no collider (cause: issue #21 text). Now each of the 11 prefabs in `Assets/World/BellsBend/Art/TreeLods/` has one `CapsuleCollider`.
- Radius is the measured trunk (median distance of LOD0 vertices from their centroid in the first usable height band); height and Y centre come from the kit capsule. Per-prototype values are in task #14's table; raw bands in `TestResults/21-trunk-measure.txt`. Not copied here.
- Three prototypes are fitted by hand-entered `TrunkFit` entries: TwistedTree_1 (radius 0.95, centre x -0.3), DeadTree_1 and DeadTree_2 (centre shifted, radius kept). See [[0024-tree-collider-sized-to-trunk]].
- Rebuilt by script (`BellsBendTreeLods.BuildCli`, `TestResults/21-tree-rebuild.log`, `21-dead-rebuild.log`), not by hand.
- Tests: EditMode `TreeColliderTests` 34/34 (`TestResults/21-l2-after-editmode.xml`); PlayMode `TreeCollisionTests` 3/3, all 11 prototypes blocked from 4 sides at walk and sprint, field-cedar scaling follows the instance, a 1.20 m gap passes and 0.50 m blocks (`TestResults/21-bush-tree-playmode-final4.xml`, 10/10 with the bush tests).

**Bush slowdown.** Only the `shrub = true` detail layers slow the player (Briar, ShrubThicket, KudzuMound, KudzuLeaf). Design and numbers: `docs/design/bush-slowdown.md`, ruled in [[0023-bush-slowdown-design]].
- Code: `ApplyBushSlowdown(MoveTarget(...), sprint)` at `PlayerController.cs:209` (one-line diff); logic in `Assets/Scripts/Gameplay/Bush/`. It is a data lookup inside the fixed tick, with no collider, so bushes cannot block or snag ([[fixed-tick-player-sim]]).
- Multipliers live in `Assets/Settings/BushSlowdownTuning.asset`, linked to `Player` > `PlayerController.bushTuning` in `Assets/Scenes/World.unity` through the Editor API (task #20).
- Stacking: crouch and bush combine by minimum, so crouch stays 1.75. Tower caps (1.15, 1.5) apply after, in the existing clamp. Outside bushes the multiplier is exactly 1.0 and walk 5 / sprint 8 are unchanged (B2 test, target bit-equal to unmodified code).
- Tests: EditMode `BushSlowdownRulesTests` 11/11 (`21-bush-editmode-final.xml`); PlayMode `BushSlowdownTests` 7/7 (B1 walk and sprint tables for all 8 layers, crouch, exit, no-snag in ShrubThicket and KudzuMound).

**Regression (task #17, qa-lead PASS).** Tower 7/8 vs 7/8 (the same failing `KF_S1_OneFrameBump_144fps` in baseline and after), water 13/13, wildlife 20/20, spawn 8/8, stance 65/65 (no baseline), FPS 6.77 ms vs 7.00 ms baseline (-3.3%, `TestResults/21-after-fps.json`). One test was changed: `WaterSwimTests.WalkIn_Swim50m_AndBack` now samples dry speed only where `LastBushMultiplier == 1f`, because a Briar cell on its approach pulled the dry median to 4.77.

## Open
- **Known limits (qa-lead):** unfitted tree capsules stand out 0.24 to 0.40 m beyond the trunk in the player's band; TwistedTree_1's root flare clips up to 0.71 m; the stand-out test measures axis-aligned extents only.
- **All bush multipliers are assumed, not playtested.** Retune in the asset; the tests read it.
- Shore walks can be slowed (the water test approach starts under a Briar cell).
- Cover may step by up to one cell where two terrain tiles meet (spec section 10, assumed acceptable).
- Feedback (rustle audio, camera brushing) is recommended, not built. Whether rustle may be audible to wildlife or players touches the "scarcity and risk" pillar and is an open question for the game director, not decided.
- Not run: macOS and Linux; stance baseline; a visual playtest of the trees (Studio Head). Whether the shipped build had the tuning asset linked was not checked (QA, task #17).
- Follow-up (not #21): `BellsBendTreeLods.Capture` adds a WebGL block to the impostor `.png.meta` files on a full rebuild; restore them with `git checkout --` after each rebuild.
- Proposal, not applied: [[player-controller]] could gain a line on the bush speed multiplier and crouch stacking. It has uncommitted edits from another change, so it was left alone.

## Rulings
[[0023-bush-slowdown-design]], [[0024-tree-collider-sized-to-trunk]].

## Patterns
[[physics-layers-and-collision-masks]], [[collider-geometry-for-a-steered-capsule]], [[fixed-tick-player-sim]].
