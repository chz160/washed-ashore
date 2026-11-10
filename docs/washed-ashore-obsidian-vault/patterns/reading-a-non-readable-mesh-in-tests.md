---
type: pattern
status: active
tech: [probe-tests, character-art]
area: [player-body]
issues: [49]
prs: []
source: "Assets/Tests/PlayMode/Character/HairlineGapSizing.Hull.cs 84ac1720 (class summary 'REST READ'); td-49 B37, td file 'STILLS FAULT (baton 10)', ONE SET B1; qa-49 12jr, 12m sign-off (b21 VOID)"
created: 2026-11-10
updated: 2026-11-10
aliases: [isReadable, Read/Write off, BakeMesh rest read]
---
# Reading a non-readable mesh in tests

_The character FBX meshes are imported with Read/Write off. In PlayMode their vertices and triangles come back empty, and some reads throw. Read the body through `SkinnedMeshRenderer.BakeMesh`, which writes a new, readable mesh, and never change the import settings for a test._

## Problem
A PlayMode test that reads `sharedMesh.vertices` or `.triangles` gets an error and an empty array. Unless it fails closed, it then reports a wrong result.

## When it applies
Any PlayMode test or probe that reads the Survivor's geometry (`Survivor_Body`, `Survivor_Head`: `isReadable False`, qa 12m). Runtime code needs no readable mesh (td's grep of `Assets` for `BakeMesh`, `vertices` and `boneWeights`: only tests and editor tools).

## Why it happens
With Read/Write off, `Mesh.canAccess` is false in PlayMode:
- `Mesh.vertices` and `Mesh.triangles` log an error and return empty arrays.
- `Mesh.AcquireReadOnlyMeshData` throws.
- Not gated: `boneWeights`, `bindposes` and `GetBonesPerVertex`.
- The Editor (EditMode) can read the mesh, so EditMode tests pass and hide the problem.

## Evidence
- **Baton 21 (X1 P0) was VOID:** the body wasn't readable, the hull built with 0 triangles, and the verdict code still printed "X1 FAILS". Nothing could be concluded (B37).
- Baton 10 (stills fault): amendment G read `sharedMesh.vertices` in PlayMode and threw. td moved the read to a second EditMode step (ruling A).
- Baton 11a: a classifier read the shared mesh's triangles in PlayMode; the fix took them from the baked mesh (ONE SET B1).

## What to do
- **Posed geometry:** `smr.BakeMesh(mesh, false)` into a new `Mesh`. Its triangles keep the shared mesh's order (FNV-1a `7e3eaf8a`), and its positions equal the GPU skinning (D3, 0.000 mm).
- **Rest geometry** (eng_51): a temporary `SkinnedMeshRenderer` on the same shared mesh at the origin, with its bones placed at the inverse bind poses, so every skin matrix is the identity. The bake is then the rest mesh. Remove the small residual per vertex in double. A new renderer has blend-shape weights 0, so no knee shape is in it.
- **Check it before using it:** vertex count, index-order hash, the same triangles as the live bake, and the rest re-skinned with live bones reproducing the live bake within 1 mm.
- **Fail closed:** a failed read gives NO VERDICT (VOID), never a pass or a fail. Stop before the first analysed frame.
- Destroy the temporary objects in the same call.
- **Don't:** turn on Read/Write, or import a scratch copy of the FBX inside `Assets` during a run (that creates a new asset, guid and `.meta`; rejected in the baton 10 ruling).

## Does not apply when
EditMode tests, where the Editor reads the mesh directly (`SurvivorRestPrint`, `SurvivorImportTests`).

## Related
[[see-through-gap-checks-on-a-skinned-body]], [[probe-and-playmode-test-gotchas]]
