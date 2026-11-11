---
type: decision
status: accepted
tech: [character-controller, physics]
area: [terrain, player]
issues: [21]
prs: []
source: "docs/design/bush-slowdown.md; task #13 (QA: PASS, gameplay-engineer ACK 19:44)"
decided_by: "systems-designer, under the designer authority issue #21 grants; multipliers assumed, not playtested"
created: 2026-11-11
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0023 Bush slowdown design

_Shrub detail layers slow the player by a per-layer multiplier scaled by cover, lowest multiplier wins, crouch combines by minimum. Every number is assumed, not playtested._

## Context
Issue #21 asked that walking through bushes slows you, more for bigger or denser bushes, and left the numbers to the designer. Detail layers cannot carry colliders, so this is a lookup, not physics. Spec: `docs/design/bush-slowdown.md` (task #13).

## Decision
1. Only layers with `shrub = true` slow (Briar, ShrubThicket, KudzuMound, KudzuLeaf). Grass and fern do not.
2. Full-cover walk / sprint multipliers: Briar 0.70 / 0.50, ShrubThicket 0.55 / 0.40, KudzuMound 0.50 / 0.35, KudzuLeaf 0.90 / 0.80.
3. Density scales the effect by a bilinear blend of the four detail cells under the feet. Height scales it between layers: taller layers slow more.
4. When layers overlap, the lowest multiplier wins; they do not multiply.
5. Crouch and bush combine by minimum, so crouch stays 1.75. Tower caps and water modes are unaffected.
6. Feedback recommendation: rustle audio first, then camera brushing. No HUD. Not built in this pod.

Rules the table keeps (spec section 4): sprint is never slower than walk in the same cover; no full-cover walk speed is under 2.5 m/s.

## Consequences
- Good, because multipliers live in `Assets/Settings/BushSlowdownTuning.asset`, so tuning needs no code change and the tests follow the asset ([[tree-collision-and-bush-slowdown]]).
- Bad, because the numbers are guesses until someone plays them; thicket walk 2.75 m/s may feel like a penalty.
- Open, not decided: whether rustle may be audible to wildlife or players. That would make brush noise a risk and touches the "scarcity and risk" pillar and the population work. It goes to the game director.

## Related
Motion model: [[fixed-tick-player-sim]]. Spec and QA lines: tasks #13, #15, #20. Issue: https://github.com/chz160/washed-ashore/issues/21
