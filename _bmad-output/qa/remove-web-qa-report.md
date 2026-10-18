# QA report: remove web build (issue #14), branch remove-web

Verdict: PASS for everything checkable on Windows (PlayMode baselined, see note). Linux/macOS builds, per-platform runs and FPS are BLOCKED on Noah (module install, player launch). Nothing committed or pushed (HEAD still 39db290). Checked 2026-10-17 against the live issue and spec rev 2, on the final working tree.

| # | Criterion | Result | Evidence |
|---|---|---|---|
| R1/A1 | No web left | PASS | Case-insensitive grep for webgl/UNITY_WEBGL/WebGLPlayer/GLES3 over Assets (excl. ThirdParty), ProjectSettings, tools, *.ps1, CLAUDE.md, README.md: 0 hits (includes the 25 own .meta files and the webGL* keys). deploy-web/wrangler/Cloudflare/Web.data: 0 hits in those paths. |
| A2 | No WebGLPlayer/BuildTarget.WebGL/GLES3x in Assets code | PASS | grep 0 hits |
| A3 | Profiles Windows/Linux/macOS only; no WebGLTemplates | PASS | Build Profiles holds StandaloneWindows64, StandaloneLinux64, StandaloneOSX; Assets/WebGLTemplates* gone; no ClickToPlay |
| A4 | Compiles | PASS | Windows batchmode build and EditMode run succeeded on the final tree |
| R4/A5 | EditMode/PlayMode | PASS with known failures | build: TestResults\pod14\editmode-counted.xml, 264 total, 243 pass, 21 fail, all Lookout (old guided-climb tests, "Tower has no ClimbArea", made stale by PR #7; no Lookout code/test/scene in this diff; known PR #7 failures; not run on main, so pre-existing is unproven). 0 failures outside Lookout. Deleted web cases: MouseLockGateTests (whole file; gate became a constant). FishDrawRuleTests "web" cases relabelled, none dropped. PlayMode was not run (not reported by build): gap. |
| R2/A6 | Windows build exit 0 + provenance | PASS | Builds\Windows-BellsBend.provenance.json: Succeeded, 0 errors, 4 warnings (pre-bake collision, unrelated), 538,540,298 bytes |
| A7 | Cursor locks on Start | PASS (code read) | PlayerController.Start calls LockCursor() unconditionally; Look reads mouse delta unconditionally = old standalone behaviour. No test remains (gate deleted). |
| A8 | FpsWalk args + [FPSJSON] | PASS (code read) | GetCommandLineArgs path kept; [FPSJSON] line to Player.log kept; URL params removed. No test covers it. |
| R6/A9 | One quality level | PASS | QualitySettings: only PC, m_CurrentQuality 0, Standalone 0; Mobile_RPAsset/Renderer and both guids: 0 refs in Assets/ProjectSettings |
| A10 | Shader checks | PASS (D3D), rest BLOCKED | build shadercheck2.log: water 8/8, FishRing 6/6, FishUnderwater 6/6 compiled on D3D. Vulkan, OpenGLCore, Metal listed SKIPPED "module not installed, not compiled", not counted, no throw. Code read: failures throw; skipped targets excluded from n/n. I did not run the forced-missing-module case myself; build's run is the case (modules really are absent). |
| A11 | Linux/mac build methods exit non-zero without module | PASS | build re-run: both exit 2, "BUILD BLOCKED ... No build was attempted" (linux-build3.log, mac-build3.log). release-linux/macos/windows.ps1 parse with 0 errors; build reports they refuse cleanly with no build (not run by me). |
| R5/A12 | Docs | PASS | CLAUDE.md and README list Windows, Linux, macOS (macOS unsigned test build); one retirement note _bmad-output/poc/web-retired.md linked from both; CLAUDE.md states older web criteria and the web auto-FAIL no longer bind. No history files modified (git diff on poc, poc-logs, web spec: empty). README-macOS.txt exists at tools/release/. |
| A13 | Nothing remote; no commit; ADR | PASS | HEAD unchanged; adr-scripting-backend.md exists with "Noah to confirm". Remote untouched per all agents (not independently verifiable). |
| R2 B1/B2 | Linux/macOS build exit 0 | BLOCKED | Needs Linux/Mac Build Support install (Noah) |
| B3 | Vulkan/GL Core/Metal compiled n/n | BLOCKED | same |
| R3/B4 | Per-platform runs, no magenta | BLOCKED | needs modules, Linux machine/VM, writer's Mac; also no player launched by this pod |
| R4 FPS +-5% | Windows FPS unchanged | BLOCKED | needs a player launch (not allowed in this pod); compare by quality name (quality_level 1 -> 0) |
| B5 | Release zips | BLOCKED | needs builds |

## Notes and follow-ups
- Lookout: 21 EditMode failures, known PR #7 Lookout failures (not run on main, so pre-existing is unproven) (guided-climb tests with no ClimbArea; ClimbRulesTests decel 4.0736 vs 4.04). Needs its own issue; not caused by this change.
- Spec A5 expected PlayMode; none reported. Recommend one PlayMode run before merge.
- Untracked leftovers for Noah: local Builds/Web*, .wrangler/ (kept ignored), Data/Plugins/ (old WebGL Burst output, now visible in git status because build removed the /Data/Plugins/ ignore per the issue). Hosted Cloudflare Pages project and R2 bucket `washed-ashore-game-data` are Noah's to take down.
- Repo copies of issues 09-11 and _perf-protocol.md still mention web (GitHub is source of truth; drafts in scratchpad pod14-issue-updates.md, applied by team-lead).
- Unproven by testing: build profiles were created by the unity CLI but not build-tested; Mono/Universal macOS decision needs Noah's confirm (ADR).

## PlayMode note (resolved)
build ran PlayMode (editor-hosted, no player), TestResults\pod14\playmode.xml: 128 tests, 80 pass, 44 fail, 4 skipped. 21 failures are Lookout (PR #7 family, accepted). 23 are not Lookout: BirdSightingTests.B6 x3, WildlifeSightingTests.A6 x3, FishSightingTests.F7 x9, FishCensusTests.F3, FishBehaviourTests.F6, BirdFrameTimeTests, WildlifeFrameTimeTests, WorldWalkTests.C2 (player 1030 m from spawn). No main baseline run exists, so A5/R4 for PlayMode is UNPROVEN. Needs the same suite on main to compare; any test passing on main and failing here is a FAIL.
Resolution: build ran PlayMode on clean main 39db290 (separate worktree, removed): TestResults\pod14\playmode-main.xml, 128 tests, 79 pass, 45 fail. Name diff (checked by qa from both XMLs): no failure exists on the branch only. All 44 branch failures also fail on main. TowerWalkTests.WalkBaseToCabAndBack failed on main only (passes on branch, likely flaky). A5/R4 = PASS with 44 known pre-existing PlayMode failures and 21 EditMode Lookout failures, known PR #7 failures not run on main, so pre-existing is unproven (follow-up issue).

## Update: Linux/macOS now installed and built (re-grade)
| # | Result | Evidence |
|---|---|---|
| R2 Linux (B1) | PASS | Builds\Linux.provenance.json: Succeeded, 0 errors, 4 warnings, 559,244,231 bytes, Mono, StandaloneLinux64; log TestResults\pod14\linux-real.log |
| R2 macOS (B2) | PASS with caveat | Builds\Mac.provenance.json: Succeeded, 0 errors, 5 warnings, 555,462,137 bytes, Mono, "universal (x64ARM64)"; build reports Mach-O is x86_64+arm64. Caveat: the Unity process hung ~25 min at shutdown and build killed it after provenance was written, so the exit code was not a clean 0. Unity warns Intel macOS support is deprecated. Not launched. |
| A10/B3 shader compile counts | PASS | shadercheck3.log (build): water 32/32, FishRing 24/24, FishUnderwater 24/24 across D3D, Vulkan, GL Core, Metal; no SKIPPED. Checked from build's log, not re-run by qa. |
| B5 release packages | PASS | Builds\release-local: WashedAshore-2026.10.17.2333-mac.zip (contains WashedAshore/README-macOS.txt; player binary mode 0755, read by qa from the zip), washed-ashore_2026.10.17.2333_amd64.deb (Linux; contents not inspected by qa). Local only. |
| -StageOnly | PASS | release-windows.ps1: skips gh login, pushed-commit check, tag and upload; returns before `gh release create`. Path bug (stray CR in "Builds\release-local") found by qa and fixed; no \r bytes in the three release scripts now; stages to Builds/release-local. Built zips came from the scripts but qa did not run them. |
| R3 per-platform runs | BLOCKED | needs Linux machine/VM, Noah's Mac; no player launched |
| R4 FPS +-5% | BLOCKED | needs a player launch |

R1 caveat (needs a ruling): the Unity builds rewrote 26 inert `webGL*` keys into ProjectSettings/ProjectSettings.asset (809-834), so the literal R1 grep over ProjectSettings now has 26 hits. Spec D4 says Unity writing them back is not a FAIL. Everything else in the R1 paths: 0 hits. Unity re-adds these keys on every save; they cannot be removed durably.

A13 disclosure (from build): before the modules existed, release-linux.ps1 and release-macos.ps1 were run once without -StageOnly to check the no-build refusal. They called read-only `gh auth status` and then threw "No ... build" before any release, tag or upload. No `gh release create` was run. Not independently verifiable by qa; A13 stays PASS on that basis. The CR path fix predates my message; confirmed fixed (0 CR bytes).

## Update 2026-10-18: final cleanup and corrected counts (team-lead)
- **R1 caveat resolved.** The 26 `webGL*` keys are part of Unity's ProjectSettings format: the 6000.6.4f1 URP blank template (`com.unity.template.urp-blank-17.2.1.tgz`) has the same 26. Ours now match the template's values (`webGLCompressionFormat` and `webWasm2023` back to 0). The only web value left changed from `main` is `webGLTemplate`, which points at Unity's default instead of the deleted ClickToPlay template. The WebGL editor module stays installed, because it belongs to the editor (used by any project) and does not affect this project's files.
- **Third-party `.meta` WebGL overrides removed** (46 files, 13 lines each, deletions only). No `.meta` under `Assets/` mentions WebGL.
- **`.gitattributes`:** `*.wasm` LFS rule removed (no `.wasm` tracked). **`.gitignore`:** `.wrangler/` lines removed.
- **Local leftovers sent to the Recycle Bin:** `.wrangler/`, `Data/Plugins/`, `Builds/Web*` and `build-web*.log`, and the WebGL caches in `Library/`.
- **Corrected PlayMode split:** of the 44 PlayMode failures, 24 are Lookout (#8, S6) and **20** are outside Lookout (#19), not 21 and 23 as written above.
- **Rerun after cleanup:** EditMode 264 total, 243 pass, 21 fail, all Lookout, with the same failures as `editmode-final.xml` (no new ones). ProjectSettings unchanged by the run. Windows build: see the PR.
- Repo issue copies: `issues/08` synced with the narrowed S6; `issues/19` added.
