Give sleep a purpose: fatigue, rest, time passing, and being woken by the world. **No HUD**: the state is read from light, sound and the body.

## What we know (checked 2026-10-18)

- **Sleep today is a pose and a dim:** lying on the back, eye 0.20, lie down and wake 1.5 s each, pitch eases to −60°, URP Volume post-exposure dim to 15% over 2.0 s, fade up 0.5 s. **No time skip** (director gate item 3). Blocked on the tower, in water, on slopes over 15°, or with water in the lie box (`_bmad-output/research/technical-player-character-2026-10-18/research.md` A.4; `player-stances.md`).
- **No HUD** (greenlight C1): no fatigue bar, icon or text.
- **There is no day/night clock or needs model yet** (no file in `Assets/Scripts` mentions day/night, time of day or fatigue, checked 2026-10-18).
- **designer-2's proposal** (player-character pod):
  - Fatigue rises with time awake and exertion; sleep restores it.
  - Time skip of about 8 game-hours per real minute asleep, capped by wake-at-dawn.
  - Interruptions: wildlife nearby, rain, cold. A wake "jolt" moves the eye only.
  - Shelter or a bedroll raises rest quality ×1.5 later.
  - All of these are design proposals, not sheet values.
- **Multiplayer:** a time skip in a shared world needs a rule (all players asleep, or no skip) (#13).

## Gate

- [ ] Noah rules on time skip in multiplayer.
- [ ] game-director approves a HUD-free fatigue read (world cues only).
- [ ] A day/night clock and a needs model exist or are specced alongside.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| N1 | The fatigue curve is tunable data, every value tagged sheet / derived / designer's choice | Design doc; EditMode table test |
| N2 | Sleeping restores fatigue per the curve; a skip, if ruled, advances the clock by the ruled rate | PlayMode test |
| N3 | A threat within the designer's radius wakes the player within 1 s | PlayMode wildlife test |
| N4 | No UI element of any kind is added | Scene and prefab audit |
| N5 | The existing sleep blocks (tower, water, slope, lie box) still hold | player-character H6 matrix |

## Automatic FAIL

- HUD elements.
- A time skip in multiplayer without Noah's rule.

## Pod

**systems-designer** (lead), **gameplay-engineer**, **audio-designer** (wake and breath cues), **qa-lead**: 4 agents.

Related: #13, #15 (wildlife); future-character-09.
