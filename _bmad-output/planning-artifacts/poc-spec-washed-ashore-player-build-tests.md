---
title: 'POC Spec: Tests in built players + automated player probes'
status: ready-for-pod
owner: John (PM)
created: '2026-10-21'
inputs:
  - Assets/Tests/PlayMode/**            # 64 files, InputTestFixture-driven
  - Assets/Tests/Performance/BirdFpsPlayerBuildOnly.cs
  - Assets/Scripts/Perf/BellsBendFpsWalk.cs, FarViewProbe.cs
  - tools/perf/farview_matrix.ps1
  - docs/physics-guidelines.md
---

# POC Spec: Tests in built players + automated player probes

**The question this answers:** can Ruflo's agents check **real Windows and Linux builds** automatically, with one command, a results file and an exit code? That means running a smoke set of our existing PlayMode tests inside built players, and running the `-bbFpsWalk` probe against pass thresholds instead of copying JSON by hand.

**Why:** Editor Play mode hid WL-BUG-1, which only a player build exposed (`_bmad-output/poc/friction-log.md:384`). The FPS-walk arguments and the `[FPSJSON]` line have "no test covers it" (`_bmad-output/qa/remove-web-qa-report.md:14`). Player QA steps today are manual (`player-character-qa-report.md:114-123`).

## What exists today (checked 2026-10-21)

- **Unity Test Framework 1.8.0** with 35 EditMode and 64 PlayMode test files. Most PlayMode suites drive the player with `InputTestFixture` virtual devices, for example `WorldWalkTests`, `TowerWalkTests`, `WorldBoundsSweepTests`, `WaterSwimTests` and `LookoutFixture`. Only one `[Category]` is in use (`LookoutProbe`).
- **UTF can run PlayMode tests in a player:** `-testPlatform StandaloneWindows64 | StandaloneLinux64 | StandaloneOSX` builds a player with the tests, runs it, and writes NUnit XML through `-testResults`. The Editor and player talk over a network socket (PlayerConnection). **A Windows firewall dialog has blocked that socket before** (friction-log row WL60). Unity gives no standard exit codes, so the XML is the verdict.
- **`BirdFpsPlayerBuildOnly.cs`** already builds a test player without running it (`ITestPlayerBuildModifier`).
- **Probes:**
  - `BellsBendFpsWalk` (`-bbFpsWalk`, `-waterFpsWalk`, plus `-bbFishOff`, `-bbWildlifeOff`, `-bbBirdsOff`, `-bbBodyOff`) writes `bells-bend-fps.json` and a `[FPSJSON]` line, then quits.
  - `tools/perf/farview_matrix.ps1` is the pattern to copy: arms, a refusal check, results under `TestResults/`, and exit codes 0, 1 and 2.
- **Linux and Mac build support are installed** (#14). There's no CI and no `.github/workflows`.
- **Launching a visible player window needs team-lead's OK** (`tower-qa-plan.md`). Headless runs don't.

## Phase 0: short research (gate)

**platform-tools-engineer** leads with **qa-lead**. Write `_bmad-output/poc/player-tests-plan.md` (≤ 2 pages) covering:
1. **Run in player vs. build then run:** use `-testPlatform` against building with the tests (as `BirdFpsPlayerBuildOnly` does) and launching it ourselves with results written to disk. Cover the firewall and socket risk on Windows, and what happens when the connection drops (tests pass, but no XML).
2. **Which tests can run in a player:** tests that use `UnityEditor` APIs, `AssetDatabase` or editor-only asmdefs can't. List each PlayMode suite as runs in player / Editor only, with the reason.
3. **Windowed vs. `-batchmode -nographics`:** which suites need a GPU (rendering, frame time, screenshots) and which can run headless.
4. **Thresholds** for the probe gate, taken from a **fresh baseline of 3 runs** on the current build, not from memory.

**Gate:** technical-director approves the plan. Noah approves the probe thresholds and which launches may open a visible window.

## Phase 1: pass criteria

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| E1 | **A `PlayerSmoke` category** on at least: world walk, tower walk (up time 26.6 s ±10%), swim entry and exit, the world-bounds sweep, the stance matrix, and one wildlife, bird and fish liveness check each. Only tests that Phase 0 marked as runs in player are included | Category list; diff |
| E2 | **The smoke set passes in a Windows player** (`StandaloneWindows64`) from one command line. NUnit XML lands in `TestResults/player-smoke/windows/`, and 0 tests are skipped for being unable to run in a player | XML; command log |
| E3 | **The smoke set passes in a Linux player** (`StandaloneLinux64`), headless where Phase 0 allows, on a Linux machine or VM. XML in `TestResults/player-smoke/linux/` | XML; Player.log |
| E4 | **One script** `tools/test/player_smoke.ps1 -Platform windows|linux` (plus a `.sh` for Linux) builds, runs and reads the XML. **Exit codes: 0 all pass, 1 a test failed, 2 no XML / crash / timeout, 3 refused** (another player running, or the firewall rule missing). It never reports pass without an XML file | Script; a forced-failure run giving exit 1, and a killed-player run giving exit 2 |
| E5 | **The firewall risk is handled.** The script checks for the PlayerConnection rule first and returns exit 3 with clear instructions **for Noah** to add it. The script never changes firewall rules itself | Script; a run with the rule missing giving exit 3 |
| E6 | **The probe gate:** `tools/test/probe_gate.ps1` runs `-bbFpsWalk` (and `-waterFpsWalk`) on the Windows build, reads `bells-bend-fps.json`, and checks it against the approved thresholds. It writes `TestResults/probe-gate/<run>/verdict.json` (metric, value, threshold, pass) and uses the same exit codes as E4 | Script; verdict JSON; one deliberately failing threshold giving exit 1 |
| E7 | **Probe arguments are tested.** An EditMode or PlayMode test covers argument parsing for `-bbFpsWalk`, `-waterFpsWalk` and the `-bb*Off` switches, and the `[FPSJSON]` line format | Test XML |
| E8 | **The tests are stable.** Run the smoke set 5 times in a row on Windows: 5 of 5 pass with no reruns. Any flaky test is fixed or taken out of `PlayerSmoke`, with a one-line reason | 5 XML files; a flake log |
| E9 | **Nothing else changes.** The full EditMode and PlayMode suites still pass in the Editor (except the known #19 failures), and no game code changes except what makes tests runnable in a player (asmdef platform settings, `#if UNITY_EDITOR` guards), each listed | Test XML; diff review |
| E10 | **Docs:** a "Testing built players" section in `README.md` or `docs/testing.md` (commands, exit codes, thresholds, firewall note), linked from `CLAUDE.md` | Diff |

## Automatic FAIL

- Changing gameplay tuning, thresholds after they're approved, or any test's assertions just to make it pass.
- A script reporting pass without an XML or JSON file.
- Changing Windows firewall or OS settings automatically.
- Launching a visible player window without team-lead's OK.
- Adding paid tools (AltTester Pro, GameDriver), CI or GitHub Actions. **GameCI is a later issue.**
- macOS player runs. The writer's Mac stays manual (#14).
- The same blocker 3 times in a row, or more than 3 assists.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`.

| Role | Owns |
|---|---|
| platform-tools-engineer | Phase 0 lead; player runs, scripts, exit codes, firewall check (E2–E6) |
| build-engineer | Test-player builds for Windows and Linux, Linux headless (E2, E3) |
| gameplay-engineer | Making suites runnable in a player, probe argument tests (E1, E7, E9) |
| qa-lead | The smoke set, the stability runs, the verdict (E1, E8) |
| technical-director | Gate approval, review of runtime code touched (E9) |

Related: #13 (multiplayer test harness later), #14 (platforms), #19 (known PlayMode failures).
