---
type: feature
status: in-progress
tech: [perf]
area: [terrain]
issues: [9, 10, 11]
prs: []
source: "issue #9; pod tasks #23-#33 (studio task list); _bmad-output/poc/perf-frustum-note.md blob b0bdfce9; TestResults/9-qa-verdict.md (qa-lead, #32); TestResults/9-analysis.md blob e3a533a1; release build b7843ce9"
created: 2026-11-13
updated: 2026-11-13
---
# Frustum culling investigation (#9)

_Measured whether culling in `World.unity` does the most it can. Result: **no change is recommended as measured.** The one real saving, a 600 m far clip, changes the skyline at 3 of 4 ground spots. Q3 and Q5 are INCONCLUSIVE, and the Studio Head accepted that and finished #9. No live setting was changed; nothing is committed._

Full results and figures: `_bmad-output/poc/perf-frustum-note.md` (link only, not copied). Verdict: `TestResults/9-qa-verdict.md`. Method and tools: [[frustum-culling-harness-and-measurement-rule]].

## Shipped (working tree, not committed)
- The harness and measurement scripts (`Assets/Scripts/Perf/FrustumProbe.cs`, `tools/perf/frustum_matrix.ps1`, `frustum_analysis.py`, `popin_diff.py`) and the static census `TestResults/9-culling-census.json`.
- qa-lead's verdict (#32, 2026-11-13): **Q1, Q2, Q4, Q6 PASS; Q3 and Q5 INCONCLUSIVE; no automatic FAIL** (no recommendation without a measurement, no live setting committed, web not graded). Technical-director #30 and skeptic #31 PASS on the note.
- Runs: release build b7843ce9, 5 spots plus the walking route, 3 runs each, plus a rerun (`TestResults/9-runs/`).

## What was found (all from the note and 9-analysis.md)
- **600 m far clip instead of 1000 m:** saves 1.6-3.5 ms p95 at all four ground spots, about 1.9 ms on the route (only under addendum 6c). technical-artist (#28): skyline changes at park, ridge_top and valley_floor. river_bank passes only on its sea-facing headings, where FAR removed no renderer. A zone or view-based far clip is an unmeasured follow-up.
- **No saving:** GRD small-mesh cull at 1%, GRD occlusion culling (NO EFFECT). Wildlife and birds cull at 400 m: NO EFFECT under the pre-set median test, though it cut up to about 46 visible skinned meshes on part of the sweep ("counters only").
- **Q4:** 20 renderers over 200 m, all 1024 m water quads; 4 defeat culling. Edit-time census only (517 of 551 run-time renderers).
- **Q5:** shadow distance 50 m applied, shadow-pass cost 0.46 ms at river_bank (COST).

## Corrections to the issue text (qa-lead, from the code)
- **4 shadow cascades** apply (`Assets/Settings/PC_RPAsset.asset:57-58`), not 2. The 2 is QualitySettings, which URP ignores.
- **Fish are drawn procedurally** (`Graphics.RenderMeshInstanced`), so the census and camera layer culls do not reach them.
- **Terrain tiles are not Renderers**, so they are absent from Q4; the terrain system culls them per patch.
- **GPU Resident Drawer ignores `Camera.layerCullDistances`**, so layer culls cannot reach the 479 static props it draws.

## Open
- **Accepted as INCONCLUSIVE:** five rows still INCONCLUSIVE after the plan section 5 rerun (SHADOW at park, ridge_top, valley_floor, cab; FISHOFF at fish_water). The Studio Head ruled "Accept, finish #9" (`TestResults/9-studio-head-rulings.md` item 4): no third set, no GPU-timing follow-up asked for in #9.
- **Not measured** (note "Not measured", 13 items): engine-side shadow-caster culling at 50 m (engine count 122-380 against census 2-13, cause assumed); LAYER pop-in where the cull acted (no edge still); shadow cost at 4 of 5 spots; smallest passing far distance; the FAR main-thread spike cause; details at 70 m; GPU timing (needs a ProjectSettings change).
- **Issue to post (draft):** `.studio/drafts/issue-f9-fish-counter.md`. `BellsBendFpsWalk.CountFish` reads fish draw modes before the renderer fills them, so past `fishDrawnMax` values are suspect. The #9 run used `FishDrawCounter` (order 10000) instead.
- Follow-ups, none started: FAR main-thread spike cause, a zone or view clip (unmeasured), a smallest passing distance. Reuse the harness for #10 and #11.

## Rulings
[[0025-savings-rule-s1-perf-trials]] (saving rule), [[0026-agents-may-launch-the-player]] (agents may launch the player, amended: no ask for pre-reviewed runs), [[0027-reruns-replace-and-pre-data-facts-are-blind]]. Standing: [[0016-frame-time-noise-rule-a1]].
- Disclosed: addendum 6c was set after the rule-setter saw set 1's route numbers; the Studio Head accepted it with disclosure (`TestResults/9-studio-head-rulings.md` item 2).
- Disclosed: platform-tools-engineer and qa-lead each ran a process during a timed block, against 0026's no-agent-processes condition (qa-lead's verdict, "Process checks").

## Patterns
[[frustum-culling-harness-and-measurement-rule]], including the lessons from this pod.
