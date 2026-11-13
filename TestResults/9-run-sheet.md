# #9 frustum culling: run sheet for the Studio Head

Three steps: a 3-minute pose check, the unattended matrix of about **2 h 45 min**, and right after it the development-build counter pass of about **25 min**. Together they run the pre-registered matrix (`TestResults/9-measurement-plan.md`, revision 2, git hash-object `710f0d11`). Agents never launch the player (decision 0016); this sheet is for you.

## Before you start
1. **Sit at the machine.** The console session must be active: no RDP, and don't lock the screen or let it sleep. The script refuses a disconnected session, because the numbers would be meaningless.
2. **Close the Unity Editor**, Blender and ffmpeg. The script refuses if an Editor or a batchmode Unity is running. The Unity CLI's login helper may stay; it's watched.
3. **Quiet machine:** no builds, downloads, video, games or indexing. Turn off the screen saver and sleep for about 3.5 hours.
4. **Two monitors:** the game opens windowed at 1920×1080 on the right-hand monitor (`-monitor 1`). Leave it alone; mouse or keyboard input in that window spoils a run.
5. Tell team-lead you're starting. No agent runs Unity while the session holds `.studio/unity.lock`; the script takes the lock at the start and releases it at the end.

## Check (launches nothing, about 5 s)
```
pwsh tools/perf/frustum_matrix.ps1 -DryRun
```
This lists all 29 launches and ends with `Would refuse now: no`. If it names anything after "refuse", fix that first.

## Step 1: pose check (about 3 min), then wait for the go
```
pwsh tools/perf/frustum_matrix.ps1 -PoseCheck
```
- One short launch: the camera visits the five places and takes four stills at each. Nothing in it is timed.
- When it prints `FRUSTUM MATRIX DONE run=<id>-posecheck`, send team-lead the run id and **stop**.
- platform-tools-engineer and technical-artist check the stills: no camera under water or inside terrain, and each still 1920x1080. team-lead then gives you the go for step 2.
- If a place is wrong, technical-director fixes its pose before any timed launch, so no measurement is lost (rule N3).
- Already checked from scene data (`TestResults/9-pose-check.json`): river_bank and valley_floor stand on ground 1.5 m above the water surface, with the eye 3.2 m above it.

## Step 1b: fish sample (about 3 min)
Done before the matrix: the pose checks (runs `20261112-1018-posecheck` and `20261112-1029-posecheck`) moved ridge_top and river_bank to their winning candidates.
```
pwsh tools/perf/frustum_matrix.ps1 -FishSample
```
- One untimed launch, no stills: a 60 s slow turn at each of two shore places, counting fish on every frame.
- It ends with `FRUSTUM MATRIX DONE run=<id>-fishsample`.
- The matrix reads the newest finished fish sample by itself. It refuses to start if there's none.
- If one of the two places shows a fish on at least 10% of frames, the matrix adds 3 fish_water launches (about 8 min each). If neither does, it adds none.

## Step 2: run the matrix (after the go)
```
pwsh tools/perf/frustum_matrix.ps1
```
Run it from the repo root (`E:\GitHub\washed-ashore`).
- The player: `Builds/Windows-BellsBend-frustum9/WashedAshorePOC.exe`, exe sha256 `91a36fa14dd3ccf390b99720805eda0553da1ca1108381b49c98c938f69dc256`, built from commit `eaf238d` plus the uncommitted harness. It also includes the locally modified `Assets/World/Lookout/MC39_Glass.mat`, which you chose to keep. Provenance: `Builds/Windows-BellsBend-frustum9.provenance.json`.
- What runs, in order:
  - 15 spot launches: 5 places × 3 runs, about 7-9 min each, with the camera turning slowly.
  - 1 route-census launch, about 3 min.
  - 13 route walks, about 3.3 min each.
- If a launch fails, the script reruns it once straight away.
- If a spot launch finishes but any of its windows is invalid, the script queues one repeat of that launch at the end of the spot block. Each repeat adds about 8 min, and the slot count rises above 29. That's expected.
- **If the session is interrupted** (power, a crash, or you need the machine):
  - Run it again with the same run id, which is printed at the start and is the folder name under `TestResults/9-runs/`: `pwsh tools/perf/frustum_matrix.ps1 -RunId <id>`
  - Finished launches are skipped, and a half-finished one is moved aside, never overwritten.

## How to tell it finished
- The console prints `FRUSTUM MATRIX DONE run=<id> slots=29 ...` (more if repeats were queued) and exits with code 0.
  - `FRUSTUM FINISHED WITH FAILURES: <slots>` (exit 1) means a launch failed twice. Send team-lead the run id.
- The last line of `TestResults/9-runs/<id>/matrix-progress.txt` is `MATRIX DONE all <n> slots`.
- `TestResults/9-runs/<id>/summary.json` exists and shows `"same_exe": true`.
- `.studio/unity.lock` is gone.

Don't open files under `TestResults/9-runs/<id>/` while it runs: a file lock lost an earlier run (0016).

## Step 3: the development-build counter pass (about 25 min), right after step 2
Start it as soon as step 2 has printed `FRUSTUM MATRIX DONE`, with the same preconditions.
```
pwsh tools/perf/frustum_matrix.ps1 -DevCounters
```
- It uses `Builds/Windows-BellsBend-frustum9-dev/` (exe sha256 `437691663c5fb21231524f4886e60a10b1841f13e42fbbc93ee39fbd827930cd`): 5 launches, counters only, no stills.
- It reads the GPU Resident Drawer's own culling counters, which a release player doesn't expose. Its frame times are never used.
- It has its own run id (ending `-dev`) and ends with `FRUSTUM MATRIX DONE run=<id>-dev`.

## Afterwards
Tell team-lead both run ids (the matrix and the `-dev` pass). platform-tools-engineer then analyses it (#27); nothing in it needs committing by hand. The files to keep are listed in `TestResults/9-evidence-manifest.txt`, and team-lead force-adds them once you agree.
