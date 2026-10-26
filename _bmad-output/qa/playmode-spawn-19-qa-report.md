# QA report: #19 PlayMode tests broken by the spawn move

Author: qa-19. Date: 2026-10-25. Branch `playmode-spawn-19` from `main` 2d263f55. Verdict: **PASS WITH NAMED FOLLOW-UPS** (graded against D1-D6, Noah's rule of record `studio:production pod/playmode-spawn-19/noah-done-rule`).

Graded against: spec r3 (sha256 751f53e58a7b09602465af950d7c40a2122a5b0481086a98b7774d904df642b5, td OK part9), director gate `gate/playmode-spawn-19` C1-C8 with amend1 and amend2, td ADR `adr/playmode-spawn-19` parts 1-20.

## Runs (all under TestResults/playmode-spawn-19/)

| Run | When (CDT, 2026-10-25) | Result |
|---|---|---|
| BEFORE B1, B2 (main 2d263f55, red fixtures, 22 cases) | 13:26:29-13:36:28, 13:36:28-13:46:20 | 2 passed, 20 failed each; 18 non-timing reds byte-equal B1=B2; `before/before-messages.txt` sha256 225a5327...d363 (td part14 record of file) |
| EditMode on landed tree (engineer-19) | 15:36:40-15:37:57 | 420 total, 419 passed, 0 failed, 1 skipped (`ClimbRulesTests...` [Ignore] since #40, same on main) |
| AFTER-R1 cause check (21 fixtures) | 15:39:40-15:43:44 | 10 passed, 11 failed (`after/R1.xml`) |
| Frame-time six legs (td part4) | 15:45:02-16:03:54 | all legs red; none void (`legs/`) |
| Full PlayMode run 1 | 16:04:29-17:32:15 | hung at ReloadAssembly, ended by Noah, no results; infrastructure, not graded (`full/`) |
| Full PlayMode run 2 | 17:36:19-18:20:54 | 266 total, 227 passed, 12 failed, 27 skipped (`full-r2/playmode.xml` sha256 7a8f970c...be03) |

Skips: 27 in full-r2 and 27 in the #40 baseline (`lookout-interactions-40/after/playmode.xml`); no `[Ignore]` or `[Explicit]` added in the diff.

## D1-D6

- **D1 PASS.** `C2_GameStartsAtTowerBaseSpawnOnTheGround`, `C2b_TestSpawnAtPlayerSpawnLandsOnTheGround`, A6 101/202/303, B6 101/202/303 all pass in R1 and in full-r2. [TestSpawn] lines show a 0.00 m miss at PlayerSpawn (td part17).
- **D2 PASS.** Every test whose cause check showed the spawn passes. The old `C2_PlayerSpawnsAtPlayerSpawnOnTheGround` is replaced by C2 and C2b (spec section 5).
- **D3 PASS WITH NAMED FOLLOW-UPS.** The 12 older reds keep their name and clause, with numbers inside BEFORE noise:
  - F7 shore x3, F7 bluff x6, F3 and F6 seed 303: failure message byte-equal to `before-messages.txt` in R1 and in full-r2 (script comparison against B1.xml). With the player at PlayerSpawn ([TestSpawn] line) the numbers do not move, so the start point is not their cause. Follow-up issues to the fish owner are required (td/producer to file; not filed by QA).
  - WildlifeCostsAtMostTenPercentFps: same name and clause as BEFORE. Legs: drop 0.2278 (A1 PlayerSpawn), 0.2260 (B TowerBaseSpawn), 0.2355 (A2); B below both A legs, so not a start-point cause; both A legs fail (td part19: pre-existing, unchanged). Follow-up to td. Full-r2 reads 0.67 vs 0.46 ms (Editor, in-suite; no ms claim).
  - F6 seeds 101 and 202 stay green. The F6 seed 303 triple was not run (red equal to BEFORE; td part17/18).
- **D4 PASS.** No other PlayMode red outside Lookout/, and none inside it. BirdFrameTimeTests passed in full-r2 but failed in B1, B2 and all three Bird legs (A1 0.1153, B 0.1380, A2 0.1141). td part19: pre-existing margin red, graded as the #51 flake; attach the Bird leg values, including the start-point sensitivity (B exceeds both A legs by more than S=0.0012, mostly in the OFF median), to #51.
- **D5 PASS.** EditMode: 420 total, 419 passed, 0 failed, 1 skipped; `character-facing-report.json` restored by engineer-19.
- **D6 PASS WITH ONE OPEN ITEM.**
  - C1: `Assets/Scenes/World.unity` byte-identical to 2d263f55 (git diff empty); no spawnPoint, TowerBaseSpawn or PlayerSpawn change. Game start verified by C2 (grounded, TowerBaseSpawn).
  - C2 (bands): no band constant changed. Diff of Assets/Tests and Assets/Scripts against 2d263f55 shows only spawn-mechanism edits (SetSpawnPoint, SpawnAt, PlaceAt, TestSpawn calls, Parked.Move). No `[Ignore]`, `[Explicit]`, retry, seed swap or wider window added.
  - C8: audit grep (td part3) over Assets/Tests outside Lookout/ and TestSpawn.cs: every hit is a non-player object (cameras, props, bones), the allowed PhysicsCountsProbe lines (file byte-identical to main, amend2), or `StanceTests.cs:276` (Respawn test, compliant per td part18 and director). Both frame-time files carry the amend1 comment and use At, then Park, then Parked.Move. C2 contains no player move (spec C2a.0, read).
  - PlayerController.cs diff to 2d263f55: 29 insertions, 3 deletions; only SetSpawnPoint, SpawnAt, PlaceAt and Respawn's call to PlaceAt. All 22 landed files hash-equal to the engineer's list (verified before the first run and again after full-r2).
  - **C7 (known-gap text) is not yet checkable:** no PR or final report exists. It must appear in both: the game opens at the tower, about 1 km from where land wildlife and birds are placed, so the opening has almost no sightings today (A6 measured 0.0-8.1 %). #15/#16 fix that.
  - **MC39_Glass.mat (closed):** `Assets/World/Lookout/MC39_Glass.mat` was rewritten at 18:20:36 during full-r2 (one line, `_SrcBlend: 5` to `1`). td part21: not caused by #19; Unity saved URP's re-derived blend value at quit, and #40's full run did the same. engineer-19 restored it with `git checkout` (Unity closed). Verified by QA: `git hash-object` = `git rev-parse HEAD:Assets/World/Lookout/MC39_Glass.mat` = 7c3b986a7e2c9a8c83bb5fc88696f4f94bebbf94; `git status` = 25 lines: the 20 modified #19 files, `Assets/Tests/Support.meta`, `Assets/Tests/Support/`, the spec, this plan and this report. Standing rule (td part21): every full PlayMode run may rewrite this file; restore it afterwards, as with `character-facing-report.json` (#61).

## Conditions on this verdict

1. The final report and PR carry C7 and name A6/B6 as to be rewritten in #15/#16 (C4).
2. Follow-up issues exist for F3, F6(303), F7 x9 (fish owner) and WildlifeFrameTime (td); BirdFrameTime values go to #51.
3. (closed) `MC39_Glass.mat` restored and verified; see D6.

## Out of scope for this sign-off
Lookout/ suites (#8); frame-time budget values (td); any ms or fps claim from Editor timings; Windows player build.
