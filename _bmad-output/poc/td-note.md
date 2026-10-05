---
title: 'TD note: does the Ruflo + unity CLI pipeline scale to Washed Ashore?'
owner: technical-director
inputs: [qa-report.md, friction-log.md, Assets/Scripts, Assets/Tests, Assets/Editor, session scratchpad eval scripts]
date: '2026-10-04'
---

# Recommendation: **GO WITH CHANGES**

The spike answered its question. Agents took an empty folder to a tested, built, walkable world in about 27 minutes with 0 assists and 0 blockers. About 30 minutes were lost to friction, and none of it was structural. But the run depended on things that will not hold at production scale: one editor, one baton, and pipeline logic kept in throwaway files.

## What worked
- **The eval loop is the real asset.** `eval`/`eval_file` takes 0.2-5 s, a full world rebuild takes 2 s, and recompile takes about 4 s. There was no Safe Mode and no reload loop. Authoring content through scripts is viable.
- **Tests and builds through the CLI are trustworthy.** The PlayMode suite uses real `InputTestFixture` input with no teleports and no ignored logs. The build ships a provenance manifest. qa re-ran everything independently.
- **The serialized baton prevented write conflicts.** No corruption and no two agents writing to the same scene.

## Real bottlenecks (friction log)
1. **Live editor vs. batch editor (#21, #22).** `unity test` and `unity build` need the project lock. Every verification means close, run, reopen, then reopen the scene. That cost about 1 min per cycle here. At game scale, with a large Library and minutes-long imports, it becomes the dominant cost.
2. **Shell environment (#1, #2, #6, #8, #19).** Four of six agents lost time to PATH and quoting. It was the most repeated cost, and it's entirely ours to fix.
3. **CLI semantics (#16, #17, #18, #26, #29).** Agents mistook tags for commands, positional arguments bound to the wrong parameter, `--project-path` behaves inconsistently, `unity logs` reads the Hub log, and `eval` fails during a domain reload. A bad call also writes errors to the console, which pollutes P9-style gates.
4. **Bootstrap (#4, #30).** `projects create` refuses a non-empty folder. The create-then-move workaround left `productName=washed-ashore`.

## Code and architecture quality
- **`Assets/Scripts/Gameplay/PlayerController.cs`: good.** It's a clean CharacterController. Every tunable is serialized, the InputActions are disposed, and there is a fall-through guard. Bindings are built in code (lines 49-58), which is fine for a POC. The real game needs an `.inputactions` asset so rebinding and gamepad UX can be designed.
- **`Assets/Tests/PlayMode/WorldWalkTests.cs`: good.** These are real behavioural tests in their own asmdef. B and C wait on wall-clock time and will be flaky in loaded CI. Gate on state with a timeout instead.
- **`Assets/Editor/Level/WorldBuilder.cs`: workable, not production-grade.** It's seeded and idempotent, and it records the TerrainData persistence gotchas (#14, #15). Problems:
  - `StagingDir` is a hard-coded absolute path (line 15) into `_staging/`, which is outside `Assets/`.
  - It has no asmdef, so it compiles into Assembly-CSharp-Editor.
  - Tuning lives in `public static` fields instead of a ScriptableObject.
  - It supports a single terrain only, with no tiling or streaming.
- **Blocking gap: the vendor import pipeline is not in the project.** The URP material remap, the FBX `globalScale` fixes, and the prefab builds (`p5_materials.cs`, `build_prefabs.cs`, `build_animals.cs`) exist only in the agents' temporary session scratchpad. The state of `Assets/ThirdParty/` can't be reproduced from source. The project also has **no git repo** and no `.gitignore`.

## Risks at Washed Ashore scale
| Risk | Sev | Why |
|---|---|---|
| Single-editor contention | High | One baton means agents run serially. Adding agents adds queueing, not throughput, and the batch-lock cycle multiplies it. |
| Domain reloads | Med | Fine at 2 asmdefs plus the default assemblies. Grows with code size. `eval` fails mid-reload (#18), so every call needs wait-until-ready plus retry. |
| Asset import at scale | High | This run was 89 materials and about 70 FBX files. The real game will be 10-100 times that. Without `AssetPostprocessor` rules, each import depends on an agent remembering to run a script. Cold import and shader compile (the first build took 437 s) sit on the critical path. |
| CC0 vendor pipeline | Med | The manual itch.io drop is acceptable. The risk is unversioned remap logic and per-vendor quirks (scale #10, multi-material details #12, bad meshes #20) with no validators. |
| Security | Med | The Hub-launched Editor carries `-accessToken` in clear text on its command line (#32), and any agent that lists processes can read it. Keep it out of logs and artifacts. |

## Top 3 changes before production
1. **Make the import pipeline versioned code.** Put all vendor import, material remap, and prefab-build logic in `Assets/Editor/Pipeline/` with its own asmdef: `AssetPostprocessor` rules per vendor folder, plus a validator entry point (no non-URP shaders, no null slots, scale within bounds). Remove absolute paths. Run `git init` with a Unity `.gitignore` and LFS. *Owner: platform-tools-engineer with technical-artist.*
2. **Break the single-editor bottleneck.** Run tests inside the live Editor (`run_tests` with a job wait) instead of `unity test`. Run builds and the authoritative test gate in batchmode in a **separate CI clone** owned by build-engineer, never against the live project. The live editor's baton becomes author-and-quick-verify only. For parallel content work, evaluate one cloned project per agent, each with its own editor.
3. **Standardize the agent harness.** Every pod prompt gets one shared preamble or wrapper script covering:
   - the PATH fix and the pwsh invocation;
   - `unity` calls that wait until ready and retry during a reload;
   - a cheat-sheet of correct command names and flags.

   Add a bootstrap script that fixes product and company names and reopens the last scene. File the CLI defects upstream (#4, #17, #26, #29, console pollution, reopening on an Untitled scene).

S1 and S2 were not met. That is a time-budget result, not a feasibility result. But wind sway (S2) needs a Shader Graph authoring path that the CLI doesn't give us yet, so treat it as an open tech risk for foliage.
