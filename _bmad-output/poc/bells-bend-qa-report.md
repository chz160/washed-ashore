---
title: 'QA report: Bells Bend land + north boundary'
owner: qa-lead (qa-2)
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-bells-bend-land.md
plan: _bmad-output/poc/bells-bend-qa-plan.md
brief: _bmad-output/poc/bells-bend-design-brief.md (rev 16+)
state_of_record: 'World.unity content = F0 (file sha 91d9a6b3d1b2d357 after the AF1 rebuild; e8e3c28ed8d3a783 before); terrain rc4 tiles 98a53eb7e0e3fea8, lake 8f8a637cd4657d82; MapConfig d18800230508b305; BarrierBuilder.cs 40602d4b560c0ff8; BirdTuning 55a068b7f1e54d3a; WildlifeTuning e8ff97957016c651'
status: FINAL
---

# Verdict: PASS (0 assists)

All criteria L1–L7, B1–B6, R1 and R2 pass; R3 is N/A. There is no automatic FAIL and there were 0 assists (Noah's FPS-walk go-ahead and his five look checks were planned verification, not assists). Everything below was measured by qa-2 itself: my own geo maths, scans of the dumped Unity scene and terrain, my own raycast/clamp probes, and batchmode test runs with the Editor closed. Other agents' numbers appear only as "claimed" where noted.

## Land

| # | Result | Evidence (qa-run) |
|---|---|---|
| L1 | **PASS** | Polygon→waterline contour on the live Unity terrain: mean 0.50 m, p95 1.00, max 2.00 (bar ≤ 15 m). level-2's live-tile measure 0.51 / 0.75 / 1.25. Overlay: `bells-bend-overlay.png`. Noah: "Reads as Bells Bend: right" |
| L2 | **PASS** | 10 graded points (director `ruling/bells-bend-L2-points`), all within 0.02 m of expectedY computed from MapConfig. The east inner-bank point sits at 36.1710 rather than "near 36.18": the in-polygon ground at 36.18 is the McCord ridge (designer-2). The 5 river and opposite-bank points are graded under L3 (−8.39). scottsboro_ridge_N is N/A (2.8 km north, off the terrain). Converter: Krüger series, matches Snyder to 0.1 mm; centroid within 0.4 m of the pipeline's |
| L3 | **PASS** | Outside the polygon, south of the line, > 1.5 m from the edge: max 1.598 ≤ W (1.61). Beyond the 20 m ramp from any land: max −8.364 (bar ≤ −6.39). Agreed profile reading (BB-T8). Margins W 667 / E 668 / S 642 m |
| L3b (cond. 3) | **PASS** | Min 3.11 = W+1.5 more than 20 m inland; floor clamp 2.84% of land (≤ 5%). The bank band complies (director `ruling/bells-bend-shoreline-bank`); bankBandMeters = 12 |
| L3c (banks) | **PASS** | Option B (`ruling/bells-bend-bank-faces`): 0 lowered cells on the flagged toe faces, 0 within 60 m of a bluff marker, 0 steep lowered cells on banks rising > W+6. Low banks 766/766 ≤ 35°, excluding the accepted 0.31 m lip (below stepOffset 0.4, verified in World.unity). Climbable all-stations 68–69% (informational) |
| L4 | **PASS** | 96.82% ≤ 40° (median 8.2, p90 29.2); vertical 0.70. Bluff area > 40° within 60 m: Buzzard 3,859 m², McCord 6,044 m² (≥ 200). Histogram: `bells-bend-slope-histogram.png` |
| L5 | **PASS** (Noah: "25 years after the collapse: right") | qa reclassification from the DEM + qa road projection, on the final (LOD-tree) state. Ridge 144/ha, open 1.6%, bare 1.6%. Hollows 143.5/ha, 2.9/2.6%. Fields 70/ha, open 37.0%, bare 5.7%, shrub-in-open 84.6%. Bank 94/ha, shrub 81%. Road buffer kudzu 91.7%. Road splat 100%. Cliff rock 86%. Whole land open 22.1% / bare 4.6%. Vista 148/ha. 7 ambientCG layers (layer 0 OldFieldGrass), 8 valid detail prototypes, 101,592 MegaKit-derived trees |
| L6 | **PASS** | Height changes against the gated RAWs lie only within 15 m of the 4 qa-projected roads (max 3.00 m, none below the floor, none within 30 m of the bluffs). Cross-slope median 0.0–1.0°. Road splat ≥ 0.6 on 100% of qa road cells |
| L7 | **PASS** | 10/10 empty markers on the surface. 7 within ≤ 10 m. Slipway 12.1 m from its in-water source (5 m inland of the nearest bank, accepted as a snap). Buchanan House and Cleeces landing are approximate by source |

## North boundary

| # | Result | Evidence (qa-run) |
|---|---|---|
| B1 | **PASS** (Noah: "View north: right"; west corner "what you have now is fine") | Vista 641 m (≥ 400); real DEM ±0.004 m on the neck; neck_north L2 −0.01 m. North cut (BB-QA-1, `ruling/bells-bend-north-cut`): max lake/land step 1.47 m away from the crossings; 4 corner edges 2.09–3.41 m covered by `ruling/bells-bend-corner-step` (x −1422..−1427, z 1989–1991, ≤ 9 m from the crossing); 0 cap-induced slopes; offset std 34.45 m; longest run 16 m; seed 1987 in MapConfig; lake hash stable across rebuilds |
| B2 | **PASS** (Noah: "Barrier: right") | Fence 20 m south of the line (layered design, designer-2), colliders continuous bank to bank (max gap 0.10 m at the closed gate). Gate exactly at the OHB crossing of the fence (x −394.6), closed, gateOpen 0. Sources: Quaternius kits / blockout / MegaKit rocks. Dressing grounded (51 objects checked). Fence-only min clearance 3.29 m above the highest ground within 12 m south; tallest invisible extension 16.0 m (feel note). No non-Fast50 sweep run passed the fence |
| B3 | **PASS** | qa probe: 25,182 rays over x −2137..2059 at 6 heights, 0 gaps, thickness 3.00 m, north face on 1986.7, 0 terrain holes within 50 m. EditMode suite 3/3 (`qa2-editmode.xml`) |
| B4 | **PASS** | qa Play probe: a Rigidbody cube 10 m north at +5 m/s was snapped on the next frame and the Snapped event fired. Clamp suite 5/5 (`qa2-playmode-full.xml`) |
| B5 | **PASS** | `qa2-playmode-full.xml`: 9/9, 0 crossings, 0 snaps; 400 point×case runs with 6 unreplaceable skips (Off60Left 3, Off60Right 2, Off30Right 1; terrain slope / start in water; none in 0° cases); sprint peak 8.01 m/s; dressing-only validity ≤ 10.6%, and at every such index the fence was tested by another case. Lake-bed and wall-end runs 24/24 |
| B6 | **PASS** | Full chain on the REAL MapConfig +100: tools' line-moved warning fired. Backstop probe at 2086.7: 0 gaps (covers the neck cut). Fence +100, gate moved to the new OHB crossing, clamp line 2086.7. Batchmode sweep `qa2-b6-sweep-shifted.xml` 9/9, 0 crossings. Revert: no warning, rc4 hashes, F1 == F0 |

## Regression and budget

| # | Result | Evidence |
|---|---|---|
| R1 | **PASS** | Spawn: Player 0.00 m from PlayerSpawn, grounded, spawnPoint linked (BB-QA-5 fixed); WorldWalk 6/6 incl. C2. Wildlife 18/18 ×3 and birds 17/17 ×3 pinned 60 fps; birds deterministic, flushSeen 101 42% / 202 60% / 303 78% (≥ 40%; 101 is under designer-2's 45% ship margin by designer-2's decision, option A). Informational 30 fps: all pass. **But** `WashedAshore.Tests.Performance` fails deterministically 3/3: WildlifeCostsAtMostTenPercentFps ("OFF run still has enabled Animators: 31") and BirdsCostAtMostTenPercentFps (median 0.47 vs 0.35 ms, bar ≤ 10%). **Cost gate per lead ruling (player ON vs OFF):** OFF walk (`-bbWildlifeOff -bbBirdsOff`, 18:03:30–18:07:26Z) median 149.8 FPS vs ON 149.7, ratio 0.999 (0.15% drop, limit 10%), PASS. JSON flags wildlife/birds false in OFF, true in ON; no active-object counts (skipped, would have needed a re-cut; accepted on qa code review of the toggle). The batchmode Performance tests are non-gating (before: +0.151/+0.124 ms; after the cache: +0.093/+0.060 ms, engineer-2). **qa final re-check after the TerrainQuery cache (18:09–18:19Z, batchmode):** birds 17/17 with 42/60/78 identical; wildlife 18/18, A6 gating all pass (±1 sample tolerance per team-lead); WorldBounds clamp/sweep/WorldWalk 20/20; B5 runs/skips CSVs byte-identical to session 3; Performance (non-gating) birds +0.07 ms, wildlife +0.10 ms (0.37 vs 0.30/0.27 ms); fingerprint F2 == F0; ProjectSettings and scene unchanged. Code reviews accepted: TerrainQuery cache, FPS-walk toggles, build-script provenance |
| R2 | **PASS** | Re-cut build `Builds/Windows-BellsBend/WashedAshorePOC.exe` (17:56:37–17:56:54Z, Succeeded, 0 errors, 3 benign warnings now listed in the provenance file; WashedAshore.Gameplay.dll (TerrainQuery cache) and WashedAshore.Perf.dll rebuilt at 17:56:52Z; scene content = F0). Walk ON (`-bbFpsWalk`, artist-launched per Noah, 17:58:41–18:02:41Z, 1920x1080 windowed, vsync 0, RTX 4060 Ti / i9-14900K, release player): median **149.7 FPS** (6.68 ms), p95 8.55 ms (116.9 FPS), p99 9.34 ms, max 14.51 ms, 35,196 samples, **100% of frames ≥ 60** (bar: median ≥ 60 and ≥ 90%). Route: PlayerSpawn → LM_OutdoorCenter → (474, −13), which qa verified is the map's high point (Y 92.5 of 92.7 max, hRel 129.9 m, ridge zone); 1,150 m. qa read both JSONs and Player.log itself (no errors) |
| R3 | N/A | Struck (greenlight condition 5, Windows-only) |

## Automatic-FAIL checks

| # | Result |
|---|---|
| AF1 scripts only | **PASS.** F0 → full chain (tools, BuildAll, TreeLods, Ground, HabitatRegen, Coverage), shifted and reverted → F1 == F0 on all 20 terrains (heights, splats, trees, details, prototypes), every object group (barrier, backstop, wildlife, landmarks, player/spawn link) and the tuning assets. Impostor PNG bytes excluded (GPU renders) |
| AF2 visible barrier | PASS (fence, gate, dressing, all visible) |
| AF3 crossings | PASS (0 in B5 and B6) |
| AF4 same blocker 3× | Not tripped. BB-QA-4 failed twice and passed on its 3rd attempt; BB-QA-3 1/3 (producer-2's reading, which qa concurs with) |
| AF5 > 3 assists | 0 assists |

## Bugs filed (studio:qa)

BB-QA-1 north-cut seam (fixed; director ruling). BB-QA-2 lake-bed end-around (closed by design: full-width backstop, X-independent clamp). BB-QA-3 bank rule cut the bluff toes (fixed, option B). BB-QA-4 north-cut seam cliffs (fixed on its 3rd attempt). BB-QA-5 player spawned underground (fixed: relink plus fallback). BB-QA-6 bird flushSeen (fixed: FID 10/alert 16). BB-QA-7 Performance tests (closed by lead ruling: the isolation bug was fixed, the TerrainQuery cache landed, the batchmode cost bars are non-gating, and the R1 cost gate is the player ON/OFF walk, PASS).

## Known limitations and follow-ups

- Moving the north line: the neck crossings aren't re-derived, so cuts up to 22.7 m appear beside the neck at +100 m (director `ruling/bells-bend-line-move-terrain`; the warning guard is verified). At the shifted line, 10 angled end-runs passed the fence plane and were stopped by the backstop.
- BarrierBuilder.LineOffset (20 m) is a code constant (designer-2: a post-POC move into MapConfig).
- Invisible fence extensions up to 16 m (feel note); a robin-ignore layer is a follow-up.
- Neck-vista carve steps up to 3.22 m (info, outside §f).
- TerrainQuery cache (BB-QA-7 fix, reviewed and accepted). Post-POC hardening, agreed with engineer-2: TileAt scans at most twice (cached, then one forced refresh) with no recursion; reset `cachedFrame` to −1 in a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` hook for domain-reload-off play sessions. Today it is bounded by the `usable[i] = t != null` invariant and self-heals via the null check.
- Editor-batchmode 10% cost tests (`WashedAshore.Tests.Performance`): non-gating per the lead ruling; re-baseline or retire them post-POC.
- Wildlife A6 sighting rates vary by ±1 sample run to run even with a pinned frame step (old and new TerrainQuery alike), so they are not strictly deterministic and are compared with a ±1-sample tolerance (team-lead).
- Process: two agents saved the same Editor at about 10:16Z (cancelled cap plus R2 repaint crossing); resolved by content check.

## BB-QA-7 lead ruling (team-lead via producer-2, verbatim summary)

(1) Fix now (option a): engineer-2 lands the TerrainQuery per-frame tile cache (a real Bells Bend regression); its slot re-runs Performance (informational), the pinned bird and wildlife suites (must reproduce 42/60/78 and unchanged wildlife), the clamp tests and B5 (identical to the last clean run). (2) The editor-batchmode 10% cost tests in `WashedAshore.Tests.Performance` are NON-GATING for R1; record their before/after numbers and log a follow-up to re-baseline or retire them. (3) The R1 cost gate is the R2 player: the walk ON (= R2) vs the walk with `-bbWildlifeOff -bbBirdsOff`; pass if the ON median FPS is ≤ 10% below OFF (the combined A8/B8 bar). Before numbers (batchmode, after the isolation fix, `bb-qa7-performance.xml`): wildlife 0.470 vs 0.319 ms; birds 0.470 vs 0.346 ms.

## Noah's words (verbatim, relayed by team-lead)

- On launching the R2 FPS walk: "Artist launches it (Recommended)"
- On the ON/OFF walks: "Yes, two walks (~10 min) (Recommended)"
- Look checks (relayed by team-lead):

  > "1. Reads as Bells Bend: right
  > 2. 25 years after the collapse: right
  > 3. View north: right
  > 4. Barrier: right
  > 5. West corner: please clarify this question for me"

  - Look check 5 (after team-lead's clarification): "I'm not sure Question 5 is going to be a big issue pretty soon, because in the future work we do there will be water and if a player tries to swim around to placethey shouldn't be they'll drown, so what you have now is fine". This meets the director's `ruling/bells-bend-corner-step` condition, so the exception stands.
  - **Design intent for the water pod (Noah):** water plus drowning is the out-of-bounds deterrent for players who try to swim around the boundary. The water pod should design and test that, including the dry-lake-bed routes that the full-width backstop and clamp cover today.

## Evidence files

- Overlay: `_bmad-output/poc/bells-bend-overlay.png`; slope histogram: `_bmad-output/poc/bells-bend-slope-histogram.png` (+ .csv)
- qa test XML: `TestResults/qa2-editmode.xml`, `qa2-playmode-full.xml`, `qa2-r1-*.xml`, `qa2-b6-sweep-shifted.xml`, `qa2-s4-*.xml`, `qa2-perf-rerun*.xml`; B5 CSVs `TestResults/qa2-session3-csv/`
- R2: `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Washed Ashore/bells-bend-fps*.json`, `Builds/Windows-BellsBend.provenance.json`
- qa scripts, dumps and fingerprints: the session scratchpad `qa2/` (precheck_raw, bank_check*, toe_overlap, northcut_check, l5_check, road_check, l7_spawn_check, b2_check, probe_b3.cs, fp.cs, dump/fp_F0/F1/F2)
- Test plan: `_bmad-output/poc/bells-bend-qa-plan.md`
