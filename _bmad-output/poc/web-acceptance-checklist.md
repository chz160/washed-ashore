---
title: 'Web acceptance checklist: Washed Ashore walk-around (WebGL)'
source_spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-web-build.md
prior_checklist: _bmad-output/poc/acceptance-checklist.md
owner: systems-designer
consumer: web-qa (verdict), main (all browser checks)
created: '2026-10-04'
---

# Web acceptance checklist (W1–W8 blocking, AF gates at end)

## Rules for qa

- Every check is PASS/FAIL. No partial credit. For each check, record the exact command, the raw output and a timestamp.
- `eval` snippets are C# bodies run through `unity command eval` / `eval_file`. Run them in Edit mode with `Assets/Scenes/World.unity` open and no compile errors pending. If an `eval` call fails mid-reload, retry once (friction #18). The same failure 3 times with no progress counts as a blocker (AF2).
- `unity build` and `unity test` need the project lock. Close the live Editor first (friction #21) and reopen World.unity afterwards (friction #22).
- **Only `main` does browser work.** It uses the Claude-in-Chrome tools: `navigate`, `javascript`, `read_console_messages`, screenshot. Other agents ask `main` through SendMessage and quote its raw output as evidence.
- Shell: the Bash tool has no coreutils (friction #1). Run size and file checks with `pwsh.exe` using the full path, or as `.ps1` files.
- **Baseline snapshot (needed by AF1 and W8).** Before any web change, web-techart writes `_bmad-output/poc/web-baseline.json` from one `eval_file`. It must contain:
  - TerrainData: tree instance count, per-prototype counts, and the per-layer detail sums. Expected values are 735 instances and detail sums 116882 / 113413 / 1514, from qa-report P4.
  - For every `TextureImporter` under `Assets/`: the default platform settings and the `Standalone` settings (`overridden`, `maxTextureSize`, `format`, `textureCompression`).
  - QualitySettings for level 1 (PC): every serialized field, including `terrainDetailDensityScale`, `terrainTreeDistance`, `terrainDetailDistance`, `terrainBillboardStart`, `terrainMaxTrees`, and `renderPipeline`. Also the Standalone default quality level, which is 1.
  - The `PC_RPAsset` fields (render scale, shadow distance, MSAA, HDR).
  - The shared PlayerSettings: `colorSpace`, `productName`, `companyName`, the Standalone graphics APIs, the Standalone scripting backend, and `SplashScreen.show`.
  - The SHA-256 of `Assets/Scenes/World.unity`, of the TerrainData `.asset` file, and of every source image file under `Assets/` (the image files, not their `.meta` files).

  After the run, qa recomputes the same snapshot and diffs it. Allowed differences are listed per item below.

## Click-to-play rule (applies to W3, W5, W6, AF3)

| ID | Check | Pass condition |
|---|---|---|
| C1 | Page content before the first click (`main`, `javascript` tool) | `document.body.innerText.trim()` is exactly `Click to play` (case-insensitive). It is one line, so it contains no `\n`. |
| C2 | No interactive or brand elements (`main`, `javascript`) | `document.querySelectorAll('button, img, svg, a, input, select, video, [role=button]').length === 0`. Unity's **Default** template fails this check because it has a logo, a fullscreen button and a footer. Use the **Minimal** template or a custom template that adds only the text line. |
| C3 | Template source (`Builds/Web/index.html`) | The page has no CSS background image and no progress bar or logo markup. A `<link rel="icon" href="data:,">` is required (see W5.4). |
| C4 | In-scene UI is unchanged | The P2.2 eval still returns Canvas 0, UIDocument 0 and EventSystem 0. The text must live in the HTML, not in a Unity Canvas. |
| C5 | Text hides while the mouse is locked and returns when it is released | After a click, `document.pointerLockElement === canvas` and the text element is hidden. After Esc, `pointerLockElement === null` and the text is visible again. |
| C6 | Esc releases and the next click recaptures | Press Esc, then **wait ≥ 1.5 s**, then click. `pointerLockElement === canvas` again. The wait is required: Chrome rejects a re-lock request that comes sooner after a user exit, and that rejection logs a console error that would fail W5. |
| C7 | Unity splash | `PlayerSettings.SplashScreen.show == false` and `logos.Length == 0`, both unchanged from the baseline. |

`main` checks C5 and C6 on a best-effort basis. Clicks sent by browser automation may not count as a user gesture for pointer lock. If they don't, **C5 and C6 move into Noah's W6 walk-around**, which is planned and does not count as an assist. Do not ask Noah to click separately for agents: that would be an assist.

## W1 — Web Build Support installed through the CLI

| ID | Check | Pass condition |
|---|---|---|
| W1.1 | `unity install-modules -l` (or `unity modules`) for 6000.6.4f1 | The output lists the web module ID (expected: `webgl`). Record the exact ID. |
| W1.2 | `unity install-modules <id> --editor-version 6000.6.4f1` (exact syntax as the CLI reports it) | Exit code 0. Noah's EULA or permission approval is allowed and is **not** an assist. |
| W1.3 | `Test-Path "<Hub editor root>\6000.6.4f1\Editor\Data\PlaybackEngines\WebGLSupport"` | `True` |
| W1.4 | eval `UnityEditor.BuildPipeline.IsBuildTargetSupported(UnityEditor.BuildTargetGroup.WebGL, UnityEditor.BuildTarget.WebGL)` after an Editor restart | `true` |

## W2 — WebGL build exits 0 into `Builds/Web/`

| ID | Check | Pass condition |
|---|---|---|
| W2.1 | `unity build E:\GitHub\washed-ashore --target WebGL --output-path E:\GitHub\washed-ashore\Builds\Web --format json` | Exit code 0. The envelope reports `success: true` and the log contains `Build Finished, Result: Success.` A `license-error` entry in `editorErrors` is noise (friction #24) and does not fail this check. |
| W2.2 | Artifacts | `Builds/Web/index.html` exists. `Builds/Web/Build/` contains `*.loader.js`, `*.framework.js*`, `*.wasm*` and `*.data*`. |
| W2.3 | eval `UnityEditor.PlayerSettings.WebGL.compressionFormat` | `Brotli` or `Gzip`, not `Disabled`. Record which one. |
| W2.4 | eval `UnityEditor.PlayerSettings.WebGL.decompressionFallback` | `true`. Otherwise `Builds/Web/SERVER-HEADERS.md` must document the `Content-Encoding` and `Content-Type: application/wasm` headers, and W5 must use a server that sends them. |
| W2.5 | Build scenes | They are unchanged from P2.1: exactly `Assets/Scenes/World.unity:True`. |
| W2.6 | Graphics API | Record it in the friction log for the td note: WebGL2, or WebGPU if enabled. WebGL2 is the expected default. |
| W2.7 | Development flag on the **final** build | `EditorUserBuildSettings.development == false` when the build is made. |

## W3 — Platform-gated mouse lock (gameplay-engineer)

Required shape: the gating decision is a **pure static method** that takes the platform. Code that reads `Application.platform` passes it to that method. Example:

```csharp
public static bool ShouldLockOnStart(RuntimePlatform p) => p != RuntimePlatform.WebGLPlayer;
public static bool ShouldRelockOnClick(RuntimePlatform p) => p == RuntimePlatform.WebGLPlayer;
```

A bare `#if UNITY_WEBGL` guard **does not pass** on its own. It can't be tested in the Editor, and it flips whenever the Editor's *active build target* is WebGL. With WebGL active, the existing PlayMode tests would run the web path on Windows. A `#if` may wrap the static call, but the decision must be the static method.

| ID | Check | Pass condition |
|---|---|---|
| W3.1 | Code review by web-td of `Assets/Scripts/Gameplay/PlayerController.cs` | `Start()` locks only when `ShouldLockOnStart(Application.platform)` is true. On WebGL, the lock is requested on a left click (`Mouse.current.leftButton.wasPressedThisFrame`) while `Cursor.lockState != Locked`. There is no Esc handler in C#, because the browser releases the lock itself. Everything else in the file is unchanged. |
| W3.2 | Look input while the mouse is unlocked on WebGL | Look delta is ignored when the platform is WebGL and `Cursor.lockState != Locked`. This also goes through a static method, so the camera doesn't spin before the first click. Windows behaviour is unchanged. |
| W3.3 | EditMode test asmdef `Assets/Tests/EditMode/` (EditMode test assembly referencing the gameplay assembly) | It contains these tests: `ShouldLockOnStart(WindowsPlayer)==true`, `(WindowsEditor)==true`, `(LinuxPlayer)==true`, `(OSXPlayer)==true`, `(WebGLPlayer)==false`; `ShouldRelockOnClick(WebGLPlayer)==true`, `(WindowsPlayer)==false`, `(WindowsEditor)==false`; and the W3.2 look gate for WebGL+None (ignored), WebGL+Locked (applied) and WindowsPlayer+None (applied). |
| W3.4 | `unity test E:\GitHub\washed-ashore --mode EditMode` | Exit code 0. NUnit XML `failed="0"`, and every W3.3 test is present with `result="Passed"`. |
| W3.5 | Browser behaviour | C5 and C6 pass, checked either by `main` or in Noah's W6 walk-around. |

## W4 — Download size ≤ 50 MB and WebGL texture overrides (technical-artist)

| ID | Check | Pass condition |
|---|---|---|
| W4.1 | `pwsh -c "(Get-ChildItem E:\GitHub\washed-ashore\Builds\Web\Build -Recurse -File \| Measure-Object Length -Sum).Sum"` on the **final** (non-development, compressed) build | **≤ 50 000 000 bytes**. Decimal MB is the strict reading. Record the bytes before and after the overrides in the friction log. |
| W4.2 | eval: for every `TextureImporter` under `Assets/` (`AssetDatabase.FindAssets("t:Texture2D", new[]{"Assets"})`), read `ti.GetPlatformTextureSettings("WebGL")` | Return the list of failing `path:reason` entries; it must be **empty**. A texture fails when: `!s.overridden`; or `s.maxTextureSize > 1024`; or `s.format` is uncompressed (`RGBA32`, `ARGB32`, `RGB24`, `RGBA64`, `RGBAHalf`, `RGBAFloat`); or `s.format == Automatic && s.textureCompression == Uncompressed`. |
| W4.3 | Baseline diff: default and `Standalone` texture settings | Identical to `web-baseline.json`. |
| W4.4 | Baseline diff: source image SHA-256 | Identical. Only `.meta` files may change. |

## W5 — Runs when served locally, with zero console errors (main reads them)

| ID | Check | Pass condition |
|---|---|---|
| W5.1 | Serve: `python -m http.server 8000 --directory E:\GitHub\washed-ashore\Builds\Web` | The server starts. `main` navigates Chrome to `http://localhost:8000/`. |
| W5.2 | Load to gameplay | Within 120 s the loader finishes: the canvas is present and the C1 text is shown. After a click (or Noah's click in W6), a screenshot shows the terrain from the player's view. |
| W5.3 | `read_console_messages` covering page load plus 30 s after the first click | **0 entries at error level**. This includes uncaught exceptions, unhandled promise rejections, `Failed to load resource` (any 404 or 500), Unity `Debug.LogError` and `Debug.LogException`, `Unable to parse Build/...` (a compression or header error), `RuntimeError` / `abort(` from wasm, and the Chrome pointer-lock rejection. |
| W5.4 | favicon | No `favicon.ico` 404. The template includes `<link rel="icon" href="data:,">`. This 404 gets no exemption. |
| W5.5 | Warning list | Every warning is copied into the friction log. Warnings never fail W5 on their own, with the exceptions in the next table. |
| W5.6 | Final build only | 0 console lines start with `[FrameLog]` (see W7.6). The `Development Build` watermark is absent. |

**Known benign warnings (allowed):**
- Chrome's `The AudioContext was not allowed to start...`, which clears after the first click.
- Unity's decompression-fallback notice, which says the server did not send `Content-Encoding` and the content is being decompressed in JavaScript. It is expected under `python -m http.server`.
- `[Violation] 'requestAnimationFrame' handler took Nms` and other Chrome `[Violation]` performance notices.
- Shader-unsupported or stripped warnings **only** for the URP internal post-process shaders already ruled benign in P8.3 (`GaussianDepthOfField`, `BokehDepthOfField`, `PaniniProjection`).
- WebGL extension-not-supported notices (for example `WEBGL_debug_renderer_info`), and Unity's informational logs: `[UnityMemory]`, `Initialize engine version`, `WebGL 2.0 context created`, `Loading player data`.

**Warnings that fail anyway:**
- Any `Shader ... not supported` or `Hidden/InternalErrorShader` that names a material shader used in the project. This counts as magenta and fails W6.
- Any `PlayerController: fell below terrain`, which means the terrain collider is broken on the web. This fails W6.
- Any `WebGL: INVALID_OPERATION` or `GL_INVALID_*` repeated every frame (more than 10 occurrences). It goes to web-td for a ruling; if not ruled benign, W5 fails.

## W6 — Playable (Noah's 60-second walk-around in Chrome)

This walk-around is planned and **is not an assist**. `main` serves the final build and Noah answers each line yes or no. `main` may screenshot it.

| ID | Check | Pass condition |
|---|---|---|
| W6.1 | Before clicking, the page shows only "Click to play" | Yes |
| W6.2 | One click captures the mouse; mouse movement looks around | Yes |
| W6.3 | WASD moves the player; the player stays on the ground | Yes |
| W6.4 | Terrain is textured with 3 layers; trees, rocks, bushes and grass are visible | Yes |
| W6.5 | Nothing is magenta, black or missing | Yes |
| W6.6 | Esc releases the mouse; a click after a short pause captures it again (C5, C6) | Yes |
| W6.7 | No other UI, menu, logo or button appears at any point | Yes |

## W7 — Performance: median ≥ 30 FPS at 1920×1080 over 30 s (technical-artist)

**Method: a dev-only frame-time logger.** The whole file is inside `#if DEVELOPMENT_BUILD && UNITY_WEBGL`. It bootstraps through `[RuntimeInitializeOnLoadMethod]`, so it adds **no scene object and no change to World.unity**.

The logger behaves as follows:
- It starts sampling `Time.unscaledDeltaTime` 5 s after the first pointer lock, as a warm-up.
- It samples for 30 s.
- It prints one summary line with `Debug.Log`, which appears in the browser console:
  `[FrameLog] res=<Screen.width>x<Screen.height> frames=<n> secs=<t> median_ms=<x> fps_median=<y> p95_ms=<z>`.
- It also prints a short `[FrameLog] window` line every 10 s, so partial runs leave evidence.

**Walking input** (choose before measuring and record the choice):
- **A (preferred, 0 assists):** the same dev-only file includes an auto-walk. For 30 s it queues Input System state for Keyboard W plus a slow mouse-delta yaw (`InputSystem.QueueStateEvent`), so the real `PlayerController` drives the walk. It is stripped with the logger.
- **B:** Noah walks for 30 s in the dev build. This counts as **1 assist**.

| ID | Check | Pass condition |
|---|---|---|
| W7.1 | Dev build: `unity build ... --target WebGL --output-path E:\GitHub\washed-ashore\Builds\WebDev` with development on | Exit code 0. This build is used for W7 only. |
| W7.2 | Resolution | The `[FrameLog]` line shows `res=1920x1080` or larger in both dimensions (DPR scaling can push it higher, which is the stricter case). Chrome is fullscreen or sized to produce it. Mobile_RPAsset render scale is `1.0`. |
| W7.3 | Sample | `secs ≥ 29.5`, taken while walking. |
| W7.4 | `main` reads the console with `read_console_messages` and pattern `\[FrameLog\]` | **`fps_median ≥ 30.0`**, equivalently `median_ms ≤ 33.33`. Record p95. |
| W7.5 | If W7.4 fails, cut in this order and re-measure after each step | (1) Grass: lower `terrainDetailDensityScale` and `terrainDetailDistance` on **quality level 0 only**. (2) Trees: lower `terrainTreeDistance`, `terrainMaxTrees` and `terrainBillboardStart` on **level 0 only**. TerrainData must **not** be edited, because that would change the Windows look (AF1). Other Mobile_RPAsset changes (shadows, for example) need web-td approval first, must be added to the W8.6 list, and are logged. Render scale below 1.0 does not count. Record each step's FPS in the friction log. Still below 30 after both cuts means W7 fails. |
| W7.6 | Absent from the final build | Three conditions. (a) The logger and auto-walk source files are guarded by `#if DEVELOPMENT_BUILD && UNITY_WEBGL` (web-td reviews this). (b) The final build is non-development (W2.7). (c) The final build's console has 0 `[FrameLog]` lines (W5.6). |

## W8 — No regression on Windows (build-engineer)

All W8 checks run **after the last web change**, with the active build target switched back to `StandaloneWindows64`.

| ID | Check | Pass condition |
|---|---|---|
| W8.1 | `unity build E:\GitHub\washed-ashore --target StandaloneWindows64 --output-path E:\GitHub\washed-ashore\Builds\Windows\WashedAshorePOC.exe` | Exit code 0. The build start time is later than the modification time of every file changed during the web spike. |
| W8.2 | Artifacts and 20 s launch smoke, same as P8.2 and P8.3 | The exe, `_Data/` and `UnityPlayer.dll` exist. The player is alive at 20 s with 0 `Exception` lines. The only `Shader ... not supported` lines are the 4 URP internals from P8.3. |
| W8.3 | `unity test E:\GitHub\washed-ashore --mode PlayMode --filter WashedAshore.Tests.PlayMode` | Exit code 0. NUnit `total=5 passed=5 failed=0`. Tests A_LoadsWorld, B_HoldingWForTwoSecondsMovesPlayerAtLeastThreeMetres, C_PlayerIsGroundedAfterThreeSeconds, D_FrameCountAdvances and E_MouseDeltaRotatesPlayer all show `Passed`. The test file is unmodified (its SHA-256 matches the baseline). |
| W8.4 | Unfiltered `unity test --mode PlayMode` | `failed="0"`. The 2 Input System skips are expected (friction #23). |
| W8.5 | `unity test --mode EditMode` | `failed="0"`, the same run as W3.4. |
| W8.6 | Baseline diff (AF1) | Only these may differ: the WebGL texture overrides; `ProjectSettings/QualitySettings.asset` **level 0 ("Mobile") fields only**, for the W7.5 cuts; `Assets/Settings/Mobile_RPAsset.asset` **only** `m_RenderScale` 0.8 → 1.0 and keys matching `^m_Prefilter` or `^m_PrefilteringMode`, which URP rewrites at build time and nothing reads at runtime (producer ruling, web-td approved). Three more keys are allowed in Mobile_RPAsset, with exactly these values: `m_ShadowSmallMeshScreenPercentages` `{0,0,0,0}`, `m_Light2DPrefilteringMode` `0` and `m_Light2DKeptVariantCombos` `[]`. These are 6000.6 serialization-upgrade defaults that PC_RPAsset already has with the same values, and they are inert here (web-td ruling). `Assets/Settings/PC_RPAsset.asset` may have a prefilter-only change (`^m_Prefilter` / `^m_PrefilteringMode`) **only if** web-qa shows that a Windows-only rebuild from the baseline produces the same change. Any other changed key in either RP asset needs a fresh web-td ruling added here first, and is otherwise AF1; `PlayerSettings.preloadedAssets` only if the diff clears once the build finishes, or if a Windows-only build produces the same entries (web-td ruling); `PlayerSettings.WebGL.*`, the WebGL template choice and its files under `Assets/WebGLTemplates/` (the template `index.html` and the `.meta` files Unity generates; the content must meet C1–C3), `PlayerController.cs`; the **new** file `Assets/Settings/Build Profiles/WebGL.asset` plus its `.meta` (producer ruling; it must target WebGL only and have no scene list, scripting-define or player-settings overrides; any change to a Windows or Standalone profile is AF1); the **new** file `Assets/Settings/Build Profiles/WebGL Dev.asset` plus its `.meta` (producer ruling; it must target WebGL only and be used only for the W7.1 `Builds/WebDev` build. The W2.1 release web build and the W8.1 Windows build must not use it, and qa checks their build commands and logs for the profile they used); the new EditMode tests, the dev-only W7 file, and `Builds/`. At the end of the run the active build target is `StandaloneWindows64`, the same as the baseline. Everything else in `web-baseline.json` is identical, including World.unity, TerrainData, PC quality, PC_RPAsset (apart from the conditional prefilter exception above), colour space and the Standalone texture settings. |

## Automatic FAIL (stop and report to web-producer and web-qa)

| ID | FAIL if |
|---|---|
| AF1 | The Windows look or behaviour changes to make the web build work. Concretely: any W8.6 diff outside the allowed list. Examples: switching colour space to Gamma, editing TerrainData to cut grass or trees, editing PC quality or PC_RPAsset, changing shared shaders or materials, or locking the mouse on click on Windows. |
| AF2 | The same blocker gets 3 attempts with no progress. Examples: the module install, IL2CPP or linker errors, a shader missing on WebGL2, or an `eval` that hangs. |
| AF3 | Something non-CC0 is added (new packages, assets or fonts), or a menu, HUD, logo, button or second line of text appears (C1–C4, W6.7). |
| AF4 | **More than 2 assists.** Not assists: Noah approving permission prompts and the module EULA, and Noah's W6 walk-around. Assists: everything else. Examples: Noah clicking in the browser for an agent check, walking for W7 under option B, editing any file, or restarting or clicking anything in Unity or the Hub. web-producer keeps the count. |

Verdict: **PASS** when W1–W8 all pass with 0 assists. **PASS WITH ASSISTS (n)** when they all pass with n = 1 or 2. **FAIL** otherwise. A FAIL names the failing ID and gives its evidence.
