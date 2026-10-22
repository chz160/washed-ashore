Replace the placeholder body pose in water with real wade and swim animation, without the camera ever going underwater.

## What we know (checked 2026-10-18)

- **Water movement exists** (water pod): wade from 0.05 m depth; swim float depth 1.30 with eye 1.65 and the rule floatDepth + 0.2 ≤ eye (`SwimTuning.cs:75`).
- **The player-character pod logged a placeholder pose in water** (spec H8 allows it). All lowered stances are blocked in water and stand up at the first wade (`_bmad-output/research/technical-player-character-2026-10-18/research.md` A.4).
- **The camera never goes underwater** in any stance or swim pose (greenlight C4, fish N1). The camera stays on `CameraPivot`, never a bone (gate item 1), so animation can't drag the eye under.
- **Clips:** the Quaternius packs have no swim or wade clips. Clips are authored by Blender script per gender on `CharacterArmature` (Generic), with licences in `_bmad-output/poc/character-clip-manifest.md`.
- **Duck-under** is parked and will use the crouch action (Left Ctrl / East) in water (`water-swim-spec.md:73-76`; td confirmed in `_bmad-output/research/technical-player-character-2026-10-18/research.md` A.4).

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| W1 | Wade (shin, knee and waist depth), swim idle (tread) and swim forward clips per gender, authored by script | Clip manifest; licence per clip |
| W2 | Looking down while wading or swimming shows the body in the water, with no camera inside the body at −85..+85 | Pitch sweep in water |
| W3 | The eye stays above the surface every frame in every water clip, including on waves | PlayMode: eye Y minus water height ≥ 0.2 m |
| W4 | Hand and foot slide is not graded in water; the swim stroke matches swim speed within the designer's range | Designer's table; Noah's check |
| W5 | Wade, swim and existing water tests pass unchanged | Test XML |

## Automatic FAIL

- The camera going underwater.
- Hand-made Blender steps.
- Changing the swim or wade tuning without the water spec's owner.

## Pod

**technical-artist** (lead), **gameplay-engineer**, **qa-lead**: 3 agents.

Related: the water spec; future-character-04 (wetness).
