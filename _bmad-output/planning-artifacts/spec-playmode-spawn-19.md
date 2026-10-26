# Spec: PlayMode tests broken by the spawn move (#19)

Owner: designer-19 (systems-designer). Branch `playmode-spawn-19` (cut from `main` 2d263f5).
Status: **r3, 2026-10-25 13:20 CDT (clock read). Criteria fixed before any result.** Open items: section 9.
r2 changes from r1 (hash cc38733e..., withdrawn): sections 3, 6 and 7 now follow td's ADR; 4.4-4.7, 5.1 and 9
updated to match. r3 changes from r2 (hash cbe2f7cf..., withdrawn): director amend2 (5.1 condition,
PhysicsCountsProbe out of #19 in section 6) and td part7/part8 (log format, Reposition clears no state,
noise bands, void rule). No per-test option and no band changed in either revision.

Gates this spec serves: director `studio:decisions gate/playmode-spawn-19` (C1-C8) and
`gate/playmode-spawn-19-amend1` (A1.1-A1.5, frame-time rig) and `-amend2` (A2.1-A2.4). td
`studio:engineering adr/playmode-spawn-19` parts 1-8 (API, helper, the C8 rule, the frame-time method,
the review of r1 in part 7, clock and amend2 adoption in part 8). Cite td parts by key, not by the time
written inside them (part 8). Where this spec and the ADR disagree, the ADR
holds on mechanism and this spec on what each test measures and its bands.

## 0. How values are tagged

- **[sheet: file:line]**: copied from a record. Not changed by this spec.
- **[derived: how]**: computed from sheet values or read from a file named in the tag.
- **[my choice]**: my design decision for #19. These are listed in section 9 for td and QA.

No band in this spec differs from its record. Gate C2: a band change needs the owning designer's
record in `studio:design`, a reason in the PR, and a director amendment. This spec proposes none.

## 1. Records read

| Record | Where | sha256 / note |
|---|---|---|
| Wildlife density brief (A6 bands, route) | `_bmad-output/poc/wildlife-density-brief.md` | 850a3a94...422cd1f9 (full hashes: section 10) |
| Bird density brief (B6 bands) | `_bmad-output/poc/bird-density-brief.md` | 4e6ddc84...4aa82dd7 |
| Fish targets F18 (F3, F6, F7 bands) | `_bmad-output/poc/fish-targets.json` | 02319b3d...cfb07670 = brief of record in `fish-qa-report.md:6` |
| Player start bar (BB-QA-5) | `_bmad-output/poc/bells-bend-qa-plan.md:62` | "within 1 m of PlayerSpawn, grounded, spawnPoint linked, no fallback warning" |
| Scene start | `Assets/Scenes/World.unity` | `PlayerController.spawnPoint` = fileID 1988935799 = `TowerBaseSpawn` transform (YAML read, line 11838) |
| Fish QA final | `_bmad-output/poc/fish-qa-report.md` | FAIL, 2026-10-08 |
| Bells Bend QA final | `_bmad-output/poc/bells-bend-qa-report.md` | BB-QA-7 ruling, lines 44, 60, 69, 75 |
| Tower QA baseline | `_bmad-output/poc/tower-qa-plan.md:265, 267` | pre-tower tree |
| #40 QA after-run | `TestResults/lookout-interactions-40/after/playmode.xml` | C2 log line (section 5) |

Scene points [derived: World.unity YAML, root transforms; yaw = 2·atan2(q.y, q.w)]:
`PlayerSpawn` (-202.81, 15.89, -942.11), yaw 46.23°. `TowerBaseSpawn` (544.02, 90.75, -232.17), yaw 61.41°.
Horizontal distance 1030.42 m (matches the C2 failure, 1030.4 m).

## 2. Cause history: 12 of the 19 reds were red before the spawn move

PR #7 (tower, spawn move) merged 2026-10-14 13:51 (`git log cd2d766`). The fish pod merged 2026-10-07
(`35c3fcd`). The records show these reds already existed before #7:

| Red | Earliest record | Same as today? |
|---|---|---|
| F7 shore ×3 | `fish-qa-report.md:57`, 2026-10-08: 7.32 / 14.97 / 10.98 per min | issue: 7.3 / 15.0 / 11.0, same |
| F7 bluff ×6 | `fish-qa-report.md:58`: McCord 0 / 1.00 / 0.33, Buzzard 0 / 0.67 / 0.33 | issue: 0.0-1.0, same |
| F3 ×1 | `fish-qa-report.md:20` (species mix and structure pools fail) | same test, same clause type |
| F6 seed 303 ×1 | `fish-qa-report.md:23` (wade fish not back in band at 90 s) | same message |
| WildlifeFrameTime ×1 | `bells-bend-qa-report.md:44` (2026-10-06), non-gating by lead ruling BB-QA-7 (`:60, :69, :75`); `water-qa-report.md:59, 72` | same test, same clause |
| BirdFrameTime | same records; now the #51 flake | flake |

`tower-qa-plan.md:265` (the tower pod's baseline, on the pre-tower tree) lists exactly these 13 fails.
`tower-qa-plan.md:267`: "BirdFrameTime / WildlifeFrameTime fail before the tower (BB-QA-7)".

So only **C2, A6 ×3 and B6 ×3 (7 cases)** are expected to be spawn reds. The fish tests disable the
`PlayerController` and drive a `FishPlayer` override plus the camera (`FishTestKit.cs:64-66, 208-210`), so
the start point should not reach them. Both frame-time tests already walk the A6 route anchored at
`PlayerSpawn` (`WildlifeFrameTimeTests.cs:173`, `BirdFrameTimeTests.cs:123`).

This is history, not proof. Gate C3 still requires each cause check (section 4) on the #19 tree.
Consequence for the done rule: section 8.

## 3. TestSpawn: what the spec relies on

The API and helper are td's, of record in `studio:engineering adr/playmode-spawn-19` parts 1-6
(2026-10-25). This spec names only these calls and does not restate the mechanism:

- **Game side** (part 1): `PlayerController.SetSpawnPoint(Transform)` (changes the respawn point) and
  `SpawnAt(Vector3, float yaw, bool snapToGround = true)` (keeps it). Both go through `Respawn()`'s body
  (`PlaceAt`). No scene value changes (C1).
- **Test side** (part 2), `Assets/Tests/Support/TestSpawn.cs`: `At("PlayerSpawn")` = option (a);
  `AtTowerBase()` = option (b); `At(position, yaw, snapToGround)` (with `snapToGround: false` = the
  exact-height form; it runs the state clearing); `Reposition(...)` for mid-test moves: today's kit ops
  exactly, **no state clearing**, so gait loops keep velocity and stance (td part 7); `Park(...)` /
  `Parked.Move` / `Parked.Release` for scripted rigs.
- **What At does before the test goes on** (part 2): it waits one frame before placing, then one physics
  step and one more frame. It fails the test if the player is more than 0.1 m from the target, or below
  ground - 0.1 m when snapped. It logs one line in this format (td part 7):
  `[TestSpawn] <name or 'pose'> target (x,y,z) yaw a snap true|false -> player (x,y,z) ground g|none grounded true|false frame N`. It does **not** load the scene and does
  **not** wait for grounded.
- **Settle time therefore lives in each test** (td's note). The acceptance criteria in sections 4-5 state
  it. Each test keeps its own existing grounding wait, unchanged:
  `WildlifeTestKit.WaitGrounded` (5 s timeout) and then `startDelayAfterGrounded` 1.0 s for A6/B6
  [sheet: `WildlifeTestKit.cs:58`; wildlife-density-brief.md:121; `WildlifeTuning.asset`], or 3 s for C2
  [sheet: `WorldWalkTests.cs:112`].
- **Order with the kits**: the kit loads World (seed overrides first, as today), then `At(...)`, then the
  kit's own waits. Fish and frame-time call `At` before `Park` (At needs both controllers on).

My r1 requirements H1-H10 are met by parts 1-2 and 7 or replaced by them. r1's H4, H8 and H9-H10 wording is
withdrawn in favour of td's text.

## 4. Per-test decisions

Option (a) = start at `PlayerSpawn`. Option (b) = rewrite for the tower start (C5: only C2).
"Cause check" = what QA runs first (C3). A test still red with the player at `PlayerSpawn` is **not**
a spawn red: it stays red in the report, and gets a follow-up issue to its system owner.
Noise: td's rule of record `adr/physics-layers-43/addendum3` (name, clause with numbers masked, numbers
inside [min - d, max + d] with d = |B1 - B2|, exact if equal). For #19, BEFORE = main 2d263f5, with B1
and B2 as set in td part 7. Frame-time numbers are not banded (Editor timings).

### 4.1 `WorldWalkTests.C2_PlayerSpawnsAtPlayerSpawnOnTheGround` (1 case)
Option **(b)**, plus a second case. Section 5.

### 4.2 `WildlifeSightingTests.A6_SightingWalkLandsInsideTheBriefBands` (101, 202, 303)
- **Option (a), one line** (C4): in `Walk()`, after the population checks and before
  `var player = Player(); yield return WaitGrounded(player);` (`WildlifeSightingTests.cs:131-133`), call
  `TestSpawn.At("PlayerSpawn")`. The brief defines t = 0 as "1.0 s after the player is grounded at
  spawn" and W0 as PlayerSpawn [sheet: wildlife-density-brief.md:121, :147]. The shared `Walk()` also
  runs A6r (non-gating robustness) and A6f (non-gating fidelity); they get the same start. [derived]
- **Measures:** sightings of land wildlife on the fixed 180 s route from PlayerSpawn.
- **Bands, unchanged** [sheet: wildlife-density-brief.md:132-137; approval :270]: sighting rate
  25-55 %; longest gap ≤ 45 s; first sighting ≤ 30 s; max in view 3-8; samples with ≥ 5 visible ≤ 10 %;
  each species visible in ≥ 2 consecutive samples. Read by the test from `WildlifeTuning.asset` sighting
  block [derived: asset lines 164-172 equal the brief].
- **Cause check:** the AFTER run of A6 is the check. Cause = spawn if all three seeds pass every band.
- **Acceptance:** A6.1 all 6 bands pass at 101, 202, 303. A6.2 the `TestSpawn` log line shows
  PlayerSpawn's pose within 1 m before t = 0. A6.3 `plannedAnchorsMatchScene` true and no
  `placementError` (existing asserts, unchanged). A6.4 no band constant or tuning value differs from
  `main` (QA diff). A6.5 the PR says A6 will be rewritten in #15 (C4).

### 4.3 `BirdSightingTests.B6_BirdSightingWalkLandsInsideTheBriefBands` (101, 202, 303)
- **Option (a), one line** (C4): in `Walk()` (`BirdSightingTests.cs:71`), before
  `var player = WildlifeTestKit.Player(); yield return WildlifeTestKit.WaitGrounded(player);` (:91-92).
  B6r (non-gating) shares the start. [derived]
- **Measures:** bird sightings and flushes on the same A6 route.
- **Bands, unchanged** [sheet: bird-density-brief.md:166-175; R1 approval :236; B1 approval :242]:
  flock in view 30-65 %; longest empty-sky gap ≤ 45 s; first flock ≤ 30 s; ground birds met
  1.7-5.0 /min; flushes 3-12 per walk, ≥ 40 % seen; max birds in view 3-20; samples with ≥ 15 in
  view ≤ 5 %; glide share 40-75 %, and every other gating band the test already asserts.
- **Cause check:** the AFTER run of B6. Cause = spawn if all three seeds pass every gating band.
- **Acceptance:** B6.1 every gating band passes at 101, 202, 303. B6.2 `TestSpawn` log line as A6.2.
  B6.3 no band changed (QA diff). B6.4 the PR says B6 will be rewritten in #16.

### 4.4 `FishSightingTests.F7_ShoreWalk_HitsSightingAndSurfaceEventBands` (3) and `F7_BluffLook_SurfaceEventsOnly` (6)
- **Option (a)** [my choice; the issue comment also lists F7 and F3 for (a)]: one `TestSpawn.At("PlayerSpawn")`
  in `FishTestKit.Load`, after the scene load and before the player is disabled
  (`FishTestKit.cs:62-66`; td part 3 makes that disable `TestSpawn.Park`, so the order is `At`, then `Park`). This puts every fish test's start in the test (C8 intent) and is the
  cause check at the same time. It also reaches F3, F6 and the passing fish tests that use `Load`
  (F4/F5, LiveSet, ShoreDistanceGuard, radius rule, F8Spawn, FishInfoRun, SignCalibration); those must
  stay green with unchanged numbers.
- **Measures:** shore sightings per minute on the brief's fixed shore route, and surface events from
  both bluffs. The route and poses come from the brief, not from the player start
  [sheet: fish-targets.json:931-932].
- **Bands, unchanged** [sheet: fish-targets.json:899-901 sightings 1.4-2.8 /min; :904-906 surface events
  0.6-1.5 /min; :923-926 bluff 1.2-3.0 /min; and every other band the test asserts].
- **Cause check:** AFTER vs BEFORE numbers; the two runs differ only by the `At` line (qa cites the diff,
  td part 4). Expected from the records (section 2): unchanged, so not a
  spawn red. If unchanged: stays red (C3), follow-up issue to the fish owner, citing `fish-qa-report.md`
  F7. If green: spawn red, fixed.
- **Acceptance:** F7.1 either all 9 pass, or every red keeps the same name and clause with numbers inside
  td's noise bands of BEFORE. F7.2 no band changed. F7.3 the follow-up issue is named in the final report
  if any stay red.

### 4.5 `FishCensusTests.F3_Census_AtThreeSeeds_MatchesTheBrief` (1)
- **Option (a)**, the same `FishTestKit.Load` line (4.4). [my choice]
- **Measures:** fish counts per band, species and structure pool against F18.
- **Bands, unchanged** [sheet: fish-targets.json densities and mix; rule of record in
  `fish-qa-report.md:20` detail: per seed where expected groups ≥ 10, else pooled ±max(25 %, 2σ), hard ±50 %].
- **Cause check, acceptance:** as F7 (4.4), against BEFORE's per-cell numbers.

### 4.6 `FishBehaviourTests.F6_FourTriggers_EachFishFleesAndSettles` (303)
- **Option (a)**, the same `FishTestKit.Load` line. [my choice]
- **Measures:** each triggered fish flees 8-15 m and settles back in its band within the settle window.
- **Bands, unchanged** [sheet: fish-targets.json:1015-1019 settleSec 45, settleBand 30-90 s; flee
  8.0-15.0 m at :1011-1012].
- **Cause check:** as F7 (4.4). F6 has a single red seed, so the cause counts as shown only after
  **3 green runs of seed 303** at PlayerSpawn [sheet: td part 4]. Fewer than 3 green = not shown.
- **Acceptance:** as F7 (4.4). 101 and 202 must stay green.

### 4.7 `WildlifeFrameTimeTests.WildlifeCostsAtMostTenPercentFps` and `BirdFrameTimeTests.BirdsCostAtMostTenPercentFps`
- **Start:** `TestSpawn.At("PlayerSpawn")` right after the load, before the route rig takes over and
  before the warm-up (A1.1). [my choice of the two A1.1 forms: by name, the same start as A6/B6]
- **Rig** (td part 5): `Park` after the start, then `Parked.Move` per frame (the same transform write as
  today), both controllers off for the whole walk. A comment names `gate/playmode-spawn-19-amend1` and
  `adr/playmode-spawn-19/part5` (A1.2, A1.3).
- **`eye`** (`WildlifeFrameTimeTests.cs:186`, `BirdFrameTimeTests.cs:132`) is read after the TestSpawn
  start, so it is measured at PlayerSpawn. [derived]
- **Bar, unchanged** [sheet: test constant `MaxFpsDrop = 0.10`, from A8 `poc-spec-washed-ashore-wildlife.md:49`
  and B8 `poc-spec-washed-ashore-birds.md:49`]. Owner: td (C6). No ms or fps claim from Editor timings.
- **Cause check:** the A-B-A in section 7.
- **Acceptance:** FT.1 TestSpawn start present and before the warm-up (code read). FT.2 A1.3 (a)-(c) met
  (code read). FT.3 the A-B-A result is reported by its decision rule. FT.4 BirdFrameTime is graded as the
  #51 flake whatever it does; it is not fixed inside #19 (C6).

## 5. C2 becomes two cases

The old name `C2_PlayerSpawnsAtPlayerSpawnOnTheGround` goes; its claim is no longer true (C1). QA maps
old name to new names in the report. [names: my choice]

### 5.1 `C2_GameStartsAtTowerBaseSpawnOnTheGround` (option b)
- **No `TestSpawn` call.** The game's own start is what this case checks; calling the helper would
  hide a broken scene wiring. Confirmed by director amend2 A2.1 and td part 7.
- **C2a.0 (amend2 A2.1): C2a moves the player by no route at all**: no TestSpawn, no `Respawn()`, no
  transform write and no CharacterController or PlayerController toggle. Any such move is a C1/C8 FAIL.
  QA checks it by reading the test.
- Steps: load World, watch `PlayerController:` warnings through `Start` and the landing (as today,
  `WorldWalkTests.cs:95-118`), wait 10 frames, then until grounded or 3 s.
- **Acceptance:**
  - C2a.1 no `PlayerController:` fallback warning [sheet: BB-QA-5, bells-bend-qa-plan.md:62].
  - C2a.2 `player.SpawnPoint` is the transform of the object named `TowerBaseSpawn` [my choice: proves the scene reference, not the name fallback].
  - C2a.3 horizontal offset from `TowerBaseSpawn` ≤ 1 m [sheet: BB-QA-5 bar, applied to the new point: derived].
  - C2a.4 grounded [sheet: BB-QA-5].
  - C2a.5 y within [ground - 0.1, ground + 1] m [sheet: existing bounds, `WorldWalkTests.cs:127`].
- Range check [derived: `TestResults/lookout-interactions-40/after/playmode.xml`, C2 log line]: the
  player landed at (544.02, 90.80, -232.17), ground 90.75, grounded, 0.00 m from TowerBaseSpawn. So
  y - ground = 0.05, inside C2a.5, and the bar is not fitted to a margin.

### 5.2 `C2b_TestSpawnAtPlayerSpawnLandsOnTheGround` (new)
- Steps: load World, `TestSpawn.At("PlayerSpawn")`, same warning watch, wait until grounded or 3 s.
- **Acceptance:**
  - C2b.1 no `PlayerController:` warning.
  - C2b.2 horizontal offset from `PlayerSpawn` ≤ 1 m after the grounding wait [sheet: BB-QA-5]. (At itself already fails beyond 0.1 m at placement, td part 2.)
  - C2b.3 grounded within 3 s [sheet: existing wait].
  - C2b.4 y within [ground - 0.1, ground + 1] m [sheet: `WorldWalkTests.cs:127`].
  - C2b.5 yaw within 1° of PlayerSpawn's yaw (46.23°) [my choice].
  - C2b.6 the `PlayerSpawn` found by the helper is the same transform as `WildlifePopulation.PlayerSpawn`
    [my choice: proves A6/B6 start where their route and animals are laid out].
- Range check: Bells Bend R1 measured "Player 0.00 m from PlayerSpawn, grounded" on this point
  (`bells-bend-qa-report.md:44`); the point is valid ground.

## 6. Gate C8: every other player move in the tests

td's part 3 is the rule and the call-site map, as amended by director amend2 and td part 8. It converts
the WildlifeTestKit, WorldBoundsTestKit, WaterTestKit and StanceTestKit moves via `Reposition`, and the
fish and frame-time controller toggles via `Park`. QA runs part 3's audit grep.

Known movers **outside #19** (listed in the PR, each with a follow-up to adopt TestSpawn):
- `Assets/Tests/PlayMode/Lookout/` (#8 owns it; amend2 A2.2). No #19 edits there.
- `Assets/Tests/PlayMode/Physics/PhysicsCountsProbe.cs:188-190` and `:226-228`
  (`cc.transform.SetPositionAndRotation` with the CharacterController toggled around it). It is
  `[Explicit]` (#43 L5, :47), so it is not in the suite. It stays **byte-unchanged**, so #43's counts stay
  comparable (amend2 A2.3; td part 8). These lines are the only allowed audit hits in `Physics/`.
  This is not an exemption for any test in the suite.

Design requirements on that conversion [my choice]:
- The suites converted with `Reposition` are green today. They must stay green under their own asserts
  (td part 7 grades them pass or fail; a new red goes to td with the row). If an evidence number they
  write moves, report it as a finding; never retune it.
- The A6/B6 walker's stall fallback (`WildlifeTestKit.cs:177`) keeps its rule: 4 failed sidesteps, then
  a logged hop to the next path corner. Only the mechanism changes (`Reposition`). The stall log fields
  in the A6 JSON are unchanged.
- `FishF8Spawn` (`Assets/Scripts/Fish/FishF8Spawn.cs`) is game code, not a test mover. Not in #19.

## 7. Frame-time A-B-A plan

The method and decision rule are td's. They are fixed in `adr/playmode-spawn-19/part4`, with part 5
(mechanism, C6 budget) and part 6 (routing). My r1 legs (file swap) and ratio rule are **withdrawn**.

This spec supplies only what part 4 asks of it:
- **Spec start, both tests: `TestSpawn.At("PlayerSpawn")`** [my choice; amend1 A1.1 allowed this or
  `At(route[0], yaw)`]. Reason: the same start as A6/B6, and route[0] is PlayerSpawn snapped to the
  NavMesh within 3 m anyway [sheet: wildlife-density-brief.md:147; `WildlifeRules.cs:41-43`].
- So with `WA_FRAMETIME_SPAWN` unset or `PlayerSpawn`: legs A1 and A2. With `TowerBaseSpawn`: leg B.
- Order in each walk (part 5): load, `At`, read `eye`, `Park`, then `Parked.Move` per frame. So `eye` is
  measured at PlayerSpawn [derived].
- Bar unchanged (C6, part 5): `MaxFpsDrop = 0.10` [sheet: A8 `poc-spec-washed-ashore-wildlife.md:49`,
  B8 `poc-spec-washed-ashore-birds.md:49`].
- **Void rule** (adopted into td's rule, part 7): if A1 and A2 differ in NUnit result, the triple is
  void. qa reports it, td decides on a repeat, and nobody picks a leg.
- The graded number is `fps_drop`. r = on/off may be reported alongside (drop = 1 - 1/r, td part 7).
- All six legs run back to back in one quiet-machine window on the landed tree; none is taken from the
  baseline slot (producer-19, td part 8).
- Outcomes route as part 4 and part 6 say. A "pre-existing red" outcome goes from qa-19 to td and
  producer-19, then to team-lead and Noah.

## 8. Done rule

Of record (brief, issue, Noah's agreement): **0 PlayMode failures outside `Assets/Tests/PlayMode/Lookout/`,
and no new EditMode failures.**

Gate C3 says a red that is still red with the player at PlayerSpawn stays red, with a follow-up issue.
Section 2 shows 12 reds (F7 ×9, F3, F6 seed 303, WildlifeFrameTime) that predate the spawn move. If
their cause checks confirm that, the done rule above cannot be met inside #19 without breaking C2 or C3.

**Proposed done rule for #19** (PENDING: team-lead to put to Noah, since he agreed the first one; P1):
- D1 C2a, C2b, A6 ×3 and B6 ×3 pass.
- D2 Every test whose cause check (section 4) shows the spawn is the cause passes.
- D3 Every red whose cause check clears the spawn keeps its name and clause, with numbers inside td's
  noise bands of BEFORE, and has a follow-up issue named in the final report (fish owner for
  F3/F6/F7; td for WildlifeFrameTime).
- D4 No other PlayMode red outside `Lookout/`. BirdFrameTime counts as the #51 flake only.
- D5 No new EditMode failures (restore `character-facing-report.json` after each run, #61).
- D6 C1 (scene diff empty), C2 (band diff empty), C7 (known-gap text in the PR and final report)
  and C8 (section 6) hold.

Until Noah answers, QA grades against both rules and reports which one passed.

## 9. Open items and my-choice values

Pending:
- **P1** Done rule (section 8). Owner: team-lead with Noah (td part 6 routes the known-failure
  acceptance the same way).

Closed: r1's P2 (C2a with no At call) by director amend2 A2.1; P3 (`Lookout/` outside C8) by amend2
A2.2 and td part 3; P4 (td's ADR) by parts 1-8. amend2 A2.4: D1-D6 are consistent with the gate; the
choice is team-lead's and Noah's.

My-choice values (for td and QA, before any expensive run): fish start in `FishTestKit.Load` (4.4-4.6);
frame-time spec start `At("PlayerSpawn")` (4.7, 7); new C2 names; C2a.2; C2b.5 (1° yaw); C2b.6; the
design requirements in section 6; proposed D1-D6.

## 10. Full hashes of the records read

- wildlife-density-brief.md 850a3a945d8fc6dd72d44b6acb19fa4193bd701b853896810ba57aee422cd1f9
- bird-density-brief.md 4e6ddc84df46f254fb5e7cf1e8a881d5d07d8e01b8d20518b05e4f044aa82dd7
- fish-targets.json 02319b3dbf5cb70caf3638b56b710af2e7000db17cecfaed7bb40830cfb07670
