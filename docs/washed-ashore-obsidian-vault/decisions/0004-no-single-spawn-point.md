---
type: decision
status: accepted
tech: [probe-tests]
area: [player]
issues: [19]
prs: [65]
source: "PR #65 body; pod86-brief.md 'Hard rules (Noah's standing rules)'; studio:decisions gate/playmode-spawn-19"
decided_by: "Noah (standing rule); game-director gate for #19"
created: 2026-10-30
updated: 2026-10-30
supersedes: ""
superseded_by: ""
---
# 0004 No single spawn point; tests choose their own start

_The finished game won't start every player at one fixed point. The scene's current start at the tower base (`TowerBaseSpawn`) is a dev convenience. Tests place the player themselves and never rely on where the scene starts._

## Context
PR #7 moved the scene's start to `TowerBaseSpawn`, about 1,030 m from `PlayerSpawn`, where wildlife, birds and walk lanes are laid out. Twenty PlayMode tests broke (#19).

## Decision
PR #65:

> **The scene's dev start (`TowerBaseSpawn`) is unchanged.** The final game won't have a single spawn point, so tests no longer depend on one.

Noah's standing rules in the #86 pod brief:

> Don't change the game's spawn code or spawn placement (there will be no single spawn point).

The director's gate for #19 (`gate/playmode-spawn-19`, 2026-10-25) kept the dev start in place:

> C1 Tower-first opening stays. The game starts at TowerBaseSpawn. No change to the World.unity spawnPoint, TowerBaseSpawn or PlayerSpawn transforms, or any other hand-placed scene value.

> C8 Every test that depends on where the player starts calls TestSpawn.

## Consequences
- Good, because tests say where they start, through `TestSpawn` and `PlayerController.SpawnAt` ([[probe-and-playmode-test-gotchas]]).
- Bad, because the scene's dev start is about 1 km from where land wildlife and birds are placed. A fresh session there has almost no sightings: A6 measured 0.0-7.5% (gate C7). #15 and #16 fix it by placing wildlife around wherever the player is.
- Docs and issues should say "the scene's current dev start", not "where the game starts".
- What will replace the single start is not designed or recorded yet.

## Related
[[player-controller]]
