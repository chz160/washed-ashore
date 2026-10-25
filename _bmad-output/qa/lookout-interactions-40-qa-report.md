# QA report: GitHub #40 wire the tower hatch, sashes and firefinder

qa-lead, 2026-10-25. Branch `lookout-interactions-40` (cut at `main` f98d029, #45 merged), uncommitted tree as landed. Plan: `_bmad-output/qa/lookout-interactions-40-qa-plan.md`. Results: `TestResults/lookout-interactions-40/{before,after,after-rerun,after-rerun2,after-rerun3,after-probes2}/`. No commit, push or PR was made by qa.

## Verdict: PASS WITH NAMED FOLLOW-UPS (final; F2 closed by the director, amend8b)

Every acceptance row I could measure passes on the final tree. The director's amend8 made one look check gate the close: F2, whether the body presses into the wall or sill below the W and N windows. The director looked at the stills and closed F2 with no action (studio:decisions gate/lookout-interactions-40-amend8b-f2-closed); no look item is open on #40. Nothing is a release blocker.

## What ran (all in the producer's slot, one Unity process at a time, no kills, no player launch, no commit)

| Run | Start to end (CDT, 2026-10-24/25) | Result |
|---|---|---|
| BEFORE EditMode (f98d029) | 22:19 to 22:20 | 436 cases, 415 pass, 21 fail (the 21 retired below) |
| BEFORE PlayMode full | 22:20:09 to 23:06:17 | 254 cases, 199 pass, 44 fail, 11 skip; noise rerun B2 23:08 to 23:13 |
| AFTER EditMode full | 03:16:18 to 03:17:46 | 420 cases, 419 pass, 0 fail, 1 skip (the retired ClimbRules case) |
| AFTER PlayMode full | 03:18:02 to 04:02:54 | 264 cases, 213 pass, 24 fail, 27 skip |
| Rerun 1 (Lookout + TowerWalk) | 04:10:58 to 04:12:30 | 44 cases, 26 pass, 2 fail (F, Sash B: both ruled test faults by td) |
| Rerun 2 | 04:20:58 to 04:22:28 | 45 cases, 28 pass, 1 fail (F opening half: test fault) |
| Rerun 3 (final tests) | 04:27:22 to 04:28:50 | 45 cases, 29 pass, 0 fail, 16 skip |
| Probe slot 1 | 04:06:16 to 04:07:00 | still passed; sash probe v1 NOT GRADED (td part 77) |
| Probe slot 2 (final) | 04:36:46 to 04:37:23 | both passed (sash probe v3 8e53301b, Box 1 test 350dff5c) |

Landed hashes were checked in the chain log before every run (16 files, prefixes: TrapDoor e1ee973e, HatchWalk abcf5e33, HatchWalk.Stop ac9a5e53, Sash 50bcc7c0, Cab cc3d0fee, Builder 71f28b75, Setup 916cba7a, Manifest dc320be5, prefab 60a23450, scene 7b32b5c8, TowerWalk b4146aa2, asmdef 6dce2122, Fixture fd161f76, Firefinder 150f305e, Tuning .cs ca977576 and .asset 56b58793). The engineer's message gave Tuning.cs as ca977595; the tree and the engineer's own sha256.txt both say ca977576, so I read it as a typo.

## Automatic FAILs: none

- `ClimbArea`: zero references to its script GUID (56e7a6dd...) in `LookoutTower.prefab` and `World.unity`. The builder text states there is no ClimbArea. `TrapDoor` still declares the old `climbArea` field; it is not wired.
- Hand-placed values: T1 below. level-40 states in writing that nothing was set by hand.
- HUD: no new Canvas, TMP, OnGUI or UIDocument in the diff. The scene holds exactly one `InteractionPrompt` and one `Interactor` (script GUID counts 1 and 1).

## Criteria

| Row | Result | Evidence |
|---|---|---|
| T1 builder, no hand placement, twice = same | PASS | level-40's run (23:34 to 23:36, `stage-40-level/runs`): after build 1 and build 2 the prefab is 60a23450... and the scene c869c3dc... byte-identical; after the player apply step the scene is 7b32b5c8..., equal to the committed bytes. Both hashes in the tree now match. I did not re-run the builder myself. |
| T2 Interactor and prompt on the player, saved in the scene | PASS | scene GUID counts above; the 24 fixtures no longer fail with "No Interactor"; LookoutInteractionTests A to D pass |
| T3 hatch: starts closed, Open on E, Close from the cab, closed holds like a floor above and below | PASS | HatchWalk A (floor from above: worst y error 0.01 mm, penetration 0.000 mm), B (stops at the headroom stop, eye to leaf 0.187 m), C (aim hits the leaf, prompt "E - Open hatch", E opens it), D (open: PassEyeHeight 1.6500, up clearance 0.2358 m, down 0.2567 m), E (Close refused across the stop band, allowed below it, open from the stop moves nothing: 21 refused, 11 allowed, worst move 0.00 mm), E2 (16 frames asserted), E3 (1 mm sweep: gap -20 to -1 mm interactable, +1 to +20 mm not, the 0 mm boundary row not asserted), F (closing: held 78 frames, capsule top 41.6 mm under the stop face, 0 pushes; opening half passes after the engineer's 0.5 s settle) |
| T4 four sashes: prompt, open, close, body clear of the frame | PASS | Cab C; Sash A (outward sweeps blocked at all four windows by CabWall/CabBand at 0.126 m); Sash B (W+Shift, jump, yaw sweep at all four windows: starts clear, reach -0.071 m, lowest feet 117.290 m on a 117.240 m floor); T4.3 body clip by probe v3: A, B and C = 0 in all 32 rows (below) |
| T5 firefinder | PASS | Cab B (enter, turn 90 degrees, read, leave), Q4 (body and shadow off during the view and restored exactly), tuning tests (eye = cab floor + 1.22 m, back 0.185 m), the director's amend6 still (eye 1.2198 m above the floor, level, FOV 45, 0 body pixels), amend6a (the grey triangles are LOD0 sash rails, E fixed and S hinged, plus scenery) |
| T6 Lookout suites brought up to date | PASS | 21 EditMode reds retired by name (below); 16 PlayMode guided-path fixtures skipped with reasons; the 8 other Lookout PlayMode fixtures (Cab A to D, Interaction A to D) pass; no new red |
| F2 body vs the sill and wall below W and N (director amend8) | PASS | `SashSillStills` stills, A vs A2 = 0 everywhere; the director looked and closed it with no action (amend8b) |
| T7 tower walking unchanged | PASS | TowerWalk UP 26.6 s in the full run (26.8 s in rerun 1, 26.6 s in rerun 3; bar 23.94 to 29.26 s), DOWN 24.5 s, 0 of 31550 frames airborne; the hatch is posed Open before the timer starts (angle 91.15 at scene time 6281.49 s); no diff to PlayerController, LookoutRules or TowerWalkZones |

### Spec numbers qa printed

- Hatch open angle: tuning 91.1518 (was 92), equals the manifest within 0.001 (EditMode test passes).
- Headroom stop: depth 0.2000 m (at most 0.25 m), underside 117.1890, stop lower face 116.9890; band starts at feet y 115.3390 (capsule 1.600 m, skin 0.050).
- Eye to leaf at the stop: 0.187 m. Per director amend4a this is a PASS (0.150 m or more). td parts 28, 29, 66 and 68 explain the figure as 0.150 plus the 0.031 skin and 0.006 leaf collider offset, not slope.
- Duck: PassEyeHeight 1.6500 (derived 1.9014, clamped to [1.000, 1.6500]); duck run 0.600; eye min and max 1.6500 on both legs.
- Cab render probes: 20 made; excluded as a later LOD: MC39_Impostor, MC39_LOD1, MC39_LOD2. Cab walk: 977 frames, 0 escapes, 0 launches, 0 camera breaks.
- Box 1 (director amend6b): at all nine pixels the only collider behind the camera is the Player, then CLIP_BOX_CabBand_03 and COL_BOX_SashSHinged_01 (1.03 to 1.09 m); the only renderer ahead is LOD0 MC39_Sash_S_Hinged. No birds, particles or terrain. isVisible reads false for every renderer in the batch run, so the visibility column is empty. Cause per td part 94 and director amend8a: the remaining glazing of the south hinged sash (MC39_Sash_S_Hinged, LOD0; 5 of its 9 panes are missing per the manifest) seen against the sky through the missing panes. Look backlog F1, not a #40 defect.

### T6: retirements by name

EditMode (21): `LookoutBuiltTowerTests` 16 cases (3 files deleted) and `LookoutEyeClearanceTests` 4 cases (1 file deleted), with the manifest reader change (td R4); `ClimbRulesTests.LevelSpeed_IsWalkSpeed_RampedToStairSpeedWhereTheLevelMeetsTheIncline` skipped (guided path, td part 14; BEFORE value 4.073596 against 4.04). Exactly those 21; no other EditMode test is gone or skipped.
PlayMode (16, each skipped with a stated reason, all guided-path): `LookoutClimbTests` A, C, E, F, G, I; `LookoutHatchTests` A, B, E (replaced by `LookoutHatchWalkTests`); `LookoutStressTests` A(11), A(22), A(33), A2, B, C; `LookoutTwinTests.ShiftedLanes_...`. `RealTowerWiring.cs` is deleted; the `LookoutMovers` cases now read the real tower and all pass (9 cases in the reruns; 8 before).

### Non-Lookout tests (td part 25)

19 reds, the same names and clauses as BEFORE. Only numbers move: `WildlifeFrameTimeTests` 0.67 ms with wildlife vs 0.47 without (BEFORE 0.72 vs 0.49), name and clause only per add3; `WildlifeSightingTests.A6(202)` 8.3 (BEFORE 8.1; band [6.9, 8.7]); A6(101) and A6(303) exact. `BirdFrameTimeTests` B8 (the BEFORE flake) passed. The #38 refusal tests `SlimmedCapsule_EndsAndBlocksLoweredStances`, `EnteringTheTowerCrouched_NeverLeaksTheCrouchCapsule` and `SitAndSleep_RefuseBadGround` pass, unchanged. The `TowerMeshTable` collision-mesh sha256 is equal before and after (f72d4746...867e; the prefab and scene changed on purpose, td part 70 approved them).

### #45 carry-overs

The 24 Lookout fixtures: 8 pass, 16 retired. `LookoutMovers` hatch cases run on the real tower with no stand-ins. The swing blocker is on only while a swing runs (`AllHingeLeafColliders_AreOnAtRest_AndOffWhileMoving`). Hatch rest angle equals tuning, 91.1518. The open leaf stays clear of the east wall's outer face (EditMode `HatchOpenPoseTests`: no visible leaf vertex beyond it, max y under the wall top). Layer 12 (Interactable) now holds the hatch leaf, the four hinged-sash proxies and the firefinder proxies. Player x Interactable is ON (td decoded the matrix: Player collides with 0, 9, 12 and 13), `docs/physics-guidelines.md` is updated, and `PhysicsLayersTests` (EditMode: matrix, masks, layer indices) still pass.

### T4.3 body clip (director amend7 and 7a, td parts 77, 79, 91)

SashBodyClipProbe v3 (8e53301b, td-OK'd), 4 windows x 8 poses (closed, open, every 5 degrees), 6 samples per pose, view and shadow body separately. Start: Sash B's proven start, 0.6 m inside, floor-only assert. A (past a standing sash's cab-side face): 0. B (beyond the wall's outer face by more than 1 mm at open poses): 0. C (inside closed frame or mullion meshes, exact parity per collider, self-tested on a closed cube): 0. Glass within 2 mm: 0. Sash boxes and near-plane corners: 0. No row is vacuous (capsule front 0.076 m from the inner face and 0.116 m from the outer plane; player moved at least 0.239 m). qa choices, tagged: the outer face is the largest outward vertex distance of the rendered MC39_LOD0_Cab and MC39_LOD0_Frame meshes within 0.6 m sideways and the window height (+0.0200 m from the window centre); C counts a vertex as inside only when two ray directions both read odd on a mesh that the weld test calls closed.
Limit: only MC39_LOD0_Frame is a closed mesh. MC39_LOD0_Cab and every sash leaf mesh are open (60 to 80 boundary edges), so C cannot cover them. 16 of 32 rows (the N window all 8 poses, 30 to 48 vertices each; the W window all 8 poses, 153 to 167 each) read "parity unreliable" on the open Cab mesh, the same count for view and shadow body. A and B are 0 in those rows. td and the director rule on them (open item 1).

## F2 sill stills (director amend8, td parts 93, 95, 98)

T4.3 PASSES (td part 93, director amend8). F2 asked whether the body presses into the wall or sill below the W and N windows. Test `SashSillStills` (td-OK'd 6013d6d9, temporary, removed after) ran 04:45:04 to 04:45:25 CDT, exit 0. Sash closed; the player starts 0.6 m inside as in the body-clip probe, holds W 2 s and stays held (moved 0.259 m at W, 0.260 m at N); eye 1.650 m above the feet; view camera FOV 70, near 0.1; the scene's sun rotation 48, 325, 0, soft shadows. Each view is rendered as A (as played), A2 (same frame again) and B (every Player-root renderer hidden) at 960x540, in one frame.

| View | A vs B changed px (over 8 levels) | A vs A2 | Where |
|---|---|---|---|
| W first person, 70.4 deg down at the sill | 55,883 (20,285) | 0 | lower frame: the legs, hands and floor shadow |
| N first person | 55,881 (16,116) | 0 | lower frame, same |
| W from outside (stand-in camera 1.5 m out at sill height, aimed 0.5 m under the sill) | 13,145 (8,901) | 0 | top: the player seen through the closed glass; a few px at the window's lower edge |
| N from outside | 13,903 (9,159) | 0 | top, same |

By qa's reading of the stills (`TestResults/lookout-interactions-40/after/sash-sill-*.png`) the body stands on the cab floor and nothing of it or its shadow cuts into the sill or the wall panel below the window, from inside or from outside. td part 98: a non-zero A vs B is not clipping by itself; it shows where the body is. The director looked at the W stills and closed F2 with no action (amend8b): the body stands clear of the wall and sill.

## Follow-ups, named

F2 is not a follow-up any more: it is a PASS (sill stills above, director amend8b). Backlog, not #40: the washed-out land and fog beyond the glass, and the firefinder front-sight hair at 960x540; Noah's L9 hand check closes the H8 cells and his ratings stand as given.

1. Look items that qa cannot judge from numbers and did not measure: T7.6's coplanar-triangle count and T7.7's zero changed pixels from the leaf and the no-flicker pixel test were not implemented as tests; the only leaf-through-the-wall check is the EditMode outer-face and top test. These are Noah's hand check (list below). The firefinder front-sight hair is not visible at 960x540, and the land beyond the glass looks washed out (scene fog): look backlog, not #40.
2. A hinge set straight to another rest state (late joiner or network snap) shows its colliders without the swing blocker: that is #13's, not covered here.
3. E2 itself logs no frames within 10 mm of the band edge; E3's 1 mm predicate sweep carries that coverage (td part 85 accepted it). No physical walking frame lands within 10 mm there.
4. The `TrapDoor.climbArea` field is dead code now; removing it is a cleanup for a later change.
5. The firefinder Box 1 isVisible column is empty in batch runs; if visibility matters again, run a windowed pass.
6. Stand-in-free hatch evidence is in place now (no wiring in the tests); no stand-ins remain, so nothing to carry.

## Noah's hand-check list (L9; these are Noah's pass or fail, not qa's)

1. Hatch, from the cab floor: aim at the closed hatch, the line reads "E - Open hatch"; press E, it swings open (about 1.4 s) with no flicker and the leaf does not show through the east wall; step into the opening and walk down the stairs; walk back up, aim at the open leaf, "E - Close hatch", E closes it.
2. Hatch closed: walk across it from above (it feels like floor, no bump); from below, climb up the last flight and stop under it (your head stops just under the leaf, the view does not clip the leaf); press E there and the hatch opens without stepping back.
3. Each of the four sashes (N, E, S, W): "E - Open window" and "E - Close window"; the pane swings outward; stand and sprint at an open window, jump near it, and confirm you cannot leave the cab and your body or its shadow does not cut the frame.
3a. Look down at the sill by each of the four windows (director amend8), sash closed and open, pressed against it: nothing of your body should cut into the wall or sill, and from outside nothing of you shows through the wall below.
4. Firefinder: "E - Use firefinder", E enters, move the mouse (and a stick if you use one) and the alidade turns, look at the rim numerals and the front sight, E or the leave key exits at normal standing height with your body back. Look for any stray body, shadow or grey slivers in the glass, and judge the washed-out land.
5. Climbing the tower still feels as before (about 27 s up, 24 s down).

## Process notes

Hashes withdrawn and restaged: sash probe 31662cda, 039c3477, 48105117, bff5780f, 8459478f, 8f901990, d32ca452, 05ce41ab (td read some of these while I was editing; I now send "withdrawn" before any edit). Churn after the runs: `MC39_Glass.mat` and `_bmad-output/poc/character-facing-report.json` by git checkout; `ProjectSettings/ProjectSettings.asset` restored by the engineer by landed copy (line endings only). No temporary file remains under `Assets/` (git status 41 filtered lines, the baseline). `studio:qa` keys: `signoff/lookout-interactions-40`, `interim/lookout-interactions-40-state`.
