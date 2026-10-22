Let clothing layer (shirt under coat, socks under boots) without the body or inner layers poking through.

## What we know (checked 2026-10-18)

- **Four slots today** (Head, Body, Legs, Feet), one part each, on the shared `CharacterArmature` (player-character H2). The Quaternius parts are cut per slot, so whole-slot replacement has no poke-through today.
- **Research** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` B.5):
  - **Bethesda:** armour declares biped slots, the body is split into matching partitions, and covered partitions hide. A mismatch makes the item invisible.
  - **Mutable:** Remove Mesh Blocks deletes geometry in chosen UV blocks "to avoid clipping problems with hidden geometry". Clip-with-mesh and UV-mask clipping are the alternatives.
  - **Cyberpunk 2077:** garment support shrinks inner layers under outer ones with painted weights (modding wiki).
- **td design:** each item declares `hidesSlots` (a coat hides Body, boots hide Feet) now. Body **regions** (submeshes or UV blocks: forearms, shins, neck) come only when partial cover arrives. One under-layer rule per item, checked by test.
- **First person:** looking down shows torso, hands, legs and feet (H5), so poke-through is visible to the player, not just to others.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| L1 | Every approved item combination (at least 2 layers on Body and Feet) renders with **0 poke-through** in the 5 stances and the clip set | Automated render check: twin-diff against a body-hidden twin, per stance |
| L2 | Hidden body parts cost nothing: hidden renderers or submeshes are disabled, not just covered | Profiler draw count |
| L3 | The rules are data (`hidesSlots`, regions) with an EditMode test that every item's rule names real slots | EditMode test |
| L4 | First-person pitch sweep at all 5 stances passes with layered outfits | player-character H5 test |

## Automatic FAIL

- Hand-sculpted fixes per combination with no rule.
- Hand-made Blender steps.

## Pod

**technical-artist** (lead), **gameplay-engineer**, **qa-lead**: 3 agents.

Related: future-character-04, -07.
