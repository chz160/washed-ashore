Give the player a choice of body type and, later, body shape sliders, without breaking outfit fit or the shared animation set.

## What we know (checked 2026-10-18)

- **Two rigs today:** men's `CharacterArmature` has 79 bones, women's 62, with different proportions, so clips are authored per gender (artist section, `_bmad-output/research/technical-player-character-2026-10-18/research.md` A.2). Rig: Generic, not Humanoid.
- **No blend shapes confirmed** in the Quaternius parts [unverified; artist checks on import].
- **Research** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` B.2): Unity blend shapes run 0-100 through `SetBlendShapeWeight`. Every outfit part must carry the same body blend shapes as the body, so one slider set drives both. Height and limb length need bone scaling (UMA "DNA"), not blend shapes. Merging meshes in Unreal drops morph targets, which conflicts with sliders (B.6).
- **First person:** the camera sits on `CameraPivot` at a fixed stance eye height (1.65 standing), not a bone. A taller body must not move the eye unless the stance numbers are retuned.

## Phase 0 (research, short)

- [ ] Pick the model: rig choice only; rig plus 2-3 blend shapes (build, weight); or rig plus bone-scale proportions.
- [ ] Measure: authoring cost per outfit part (blend shapes per part, by script in `tools/blender/character/`).

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| T1 | At least 3 body types per rig assemble with every approved outfit, 0 errors and no visible gaps | EditMode test; screenshots |
| T2 | One slider value drives body and all equipped parts identically | EditMode weight check per renderer |
| T3 | The first-person eye height and the stance capsule numbers are unchanged, or a retune is ruled by the designer and the tower eye minimums are re-checked | PlayMode stance test; tower eye report |
| T4 | Foot slide stays ≤ 10 cm per stride on every body type | The player-character H4 measurement |
| T5 | Body type is stored as an ID plus quantised bytes in the appearance descriptor | future-character-06 test |

## Automatic FAIL

- Hand-made Blender steps.
- Changing walk 5, sprint 8, jump 1.2 or tower 1.15/1.5.
- A body type that changes the first-person eye without a designer ruling.

## Pod

**technical-artist** (lead), **systems-designer**, **qa-lead**: 3 agents.

Related: future-character-01, -05, -07.
