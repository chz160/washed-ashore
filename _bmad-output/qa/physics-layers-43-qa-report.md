# QA report: GitHub #43 physics layers and minimal collision matrix

Status: FINAL 2026-10-22 23:07 CDT. Verdict: PASS WITH NAMED FOLLOW-UPS, per the director's ruling studio:decisions `gate/physics-layers-43-amend2-closeout` (23:04 CDT).
Branch physics-layers-43, baseline commit 04259f4, landing uncommitted on the working tree (22 modified + new test files, per `git status`).
Evidence root: `E:\GitHub\washed-ashore\TestResults\physics-layers-43\` (`before\`, `after\`). Grading rules: td addenda 1-23 (studio:engineering `adr/physics-layers-43/addendum1..`).
L5 counts are "Editor PlayMode counts, not player timings". No ms or fps claim about the change comes from Editor timings (td s8).

## Verdict

PASS WITH NAMED FOLLOW-UPS.

- L1, L2, L3, L5: PASS. L4: PASS with exactly one narrowing, `BirdFrameTimeTests.BirdsCostAtMostTenPercentFps`, accepted by the director as a pre-existing known failure (B8), non-blocking, covering that one test only. C1: MET, not narrowed. C2-C6: PASS.
- Conditions of the L4 narrowing (director amend2): (a) only this test name and only for #43; any other new red still fails L4 (none exists: the other 42 baseline reds are unchanged); (b) the test and its 10 percent limit are unchanged by #43; (c) B8 carries the three pooled lines, the suite drops 0.0907 (BEFORE) and 0.1030 (AFTER) and the note that the test fails alone on both trees; fixing or re-basing the gate is separate work for td and the producer; (d) no ms or fps claim about #43 comes from these Editor timings; (e) the three pooled lines are quoted verbatim below, and the A2 quiet check was a process snapshot, not a load measurement.
- Named follow-ups (not #43 blockers): (1) B8, the Editor frame-time gate (td and producer); (2) a frozen-animator, pixel-stable still mode so the C1 twin can compare pixels (new test work, td and producer to schedule).

## Acceptance criteria (spec `_bmad-output/planning-artifacts/issues/physics-01-layers-matrix.md`)

| ID | Criterion | Evidence | Result |
|----|-----------|----------|--------|
| L1 | New layers Player, Wildlife, Interactable, Prop, Hitbox, Ragdoll with named masks in `WorldLayers`; controller and body on Player | `git diff ProjectSettings/TagManager.asset`: layers 10-15 empty before, now Player, Wildlife, Interactable, Prop, Hitbox, Ragdoll. EditMode `PhysicsLayersTests.Layers_ExistAtTheRuledIndex`, `Require_ThrowsForAMissingLayer`, `SetLayerRecursive_MovesTheWholeTree` Passed. PlayMode `PlayerLayerTests` x3 Passed (`after/playmode.xml`: controller and every child on Player after load and after body rebuild). `WildlifeLayerTests` seeds 101 and 202 Passed (two cases expanded, td add18) | PASS |
| L2 | Minimal matrix; each pair in a table in `docs/physics-guidelines.md` | `PhysicsLayersTests.Matrix_EqualsTheRuledTable` Passed; `git diff docs/physics-guidelines.md` adds the "Layers and the Layer Collision Matrix (#43)" section; `PhysicsLayersTests.PlayerCollisionMask_IsExactlyThePlayerRowOfTheMatrix` Passed; `LayerAuditTests.PhysicsMatrix_PlayerStillCollidesWithBoundsAndTheWorld` Passed | PASS |
| L3 | Every query uses a named `WorldLayers` mask; no `~0`, no `~excludeLayers` in Assets/Scripts and Editor (five named test-only exemptions, director amend1) | `after/l3-grep.txt` (same pattern family as `before/l3-grep.txt`, which had 8 hits incl. `~controller.excludeLayers.value` at PlayerController.Climb.cs:276, `Physics.DefaultRaycastLayers` at Stance.cs:265, FarViewProbe.cs:330, WorldLayers.cs:24, RobinFlightPlanner.cs:22). AFTER: no `~0` and no `~...excludeLayers` in Scripts/Editor (two "~0" hits are the word "approximately zero" in comments, FishImportCheck.cs:84 and BellsBendTiles.cs:133). Remaining hits are not mask queries: `LayerMask.NameToLayer` lookups (BirdPopulation.cs:72, FarViewProbe.cs:326, WorldLayers.cs:76, WorldBoundsBuilder.cs:142) and `controller.excludeLayers \|= 1 << WorldLayers.Water` (PlayerController.Water.cs:38, a write). Tests: exactly the five exempt lines remain (QaLanding6StillsTests:183, TowerWalkTests:153, WaterSwimAroundTests:289, WorldBoundsSweepTests.Support:89 and :111). `LayerAuditTests` Passed | PASS (for td: FarViewProbe.cs:326 NameToLayer("Water") is unchanged from BEFORE) |
| L4 | No behaviour change: EditMode and PlayMode match baseline; TowerWalk up 26.6 s +-10%; wildlife, birds and fish tests pass | see "Test suites" and "A-B-A" below | PASS, narrowed by the director for `BirdFrameTimeTests.BirdsCostAtMostTenPercentFps` only (B8, pre-existing, amend2) |
| L5 | Broadphase and Narrowphase counts do not go up | see "L5" below | PASS (td add21) |

## Director conditions (studio:decisions `gate/physics-layers-43`, amend1)

| ID | Condition | Evidence | Result |
|----|-----------|----------|--------|
| C1 | First-person look unchanged: camera and sun masks include Player; view body visible; shadow body casts; twin still at Stand +60 vs pre-change; LookDownTests rerun | World.unity camera `m_CullingMask` 4294967295 (Everything, includes layer 10) at lines 49992 and 83740, scene file has no diff. `LookDownTests` AFTER (18 cases in `after/playmode.xml`) pass or skip exactly as BEFORE; no new LookDown red. `PlayerCharacterView`/`CharacterAssembler` changes only assign layers. Still twin: see "C1 stills". Mask proof (td add24/26/27), `after/c1-mask-read.txt` and `after/c1-mask-compare.txt`: enabled camera CameraPivot cullingMask -1 and Sun cullingMask -1 (bit 10 set, maskMissingBit10 = 0); 7 of 7 Player renderers on layer 10 (offLayer10 = 0); B (04259f4, `aba/B-masks-raw.txt`) and AFTER agree on all 7 paths for enabled, activeInHierarchy and shadowCastingMode (Men_Farmer Head/Body/Pants/Feet: ShadowsOnly; Men_Farmer_View Body/Pants/Feet: Off); no path in only one read. There is no separate hat Renderer under the Player root in either read. Main ground: every camera, light and URP renderer mask is all bits, so no layer assignment can change whether any renderer draws or casts a shadow, and the hat is unchanged whichever mesh carries it. Carrier (director amend2a): `Assets/Art/Character/Appearances/Men_Farmer.asset` has only head (line 24, Farmer_Head.fbx), body, legs and feet slots and no hat slot, so `Player/Men_Farmer/Farmer_Head` (ShadowsOnly in both B and AFTER) carries the hat; the slot list is the source, the mesh itself was not opened | MET, not narrowed (director amend2): the mask proof makes the change render-neutral by construction; the pixel stills are within run noise, and C1 never required pixel equality |
| C2 | Robins still treat the player as an obstacle | `RobinFlightPlanner.ObstacleMask` now `WorldLayers.RobinObstacleMask(terrain)` ("the solid world, the terrain's layer and the player"); `PhysicsLayersTests.QueryMasks_LeaveOutThePlayerAndRobinMaskKeepsIt` Passed; bird tests: same reds as BEFORE except the BirdFrameTimeTests timing red (add20) | PASS (bird timing red = B8, director amend2) |
| C3 | No feel-visible pair removed without director sight; Player-Water, swim/wade unchanged | Matrix table in `docs/physics-guidelines.md`; `PhysicsMatrix_PlayerStillCollidesWithBoundsAndTheWorld` Passed; WaterSwim/D5 tests: same results as BEFORE (no new red, none fixed) | PASS (the pair table is td's; qa did not re-derive it) |
| C4 | Queries still ignore the player's own colliders and see props/vegetation (#38 refusals unchanged) | StanceTests, Interactor tests, #38 sit/sleep tests: same results as BEFORE; `PhysicsLayersTests.QueryMasks_HoldTheSolidWorldAndNoWaterOrBoundsExceptThePlayerRow` Passed | PASS |
| C5 | Vendor/ThirdParty assets not re-layered in the asset | `git status --short`: no change under any vendor or ModularCharacters path; layers set at runtime (`CharacterAssembler`, `ApplyPlayerLayer`) | PASS |
| C6 | TowerWalk 26.6 s +-10%, no speed change | `after/playmode.xml` TowerWalkTests.WalkBaseToCabAndBack: UP 26.6 s, DOWN 24.5 s, PASS (BEFORE 26.7 / 24.6). No speed constant in the diff | PASS |

## Test suites (BEFORE commit 04259f4 vs AFTER landed tree)

| Suite | BEFORE | AFTER | Grade |
|-------|--------|-------|-------|
| EditMode | 408 cases, 387 pass, 21 fail (all Lookout) | 416 cases, 395 pass, 21 fail | Same 21 names, same messages (0 changed). +9 new passing (8 PhysicsLayersTests, 1 LayerAuditTests under its new name); 1 BEFORE name absent: `LayerAuditTests.PhysicsMatrix_PlayerLayerStillCollidesWithBoundsAndWater` renamed to `PhysicsMatrix_PlayerStillCollidesWithBoundsAndTheWorld`, graded as the same case, pass to pass (td add19, add1 (D)). Net +8. |
| PlayMode | 233 cases, 180 pass, 43 fail, 10 skip | 239 cases, 184 pass, 44 fail, 11 skip | 42 of 43 BEFORE reds identical in name, clause and every number; the 18 reds in the six noise groups are exact (bands exact, `before/noise-bands.txt`). WildlifeFrameTimeTests red in both, name and clause equal (B 0.69 vs 0.47 ms, A 0.68 vs 0.46 ms; Editor timings, not graded). None fixed. One NEW red: see below. 239 = 233 + 3 PlayerLayerTests + 2 WildlifeLayerTests (seeds 101, 202) + 1 Explicit probe. No PlayMode case renamed or removed. |
| Skipped | 10 | 11 | Extra skip is `PhysicsCountsProbe.PhysicsCounts_AtThePerfSpots`, Skipped, runstate Explicit, reason "#43 L5 physics counts probe" (my probe; not in the tree for BEFORE). The other 10 skips are identical in name and reason. |

New red (not graded by qa; decided by td addendum 20): `WashedAshore.Tests.Performance.BirdFrameTimeTests.BirdsCostAtMostTenPercentFps`, Passed BEFORE, Failed AFTER: "Median 0.72 ms with birds vs 0.64 ms without", limit 0.7133, got 0.7157. `[BirdFps]` lines (before `playmode.xml:1722`, after `playmode.xml:1734`): birds-ON medians 0.685 / 0.684 BEFORE vs 0.716 / 0.715 AFTER, OFF 0.626 vs 0.644, robins 16, flock 15, animators 31 identical. td add20: no mechanism in #43; decided by an A-B-A (landed, 04259f4, landed).

## A-B-A (td addenda 20, 22, 23): BirdFrameTimeTests alone, landed / 04259f4 / landed

Engineer ran the legs and the file swap (raw: `C:/Users/noah/AppData/Local/Temp/claude/C--Tools-ruflo-studio/d30412c9-b209-4b7a-8cb4-af7df6b6cb3b/scratchpad/layers-43-engineer/aba/{A1,B,A2}/` each with playmode.xml, playmode.log, cli.log, cmd.txt; manifest 31 files, `sha256sum -c` all OK; git status after restore identical to `aba/pre-swap-status.txt`). Command line = qa's AFTER PlayMode command (`unity test --mode PlayMode --output ... --timeout 6000 . -- -logFile ...`) with `--filter WashedAshore.Tests.Performance.BirdFrameTimeTests.BirdsCostAtMostTenPercentFps` and `--timeout 720` instead of 6000 (a timeout cannot change a passing result). Pooled lines verbatim:

- A1 (landed, 22:31:07-22:34:16): `{"on_median_ms":0.452,"off_median_ms":0.395,"on_median_fps":2210.0,"off_median_fps":2533.6,"limit_on_median_ms":0.439,"fps_drop":0.1277,"pass":false}`
- B (04259f4, 22:36:21-22:39:38): `{"on_median_ms":0.465,"off_median_ms":0.404,"on_median_fps":2152.4,"off_median_fps":2475.8,"limit_on_median_ms":0.449,"fps_drop":0.1306,"pass":false}`
- A2 (landed, 22:53:56-22:57:11): `{"on_median_ms":0.444,"off_median_ms":0.387,"on_median_fps":2252.3,"off_median_fps":2584.0,"limit_on_median_ms":0.430,"fps_drop":0.1284,"pass":false}`

|A1-A2| = 0.0007. Rule (add20): a #43 cost only if B's drop is below BOTH A drops by more than |A1-A2|. B's drop (0.1306) is higher than both (0.1277, 0.1284): no #43 cost shown. All three fail, so the test fails standalone on the unchanged tree too; in the full suite it passed BEFORE (drop 0.0907) and failed AFTER (0.1030). Pre-existing margin flake that depends on run context: filed as B8 (studio:qa `bug/B8`). The A2 quiet check (`aba/A2-quiet.txt`) was a process snapshot at 22:53:36, not a load measurement: no Unity Editor, no blender.exe (blender-mcp at about 0 CPU), Docker Desktop processes present with cumulative CPU only, so their load during the leg is not shown. No ms or fps claim about #43 comes from these timings (s8).

## L5: Physics counts (probe 25d6019a, sha256 25d6019ac600132e8a1ab3fc7dd3571c30054187ff1c5fdcd11f2d6e2c833315; td OK add7, baseline accepted add9, PASS add21)

- Both runs: `Assets/Tests/PlayMode/Physics/PhysicsCountsProbe.cs` unchanged between BEFORE (2026-10-22 20:05 CDT) and AFTER (22:25 CDT); fixed 1/60 step, 120-frame warm-up, 60-frame settle, 90 sampled frames. probe.xml Passed 1/1 both, `voids.txt` none both, no NARROWPHASE_ZERO line.
- 14 named spots (at 12 distinct places: Route_0 = Spawn, Route_7 = LookoutBase) graded separately, each under add9 and add10. Per-spot table with coordinates and all 6 counters: `after/l5-grading.txt`.
- Result: all 90 graded rows (14 spots + control, x 6 counters: Broadphase Adds/Removes, Adds, Removes, Narrowphase Touches, New, Lost): AFTER mean and max identical to BEFORE. No rise.
- Plain statement (td add21): the six graded counters read 0 at every real spot in both runs (max 0 at all 14 spots, BEFORE and AFTER). L5 is therefore met without the matrix being exercised at any spot. The positive control shows the counters are live (crate lands at frame 32, rests, end speed 0.0000 m/s, Narrowphase Touches max 2), and is not graded against BEFORE.
- Coordinates (add10): `spot,x,y,z` for all 14 spots and the control x match BEFORE exactly at the printed 0.1 m; settle-end positions in `spot-checks.txt` differ by 0.000 m (limit 0.05 m). All spots grounded; y test terrain, Cab authored (same as BEFORE).
- Other live counters (reported, not graded, add9): only Physics Queries means wobble at 11 spots, max and min unchanged (Spawn 0.24 to 0.29, Route_3 0.20 to 0.24, Route_4 0.34 to 0.29); control Dynamic Bodies mean 1.81 to 1.80. Static Colliders and body counts identical. td accepted the wobble as timing.
- No absolute counts are claimed from the probe (Static Colliders alternates 1185/2370 on a 5-in-6 frame cycle, mechanism unproven, add9).
- Void and never cited: `before/VOID-18h42-*` (hash a4782e61: GroundY read one of 20 tiles), `before-probe-v1/`, `before-probe-v2/` (origin unknown).

## C1 stills (Stand +60 idle reference, 16x9 and 21x9)

- BEFORE: `before/stills/` (from `TestResults/player-character/runs/pl43-before`), same-tree second run `runs/pl43-before-twin` (18:43). AFTER twin: `after/stills/` (= `runs/pl43-after-twin`), `after/stills-twin.xml` Passed 1/1. The same test (`LookDownTests.ConfiguredStills_ForTheWaiver`) renders at the scene's own sun, FOV (16x9 and 21x9) and eye height 1.650 (stills-summary: eye 1.650, pitch 60.0, view root (0.000, -0.050, -0.240), shadow root (0.000, -0.050, -0.170) in both).
- Changed pixels (any channel difference; 960x540 and 1260x540; script `scratchpad\pngdiff.py`, pure numpy PNG decode):
  - BEFORE vs BEFORE-twin (same tree, the noise floor): 32,320 of 518,400 (16x9), 43,191 of 680,400 (21x9); max diff 115.
  - BEFORE vs AFTER twin: 32,670 (16x9), 41,752 (21x9); max diff 114/115.
  - BEFORE-twin vs AFTER twin: 21,138 and 24,422.
  - BEFORE vs the AFTER full-suite run (`runs/20261022-215934`): 37,869 and 41,298.
- Reading: same-tree runs already differ by about 32,000-43,000 pixels (animator clip time differs: summary lines read t 1.18 vs 1.26 and hand rays 4 vs 3 between BEFORE and AFTER), and BEFORE-vs-AFTER is the same size. The change is not distinguishable from the render noise floor. This still does not show that the pixels are identical. qa ran no sign-free twin because the test has no pixel-stable mode; a pixel-exact check would need a frozen animator time (not in scope).
- Structural C1 evidence (masks, view/shadow roots, unchanged scene file, LookDownTests table) is in C1 above.

## Defects and harness notes

- Harness bug (named, not a product failure): first BEFORE probe (a4782e61) used one terrain tile for ground height and placed 11 spots underground; fixed in 25d6019a (TerrainQuery.TryGroundHeight).
- Harness note: `chain.sh` guard restored only ProjectSettings.asset and aborted at 22:23:01 on MC39_Glass.mat churn; fixed, probe and twin ran 22:25:34 to 22:29:47. No product impact.
- Baton 3 (engineer): `SpawnedWildlife_AreOnTheWildlifeLayer` red fixed by td add16/18 (baked animals on Default); both cases Passed in AFTER. EditMode verify hang (add12-15) was a transient incident, not a product defect.
- B8 (Editor timing at the 10 percent gate in BirdFrameTimeTests): FILED 2026-10-22 23:02 CDT, studio:qa `bug/B8`, severity low, OPEN; pre-existing on 04259f4 (A-B-A above).
- Known baseline failures (43 PlayMode, 21 EditMode) are unchanged and are not caused by #43.

## Out of scope

Hitbox and Ragdoll behaviour (reserved only), #40 Interactable wiring, player-timing performance (Editor counts only), the Lookout reds (baseline), the photo-real player body work in `_bmad-output/planning-artifacts/issues/photo-real-player-body-and-layers.md` (other work).

## Sign-off

Signed PASS WITH NAMED FOLLOW-UPS, 2026-10-22, stored at studio:qa `signoff/physics-layers-43`. Tested: L1-L5, C1-C6 as above, on the uncommitted landing on branch physics-layers-43 (baseline 04259f4), Editor PlayMode and EditMode only. Not tested: player builds, player-timing performance, pixel equality of first-person stills, the Lookout baseline reds, Hitbox/Ragdoll behaviour, #40 wiring.
