---
title: 'QA report: Water around Bells Bend'
owner: qa-lead (w-qa)
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-water.md
plan: _bmad-output/poc/water-qa-plan.md
td_note: _bmad-output/poc/water-td-note.md
gate: studio:decisions water/tech-pick (22:20:06Z), greenlight/water-phase0 (22:20:43Z)
state_of_record: 'qa slot #12 final tree (2026-10-07 00:04Z): World.unity af8262415060d25e; WaterRipple.png e5ac4e9dd97f0aca; WaterMaps.png d34968e44e76ec1a; BellsBendWater.shader fcc7b0af7b2d0119; terrain rc4, lake 8f8a637cd4657d82 (unchanged). Windows player exe 91a36fa1…, Perf.dll a632a38f… (w-tools slot #15); web build hashes in TestResults/water-w4/builds/build-hashes.txt'
status: FINAL (2026-10-07, after Noah's session and the -spawnShore cleanup)
---

# Verdict: PASS (0 assists)

- **W1–W6 and W8–W11:** PASS, with the recorded deviations and gaps listed below.
- **W7:** dropped on purpose.
- **W10:** Windows PASS; web PASS (park route; water-in-view web FPS not measured, recorded gap). Deploy not exercised (team-lead).
- **Automatic FAILs:** none tripped.
- **Assists:** 0.
- **Cleanup:** the DEBUG-ONLY `-spawnShore` aid is removed and verified by qa (see Follow-ups).

Noah's in-person checks:

1. W5 drift in motion: **"Right"**, PASS.
2. W6 swim feel: **"Right" ×3**, PASS.
3. W10 web FPS (≥ 30): median 59.9 fps (Chrome 60 Hz cap), PASS, **with a recorded limitation**: the 30 s walk was in the park with no water in view.
4. W10 web blue tint: Noah, verbatim, "Accept from Windows (Recommended)", so it's recorded as INFERRED from the Windows in-build check (same shader, material and sky), not observed on web.

All four are answered, and the `-spawnShore` aid is removed (w-engineer slot #17, qa-verified).

Evidence is cited only from `_bmad-output/poc/` and `TestResults/` (team-lead rule). Numbers marked qa-run come from qa's own runs and scripts. Other agents' numbers are reproduced by qa or marked "claimed".

## Automatic-FAIL checks

| # | Check | Result | Evidence |
|---|---|---|---|
| AF1 | Building before the gate | **CLEAR** | Pre-gate SHA-256 list of 455 files taken at 21:59Z. At 22:21:48Z, after the gate records (22:20:06Z / 22:20:43Z), all 455 still matched, 0 files were newer under Assets/Packages/ProjectSettings, and git status showed docs only. w-engineer's scratchpad staging was authorized by team-lead's spawn brief (friction log). `water-qa/pregate-baseline.sha256`, `phase1-open-seen.txt` |
| AF1b | Scripts-only (water pod scope) | **PASS** | qa slot #12 took a content fingerprint of the Water and Sky roots + RenderSettings + generated assets, ran `BellsBendWater.Build()` + `BellsBendSky.Build()`, and got F1 == F0 (75 lines, 0 diffs). WaterTile/WaterMaps/WaterRipple and both material hashes are unchanged; the ripple seed is a constant (1987). The pod never touched terrain, tiles or MapConfig (they match the pre-gate hashes). `TestResults/water-qa/af1-F0.txt`, `af1-F1.txt`, `qa-probes.txt` |
| AF2 | Non-free or unrecorded licence | **PASS** | Pick A is a hand-written URP shader. `Packages/` is unchanged against HEAD; no new ThirdParty/vendor content |
| AF3 | Hard-coded water height | **PASS** | qa grep for `1.61(0*)` over Assets/ and tools/ (.cs/.py/.shader/.hlsl/.mat) finds 0 hits outside the test's own regex. `W1_NoLiteralWaterHeight` passes, and it also scans YAML .mat/.asset |
| AF4 | Swim-around crossing | **PASS** | 0 crossings in every W8 run, every B5 run and the extra inward-corner runs |
| AF5 | Drowning/stamina/death/respawn built | **PASS** | grep breath/oxygen/stamina/drown/health/death/duck over Assets finds only the pre-existing terrain `healthyColor`. The only Respawn is the pre-existing fall-through guard, textually identical to the pre-gate copy |
| AF6 | Broken web build | **PASS** | `unity build` with profile WebGL.asset → Builds/Web (deploy-web.ps1's build step): exit 0, "Build Finished, Result: Success.", index.html + 4 Build files. team-lead's attempt (`TestResults/water-w4/w10-web/w10-web-attempt1.txt`): local serve only (127.0.0.1:8090), Chrome 154, WebGL 2.0 via ANGLE D3D11 on the RTX 4060 Ti; loads, the park scene renders, no console errors, nothing uploaded. **Hash check (qa):** the served Web.data sha256 `d8f5faf9…eb53cf25` equals `TestResults/water-w4/builds/build-hashes.txt` and the file on disk, so the attempt ran the build of record |
| AF7 | Same blocker 3× / > 3 assists | **NOT TRIPPED** | Crate tests: `ruling/water-same-blocker-crate-snap` (A, B, A by root cause; crate-initial-height at 2). The product fix landed and the W9 record run is green twice. Assists: 0 |

## Pass criteria

| # | Result | Evidence |
|---|---|---|
| W1 | **PASS** | `W1_PlaneYEqualsWaterLevelY`, `W1_WaterBodyLevelEqualsWaterLevelY` and `W1_NoLiteralWaterHeight` pass in qa's EditMode run (`TestResults/water-qa/qa-editmode.xml`, 94/94). The water build log shows 20 quads at y = WaterLevelY = 1.610002, from MapConfig. qa's grep finds no literal |
| W2 | **PASS** | `W2_CoverageScanWholeExtent` (5,628,438 samples, 0 uncovered), `W2_NorthOfLineRiverCovered`, `W2_SeamsShareEdgesExactly` and `W2_QuadsAreWaterLayerNoColliderNoShadows` pass in qa's run. The rebuild log shows WaterMaps `uncovered=0`. Object-level scene diff: 20 quads under the single root "Water", 0 colliders, shadows off. Seam shots `water-seam-top.png` / `-oblique.png` show no seam |
| W3 | **PASS** (w-td signed off, `studio:engineering review/water-w3-guard`) | w-td's tests (i)–(v), `W3_BuildAllThrowsOnMovedLine`, `W3_PostGuardFailureIsWaterBuildException` and `W3_HookFailureIsWaterBuildException` pass in qa's run. qa probe (b) used an in-memory MapConfig clone with northLineZ +100: the Guard alone throws, then `BuildAll(clone, false)` throws `WaterBuildException` "north line moved", with 0/22 watched files changed (`TestResults/water-qa/qa-probe-b-rerun.txt`). See bug W-QA-2 |
| W4 | **PASS** | LayerAudit 7/7 in qa's run ("water objects: 21" under the single root [Water]). 800 fence extensions and the backstop are on WorldBounds(9). Layers are looked up by name. TagManager/DynamicsManager are unchanged since pre-gate. Birds 17/17 ×3, byte-exact to pre-W4 (flushSeen 42/60/78). B5 9/9 ×3. Robin/sight masks exclude 4 and 9; the NavMesh bake mask excludes Water. `TestResults/water-w4/` |
| W5 | **PASS** with 1 known deviation (drift in motion: Noah "Right") | Director: `ruling/water-look-final` and `ruling/water-look-final-flat-check`. Noah, verbatim: "Right" (reads as the river), "Right" (scum), "No blue, edges fine", horizon line "Acceptable". He was told about the far-bank gap up front. Code: LevelMaps shore distance byte-copied into WaterMaps R, with no shoreline derivation; absorption k 2.2/2.4/3.2 hides the bed (≈ 0.02% at 4 m); sky/probe + Fresnel only; colours as greenlit. Flow: 80/80 stations downstream on qa's independent recompute (`water-qa/flow_check.py`); anchors east south 11/11, west-neck north 6/6, south tip west 13/13. Rings fixed (qa verified on before/after crops). Horizon: closed by `ruling/water-look-reshoot-addendum`; edge step bank 29.9 → 9.5, N-line 50.0 → 10.7; upper sky +0.027–0.033 (limit 0.05). In-build no-blue check: 7 opaque player shots from the solo W10 run, with water blue-excess negative in every one. Lip and west neck step: clean waterline. Scum (`water-qa/scum_measure.py`, landed bytes, softness 0.11): median length 0.74 m, median width 0.12 m, moment aspect 4.66 (graded 4–6), coverage 0.32, edge ramp 70%. Known deviation: p10 length 0.32 / width 0.05 m (see Deviations). Shots: `water-look/flat/`, `water-qa/w5-north-line-west-channel.png`, `TestResults/water-w4/w10-windows/shore/water-look-*.png`. Drift in motion: headless frames were inconclusive, so it was deferred to Noah by director ruling. Noah (slot #16 player, east bank): scum drifts downstream along the bank: "Right" |
| W6 | **PASS** (feel: Noah "Right" ×3) | Noah, verbatim (slot #16 player with `-spawnShore`, east bank): wading heavy and clearly slower: "Right"; swimming slow and laboured, worse than walking: "Right"; climbing out on a low bank reliable, no snagging: "Right". Reading of record: `ruling/water-swim-numbers` item 5, plus r2 (no duck, no current push). `pm-w6c.xml` 6/6, reproduced in qa's runs 1–3. Exits: 12 low-bank stations from the rc4 `bank_stations.json` (incl. 1549, nearest to both bluffs, and 31 for the west neck corner), all out, worst 12.17 s (bar 15 s), 0 stall. Climb guard 12/12 (feet ≥ 0.99 m below S). At the bluffs: not an exit, swam away 17.4–17.8 m. SwimRulesTests 10/10 (approved values; stroke-mean speeds; hysteresis; SwimTuning GUID in World.unity) |
| W7 | **DROPPED ON PURPOSE** | Absent from the spec; greenlight note 6 |
| W8 | **PASS** | `water-w8-runs-v2.csv`: 25 valid counted attempts per end, 0 crossings (worst 3.41 m south of the line), 0 clamp snaps. Stoppers: backstop 50, barrier 6, 1 replaced with a logged reason. Extras: 3 inward-corner runs per end (closest to the corner 1.4 m W / 0.7 m E), all stopped by the backstop. qa runs 1–3 reproduce 0 crossings. Try 1 (`water-w8-runs.try1.csv`) also had 0 crossings, but only 19/20 valid on the West end because the invalid runs walked onto land; qa accepted the test-validity fix |
| W9 | **PASS** (w-td signed off) | `water-w6w8w9/editmode-w9-record.xml` 94/94; `pm-w9-record-run1.xml` and `-run2.xml` 5/5 each: crate 0.3 mm; GPU readback 0.00–0.02 mm at 4 corners + origin, t = 0 and +3 days; snaps 7/7 per end. WaterMotionTests 11/11 in qa's run: 1000 samples ≤ 1 mm out to 7 days, wrap continuity, shipped amplitude ≤ 3 cm. The current is visual only |
| W10 | **PASS** (Windows PASS; web builds, loads and runs; deploy not exercised (team-lead); web FPS PASS with a recorded limitation; web blue tint inferred from Windows) | **Windows** (qa-graded with `water-qa/w10_grade.py`; 1920×1080 windowed, vsync 0, release, wildlife + birds on, RTX 4060 Ti). Shore route agreed by qa (`TestResults/water-w4/support/shore-route.json`: park → east low bank 1410–1611 → McCord Bluff, 2,613 m). Solo run of record launched 02:50:13Z (single instance, no Editor, foreground per team-lead's note): median 5.49 ms (182 fps), p95 8.07, p99 9.01, 100% ≥ 60; shore leg median 5.51 ms, p99 7.81, 100% ≥ 60 → PASS (`w10-windows/shore/`). R2 re-run on the water build, solo at 03:00:20Z: median 6.91 ms against 6.68 = **+0.23 ms** (TD budget ≤ +1.5), p99 9.83, 99.99% ≥ 60 → PASS (`w10-windows/r2/`). Validity: the sample counts fit duration/median (0.96–1.03), and each Player.log has one session. The earlier 02:36Z shore run is VOID (two players ran concurrently; archived in `shore-invalid-concurrent/`, friction log). **Builds:** Windows exit 0 / Success; web exit 0 / Success, not deployed (team-lead: NO), both from the final tree. **Web size** (w-td ruling, verbatim): "W10 size PASS for water: about 0.5 MB of the 57.1 MiB Web.data (0.8%; WaterMaps about 0.2 MB, ripple normal about 0.3 MB), by block-level attribution of the Brotli-over-LZ4HC data. The growth since the web baseline is Bells Bend land (terrain about 23 MB shipped, plus kit textures), outside this pod; hosted on R2." (`TestResults/water-w4/builds/web-size-compressed.txt`). **Web FPS** (`TestResults/water-w4/w10-web/w10-web-fps.json`, measured 04:01:29Z by team-lead with Noah present, tab visible; summary only, no per-frame samples): local serve, no deploy; Chrome 154, ANGLE D3D11 on the RTX 4060 Ti; canvas 1904×1015; served Web.data sha256 = the build of record. 1,294 frames in 30 s: median 16.7 ms = **59.9 fps** (Chrome's 60 Hz vsync cap), mean 43.1 fps, p95 33.4, p99 33.6, max 100.1 ms. 79.75% of frames ≤ 33.33 ms; most of the rest sit on the quantised 30 Hz step (33.4 ms ≈ 29.9 fps). Against the ≥ 30 fps bar: **PASS on median and mean.** **Recorded limitations:** (1) the 30 s W-hold walked into the park with **NO water in view**, so the water's web render cost is not directly measured. qa accepts this on the strength of the Windows like-for-like cost (+0.23 ms on R2) and the same shader, but it's a gap, not evidence. (2) The canvas was 1904×1015, just under the requested 1920×1080. (3) About 20% of frames are on the 30 Hz step, a hair under 30 fps. The earlier attempt (`w10-web-attempt1.txt`) was not measurable because the tab was hidden. **Web blue tint:** Noah, verbatim, "Accept from Windows (Recommended)". Recorded as INFERRED from the Windows in-build check (7 opaque player shots, no blue; same shader, material and sky), not observed on web |
| W11 | **PASS**, with 1 recorded deviation (W-QA-1) | qa runs on the final tree (`TestResults/water-qa/`). EditMode 94/94. PlayMode run 1 (full) 70/74: the only failures are the non-gating `WashedAshore.Tests.Performance` cost tests (birds 0.38 vs 0.32 ms, wildlife 0.38 vs 0.29 ms; BB-QA-7), and the 2 skips are Unity InputSystem package tests. Runs 2–3 (gating) 68/68 each. B5: water placement on, 0 crossings, the 414 dry rows identical to Bells Bend `qa2-session3-csv`. WorldWalk/clamp green. Habitat dry: the NavMesh bake volume and the bird/robin habitat window (512 m at −151.8, −891.1) have 0 water cells and lie ≥ 127 m from the shore, and IsLand needs ground ≥ W + margin, so animals can't path into water and robins can't land on it. Terrain is unchanged, so Bells Bend L1–L4 stand |

## Recorded deviations and rulings

| Item | Ruling | Detail |
|---|---|---|
| W5 scum small end | `ruling/water-scum-small-end` (director) | p10 length 0.32 m (< 0.375) and p10 width 0.05 m (< 0.075). These are sliver fragments clipped at the band edge. The medians pass and the visual read governs |
| W5 aspect metric | `ruling/water-scum-aspect-metric` | Graded on the moment axis ratio (4.66). Length ÷ mean width (≈ 6.7) is reported only |
| W5 horizon hue | `ruling/water-haze-hue` | Neutral grey-green accepted in place of "warm". Upper-sky shift ≤ 0.05 |
| W5 far bank | `ruling/water-look-scum-horizon` | No far bank in some views: a known gap, post-POC world work; Noah was told up front |
| W6 reading | `ruling/water-swim-numbers` (+ r2) | The swim switch is at chest depth 1.35 m. No duck-under; the current doesn't push |
| W-QA-1 | `ruling/water-wildlife-a6-tolerance` rev 3; team-lead (a) | Seed 303 sightingRate over 6 runs: 182, 185, 182, **172**, 182, 182 (baseline 182); pctSamplesGe5 down to −8. The only off-pattern run (C2) was the unfiltered full suite with the Performance tests; the filtered C3/C4 were exact. SightSaturations 0 and maxHits ≤ 1 on every counted gating walk, and bands pass. Recorded deviation, not a regression; follow-up P1 |
| Crate same-blocker | `ruling/water-same-blocker-crate-snap` | Not tripped (A, B, A) |
| Performance tests | BB-QA-7 (lead) | `WashedAshore.Tests.Performance` cost tests fail as before; non-gating |
| W10 deploy | team-lead | NO, so it's graded "builds; deploy not exercised" |
| W10 web FPS scope | w-td (recorded gap, not blocking) | Verbatim: "W10 web PASS (park route, 59.9 FPS median); water-in-view web FPS not measured; follow-up logged." Reasons: the spec's shore requirement is Windows-only and the web bar is ≥ 30 FPS; the shader costs +0.23 ms on Windows R2 and has no extra web passes. Canvas 1904×1015. **Watch item:** p95/p99 ≈ 33 ms (one frame in five on the 30 Hz step with no water in view; CPU/wasm headroom, not water). Follow-up #6 in `water-td-note.md`: a 60 s web shore-leg walk with water filling the view (bar: median ≥ 30) |
| W10 web blue tint | Noah "Accept from Windows (Recommended)"; director `ruling/water-web-blue-tint` | Closed by inference from the Windows in-build check (same shader, material and sky); not observed on web |
| Evidence note | — | w-artist flattened the 28 look PNGs to RGB in place after qa made the `flat/` copies. RGB is unchanged, the 19 top-level files are byte-identical to `flat/`, and the original hashes are in `flat/README.md` |

## Bugs filed (studio:qa)

- **W-QA-1** (open, P1 follow-up): wildlife walk not deterministic, with suspected test-order/shared-state dependence (the full-suite run differs from filtered runs). Isolate, then restore ±1.
- **W-QA-2** (open, minor, owner w-level): with no scene open, `BellsBendLevel.BuildAll(nonAssetClone)` unloads the clone in OpenScene, so the Guard reports "no MapConfig" instead of "line moved". It fails safe with 0 writes.

## Friction (`_bmad-output/poc/water-friction-log.md`)

There are 0 assists. Rows recorded:

- w-designer had no web tool, and w-web fixed the citations.
- Conflicting pre-gate instructions (team-lead's brief vs the producer kickoff): not a breach.
- w-td: deep-recon handbacks were missing, and a heredoc broke on U+2248.
- w-engineer: the W9 crate timing and the W8 crate desync; the W6.8 shelf grounding (fix 1); the W8 try-1 validity; the slot #9 crate repeat (same-blocker ruling).
- w-artist overwrote the look PNGs in place (no RGB loss).
- qa's W3 probe artefact (W-QA-2).
- The W-QA-1 deviation.
- team-lead: a background launch with a sleep raced a manual relaunch, so two players ran the W10 shore walk concurrently. Those results are void and the walk was rerun alone.

## Noah's checks (planned evidence, not assists)

W5 look, answered (verbatim, relayed by team-lead via w-producer; far-bank gap raised first):

> 1. Reads as the river: "Right"
> 2. Scum reads as scum: "Right"
> 3. Blue cast and the lip/west neck step: "No blue, edges fine"
> 4. Low-eye horizon line from ~2 m: "Acceptable"

W6 feel and W5 drift, answered (verbatim, relayed by team-lead via w-producer; slot #16 player with `-spawnShore`, east bank):

> - Wading heavy and clearly slower: "Right"
> - Swimming slow and laboured, worse than walking: "Right"
> - Climbing out on a low bank reliable, no snagging: "Right"
> - Scum drifts downstream along the bank: "Right"

Build provenance. Noah's feel and drift checks used the Windows player from w-engineer slot #16, launched with the DEBUG-ONLY `-spawnShore` flag (spawn 6 m inland of low-bank station 1410, facing the water). That player differs from the slot #15 player used for the W10 Windows FPS evidence (Perf.dll a632a38f) only in Gameplay.dll (#16: 5d82a97e…), which adds that flag. The slot #15 Gameplay.dll was not hashed (w-tools recorded only its size, 25,088 B, built 19:06 local), so the equivalence rests on source: the only Gameplay source change in #16 is `PlayerController.DebugSpawnShore.cs` plus the 1-line hook (qa-reviewed). The post-check cleanup proves the reversal the same way: Gameplay .cs hashes equal w-engineer's `landed-final.sha256`, the dll size matches, and a grep finds no `-spawnShore` leftovers. The web build is unchanged (Web.data d8f5faf9…eb53cf25).

W10 web, done by team-lead with Noah present (`TestResults/water-w4/w10-web/w10-web-fps.json`):

- Web FPS: median 59.9 fps (vsync cap), mean 43.1; park only, with no water in view (a recorded limitation).
- Web blue tint, Noah verbatim: "Accept from Windows (Recommended)". INFERRED from the Windows in-build check, not observed on web.

## Closing items after Noah's session (all done)

| Item | Row | Result |
|---|---|---|
| Drift in motion downstream | W5 | PASS (Noah "Right") |
| Swim feel (all three) | W6 | PASS (Noah "Right" ×3) |
| Web FPS ≥ 30 (same Web.data hash) | W10 | PASS (59.9 fps median; recorded gap: no water in view) |
| Web blue tint | W10 / W5 | Closed by inference from Windows (Noah: "Accept from Windows (Recommended)"; `ruling/water-web-blue-tint`) |
| `-spawnShore` removal (w-engineer slot #17) | all | VERIFIED by qa: `PlayerController.DebugSpawnShore.cs` and its .meta are gone; all 5 Gameplay sources match `water-w6w8w9/landed-final.sha256`; grep for "spawnShore" finds 0 in Assets/*.cs and 0 in the built `WashedAshore.Gameplay.dll`; dll size 25,088 B (= the slot #15 size); EditMode 94/94 (`water-w6w8w9/editmode-spawnshore-removed.xml`); World.unity still af8262415060d25e |

## What the pod hands back (spec)

1. The water research report and decision matrix: `_bmad-output/research/technical-water-2026-10-06/research.md`.
2. This verdict: W1–W11, Noah's look/feel ratings, friction rows.
3. The TD note on boats, the arrival-by-wreck spawn and server-side physics: `_bmad-output/poc/water-td-note.md` (w-td: FINAL).

## Follow-ups

- W-QA-1 determinism (P1); W-QA-2 BuildAll clone unload (w-level).
- Scum: a whole-patch metric plus w-artist's 11.5/14.2 tiling and wider band ramps (`water-look/scum-measure-wholepatch.py`, `followup-scum-small-end.md`).
- BellsBendWaterLook (editor) captures should save opaque RGB; w-artist has staged the RGB24 fix.
- Web download levers: the land terrain tiles (about 23 MB shipped) and the 1K kit normal maps (w-td). The all-water-tile alphamap override saves memory/disk only.
- Performance cost tests: re-baseline or retire (non-gating since BB-QA-7).
- Far bank / distant treeline for lake-like views (director, post-POC).
- Web FPS with water in view (TD note follow-up #6): a 60 s web shore-leg walk with water filling the view (bar: median ≥ 30), per-frame samples written out, canvas ≥ 1920×1080. Add a web bluff-top look shot to observe the blue tint directly. Watch the web p95/p99 (≈ 33 ms already, without water).
- **Cleanup (DONE, slot #17, qa-verified):** delete the DEBUG-ONLY `-spawnShore` aid (w-engineer slot #16: `Assets/Scripts/Gameplay/Water/PlayerController.DebugSpawnShore.cs` bfcbcd96… and the 1-line `DebugSpawnShore()` hook in `PlayerController.Start`), re-run EditMode and rebuild. qa reviewed it: inert without the flag (command-line check only); spawns 6 m inland of low-bank station 1410 at terrain height (no literal); faces the nearest water via `WaterBody.SurfaceY`; no AF5 terms; no scene change (World.unity still af8262415060d25e). The Windows build of slot #16 changed only Gameplay.dll (5d82a97e…). The W10 FPS runs used the slot #15 player, and the #16 change adds only a Start-time argument check.
- TD note on boats, the arrival-by-wreck spawn and server-side physics: `_bmad-output/poc/water-td-note.md`.

## Evidence index

- **qa:**
  - `_bmad-output/poc/water-qa/` (evidence.sha256; flow_check.py, scene_diff.py, scum_measure.py, w10_grade.py, WaterQaProbes.cs.txt, pregate-baseline.sha256, phase1-open-seen.txt, west-channel shot)
  - `TestResults/water-qa/` (evidence.sha256; qa-editmode.xml, qa-playmode-run1..3.xml, run1..3 sightings JSONs, qa-probes.txt, qa-probe-b-rerun.txt, af1-F0/F1.txt, pre/post slot-12 hashes)
- **Pod:**
  - `_bmad-output/poc/water-w1w3-editmode.xml`, `water-seam-*.png`, `water-horizon/`, `water-look/` (+ `flat/`)
  - `_bmad-output/poc/water-w6w8w9/` (evidence.sha256, EVIDENCE-INDEX.txt)
  - `TestResults/water-w4/` (W4 slots, `builds/`, `w10-windows/shore/`, `w10-windows/r2/`, `support/shore-route.json`)
  - `_bmad-output/poc/water-friction-log.md`
