Design and build a character-creator screen for Washed Ashore, on top of the placeholder body from the player-character pod. **This is the first UI beyond the single interaction prompt line, so it starts with a director ruling, not code.**

## What we know (checked 2026-10-18)

- **Appearance is already data.** `CharacterAppearance` (rig/body type, Head, Body, Legs and Feet slot meshes, tint slots) is assembled at runtime by `CharacterAssembler` (`Assets/Scripts/Character/Appearance/`, player-character H2). The default look is men's Worker (director gate, `gate/player-character-phase0`).
- **Parts on hand:** Quaternius CC0 packs, 11 men's and 10 women's outfits, each as Head/Body/Legs/Feet parts. Excluded for the fiction: Suit, Swat, Soldier, Punk, King, SpaceSuit, SciFi, Medieval, Witch (greenlight C6, research.md "Default look").
- **The UI rule:** no HUD; the existing prompt line is the only UI (tower N11, greenlight C1). A front-end screen is outside the HUD, but it needs a ruling.
- **How others do it** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` B.9): camera framing presets (MetaHuman Creator Face/Body/Far/Auto; Hogwarts Legacy cycles Face → Full Body → Upper → Lower → Shoe). The usual order is base and sex → body → face → hair/colour → name. SCUM's body triangle changes looks and stats. Rust has no creator at all (look from SteamID).
- **Permadeath:** the README's permadeath and persistent world raise a question: a new look each life, the same look, or a random survivor?

## Gate (before any code)

- [ ] game-director rules: is a creator screen allowed, which flow (full creator, preset picker, or a random survivor like Rust), and the permadeath rule.
- [ ] Noah approves the ruling.
- [ ] Depends on: body types (future-character-02), face/hair/skin (03), appearance save (06).

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| U1 | The screen edits only a `CharacterAppearance` descriptor; the game reads the same descriptor (no second path) | Code review; EditMode round-trip test |
| U2 | Every choice reachable by keyboard/mouse and gamepad | PlayMode navigation test |
| U3 | Camera framing presets (at least face and full body) with no clipping into the model | Screenshots per preset |
| U4 | Randomise produces only fiction-approved combinations (the exclusion list) | EditMode test over 1,000 seeds |
| U5 | No in-game HUD is added: the screen exists only before play (or at respawn, per the ruling) | Scene audit |
| U6 | Noah's check: "it feels like choosing a survivor" | Noah's verdict |

## Automatic FAIL

- Any in-game HUD element.
- Code before the director's ruling.
- A look stored as mesh or material references instead of IDs.

## Pod

**systems-designer** (flow, lead), **gameplay-engineer** (UI Toolkit), **art-director** (approves options), **qa-lead**: 4 agents.

Related: future-character-02, -03, -06; spec `poc-spec-washed-ashore-player-character.md` (B).
