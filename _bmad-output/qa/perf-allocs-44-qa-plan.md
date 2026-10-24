# QA plan: GitHub #44 physics allocations (bounds clamp, robin planner)

Draft 2026-10-23 00:12 CDT, qa-lead. Spec: issue #44 (`gh issue view 44`), pass criteria A1-A5; `docs/physics-guidelines.md` rule 17. Baseline = `main` at e6ad92d (#43 merged). Evidence root `TestResults/physics-allocs-44/{before,after}/`.
Rules: one Unity process at a time; probes and harness tests are staged outside Assets/ and landed only in a producer slot; restore churn (ProjectSettings.asset, QualitySettings.asset, MC39_Glass.mat) after every run.
Known flake, not a #44 blocker unless #44 is shown to cause it: `BirdFrameTimeTests.BirdsCostAtMostTenPercentFps` (10 percent Editor gate, #51 / B8).

## Automatic FAIL (checked by diff review)

Any change to the bounds rule (`WorldBoundsRule`, `TryConstrain`, snap target, ground offset, velocity handling in `SnapBody`) or to the robin flight rules (`RobinTuning` values, `Offsets`, distances, `GroundOk`, `LandingOk`, `Clear` thresholds, rng call order). qa diffs `WorldBoundsClamp.cs`, `RobinFlightPlanner.cs`, `RobinAgent.cs` against main and lists every changed line that is not allocation plumbing.

## Criteria

| ID | Check | Method | Evidence |
|----|-------|--------|----------|
| A1 | Rigidbodies register for the clamp (component or registry, OnEnable/OnDisable); no `FindObjectsBy*` in any Update/LateUpdate | `grep -rn "FindObjectsBy\|FindObjectOfType\|FindGameObjectsWithTag" Assets/Scripts` and read each hit's caller; diff review. Hazard to test: a Rigidbody added at runtime WITHOUT the registering component (a plain `AddComponent<Rigidbody>()` as `WorldBoundsClampTests` does today) must still be clamped, or the spec's "register themselves" must be met by an automatic path (a base prefab component, or a registry hooked from the body). The engineer states which; qa tests a runtime-spawned body, a disabled body (must not be clamped, must not throw), a destroyed body (registry holds no null), and re-enable | grep output, diff, `after/playmode.xml` |
| A2 | `SphereCastNonAlloc` with a static buffer for the worst case; a test shows a full buffer is detected, not silently dropped | Diff review: no `SphereCastAll`; buffer size is a named constant with the worst-case reasoning. EditMode or PlayMode test: fill the buffer (more overlapping colliders than the size) and assert the planner reports blocked (a path through a full buffer is rejected). Also assert that hits on the `ignore` hierarchy do not hide a real obstacle when they fill the buffer (the old code filtered ignored hits after the cast: with a fixed buffer the ignored colliders can crowd out the blocker) | diff, new test in `after/editmode.xml` or `playmode.xml`, qa forced-failure run of that test on a version that drops hits silently |
| A3 | 0 B GC alloc from `WorldBoundsClamp` and the robin planner while robins flush | Method below (agreed with layers-engineer) | `after/alloc-*.txt`, XML |
| A4 | Clamp still works: B5 sweep passes; a Rigidbody north of the line is snapped back | `WorldBoundsSweepTests` and `WorldBoundsClampTests` (incl. the Rigidbody case) in full PlayMode; `WorldBoundsBackstopTests` EditMode; plus A1's runtime-spawn case | `after/playmode.xml`, `after/editmode.xml` |
| A5 | Robin behaviour unchanged | Same-seed before/after path comparison (below) as the main evidence, plus bird tests green | `before|after/robin-paths.csv`, diff, bird tests in `playmode.xml` |

## A3 capture method (proposal for layers-engineer)

Two measurements, both in PlayMode on the World scene, robins flushing for real:

1. Direct, deterministic: `GC.GetAllocatedBytesForCurrentThread()` read before and after, over N calls, around (a) `RobinFlightPlanner.Plan` with a warmed, reused `List<Vector3>` path and (b) one `WorldBoundsClamp` LateUpdate with at least 20 registered bodies. Warm-up calls first (JIT, buffers). Asserted delta = 0. This is the pass/fail number. Plan allocations other than the cast (the `new[]` at line 52, enumerator or closure allocations, `Quaternion` boxing) all show up here, so the test also covers "Line 57 allocates `new[]`".
2. Profiler capture, supporting evidence: `ProfilerRecorder` on `ProfilerCategory.Memory` "GC Allocated In Frame" per frame over a window in which robins flush (the B4 approach drives a real flush), reported next to a frame-matched BEFORE capture. Frame-level GC includes other systems, so it is reported, not asserted at 0. Optional if (1) is clean: a Profiler.BeginSample around the two call sites and the Hierarchy "GC Alloc" column.

Both must cover WorldBoundsClamp and the robin planner. A deliberate BEFORE run of (1) on main must show non-zero for both (the positive control), so a zero AFTER is not a test that reads nothing. Editor PlayMode only; label "Editor counts, not player allocations".

## A5 same-seed path comparison (qa-owned harness, staged outside Assets/)

Explicit PlayMode test `RobinPlanTrace`: for seeds 101, 202, 303 load World with the same pinned frame step as the bird tests, then for every robin and for 8 fixed threat bearings at 6 m, call `RobinFlightPlanner.Plan` with its own site, ground, tuning and mask (private fields via reflection) and `new System.Random(seed * 1000 + index)`, and write `seed,robin,bearing,landing,pointCount,x,y,z...` at 1e-4. Run on main (BEFORE) and on the engineer's build (AFTER); the two CSVs must be byte-identical (diff, sha256). Also `RobinAgent` flush-order independence: the planner must consume `rng` in the same order, so the CSV also records the next `rng.NextDouble()` after each call. Any difference goes to td with the first differing row before any ruling. Deterministic physics (static colliders) is assumed; if the BEFORE CSV differs between two BEFORE runs, qa reports that first and the comparison falls back to the bird tests plus the B4 probe lines.

## Test runs (AFTER, one Unity process at a time, standing GO)

1. Probe and harness staged, hashes to td and the producer; land in a slot.
2. BEFORE on main: EditMode, PlayMode full (about 45-55 min), A3 positive control, `robin-paths.csv`.
3. Engineer's build lands. AFTER: EditMode, PlayMode full, A3 measurements, `robin-paths.csv`.
4. Grade: baseline reds by name, clause and numbers (td add3 rule, noise bands from the #43 record); the 43 known PlayMode and 21 known EditMode reds are the baseline; `BirdFrameTimeTests` is graded as the known flake unless evidence ties it to #44; any other new red fails.

## Report

`_bmad-output/qa/perf-allocs-44-qa-report.md`: each A1-A5 and the automatic-FAIL check with its evidence path, verdict PASS / PASS WITH NAMED FOLLOW-UPS / FAIL, sent to layers-producer; sign-off stored at studio:qa `signoff/perf-allocs-44`; bugs under `bug/<id>`.
