# #9 QA verdict (qa-lead, task #32)

2026-11-13. It grades issue #9's pass criteria Q1-Q6 and its automatic-FAIL list against the final state:
- **Note:** `_bmad-output/poc/perf-frustum-note.md`, blob `c6e108c4`. qa-lead #29 PASS on c6e108c4; technical-director #30 PASS on c6e108c4 (the #9 gate). c6e108c4 differs from b0bdfce9 only by text applying the Studio Head's ruling item 4, with every figure identical (my diff against a saved copy of b0bdfce9).
- **Analysis:** `TestResults/9-analysis.md`, blob `e3a533a1`, from `tools/perf/frustum_analysis.py`, blob `52ab022c`. qa-lead #27 PASS; my rerun reproduces it byte for byte.
- **Runs:** `TestResults/9-runs/` 20261112-1803 (main), 20261112-2034-dev, 20261112-2248-route2, 20261113-1342-rerun.
- **Build and plan:** release build `b7843ce9` (208 files); plan `710f0d11` with addenda 1-7 (note, "Plan addenda and rulings").

My draft (`TestResults/9-qa-verdict-draft.md`) was written before I read technical-director's or skeptic's findings. The comparison with them is at the end.

## Verdict: #9's investigation is complete. Q1, Q2, Q4 and Q6 PASS; Q3 and Q5 INCONCLUSIVE. No automatic FAIL.

The INCONCLUSIVE grades are measured outcomes under the pre-set rules, not gaps in the work. Each is stated in the note and listed under "Not measured".

| # | Grade | Evidence | Stated gaps |
|---|---|---|---|
| Q1 submitted against visible | **PASS** | 9-analysis.md "Q1 census", "Q1 on the route" and "Q1 engine counters": per spot and heading enabled 551, submitted 6-75, fog-visible 5-34, bands; route waypoints submitted 16-59; SetPass 100-130, triangles 4.7-19.2 M, shadow casters 122-380 | Batches and draw calls aren't exposed (null in the release player, 0 in dev). "Submitted" is a CPU emulation of the camera rule; engine-side culled counts are unmeasured (GRD counters 0 in all 35 dev windows). Note Q1, Not measured item 7. |
| Q2 draw distance against fog | **PASS** | FAR 600 m (cab 1150) against 1000 m, 3 runs × 5 spots + route set 2. SAVING park 2.63/2.97/3.15, ridge_top 2.70/3.26/3.54, river_bank 1.60/1.91/2.10, valley_floor 1.63/2.41/2.57 ms p95 (my raw recompute matches); cab COST; route set 2 1.89/1.87/1.82 under addendum 6c (accepted with disclosure). Median/p95/p99 per spot; still pairs per heading (1,170 files). #28: visible at park, ridge_top, valley_floor (I confirmed ridge_top h000 by eye); river_bank sea-facing headings show no change, but no renderer was removed there. | No smallest passing distance was measured (only 600 m); the zone/view clip is unmeasured (Not measured items 2-3). |
| Q3 per-layer cull distances | **INCONCLUSIVE** | LAYER Wildlife+Birds 400 m: NO EFFECT under the pre-set median test, but the cull removed up to about 46 skinned meshes at p95 (ridge_top and cab 43-49 → 3), with no measured saving: "counters only". SMALLMESH (the small-props lever GRD honours): NO EFFECT. FISHOFF (fish upper bound): INCONCLUSIVE in set 1 and again after the §5 rerun (d −0.01/−0.12/+0.66, max bracket 1.02). The Studio Head accepted it as INCONCLUSIVE ("Accept, finish #9", 9-studio-head-rulings.md item 4). | Details (70 m) not run (plan section 3). The 400 m pop-in check wasn't done where the cull acted (no LAYER edge still). The SMALLMESH edge frame can't show its target. Fish is INCONCLUSIVE. Not measured items 4, 8, 9. |
| Q4 oversized bounds | **PASS** | 9-culling-census.json: 20 renderers > 200 m, all 1024 m water quads (4 vertices, shadows off); 4 defeat culling (≥ 50% of 96 views); terrain tiles aren't Renderers and are culled per patch | The edit-time census covers 517 of the player's 551 renderers. The 34 run-time renderers are wildlife and birds, assumed under 200 m (indirect evidence: their submitted counts vary 0-18 by heading, so they are frustum-culled). Timing not run (plan section 3). |
| Q5 shadow casters | **INCONCLUSIVE** | Shadow distance 50 m applied (352/352 readbacks, PC_RPAsset 50 m, **4 cascades**, not 2 as the issue says). Shadow-pass cost: COST at river_bank (0.282/0.546/0.546, mean 0.458 ms); INCONCLUSIVE at park, ridge_top, valley_floor and cab in set 1 and again after the §5 rerun. The Studio Head accepted these as INCONCLUSIVE, with no third set and no GPU-timing follow-up in #9 (item 4) | Engine-side caster culling at 50 m is not verified (engine count 122-380 vs census 2-13, cause assumed). The pass cost is measured at 1 of 5 spots; GPU timing needs a ProjectSettings change (Not measured items 10-11). |
| Q6 the recommendation | **PASS** | Note Q6: SAVING rows only, with measured p95 from named runs, risk (incl. #28) and effort. Result: **no change is recommended as measured**. A global 600 m clip is visible at 3 of 4 spots; a zone/view clip is an unmeasured follow-up. | None for the criterion. |

## Automatic-FAIL list

| Line | Result | Evidence |
|---|---|---|
| A recommendation with no measurement behind it | **Not triggered** | The note recommends no change. Every idea without a measurement is in "Not measured" (13 items), marked assumed or follow-up. Route FAR's SAVING rests on 6c, accepted by the Studio Head with disclosure (9-studio-head-rulings.md item 2), and it isn't recommended. |
| Committing a live setting change | **Not triggered** | No commits: HEAD is still `eaf238d`. `git status -- ProjectSettings Packages` is clean. Scenes, prefabs, materials, URP and terrain: only `Assets/World/Lookout/MC39_Glass.mat` `_SrcBlend 5→1`, already present at pod start (9-qa-baseline.md) and kept by the Studio Head (9-studio-head-rulings.md:11). Every trial was runtime-only and refused in the Editor (FrustumProbe.cs Boot). |
| Grading the web build | **Not triggered** | Every run is the Windows release or dev player; note "Not measured" item 13. |

## Process checks
- **Decision 0026 (agent launches):** started-by.txt is present in every run folder I graded, with the Studio Head's OKs (9-studio-head-rulings.md). The script took and released the lock. Two condition-2 breaches are disclosed in the note: platform-tools-engineer's Python during set 1 (s01-r1-park windows 0-3, kept under technical-director's ruling), and qa-lead's (mine) during the rerun (before any timed window). Separately, I read a live run's matrix-progress.txt during fish sample 1232, which was later voided for the counter bug; reported to team-lead.
- **Rules set after the data:** only 6c, accepted by the Studio Head with disclosure. Addenda 6, 6b and 7 and the rerun were set before their data (set 2 started 04:48:59Z after the 03:05Z rulings; the rerun at 19:42:12Z after addendum 7).
- **Build identity:** one build hash, b7843ce9, in every graded run.json; no launch changed the player folder.

## Against the reviews
- **technical-director #30:** PASS on b0bdfce9. It agrees with my grades in substance: the savings were recomputed, the 5 rerun rows replaced, Q6 unchanged, the after-data addendum disclosed. No difference in outcome.
- **skeptic #31:** PASS, holding on 949d5e96. Its open item was the rerun, now done and disclosed. Its findings, each reproduced by me from the raw data:
  - 1, river_bank FAR removes nothing at h000/h090: fixed.
  - 2, LAYER's p95 cut: fixed as "counters only". **My earlier #29 PASS missed this.**
  - 3, Q5 circular: fixed.
  - 4, FAR triangles +3.6-4.2%: fixed.
  - 5, the §5 rerun: done.
  - 6, Q4 covers edit-time only: disclosed.
- **Where I differ:** none in outcome. Neither reviewer grades Q1-Q6 individually. My Q3 and Q5 INCONCLUSIVE grades are stricter than "the note is right", and they rest on the issue's literal criteria (details, the pop-in check, shadow culling verified at 50 m).

## Open findings: none against the note. For the Studio Head
1. Decided: the 5 rows still INCONCLUSIVE after the §5 rerun (SHADOW at park, ridge_top, valley_floor and cab; FISHOFF at fish_water) were accepted as INCONCLUSIVE ("Accept, finish #9", 9-studio-head-rulings.md item 4, about 21:05Z). No grade changes: Q3 and Q5 stay INCONCLUSIVE.
2. Which follow-ups to raise (note "Not measured" items 1-3: the FAR main-thread spike cause, a zone/view clip, a smallest passing distance).
3. MC39_Glass.mat stays out of the #9 commits (his earlier ruling).
