20 PlayMode tests outside the Lookout suites fail on `main` (39db290) and on `remove-web`. They are not caused by #14: a name-by-name diff of both runs found no failure on the branch only (`TestResults/pod14/playmode.xml` and `playmode-main.xml`, 2026-10-17). The likely cause is PR #7, which moved the player start to `TowerBaseSpawn` at the lookout tower, about 1,030 m from `PlayerSpawn`. These tests were written when the player started at `PlayerSpawn`.

Split out of #8, which keeps only the Lookout suites (S6).

## Failing tests

| Test | Count | Failure (seed 101 unless noted) |
|---|---|---|
| `WorldWalkTests.C2_PlayerSpawnsAtPlayerSpawnOnTheGround` | 1 | Player is 1030.4 m from PlayerSpawn (bar ≤ 1 m) |
| `WildlifeSightingTests.A6_SightingWalkLandsInsideTheBriefBands` (101, 202, 303) | 3 | Sighting rate 0.0 %, 7.5 %, 1.9 % (band 25–55 %) |
| `BirdSightingTests.B6_BirdSightingWalkLandsInsideTheBriefBands` (101, 202, 303) | 3 | Flock in view 12.5 %, 10.3 %, 11.9 % (band 30–65 %) |
| `FishSightingTests.F7_ShoreWalk_HitsSightingAndSurfaceEventBands` (101, 202, 303) | 3 | Sightings 7.3, 15.0, 11.0 per minute (band 1.4–2.8) |
| `FishSightingTests.F7_BluffLook_SurfaceEventsOnly` (3 seeds × McCord, Buzzard) | 6 | Surface events 0.0–1.0 per minute (band 1.2–3) |
| `FishCensusTests.F3_Census_AtThreeSeeds_MatchesTheBrief` | 1 | e.g. seed 101 Shelf CommonCarp 98 fish vs 66.1 |
| `FishBehaviourTests.F6_FourTriggers_EachFishFleesAndSettles` (303) | 1 | One wading fish not back in its band by 90 s |
| `WildlifeFrameTimeTests.WildlifeCostsAtMostTenPercentFps` | 1 | 0.64 ms with wildlife vs 0.42 ms without (bar +10 %) |
| `BirdFrameTimeTests.BirdsCostAtMostTenPercentFps` | 1 | 0.63 ms with birds vs 0.55 ms without (bar +10 %) |

## What to do

- [ ] **Confirm the cause.** C2 proves the spawn moved. Wildlife is placed around `PlayerSpawn` (`HabitatGround.Around(spawn)`), which explains A6. Fish census, F6 and the two frame-time tests are **not yet proven** to share the cause: check each one, for example by running it with the player placed at `PlayerSpawn`.
- [ ] **Decide what each test should measure.** For each test, either (a) place the player or the walk explicitly at `PlayerSpawn` (the test measures the system, not the start point), or (b) rewrite it for the tower-base start, if the start point is what it checks. C2 most likely becomes "the player spawns at `TowerBaseSpawn` on the ground".
- [ ] **Don't loosen bands to pass.** A changed band or threshold needs a reason written in the PR, and the brief values stay unless the designer who owns them agrees.
- [ ] **Frame-time tests:** check whether they fail because of the start point or because the measurement is noisy (run each one three times, A-B-A with the system on and off).
- [ ] **Done when:** the full PlayMode suite has 0 failures outside `Assets/Tests/PlayMode/Lookout/`, and there are no new EditMode failures.

## Overlaps

- #15 (land animals) and #16 (birds) will replace placement around `PlayerSpawn` with placement around the player. A6 and B6 may be rewritten there, so keep fixes to them small (option a), and say so in the PR.
- #8 owns the Lookout suites (S6).
