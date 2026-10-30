---
type: feature
status: in-progress
tech: [stairs-ramps, collider-geometry, character-controller]
area: [lookout-tower, player]
issues: [8, 40, 41, 45, 63, 78, 80, 86, 91]
prs: [57, 79, 85, 92, 93]
source: "PR #57, #79, #85, #92, #93; issues #8, #80, #86 (comments 2026-10-30); studio:qa pod8/final-signoff"
created: 2026-10-30
updated: 2026-10-30
---
# Lookout tower: traversal and interactions

_The player walks up the lookout tower's eight flights to the cab with W only, then opens the hatch and sashes and uses the firefinder. The climb and interactions shipped, and Noah signed off the feel. Collider snags at the flight 8 head and three rail ends are still open._

## Shipped
- **Walkable tower (PR #7, then #85):**
  - Invisible ramps per flight, with `TowerWalkZones` setting speed, slope limit and the slim tower capsule.
  - Ramp step offset 0.25 (#41 fix) and the fixed 60 Hz tick together removed the rail stalls.
  - See [[stairs-and-ramps-with-a-charactercontroller]].
- **Climb speed is final** (#8: "Climb speed is final (Noah's ruling, 2026-10-14)"). Stair 1.15 m/s, landing 1.5 m/s. Measured up/down 24.6/23.9 s on PR #85 and later. The test bar is 20-40 s; the older 60-120 s spec bar is superseded.
- **Interactions (PR #57):**
  - Hatch: closed, it is a floor from above, and a 0.20 m headroom stop holds the head under it from below. It won't close while the player stands in the opening.
  - Sashes, and the firefinder (enter, rotate, exit; the body is hidden in the view).
  - One prompt line, no other HUD.
  - Everything is set by the builder (`WalkableTowerBuilder`, `LookoutGameplaySetup`), and a rebuild is byte-identical.
- **Moving leaves (PR #56):** colliders switch off while swinging. Tower mesh collision is prebaked. See [[moving-colliders]].
- **Guided-path code removed (PR #79):** see [[0001-guided-path-climb-dropped]].
- **Feel round #8 (PR #92):** measuring tests added. Noah walked the build: "climb feels right" (S8 pass, `pod8/final-signoff`). Noah accepted the S1 view-bump exception (rubR-144 turn 6, 10.5 mm against 10 mm).
- **Collider cleanup (PR #93):**
  - Cab floor rim trimmed 0.05 m, which fixed the S2 dir225 drop.
  - Landing 6 walls moved 0.063 m outward, which fixed TowerStep rows 76 and 94.
  - S4 row 30 now starts on the lane (#91).
  - The S1 worst turn went from 10.5 mm to 8.1 mm. Noah signed off the feel.

## Open
- **#80, the flight 8 head (from the #64 probe):**
  - S4 row 29 (F8 up R30) is stuck in every configuration tried. The untried fix is new geometry, which needs Noah.
  - S4 row 25 and row 32 read 0.319 against the 0.80 bar.
  - TowerStep rows 101/102 are stuck.
  - Top-of-flight end stops: rows 63, 77, 81, 91, 95, 99.
- **#86 part 1:** rail ends on flights 2, 3 and 6 read 0.0301 / 0.0300 / 0.0304 against a 0.0300 bar. Every change tried made another row worse. See [[collider-geometry-for-a-steered-capsule]].
- **#78:** Lookout test start poses are pushed 0.12-0.15 m. Test-only, low priority.
- **#81:** the probe's known limits before it is used as an acceptance test.
- **The hatch duck lowers nothing at today's size.** The eye clears the leaf by 0.187 m (floor 0.15). If the top flight, cab floor or hatch opening changes, walk under the open hatch again and check the view doesn't bob (#8 comment 2026-10-25).
- **Late joiners:** a late joiner may see hatch colliders without the swing blocker (#13).

## Rulings
- [[0001-guided-path-climb-dropped]]
- [[0006-charactercontroller-kept]]
- [[0007-nine-tower-gate-items-deferred]]
- [[0008-dir315-hatch-fall-is-a-normal-fall]] (also row 29 stays open, no new geometry)
- Rulings that carried over from the first attempt: [tower-restart-rulings](https://github.com/chz160/washed-ashore/blob/main/_bmad-output/poc/tower-restart-rulings.md)

## Patterns
[[stairs-and-ramps-with-a-charactercontroller]], [[collider-geometry-for-a-steered-capsule]], [[probe-and-playmode-test-gotchas]], [[moving-colliders]]

The place itself: [[lookout-tower]].
