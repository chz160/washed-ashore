# QA plan draft, playmode-spawn-19 (qa-19). Moves to _bmad-output/qa/playmode-spawn-19-qa-plan.md after the greenlight.

## Baseline (from TestResults/lookout-interactions-40/after/playmode.xml, 264 tests, 24 failed)
19 red outside Lookout/, same names as issue #19 minus nothing: C2 (1), A6 (3), B6 (3), F7 shore (3), F7 bluff (6), F3 (1), F6 seed 303 (1), WildlifeFrameTime (1). BirdFrameTime passed in after/, full/, full-run1/ and failed in before/ (the #51 flake). 5 further reds are in Lookout/ and belong to #8/#40.
Noise (before/noise-bands.txt, two runs): B6, F6, F3, F7 values are EXACT between runs, so these are deterministic, not noisy. Only the two frame-time tests are timing-based.

## Checks to run (each only with the producer's Unity slot)
1. Cause checks (per test, spec names the method): run each of C2, A6, B6, F7 shore, F7 bluff, F3, F6, and the two frame-time tests with the player at PlayerSpawn. Pass for the cause = the test goes green with the same bands. A test that stays red at PlayerSpawn has a different cause and is a finding, not a fix target to loosen.
2. Frame-time A-B-A (td's method, from studio:production pod/physics-layers-43/batons-resume28): one run per leg of the named test, same wrapper and flags, back to back, start/end from date, quiet machine confirmed at A1 and A2. Report pooled lines verbatim. No ms/fps claim from Editor timings.
3. Full PlayMode suite, TestResults/playmode-spawn-19/. Goal: 0 failures outside Lookout/. EditMode: no new failures; restore _bmad-output/poc/character-facing-report.json afterwards (#61).

## Static checks (grep, no Unity)
- Scene start still TowerBaseSpawn: Scenes/World.unity PlayerController.spawnPoint fileID unchanged vs main; git diff of the scene is empty.
- No test moves the player except through TestSpawn: grep Assets/Tests for `transform.position =`, `SetPositionAndRotation`, `Teleport`, `controller.enabled` on the player. NOTE: WildlifeFrameTimeTests and BirdFrameTimeTests Place() use player.SetPositionAndRotation every frame on a scripted walk (player.enabled=false, CC off). Ask the spec whether that is allowed or must go through the helper; do not decide myself.
- Bands: diff every numeric constant/assert in the touched test files vs main; any change needs a recorded reason (designer ruling in studio:design) else FAIL.
- Regression for C2: new case that TestSpawn.At("PlayerSpawn") lands on the ground.
- Game side: SetSpawnPoint/SpawnAt calls Respawn (ground snap, controller off/on, ClearClimb); check an EditMode/PlayMode case exists for it.
- Test-order leakage: TestSpawn must not leave the spawn point changed for later tests (the scene reloads, but check a PlayerController static or persisted field).

## Open questions for the spec
- Frame-time tests: start point is irrelevant to the cost if the walk is scripted, but the route and wildlife are laid around PlayerSpawn; what does the player start matter there? Expect the red to be from the route/eye height (eye is computed from the player Y at the tower base) or noise. A-B-A decides.
- Sign-off wording: PASS / PASS WITH NAMED FOLLOW-UPS (A6/B6 rewrite in #15/#16, #51 flake) / FAIL.

## Director conditions (studio:decisions gate/playmode-spawn-19) that I grade
- C1 no scene/spawnPoint/TowerBaseSpawn/PlayerSpawn change (git diff of World.unity empty). C2 bands unchanged vs the records listed in the gate. C3 a test red at PlayerSpawn stays red + follow-up, not moved. C4 A6/B6 one-line option (a). C5 option (b) only for C2-test. C6 no Editor ms/fps claims; #51 separate. C7 known-gap statement in PR/final report. C8 player moves only through TestSpawn (grep).
- Open for C8: the frame-time tests' per-frame scripted SetPositionAndRotation walk. Asked designer-19/td to rule whether it is exempt.

## td rulings I grade (studio:engineering adr/playmode-spawn-19, part1..part4)
- C8 audit (part3): `grep -rnE "SetPositionAndRotation|\.position\s*=|localPosition\s*=|\.Translate\(|\.enabled\s*=|\.Respawn\(\)" Assets/Tests --include=*.cs | grep -v /Lookout/`. Every hit outside TestSpawn.cs must be a non-player object or a yaw-only write; else automatic FAIL. Frame-time rig: At(start) then Park then Parked.Move, with the amend1 key in a comment.
- Game side: diff of PlayerController.cs to 2d263f5 is only SetSpawnPoint, SpawnAt, PlaceAt and Respawn's call to PlaceAt. Scene/serialized values unchanged.
- TestSpawn.At self-checks (0.1 m horizontal, feet >= ground - 0.1) and logs a [TestSpawn] line; I cite it.
- Cause checks: F3/F6 green run differs from red only by the At line (cite diff). F6 seed 303 needs 3 green runs at PlayerSpawn.
- Frame-time A-B-A (part4): env WA_FRAMETIME_SPAWN (unset = spec start, PlayerSpawn, TowerBaseSpawn). Per test: A1 spec start, B other point, A2 spec start; one run of that test alone per leg, full-suite wrapper flags, quiet machine confirmed per leg, date start/end per leg. Wildlife three legs, then Bird three. Start point is the cause only if the TowerBaseSpawn drop exceeds the PlayerSpawn drop by more than |A1-A2| at both A-B pairs. Else: both A pass = Editor noise (Bird -> #51); any A fails = pre-existing red, file/update bug, director via producer. Report pooled lines verbatim; no ms/fps claims. Evidence goes to td to grade.
