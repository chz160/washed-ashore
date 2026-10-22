Let the survivor's face, hair and skin vary: preset heads, hair and beard options, and skin tone, in the flat-colour Quaternius style.

## What we know (checked 2026-10-18)

- **In first person the head draws only as a shadow** (Head slot `ShadowsOnly`, director gate item 1). Faces matter for other players, the debug camera and a creator screen, not for the local view.
- **Head parts:** each pack outfit has a `*_Head` part on the shared skeleton (artist section, `_bmad-output/research/technical-player-character-2026-10-18/research.md` A.2).
- **Research** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` B.3, B.4): survival games mostly use preset heads. Rust derives skin, head mesh and material from the SteamID (2×2 faces at launch, 2016). 7 Days to Die 1.0 has 4 faces per race and sex, 20 hairstyles, 10 hair colours and beards, and players criticised the lack of body types. Hair is layered alpha cards. Flat-colour art suits per-material tint or a palette over channel masks.
- **The tint route:** per-material `_BaseColor` through a `MaterialPropertyBlock` now; a palette texture once merging (future-character-07) lands.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| F1 | At least 6 head presets per rig, all from CC0 or project-authored sources with licences recorded | Asset manifest |
| F2 | Hair and beard are optional sub-slots of Head, swappable at runtime with no seams or T-pose | PlayMode swap test; screenshots |
| F3 | Skin tone is a palette index (at least 8 tones) applied without material copies | EditMode: renderer shares one material |
| F4 | The first-person view is unchanged: the head and hair never draw to the local camera | Pitch-sweep test from player-character H5 |
| F5 | All choices are IDs in the appearance descriptor | future-character-06 test |

## Automatic FAIL

- Hair or head geometry visible in the local first-person camera.
- Any asset without a recorded game-compatible licence.
- A download or install without Noah's approval.

## Pod

**technical-artist** (lead), **art-director** (approves presets), **qa-lead**: 3 agents.

Related: future-character-01, -06, -07.
