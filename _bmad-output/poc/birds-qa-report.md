---
title: 'QA report: Washed Ashore birds spike'
owner: qa-lead (bd-qa)
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-birds.md
brief: _bmad-output/poc/bird-density-brief.md (frozen at SHA-256 BEA7DD9A…CAFA; final B1 ruling + A1–A5 + R1 final)
friction_log: _bmad-output/poc/friction-log.md, section "Birds spike (2026-10-05)", rows BD1–BD27
verified: '2026-10-06T00:48Z – 04:50Z (2026-10-05 19:48 – 23:50 local)'
platform: Windows only (standing rule; web W4/W7 N/A)
---

# Verdict: **PASS** (0 assists) — final

B1–B9 all pass. B7 was finalised on Noah's re-walk in the release build `Builds\Windows` (option (b), see D7). qa produced every gating value by running the command itself, on the final code (R1 tuning, the seed-101 layout re-placed under the final A5, saved 01:41:09Z). No automatic-FAIL condition was triggered.

**Noah's B7 rating of record (release-build walk, session ended 23:48:00 local): "sky right, ground right"** (0 of 2 retunes used). Relayed verbatim by the lead. After that walk he also said "the sky birds looked great. the ground birds looked great". His earlier words about the superseded dev-test-player walk were "thinkgs look good. I rate my walk through a success" and "sky right, ground right, yes I clicked cancel".

Sources: "qa" means qa ran it; "pod" means an owning agent's file that qa read and re-ran or re-hashed; "lead" and "Noah" are relayed verbatim.

## Evidence B1–B9

| # | Criterion | Actual value | Source | Result |
|---|---|---|---|---|
| B1 | Brief exists, cites sources, sets measurable targets, game-director approval | `bird-density-brief.md`: 8 DOIs and 5 URLs; 16 claims tagged "(lit)" (from literature, not re-fetched) and 6 "(pod)". Measurable targets are in the 3.4 TARGETS table. Approval: `studio:decisions` / `greenlight/birds-brief` (tag `final`, 00:56:36Z). The brief's "## Approval" quote is byte-identical to it (2523 chars). Amendment A5 (`amendment/birds-brief-A5`, 01:32:32Z, 727 chars) and R1 (`amendment/birds-brief-R1`, tag `final`, 02:23:29Z, 1951 chars) are also quoted byte-identically. qa compared all three with the store itself, with the console set to UTF-8 (BD11). The brief is frozen at BEA7DD9A, unchanged through 04:45Z. **A3 caveat:** the "(lit)" DOIs were not fetched because web tools were disabled (BD1). They must be verified before the brief becomes the real-game baseline | qa | PASS |
| B2 | The pigeon export is repeatable: 76 bones, 5 clips, Generic, no errors | qa ran `tools/blender/export_pigeon.ps1 -Out <fresh scratch dir>`: exit 0 in 6 s, "EXPORT_PIGEON bones=76", clips Flapping 1–14, Gliding 1–105, Takeoff 1–60, Landing 1–60, Standing Idle 1–150. Imported into a temporary `Assets/_qa_b2` (deleted afterwards): **Generic, avatar valid, 76 skinned bones, 5 clips**, with names and lengths identical to the project's `Pigeon.fbx`; no metarig or `WGT-*`; **0 import errors or warnings**. The FBX bytes differ from the project copy only in the creation timestamp and the embedded absolute texture path, which depends on the output path. Project import (`BirdSetup.Verify()`): Pigeon Generic 76 bones, 5 clips, 0/0; Robin Generic 53 bones, 20 clips, 0/0; head at +Z for all 25 clips | qa | PASS |
| B3 | Robins in the brief's habitat cycle Idle/Peck/Hop/Preen/Scratch, out of step | `qa-bd-birds.xml` B3: 16 robins, 60 s, **every robin ≥ 10 distinct clips** (bar ≥ 3), 5364 neighbour-pair samples, max coincidence 0.3 s (the test's lockstep limit is 0.5 s; see Notes), every robin ≥ 3.02 m from the route (A4 ≥ 3 m). Placement (qa's own re-check of the saved scene, bushes counted as trees): 7 patches 3/2/2 trail + 3/2/2/2 meadow; nearest tree 6.1–25.8 m (6–30); trail centres 4.6–5.3 m off the route (4–6); meadow 12.5–23.4 m (10–25); rock ≥ 4.1 m (≥ 3); starts ≥ 3.68 m from trunks (> 1.5) and ≥ 3.36 m from the route (≥ 3). `BirdPlacer.Verify()` PASS. Positive control B3c caught lockstep, a single clip and a trail-standing robin | qa | PASS |
| B4 | Robin flushes at the brief's FID: Flutter→Fly, climbs away, lands ≥ 20 m away or despawns, no clipping | B4: flush trigger at **8.00 m** (A1 band 7.0–8.5), distance 8.0 → 11.0 m at 2 s → 28.6 m, **height gain +8.0 m**, landed **32.8 m** away and 32.7 m off the route, 0 collider overlaps, 0 terrain, 0 crown, facing 100% (min dot 0.99). Controls B4c/B4d caught facing/overlap/crown/trigger faults and a robin that never flies | qa | PASS |
| B5 | Crow flocks circle at the band, glide/flap rule, no overlap, never below canopy | B5 (60 s, 15 birds): **AGL 26.6–49.0 m** (R1/A2 gate 20–60), crown clearance ≥ 7.7 m (≥ 5), 0 below the crown, **min spacing 3.14 m** (≥ 1.5), 0 mesh overlaps, **flap rule 98.0%** of 8940 samples (≥ 95), glide share 55.7%, facing 100%. Control B5c caught spacing, wings, facing, band and crown faults. Facing report: Pigeon and Robin both **Keep** (L/R rule, head at +Z); both fly head-first | qa | PASS |
| B6 | Sighting numbers inside the bands, 3 seeds × 360 samples at 0.5 s | **qa's own run gates (lead ruling, no tolerance)**: `qa-bd-birds.xml` 17/17, `qa-bird-sightings.json`. All gating seeds pass every R1-final band; see the B6 table | qa | PASS |
| B7 | Noah's 3-minute walk, sky and ground each "right" | **Sky: right. Ground: right.** Rating of record, given by Noah for the **release-build re-walk**: "sky right, ground right" (plus "the sky birds looked great. the ground birds looked great"). 0 retunes. Release-build Player.log for the re-walk (qa-checked 04:49Z): Mono path `Builds/Windows/WashedAshorePOC_Data`; session launched after 04:42:42Z and ended 04:48:00Z (≤ 5 min 18 s; the log has no start stamp); Birds 16 robins / 3 flocks / 15 flock birds, wildlife 15/15 on the NavMesh, **0 errors or exceptions**, clean shutdown, 0 crash events. The first walk (dev test player) is superseded (D7) | Noah (via lead) + qa | PASS |
| B8 | WorldWalk + wildlife pass; Windows build exit 0; FPS drop ≤ 10%; web W7/W4 | **WorldWalk 5/5, wildlife 18/18, EditMode 42/42** (qa, 03:35–03:49Z). **Windows build**: qa rebuilt it, exit 0, provenance success 04:05:07Z, "Build Finished, Result: Success."; the lead's rebuild at 04:40:49Z gave the same `level0` (DB6DBE95). Smoke 65 s: exit 0, 0 errors, Birds 16/3/15, wildlife 15/15, "Using XInput". **FPS** (physical console, `SESSIONNAME=Console`, GPU 2% idle beforehand, 04:15–04:18Z, dev test player, 1920x1080, vsync off, 4 × 40 s): birds ON **4.738 ms (211.1 FPS)** vs OFF **4.705 ms (212.6 FPS)**, **drop 0.71%** (limit 10%). Runs: ON 4.732/4.746, OFF 4.701/4.708 ms, 8430–8507 samples each; ON had 16 robins + 15 flock birds active, OFF 0. **Web W4/W7: N/A by the standing rule** (Windows only; confirmed by the lead) | qa | PASS |
| B9 | Robin licence copied; git rule honoured; setup scripts in `Assets/Editor/` or `tools/` | The Robin `LICENSE.txt` in `Assets/ThirdParty/SoltorchGames/AmericanRobin/` is byte-identical to the vendor zip's (2627 B); the Pigeon public-domain dedication travels with the asset. **Repo `chz160/washed-ashore` PRIVATE** (`gh repo view`; the lead ruled the Robin folder is not git-ignored, and the private repo satisfies the rule). `vendor/` is git-ignored (`.gitignore:73`), 0 tracked files, 11/11 hashes unchanged since 00:48Z. Setup scripts: `Assets/Editor/Birds/{BirdImportSettings,BirdPlacer,BirdSetup}.cs` and `tools/blender/export_pigeon.{py,ps1}`, `tools/birds/extract_robin.ps1`. Runtime code is in `Assets/Scripts/Birds/`, tests in `Assets/Tests/`. No scratchpad, Temp or `C:\Users` path appears in any Assets `.cs`/`.asmdef` or tools file. LFS: 110 files, 0 pointers | qa | PASS |

### B6 detail (bands: final B1 + R1 final; gating seeds)

| Band | Bar | 101 | 202 | 303 |
|---|---|---|---|---|
| Flock in view | 30–65% | **31.7%** | 41.4% | 34.2% |
| Longest empty-sky gap | ≤ 45 s | 35.5 s | 18.5 s | 10 s |
| First flock sighting (gating, R1 final) | ≤ 30 s | 0 s | 0 s | 4.5 s |
| Ground birds met | 1.7–5.0 /min | 3.00 | 3.67 | 3.33 |
| Flush events / seen | 3–12 / ≥ 40% | 7 / 86% | 7 / 71% | 7 / 57% |
| Max birds in view | 3–20 | 12 | 10 | 11 |
| Samples with ≥ 15 in view | ≤ 5% | 0.0% | 0.0% | 0.0% |
| Glide share / flap rule | 40–75% / ≥ 95% | 55.9 / 97.3% | 55.7 / 98.0% | 54.1 / 97.6% |
| Flock AGL (A2 gate 20–60, ≥ crown + 5) | in band | 23.8–50.7 (crown 6.4) | 22.5–52.1 (8.4) | 24.1–50.5 (6.6) |
| Min flock spacing | ≥ 1.5 m | 3.15 | 3.17 | 3.39 |

- **Robustness** (non-gating): seeds 404–808 pass every band (flock-in-view 39.4–43.6%, gaps 11.5–31 s). 808's flush-seen is exactly 40%. Not seed-fragile.
- **Margin:** seed 101's flock-in-view is 1.7 points above the floor. bd-engineer's run gave the same 31.7%, so it is deterministic, not lucky.
- **History:** attempt 1 (original bands and tuning) failed the sky on all 3 seeds (18.3/26.9/38.6%; gaps 81/41/47.5 s). R1 final (no B7 slot) widened the gap band to ≤ 45 s, made first-flock gating, and retuned orbit 35–55 m, dwell 25–40 s and altitude 25–50 m AGL. Attempt 2 passed, and attempt 3 (a 4th flock) was not needed.

### Placement and approval gates

| Gate | Check | Result |
|---|---|---|
| Placing birds before Phase 0 approval | The first approval (rev 1) is 00:54:35Z, the final ruling 00:56:36Z. First bird write to `World.unity`: 01:32:37Z (the void placement); the placement of record is 01:41:09Z, after A5 (01:32:32Z). The 19:52-local Assets writes were imports and scripts (not placement; ruled allowed by the producer and the lead) | Clear |
| Outer bounds | Robins 16 (4–30); flocks 3 (1–4) of 6/5/4 (3–12); max in view ≤ 16 (≤ 20) | Clear |
| Vendor edited / Robin in a public repo | 11/11 vendor hashes unchanged; the repo is private | Clear |
| Same blocker 3 attempts without progress | Max 1 attempt per blocker (Safe Mode BD12, LFS pointers BD13, A5 scope BD17, B6 sky = 2 attempts with progress) | Clear |
| More than 3 assists | 0 | Clear |

## Assists and judgment calls

| What | Ruling |
|---|---|
| Noah at the console for B8 FPS and his B7 walk | Planned verification, not an assist |
| Noah pressed **Cancel** on the Windows Firewall prompts for the test players (confirmed by Noah). Windows then created inbound **Block** rules "playerwithtests" (temp path) and "birdsfpstestplayer" (TCP+UDP, Public) | Not an assist (lead). The rules are harmless and can be deleted on request; qa did not touch them |
| Noah asked for a fresh Windows build because the exe date read 6:14 PM | The lead rebuilt (04:40:49Z). The date was the player stub, which Unity copies unchanged (see D6). Not an assist |
| Retunes before B7 (R0 placement, A5, R1 sky) | Producer and lead: pre-B7 corrections use no B7 slot |

## Deviations and rulings to know

- **D1. Spec erratum:** the Robin has **20** clips, not 22. The vendor README agrees, and every B3/B4 clip exists (lead: erratum, not a failure).
- **D2. Known limitation:** Rigify B-bone segments are flattened in Unity, so the pigeon spine and neck are slightly stiffer (lead: accepted, non-blocking).
- **D3. B1 churn:** three B1 rulings (rev 1 AGL, rev 2 HAC, final AGL + A4), three A5 texts and two R1 texts. Each overwrote its key, and only the final records bind (BD10, BD14, BD21). qa initially relayed a stale reading twice (the rocks wording for A5 and the first-flock gate for R1) and withdrew both.
- **D4. Void placement:** the 01:32 seed-101 placement applied A5 too broadly (bushes ignored for the robin rules), and 1/7 robin patches broke the 6–30 m rule (BD17). It was voided and re-placed at 01:41:09Z. No evidence from before the re-place counts. The brief's L220 "every robin rule as placed" means the robin rules as written (lead).
- **D5. Brief R0 row:** "8/8 under A5" in the R0 table is bd-level's broad-scope run. The R0 evidence of record is bd-level's narrow-scope re-run (8/8 under the final A5) plus qa's placement re-check.
- **D6. Build dates:** in every build, `WashedAshorePOC.exe` (the player stub, sha 91A36FA1), `app.info` and `RuntimeInitializeOnLoads.json` keep their 18:14-local date because Unity copies them unchanged. Freshness is shown by `level0` and the other data files (04:41:02Z) and the provenance.
- **D7. B7 build:** the lead's relay said Noah walked the 04:40:49Z release rebuild; **the lead has since said that relay was mistaken** (an assumption). On disk, the only release-build Player.log session started after 04:41:11Z and ended 04:42:42Z (≤ ~91 s). The ~3-minute session, ended 04:39:33Z, ran in bd-engineer's dev test player `Builds/BirdsFpsTestPlayer/` (built 03:16Z). Its game content is identical (every runtime file predates 03:16Z, and the startup logs match line for line), but it is a development build with test assemblies included, and the spec asks for "the Windows build". Both logs show 0 errors. **Outcome: option (b).** Noah re-walked in `Builds\Windows\WashedAshorePOC.exe` (the lead's 04:40:49Z rebuild); the session ended 04:48:00Z with 0 errors. For that walk he rated "sky right, ground right" (the rating of record) and said "the sky birds looked great. the ground birds looked great". B7 rests on this release-build walk. The first walk in the dev test player is superseded.
- **D8. B3 interpretation:** the spec's "no two neighbours in the same clip at the same normalised time" is tested as no neighbour pair within 5% phase for ≥ 0.5 s. qa's run: max 0.3 s, 25 of 5364 pair samples momentarily coincident.
- **D9. ProjectSettings:** the accepted, disclosed change is `TagManager.asset` (layer 8 = Birds, plus Unity's serializedVersion 2→3 migration). The Test Runner's player builds left temporary PlayerSettings changes (splash logo, runInBackground, resizableWindow, fullscreenMode, managedCodeVariant) and a URP dev `rid` line. bd-engineer reverted these once; qa reverted them at 04:20Z with the lead's OK (`git diff` 0 lines on both). The URP global settings show line-ending-only `M` after the lead's rebuild (0 lines with `--ignore-cr-at-eol`).
- **D10. Accepted diffs:** `WashedAshore.Tests.Performance.asmdef` + `WashedAshore.Birds` reference; `Assets/Tests/PlayMode/Wildlife/AssemblyInfo.cs` (InternalsVisibleTo). The wildlife regression covered both. bd-level edited bd-techart's `WashedAshore.Birds.Editor.asmdef` (lead-approved ownership exception); it compiles (`unity recompile`: 0 errors).
- **D11. Process:** three baton overruns (BD12, BD15, BD16) and the LFS-pointer stop-the-line (BD13: the tree held 110 pointer files; it was restored before any placement).

## A8-regression item: wildlife A6 seed 202

bd-engineer's first birds regression run produced A6(202) gap = 46 s (band ≤ 45). Its raw JSON was lost (BD18). qa ruled it open, not a flake, until characterised:
- **Coupling ruled out by code:** the A6 raycasts mask Default only (`WildlifeTestKit.cs:192`); birds are on the Birds layer and have no colliders; no shared `UnityEngine.Random`.
- **qa characterisation** (`TestResults/qa-bd/a6-202/`, a temporary env-gated hook, deleted afterwards, BD24): 5 birds-ON and 5 birds-OFF runs interleaved. **The gap was 30 s in all 10.** Rates were 30.0–30.8% ON vs 29.7–30.6% OFF. qa's full wildlife run also gave 30 s.
- **Ruling:** the 46 s did not reproduce in 13 birds-present runs, so it is not a birds regression. It is recorded as a one-off flake. Pre-birds history for seed 202 is 29–30 s across 6 runs.

## Bugs (studio:qa)

| ID | Severity | Status | Summary |
|---|---|---|---|
| BD-BUG-1 | Minor (test harness) | Open, follow-up | The wildlife tests hard-code `TestResults/wildlife-sightings*.json` and `wildlife-a4.json`, so every regression run overwrites the wildlife pod's evidence files (BD18). Restored each time (sha-verified). Fix: an output-path parameter (lead: after the pod) |
| BD-BUG-2 | Minor (tooling) | Open, workaround | Test Runner player builds (TestRunnerApi, StandaloneWindows64) leave temporary PlayerSettings and a URP `rid` line in the project (BD27). Workaround: close the Editor, then `git checkout` both files |

No product bugs were found in the birds feature.

## Out of scope / not tested

- Bird audio, landing in trees, and interaction or hunting (spec scope).
- Web build (W4/W7): N/A by the Windows-only standing rule.
- The B6 visibility is foliage-aware (gating); the pitch-0 camera makes the sky metrics pessimistic for overhead flocks (brief note).
- B2 "clean checkout": the export scripts are not yet committed, so qa ran them from the working tree into a fresh output directory. The repeatability claim covers the script plus vendor `bird.blend`, not a fresh clone.
- The B8 FPS baseline is the Birds root ON/OFF in the same dev player (producer ruling, WL4 precedent). `Builds/Windows-nobirds` is a reference copy only.
- The (lit) DOIs in the brief are unverified (A3).

## Evidence files

- Brief and records: `_bmad-output/poc/bird-density-brief.md`; `studio:decisions` keys `greenlight/birds-brief`, `amendment/birds-brief-A5`, `amendment/birds-brief-R1`
- qa runs (`TestResults/qa-bd/`): `qa-bd-birds.xml` (17/17), `qa-bird-sightings.json`, `qa-bd-editmode.xml` (42/42), `qa-bd-worldwalk.xml` (5/5), `qa-bd-wildlife.xml` (18/18) + `qa-bd-wildlife-*.json`, `a6-202/` (10 runs), `b8-fps/` (`bird-fps.json`, `Player.log`, `TestResults.xml`, `PerformanceTestResults.json`), `qa-bd-step2-placement-verify.txt`, `qa-model-facing-report.json`, `qa-bd-b2-import.json`, `qa-bd-b2-export-hash.txt`, `qa-bd-smoke-player.log`, `qa-bd-build-windows.log`, and the eval scripts used
- Pod runs (dev evidence, not of record): `TestResults/birds-final-*.xml`, `birds-final-sightings.json`, `birds-b6-a1-*`, `birds-b6-a2-*`, `birds-regress-*`
- Builds: `Builds/Windows/WashedAshorePOC.exe` (+ provenance, lead rebuild 04:40:49Z), `Builds/lead-rebuild-windows.cli.log`; no-birds reference `Builds/Windows-nobirds/` (copied at 00:48Z, built 18:12–18:14 local)
- qa scratch (baseline hashes, records as received, comparison scripts): `C:\Users\noah\AppData\Local\Temp\claude\C--Tools-ruflo-studio\d30412c9-b209-4b7a-8cb4-af7df6b6cb3b\scratchpad\bd-baseline\`, `…\qa-bd\`
