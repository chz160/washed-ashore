Drop the web (WebGL) version. From now on the game targets **Windows, Linux and macOS** only (Noah's decision, 14 Oct 2026). Remove the web code paths, build settings and deploy script, and add the new platforms.

## Remove from the project (checked 2026-10-14)

**Code paths:**
- [ ] `Assets/Scripts/Gameplay/PlayerController.cs:127-134, 190`: the WebGL mouse-lock gate. Keep the standalone behaviour (lock on Start).
- [ ] `Assets/Tests/EditMode/MouseLockGateTests.cs`: remove the WebGL cases, or the whole gate if it becomes trivial.
- [ ] `Assets/Scripts/Perf/BellsBendFpsWalk.cs:18, 20, 35, 54, 74, 244`: URL query parameters, browser-console FPS JSON, and their doc comments.
- [ ] `Assets/Scripts/Perf/FarViewProbe.cs:78`.

**Shaders and editor checks** (replace the GLES3x/WebGL targets with **Vulkan**, **GL Core** and **Metal**):
- [ ] `Assets/Editor/Art/BellsBendWaterLook.cs:15, 62, 74`
- [ ] `Assets/Editor/Fish/FishLookDev.Helpers.cs:93, 103`
- [ ] `Assets/Editor/Art/BellsBendWaterMaps.cs:12, 228`: the WebGL import override and the "samplers limited by WebGL2" note. Check whether that limit still applies.
- [ ] `Assets/World/BellsBend/Water/BellsBendWater.shader:7` and `Assets/World/Lookout/LookoutFarCard.shader:6`: WebGL notes.
- [ ] Comments only: `Assets/Scripts/Level/BellsBendSkyInit.cs:7`, `Assets/Editor/Fish/FishVatBake.cs:18`.

**Settings and assets:**
- [ ] `Assets/Settings/Build Profiles/WebGL.asset` (and its `.meta`), and `Assets/WebGLTemplates/` (ClickToPlay).
- [ ] `ProjectSettings/ProjectSettings.asset`: `m_BuildTarget WebGLSupport` (line 546) and the `webGL*` block (lines 812–820, including `webGLTemplate`).
- [ ] **Remove the Mobile quality level** (Noah's decision, 2026-10-14). Delete it from `ProjectSettings/QualitySettings.asset` (it's the WebGL default at line 133), and delete `Assets/Settings/Mobile_RPAsset.asset` and `Mobile_Renderer.asset` with their `.meta` files. **PC becomes the only quality level and the default on every platform.** Before deleting, grep for any script, test or tool that references `Mobile_RPAsset`, `Mobile_Renderer` or quality index 0, and fix it.
- [ ] `.gitignore:93-94` (WebGL Burst output, `/Data/Plugins/`).
- [ ] WebGL overrides in **25 of our own `.meta` files** under `Assets/` (not ThirdParty): remove them. Third-party `.meta` overrides are optional.

**Deploy and hosting:**
- [ ] `deploy-web.ps1`. Local `.wrangler/` is already git-ignored; delete it locally.
- [ ] The Cloudflare Pages project and the R2 bucket `washed-ashore-game-data`: **Noah takes these down himself.** Nothing here deletes hosted content.
- [ ] Local `Builds/Web*` (untracked).

**Docs:**
- [ ] `CLAUDE.md` (repo): the "Web build size (deferred Addressables work)" section.
- [ ] `deferred-addressables-content-streaming.md`: its `Web.data` trigger lapses. Re-base it in #12.
- [ ] `README.md`: no web mentions (checked).
- [ ] History: add **one** "Web build retired 2026-10-14" note in `_bmad-output/poc/`, linked from the README or CLAUDE.md. Leave the web POC files (`poc-spec-washed-ashore-web-build.md`, `web-qa-report.md`, `web-acceptance-checklist.md`, `web-baseline.json`, `td-web-note.md`, `_bmad-output/poc-logs/*web*`) as history. **Web criteria and the "breaking the web build" automatic FAIL in older specs no longer bind.**
- [ ] Open issues #9–#13 have been updated to drop web criteria.

## Add for the new targets

- [ ] **Install the Linux and Mac Build Support modules** for 6000.6.4f1 through the `unity` CLI. Only WebGL and Windows are installed today.
- [ ] **Build profiles** for Windows, Linux and macOS, with batchmode methods like `BellsBendWindowsBuild` for each, plus `release-*.ps1` scripts.
- [ ] **Windows and Linux: Mono or IL2CPP** (an ADR). Linux IL2CPP from Windows needs Unity's Linux toolchain package.
- [ ] **macOS: an unsigned Mono build for now** (Noah's decision, 2026-10-14). The only Mac on the team belongs to the writer, so there's no IL2CPP, signing or notarisation yet. Build it from Windows as a **universal (Apple Silicon + Intel)** Mono player if the 6000.6 macOS module allows it. Otherwise build for Apple Silicon and record why. Ship it with a short `README-macOS.txt` explaining how to open an unsigned app (right-click → Open, or `xattr -dr com.apple.quarantine`). Signing and notarisation become a later issue, once someone with a Mac takes them on.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| R1 | **No web left.** `rg -i "webgl\|UNITY_WEBGL\|WebGLPlayer\|GLES3"` over `Assets/` (excluding ThirdParty), `ProjectSettings/`, `tools/`, `*.ps1`, `CLAUDE.md` and `README.md` returns **0 hits**, apart from the retirement note | The grep output |
| R2 | **Three builds.** Windows, Linux and macOS each build from the command line with exit 0 | Build logs and provenance |
| R3 | **Each platform runs.** Each build launches into World on its native graphics API (D3D12 or D3D11, Vulkan, Metal), walks the `-bbFpsWalk` route with 0 errors, and shows **no magenta or missing shaders**. Water, fish, birds and the tower are checked by screenshots. **The macOS run is done by the writer on their Mac:** the pod sends them the build and a one-page check sheet, and their `Player.log` and screenshots are the evidence. Linux is run on a Linux machine or VM | `Player.log` plus screenshots per platform |
| R4 | **No regression.** The EditMode and PlayMode suites pass, and the Windows FPS is unchanged ±5% | Test XML; FPS JSON |
| R5 | **Docs updated.** The README and CLAUDE.md list Windows, Linux and macOS (macOS as an unsigned test build), and nothing lists web or the Mobile quality level as a target or a pass criterion | Diff |
| R6 | **One quality level.** `QualitySettings` has only PC, which is the default for every platform. `Mobile_RPAsset` and `Mobile_Renderer` are gone, and nothing references them | QualitySettings diff; grep |

**Out of scope:** removing hosted content (Noah does that), store publishing, and macOS IL2CPP, signing and notarisation (a later issue).

## Pod

**platform-tools-engineer** (lead), **build-engineer**, **technical-artist** (shader targets) and **qa-lead**.
