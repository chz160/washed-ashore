# Spec: remove the web build, target Windows, Linux and macOS (issue #14)

Author: systems-designer (pod14 'designer'). Rev 2, 2026-10-17: rewritten to match the live #14 (scratchpad
`issue14-live.md`), which overrides rev 1. Branch `remove-web`. Ruling: Noah, 2026-10-14, "going forward we'll just
need to build for Windows, Linux, and Apple".

**Player problem:** none in play. This is a platform change: players get native builds on three desktop OSes,
and nothing in the game should still behave as if it runs in a browser.
**Loop served:** all of it. Keyboard and mouse play must stay the same on every desktop target.
The pointers below were checked with `git grep` on `main` (39db290). "New" marks items the live issue doesn't list.

## Decisions

- **D1 Remove the Mobile quality level** (Noah, 2026-10-14). PC becomes the only level, and the default on every
  platform: `m_CurrentQuality` and every `m_PerPlatformDefaultQuality` row point to PC's new index (0). Delete
  `Assets/Settings/Mobile_RPAsset.asset`, `Mobile_Renderer.asset` and their `.meta` files. Grep on `main`: the only
  references are QualitySettings.asset:51 (the Mobile level itself) and the two assets themselves. No script or tool
  uses index 0 or the names. Heads-up: `FarViewProbe` FPS JSON writes `quality_level`, which goes from 1 to 0. The
  `quality` name stays "PC", so compare runs by name.
- **D2 Scripting backend, Windows and Linux:** Mono, *Noah to confirm.* See
  `_bmad-output/planning-artifacts/adr-scripting-backend.md`.
- **D3 macOS:** an unsigned Mono build (Noah, 2026-10-14), built from Windows. **Universal (Apple Silicon + Intel)
  if the 6000.6 macOS module offers it for Mono** (`OSXStandalone.UserBuildSettings.architecture` = x64ARM64).
  Otherwise build Apple Silicon only, and write the reason (the exact error or missing option) into the build
  provenance json and the report. Ship it with `README-macOS.txt` (item 26). No IL2CPP, signing or notarisation.
- **D4 `webGL*` player settings:** remove `ProjectSettings.asset:812-837` and the `WebGLSupport` entry (:546-548).
  If Unity writes inert defaults back on save, that is not a FAIL, but `webGLTemplate: PROJECT:ClickToPlay` must
  not survive.
- **D5 Graphics APIs:** Windows keeps D3D11 + D3D12. Linux uses Vulkan first, with GL Core as the fallback. macOS
  uses Metal.
- **D6 Inline-sampler rule (BellsBendWaterMaps.cs:12, BellsBendWater.shader:7): keep it.** It costs nothing, and it
  keeps the GL Core fallback safe. Reword the reason from "WebGL2" to "keeps the GL Core fallback simple". Drop only
  the `"WebGL"` import override at :228.

## Removal list (owner in brackets)

Code [engineer]:
1. `PlayerController.cs:126-134, 190`: the WebGL lock gate goes. Lock on Start, as standalone does today.
2. `Tests/EditMode/MouseLockGateTests.cs`: delete it if the gate is gone; otherwise drop the WebGL cases.
3. `Perf/BellsBendFpsWalk.cs:18-20, 35-41, 54-57, 74, 243-244`: URL params, `absoluteURL`, browser-console notes.
   Keep the command-line args and the `[FPSJSON]` line in Player.log.
4. `Perf/FarViewProbe.cs:78`.
5. **New:** `Scripts/Fish/FishTuning.cs:189` tooltip "(web hitch guard)". Reword it; the value is unchanged.
6. **New:** `Tests/EditMode/FishRendering/FishDrawRuleTests.cs:102, 104` "// web" cases. Relabel them, or drop them
   and name the tier each covered.
7. Comments: `Level/BellsBendSkyInit.cs:7`, `Editor/Fish/FishVatBake.cs:18`.
8. Shader checks, `Editor/Art/BellsBendWaterLook.cs:15, 53, 62, 74` and `Editor/Fish/FishLookDev.Helpers.cs:93, 103`.
   Targets: D3D/Win64, **Vulkan**/Linux64, **GL Core** (`ShaderCompilerPlatform.OpenGLCore`)/Linux64 and **Metal**/OSX.
9. `Editor/Art/BellsBendWaterMaps.cs:12, 228` (D6).
10. Comments only (producer ruling): `World/BellsBend/Water/BellsBendWater.shader:7` and
    `World/Lookout/LookoutFarCard.shader:6`. Use the D6 wording; don't change shader logic.

Assets and settings:
11. `Settings/Build Profiles/WebGL.asset` + .meta [build].
12. `Assets/WebGLTemplates/`, plus **new:** the folder meta `Assets/WebGLTemplates.meta` [engineer].
13. `ProjectSettings.asset` (D4) and `QualitySettings.asset` + the Mobile assets (D1) [build].
14. `deploy-web.ps1`, plus **new:** `deploy-web.sh` (tracked) [build].
15. **New:** `release-windows.ps1:23`, "Use deploy-web.ps1 for that" [build].
16. `.gitignore:93-94` (WebGL Burst, `/Data/Plugins/`), plus **new:** `:74-75` `.wrangler/` [build]. The `.wrangler/`
    ignore stays for as long as the local folder exists. Producer ruling: nobody deletes the local `.wrangler/` or
    `Builds/Web*`. They are listed in the report as Noah's call. (The live issue says to delete `.wrangler/` locally;
    the producer's ruling wins here.)
17. **WebGL overrides in our own 25 `.meta` files** (not ThirdParty): World/BellsBend 15, World/Terrain 6,
    World/Lookout 3, TutorialInfo/Icons 1. Remove the WebGL platform block only and leave the GUIDs alone
    [engineer]. ThirdParty `.meta` overrides stay.

Docs [engineer]:
18. Repo `CLAUDE.md:5`, the "Web build size" section. Replace it with "Targets: Windows, Linux, macOS (unsigned test
    build). Web retired 2026-10-14, see `_bmad-output/poc/web-retired.md`."
19. **The one retirement note:** `_bmad-output/poc/web-retired.md`. It holds the date, the ruling, the list of the
    web POC files kept as history, and this sentence: "Web criteria and the 'breaking the web build' automatic FAIL
    in older specs and QA reports no longer bind." No history file gets edited.
20. `deferred-addressables-content-streaming.md`: strike the `Web.data` trigger. **Don't add a new number**
    (producer ruling). The new trigger is #12's Q6.
21. README: no web mentions. Add the target list for R5 (macOS as an unsigned test build).
22. History stays as it is: `poc-spec-washed-ashore-web-build.md`, `td-web-note.md`, `web-qa-report.md`,
    `web-acceptance-checklist.md`, `web-baseline.json`, `poc-logs/*web*`, `water-evidence/`. Repo copies under
    `planning-artifacts/issues/` are untouched: they are behind GitHub, and that is a sync gap for the report.

## Additions [build]

23. Build profiles `Windows.asset`, `Linux.asset`, `macOS.asset` under `Assets/Settings/Build Profiles/`.
24. Batchmode build methods for Linux and macOS, modelled on `BellsBendWindowsBuild.BuildBatch` (output
    `Builds/Linux-BellsBend/` and `Builds/macOS-BellsBend/`), each writing a provenance json that includes backend
    and architecture.
25. `release-linux.ps1` / `release-macos.ps1`, or `release-windows.ps1` extended to cover them.
26. **`README-macOS.txt`**, shipped in the macOS zip. It explains how to open an unsigned app: right-click → Open,
    or `xattr -dr com.apple.quarantine "Washed Ashore.app"`. It also names the architecture that was built.

## Acceptance criteria (qa-lead)

The live issue's R1-R6 bind. A-items make them checkable on Windows now.

- **R1 / A1** `rg -i "webgl|UNITY_WEBGL|WebGLPlayer|GLES3"` over `Assets/` (excluding `Assets/ThirdParty`),
  `ProjectSettings/`, `tools/`, `*.ps1`, `CLAUDE.md` and `README.md` returns **0 hits**. The only exception is the
  retirement note, which lives outside these paths. This covers the 25 own `.meta` files. Also run
  `rg -il "deploy-web|wrangler|Cloudflare|Web\.data"` over the same paths: the only allowed hit is `.gitignore`'s
  `.wrangler/`.
- **A2** `Assets/` has no `RuntimePlatform.WebGLPlayer`, `BuildTarget.WebGL` or `ShaderCompilerPlatform.GLES3x`.
- **A3** `Build Profiles/` holds exactly the Windows, Linux and macOS profiles. `Assets/WebGLTemplates*` is gone, and
  so is `webGLTemplate: PROJECT:ClickToPlay`.
- **A4** The project compiles with no errors.
- **R4 / A5** The EditMode and PlayMode suites pass. Any count change against `main` comes only from the WebGL cases
  dropped in items 2 and 6, each named. The Windows FPS is unchanged within ±5%; compare by `quality` name (D1).
- **R2 (Windows) / A6** The Windows batchmode build exits 0 and writes its provenance json.
- **A7** The cursor locks on Start for WindowsPlayer, LinuxPlayer, OSXPlayer and the Editor (a test, or a code read
  if the gate is deleted).
- **A8** `BellsBendFpsWalk` still takes its command-line args and writes `[FPSJSON]` to Player.log.
- **R6 / A9** `QualitySettings` has PC only, as the default on every platform. The Mobile assets are gone. `rg`
  finds no `Mobile_RPAsset`, `Mobile_Renderer`, or guid `5e6cbd92…`/`65bc7dbf…`, apart from the
  `_bmad-output` history snapshot.
- **A10** The Water-look and FishLookDev checks run in batchmode: D3D compiles n/n. Vulkan, GL Core and Metal each
  report "compiled n/n" or "target unavailable (module not installed)". **A skipped target never counts as
  compiled**; QA forces the unavailable case once.
- **A11** The Linux and macOS build methods and release scripts compile. Run without a module, they must exit
  non-zero with a clear message (QA runs one).
- **R5 / A12** README and CLAUDE.md list Windows, Linux and macOS (unsigned test build) and nothing else. The
  retirement note exists. `git diff --stat` on item 22's history paths is empty.
- **A13** Nothing remote was touched, and nothing was committed or pushed. The ADR exists with "Noah to confirm".

**Blocked on Noah: Linux/Mac Build Support module install** (report as blocked, not failed):
- **R2 / B1** The Linux build exits 0 (Mono, x86_64).
- **R2 / B2** The macOS build exits 0 (Mono). Its architecture is Universal, or Apple Silicon with the reason recorded (D3).
- **B3** The Vulkan, GL Core and Metal counts in A10 read "compiled n/n".
- **R3 / B4** Each player launches into World on its native API, walks `-bbFpsWalk` with 0 errors, and shows no
  magenta. Water, fish, birds and the tower are checked by screenshot. Linux runs on a Linux machine or VM, also
  with `-force-glcore`. The macOS run is done by the writer on their Mac, from a one-page check sheet; their
  `Player.log` and screenshots are the evidence.
- **B5** The Linux and macOS release zips are built, and the macOS zip contains `README-macOS.txt`.

**Not FAILs:** inert `webGL*` defaults Unity writes back (D4), ThirdParty `.meta` overrides, and the untracked
`Builds/Web*` and `.wrangler/`.
