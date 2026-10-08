---
title: 'QA report: Fish in the waters around Bells Bend'
owner: qa-lead (f-qa)
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-fish.md
plan: _bmad-output/poc/fish-qa-plan.md (rev 7.0)
gate: studio:decisions greenlight/fish, gate/fish-technique (+ addendum-1, addendum-2), ruling/fish-roster-look, PHASE 1 OPEN ~20:15Z 2026-10-07
brief_of_record: 'fish-targets.json F18, sha256 02319b3dbf5cb70caf3638b56b710af2e7000db17cecfaed7bb40830cfb07670'
state_of_record: 'World.unity sha256 6d8a19fc794515f5 (FishSurfaceFx wired, #17); FishTuning briefSha16 = F18; footprint pins content-bound and green (#23)'
status: FINAL (2026-10-08; web FPS not tested by Noah's direction)
---

# Verdict: FAIL

No fix slots remain (team-lead / Noah decision); everything still failing below is a recorded finding. F3, F6 and F7 fail; F10 carries a recorded finding (W-QA-1). F1, F2, F4, F5 and F8 pass; F9 passes on Windows, live set and size, with web FPS not tested by Noah's direction.

| # | Result | One line |
|---|---|---|
| F1 | **PASS** | Fish1/Fish2 Keep, 14 pre-fish facing entries unchanged, CC0 licence byte-identical, swim seam continuous in the shipped VAT |
| F2 | **PASS** | 24 variants on Fish1/Fish2, lengths in band, size spread ≥ 92% in band for all 17 species, palette within L1-L4 and the G3 caps, no emission/rim |
| F3 | **FAIL** | band densities pass at all seeds; species mix and 4 structure pools fail under the rule of record (statistical note below) |
| F4 | **PASS** | 0 bed / surface / land / north-of-line violations over ~1.1 M fish samples (qa's own grader) |
| F5 | **PASS** | turn ≤ 11.25°/frame, rate-speed r ≥ 0.997, spacing ≥ 1.003, 0 overlaps; f-td's stuck bar met for drawable tiers (scope decision disclosed) |
| F6 | **FAIL** | 101 and 202 pass; 303 wade fish not back in its band at 90 s |
| F7 | **FAIL** | shore bodies 7.3-15.0 sightings/min (band 1.4-2.8); legible shore signs 0; all 6 bluff looks below 1.2/min |
| F8 | **PASS** | Noah: shore "Right", realistic "Yes"; bluff "didn't see any fish at all, but that seems realistic" |
| F9 | **PASS (Windows, live set, size); web FPS NOT TESTED** | live set, tile loss, in-view pops, web size (+0.37 MiB vs water) and Windows FPS (fish on/off) pass; web FPS not tested by Noah's direction; fish rendering on WebGL2 unverified |
| F10 | **FAIL** (finding) | full-suite wildlife seed 303 = 185 outside the gate (W-QA-1, pre-existing wildlife defect exposed by test order); the W1 literal-height regression from fish test code was fixed in #26 (test-only) and re-checked green |

Automatic FAILs: none tripped (details below). Assists: 0.

## Automatic-FAIL checks

| # | Check | Result | Evidence |
|---|---|---|---|
| AF1 | Placing fish before Phase 0 | **CLEAR** | Pre-gate baseline 1,353 files at 19:27:09Z; at PHASE 1 OPEN every change was an allowed category (Fish1/Fish2 + licence, approved facing fix, look-dev tooling and scene not in the build list, prototype shaders); World.unity and EditorBuildSettings unchanged. `TestResults/fish-qa/af1-gate-check.txt` |
| AF2 | Marine models | **CLEAR** | Only Fish1/Fish2 FBX under Assets; all 24 prefabs source Fish1.fbx (17) or Fish2.fbx (7). `f2-prefabs-engine.txt` |
| AF3 | Rotation offset in code | **CLEAR** | No 180° yaw constants, negated forward or LookRotation(-…) in fish code/shaders; facing from ModelFacingPostprocessor only. `f2-offline.txt` |
| AF4 | Broken web build | **CLEAR (build); load not exercised** | #26 web build: exit 0, 'Build Finished, Result: Success.', index.html present; the in-browser load was not exercised (web walk not run, Noah's direction). `TestResults/fish-qa/builds/qa-build-check.txt` |
| AF5 | Same blocker 3× in a row / > 3 assists | **NOT TRIPPED** | Batchmode Editor hangs: 2 (rows 8, 10), not consecutive. JSON format-specifier root cause: 3 occurrences (one root cause, row 9), not consecutive, fixed at source with write-then-parse tests. Assists 0. |
| AF6 | Greenlight N7 breaches | **CLEAR** | no colliders/fishing affordance/UI markers; no in-view pops; 0 sub-surface draws from the bluffs |

## Pass criteria

**F1 PASS.** qa's own Model Facing Report rerun (slot #9) equals f-artist's entry for entry: Fish1 Keep (head/Face), Fish2 Keep (L/R), 0 ambiguous, the 14 pre-fish entries identical (`fish-qa/f1-model-facing-report.json`). The 12 Animals FBX model entries appeared after a reimport triggered by the facing-fix script change; their decisions match the existing test and their prefabs are unchanged. Licence and FBX are byte-identical to the vendor zip. Seam: graded per vertex on the shipped VAT (decoded offline from the text assets, re-read in engine to 1e-8): 0 vertices outside the local speed envelope at the wrap for both models, and the wrap is smoother than the clip's own frames (`fish-qa/f1-vat-seam-offline.txt`). Disclosure: the strict per-vertex ratio test fails 37 Fish1 vertices at the wrap but also fails up to 250 vertices on ordinary vendor frames, so it is not a pop detector; the envelope test was adopted with that control recorded (plan rev 3.1). VAT 237.5 / 207.5 KB (cap 256), RGBAHalf, 31 frames + rest row (addendum-2).

**F2 PASS.** Offline and in-engine checks (`f2-offline.txt`, `f2-prefabs-engine.txt`, `c2-f2-sizes.txt`): all 24 brief variants present, buffalo on Fish1 (L2), every mean adult length inside its band; palette saturation ≤ 0.40 and value ≤ 0.82 after two one-step nudges (RedearSunfish belly, FlatheadCatfish back); FishUnderwater shader with no emission, rim or specular; shadows off. Size spread from 3 census seeds: ≥ 92% inside [lengthMin, lengthMax] for every species (0.939-1.000), 0 above lengthTailMax, 0 below lengthMin. f-director closed L1-L4 on production stills (`ruling/fish-look-L1-L4-closeout`).

**F3 FAIL** (team-lead ruling: FAIL under the rule of record, analysis shown beside it, not a deviation). Band densities pass at every seed against F18 (shelf 0.0147/0.0141/0.0159 vs 0.0143; ramp 0.0059/0.0055/0.0066 vs 0.0062; open 0.0019/0.0017/0.0019 vs 0.0018). Species mix (rule of record: per seed where expected groups/seed ≥ 10, else pooled ±max(25%, 2σ), hard ±50%) fails in 9 species × band cells, and 4 structure pools fail (IslandFace shelf +35% and ramp +36%, Landing ramp +27%, chute mouth ramp −32%). 0 fish beyond the 50 m open-band edge; shore guard: 0 open cells admitted beyond the edge. **Statistical note:** the plan is unbiased: census vs the plan's own expectation gives z mean +0.26, sd 0.86 over 23 cells, 1 cell beyond 2σ (`TestResults/fish/slotF-f3gate.txt`); at 10-70 groups per species × band per seed the ±25% per-seed bar is under-powered (CV 12-32% before group-size variance). Area reconciliation: ramp and open within 0.3% of qa's probe; shelf −4.9% unresolved (non-material).

**F4 PASS.** qa's grader (`fish-qa/d-grade-f4-tiers.jsonl` and the C2/F equivalents) on every behaviour, scatter, shore and bluff dump at 3 seeds: 0 bed + clearance, 0 surface − margin (WaterMotion, not flat Y), 0 land / no tile, 0 north of the line; terrain calibrated against in-engine TryGroundHeight to ≤ 9 cm.

**F5 PASS.** Turn ≤ 11.25°/frame, rate-speed correlation 0.997-0.999 with speed CV 1.4-1.7, ≥ 99% of samples on the animation rule, spacing ratio ≥ 1.003, 0 box overlaps (`fish-f4f5-*.json`, `slot24-f4f5.xml`). **Disclosed scope decision (post-result):** f-td's ≤ 5 s stuck/escape bar is an engineering condition, scoped to drawable tiers V1/V2 (`ruling/fish-stuck-bar-scope`, accepted by team-lead); drawable stuck 0.13 / 2.00 / 2.07 s. V3 gizzard shad (never drawn) reported only: stuck 7.13 / 2.00 / 9.73 s (known limit).

**F6 FAIL.** Per fish, 3 seeds, 4 triggers (bank by placement, bottom, wade, swim); 8 m measured from the trigger point. 101 and 202 pass; 303 fails: the wade fish is not back in its band at 90 s (`fish-f6-*.json`).

**F7 FAIL** (final; F18 legibility counting, flat distance, counted = drawn).
- Shore 101/202/303: sightings 7.32 / 14.97 / 10.98 per min (band 1.4-2.8), body sightings 22 / 45 / 33 per walk (counter matches the brief's definition: qa recount 21/44/33); legible surface events 0 / 0 / 0 per min (band 0.6-1.5); max sightings in 15 s 5 / 9 / 6 (≤ 3); longest gap 36.2 / 26.5 / 43.7 s (≤ 75, PASS).
- Bluff (near + far, 1.2-3.0/min): McCord 0 / 1.00 / 0.33, Buzzard 0 / 0.67 / 0.33 -> all 6 looks fail; 0 sub-surface body/shadow draws (N2 PASS); airborne jumps per ruling.
- Counted = drawn: 0 undrawn in all runs (qa join of the probe's signDrawn lines agrees).
- Legible distances (F17/F18, min of three independent derivations from the rev 4.2 floor: 6 px minor axis at 1080 lines, Weber ≥ 0.05): rings 10-15 m from the shore eye, 30 m from the bluffs (NervousWater/Busting 60 m from both bluffs, Wake 30 m shore / 100 m bluffs); GarBask 0 everywhere (vertical-extent criterion on a thin line; flagged for F8/f-director). External reference: sipping rises are spotted within ~9 m (Rosenbauer 2018).
- Known limits: canopy occlusion not modelled (f-td); the sign rate never compensated for legibility (rulings).

**F8 PASS** (team-lead ruling). Noah's words, verbatim (relayed by team-lead via f-producer; he was told the F7 finding only after answering; no retune):
> 1. Shore (3-min walk): "I saw one fish. is that realistic?" Asked to pick a rating, he chose: "Right".
> 2. Bluff top: "We were to far away, I didn't see any fish at all, but that seems realistic for how far away from the water we were"
> 3. Realistic: "Yes"

The spec bar ("Right" and "yes") is met on the shore rating and realism; the bluff answer is recorded as given (not a too-empty/right/too-busy rating) and judged realistic by team-lead. What was actually drawn (`TestResults/fish-qa/f8/f8-probe-analysis.txt`, from the build's -fishProbe dumps): shore walk 206 s, 16 distinct fish drawn (13 as bodies), up to 3 at once; by the F7 definition 11 body sightings = 3.2/min (automated F7: 7.3-15.0/min on its scripted route); Noah noticed one, i.e. most drawn bodies are dim, brief and murky enough to go unnoticed. Signs: 2 in view on the shore, 0 within their legible distance; bluff: 0 fish drawn, 1 sign in view, 0 legible, matching his answer. Facing check: the spawn set the facing exactly (shore 323°, bluff 50°); the later yaw values are Noah turning the mouse. **Context, information only (no F7 regrade):** re-scoring body sightings with a legibility floor: Noah's walk 11 → 11 with the size floor (≥ 6 px) → 3 (0.87/min) with size + a contrast proxy (FishMurk vis × assumed body contrast 0.3 ≥ Weber 0.05); scripted F7 seed 202 44 → 26 → 9 (3.0/min). Camera and route explain most of the scripted-vs-Noah gap; a contrast floor brings Noah's count close to his 'one fish'. Follow-up: a pre-agreed body legibility floor (size and measured contrast) for future F7 grading.

**F9** — live set PASS: 0 live fish beyond the sim bubble, 0 in-view spawns/despawns, a disabled tile removes its fish without errors. Web size PASS: Web.data 57.47 MiB (> 25 MiB, so the R2 path, referenced by index.html), no file > 300 MB, under the 100 MB warning; fish add +0.39 MiB over the pre-fish web build (`fish-qa/builds/qa-build-check.txt`). **Windows FPS PASS** (`TestResults/fish-qa/f9/f9-windows-grade.txt`; Builds/Windows-BellsBend, F18, shore route 2,613 m, 1920x1080 windowed, vsync 0, wildlife + birds on): fish on median 5.76 ms (173.6 fps), 99.97% of frames >= 60, p99 9.96 ms, shore leg 5.75 ms / p99 9.01; fish off 5.93 ms, 100%, p99 9.99, shore leg 6.01 / 9.25; fish cost (on - off) -0.17 ms median, within run-to-run noise. One session per Player.log; launches alone with the Get-Process check. Limitation: the walk's fishDrawnMax reads 0 by construction (counter reads DrawModes before the renderer's LateUpdate), so draws in the build are evidenced from the F8 probe dumps instead: confirmed, 16 fish drawn on the F8 shore walk in this build. Follow-up: one 150.9 ms frame in the fish-on run (likely first-use warm-up; fish-off max 46.5 ms). **Web FPS: NOT TESTED, by Noah's direction (web is not this pod's priority).** The web build size check stands (PASS). Stated limitation: fish rendering on WebGL2 is unverified (RGBA16F VAT sampling on WebGL2 untested; fallback FishVatBake.Rgba32). The water pod's web-FPS-with-water-in-view gap stays OPEN.

**F10 FAIL (recorded finding)** (`TestResults/fish-qa/ab/ab-grade.txt`).
- Wildlife/birds A/B: three fish-on filtered runs and one fish-off run all inside the pre-fish gate (sightingRate 112-114 / 180 / 181-182), fish-on identical to fish-off (canary proves the off arm); birds 42/60/78 exact in every run.
- Full-suite run: wildlife seed 303 = 185 (pctGe5 5), outside the gate (181-183): the open W-QA-1 test-order pattern (slot B's full run gave the same 185), not reproduced by any filtered run including fish-off; recorded as an F10 FINDING attributed to W-QA-1 (team-lead): evidence = filtered fish-on identical to fish-off and all in gate, slot B's earlier full run also 185; f-td classifies it as a pre-existing wildlife defect exposed by test order (owner: wildlife), not a fish regression. Full-suite fish-off run: not performed (team-lead: optional, only in idle baton time).
- W1 regression: WaterBuildTests.W1_NoLiteralWaterHeight failed on literal 1.61 in new fish test code (FishSignShapeTests.cs:47, 102). "F10 W1 regression from new fish test code, fixed in #26 (test-only)": re-checked, W1_NoLiteralWaterHeight Passed in `TestResults/fish/slot26-editmode.xml` (105/105), no literal left.
- Water, WorldBounds (B5, clamp), WorldWalk, swim/wade suites pass with fish on; no fish colliders; robins never target fish; Bells Bend terrain unchanged (L1-L4 stand). BirdFrameTime / WildlifeFrameTime perf tests fail as before fish (known exception, f-td).

## Product defects found by QA during the pod

- FishSurfaceFx.set was never wired in World.unity: no surface sign had ever been drawn in the game (found by f-artist's in-game check, confirmed by qa); fixed in #17; every earlier F7 sign count was voided.
- F3 census bugs (structure double count) corrected before grading.

## Follow-ups

1. F3: criterion under-powered; recommend a power-based bar (pooled or σ-based) for future pods.
2. F7: shore body visibility 3-6x the band; f-designer's daytime V1 depth rule is retune candidate #1 if Noah says "too busy".
3. F7: signs illegible beyond 10-15 m from the shore; GarBask 0 under the vertical-extent criterion (look call for f-director).
3b. F7: body sightings are graded at the drawable floor (FishMurk 0.02) with no human legibility floor; Noah noticed ~1 of 16 drawn fish. Recommend a pre-agreed body legibility floor (size + measured contrast) for future pods.
4. F6: wade-trigger settle at seed 303.
5. W-QA-1 (carried from the water pod): full-suite wildlife drift persists; a full-suite fish-off run would prove the attribution.
6. V3 school members can hold ~10 s against a schoolmate (invisible today; relevant for fishing).
7. Batchmode runs rewrite ProjectSettings.asset line endings; every slot restores it.
8. F9 web: web FPS with water and fish in view not tested (Noah's direction); the water pod's web-FPS gap stays open; fish rendering on WebGL2 unverified (fallback FishVatBake.Rgba32).
9. BellsBendFpsWalk's fishDrawnMax reads DrawModes before the renderer's LateUpdate (reads 0 by construction); fix the counter timing.
10. One 150.9 ms frame in the fish-on Windows walk (likely first-use warm-up).

## Friction (`_bmad-output/poc/fish-friction-log.md`)

17 rows, 0 assists: tooling blockers (no web tools, Editor hangs ×2, a JSON format root cause, a capture fault), misroutes (crossed rulings, revision churn, a misread log, incomplete wiring, an evidence overwrite, a baton breach, a mid-slot brief write), each resolved without an assist.
