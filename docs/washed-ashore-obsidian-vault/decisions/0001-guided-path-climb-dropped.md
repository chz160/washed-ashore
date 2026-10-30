---
type: decision
status: accepted
tech: [stairs-ramps]
area: [lookout-tower]
issues: [7, 8, 63]
prs: [79]
source: "_bmad-output/poc/tower-restart-rulings.md section 2 (= studio:decisions decision/lookout-tower-restart-carry-over); issue #63; PR #79"
decided_by: "game-director, on Noah's delegation"
created: 2026-10-30
updated: 2026-10-30
supersedes: ""
superseded_by: ""
---
# 0001 Guided-path climb dropped

_The lookout tower is walked with ordinary colliders and the normal player controller. The guided path (stair lanes, hand-overs, layout search) and everything built to serve it is dropped. Its code was deleted in PR #79._

## Context
The first tower attempt guided the player along stair lanes. A ten-agent pod ran a whole-tower layout search, and it closed as a spec FAIL with the stair-landing blocker three times (#8; [tower-restart-rulings](https://github.com/chz160/washed-ashore/blob/main/_bmad-output/poc/tower-restart-rulings.md)). The restart (PR #7, commit 99a9d73) put the script-generated tower in the game with invisible ramps instead.

## Options considered
- Keep fixing the guided path.
- Restart with ordinary colliders, ramps and the normal controller.

## Decision
Game director, 13 Oct 2026, on Noah's delegation (`tower-restart-rulings.md`, "Text below is the director's, unedited"):

> The restart puts the script-generated tower into the game with ordinary colliders and the normal player controller.

Section 2, "Dropped as a class":

> Everything that exists to serve the guided path: lanes, hand-overs, run-outs, layout search, mouth gates, named overlaps, and every figure, bar and check row derived from them.

The #8 session prompt keeps it standing: "Start from the invisible ramps, `TowerWalkZones` and `TowerWalkTests`; don't bring back the guided path." #63 lists "Bringing back any guided-path behaviour" as an automatic FAIL.

## Consequences
- Good, because PR #79 deleted `Assets/Scripts/Gameplay/Climb/` and its tests before #46. The fixed tick then had no stair-lane code to port.
- Good, because tower snags are now ordinary controller and collider problems, measurable with one probe ([[stairs-and-ramps-with-a-charactercontroller]]).
- Bad, because the tower relies on collider shape and step offset for feel. Snags have to be found and fixed one by one ([[collider-geometry-for-a-steered-capsule]]).
- Rulings tied to the tower itself, the brief or Noah's answers carried over (section 1 of the rulings file). Nine items were left to re-decide on the walkable tower: [[0007-nine-tower-gate-items-deferred]].

## Related
[[lookout-tower-traversal-and-interactions]], [[lookout-tower]]
