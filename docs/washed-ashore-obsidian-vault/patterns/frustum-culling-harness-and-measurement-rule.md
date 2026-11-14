---
type: pattern
status: active
tech: [perf]
area: []
issues: [9, 10, 11]
prs: []
source: "tasks #23, #24, #25 (platform-tools-engineer, technical-director); Assets/Scripts/Perf/FrustumProbe.cs header; TestResults/9-measurement-plan.md blob 710f0d11"
created: 2026-11-11
updated: 2026-11-13
aliases: [frustum probe, fc flags, culling harness]
---
# Frustum culling harness and measurement rule

_To measure a culling or draw-distance trial, use the `-fc` flags of `FrustumProbe` in a built Windows player, run A-V-A windows, and judge them with [[0025-savings-rule-s1-perf-trials]]. Issues #9, #10 and #11 share this protocol._

## Problem
A culling change can cut draw counts and still save no frame time, or save time and pop in. Counters alone prove nothing, and a trial setting that leaks into a scene, prefab or URP asset becomes a live change by accident.

## When it applies
Any trial that changes far clip, per-layer cull distance, terrain detail distance, shadow settings or the GPU Resident Drawer (GRD) keys, in `World.unity`. Windows player only; the web build is retired (#14).

## Why it happens
Authoritative flag list and Q1 definitions: the header of `Assets/Scripts/Perf/FrustumProbe.cs` (read it, it is newer than this note). In short:
- Inert unless a `-fc` argument is passed. Refuses to run in the Editor. Trials live in player memory and are restored at quit; nothing is saved to scene, prefabs, URP assets, terrain data or ProjectSettings.
- **Spot run:** `-fcSpots <spot>` (one per launch) plus repeatable `-fcVariant id:key=value,...`. Windows run A V1 A V2 .. Vk A; each applies and reads back, settles (`-fcSettle`, 5 s), times one 360 degree yaw (`-fcYawSpeed`, 15 deg/s, about 24 s), then takes the census and stills at 4 headings. A readback mismatch marks the window `valid:false`. Output: `frustum-windows.jsonl`, `frustum-spots.json`, `progress.txt` (last line DONE).
- **Variant keys:** `far`, `farCab`, `cull.<Layer>`, `spherical=on`, `detail`, `shadows=off`, `fishOff=on`; player only: `shadowDist`, `grdOcc=on|off`, `smallMesh=<%>`.
- **Route run:** `-bbFpsWalk` plus global flags (`-fcFar`, `-fcLayerCull`, `-fcGrdOcc`, `-fcSmallMesh`, ...), one variant per launch; writes `frustum-route.json`.
- **Options:** `-fcWarmup` (15 s), `-fcRun`, `-fcArm`, `-fcPose`, `-fcCabHeight`, `-fcEdgeShots`, `-fcNoShots`.
- **Counters:** batches, SetPass calls, draw calls, triangles, vertices, shadow casters, visible skinned meshes per timed frame; Q1 census (enabled, submitted, fog_visible, bands, by type). GPU ms is null (frame-timing stats are off in ProjectSettings); GRD counters need a development player.
- **Census tool (read-only):** `Unity.exe -batchmode -nographics -projectPath . -executeMethod WashedAshore.Perf.Editor.CullingCensus.RunBatch -logFile <log>` writes `TestResults/9-culling-census.json` (oversized bounds, culling inventory). Hold `.studio/unity.lock` while it runs.

## Evidence
- 33 EditMode tests in `Assets/Editor/Perf/Tests` (platform-tools-engineer, task #23; not re-run by the writer).
- **GRD limit:** `InstanceCuller.cs` in `com.unity.render-pipelines.core@285af6b8c83f` (`Runtime/GPUDriven/Culling/`) tests only the camera layer mask at lines 427-435; `layerCullDistances` does not appear anywhere in the file (grep, 2026-11-11). Its own screen-size cull is `minScreenRelativeHeight` from `smallMeshScreenPercentage` (line 2218). So `Camera.layerCullDistances` reaches only non-GRD renderers.
- Fish are not Renderers: they draw through `Graphics.RenderMeshInstanced`, so no camera layer cull reaches them (plan section 3).

## What to do
- Agents may launch the player under the conditions in [[0026-agents-may-launch-the-player]] (it replaces the "never launch" line of [[0016-frame-time-noise-rule-a1]]). Vsync off, 1920x1080, A-B-A, 3 runs, quiet machine.
- Judge with S1 in [[0025-savings-rule-s1-perf-trials]]: p95, S_min = max(0.20 ms, 2%), worst run must clear it. A recommendation also needs the visual check.
- For small props use `smallMesh` (the GRD lever), not a layer cull; for fish use `fishOff` as an upper bound only.
- Pre-registered plan for #9: `TestResults/9-measurement-plan.md`. Results and recommendation: [[frustum-culling-investigation]], and `_bmad-output/poc/perf-frustum-note.md` (link only).

## Lessons from #9 (for the #10 and #11 plans)
Sources: qa-lead's verdict (`TestResults/9-qa-verdict.md`), technical-director's facts on #25 and #30 (task metadata, as sent by message).
- A sample-count band is invalid on a fixed-duration walk: it voids faster arms. Judge route validity by speed and duration against the set's own A.
- A median-only arm test hides a cut in the tail (LAYER's p95 of visible skinned meshes went 49 to 3 while the median stayed 3). Test p95 or max as well.
- A coroutine reading per-frame data between `Update` and `LateUpdate` reads cleared state. Fish counts must be read after `FishRenderer` (order 1000); `FishDrawCounter` does it at order 10000. `BellsBendFpsWalk.CountFish` still has the flaw (issue draft `.studio/drafts/issue-f9-fish-counter.md`).
- Open harness defects: the edge-still aim has no line of sight and the player body is in frame, and edge pairs have no same-frame repeat.
- Rules for reruns and pre-data facts: [[0027-reruns-replace-and-pre-data-facts-are-blind]].

## Does not apply when
Measuring an ON/OFF cost gate (use 0016 alone), or a development-build run: its frame times are never compared with release figures.

## Proposed addendum (not yet ratified): S1 for build-vs-build comparisons
> [!warning] Proposal
> Written 2026-11-13, before any build-vs-build data exists: nobody writing it has seen R1, R2 or R6 numbers ([[0027-reruns-replace-and-pre-data-facts-are-blind]] item 2). It is not binding until technical-director rules on it and the Studio Head accepts it. It then becomes its own decision note, and this section points there. Drafted by the Phase 0 harness workstream; technical-writer to place or reword.

1. **Scope.** A and B are two player builds (for example R1 per-tile layers, R2 masks, R6 trees as GameObjects), so the "same exe hash" part of test 1 cannot hold. Everything else in [[0025-savings-rule-s1-perf-trials]] stands: p95, d_r, S_min = max(0.20 ms, 2% of the mean A-build p95 at the spot), tests 3 and 4, COST, NO SAVING, INCONCLUSIVE, and 0027 (reruns replace, never pooled). It covers static spots and the route.
2. **Test 1b, the pinned pair (replaces "same exe hash").**
   - Before the first launch the plan names H_A and H_B: each build folder's hash (sha256 over sorted "relpath, sha256" of every file, as `BuildHash` in `tools/perf/frustum_matrix.ps1`), plus each exe sha256 and `WashedAshore.Perf.dll` sha256.
   - Every launch's `run.json` build hash equals its arm's pinned hash, and the player's `exe_sha256` matches the runner's. Both folders are hashed before the first and after the last launch; any change voids the session.
   - Same harness: the two `WashedAshore.Perf.dll` hashes are equal, unless the plan names a harness change. `useDeterministicCompilation` is 1 (`ProjectSettings.asset`). If the dll hashes differ while `git hash-object` of every file under `Assets/Scripts/Perf/` is the same at both commits, the pair stands and the report records the mismatch as compiler non-determinism. If the sources differ and the plan does not name the change, the pair is void.
   - Both builds: same Unity version, release (not development), the same build method and options, the same machine, and frame-timing stats on in both.
   - The `Machine()` header fields match across arms: gpu, graphics_api, cpu, quality, lod_bias, texture_mip_limit, resolution, window_mode, vsync, frame_timing_stats. Since Phase 0 the header records the authored lodBias and mip limit, so a trial flag cannot change it.
3. **What is recorded** (`build-diff.json` in the run folder, written before the first launch):
   - both build provenance files (commit, dirty files, size, Unity version, options);
   - both folder hashes and the per-file differences between the folders (added, removed or changed relative paths);
   - `git diff --stat` and the full diff of `ProjectSettings/`, `Assets/Settings/` and `Packages/` between the two commits;
   - the QualitySettings, URP asset and renderer values read back in each build's windows.
   - The plan lists the differences it expects. An unexpected one (another Unity version, scripting backend, development flag, or a changed setting the plan does not name) voids the pair.
4. **Interleaving.** One spot per launch, authored windows only (no `-fcVariant`), so A and B differ only by build. Per spot: one discarded warm-up launch of each build, then A B A B A B A (7 launches). Run r: d_r = mean(p95 of the A launch before, the A launch after) minus p95 of B launch r; the bracket is |A before - A after|. Spot order rotates per run as in the #9 matrix. A launch is valid with the DONE line, at least 600 samples, the runner's record and `valid:true`. A failed launch is retried once in place; if it is still invalid, that run is INCONCLUSIVE, and runs are never re-paired.
   - **Route:** the same A B A B A B A launch order on `-bbFpsWalk`. Test 1 is technical-director's addendum 6 as used for route set 2 (speed and duration against the set's own A, no sample-count band). The route arm test is test 2b below.
5. **Test 3 with launch brackets is untested at static spots (assumed, stated before data).** Static-spot brackets so far were in-session; in #9 the largest reached 1.164 ms (park SHADOW), with 1.023 ms (fish_water FISHOFF), 0.994 ms (ridge_top LAYER) and 0.884 ms (cab SHADOW) (`TestResults/9-analysis.md`, max bracket column). Launch-to-launch brackets were measured only on the route, set 2: at most 0.131 ms (FAR) and 0.198 ms (LAYER). If static launch brackets are as wide as the in-session ones, a build change that saves less than about 1 ms is likely INCONCLUSIVE by design; that cost is accepted now. Reporting, fixed now: every run's bracket is printed beside its d_r; a run that fails test 3 makes the spot INCONCLUSIVE with the reason "launch bracket"; that spot gets one rerun under 0027, then goes to the Studio Head. Test 3 is never relaxed after data.
6. **Test 2b, the arm is the arm.** For each change the plan pre-registers a readback or counter that must differ between the builds (for example R1: mean terrain layers per tile lower, or SetPass calls lower; R6: census MeshRenderer count up and terrain tree instances down). Enabled renderers stay within +-2% unless the plan says the change adds or removes renderers. A valid pair whose named measure did not move is NO EFFECT.
7. **What A is.** A is the base commit rebuilt with the same settings as B, in the same block. Builds from before Phase 0 lack frame-timing stats, so the #9 build (`b7843ce933ab`) is not a valid A. New Phase 1 spots (`ridge_c2`, `shore_mid`, `shore_mcCord`) have no #9 numbers; their A is the rebuilt base build at that spot. This item does not change the approved Phase 1 gate ("no COST beyond 2 ms against the #9 baseline"). Whether that gate is measured against the #9 build or against the rebuilt A is an open question for the Studio Head (the #9 build has frame-timing stats off and none of the Phase 0 recorder's per-frame work); this item will cite the answer once it is given.
8. **Secondary measures.** GPU frame time and memory from the Phase 0 recorder are reported and descriptive (`tools/perf/frustum_resources.py`). They count only if a plan pre-registers them with their own threshold. GPU time is null where the API gives none and is never read as 0.
9. **Cost (assumed; time the first block).** About 9 launches per spot (7 plus 2 warm-ups).

## Related
[[0016-frame-time-noise-rule-a1]], [[0025-savings-rule-s1-perf-trials]], [[0026-agents-may-launch-the-player]], [[0027-reruns-replace-and-pre-data-facts-are-blind]], [[frustum-culling-investigation]]
