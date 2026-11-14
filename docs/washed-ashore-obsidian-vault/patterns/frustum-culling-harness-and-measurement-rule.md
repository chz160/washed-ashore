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

## Related
[[0016-frame-time-noise-rule-a1]], [[0025-savings-rule-s1-perf-trials]], [[0026-agents-may-launch-the-player]], [[0027-reruns-replace-and-pre-data-facts-are-blind]], [[frustum-culling-investigation]]
