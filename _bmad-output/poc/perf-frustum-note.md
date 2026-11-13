# Frustum culling in World.unity: is it doing the most it can? (#9)

Recommendation note for issue #9 (Q6), written by platform-tools-engineer, 2026-11-13. It measures and recommends; **no live setting was changed**.
- Plan: `TestResults/9-measurement-plan.md`, revision 2, git hash-object `710f0d11`, with technical-director's addenda on #25 (listed one by one under "Plan addenda and rulings").
- Every figure below comes from a named run and file. Anything not measured says "assumed" or sits in the "not measured" list.

## Short answer
- **What was measured:** five culling levers at five fixed spots and on the walking route.
  - Only one saves frame time: **a 600 m far clip in place of 1000 m**, cutting p95 by 1.6-3.5 ms at every ground spot.
  - On the route it saves about 1.9 ms, a SAVING only under addendum 6c (see Disclosures).
  - GRD small-mesh culling and GRD occlusion culling are NO EFFECT under the pre-set test (median triangles not below both A windows).
  - Per-layer culling of wildlife and birds at 400 m is NO EFFECT under the pre-set median test, but it did cut visible skinned meshes on part of the sweep (Q3). There's no measured saving.
  - Whether other culling levers not trialled here would save time isn't known.
- It **changes the skyline** at three of the four ground spots: technical-artist's #28 verdict is FAIL at park, ridge_top and valley_floor.
  - river_bank passes: on its two sea-facing headings (h000, h090) the 600 m clip made no visible difference (technical-artist). The census shows FAR removed no renderer there (8 → 8 and 6 → 6 in every run), and any per-pixel clipping of the water past 600 m is unverified.
  - Its other headings (h180, h270, own) pass only because nothing past 600 m is in view (closed forest).
- **So no far-clip change is recommendable as measured.** A global change fails visibly, and a zone- or view-based far clip is an unmeasured follow-up (see "Not measured").
- No other change saves time:
  - per-layer cull distances for wildlife and birds: NO EFFECT under the pre-set median test; the cull removed up to about 46 skinned meshes on part of the sweep at ridge_top and cab (visible skinned meshes p95 43-49 in A against 3 in LAYER; river_bank 4-9 against 3); d -0.45 to +0.64 ms, descriptive, not ruled;
  - GPU Resident Drawer small-mesh culling at 1%: NO EFFECT;
  - GPU Resident Drawer occlusion culling: NO EFFECT;
  - the turned-off-everything readings (shadows off, fish off): costs or INCONCLUSIVE, never candidates. The INCONCLUSIVE ones were rerun once as plan section 5 requires, and are still INCONCLUSIVE; the Studio Head accepted them as INCONCLUSIVE (`TestResults/9-studio-head-rulings.md` item 4) (Q3, Q5).

## Runs and evidence
- **Player:** Windows release build `Builds/Windows-BellsBend-frustum9`, whole-folder build hash `b7843ce9…` (208 files), the same in every launch. Provenance: `Builds/Windows-BellsBend-frustum9.provenance.json`.
- **Runs** (all under `TestResults/9-runs/`):

  | Run | What | Result |
  |---|---|---|
  | `20261112-1803` | Main matrix: 15 spot launches + 3 fish_water + route census + 13 route walks | 32/32 ok, 176 windows, 0 invalid; 1,705 stills, all 1920x1080 |
  | `20261112-2034-dev` | Development-build counter pass | 5/5 ok |
  | `20261112-2248-route2` | Route set 2 (addendum 6) | 13/13 ok |
  | `20261112-1243-fishsample` | Fish sample that chose the fish_water pose | — |
  | `20261113-1342-rerun` | Plan section 5 rerun of the INCONCLUSIVE SHADOW and FISHOFF launches (park, ridge_top, valley_floor, cab, fish_water × 3) | 15/15 ok; args byte-identical to set 1 |
  | `20261112-1018-posecheck`, `20261112-1029-posecheck` | Pose checks | — |
- **Analysis:** `TestResults/9-analysis.md` and `.json`, produced by `python tools/perf/frustum_analysis.py --run 20261112-1803 --dev 20261112-2034-dev --flag-window 2026-11-13T00:04:30Z 2026-11-13T00:06:05Z --route2 20261112-2248-route2 --accept-6c --rerun 20261113-1342-rerun`. Findings and rulings: `TestResults/9-analysis-notes.md`.
- **Visual verdicts:** `TestResults/9-popin-review.md` (technical-artist, #28), reproducible with `python tools/perf/popin_diff.py TestResults/9-runs/20261112-1803`.
- **Static census:** `TestResults/9-culling-census.json` (#24).
- **Harness:** `Assets/Scripts/Perf/FrustumProbe.cs` (the method is in its header) and `tools/perf/frustum_matrix.ps1`.
- **The rule** (plan section 5): `d = mean(p95 of the A windows either side) − p95(trial)`, all inside one launch, with three runs per spot. A **SAVING** needs every run valid, the trial to have measurably taken effect, each bracket smaller than its d, and the worst run ≥ S_min = max(0.20 ms, 2% of the spot's A p95). S_min came out at 0.20 ms everywhere.

## Q1: submitted against visible
Source: `9-analysis.md`, "Q1 census in the A windows" and "Q1 engine counters".
- **Method** (agreed in plan section 2):
  - **Enabled:** renderers that are enabled and active.
  - **Submitted:** enabled, the bounds meet the frustum, and the nearest view depth is within the far clip (CPU emulation of the camera rule).
  - **Fog-visible:** submitted, with nearest view depth under fog end (520 m).
  - Counted at five headings per window, after the timed sweep.
  - Occlusion isn't counted.
- **Enabled renderers:** 551 at every spot (the census found 517 at edit time; populations add the rest at run time).
- **Submitted per heading:** 6-75. Fog-visible: 5-34.
- **Submitted but fully fogged**, past fog end, still drawn:

  | Spot and heading | Submitted | Fully fogged |
  |---|---|---|
  | ridge_top 270 | 56 | 52 |
  | cab 270 | 68 | 52 |
  | ridge_top 180 | 75 | 45 |
  | valley_floor 090 | 31 | 25 |

  - The fogged band at ridge_top and the cab includes Wildlife (12-18 renderers) and LookoutTower (12-23).
  - At valley_floor 090 the NorthBarrier is 21 of the 25.
- **Terrain trees** in the frustum within the 450 m tree distance: 24-2,667 per heading. Trees aren't Renderers, so they show up only in the triangle counts.
- **Engine counters** (release player, median of A windows):

  | Spot | Triangles | Vertices | SetPass calls | Shadow casters | Visible skinned meshes |
  |---|---|---|---|---|---|
  | All spots (range) | 4.7-19.2 M | 6.7-26.8 M | 100-130 | 122-380 | 3-14 |

  - Park and valley_floor carry about 19 M triangles.
  - Batches and draw calls aren't exposed: null in the release player, 0 in the development player.
- **The route** (`9-analysis.md`, "Q1 on the route"):
  - The route census launch `c01-route-census` (one A window at each of two route waypoints) found 16-40 submitted per heading at outdoor_centre (12-29 fog-visible) and 16-59 at tower_base (5-16 fog-visible).
  - At tower_base h270, 54 of 59 submitted renderers are past fog end, and 18 of them are Wildlife.
  - The route's A walks (7 per set) have median engine counters of 10.1 M triangles, 14.1-14.2 M vertices, 129 SetPass calls, 453 shadow casters and 3 visible skinned meshes, the same in set 1 and set 2.
- **Not measured:** engine-side culled counts. The GPU Resident Drawer's own counters (visible instances, frustum, layer and LOD culled) read 0 in all 35 dev-pass windows. They're registered but never filled in that player. **The Q1 and Q5 figures are the CPU emulation; engine-side culling counts are unmeasured.**

## Q2: draw distance against fog
Sources: `9-analysis.md` "Spots" and "Frame time and counters per spot and variant"; `9-popin-review.md`.

**Result:** FAR (far clip 600 m at ground spots, 1150 m in the cab) against the authored 1000 m.

| Spot | Outcome | d per run (p95, ms) | A p95 → FAR p95 (median of runs) | technical-artist visible difference |
|---|---|---|---|---|
| park | **SAVING** | 2.63, 2.97, 3.15 | 8.33 → 5.18 | **FAIL** (skyline masses gone at h000, h090, own) |
| ridge_top | **SAVING** | 2.70, 3.26, 3.54 | 8.88 → 5.33 | **FAIL** (horizon band grows from about 30 to 75 px; fogged hills gone) |
| river_bank | **SAVING** | 1.60, 1.91, 2.10 | 8.09 → 6.00 | **PASS**: on the sea-facing h000 and h090 FAR removed no renderer, so nothing measurable was cut there (per-pixel water clipping past 600 m unverified); vacuous on h180, h270 and own (closed forest, nothing past 600 m in view). See the census note below |
| valley_floor | **SAVING** | 1.63, 2.41, 2.57 | 9.07 → 6.66 | **FAIL** (h000 fogged mass replaced by sky) |
| cab (1150 m) | **COST** | −1.40, −1.27, −1.49 | 8.49 → 9.85 | horizon edge moves about 8 px; a cost reading, not a candidate |
| route set 2 (walk) | **SAVING** under addendum 6c (NO EFFECT under the original test 2; see Disclosures) | 1.89, 1.87, 1.82 | A 9.10-9.34 → FAR 7.28-7.32 | not stills-checked |

**What the saving is:**
- Mostly the slow tail, in main-thread time. At park the median goes 5.15 → 4.58 ms while p95 goes 8.33 → 5.18 ms, and CPU main-thread time tracks frame time.
- **FAR doesn't lower triangles; it raises them at most spots** (median over runs, `9-analysis.md` "Frame time and counters"):

  | Location | Triangles A → FAR (M) | Change |
  |---|---|---|
  | ridge_top | 7.44 → 7.75 | +4.2% |
  | river_bank | 5.35 → 5.54 | +3.6% |
  | park | 19.18 → 19.27 | +0.5% |
  | valley_floor | 18.98 → 18.97 | −0.1% |
  | cab (1150 m) | 4.70 → 4.62 | −1.6% |
  | route, A walks against FAR walks (`frustum-route.json` medians) | set 1: 10.07 → 10.32; set 2: 10.09 → 10.32 | +2.5%; +2.3% |

  Vertices rise 0.7% at park.
- **The river_bank census** (FAR window against the A before it, all three runs; `s02`, `s09`, `s15`):
  - At the sea-facing h000 and h090, FAR removes **nothing** from the submitted set.
  - It removes 10-20 Wildlife and Birds renderers at h180 and 2 Water quads plus 2 terrain tiles at own. Birds drop at h270 in one run.
  - Those are the closed-forest headings, where that content is hidden from view.
  - Assumed, not measured, and only for river_bank: its saving may come from that content past 600 m on headings where it isn't visible.
    - d is one p95 over the whole sweep, and occlusion isn't computed, so the census can't attribute the saving to any heading or renderer.
    - At park, ridge_top and valley_floor the saving comes with visible removals.
    - This is a lead for the main-thread-spike follow-up ("Not measured", item 1), not a finding.
- So the 1000 m view carries periodic main-thread spikes that a 600 m far plane avoids. **Which system causes them** (terrain, trees, culling or something else beyond 600 m) **wasn't measured**; see follow-ups.

**Visible difference:**
- The fogged far terrain and forest are paler than the sky at the horizon. Cutting them changes the skyline silhouette, even though everything past 520 m is fully fogged.
- technical-artist's FAIL rule (#28) and its stills are in `9-popin-review.md`, for example `s04-r1-ridge_top/ridge_top-r1-w01-FAR-hown.png` against its `-authored.png` twin.

## Q3: per-layer cull distances
Sources: `9-analysis.md` "Spots" and "Q3 cull-edge stills"; `9-popin-review.md`.

| Trial | What it changes | Outcome at every spot | Pop-in |
|---|---|---|---|
| LAYER: Wildlife and Birds at 400 m (planar) | Camera.layerCullDistances | **NO EFFECT under the pre-set median test; the cull removed up to about 46 skinned meshes on part of the sweep at ridge_top and cab (visible skinned meshes p95 43-49 in A against 3 in LAYER; river_bank 4-9 against 3); d -0.45 to +0.64 ms, descriptive, not ruled**. Plan section 5: counters only, no measured saving. Route set 2 also NO EFFECT under the median test | **Not checked where the cull acted.** The stills at the five headings sit at the noise floor (technical-artist PASS there), but the 400 m pop-in check wasn't done on the part of the sweep where skinned meshes were cut |
| SMALLMESH: GPU Resident Drawer small-mesh cull at 1% of screen height | URP asset `smallMeshScreenPercentage` (player memory only) | **NO EFFECT**: triangles not below both A; readback confirms 1% applied | none seen; edge stills show no drop-out |
| FISHOFF: Fish root inactive, at fish_water only | runtime SetActive | **INCONCLUSIVE after the §5 rerun; accepted by the Studio Head (rulings item 4)** (`20261113-1342-rerun`, replacing set 1 under addendum 7): d −0.01, −0.12, +0.66 ms, max bracket 1.02, so the brackets aren't under \|d\|. Fish drawn in the bracketing A windows on 0.168 / 0.166 / 0.166 of frames before and 0.494 / 0.489 / 0.508 after (both ≥ 0.10, so the arm held). Set 1, superseded: INCONCLUSIVE, d −0.04, −0.17, +0.21, with A shares 0.16 before and 0.49 after. The rise after FISHOFF is consistent with the fish respawning when the root is re-enabled (technical-director's reading; assumed) | n/a (an upper bound, never a recommendation) |

Why the layer and small-prop levers can't save:
- **Small props** (NorthBarrier 456 and LookoutTower 23 static MeshRenderers, census `renderers.by_root`) are drawn by the **GPU Resident Drawer, whose culler ignores `Camera.layerCullDistances`**. It tests only the camera's layer mask (`com.unity.render-pipelines.core/Runtime/GPUDriven/Culling/InstanceCuller.cs:427-435`).
  - So a layer cull can't reach them. The proposed runtime re-layering was declined for that reason (plan section 3).
  - GRD's own lever, the small-mesh cull, was measured instead: no effect.
- **Wildlife and birds** at 400 m: the cull took effect at the long-view headings.
  - Visible skinned meshes p95 went from 43-49 in A to 3 in LAYER at ridge_top and the cab (all 3 runs), and from 4-9 to 3 at river_bank. That's up to about 46 on part of the sweep (`9-analysis.md`, "LAYER: what the cull removed").
  - The census removes 13-18 Wildlife and 14-31 Birds renderers at h180, h270 and own (ridge_top: h180 and h270; cab: h180, h270 and own).
  - At park, skinned p95 is unchanged (A 27-36 against LAYER 27-31). The census counts of Birds and Wildlife per heading differ from the A window by −6 to +8 (LAYER minus A), in both directions (all 3 runs). Animals move between the two frozen frames, so that isn't a consistent cut. Up to 6 Birds fewer at some headings, but up to 8 more at others.
  - There's no measured p95 saving: ridge_top d −0.09 / 0.64 / 0.44 ms, cab 0.11 / 0.13 / −0.07 (descriptive).
  - The pre-set test 2 compares medians, which are 3 in both A and LAYER windows there. That median blind spot is technical-director's plan error; the label stays NO EFFECT under it.
  - The edge-still search ran once per launch, on the frozen frame after the sweep, and found no wildlife or bird renderer 300-500 m away at that moment. So there's no LAYER edge still, and LAYER pop-in is **not checked** where the cull acted.
- **Fish** draw through `Graphics.RenderMeshInstanced` with their own 25 m body cull (`FishTuning.cs:184`). No camera layer cull reaches them. FISHOFF bounds the most any fish cull could save at fish_water (fish_c1, `TestResults/9-shore-candidates.json`). Its d is within noise, but INCONCLUSIVE stays INCONCLUSIVE.
- **Details** (terrain detail objects at 70 m, inside fog start at 120 m): not run; see "not measured".

## Q4: oversized bounds
Source: `TestResults/9-culling-census.json` (`oversized`).
- The census checks the **edit-time** scene: 517 renderers. The 34 renderers that populations add at run time (551 enabled in the player) aren't covered; they're wildlife and birds, assumed to be well under 200 m each.
- **Only 20 edit-time renderers exceed 200 m on any axis:** the Water quads (`Water/Water_x*_z*`), each 1024 x 1024 m, 4 vertices, shadows off.
- **4 defeat culling:** their boxes meet the frustum in at least half of the 96 protocol views. `Water_x2_z2` 67/96, `Water_x2_z1` 59, `Water_x1_z1` 57, `Water_x1_z2` 48.
- Each costs one 4-vertex draw. Assumed, not measured: their real cost is screen-space water shading, which culling can't remove.
- No merged or tile-wide meshes besides water. Terrain tiles (20 x 1024 m) aren't Renderers and are culled per patch by the terrain system, so they aren't in the oversized list.
- Q4 timing was **not run** (plan section 3): culling can't remove the quads' on-screen pixels.

## Q5: shadow casters
Sources: `9-analysis.md` "Q5 shadows" and "Q5 census".
- **Applied settings:** URP shadow distance 50 m, **4 cascades** (not 2 as issue #9 says; the 2 is QualitySettings', which URP ignores), 2048 shadowmap (`Assets/Settings/PC_RPAsset.asset:57-58`).
- **The 50 m shadow distance is applied** (readback shadow_distance 50 in every window). **Engine-side caster culling at 50 m isn't verified.** The census applies the 50 m rule itself, so it can't confirm what the engine does: it finds 2-13 casting renderers per heading within 50 m of the frustum, against 8-52 submitted.
  - The engine's Shadow Casters Count (exposed in the release player) is larger, 122-380.
  - **Not verified; the gap is unexplained.** Possible causes, all assumed: terrain trees (not Renderers, so not in the census), casters outside the view that throw shadows into it, and the engine counting casters per cascade (4 cascades).
- **The shadow pass cost:** shadows off drops shadow casters to 0 and triangles by 22-57%.

  | Spot | Outcome (current) | d per run (ms) | Set 1 (superseded) |
  |---|---|---|---|
  | river_bank | **COST** (set 1; not rerun) | 0.546, 0.546, 0.282; mean 0.458 | — |
  | park | **INCONCLUSIVE after the §5 rerun; accepted by the Studio Head (rulings item 4)** (max bracket 1.16) | 0.80, 0.24, 0.35 | INCONCLUSIVE, d 0.31, 0.20, 0.15 |
  | ridge_top | **INCONCLUSIVE after the §5 rerun; accepted by the Studio Head (rulings item 4)** (max bracket 0.77) | 0.71, −0.04, −0.58 | INCONCLUSIVE, d 0.29, −0.03, 0.73 |
  | valley_floor | **INCONCLUSIVE after the §5 rerun; accepted by the Studio Head (rulings item 4)** (max bracket 0.55) | 0.27, −0.45, 0.47 | INCONCLUSIVE, d 0.54, −0.30, 0.35 |
  | cab | **INCONCLUSIVE after the §5 rerun; accepted by the Studio Head (rulings item 4)** (max bracket 0.88) | 0.25, −0.11, 0.31 | INCONCLUSIVE, d 0.20, 0.44, 0.75 |

  - The four INCONCLUSIVE spots were rerun once as plan section 5 requires (`20261113-1342-rerun`), and the rerun replaces set 1 for them (addendum 7).
  - They're still INCONCLUSIVE: the arm took effect (shadow casters 0), but the A windows either side differ by more than d.
  - Under section 5 they went to the Studio Head, who accepted them as INCONCLUSIVE (rulings item 4). The shadow pass's frame-time cost at those spots stays unmeasured; at river_bank it is about 0.46 ms.
  - Shadows-off is a cost reading, never a recommendation.

## Q6: ranked recommendations (SAVING rows only)
**No change is recommended as measured.** The pre-set Q6 rule recommends a SAVING row only with technical-artist's "no visible difference". The one SAVING row, FAR, doesn't have it where it was measured as a whole:

| Change | Measured saving (p95) | Visual verdict | Status | Effort if pursued |
|---|---|---|---|---|
| Global 600 m far clip | 1.6-3.5 ms at all four ground spots (run `20261112-1803`); 1.82-1.89 ms on the route (`20261112-2248-route2`, addendum 6c) | FAIL at park, ridge_top and valley_floor (skyline change); river_bank PASS | **Not recommended** (visible) | Low |
| A 600 m far clip limited by a zone or view rule | **Not measured.** No such rule was defined or timed. river_bank's 1.60 / 1.91 / 2.10 ms is a full 360° sweep, including forest headings | Cleared visually only for river_bank's sea-facing h000 and h090 (one pose, two headings), where FAR removed no renderer, so nothing measurable was cut there. technical-artist: the verdict "applies to sea-facing river_bank headings, not to river_bank-type views in general" (9-popin-review.md). Unmeasured extra risk: the skyline switching visibly as the far plane changes at each zone or view transition | **Follow-up, assumed** ("Not measured", item 2) | Medium (assumed): a runtime rule per zone or view, plus stills per zone and at each transition |

- The river_bank sea-facing pass doesn't generalise to other shore views without their own stills.
- On the sea-facing headings FAR removed no renderer (census), so the "no visible difference" there clears nothing that was measurably cut. Whether the water surface is clipped per pixel past 600 m is unverified.
- technical-artist notes that some PASS headings elsewhere (park h180/h270, valley_floor h090/h180/h270/own) may be vacuous in the same way; that wasn't checked.

Counters only, no measured saving: **LAYER** (Wildlife and Birds at 400 m) cut visible skinned meshes on part of the sweep at ridge_top, the cab and river_bank, with no measured saving and its pop-in not checked. SMALLMESH and GRDOCC moved no counter.

## Not measured (assumed or follow-up; not recommendations)
1. **What causes FAR's main-thread spikes.**
   - Follow-up: profile a 1000 m view in a development build to find the system that spikes when content past 600 m is in view.
   - Fixing that system could keep the 1000 m skyline and still take most of the 1.6-3.5 ms. Assumed: no measurement here.
2. **A zone- or view-based far clip** (600 m only in views where it makes no visible difference).
   - Assumed and unmeasured. Which views qualify is unknown: the only cleared views are river_bank's two sea-facing headings, and why they don't change is itself assumed.
   - Follow-up: define the rule, then time it with its own A/B over the views it would apply to, with stills per zone.
   - The 1.6-2.1 ms at river_bank was measured over a full sweep, not over sea-facing views alone.
3. **A smallest passing far distance**, for example a fog-matched 550-700 m or a per-spot value. Only 600 m was run.
   - Assumed by technical-artist: any distance short of the farthest visible content changes the skyline, because fog is paler than the sky.
   - Follow-up run needed before any value is claimed.
4. **Terrain detail distance (70 m):** not run. It sits inside fog start, so cutting it is a visible quality cut, not culling waste (plan section 3).
5. **Small props by a camera layer cull:** not run (the layer-16 re-layering was declined). The GPU Resident Drawer ignores layer cull distances by construction (InstanceCuller.cs:427-435).
6. **Oversized-bounds timing:** not run (Q4).
7. **Engine-side culled counts:** GRD counters unpopulated (Q1).
8. **A clean SMALLMESH pop-in frame:** see Disclosures.
9. **LAYER 400 m cull-edge pop-in:** not checked where the cull acted (ridge_top, cab and river_bank long-view headings). The only edge search was a frozen frame after the sweep, and it found no wildlife or bird renderer 300-500 m away at that moment (Q3).
10. **The shadow pass's cost at park, ridge_top, valley_floor and the cab:** not resolved by a frame-time A/B. Shadows-off was INCONCLUSIVE in set 1 and again after the §5 rerun (Q5).
    - A possible follow-up would be GPU or profiler timing of the shadow pass itself, in a development build with frame-timing stats, which needs a ProjectSettings change. The Studio Head requested no third set and no GPU-timing follow-up in #9 (rulings item 4).
11. **Engine-side shadow-caster culling at 50 m:** not verified (Q5). The engine's Shadow Casters Count (122-380) is far above the census's 2-13, and the cause is assumed.
12. **Baked occlusion culling:** none is baked (census `occlusion_culling.baked` false), and it wasn't trialled. GPU occlusion culling (GRDOCC) was measured: NO EFFECT.
13. **The web build:** not measured or graded (retired, #14).

## Who ran the measurements
Every run in this note was launched by **team-lead (an agent) under decision 0026 as amended**, not by the Studio Head as decision 0016 says. That's a departure from 0016 made by the Studio Head's ruling ("go ahead, run both back to back. you don't need to wait for me on these type of runs", `TestResults/9-studio-head-rulings.md`). Each run folder's `started-by.txt` records who launched it, the command, the build and any known extra load.

## Plan addenda and rulings (technical-director, #25 and #26, all set before the data they govern unless stated)
- **Addendum 1 (pose):** every window also takes a still and census at the pose's own yaw. Up to 3 ridge_top candidates were fixed before the pose check (`TestResults/9-ridge-candidates.json`).
  - It also set which headings count for Q2 and Q3 at each spot:
    - own yaw primary at every spot;
    - park, valley_floor and cab: all four compass headings (the cab's diff cropped to y 570-830);
    - river_bank: h000 and h090;
    - ridge_top: compass headings only if passed on the pose check.
- **Addendum 2 (pose):** river_bank candidates Shore[1..3]. Pass = the river is visible in the own-yaw still, and no single object within 5 m fills more than 25% of any still.
- **Pose winners:** ridge_top = ridge_c1 (568, -180, yaw 45); river_bank = river_c3 (1027.2, 13.9, yaw 321.1), on technical-artist's verdict.
- **N3 (pose rule):** a launch at a spot found under water or inside terrain is void and rerun with a corrected pose. It wasn't triggered.
- **Addendum 3 (fish):** FISHOFF moved off river_bank and valley_floor to a fish_water spot, run as A FISHOFF A × 3.
- **Fish scope:** a FISHOFF result bounds fish cost only at fish_water. The plan line "if FISHOFF saves less than S_min, no fish cull can" is withdrawn.
- **Addendum 4 (build identity):** each launch pins a hash over every file in the player folder and refuses on a change.
- **Addendum 5 (fish sample):** a per-frame fish count, plus a 60 s fish sample choosing fish_water (the first candidate with fish on ≥ 10% of frames). FISHOFF's arm test needs ≥ 10% in both bracketing A windows. The first sample was voided as an instrument correction (the counter bug); the 10% threshold was kept.
- **#26 repeat rule:** a spot launch with any invalid window, missing windows or no window file is repeated once at the end of the spot block. A repeat is never repeated. It wasn't triggered.
- **Addendum 6 (route set 2):** the 13 route walks rerun under a new test 1: DONE, valid, speed and duration checks, no sample band.
- **Addendum 6b:** the speed check is relative, within 5% of set 2's own A median. It replaced an absolute 4.75-5.25 band before set 2's data.
- **Addendum 6c:** route FAR's arm test is the far-plane readback. Set after set-1 data; see Disclosures.
- **Analysis rulings (#27):** route set 1 is descriptive only; park run 1 is kept; the GRD wording is as in Q1.
- **Addendum 7 (the section 5 rerun):** the Studio Head chose the rerun (`TestResults/9-studio-head-rulings.md` item 3).
  - It reruns all 15 launches at park, ridge_top, valley_floor, cab and fish_water, unchanged, on b7843ce9.
  - Only SHADOW (4 spots) and FISHOFF (fish_water) are ruled from it, and they replace set 1's rows, with no pooling of the two sets. Everything else in it is descriptive.
- **Studio Head ruling, item 4:** SHADOW (park, ridge_top, valley_floor, cab) and FISHOFF (fish_water), still INCONCLUSIVE after the §5 rerun, are accepted as INCONCLUSIVE: "Accept, finish #9". No third set and no GPU-timing follow-up are requested in #9 (`TestResults/9-studio-head-rulings.md` item 4).

## Disclosures
- **Addendum 6c (route FAR's arm test)** was set by technical-director **after it had seen route set 1's descriptive FAR numbers.**
  - Set 1's route FAR walks fail the original test 2: median triangles aren't below both A.
  - The Studio Head **accepted 6c with disclosure** (`TestResults/9-studio-head-rulings.md`). Under 6c, the far-plane readback (600 m in every FAR walk, 1000 m in every A) proves the arm, the same rule the cab uses.
  - Route FAR is SAVING under 6c and NO EFFECT under the original test; `9-analysis.md` reports both rows.
- **The route sample-band plan error.** Plan section 5 test 1 voided any route walk more than 5% below the block's most frames.
  - FAR is faster, so it records about 20% more frames over the same fixed 208.4 s route. That voided every A and LAYER walk in set 1, so set 1's route is **INCONCLUSIVE**, with its numbers descriptive only.
  - technical-director names this as its own plan error. Route set 2 replaced the band with speed and duration checks (addenda 6 and 6b), set before set 2's data.
- **Machine load during measurement:**
  - platform-tools-engineer ran short local Python (a fake-data test of the analysis script) from about 00:04:30 to 00:06:05Z, inside launch `s01-r1-park`, overlapping its windows 0-3 (A, FAR, A, LAYER). That **breached decision 0026 condition 2**.
    - park FAR run 1's trial p95 was 5.85 ms against 5.16 and 5.18 in runs 2 and 3, so **run 1's d of 2.63 ms is probably understated**, and the worst-run test was passed conservatively.
    - Run 1 is kept under the pre-set rule (no void runner, no invalid window).
  - technical-director ran one `cat` of a 1 KB file at about the route-set-2 launch (`20261112-2248-route2/started-by.txt`).
  - During the section 5 rerun, qa-lead ran a read-only compare (at an unknown time after 19:40:48Z, before the Python run, so before any timed window) and a short Python run that ended at 19:42:33.1Z (`20261113-1342-rerun/started-by.txt`).
    - The Python, which ran to 19:42:33Z inside s01-r1-park's warm-up, was a **breach of decision 0026 condition 2** (no agent processes during a measurement).
    - No timed window was affected: window 0 was applied at 19:42:33.15Z, and timing began after its 5 s settle.
    - s01's load records: start 15.7% CPU, end 9.7%, no render or Editor processes.
- **The INCONCLUSIVE rerun.** The Studio Head chose plan section 5's rerun (recorded in `TestResults/9-studio-head-rulings.md` item 3), although technical-director had recommended skipping it (its #30 ruling message).
  - All five rerun rows (SHADOW at park, ridge_top, valley_floor and cab; FISHOFF at fish_water) are still INCONCLUSIVE.
  - Under section 5 they went to the Studio Head, who accepted them as INCONCLUSIVE and said "finish #9" (rulings item 4). The note reports them as INCONCLUSIVE and draws nothing from them.
- **The fish counter bug and the rerun.**
  - The first fish readings (pose checks, the 60 s sample `20261112-1232-fishsample`) all read 0 because the probe read `DrawModes` before the fish renderer refilled them each frame.
  - The counter was fixed to count after the renderer (`FishDrawCounter`, order 10000) and proven on the rerun sample `20261112-1243-fishsample`: fish_c1 drew fish on 18.9% of frames, fish_c2 on 33.9%.
  - The counter counts individual fish drawn (body or shadow) after the visible-body cap.
  - The fish_water pose comes from that corrected sample. Report: `TestResults/9-fish-counter-check.md`.
  - The same ordering flaw exists in `BellsBendFpsWalk.CountFish`. Every `fishDrawnMax` in the 11 `bells-bend-fps*.json` results under `TestResults/` is 0, including the F9 fish-on walk where `fishLiveMax` is 127 (`TestResults/fish-qa/f9/win-fishon/bells-bend-fps-shore.json`). It's routed outside #9.
- **The SMALLMESH edge-still aim limitation.** The probe aims an edge still at the target's bounds centre, with no line-of-sight check and without hiding the player body.
  - At ridge_top the target, MC39_Firefinder_Ring at 62.5 m, was behind a trunk with the survivor's boot in frame.
  - So SMALLMESH's "no pop-in" rests on its counters (NO EFFECT) and technical-artist's "no drop-out" read, not on a clean edge frame.
- **GRD counters:** not measured (Q1). The Q1 and Q5 counts are the CPU emulation.
- **Poses changed before any timed launch:** ridge_top and river_bank moved to pose-check winners (`TestResults/9-posecheck2-sheet.png`, `9-posecheck2-coverage.md`). The original ridge_top saw only forest, and the original river_bank stood about 1 m from a trunk.
- **The player build includes `Assets/World/Lookout/MC39_Glass.mat` as locally modified** before this pod. The Studio Head chose to keep it, with no rebuild (`TestResults/9-studio-head-rulings.md:11`; `.studio/handoff.md:16` lists it among the changes he kept out of commits).

## No live setting changed
- Every trial was a runtime flag in the player, refused in the Editor (`FrustumProbe.cs` Boot).
- `git status` shows no change from this pod to scenes, prefabs, materials, URP or Quality assets, terrain data, ProjectSettings or Packages. The only modified asset is `MC39_Glass.mat`, which was already modified before the pod.
- The pod's files are the harness (`Assets/Scripts/Perf/`, `Assets/Editor/Perf/`), the tools (`tools/perf/`), the evidence (`TestResults/9-*`) and this note.
