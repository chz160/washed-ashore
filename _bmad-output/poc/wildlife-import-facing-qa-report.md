---
title: 'QA report: wildlife import-time facing fix (WL-BUG-7 follow-up)'
owner: qa-lead (if-qa)
spec: _bmad-output/planning-artifacts/poc-spec-wildlife-import-facing.md (Amendments 1-3, the L-R ruling, the upright guard and side-matching addition)
prior: _bmad-output/poc/wildlife-qa-report.md (WL-BUG-7 runtime workaround)
friction_log: _bmad-output/poc/friction-log.md, IF1-IF14
verified: '2026-10-05 21:33Z - 22:40Z, Windows only (no WebGL, W7 or browser work)'
code_under_test: 'ModelFacingPostprocessor Version = 2 (techart handback ~22:10Z); every verdict run is newer than the last Assets/ write'
---

# Verdict: **PASS** (0 assists)

All gating criteria pass on the final v2 code: AC1, AC1a, AC2, AC3, AC4, AC5, AC6, plus the Amendment 2 prefab checks and the producer's component diff. **AC7 is pending:** Noah's optional visual check in the Windows build, relayed by the lead. It is not gating. No automatic-FAIL condition was triggered. The one judgment call (the Fox.fbx.meta restore) was **ruled not an assist by the lead**: "An automated test dirtied the file, an agent restored it byte-identical, and no human was involved." The verdict is final.

qa produced every value marked "qa" by running the command or probe itself on the v2 code, from 22:11Z on. The engineer's 21:43-22:01Z run was made against v1 and counts only as a pre-v2 baseline (IF13). "techart" means a value from techart's handback that qa re-derived independently, as shown.

## Evidence

| # | Criterion | Actual value | Source | Result |
|---|---|---|---|---|
| AC1 | The fix is a postprocessor in `Assets/Editor/`, a reimport reproduces it, no hand-edited FBX/.meta/prefab | Hash diff of all 567 files in Assets/ProjectSettings/Packages vs qa's pre-change snapshot (565 files, 16:33 local): **added** `Assets/Editor/Wildlife/ModelFacingPostprocessor.cs` (+.meta) and `Assets/Tests/EditMode/ModelFacingDetectionTests.cs` (+.meta); **changed** the 5 workaround/test files, the EditMode asmdef (+1 Editor-only reference) and `WildlifeTuning.asset`, plus the 12 `Animal_*.prefab` files rewritten by `SyncPrefabs()`. The 24 FBX and .meta files are unchanged (Fox.fbx.meta included); `Wildlife_*.prefab`, ProjectSettings and Packages are unchanged. The scene is unchanged. **Reimport:** qa forced `ImportAsset(ForceUpdate)` of Deer.fbx and Wolf.fbx; the AC2 probe JSON is **byte-identical** before and after, and both import records read Flip by LR. No scratchpad code is referenced from Assets/ | qa | PASS |
| AC1a(1) | All 12 Quaternius animals decided Flip **by the L/R rule** (L-R form) | Shipped `FacingReport()`: 12 models `Flip / decidedBy LR / lrForm L-R / applied`, and 12 prefabs now `Keep / LR`. qa's independent probe with the mandated measure and ε (AABB of all Transforms under the armature root): all 12 decide flip with ε matching the report to 4 decimals. Margins over ε: Fox 1.82×, Wolf 1.85×, Husky 2.09×, ShibaInu 2.10×, Alpaca 2.18×, Stag 2.19×, Horse/Horse_White 2.27×, Donkey 2.34×, Deer 2.55×, Bull/Cow 2.72× | qa + techart | PASS |
| AC1a(2) | A +Z model with L bones at -X stays unflipped | EditMode `Fixture1_LeftBonesAtMinusX_KeepsAndIsUnchanged` passed. **qa on a real temporary prefab** (`Assets/QaTemp/QaKeep.prefab`, run through the shipped `SyncPrefabs()`): "Keep. L/R L-R -0.4000 vs eps 0.1000; decided by LR"; head (0, 1) before and after; dependency hash unchanged | qa | PASS |
| AC1a(3) | No L/R bones and a sideways head: warns naming the asset, unchanged | EditMode `Fixture2_...` (x = ±1) passed. **qa on a real temporary prefab**: warning "Assets/QaTemp/QaAmbig.prefab: facing is ambiguous, left unchanged (no left/right bones and no clear head direction) ... head 'Head' (x 1.00, z 0.00)"; head (1, 0) before and after; hash unchanged. The upright guard is covered by `UprightHeadWithoutLeftRight_IsAmbiguous` | qa | PASS |
| AC1a(4) | A Humanoid rig is skipped with a log line | EditMode `HumanoidRig_IsSkipped` and `NonFbx_IsSkipped` passed. **qa on a real Humanoid FBX** (a copy of Fox.fbx in `Assets/QaTemp`, switched to Human): log "Assets/QaTemp/QaFoxHuman.fbx: facing check skipped (Humanoid rig)."; not flipped (head stays at source -Z) and no import record. The same copy imported as Generic flips by LR (control) | qa | PASS |
| AC1a(5) | Idempotent, no double-flip; before/after report diff shows only flip-decision assets changed | `Flip_IsIdempotent` and `ReimportingTwice_GivesIdenticalHeadDirection` passed; qa's forced reimport is byte-identical (AC1). `SyncPrefabs()` on the turned prefabs: Keep, nothing saved. **qa's 80-model scan diff** (`qa-scan-all-PREFIX.json` vs post): exactly the 12 flip-decision FBXs changed orientation, (0, -1) to (0, +1); the other 68 (static, no head) are unchanged. All 80 models are Generic FBX; there are no glTF/GLB files or Humanoid rigs in the project. No path or name allowlist exists: the code scope is `SkipReason()` (non-FBX, Humanoid) plus detection | qa | PASS |
| AC2 | Head direction z > +0.9 for Deer, Stag, Fox, Wolf at rest and at mid-clip (t = 0.5) in Idle, Walk, Gallop; prefab **and** FBX, Animator not evaluating | qa probe (FBX instantiated at identity; `SampleAnimation` at 0.5 × length): rest z = 1.000 for all 4; Idle 0.999 / 0.999 / 0.999 / 1.000; Walk and Gallop 1.000. Pre-fix control: -1.000 everywhere. Every non-Death/HitReact clip of all 12 models ≥ 0.996 (farm animals reported, not gating). **Prefabs** (`LoadPrefabContents`, no Animator update): `Animal_*` and `Wildlife_*` head (0.000, 1.000) for all 4 species | qa | PASS |
| Amd 2 | Prefab sync: rest pose and height | Armature root and mesh node rotation (0.7071, 0, 0, 0.7071) → (0, 0.7071, -0.7071, 0) = exactly (z, w, -x, -y), the 180° yaw (YAML, residual 3e-8). Lowest non-armature bone Y identical to baseline: Deer 0.0006, Stag 0.0006 (FFB.L_end), Fox 0.0101, Wolf 0.0165 (FF.L_end), Δ = 0.0000 (bar ±0.005). Renderer local bounds identical. `Wildlife_*` variants hold only the variant-root identity rotation (present before the fix as well), with no armature or mesh overrides | qa | PASS |
| Prod. | Prefab component diff: NavMeshAgent, Animator + controller, colliders, WildlifeAgent refs and PerfSetup fields unchanged | qa's YAML document diff vs pre-change byte copies of `Animal_`/`Wildlife_` Deer, Stag, Fox, Wolf (control-tested: it flags a mutated `m_Speed`/`m_CullingMode`): **0 non-allowed changes**; in each `Animal_*` only 2 Transforms changed (AnimalArmature and the mesh node, rotation/position only); `Wildlife_*` byte-identical. `WildlifePerfSetup.Report()`: all 8 prefabs `animators=1 culling=[CullUpdateTransforms] smr=1 motionVectors=[False] shadows=[On]`; tris/bones Deer 2098/46, Stag 2054/38, Fox 1848/51, Wolf 1962/51 (identical to techart's pre-sync report) | qa | PASS |
| Lead | Every `.fbx.meta` under `Quaternius/Animals` identical to `lead-snapshot-20261005-163719`, checked after qa's EditMode run | 12/12 SHA-256 identical to the lead snapshot **and** to qa's own 16:33 pre-change hashes (Fox.fbx.meta `08250AFA…00E7`). Checked at 22:36Z, after qa's EditMode run (ended 22:11:32Z) and after qa's Editor pass (forced Deer/Wolf reimport plus a Humanoid import of a Fox copy). So the rewritten AC1a test no longer touches real importers (`qa-fbxmeta-vs-lead-snapshot.txt`) | qa | PASS |
| Lead | The EditMode asmdef's `WashedAshore.Wildlife.Editor` reference keeps editor code out of the player | `WashedAshore.Tests.EditMode`: includePlatforms [Editor], define `UNITY_INCLUDE_TESTS`; `WashedAshore.Wildlife.Editor`: includePlatforms [Editor]. The player's `Managed/` holds only `WashedAshore.Gameplay.dll` and `WashedAshore.Wildlife.dll` from this project (no `*.Editor` or `WashedAshore.Tests.*` DLLs, no nunit); 0 occurrences of `ModelFacingPostprocessor`/`ModelFacingDetectionTests` in any player DLL. The player's `WashedAshore.Wildlife.dll` has no `modelYawOffset` string | qa | PASS |
| AC3 | Workaround fully removed | `grep -i modelYawOffset` over Assets/: **0 hits**. The only rotation terms left in `Assets/Scripts`: `yaw + ExtraModelYaw`, with `ExtraModelYaw` assigned only in `WildlifeBehaviourTests.cs:212/263` (the A4c test hook). No residual runtime 180 under any name (`walkSeconds = 180f` is unrelated) | qa | PASS |
| AC4 | The facing test measures the head bone; the flipped control fails | `WildlifeBehaviourTests.cs:140` asserts `HeadDirInModelFrame().z > 0.9` per agent (head bone, not transform.forward) and passed. **A4c control**: flipped Deer_1_4 `facing=0/41 (0 %, minDot -1.00)`, which fires the "runs facing away" condition; its 3 herd-mates 41/41, 37/37, 38/38 | qa | PASS |
| AC5 | No regressions; A4 vs baseline | **EditMode 42/42** = the 11 pre-existing `MouseLockGateTests` (all present) + N = 31 AC1a cases. **Wildlife PlayMode 18/18** (22:11:45-22:25:13Z). **WorldWalk 5/5**. Judged from the NUnit XML. A4 vs `qa-final-wildlife-a4.json`: facing share Deer 0.998 = 0.998, Fox 1.000 = 1.000, Stag 1.000 = 1.000, Wolf 0.993 = 0.993; `maxClipRunSec` 0 and `maxFloatRunSec` 0 for every species; max clip depth Δ ≤ 0.003 m; stuck 0; off-NavMesh 0. Head basis z: -0.97..-1.00 → +0.99..+1.00 | qa | PASS |
| AC6 | Windows build exits 0; smoke ≥ 60 s with 0 errors | `unity build --target StandaloneWindows64`: exit 0; provenance outcome success, exitCode 0 (22:25:33-22:25:48Z); Editor log "Build Finished, Result: Success."; data files rewritten 22:25:44Z (the exe stub is unchanged, as expected). The CLI's only "error" is the known `LicensingClient has failed validation; ignoring` (friction #24). **Smoke**: 1280×720 windowed, started 22:25:49Z, alive at 75 s, closed with CloseMainWindow, exit code 0. Player.log: **0** error/exception/"Failed to create agent" lines; "Wildlife: seed=101 agents=15 onNavMesh=15", "t=5 onNavMesh=15 moving=11", "Using XInput", clean shutdown | qa | PASS |
| Tuning | Prey angularSpeed unchanged | `WildlifeTuning.asset`: 600 / 600 / 600 / 300 (Deer, Stag, Fox, Wolf), the same as before the fix | qa | PASS |
| AC7 | Noah's visual check (optional) | **Pending**: to be relayed by the lead | Noah | Not gating |

## Automatic-FAIL gates

| Gate | Check | Result |
|---|---|---|
| Same blocker survives 3 attempts | v1 → v2 was a spec change, not a failed fix; IF10 was fixed at attempt 1 | Clear |
| WebGL, W7 or browser work | None by qa or the pod during this feature | Clear |
| FBX, .meta or `Animal_*.prefab` hand-edited to fake the result | FBX and .meta files byte-identical to the pre-change snapshot; prefab changes come only from `SyncPrefabs()` (rotation/position of 2 nodes, exact yaw). See the judgment call on Fox.fbx.meta | Clear |
| Residual runtime 180 | Only the test hook `ExtraModelYaw` (AC3) | Clear |
| Positive control passes | A4c flipped deer 0/41 | Clear |
| Tuning changed to mask the bug | angularSpeed 600/600/600/300 unchanged | Clear |
| Clips silently skipped / Euler clips unremapped | Every clip has 0 Euler curves (qa probe); `TurnClip` remaps quaternion, Euler-Y and position curves and reports any other curve as an import error; all clips read +Z at mid-clip | Clear |
| +Z or ambiguous model flipped; double-flip | QaKeep and QaAmbig unchanged; reimport byte-identical; SyncPrefabs is a no-op on turned prefabs | Clear |
| Path or name allowlist instead of detection | Scope is `SkipReason()` + detection; no Quaternius path or name in the decision code | Clear |

## Judgment calls and assists

| What | Ruling |
|---|---|
| **Fox.fbx.meta restored by techart (IF10).** An AC1a test (since rewritten) dirtied Fox's importer in memory, and a reimport wrote `avatarSetup 0→1` and `globalScale 0.0022→0.22` into `Fox.fbx.meta`. Techart restored the file byte-for-byte from the snapshot | **Not a hand edit to fake the result**: it reverted a test side effect, and the file is byte-identical to qa's 16:33 pre-change hash, so the net change is zero. The faulty test no longer touches real importers (`SkipReason(path, animationType)`), and qa's post-run hash diff confirms 0 FBX/.meta changes. Filed as IF-BUG-1. **Lead ruling: not an assist** (an automated test dirtied it, an agent restored it byte-identical, no human involved) |
| Premature baton (IF13) | Process friction; caught before any verdict run. Not a strike, not an assist |
| Noah closing Editor PID 74556 | An environment event, not an assist |

Assists: **0**. Strikes: **0**.

## Bugs filed (studio:qa)

| ID | Severity | Status | Summary |
|---|---|---|---|
| IF-BUG-1 | Major (test hygiene; touched vendor data) | Fixed, verified | The AC1a `HumanoidRig_IsSkipped` test changed a real `ModelImporter` and a later reimport persisted it into `Fox.fbx.meta`. Fix: pure `SkipReason(path, animationType)` overload. Verified: post-run hashes of all 24 FBX/.meta = pre-change. Regression: qa's hash diff, plus EditMode leaving the tree clean |
| IF-BUG-2 | Minor (spec deviation, no effect) | **Closed by spec change** (the spec now says a trailing `_end` is stripped before side matching, so an `_end` tip counts as its parent's side; the code is unchanged, and the 569 files are hash-identical to qa's post-run list) | `BoneSide()` strips `_end` before matching, so tips such as `FF.L_end` count as side bones; the spec's L/R patterns don't include them (the test case `"FF.L_end", -1` encodes the deviation). Effect: the L-R measure is about 4 % higher (Fox 0.1335 vs 0.1304 strict); every decision is unchanged and the strict form also clears ε for all 12 (≥ 1.82×). Fix either the code or the spec text |

## Out of scope / not tested

- AC7 (Noah's visual check) is pending.
- A8 FPS was not re-run: the change is import-time (rest pose and clip curves) plus removing one addition per frame; the prefab render components, tris and bones are identical (PerfSetup report). The spec does not require it.
- Non-Quaternius vendor packs and Humanoid production rigs: none exist in the project; covered only by fixtures and qa's temporary assets.
- The A6 sighting bands were re-run as part of wildlife 18/18 (pass), not re-tabulated here.

## Evidence files

- qa runs (v2): `TestResults/qa-if/qa-editmode.xml` (42/42), `qa-playmode-wildlife.xml` (18/18), `qa-playmode-worldwalk.xml` (5/5), `qa-wildlife-a4.json`, `qa-build-windows.cli.log`, `qa-smoke-Player.log`, `qa-run-status.txt`; Editor build log `Logs/build-StandaloneWindows64-1791239133373.log`; provenance `Builds/Windows/WashedAshorePOC.provenance.json`
- qa probes: `qa-ac2-PREFIX.json`, `qa-ac2-v2-before-reimport.json`, `qa-ac2-v2-after-reimport.json` (byte-identical), `qa-scan-all-PREFIX.json` / `qa-scan-all.json`, `qa-lr.txt`, `qa-prefabs-PREFIX.txt` / `qa-prefabs.txt`, `qa-ac1a-assets.txt`, `qa-model-facing-report.json` (the shipped report), `qa-perfsetup-report.txt`, `qa-prefab-component-diff.txt`, `qa-fbxmeta-vs-lead-snapshot.txt`
- Pre-v2 baseline (engineer, v1): `TestResults/if-editmode.xml`, `if-playmode-wildlife.xml`, `if-playmode-worldwalk.xml`, `if-wildlife-a4.json`
- qa scratch (probe scripts, pre/post hash lists, pre-change prefab copies): `C:\Users\noah\AppData\Local\Temp\claude\C--Tools-ruflo-studio\d30412c9-b209-4b7a-8cb4-af7df6b6cb3b\scratchpad\qa-if\`
