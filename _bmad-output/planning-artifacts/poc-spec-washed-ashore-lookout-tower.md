---
title: 'POC Spec: Burns (Bell) Lookout Tower — Blender model, placement, climbable and interactive'
status: ready-for-pod
owner: John (PM)
created: '2026-10-10'
inputs:
  - _bmad-output/research/domain-burns-lookout-tower-2026-10-10/research.md   # + digests/ and images/
  - CLAUDE.md
---

# POC Spec: The lookout tower

**The question this answers:** can Ruflo's agents build a faithful, game-ready model of the Burns (Bell) International Derrick MC-39 fire lookout **in Blender, entirely by script**, stand it on the highest point of Bells Bend, and let the player climb the stairs into the cab and interact with it?

## Fixed inputs (decided)

| Item | Value |
|---|---|
| Reference | `research.md` dimensions (the MC-39 standard drawings), plus the Burns photos for its specific look. **The photos are reference only:** don't ship them or trace their textures |
| Height | **79'6" (24.2 m) by default.** technical-artist counts the flights in `burns-nhlr-lutes-main.jpg` and the YouTube climb. If the count matches the 86'3" standard (NGS says "about 90 ft"), use that. **The script takes height as a parameter**, either way |
| Scale | **Real size (1:1),** not the map's 0.5/0.7 squeeze. Structures stay human-scale (README) |
| Location | **Central Ridge Summit, 36.1655 N, -86.9065 W** (244 m, the highest point in the bend). It's converted to game coordinates by the **terrain pipeline's own projection** (`tools/terrain`), not computed by hand. Add the site to `landmarks.json` as `LookoutTower` |
| Era and condition | **2012, 25 years after the collapse,** last staffed in the mid-1970s. Weathered white paint with rust streaks, about 30% of cab panes missing, grey rotting plank landings (still passable), and a bent roof mast and Yagi antenna. No working electronics |
| Build route | `tools/blender/lookout/build_mc39.py`, run headless with `blender -b` (Blender 5.2.2), builds the model **procedurally from a parameter table** and exports FBX with LODs and collision proxies. There's a wrapper `.ps1` like `export_pigeon.ps1`. **No hand-modelled steps.** Textures are CC0 (ambientCG metal, painted metal and wood) or generated |
| Facing | It's a static mesh, so CLAUDE.md's rigged-FBX facing rule doesn't apply. Still, the export has to come in **+Z forward, Y up, 1 unit = 1 m**, with an EditMode test checking the bounds |
| Out of scope | Fall damage and death (none exist), sound, a keeper's cabin, a usable firefinder sighting system beyond F-I3, multiplayer sync of the doors |

## Pass criteria: the model

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| T1 | **Dimensionally faithful.** In the exported FBX: base square 19'0½" (±5 cm), tapering to 7'0" at the cab, total height to the eaves per the chosen standard (±10 cm). Cab 7'0" square, lower wall 3'6", window band 4'0", roof 8'0" at the eaves, 6:12 pitch. Stairs 2'0" wide, pitch 52° ±2°, flights running diagonally between interior corners with triangular landings | An EditMode measurement test on the imported prefab, read against `research.md` |
| T2 | **Reads as the Burns tower.** Side-by-side renders at the same camera angle as `burns-nhlr-lutes-main.jpg` and `-feb2023.jpg`. Noah's check: "**that's the tower: yes**" | The render pair, plus Noah's verdict |
| T3 | **Reproducible from script.** One clean run of `build_mc39.ps1` regenerates the FBX byte-identically, apart from timestamps. The parameter table (height, pitch, decay amount) is the only input | Run it twice and diff |
| T4 | **Budgets.** LOD0 ≤ 60k tris, ≤ 4 materials, an atlas texture of 2k or less. LOD1 and LOD2 plus a distant impostor or billboard, **because the tower should be visible across the map**. Windows ≥ 60 FPS and web ≥ 30 FPS with the tower in view from 1 km and from inside the cab | Mesh stats; FPS JSON for both builds |

## Pass criteria: placement

| # | Criterion | Evidence |
|---|---|---|
| P1 | **Correct site.** The tower centre is within 10 m (game) of the projected Central Ridge Summit point. All four footings sit on the terrain (gap ≤ 0.1 m on concrete pads). It doesn't float or sink. Terrain changes are limited to a ≤ 1 m footing pad levelled by the **level-build script** | An `eval` of the position and footing gaps; level-build diff |
| P2 | **The site fits the fiction.** Trees are cleared from a ring about 15 m around the base. A short overgrown access track (gravel and grass) runs to the nearest road or track, and is painted through the existing roads pipeline | Screenshot; road-map diff |
| P3 | **The view earns the climb.** From the cab, the shoreline is visible in at least 3 of 4 compass directions, and the north barrier line is visible. The cab is above the surrounding canopy | Raycast visibility test from the cab; 4 compass screenshots |
| P4 | **No regressions.** The NavMesh is rebaked so animals walk around the legs. Bird flocks and robins avoid the tower. Bells Bend L1–L4, water, wildlife, birds, fish and WorldBounds all pass | Full test XML |

## Pass criteria: climbing and interaction

| # | Criterion | Evidence |
|---|---|---|
| C1 | **Climbable with WASD.** The player walks up every flight with W only (no jumping), in about 60–120 s base to cab. They can stop, turn and walk back down. The 52° pitch is steeper than the controller's slope limit of 45°; the pod chooses a stair-volume or a constrained-path solution and documents it | PlayMode: a scripted climb from base to the cab floor, then a full descent |
| C2 | **Can't slip through.** Handrails and landing edges block falling off the stairs. The player can't clip through members, get stuck on landings, or end up inside the steel frame. Jumping on the stairs doesn't launch the player out | PlayMode: a stress test of strafing, jumping and sprinting on each flight, with 0 escapes or stuck states |
| C3 | **The cab is walkable** (7 ft square, firefinder pedestal in the centre). The player can stand at every window and look out, and the camera doesn't clip through the walls or roof | PlayMode: a walk around the cab perimeter; camera clipping check |
| I1 | **A minimal interaction system,** reusable later. Aim and press **E** (gamepad: the South button) through the Input System, with a raycast of 2.5 m or less. The only UI is **a single one-line prompt** ("E – Open hatch"), shown only while aiming at an interactable | EditMode and PlayMode tests; screenshot |
| I2 | **The trap door.** It opens and closes with a smooth animation. When closed, the player can stand on it. When open, it's the way into the cab. It doesn't trap or launch the player | PlayMode: open, climb in, close, stand on it, open, climb down |
| I3 | **The firefinder.** Using it locks the view to the sight with a mild zoom, Q and E (or mouse) rotate the bearing, the bearing shows as a world-space number on the brass ring (no HUD), and Esc or E exits | PlayMode: enter, rotate 90°, check the bearing, exit |
| I4 | **The window sashes.** The hinged sash on each side opens and closes. Missing-pane decay stays consistent whether the sash is open or closed | PlayMode |
| I5 | **Noah's feel check.** Noah does one full climb and rates the climb, the view and the interactions as **right / not right**. All three must be "right". 2 retunes are allowed | Ratings in the QA report |

## Automatic FAIL

- Hand-made Blender or Unity steps that aren't in a script.
- Shipping or tracing the reference photos.
- A tower that's off-site or floating.
- Being unable to climb to the cab, or escaping through the stairs.
- HUD elements beyond the single prompt line.
- Breaking the web build.
- The same blocker 3 times in a row, or more than 3 assists.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`.

| Role | Owns |
|---|---|
| technical-artist | Blender script, the flight count and height decision, materials and decay, LODs, impostor, budgets (T1–T4) |
| level-designer | Site projection, the footing pad, tree clearing, access track, the view test (P1–P3) |
| gameplay-engineer | Stair traversal, the interaction system, trap door, firefinder, sashes, tests (C1–C3, I1–I4) |
| platform-tools-engineer | `build_mc39.ps1` wrapper, the reproducibility check (T3) |
| qa-lead | Verdict, Noah's checks (T2, I5), regression (P4) |
| game-director | Approves the post-collapse look and the fiction of a relocated tower |
| executive-producer | Schedule, assist count, final report |
