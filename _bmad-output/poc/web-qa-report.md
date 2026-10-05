---
title: 'QA report: Washed Ashore web build spike'
owner: qa-lead (web-qa)
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-web-build.md
checklist: _bmad-output/poc/web-acceptance-checklist.md (with W8.6 amendments 1–6)
friction_log: _bmad-output/poc/friction-log.md, section "Web build spike (2026-10-04)", entries WF1–WF76
verified: '2026-10-04T23:19 – 2026-10-05T00:55 -05:00'
---

# Verdict: **PASS WITH ASSISTS (1)**

W1–W8 all pass. One assist was counted: Noah paused a Macrium Reflect NAS backup that was saturating the line during the module download (WF26). Two other human actions were ruled **not** assists by web-producer. They are listed under "Judgment calls" so Noah can overrule them. Overruling either one makes the count 2, which is still a pass. Overruling both makes it 3, which is a FAIL under AF4.

**Noah's W6 walk-around is done, not pending.** Every W6 item is an explicit PASS in his own words (WF60). He also walked the Windows player and reported it "also worked. same as the web version". It was almost certainly the post-web W8 build (WF61).

qa produced every value marked "qa" below by running the command itself. Values marked "main" come from the lead session's browser tools, which only main has. Values marked "Noah" are his own words, relayed by main.

## Evidence W1–W8

| # | Check | Actual value | Source | Result |
|---|---|---|---|---|
| W1.1 | Module ID | `webgl` (`unity install-modules -e 6000.6.4f1 --list`, exit 0) | web-build log, qa read | PASS |
| W1.2 | Install via CLI | `unity install-modules --editor-version 6000.6.4f1 --module webgl --accept-eula --yes --non-interactive --wait-timeout 1800` exit 0, 1565.7 s, `{"completedUids":["webgl"],"status":"installed"}` (`poc-logs/install-webgl.txt`, qa read). The EULA was accepted with `--accept-eula` under auto-mode permissions, and **Noah has since confirmed he approves it** (WF25). No UAC prompt appeared | qa | PASS |
| W1.3 | `PlaybackEngines\WebGLSupport` | Absent at 23:19 (qa baseline); present after the install (5.7 GB, contains `UnityEditor.WebGL.Extensions.dll`) | qa | PASS |
| W1.4 | `IsBuildTargetSupported(WebGL)` after Editor restart | `True` | qa eval | PASS |
| W2.1 | Build exit code | `unity build --profile "Assets/Settings/Build Profiles/WebGL.asset" --output-path Builds\Web`: exit 0, 204.4 s; provenance `outcome success, exitCode 0`; `build-web-final.log` "Build Finished, Result: Success." Form: the CLI can't build WebGL with a bare `--target WebGL` (it needs a Build Profile), so web-producer approved this form as meeting W2 (WF4, WF6) | qa (provenance + log) | PASS |
| W2.2 | Artifacts | `Builds/Web/index.html`; `Build/Web.loader.js`, `Web.framework.js.unityweb`, `Web.wasm.unityweb`, `Web.data.unityweb` | qa | PASS |
| W2.3 | Compression | `Brotli` | qa eval | PASS |
| W2.4 | Decompression fallback | `True` (files named `.unityweb`; plain `http.server` needs no headers) | qa eval, main | PASS |
| W2.5 | Build scenes | `Assets/Scenes/World.unity:True` only | qa eval | PASS |
| W2.6 | Graphics API | WebGL2 only (`OpenGLES3`, automatic off); Chrome reported a WebGL 2.0 context. No WebGPU | qa eval, main | PASS (recorded) |
| W2.7 | Non-development | `EditorUserBuildSettings.development=False`; the release profile has no development flag; release IL2CPP config in the log | qa | PASS |
| W3.1 | Code review | web-td **APPROVED**, re-issued after the rework, covering the current `PlayerController.cs` (statics `ShouldLockOnStart` / `ShouldRelockOnClick` / `ShouldApplyMouseLook`), `MouseLockGateTests.cs` and the ClickToPlay template. qa's read of the diff against its 23:20 pre-change copy: Windows still locks on Start, the click relock is gated to WebGL, and mouse look always applies off WebGL | web-td + qa | PASS |
| W3.2 | Look gated while unlocked on WebGL | `ShouldApplyMouseLook(WebGLPlayer, None)=false`, tested | qa | PASS |
| W3.3/3.4 | EditMode tests | `unity test --mode EditMode` exit 0: **Passed 11/11, failed 0**. All the W3.3 cases are present (ShouldLockOnStart x5, ShouldRelockOnClick x3, ShouldApplyMouseLook x3). Coverage note from web-td: the inline "already locked / no click" logic in `Update` has no unit test and is covered by W3.5 | qa | PASS |
| W3.5 / C5 / C6 | Browser lock behaviour | main: a trusted click locked the pointer on the canvas and the text hid; `exitPointerLock()` brought the text back; a click 2 s later relocked with no console error. A synthetic Esc can't reach Chrome (WF55), so **Noah checked the physical Esc release and the click recapture: PASS** | main + Noah | PASS |
| C1 | Body text before the click | `innerText` exactly "Click to play" | main | PASS |
| C2 | No interactive elements | 0 button/a/img elements | main | PASS |
| C3 | Template | No background image, progress bar or logo markup; `<link rel="icon" href="data:,">` present (qa read `Builds/Web/index.html`). The tab title is `washed-ashore` (the productName left over from the first spike, not page text) | qa | PASS |
| C4 | In-scene UI | Canvas 0, UIDocument 0, EventSystem 0 | qa eval | PASS |
| C7 | Splash | `show=False`, 0 logos | qa eval | PASS |
| W4.1 | Compressed `Build/` ≤ 50 000 000 B | **20 241 738 B** (data 12 731 319, wasm 7 325 719, loader 118 806, framework 65 894). Before the overrides: 41 322 112 B (−51 %) | qa (byte sum) | PASS |
| W4.2 | WebGL overrides | 27/27 Texture2D overridden, max 1024, compressed (DXT1Crunched x12, DXT5Crunched x15); fails = [] | qa eval | PASS |
| W4.3 | Default/Standalone texture settings | 26 `.meta` files changed only in the WebGL block. `URP.png.meta` (a template tutorial icon) was also upgraded to the 6000.6 importer schema when it was rewritten, with Default/Standalone **values unchanged** (WF66) | qa (`meta_diff.py`) | PASS |
| W4.4 | Source images | 31/31 SHA-256 in `web-baseline.json` unchanged (27 images, scene, TerrainData, tests) | qa | PASS |
| W5.1/5.2 | Served locally, loads to gameplay | `C:\Python313\python.exe -m http.server 8090 --bind 127.0.0.1 -d E:\GitHub\washed-ashore\Builds\Web`, at `http://127.0.0.1:8090/` (port 8080 is blocked on this machine, WF54). Chrome 154 loaded to gameplay | main | PASS |
| W5.3 | Console errors | **0 errors, 0 exceptions** from load through the walk, release and relock | main | PASS |
| W5.4 | favicon | No 404 | main | PASS |
| W5.5 | Warnings | 4 logs (GaussianDoF x2, BokehDoF, Panini; on the benign list) + 1 warning for "Edge Adaptive Spatial Upsampling" (URP FSR upscaler). qa ruled the FSR warning benign: it's a URP-internal shader, not a project material, so it can't cause magenta (WF56) | main, qa ruling | PASS |
| W5.6 | No `[FrameLog]`, no dev watermark | 0 `[FrameLog]` lines in the console | main | PASS |
| W6.1–6.7 | Noah's walk-around (Chrome, final build) | "WASD worked. Esc worked. mouse look worked. as far as I can tell everything worked." + "Clicking to recapture the mouse after releasing it worked. No magenta textures. Terrain, trees and grass rendering". main's agent pass: "Click to play" was the only text, no UI at any point, terrain/trees/rocks/bushes/grass rendered with no magenta (screenshots) | Noah + main | PASS |
| W7 | Median ≥ 30 FPS at ≥ 1920x1080 over 30 s | **Median 16.7 ms = 59.9 FPS**; p95 16.8 ms (p5 59.5 FPS); worst 16.9 ms; 1799 rAF samples over 30.00 s; canvas 1934x1085 at DPR 1; pointer locked; W held for 30 s, and screenshots show the player climbed from the spawn up a hillside. Pinned at the 60 Hz vsync cap. **No grass or tree cuts were needed.** Method: web-producer withdrew Option A after the linker flake (WF47) and replaced it with main's rAF sampler on the **final release build**, so nothing was added to the build | main | PASS |
| W7.6 | Dev logger absent | `FrameTimeLogger.cs`, `Assets/Scripts/Dev/` and the `WebGL Dev` profile were deleted (0 references in Assets/). qa decompressed all 4 `Build/` files (Node `zlib.brotliDecompressSync`): **0 hits** for `[FrameLog]`/`FrameTimeLogger`/`FrameLog`. Positive control `Builds/WebDev`: 4 `[FrameLog]` hits, so the scan works | qa | PASS |
| W8.1 | Windows build | `unity build --target StandaloneWindows64 --output-path Builds\Windows\WashedAshorePOC.exe` exit 0, 37.5 s, started 00:33:31; provenance success; log "Build Finished, Result: Success.". The only project file newer than the build start is ProjectSettings.asset (00:34:02, Input System's `preloadedAssets` add/remove during that build; content = the 3 WebGL items only). The player's `WashedAshore.Gameplay.dll` (00:33:59) contains `ShouldLockOnStart`, i.e. post-web code | web-build log, qa verified | PASS |
| W8.2 | Artifacts + 20 s smoke | exe, `_Data/`, `UnityPlayer.dll` present. qa launch: alive at 20 s, D3D11, 0 Exception lines, only the 4 P8.3 URP-internal shader lines | qa | PASS |
| W8.3 | PlayMode, filtered | exit 0, **Passed 5/5, failed 0** (A–E all Passed); WorldWalkTests.cs SHA-256 matches the baseline | qa | PASS |
| W8.4 | PlayMode, unfiltered | exit 0, total 9, passed 7, **failed 0**, skipped 2 (Input System Windows-only, #23) | qa | PASS |
| W8.5 | EditMode | Same run as W3.4: 11/11 | qa | PASS |
| W8.6 | Baseline diff (AF1) | See below | qa | PASS |

### W8.6 / AF1 diff detail

The authority is qa's byte-level pre-change copy taken at 23:20, before any web write (web-build's first write was at 23:21:39), cross-checked against `web-baseline.json` and web-build's settings snapshot.

| Item | Result |
|---|---|
| ProjectSettings/ (all 27 files) | Only `ProjectSettings.asset` differs, in exactly 3 WebGL items: the `WebGLSupport` graphics-API entry, `webGLTemplate APPLICATION:Default → PROJECT:ClickToPlay`, and `webGLDecompressionFallback 0→1`. `preloadedAssets: []`: it cleared after each successful build (rule (b), first branch). The one time it stuck was after the failed dev build, and techart restored it by text edit with the Editor closed (WF45) |
| QualitySettings.asset | Byte-identical. Standalone default level 1 (PC), WebGL 0 (Mobile); the Mobile level excludes Standalone |
| PC_RPAsset, PC_Renderer, Mobile_Renderer, DefaultVolumeProfile | Byte-identical |
| Mobile_RPAsset | `m_RenderScale 0.8→1`; the URP build-time keys `m_Prefilter*` / `m_PrefilteringMode*`; and 3 keys at their default values (`m_ShadowSmallMeshScreenPercentages {0,0,0,0}`, `m_Light2DPrefilteringMode 0`, `m_Light2DKeptVariantCombos []`). All allowed by web-td rulings (amendments 5 and 6) |
| World.unity, TerrainData, WorldWalkTests.cs, 27 images | SHA-256 unchanged; trees 735, details 116882 / 113413 / 1514 |
| Shared PlayerSettings | Linear colour space, Win64 D3D11+D3D12, product/company and splash unchanged |
| Active build target | StandaloneWindows64 at the end of the run |
| New files | `PlayerController.cs` (changed), `Assets/Tests/EditMode/*`, `Assets/WebGLTemplates/ClickToPlay/*`, `Assets/Settings/Build Profiles/WebGL.asset` (BuildTarget 20 = WebGL, no scene/define/player-settings overrides). All on the allowed list |
| Not on the list, ruled harmless | (1) An empty `Assets/Editor/UnityCliTemp-a22b45fd…/` (+meta) left by `unity build --create-profile` (WF67). (2) An empty `Assets/Resources/` (+meta) left by `com.unity.test-framework.performance`'s build hook, which runs on every player build on both platforms (WF68). Both are empty and have no runtime effect. Recommend deleting them; neither changes the Windows look or behaviour |

## Automatic-FAIL gates

| Gate | Result |
|---|---|
| AF1: Windows look or behaviour changed | **Clear.** The diff is above. Noah's Windows walk-around: "same as the web version" |
| AF2: 3-strike blocker | **Clear.** UnityLinker AccessViolation on the dev build, attempt 1; attempt 2 on identical inputs succeeded, so the producer closed it as a transient flake (WF44, WF48). The slow module download was one attempt that kept progressing, not a strike |
| AF3: non-CC0 / menu / HUD | **Clear.** No new packages (manifest unchanged since 22:33) and no new assets. The only UI is the "Click to play" HTML line (C1–C4) |
| AF4: more than 2 assists | **Clear: 1 assist** |

## Assists and judgment calls

| # | What | Ruling |
|---|---|---|
| Assist 1 | Noah paused a Macrium Reflect NAS backup during the webgl download (~23:45). Attribution note: web-build's samples show most of the speed-up (8 %→70 %) happened before the pause; only the last ~30 % came after it. The spec counts the action, not the effect | **Counted** (web-producer) |
| Judgment call A | Noah reconnected the Claude-in-Chrome extension so main could run the browser checks | **Not an assist** (web-producer): tool setup, like a permission prompt; Unity, the project and the build were not touched. **Noah may overrule.** Counting it would make 2 assists, still a pass |
| Judgment call B | Noah opened the Unity Editor (pid 61548) from the Hub after his walk-around, on his own initiative | **Not an assist** (web-producer): it was for him, not the pod, and nothing in the pipeline needed it. The literal AF4 wording ("clicking anything in Unity or the Hub") would count it. **Noah may overrule.** Counting it would make 2 assists, still a pass |
| Not assists by spec | Noah's EULA approval; Noah's permission for qa to close/reopen his Editor; Noah's W6 walk-around | — |

If Noah overrules **both** A and B, the count is 3 and the verdict becomes **FAIL (AF4)**.

## Notes for producer and td

- The spec's literal W2 command (`unity build --target WebGL`) doesn't work in this CLI version, which needs a Build Profile for WebGL. A `--profile` build also leaves the active target on WebGL until it's switched back (WF33).
- The CLI's provenance file recorded `exitCode: 0` on a failed build (WF44). The CLI's own process exit code was correct (6), so gate on the exit code, the "Build Finished" log line and `outcome` together.
- `unity open` reopening on Untitled (first-spike friction #22) is confirmed to recur (WF75).
- `productName` is still `washed-ashore`, which now also shows as the browser tab title.

## Evidence files

- qa test runs: `E:\GitHub\washed-ashore\TestResults\qa\qa-web-editmode.xml`, `qa-web-playmode-washedashore.xml`, `qa-web-playmode-all.xml` (+ `*-editor.log`, `*.json`)
- qa Windows smoke: `E:\GitHub\washed-ashore\TestResults\qa\qa-web-w8-player-smoke.log`
- Builds: `Builds\Web\` (final), `Builds\Web-baseline\` (pre-override), `Builds\WebDev\` (advisory dev build), `Builds\build-web-final.log`, `build-web-baseline.log`, `build-webdev.log` (linker failure), `build-webdev-2.log`, `build-windows-w8.log`, provenance JSON files
- Install: `_bmad-output\poc-logs\install-webgl.txt`; command log `_bmad-output\poc-logs\build-engineer-commands.log`
- Baselines: `_bmad-output\poc\web-baseline.json` (techart), `_bmad-output\poc-logs\settings-snapshot\` (web-build), qa's pre-change copy `C:\Users\noah\AppData\Local\Temp\claude\C--Tools-ruflo-studio\d30412c9-b209-4b7a-8cb4-af7df6b6cb3b\scratchpad\web-baseline\`
- qa scripts: `...\scratchpad\qa-web\qa_web_eval.cs`, `meta_diff.py`, `scan_build.js`
- Browser screenshots (main): `C:\Users\noah\AppData\Local\Temp\claude-chrome-screenshots-8yKMFJ\screenshot-1791178243845-0.jpg`, `screenshot-1791178300260-1.jpg`, `screenshot-1791178300261-2.jpg`
