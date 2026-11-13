# #9 measurement plan and savings rule (pre-registered)

Status: FIXED 2026-11-12, revision 2, before any #9 run. Owner: technical-director (task #25).
- Revision 2 replaces blob 47f5ea9b. The reason: platform-tools-engineer's reworked probe runs the variants as windows inside one launch with a 360° sweep (its ACK on #25). That gives tighter brackets and covers every heading, and team-lead asked to keep small props and fish in Q3 where a method holds. The change was made before any data, so it is not a re-ruling.
- Inputs: the harness code (#23), the static census `TestResults/9-culling-census.json` (#24), and the package source cited below. No #9 run data existed.
- Applies to #9, and to #10 and #11 unless their own plan replaces section 4.
- Builds on decision 0016 (frame-time noise rule A1).
- Nothing in this file changes after the first launch. A gap found later goes in the note as a gap.

## 1. Set-up
- **Player:** one Windows release build (`BellsBendWindowsBuild.BuildBatch`), not a development build, with the reworked probe.
  - Precondition: the probe refuses to run in the Editor at all (review_technical-director on #23, B1). Then the URP-asset keys (`grdOcc`, `smallMesh`) and the vsync write exist only in player memory, and a player can't write assets or ProjectSettings.
  - Every launch uses the same exe SHA-256, read by the player at start.
- **Machine:** the runner guards from `tools/perf/farview_matrix.ps1:244-285` (console session active, no Editor, Blender or ffmpeg, the CLI helper bar), vsync off, 1920x1080 windowed, right-hand monitor. The Studio Head starts the session; agents never launch the player. The Unity lock is held throughout.
- **Cab height:** `-fcCabHeight` = cab-floor height from `tools/blender/lookout/mc39_params.json` + 1.65 m, as `farview_matrix.ps1:51-60,82` computes it (27.939 m in the census). Recorded per run.
- **Poses:** `FrustumSpots.Defaults` (`Assets/Scripts/Perf/FrustumSpots.cs:31-49`). ridge_top and valley_floor are confirmed: they are the long views, the worst case for far-clip work. Each window sweeps 360°, so the pose's yaw only sets where the sweep starts.
- **Frame time:** `Time.unscaledDeltaTime`; p50/p95/p99 nearest rank (`FrustumMath.Percentile`). No GPU split: `enableFrameTimingStats` 0 (`ProjectSettings/ProjectSettings.asset:161`, protected) stays 0. CPU "Main Thread" is an extra and doesn't decide.
- **Shadows** as applied: URP asset, 50 m, **4 cascades**, 2048 (`Assets/Settings/PC_RPAsset.asset:57-58`). The issue's "2 cascades" is QualitySettings, which URP ignores. Q5 is judged against 4.

## 2. Counter definitions (agreed with platform-tools-engineer, its ACK on #25 item 4)
Taken per window at compass 0/90/180/270, after the timed sweep. Distances are **nearest view depth**, because URP linear fog is depth-based.
- **enabled:** renderers on active objects, enabled.
- **submitted:** enabled, bounds meet the frustum, nearest view depth ≤ min(far clip, layer cull distance when set). A CPU emulation using the camera rule.
- **fog_visible:** submitted and nearest view depth < fog end (520 m). submitted − fog_visible = drawn but fully fogged (Q2).
- **Bands** of submitted: 0-120, 120-520, 520-far.
- **Split:** MeshRenderer (GRD-eligible type, approximate) against other.
- **Occlusion:** not in these fields. The LOD levels and the terrain-occlusion estimate are labelled extras.
- **Engine counters** (ProfilerRecorder Render, every timed frame; median, p95, p99, max): Batches, SetPass Calls, Draw Calls, Triangles, Vertices, Shadow Casters, Visible Skinned Meshes. Not exposed → null, reported as "not exposed".
- **GRD limit** (read in the package): the GPU Resident Drawer's culler tests only the camera layer mask (`com.unity.render-pipelines.core/Runtime/GPUDriven/Culling/InstanceCuller.cs:427-433`), not `layerCullDistances`. Its own screen-size cull is `minScreenRelativeHeight = smallMeshScreenPercentage * 0.01` (`:2218`). So the census's layer-culled figure is the camera rule. The engine check is Visible Skinned Meshes for LAYER, and Triangles/Batches for the GRD keys.

## 3. Rulings on method (team-lead's (a) and (b), platform-tools-engineer's items 3 and 6)
- **(a) `-fcRelayer` for small props: declined. The method doesn't hold.**
  - The only non-water, non-animal renderers are static MeshRenderers: NorthBarrier 456 and LookoutTower 23 (census `renderers.by_root`). Those are GRD-drawn.
  - GRD ignores `layerCullDistances` (above), so a layer-16 cull would cut nothing and the trial would read NO EFFECT by construction.
  - **Small props stay in Q3 through SMALLMESH instead**: GRD's own screen-size cull, the lever GRD honours.
- **Fish stay in Q3 through FISHOFF, as an upper bound.**
  - Fish aren't Renderers. They draw through `Graphics.RenderMeshInstanced` (`Assets/Scripts/Fish/Rendering/FishRenderer.cs`), so no camera layer cull reaches them.
  - They already cull bodies at `bodyDrawDistance` 25 m (`Assets/Scripts/Fish/FishTuning.cs:184`) with their own frustum test (`FishSimWorld.cs:446`).
  - FISHOFF deactivates the Fish root for the window. That is the most any fish cull could save. If FISHOFF is NO SAVING, no fish cull can reach S_min; that is the measured Q3 answer for fish. FISHOFF is never itself a recommendation.
- **(b) No GRD-off arm.** Turning GRD off swaps the whole draw path; it measures GRD, not culling, and is not a #9 change. Renderers drawn by GRD are reported as "can't be culled by `layerCullDistances`". SMALLMESH and GRDOCC are the levers measured for them.
- **GRDOCC included,** although the census suggests little for it to cull: no baked occlusion (`occlusion_culling.baked` false), and the GRD renderers are mostly NorthBarrier. Measured beats assumed, and a window costs about 35 s.
  - The window is valid only if `GPUResidentDrawer.IsInstanceOcclusionCullingEnabled()` reads back true.
  - If it never does, Q-occlusion is reported unmeasured.
- **Not run:**
  - DETAIL: the authored 70 m is inside fog start (120 m); a cut there is a visible quality cut, not culling waste.
  - Q4 timing: the only oversized renderers are 20 water quads of 4 vertices; culling can't remove their on-screen pixels, and the census answers Q4.
  - STACK: savings are measured one at a time, and the note says they aren't additive.

## 4. Run matrix
### Variants (window keys from the reworked probe)
| id | keys | answers | spots |
|---|---|---|---|
| A | none (live) | baseline, Q1, Q5 counts | all |
| FAR | `far=600,farCab=1150` | Q2 | all 5 (cab = a cost reading: 1150 is the accepted cab view) |
| LAYER | `cull.Wildlife=400,cull.Birds=400` (planar), edge stills | Q3 animals | all 5 |
| SMALLMESH | `smallMesh=1` (1% of screen height) | Q3 small props | all 5 |
| GRDOCC | `grdOcc=on` | the most culling can do | all 5 |
| SHADOW | `shadows=off` (camera renderShadows) | Q5 cost, never a candidate | all 5 |
| FISHOFF | Fish root inactive for the window | Q3 fish upper bound | river_bank, valley_floor |

- 400 m is 70% into the fog (120-520 m). 600 m is fog end + 80 m.
- If the probe lacks a `fishOff` key, platform-tools-engineer adds one: a runtime SetActive on the Fish root, restored after the window, read back in the JSON.

### Spot launches
- One launch per spot per run: 5 spots × 3 runs = 15 launches.
- Spot order: run 1 park, river_bank, valley_floor, ridge_top, cab; run 2 reversed; run 3 valley_floor, cab, park, ridge_top, river_bank.
- Inside a launch:
  - 15 s of A as warm-up, discarded.
  - Then windows A V1 A V2 ... Vk A: a fresh A between every variant.
- Each window: apply and read back, 5 s settle (discarded), then one 360° sweep at 15°/s = 24 s timed. Stills and the census come after the timed part, frozen, never inside it.
- Variant order (k = 5, or 6 with FISHOFF; FISHOFF goes last in each list where it applies):
  - run 1: FAR LAYER SMALLMESH GRDOCC SHADOW
  - run 2: SHADOW GRDOCC SMALLMESH LAYER FAR
  - run 3: SMALLMESH SHADOW FAR GRDOCC LAYER
- Q2 and Q3 stills: each trial window keeps its in-frame authored twin and change mask. LAYER and SMALLMESH also get cull-edge stills.

### Route
- **Route census:** one A launch, `-fcSpots` with `-fcPose outdoor_centre=-145.88,-852.69,48.03,0` and `-fcPose tower_base=544.02,-232.17,228.03,0` (census `places`). Q1 for the route waypoints (park is PlayerSpawn); its timings aren't used.
- **Route block:** `-bbFpsWalk` with global flags, one variant per launch, A FAR A LAYER A LAYER A FAR A FAR A LAYER A = 13 launches.
  - The route keeps launch brackets, because the walk can't change settings mid-route without breaking its fixed timing.
  - SMALLMESH, GRDOCC and SHADOW aren't run on the route: they are answered at five spots, and running them would add about 40 min.

### Optional, last
- A development-build counter pass: one launch per spot, k = A, FAR, LAYER, SMALLMESH. It reads the engine's GRD counters (Frustum Culled, Layer Culled, Visible Instances) and Shadow Casters.
- Counters only. Its frame times are never used or shown beside release figures. If it's skipped, Q1 and Q5 rest on the emulation and the note says so.

### Failed launch or window
- A launch the runner fails is rerun at once in the same slot.
- An invalid window (valid:false) voids only that comparison in that run. If the comparison then has fewer than 3 valid runs, that spot's launch is repeated once at the end of the block. Never more.

### Wall time (estimate, assumed until the first launch is timed)
- Spot launch: about 40 s load + 15 s warm-up + (2k+1) windows × about 35 s.
  - k = 5: about 7.3 min.
  - k = 6: about 8.5 min.
  - 15 launches ≈ 115 min.
- Route: 13 × about 3.3 min ≈ 43 min, plus about 3 min for the route census.
- Total ≈ 2 h 45 min unattended, plus about 15 min of checks. With the optional pass, add about 25 min.

## 5. The rule: is it a saving? (fixed before data; decision draft S1)
**Measure: p95 frame time, ms.** Each spot (and the route) is judged on its own.
- Spot, run r: `d_r = mean(p95 of the A window before, p95 of the A window after) − p95(V window)`, all inside launch r.
- Route, run r: the same, with the A launches either side.
- Positive means faster.

**Minimum saving that counts: S_min = max(0.20 ms, 2% of the mean A p95 at that spot).**
- 0.20 ms is 1.2% of the 16.67 ms 60 Hz budget, and about the launch-to-launch noise seen in M30 (margin 0.221 ms, spreads 0.03 and 0.14 ms; 0016).
- A culling change carries pop-in risk.
- The 2% term is for slower machines.

**SAVING** only if all four hold (0016 A1, adapted):
1. **Valid:**
   - progress.txt has the exact DONE line (not "with argument errors").
   - No argument errors or unknown layers.
   - The same exe hash; the runner isn't void.
   - Each window valid:true (readback present and equal to the request) with ≥ 600 timed samples.
   - Route launches inside the block's 5% sample band (`farview_matrix.ps1:147-154`).
2. **The arm is the arm** (replaces "no impossibly fast arm"):
   - Enabled renderer count within ±2% of the bracketing A windows, except FISHOFF (fish aren't renderers).
   - The target moved:
     - FAR at a ground spot: submitted lower than A at one heading or more, or median Triangles below both A windows.
     - LAYER: median Visible Skinned Meshes below both A windows.
     - SMALLMESH and GRDOCC: median Triangles or Batches below both A windows.
     - SHADOW: Shadow Casters median 0 when exposed, else the readback alone.
     - FISHOFF: the Fish root reads back inactive.
     - FAR in the cab: the readback alone.
   - A valid arm that moved nothing is **NO EFFECT**: a finding, not a saving.
3. **Bracket under the saving:** in every run, |A before − A after| < d_r.
4. **Worst run:** min over the 3 runs of d_r ≥ S_min.

Other outcomes (tests 1 and 2 must hold for each):
- **COST:** in every run, −d_r ≥ S_min and |A before − A after| < −d_r.
- **NO SAVING:** every bracket |A before − A after| < S_min, and max d_r < S_min.
- **INCONCLUSIVE:** anything else. That spot's 3 launches are rerun once, unchanged; if it's still INCONCLUSIVE, it goes to the Studio Head. Never re-ruled.
- **SHADOW and FISHOFF** are read as costs or upper bounds: mean d_r with its range, valid under tests 1-3. Otherwise INCONCLUSIVE. Neither is a recommendation.

**Reporting:**
- Median and p99 are reported beside p95 and don't decide.
- Counters alone never make a saving. A change with fewer draws but NO SAVING is listed as "counters only, no measured saving", below every SAVING.

**Q6 ranking:**
- Only SAVING rows rank, by mean d over the spots where they are SAVING, naming the spots where they aren't.
- A row is recommended only with technical-artist's "no visible difference" (FAR) or "no pop-in" (LAYER, SMALLMESH) on the stills.
- Risk and effort are named per row. A URP-asset change (SMALLMESH, GRDOCC) applies project-wide.

**Applied unchanged:** the analysis reads S_min and these tests from this file. The note quotes this file's `git hash-object` at run time and states any departure as a departure.
