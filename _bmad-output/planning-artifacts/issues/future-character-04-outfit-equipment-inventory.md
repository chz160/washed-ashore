Make clothing real equipment: worn items are inventory items, they change what the body looks like, and they carry condition, wetness and dirt that matter for survival. **Depends on an inventory system existing.**

## What we know (checked 2026-10-18)

- **Slots today:** Head, Body, Legs, Feet, filled from `CharacterAppearance` by `CharacterAssembler` (player-character H2). A slot can be swapped at runtime with no T-pose (H2).
- **There is no inventory system yet** in the project (no file in `Assets/Scripts` mentions "inventory", checked 2026-10-18).
- **Water exists:** wading and swimming (water pod), so wetness has a real source.
- **Research** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` B.10, B.11):
  - **DayZ** (fan wiki): 11 slots; condition Pristine → Worn → Damaged → Badly Damaged → Ruined scales insulation 100 → 90 → 80 → 50 → 0%; visual damage from Damaged on; wetness Damp → Drenched raises weight ×1.0-2.0 and cuts insulation to −15%.
  - **The Long Dark** (official, 2016): several torso layers; condition % scales warmth and waterproofing; only the outer layer gets wet; a fully wet item freezes.
  - **Project Zomboid:** holes, dirt, patching; visibly ripped clothing from Build 41.
- **td design:** an item instance holds condition, wetness and dirt as 0-1 values. They drive a shader continuously (darken, roughness, dirt mask), with mesh or material swaps only at thresholds. The look follows inventory state and is never saved separately.

## Gate

- [ ] An inventory spec exists and is greenlit.
- [ ] systems-designer writes the slot list and the condition, wetness and dirt rules (insulation, weight), every value tagged.
- [ ] game-director confirms that no HUD is needed to read condition (C1): state is read from the body and the world.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| E1 | Equipping or removing an item changes the body within one frame, with no T-pose or seams | PlayMode test |
| E2 | Wading to knee depth raises the wetness of the leg and feet items only; swimming wets all; drying follows the designer's curve | PlayMode water test |
| E3 | Condition, wetness and dirt show visibly on the body (screenshots at 0, 0.5, 1) | Screenshots |
| E4 | Stats follow the designer's table (insulation, weight) | EditMode table test |
| E5 | Save and load restore item state and the look from inventory alone | future-character-06 test |

## Automatic FAIL

- HUD elements for condition or wetness.
- Storing the look separately from the inventory.
- Clothing that changes the first-person eye or the tower capsule.

## Pod

**systems-designer** (lead), **gameplay-engineer**, **technical-artist** (wet/dirt shader), **qa-lead**: 4 agents.

Related: future-character-05, -06, -08.
