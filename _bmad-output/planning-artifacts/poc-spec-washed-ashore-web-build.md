---
title: 'POC Spec: Washed Ashore walk-around — Web build'
status: ready-for-pod
owner: John (PM)
created: '2026-10-04'
depends_on:
  - _bmad-output/planning-artifacts/poc-spec-washed-ashore-walkaround.md
---

# POC Spec: Washed Ashore web build

**The question this answers:** can Ruflo's studio agents make the existing walk-around also build and run in a desktop browser, using only the `unity` CLI, **without breaking the Windows build**?

This is a feasibility spike. A FAIL with a clear reason is a valid result.

## Fixed inputs

| Item | Value |
|---|---|
| Project | `E:\GitHub\washed-ashore` (World.unity, PlayerController, the Quaternius and ambientCG assets already imported) |
| Editor | Unity `6000.6.4f1`, URP. The web target uses quality level 0, which is `Mobile_RPAsset` (already set). |
| Control surface | `unity` CLI only: `install-modules`, `build --target WebGL`, `test`, `command` |
| Target browsers | Desktop Chrome first, then Edge and Firefox. **Mobile browsers are out of scope.** |
| Unchanged | Scope, assets, the CC0-only rule, and no HUD, menus or inventory |

## The one rule change (decided)

**Allow a bare "click to play".** Browsers only let a page capture the mouse after the user clicks, so the page needs one click before you can look around. That click may show **only** a single line of text ("Click to play") on the Unity loading canvas or in the HTML template. It may not have a menu, logo screen or buttons. Pressing Esc releases the mouse, and the next click captures it again.

## What a person is allowed to do

1. **Noah approves permission prompts** and the module install EULA.
2. **Noah does the final 60-second walk-around in Chrome.**
3. Anything else counts as an "assist" and is logged. More than 2 assists is a FAIL.

## Pass criteria (all must pass)

| # | Criterion | Evidence qa-lead checks |
|---|---|---|
| W1 | **Web Build Support** is installed for 6000.6.4f1 through the CLI. Find the module ID with `unity install-modules -l` or `unity modules`. | CLI output; `Editor/Data/PlaybackEngines/WebGLSupport` exists |
| W2 | `unity build --target WebGL` exits with code 0 into `Builds/Web/` | Build JSON or log; `index.html` and `Build/` exist |
| W3 | **The mouse lock works on the web.** `PlayerController` captures the mouse on click and recaptures it after Esc. On standalone builds it still locks immediately, as it does now. | Code review by technical-director. An EditMode or PlayMode test that the lock logic is gated by platform. |
| W4 | **Download size ≤ 50 MB** for the compressed `Build/` folder. Web textures are capped at 1024 with compression through a WebGL platform override, not by editing the source files. | `du` of `Builds/Web/Build`; the import settings show WebGL overrides |
| W5 | **It runs when served locally.** `python -m http.server` (or equivalent) serves `Builds/Web/`, and the page loads to gameplay with no errors in the browser console. Turn on Unity's decompression fallback, or document the server headers needed. | Console log captured by `claude-in-chrome` `read_console_messages`: zero errors |
| W6 | **It's playable.** After one click, WASD moves the player and the mouse looks around. The terrain, trees and grass render. Nothing is magenta. | Noah's 60-second walk-around in Chrome. Optionally, agents capture it with a browser tool. |
| W7 | **Performance:** a median of ≥ 30 FPS at 1920×1080 in Chrome over 30 seconds of walking on Noah's machine. If it falls short, cut grass density and tree count before failing. | Measured with a frame-time log or a dev-only on-screen counter that's stripped from the final build |
| W8 | **No regression.** The Windows build still passes, and `unity test --mode PlayMode` still passes all of the existing WorldWalkTests. | Windows build exit code 0; NUnit XML with 0 failures |

## Automatic FAIL (stop and report)

- Changing the Windows look or behaviour to make the web build work (beyond the platform-gated mouse lock and the per-platform texture overrides).
- The same blocker goes 3 attempts with no progress (for example the module install, linker or IL2CPP errors, or a shader that's missing on WebGL2).
- Adding anything that isn't CC0, or adding a menu or HUD.

## What the pod hands back

1. A verdict from the qa-lead agent: **PASS**, **PASS WITH ASSISTS (n)** or **FAIL**, with evidence for W1–W8.
2. A **web friction log** appended to `_bmad-output/poc/friction-log.md`: build time, size before and after optimization, every failure and workaround.
3. A technical-director note: WebGL2 or WebGPU, the risks of shipping Washed Ashore to the web, and what to cut first.

## Pod

Run `/studio-pod` from `C:\Tools\ruflo\studio`, with the project path set to `E:\GitHub\washed-ashore`.

| Role | Owns |
|---|---|
| build-engineer | Module install, web build, compression and hosting, Windows regression build (W1, W2, W5, W8) |
| gameplay-engineer | Click-to-play mouse lock, platform gating, tests (W3) |
| technical-artist | Web texture overrides, size and frame-rate budget, grass and tree cuts if needed (W4, W7) |
| qa-lead | Browser verification, console check, verdict (W5, W6, sign-off) |
| executive-producer | Schedule, assist count, final report |
