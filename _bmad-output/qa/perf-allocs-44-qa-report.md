# QA report: GitHub #44 physics allocations (bounds clamp, robin planner)

Status: FINAL 2026-10-23 02:35 CDT, qa-lead. Branch perf-allocs-44 at e6ad92d (main after #43), the #44 landing uncommitted on the working tree. Plan: `_bmad-output/qa/perf-allocs-44-qa-plan.md`.
Evidence root `E:\GitHub\washed-ashore\TestResults\physics-allocs-44\` (`before\`, `before-run1\`, `before-run2\`, `after\`). Grading rules: td adr/perf-allocs-44 parts 4-22 (A3 in the Editor, constraint arm decides; bands from the part 17 noise rerun); director gate/perf-allocs-44 (G1-G5).
All counts are "Editor PlayMode, managed allocation count, not player allocations". No ms or fps claim about #44 (G5).

## Verdict

PASS WITH NAMED FOLLOW-UPS. A1-A5 and G1-G5 pass; the automatic-FAIL review found no change to the bounds rule or the robin flight rules.

Named follow-ups (none blocks #44):
1. B8 / #51: `BirdFrameTimeTests.BirdsCostAtMostTenPercentFps` is the known Editor-timing flake. It failed in the BEFORE full run and passed in the AFTER full run. It is a flake, not a #44 effect; it stays open.
2. G3 reading, for the director's awareness: the clamp now moves only bodies that carry `WorldBoundsBody` (plus registered players). A bare Rigidbody added at runtime without the component is NOT clamped (shown by `Rigidbody_WithoutTheComponent_IsNotRegistered`). Enforcement is by design: `WaterFloat` requires the component, `WorldBoundsBodyCoverageTests` fails if any scene or prefab holds a Rigidbody without it, and the clamp logs one error at Start for any loaded Rigidbody without it. A Rigidbody created purely in code, with no prefab, would be missed until someone adds the component.
3. A3 bytes arm is blind in this Editor (reads 0 B on main); only the `Is.Not.AllocatingGCMemory` constraint can detect allocation. Any future allocation test should use the constraint (the engineer's permanent tests already do).

## Criteria

| ID | Check | Evidence | Result |
|----|-------|----------|--------|
| A1 | Rigidbodies register themselves; no `FindObjectsBy*` in any Update/LateUpdate | `WorldBoundsClamp` now walks static registries (players, bodies) filled by `PlayerController.OnEnable/OnDisable` (`WorldBoundsClamp.Register(controller)`) and `WorldBoundsBody` OnEnable/OnDisable; `playerRescanInterval` and `Rescan()` are gone. grep of Assets/Scripts for `FindObjectsBy*`/`FindGameObjectsWithTag`/`SphereCastAll`: remaining hits are `WorldBoundsClamp.Start` (once, logs bodies without the component), `PlayerDebugCamera.Attach`, `StanceBootstrap.Attach`, `FishProbe.FindRenderer`, `FishSignDraws.Find`, `FarViewProbe.Start` (not Update/LateUpdate by their enclosing method names; I did not trace the callers of `FishProbe.FindRenderer` and `FishSignDraws.Find`; none is in #44 scope). AFTER PlayMode (`after/playmode.xml`): `WorldBoundsBody_RegistersOnEnable_AndLeavesOnDisableAndDestroy`, `Player_IsRegisteredByItsController` Passed. My probe: 20 bodies registered (`WorldBoundsBody` found by type name, count 20) | PASS |
| A2 | `SphereCastNonAlloc` with a worst-case static buffer; a test shows a full buffer is detected | `RobinFlightPlanner`: `CastBufferSize = 64`, `SphereCastNonAlloc`, full buffer = BLOCKED, counted in `FullBufferHits`; EditMode `RobinCastBufferTests` (13 new cases, all Passed in `after/editmode.xml`): `FullBuffer_IsBlocked_AndCounted(64/65/100)`, `IgnoredCollidersFillTheBuffer_WithABlockerBehind_IsBlocked` (the 64 ignored + 1 blocker case), `IgnoredHitsBelowTheBufferSize_DoNotBlock_LikeTheOldRule`, `BelowTheBufferSize_NewRuleEqualsTheOldRule(1/10/63)`. Deviation (engineer names it): a full buffer of only ignored hits now blocks where the old rule was clear (conservative). qa did not run its own forced-failure of these tests | PASS |
| A3 | 0 B GC alloc from `WorldBoundsClamp` and the robin planner while robins flush | Probe `AllocProbe44` 6b465824 (td OK part 12/13/14), same bytes on both trees. BEFORE (main, `before/alloc-report.txt`): `Is.Not.AllocatingGCMemory` allocates=True for BOTH clamp and planner (positive control MET; bytes arm 0 = blind). AFTER (`after/alloc-report.txt`): clamp window with 20 registered bodies and a Rescan tick: constraint allocates=False; planner (16 robins x 8 bearings = 128 `Plan` calls): allocates=False; `A3_RESULT ... zero=yes`; body proof (a body moved 8 m north is snapped back) True; "CLAMP Rescan: absent". Plus the engineer's permanent `PhysicsAllocTests` (3 Passed in `after/playmode.xml`, incl. the control `Check_IsAlive_SphereCastAllIsReportedAsAllocating`). Informational bytes arm: 0 in both phases | PASS (constraint arm, td part 14) |
| A4 | Clamp still works; B5 sweep passes; a Rigidbody north of the line is snapped back | `after/playmode.xml`: WorldBounds cases 18 Passed, 1 Failed; the failure is `B_SolidsOcclude_WorldBoundsAndWaterDoNot_AndUnusableShowsNothing` (a test whose full name contains WorldBounds), red in BEFORE as well (baseline, same name and message). Passing: `Player_TeleportedTenMetresNorth_IsSnappedBackWithinOneFrame`, `Rigidbody_DroppedTenMetresNorth_IsSnappedBackWithinOneFrame` (body spawned from `ClampTestBody.prefab`, not hand-registered, G3), `Rigidbody_AmongOver1024StaticCollidersNorth_IsSnappedWithinOneFrame`, `Rule_SnapsOnlyNorthOfLine`, `Clamp_PerFrameCost_UnderHalfAMillisecondWithTheVistaLoaded`, and all B5 sweep cases | PASS |
| A5 | Robin behaviour unchanged | `RobinPlanTrace` e4ac810d (td OK), seeds 101/202/303, every robin, 8 threat bearings, ignore = real Player root: BEFORE twice (`before-run1`, `before-run2`) byte-identical, sha256 90d68061689f1b660927e8c6cc59ab25a30ed2c8eac78e7d4addf15aae295670 (384 rows, 364 landings); AFTER `after/robin-paths.csv` sha256 90d68061689f1b660927e8c6cc59ab25a30ed2c8eac78e7d4addf15aae295670: byte-identical (paths at 1e-4 and the next rng draw, so rng consumption order is unchanged). Bird tests: 14 Passed + 3 baseline reds BEFORE, 15 Passed + the same 3 baseline reds AFTER (names and messages unchanged) | PASS |

## Director gates

| Gate | Result | Evidence |
|------|--------|----------|
| G1 robins fly exactly as today, Player still an obstacle | PASS | A5 trace byte-identical; `RobinObstacleMask` untouched (ObstacleMask diff-free); the trace passes the real Player root as `ignore` |
| G2 full buffer = blocked, shown by A2's test | PASS | A2 evidence; buffer 64; engineer's log MaxCastHits 0 of 64 over real plans (supporting only) |
| G3 every Rigidbody clamped without hand wiring | PASS with note | A4 body from a prefab carrying `WorldBoundsBody`; coverage test over scenes and prefabs; bare runtime Rigidbody not clamped (follow-up 2) |
| G4 A5 graded against #43; B8 the only bird failure | PASS | bird reds identical to BEFORE; B8 passed this time (below) |
| G5 no ms/fps claim; A3 a GC-alloc count only | PASS | stated above; the frame-time reds are not graded |

## Automatic-FAIL review (no change to the bounds rule or the robin flight rules)

`git diff` of the nine modified files, read by qa; every changed line is allocation plumbing:
- `WorldBoundsClamp.cs`: players and bodies come from registries instead of a scan; `Tick()` public; null entries pruned; `SnapController`, `SnapBody` and `WorldBoundsRule.TryConstrain` are not in the diff.
- `RobinFlightPlanner.cs`: `new[] {...}` became a loop with the same single `rng.Range` draw per heading and the same three distances; `SphereCastAll` -> `SphereCastNonAlloc` with the same "any hit outside ignore blocks" rule plus the full-buffer rule; no tuning, offset, distance or threshold change.
- `BirdTerrain.cs`, `BirdPlacementRules.cs`: capturing-lambda visitors replaced by direct loops over the same cell range with the same conditions and arithmetic. `HabitatGround.cs`: `EdgeDistance` `Mathf.Min(params)` replaced by an inline min over the same four operands in the same order.
- `PlayerController.cs` (+2 lines: register/unregister), `WaterFloat.cs` (`RequireComponent(typeof(WorldBoundsBody))`), `docs/physics-guidelines.md`, `WorldBoundsClampTests.cs` (tests).
Landed hashes: the 16 entries of the engineer's `sha256.txt` verified by qa against the files on disk, 0 mismatches; that list equals td part 22's OKed hashes (producer's comparison; earlier 11 files OKed in td parts 9, 11, 15).

## Test suites (BEFORE = main e6ad92d, one full run; AFTER = landed tree)

| Suite | BEFORE | AFTER | Grade |
|-------|--------|-------|-------|
| EditMode | 416 cases, 395 pass, 21 fail | 429 cases, 408 pass, 21 fail | Same 21 failed names and messages as BEFORE and as the #43 AFTER baseline; +13 passing new cases (11 `RobinCastBufferTests`, 2 `WorldBoundsBodyCoverageTests`); none missing |
| PlayMode full (same command, flags, order and filters; both Explicit probes present) | 241 cases, 184 pass, 44 fail, 13 skip | 247 cases, 191 pass, 43 fail, 13 skip | No new red; 43 reds with the same names. One name left the failing list: `BirdFrameTimeTests.BirdsCostAtMostTenPercentFps` (known B8, a known failure that passed this time; timing flake, not a #44 effect, G5). +6 new passing cases (3 `PhysicsAllocTests`, 3 `WorldBoundsClampTests`) |
| Numeric reds against td part 17 bands | B1 = full BEFORE, B2 = `before/playmode-noise2.xml` (`before/noise-bands.txt`) | `after/playmode.xml` | 18 numbered reds in the six sight/fish/census/world-walk groups: 16 exact (identical to B1 = B2); `WildlifeSightingTests.A6(202)` sighting rate 7.5 % inside its band [6.9, 8.7]; `BirdSightingTests.B6(101)` flock in view 12.5 %, same as B1, inside [11.9, 12.8]. No clause changed, no red now passes except B8, no wildlife layout change (td part 20). `WildlifeFrameTimeTests`: same name and clause, Editor timings 0.66/0.45 -> 0.65/0.45 ms, not graded |

## Notes and harness

- Probes (qa, TEMPORARY, not in the commit): `AllocProbe44.cs` 6b465824286f3f8c373cfe0df76e92b9d74a066e674f6746461493a794e66ad4 and `RobinPlanTrace.cs` e4ac810d515fd045e01740ae88ee6992a5348ac3316e09c6683e1e614578b43c, ran with the same bytes in BEFORE and AFTER; both removed with their .meta at 02:28, `git status` shows no probe file. The earlier probe hashes (a withdrawn series ending d878f328) never produced a graded result: d878f328 was landed during BEFORE EditMode and PlayMode only (Explicit, skipped), then replaced by 6b465824 before the alloc and trace steps.
- Harness notes (not product failures): the BEFORE EditMode run rewrote `_bmad-output/poc/character-facing-report.json` (line endings only, `CharacterImportTests`), which stopped my first chain at its git-status guard; added to the churn restore. My AFTER chain and the engineer's baton-2 verify ran with churn restored between runs. Baton 3 reported its results late twice (BEFORE chain end 01:05:40 reported 01:08; noise rerun end 01:14 handed off 01:22).
- Not tested: player builds or player allocations; the Profiler capture arm (td made it support only); qa's own forced failure of the A2 tests; callers of `FishProbe.FindRenderer` and `FishSignDraws.Find`.

## Sign-off

PASS WITH NAMED FOLLOW-UPS, stored at studio:qa `signoff/perf-allocs-44`. Tested as above on perf-allocs-44 (e6ad92d plus the uncommitted landing); no bug opened beyond the known B8 / #51.
