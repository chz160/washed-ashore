Save the survivor's appearance and send it to other players, as a small versioned descriptor of IDs, never as meshes.

## What we know (checked 2026-10-18)

- **`CharacterAppearance`** (player-character H2) holds rig/body type, slot meshes and tint slots as an asset. There is **no save system for it** and **no networking package** in the project (#13).
- **Multiplayer direction** (#13): a persistent world with permadeath; Netcode for GameObjects 2.13.x on Unity 6000.6, or 3.1 after a 6000.7 upgrade.
- **Research** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` B.8): Mutable says "the simplest way to replicate a customization is to send the set of parameter values". UMA ships packing code and warns it may need shrinking, and users report long JSON recipes. Rust stores nothing and derives the look from the SteamID.
- **td design:** a descriptor of schema version, rig, head and hair preset IDs, quantised slider bytes and palette indices: tens of bytes [inference]. **Equipped clothing is not in it**; it comes from inventory item IDs mapped to meshes by content tables. Unknown IDs fall back to the default look (men's Worker).

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| S1 | Descriptor round-trip: encode → decode gives the same assembled character, for every approved preset | EditMode test |
| S2 | Size ≤ 64 bytes without clothing | EditMode size assert |
| S3 | Version bump: a v1 descriptor loads in v2 through a migration with no exceptions | EditMode migration test |
| S4 | Unknown or removed IDs fall back to the default look with one warning, never an exception | EditMode test |
| S5 | (After #13) a remote player sees the same look within one second of joining | Two-client PlayMode or manual run |

## Automatic FAIL

- Mesh, material or asset paths in the descriptor.
- Networking code on main before #13 rules.

## Pod

**gameplay-engineer** (lead), **platform-tools-engineer** (save format), **qa-lead**: 3 agents.

Related: #13; future-character-01, -04, -13.
