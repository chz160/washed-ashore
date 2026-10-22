Find out whether characters need merged meshes and texture atlases, and build it if a measured budget says so. **Measure first:** with one local player this is not needed.

## What we know (checked 2026-10-18)

- **Draw cost today:** 4 parts × about 4 materials ≈ 16 draws per character [derived from Worker_Body's 4 materials, artist section, `_bmad-output/research/technical-player-character-2026-10-18/research.md` A.2]. Fine for one local player; it matters with many remote players (#13).
- **Research** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` B.6, B.7, B.12):
  - Unity's `CombineMeshes` does not handle skinning. Community combiners concatenate geometry, offset bone indices and rebuild bones and bind poses, and mistakes duplicate bones or break weights. There is no official guide.
  - UMA atlases one texture per avatar (one material, one draw; up to 4096², 2048² suggested). Mutable's runtime repacking is costly, worth it only to cut draws or memory. Merging drops morph targets in Unreal, which conflicts with body sliders (future-character-02).
  - Unity 6.2+ Mesh LOD on skinned meshes: skin weights and blend shapes are ignored, there is no skinning saving, and pairing with LOD Group is "not recommended". The working route is a LOD Group with one SkinnedMeshRenderer per level on the shared bones.
- **UMA** is MIT-licensed (repo checked). Using it is a package install, so Noah decides.

## Phase 0 (measurement)

- [ ] A test scene with N = 1, 8, 16, 32 assembled characters (the #13 scale of 16 per world): draws, SetPass calls, skinning ms and memory, per the perf protocol (`_perf-protocol.md`).
- [ ] technical-director sets the character budget from that run.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| M1 | Measured cost table at N = 1/8/16/32, unmerged | Perf JSON |
| M2 | If the budget is exceeded: a merge plus palette-atlas path built by script, with ≤ 2 draws per character and identical silhouettes | Twin-diff screenshots; Profiler |
| M3 | A runtime slot swap on a merged character re-merges in ≤ 1 frame budget set by td, or swaps on the unmerged path and merges later | PlayMode timing |
| M4 | Character LOD (at least 3 levels on shared bones) for remote players | Screenshots; triangle counts |
| M5 | Blend-shape sliders survive the merge, or the merge is ruled out while sliders exist | EditMode test |

## Automatic FAIL

- Unmeasured recommendations.
- Installing UMA or any package without Noah's approval.

## Pod

**technical-artist** (lead), **platform-tools-engineer** (profiling), **technical-director**: 3 agents.

Related: #11 (LOD), #13; future-character-02, -05.
