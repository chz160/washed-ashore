---
type: world
status: active
tech: []
area: [lookout-tower]
issues: [7, 8]
prs: []
source: "_bmad-output/poc/tower-restart-rulings.md sections 1b and 1c (= studio:decisions decision/lookout-tower-restart-carry-over)"
created: 2026-10-30
updated: 2026-10-30
aliases: [fire lookout, the tower]
---
# Lookout tower

_A steel fire-lookout tower on Central Ridge Summit. It is built 1:1 from drawings and shown in its weathered 2012 condition. The player climbs it by walking, for the view from the cab and the firefinder inside._

## In the game
- **Structure:** 86'3" to the cab floor, 8 flights, 7 landings, 115 risers (9 in each), stairs 2'0" wide. Placed on Central Ridge Summit through the terrain pipeline's projection.
- **Condition (2012):** weathered white paint and rust streaks. About 30% of cab panes are missing. Grey, rotting plank landings that are still passable. A bent mast and a bent Yagi antenna, and no working electronics.
- **Shutters and roof:** per the drawing. Four shutters, open 72 degrees, static (Noah).
- **The view:** fog is not thinned, open water counts for the view, and the far limit is 1.15 km (Noah).
- **In the cab:** a trap door (hatch) in the floor, window sashes, and a firefinder whose bearing is in the world, not on the HUD.

## Design intent
The rulings below are quoted from the director's carry-over text.
- **The climb:**
  - "W only, no jump needed".
  - "no teleport, fade, cutscene or input lock between ground and cab".
  - "every landing needs a turn the player makes; nothing rotates the camera for them".
  - "releasing W stops the player; they hold on a flight with no slide or creep".
- **The camera:**
  - "no sway, bob, FOV change, vignette or roll; no per-tread jolting of the eye".
  - "looking down through the plank gaps is wanted; vertigo comes from geometry only".
- **Interactions:** "no outline, glow or icon on interactables; nothing opens or closes by itself". The HUD is one prompt line, shown only while aiming at an interactable.
- **What isn't there:** no in-world text naming Burns, Bell, Montgomery Bell, Dickson or NHLR. No HQ, shed, car, power line or keeper's cabin, and no emissive material.
- **Departures from the drawings:** "the drawing wins". Every departure is named to Noah, and none is hidden to pass a check.

Several of the tower brief's open questions are recorded with their defaults in rulings section 3. Two examples: steel or wood treads, and the 56.3° top flight. Whether Noah has answered them is not recorded here.

## Current state
Walkable, with hatch, sashes and firefinder working: [[lookout-tower-traversal-and-interactions]].

The climb is about 25 s up, faster than the brief's 60-120 s. Noah ruled the current speed final on 2026-10-14 (#8).

## Related
[[0001-guided-path-climb-dropped]], [[0007-nine-tower-gate-items-deferred]]
