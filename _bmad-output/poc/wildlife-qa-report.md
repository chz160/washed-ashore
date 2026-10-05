---
title: 'QA report: Washed Ashore wildlife spike'
owner: qa-lead (wl-qa)
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-wildlife.md
brief: _bmad-output/poc/wildlife-density-brief.md (approved; revisions R0, R1, R1-A)
friction_log: _bmad-output/poc/friction-log.md, section "Wildlife spike (2026-10-05)", entries WL1–WL116
verified: '2026-10-05T00:59 – 2026-10-05T14:00 -05:00 (05:59Z – 19:00Z)'
---

# Verdict: **PASS** (0 assists)

A1–A8 all pass. The last evidence was taken on the final code: after the WL-BUG-7 fix (animals had been running tail-first), on builds newer than every runtime change. No automatic-FAIL condition was triggered.

**Noah's A7 rating: "Right"** (0 of 2 retunes used). In his words: "the deer now run headfirst. no slidding popping of clipping that I saw right now. I think the animals turned and ran appropriatly. I walk around for maybe 3 minutes".

The assist count is 0, with one judgment call Noah may overrule (see "Assists"). Overruling it would make the verdict **PASS WITH ASSISTS (1)**.

**Process finding** (team lead's wording): "about 5 h 45 min idle; watcher-based handoffs failed twice across pods; mitigation: poll result files on disk for any step over 5 min" (WL63, WL66; a minor repeat in WL108).

qa produced every value marked "qa" by running the command itself. Values marked "pod" come from the owning agent's files; qa read the files and either re-ran the check or re-hashed the inputs. "main" means the lead session's browser measurement, and "Noah" means his own words, relayed by main.

## Evidence A1–A8

| # | Criterion | Actual value | Source | Result |
|---|---|---|---|---|
| A1 | Brief exists, cites sources, sets measurable targets, game-director approval | `wildlife-density-brief.md`: 33 unique URLs (7 marked weak); every INFERRED number labelled. Targets: population 15 (2 deer herds of 4, stag group of 3, 2 solitary foxes, 1 wolf pair); flee 35/40/25 m, wolf keep-distance 45 m; A6 bands as below. Approval: wl-director's ruling is stored at `studio:decisions` / `wildlife/brief-approval` (main, 06:03:42Z; qa retrieved it via CLI; the value is the ruling verbatim plus a "Why:" rationale sentence). The brief's "## Approval" quote is byte-identical to the ruling qa received from wl-director. Revisions R0, R1 and R1-A were each approved by wl-director; all three are quoted in the brief, and each quote is byte-identical to the text qa received. Pre-A7 tuning used 2 of the producer's 2 iterations (R0, R1), not A7 slots | qa | PASS |
| A2 | NavMesh baked; every animal on it | Edit-mode eval: NavMesh **6229 triangles** (14 299 vertices), surface `WildlifeNavMesh`, height mesh on; **15/15** animals within 0.5 m of the mesh (snap 0.000 m). Player (real scene load, after the WL-BUG-1 fix): "Wildlife: seed=101 agents=15 onNavMesh=15" and "t=5 onNavMesh=15 moving=11" in qa's final smoke, in Noah's A7 Player.log and in every A8 player run; "Failed to create agent" = 0 everywhere. A0b (all agents `isOnNavMesh` at load and after 5 s, seeds 101/202/303): Passed | qa | PASS |
| A3 | Population and grouping match the brief, within the outer bounds | Seed 101 (the authored scene and the A7 build): **Deer 4+4, Stag 3, Fox 1+1, Wolf 2 = 15** (outer bounds 6–40). Group radii 4.5/5.7 m (deer, ≤ 12), 7.0 m (stag, ≤ 15), 1.7 m (wolf, ≤ 10). Placement rules (brief 3.2 + R1): route distance 17.2–26.6 m (15–35); spawn distance 85–219 m (≥ 40; wolf 118.9 ≥ 100); centre distance ≤ 151 m (≤ 170); edge ≥ 113 m (≥ 30); closest pair of anchors 61.8 m (≥ 50); deer anchors 210.8 m apart (≥ 100); stag 120.1/92.2 m and wolf 192.0/89.7 m from the deer anchors (≥ 60); slopes 5.9–19.4° (within each species' limit); route arcs match the A6 JSON. Seeds 202/303 give the same counts per species (A6 JSON `population`) | qa | PASS |
| A4 | Movement matches animation; no sliding, floating, stuck or clipping > 5 s; **facing** (added after WL-BUG-7) | qa's final run (`qa-final-wildlife-a4.json`, 60 s, every agent): longest speed/animation mismatch ≤ 0.2 s (limit 1 s); deepest clip 0.334 m stag / 0.299 deer / 0.202 wolf / 0.163 fox (limit 0.35 m), clip run 0 s; float never (max gap −0.026 m); stuck 0; off-NavMesh 0; NavMesh vs terrain 0.000 m (positive control: lifted 1.000 m, off edge 0.354 m). **Facing** (the model's head direction vs travel, bar ≥ 95 % per agent, no grace window): deer 0.998, fox 1.000, stag 1.000, wolf 0.993; the A4c flipped-model control fails as required (0/41). Pre-fix repro: deer 0 % (`wl-bug7-prefix-repro.xml`) | qa | PASS |
| A5 | Deer and fox flee at the brief's threshold; wolves keep their distance | A5a deer: alert ~46 m, flee at 34.8 m, then distance grows 34.8→53.0 m; A5b fox: flee 24.5 m, 24.5→26.6 m; A5c wolves min distance 43.9 m while the player approaches (keep-distance 45 m, retreat at 6 m/s). Deer and stag **face the player while alert** (watch 100 %, min dot ≥ 0.89). All 3 Passed in qa's final run | qa (+pod trace) | PASS |
| A6 | Sighting metrics inside the brief's bands, 3 seeds | See the A6 table below. Evidence of record: `TestResults/wildlife-sightings.json` (pod, 13:18–13:20 local, brief revision R1, final code), **replicated by qa** (`TestResults/qa-wl/qa-final-wildlife-sightings.json`) within ±2 points. All 3 gating seeds pass every band | pod + qa | PASS |
| A7 | Noah's feel check | **"Right"**, verbatim quote above; ~3 min; physical console; the post-WL-BUG-7 Windows build. His Player.log: 0 errors, 15/15 on the NavMesh | Noah | PASS |
| A8 | No regression: WorldWalkTests, Windows build exit 0, FPS ≤ 10 % drop, web W7 ≥ 30 FPS | **WorldWalkTests 5/5** (qa; `WorldWalkTests.cs` SHA-256 = 00:59 baseline); EditMode 11/11 (qa). **Windows build**: exit 0, provenance success, "Build Finished, Result: Success." (`Builds/build-windows-bug7.log`, 13:34 local, newer than every runtime change); qa smoke alive at 20 s, 0 errors. **FPS** (run6, physical console, the post-fix code, runtime hashes 29/29 qa-verified): wildlife ON 4.754 ms (210.4 FPS) vs OFF 4.729 ms (211.4 FPS), **drop 0.51 %**; each ON run within the bound. **Web W7** (main, physical console, the post-fix `Builds/Web`): **median 59.9 FPS** (16.7 ms), p5 59.5 FPS, 1799 samples / 30 s at 1934x1085, vsync-capped; 0 console errors; 15/15 on the NavMesh | qa + techart + main | PASS |

### A6 detail (bands from the brief, unchanged since the original approval)

| Band | Bar | 101 | 202 | 303 |
|---|---|---|---|---|
| Sighting rate | 25–55 % | 38.1 % (qa 36.1) | 30.3 % (qa 31.4) | 32.5 % (qa 31.7) |
| Longest gap | ≤ 45 s | 21.0 s | 30.0 s | 28.0 s |
| First sighting | ≤ 30 s | 3.5 s | 4.0 s | 2.0 s |
| Max in view | 3–8 | 4 | 4 | 6 |
| Samples with ≥ 5 visible | ≤ 10 % | 0.0 % | 0.0 % | 0.8 % |
| Each species ≥ 2 consecutive samples | ≥ 2 | min 12 | min 11 | min 11 |
| Walker teleports (WL-BUG-5 fix) | 0 | 0 (2 sidesteps, ≤ 1.9 m) | 0 | 0 |

- **Method checks (qa):** `Score()` and the visibility block are byte-identical to qa's R0 copy (02:14 local). Camera 16:9, FOV 70, scan ±25° / 10 s, 360 samples at 0.5 s. A6 runs at timeScale 3; seed 101 at timeScale 1 gives 36.7 %, all bands pass (fidelity check).
- **Robustness** (non-gating, R1-A): 404 40.0 %, 505 37.8 %, **606 25.0 % with a 47.5 s gap (misses)**, 707 34.4 %, 808 47.8 %. One of five misses, so R1 is not seed-fragile (the rule is ≥ 2 of 5).
- **Director's caveat, verbatim:** "A6 visibility counts through canopy and bushes (trunk capsules only), so it is optimistic compared with the screen". The foliage-aware rate (non-gating) is 29.4 / 20.6 / 24.7 % for 101/202/303. Noah's "Right" is the arbiter of what the screen shows.

## Automatic-FAIL gates

| Gate | Check | Result |
|---|---|---|
| Placing animals before Phase 0 approval | First write to `Assets/` 01:15:01; director approval 01:03:48 (qa hashed Assets/ at 01:02:41, unchanged) | Clear |
| Outer bounds | Population 15 (6–40); max in view ≤ 6 in every gating run (≤ 8); every species seen in every run | Clear |
| Setup only in a scratchpad | All animal setup scripts are in `Assets/Editor/` (WildlifePrefabBuilder, WildlifePlacement, WildlifeBriefDefaults, WildlifePerfSetup, Level/WildlifePlacer); no scratchpad or Temp path in any Assets `.cs`/`.asmdef` | Clear |
| 3 attempts on one blocker with no progress | Max strikes on any blocker: 0. Every fix worked on attempt 1 (WL-BUG-1, -2, -7); the A8 console abort was interference (WL94) | Clear |
| More than 3 assists | 0 (see below) | Clear |

## Assists and judgment calls

| What | Ruling |
|---|---|
| Noah clicked "Allow" on the Windows Firewall prompt raised by the test player (prompt 01:23:22, click ~08:30 local, only when asked) | **Not an assist** (wl-producer, WL64): every evidence run before the click ran without the rule. **Judgment call, Noah may overrule**: the counter-reading is that it changed machine state and the later player runs ran under it. Counting it = PASS WITH ASSISTS (1) |
| Noah closed the A8 test-player window by accident (17:14Z) | Interference: friction, not a strike, not an assist (WL93/WL94) |
| Noah on Remote Desktop during W7 attempts | Environment friction, not an assist (WL88) |
| Noah's A7 walk, his WL-BUG-7 observation, his settings decisions | Planned / verification actions, not assists |

## Bugs filed (studio/qa)

| ID | Severity | Status | Summary |
|---|---|---|---|
| WL-BUG-1 | Blocker | Fixed, verified | In the player, NavMeshAgents enabled before `NavMeshSurface.OnEnable` loaded the data: 60 × "Failed to create agent". Fix: agents baked disabled and enabled in Start. Regression: A0b ×3 seeds + Player.log check |
| WL-BUG-2 | Blocker (A5) | Fixed, verified | Flee and wander targets sampled at the animal's own height and missed the NavMesh on slopes; the fox fled sideways. Fix: `OnGround` sets terrain height before sampling. Regression: A5a/b/c |
| WL-BUG-3 | Test | Fixed | The A5 start-point search used the herd's height on hills |
| WL-BUG-4 | Test | Fixed | A6 camera was 4:3 in batchmode; pinned to 16:9 |
| WL-BUG-5 | Test harness | Fixed, verified | The A6 walker jammed on a terrain rock (Rock_Medium_1) before W7 and teleported 10.21 m. Fix: sidestep and re-path, no teleport (0 teleports in the final runs) |
| WL-BUG-6 | Minor | **Fixed, verified (follow-up, 2026-10-05)** | Crash on quit in `Windows.Gaming.Input.dll` (0xc0000005) after a clean Unity shutdown, in sessions of about 2.5 min or longer (0/7 repros at ≤ 15 s, 3/3 at 200 s). Pre-existing, so not a regression. Root cause: the player's native gamepad layer used Windows.Gaming.Input (`windowsGamepadBackendHint` Default) with an XInput Logitech pad attached. Fix (Noah-approved, one setting): `Assets/Editor/Wildlife/WindowsInputBackend.cs` sets the hint to XInput (`ProjectSettings.asset` `windowsGamepadBackendHint: 0→1`, the only settings line changed). Verified with 0 crashes in 5 engineer + 3 qa cycles of ≥ 200 s using CloseMainWindow (0 Event 1000/1001, 0 new dumps, logs "Using XInput"); regression green. Trade-off: DirectInput/HID-only controllers are no longer read by the player's native gamepad path (no loss for this keyboard+mouse game) |
| WL-BUG-7 | Blocker (A4/A7) | Fixed, verified | All 4 species ran tail-first (the Quaternius models face −Z), found by Noah. Fix: runtime `modelYawOffset` 180 per species + prey `angularSpeed` 600°/s (deer/stag/fox), set via `WildlifeBriefDefaults`. Regression: a model-forward facing assertion + flipped-model control. The import-level fix is in the production backlog |

## Known diffs vs qa's 00:59 baseline (none are failures)

- **Assets:** 32 new files (wildlife code, prefabs, controllers, tuning, NavMesh, tests); 6 changed (World.unity, 4 `Animal_*` prefabs with `m_SkinnedMotionVectors` 1→0, and the URP global settings, which gain one dev-build `rid` line, ruled allowed in WL28); 0 removed. `Packages/manifest.json` unchanged.
- **ProjectSettings:**
  - `NavMeshAreas.asset` agent radius 0.4 / height 1.5 / slope 35 / climb 0.4 (intentional, A2).
  - `ProjectSettings.asset` `preloadedAssets` holds the Input System actions (left by a failed web build). **Noah: leave it.**
  - `managedCodeVariant: {Standalone: 3}` (a test-player build side effect; 3 = Release, the default). **Noah: leave it** (WL110). No agent edited it (an earlier text edit was blocked by a permission denial and was not worked around).
- `ProjectSettings.asset` `windowsGamepadBackendHint: 1` (XInput): the WL-BUG-6 fix, Noah-approved (WL113–WL115).
- **Leftover:** an empty `Assets/Resources/` (+ .meta), created by the performance-test package's build hook (WF68 pattern). Recommend deleting it.

## Out of scope / not tested

- No audio, combat or respawn (spec scope).
- A6 occlusion ignores foliage (director caveat above).
- The A8 FPS comparison uses the ON/OFF toggle in a development test player as "the build without animals" (producer ruling, WL4).
- Wolves still turn at 300°/s. They show 1–2 off-facing samples during the retreat pivot (98–99 %, above the bar) and were not raised by Noah.
- The web pointer-lock regression check (outside A1–A8) **passes** (WL109). Noah, verbatim: "I cliecked the game in the chrome tab and then pressed escape and it behaved correctly. I was in controlling the view, and when I pressed esc it released my mouse". The page log agrees: locked=true at 18:58:45.270Z, locked=false at 18:58:48.030Z (physical Esc), no pointerlockerror, "Click to play" shown again, 0 console errors. main's earlier failed lock (WL104) was an automation and focus artifact.

## Evidence files

- Brief: `_bmad-output/poc/wildlife-density-brief.md`. Sighting JSON (the hand-back artifact): `TestResults/wildlife-sightings.json`, `wildlife-sightings-fidelity.json`
- qa runs: `TestResults/qa-wl/qa-wl-final-playmode-wildlife.xml` (18/18), `qa-wl-final-worldwalk.xml` (5/5), `qa-wl-final-editmode.xml` (11/11), `qa-final-wildlife-a4.json`, `qa-final-wildlife-sightings*.json`, `qa-wl-final-player-smoke.log`, `a7-noah-Player.log`; earlier qa cycle `qa-wl-playmode-wildlife.xml` (17/17)
- Pod runs: `TestResults/wl-playmode-wildlife.xml`, `wildlife-a4.json`, `wl-bug7-prefix-repro.xml`; archives (not evidence): `*-R0*`, `*-preR0*`, `*-R1-0236*`, `*-0846-teleport*`, `*-dryrun-prefix*`, `*-pre-bug7*`, `*-bug7-offset-only*`
- A8: `TestResults/wildlife-fps.json` (run6, of record), `TestResults/wildlife-a8/run6-console-bug7/`; earlier runs run2–run5 (provisional/superseded)
- Builds: `Builds/Windows/WashedAshorePOC.exe` (+ provenance), `Builds/Web/` (+ `unity-build.provenance.json`), `Builds/build-windows-bug7.log`, `build-web-bug7.log`
- qa scratch (eval scripts, baseline, the director texts as received): `C:\Users\noah\AppData\Local\Temp\claude\C--Tools-ruflo-studio\d30412c9-b209-4b7a-8cb4-af7df6b6cb3b\scratchpad\wl-baseline\`, `...\qa-wl\`

## Follow-up: WL-BUG-6 fix (2026-10-05, Windows only)

Requested by Noah after the verdict. Standing rule: Windows only (`studio:decisions` platform/windows-only-default).

| Check | Actual value | Source | Result |
|---|---|---|---|
| Pre-fix control | 3/3 crashes (200 s sessions, CloseMainWindow): Event 1000 at 19:10:17, 19:13:42 and 19:17:06Z, WGI.dll 0xc0000005 @0x24b77; logs show "Using Windows.Gaming.Input" | wl-engineer, qa read the event log | Reproduced |
| Diff vs qa's 19:04Z pre-fix snapshot | Only the new `WindowsInputBackend.cs` (+ .meta) and `ProjectSettings.asset` `windowsGamepadBackendHint: 0→1`; manifest and runtime code unchanged. The URP global settings returned to the 00:59 baseline hash (the release build dropped the dev-only `rid` line, WL28) | qa | PASS |
| Engineer post-fix | 5 × 200 s CloseMainWindow on a fresh release build (exe sha 91a36fa1…): exit 0 each, 0 Event 1000/1001, 0 new dumps, "Using XInput", 0 0xc0000005/Exception lines | wl-engineer, qa read the logs + event log | PASS |
| qa post-fix | 3 × 206 s CloseMainWindow (20:23–20:33Z, exe sha 91A36FA1…): exit 0 each; 0 Event 1000/1001 for WashedAshorePOC; CrashDumps 5→5; each log "Using XInput", 0 WGI, 0 c0000005, 0 Exception, 0 "Failed to create agent", ends at a clean shutdown | qa | PASS |
| Regression | wildlife 18/18, WorldWalk 5/5 (W held → 9.38 m), EditMode 11/11 (20:03–20:04Z); Windows build exit 0, provenance success; smoke 15/15 on the NavMesh, moving=11 | wl-engineer, qa read the files | PASS |
| A8 | No re-run: the diff is one input-backend setting + an Editor script, with no runtime, prefab, scene or render change (producer + qa rule, WL113) | qa | Unchanged (run6 stands) |

The verdict is unchanged: **PASS** (0 assists). No assists in the follow-up. Strikes: 0.
