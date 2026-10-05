---
title: 'QA report: Washed Ashore walk-around POC'
owner: qa-lead
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-walkaround.md
checklist: _bmad-output/poc/acceptance-checklist.md
friction_log: _bmad-output/poc/friction-log.md
verified: '2026-10-04T22:45-22:55 -05:00'
---

# Verdict: **PASS** (0 assists)

The verdict is final. **P8.4 is signed off:** on 2026-10-05, Noah walked around `Builds/Windows/WashedAshorePOC.exe` and reported "windows build also worked. same as the web version", meaning WASD, mouse look and rendering worked with no magenta. That covers P5.3. The walk-around was the planned final sign-off, not an assist. Note: by then the web-build spike's W8 had rebuilt the player data (05:33:59Z), so the build walked was almost certainly the post-web Windows build. It has the same player behaviour, with the mouse lock gated by platform.

qa checked every row below independently with the baton held. Each value comes from a command qa ran itself; none is copied from an agent's self-report. Two checklist probes did not match how the stock URP template behaves (P1.4, P8.3). Each is ruled below with evidence. Neither hides a functional failure.

## Evidence P1–P9

| # | Check | Actual value (qa-run) | Result |
|---|---|---|---|
| P1.1 | Create command in log | `engineer-commands.log` line 9: `unity projects create washed-ashore --path E:\GitHub --editor-version 6000.6.4f1 --template com.unity.template.urp-blank --with-pipeline --no-cloud` exit 0 (63.7 s). The direct create into `washed-ashore` exited 6 (folder not empty), so the CLI-created project was moved into `E:\GitHub\washed-ashore` (friction #4) | PASS (deviation: create-then-move) |
| P1.2 | ProjectVersion.txt | `m_EditorVersion: 6000.6.4f1` (rev 12bfff696524) | PASS |
| P1.3 | manifest.json | `com.unity.render-pipelines.universal` 17.6.0, `com.unity.pipeline` 0.8.0-exp.1, `com.unity.inputsystem` 1.20.0 | PASS |
| P1.4 | `GraphicsSettings.defaultRenderPipeline` | `null`. The stock URP template assigns URP per quality level instead: `0:Mobile=Mobile_RPAsset`, `1:PC=PC_RPAsset` (both `UniversalRenderPipelineAsset`); `currentRenderPipeline=PC_RPAsset`; Standalone default quality = 1 (PC). No quality level can fall back to Built-in | PASS by ruling (probe mismatch, friction #28) |
| P1.5 | `QualitySettings.renderPipeline` | `UniversalRenderPipelineAsset` | PASS |
| P1.6 | `unity status` | `E:\GitHub\washed-ashore`, 6000.6.4f1, pid 16720 (later reopened), state `ready`, port 7800 | PASS |
| P2.1 | Build scenes | `Assets/Scenes/World.unity:True` (only entry); `Assets/Scenes/` contains only World.unity | PASS |
| P2.2 | UI objects | Canvas 0, UIDocument 0, EventSystem 0 | PASS |
| P2.3 | Scene roots | `Sun`[Directional Light], `Terrain`[Terrain], `PlayerSpawn`, `Player`[tag Player, CharacterController; camera child]. Exactly 1 Player-tagged object | PASS |
| P2.4 | Splash | custom logos 0; `SplashScreen.show=False` | PASS |
| P3.1 | Terrain size | 512 x 512 m (height scale 80 m), 1 terrain | PASS |
| P3.2 | Height range | **68.0 m** (min 4.0, max 72.0) | PASS |
| P3.3 | Layer count | 3 | PASS |
| P3.4 | ambientCG source | `Grass004_1K-JPG_Color.jpg`, `Ground037_1K-JPG_Color.jpg`, `Rock030_1K-JPG_Color.jpg` under `Assets/World/Terrain/Textures/` | PASS |
| P3.5 | Painted coverage | Grass004 46.6 %, Ground037 32.6 %, Rock030 20.8 % | PASS |
| P3.6 | Terrain shader | `Universal Render Pipeline/Terrain/Lit` | PASS |
| P4.1 | Tree types | **6**: CommonTree_1, CommonTree_3, Pine_1, Pine_3, TwistedTree_1, DeadTree_1 | PASS |
| P4.2 | Rock/bush types | **4**: Rock_Medium_1, Rock_Medium_2, Bush_Common, Bush_Common_Flowers | PASS |
| P4.3 | MegaKit provenance | All 10 prefabs under `Assets/ThirdParty/Quaternius/NatureMegaKit/Prefabs/`, meshes from `.../NatureMegaKit/FBX/<Name>.fbx` | PASS |
| P4.4 | Tree instances | **495** trees (735 total, including 240 rocks and bushes) | PASS |
| P4.5 | Instances per rock/bush | 60 / 50 / 80 / 50 | PASS |
| P4.6 | Detail layers | 3 (Grass_Common_Short, Grass_Wispy_Short, Clover_1; mesh details) | PASS |
| P4.7 | Detail density | 116 882 / 113 413 / 1 514 | PASS |
| P4.8 | Off-terrain instances | 0 | PASS |
| P5.1 | Non-URP materials | **offending = 0** of 89 checked (scene renderers, terrain template, tree and detail prototype prefabs, every Material under `Assets/`) | PASS |
| P5.2 | Null material slots | 0 | PASS |
| P5.3 | No magenta in player | Noah's walk-around, 2026-10-05: no magenta | PASS |
| P6 | Move / look / gravity / no fall-through | Covered by P7 b, c, e. Controller reads `<Keyboard>/wasd` and `<Mouse>/delta` through Input System actions, applies gravity -20 m/s² and moves via `CharacterController.Move` | PASS |
| P7 | `unity test --mode PlayMode` | qa re-run, Editor closed, 22:47. **Filtered** (`--filter WashedAshore.Tests.PlayMode`): exit 0, `result="Passed" total=5 passed=5 failed=0`. **Unfiltered** (spec command): exit 0, `total=9 passed=7 failed=0 skipped=2` (the 2 skips are Input System's own Windows-only integration tests, pulled in by `testables`; run result reads `Skipped:Ignored`, friction #23) | PASS |
| P7a | A_LoadsWorld | Passed (0.20 s) | PASS |
| P7b | W for 2 s ≥ 3 m | Passed: **9.38 m** (2.22 s) | PASS |
| P7c | Grounded at 3 s, not under terrain | Passed (3.01 s): `CharacterController.isGrounded` and y ≥ SampleHeight − 0.1 | PASS |
| P7d | Frames advance | Passed: **9 522 frames** in 1 s realtime | PASS |
| P7e | Mouse look (non-blocking) | Passed: yaw change > 1° from mouse delta (+100, 0) | PASS |
| P7 method | InputTestFixture, LogAssert default | Confirmed in `Assets/Tests/PlayMode/WorldWalkTests.cs`: `InputTestFixture`, `Press(keyboard.wKey)`, `Set(mouse.delta…)`; no transform teleports; `LogAssert.ignoreFailingMessages` not used | PASS |
| P8.1 | `unity build` exit code | `unity build E:\GitHub\washed-ashore --target StandaloneWindows64 --output-path ...\Builds\Windows\WashedAshorePOC.exe` exit 0 (437 s). Envelope `success: true`; log `Build Finished, Result: Success.`; provenance `outcome: success, exitCode: 0`. qa verified artifacts and verdicts and did not rebuild | PASS |
| P8.2 | Artifacts | `WashedAshorePOC.exe` (667 KB), `WashedAshorePOC_Data/`, `UnityPlayer.dll` present | PASS |
| P8.3 | 20 s windowed launch smoke (qa-run) | `-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile`: **alive at 20 s**, D3D11 on RTX 4060 Ti, 0 `Exception` lines. 4 lines match `Shader .* is not supported`, all URP-internal post-process shaders stripped as unused (`GaussianDepthOfField` x2, `BokehDepthOfField`, `PaniniProjection`). The project's volume profile has DoF `mode=Off` and Panini `distance=0`, so those passes never run. These are not material shaders and cannot cause magenta | PASS by ruling (friction #31) |
| P8.4 | Noah's 60-second walk-around | Done 2026-10-05: "windows build also worked" | PASS |
| P9.1 | Compile errors | `unity recompile`: `up_to_date`, 0 errors / 0 warnings; `error CS` count 0 in Editor.log and in both qa test logs | PASS |
| P9.2 | Console errors during test run | Both qa PlayMode logs: 0 Error/Exception/Assert entries (only stack frames of the tests' own `Debug.Log`). Live console after reopen: 0 errors / 0 warnings, `compilationFailed=false`. Note: `unity logs` (the source the spec names) reads the Hub log, not the Editor's, so qa used the console, recompile and log files instead (friction #29) | PASS |

## Automatic-FAIL gates

| Gate | Check | Result |
|---|---|---|
| F1 | `Packages/manifest.json`: no `scopedRegistries`; all 11 registry packages in `packages-lock.json` resolve from `https://packages.unity.com`; the rest are `builtin`. No Asset Store package, no Starter Assets, no third-party MCP (`com.unity.pipeline` is Unity's own, specified in the spec) | Clear |
| F2 | Assists: 0. No agent reported a person touching Unity or `Assets/` | Clear |
| F3 | 3-strike blockers: 0. No Safe Mode, no hung `eval`, no domain-reload loop | Clear |

## Stretch goals (do not affect verdict)

| # | Result |
|---|---|
| S1 (animals + NavMesh) | **Not met.** `NavMeshAgent` count in World = 0; `NavMesh.CalculateTriangulation` = 0 vertices. Techart imported and rescaled the animal prefabs, but they were never placed or baked |
| S2 (wind sway) | **Not met.** No `WindZone`; techart skipped it on purpose (a custom shader would break the P5 URP-prefix rule; script-authoring a Shader Graph was out of time budget) |

## Assist count

**0.** The vendor zip drop before kickoff was the allowed manual step.

## Notes for producer and td

- `PlayerSettings.productName` is still `washed-ashore`, left over from the create-then-move workaround. The player window title and `persistentDataPath` use that name. This is cosmetic and doesn't affect any criterion.
- Security: the Hub-launched Editor process command line carries `-accessToken` in clear text (seen while disambiguating `Unity.exe` processes). qa has not recorded the value in any artifact (friction #32).

## Evidence files

- qa test runs: `E:\GitHub\washed-ashore\TestResults\qa\qa-playmode-washedashore.xml`, `qa-playmode-all.xml`, `qa-playmode-*.log`, `qa-test-*.json`
- qa player smoke: `E:\GitHub\washed-ashore\TestResults\qa\qa-player-smoke.log`
- Build: `E:\GitHub\washed-ashore\Builds\unity-build.json`, `Builds\build-windows.log`, `Builds\Windows\WashedAshorePOC.provenance.json`
- Command log: `E:\GitHub\washed-ashore\_bmad-output\poc-logs\engineer-commands.log`
- qa eval snippets: `C:\Users\noah\AppData\Local\Temp\claude\C--Tools-ruflo-studio\d30412c9-b209-4b7a-8cb4-af7df6b6cb3b\scratchpad\qa_p1p2.cs`, `qa_p3p4.cs`, `qa_p5.cs`, `qa_vol_s1.cs`
