# DRAFT, not posted. For the Studio Head to post.
Title: F9 fishDrawnMax always reads 0: the route harness samples fish before they are drawn
Labels: tech:perf, area:wildlife

## Problem
`fishDrawnMax` in the route FPS report is 0 by construction, so earlier F9 results that cite it say nothing about how many fish were drawn.

## What the code does (read 2026-11-12 by the technical-writer; the cause was ruled by technical-director in #9 task #26)
- `FishPopulation.Update` clears `DrawModes` and `Visibility` every frame (`Assets/Scripts/Fish/FishPopulation.cs:184-187`).
- `FishRenderer.LateUpdate` (execution order 1000, `Assets/Scripts/Fish/Rendering/FishRenderer.cs:17,83`) refills them later in the same frame.
- `BellsBendFpsWalk.CountFish()` (`Assets/Scripts/Perf/BellsBendFpsWalk.cs:168-175`, called at line 113 from the walk coroutine) reads `DrawModes` between those two points, so it always sees `None`.

## Evidence it reads 0
- `TestResults/fish-qa/f9/win-fishon/bells-bend-fps-shore.json`: `fishLiveMax` 127, `fishDrawnMax` 0 (fish on, shore route).
- 37 JSON files under `TestResults/` carry `fishDrawnMax` (grep). Not all were opened; the walk code path is the same for all.
- `_bmad-output/poc/fish-qa-report.md:94` (item 9) already notes this: "fishDrawnMax reads DrawModes before the renderer's LateUpdate (reads 0 by construction); fix the counter timing."

## Ask
1. Move the F9 reader in `BellsBendFpsWalk` to `FishRenderer.DrawnLastFrame` (`FishRenderer.cs:37`), the source #9's probe uses.
2. List the past reports and decisions that cite `fishDrawnMax`, and mark them. Known so far: `_bmad-output/poc/fish-qa-report.md` (line 70 area and 94) and `_bmad-output/poc/fish-td-note.md:94` (the F9 Windows FPS entry; whether it quotes the drawn count was not checked). The vault has no hit (grep of `docs/`).

## Not checked
- Whether any past decision relied on a non-zero drawn count. Only the code path and one JSON were read, not re-run.
- The #9 probe's use of `DrawnLastFrame` was not confirmed in `FrustumProbe.cs` by the writer (grep found no match at the time of writing).
