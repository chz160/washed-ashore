# Draft: S1 addendum for build-vs-build comparisons

Status: draft for technical-director (ruling) and technical-writer (vault). ACK?
Source: Phase 0 of the high-res upgrade (branch phase0-hires, 2026-11-14). Moved out of
`docs/washed-ashore-obsidian-vault/patterns/frustum-culling-harness-and-measurement-rule.md`, because only technical-writer edits the vault.

Owed to technical-writer in the same note:
- "Why it happens", Counters bullet: "GPU ms is null (frame-timing stats are off...)" is no longer true; frame-timing stats are on since Phase 0.
- Variant keys and Route run bullets: add mipLimit, lodBias, aa, depthPrime, post, the -fcMipLimit/-fcLodBias/-fcAA/-fcDepthPrime/-fcPost flags, the resources block and twin_skipped.
- Release players report no Gfx memory (memory.gfx_used is null); MSAA memory needs a development player.
- A log.md entry.

Noah decided (2026-11-14): Phase 1 compares against a freshly rebuilt A build, not the #9 build; the post= key stays.

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

