**From Noah's H9 feel check (2026-10-20):** sit (X) and sleep (Z) "did not work at all" on the first tries. Both worked once Noah stood still on flat ground facing downhill, so the rules work, but the player gets no sign of why a press did nothing. Revisit the sit and sleep spot rules, and give the player feedback when they can't sit or lie down.

## What we know

- **Refusals are silent by design.** `PlayerController.Stance.cs:166-174` (`EntryBlockedBy`) returns a reason (water, tower or climb zone, not grounded, sit surface, sleep surface plus detail), but nothing shows it to the player and nothing writes it to Player.log.
- **Sit rules** (`StanceTuning.cs:41-47`, `PlayerController.Stance.cs:323-327`):
  - you are standing still (0.10 m/s or less);
  - the ground slope under you is 25° or less;
  - a 0.6 m wide, 0.9 m long box in front of you is clear from 0.15 m to 0.6 m above your feet.
- **Sleep rules** (`PlayerController.Stance.cs:331-353`):
  - the slope is 15° or less;
  - 5 ground samples along the lie length vary by 0.5 m or less, with no water over them;
  - a lie box (band 0.15-0.5 m) is clear;
  - there's clearance around the eye.
- **Likely reason the first tries failed (not verified):** the box is fixed in front of you, so on any slope you face uphill, rising terrain can enter it at 0.15 m. Plant colliders can block it too. Noah's working case, facing downhill on gentle ground, fits this.
- Crouch and squat worked everywhere Noah tried, so water, the tower zone and "not grounded" were not the cause.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| S1 | A refused sit or sleep press gives the player feedback with no HUD (the game has none, by rule). For example, the character starts to sit and stands back up, or a short head or body motion, or a sound. The director picks the form | Director ruling; a PlayMode test that a refused press triggers the feedback |
| S2 | The refusal reason is logged in dev builds (one line per refused press, naming the rule) | Player.log from a dev build |
| S3 | The spot rules are reviewed against real terrain: facing uphill on a gentle slope, next to grass and bushes, on sand. Any change in tolerance (slope, box band, box placement, for example checking all around the player instead of only in front) is a design ruling, not a test change | A designer write-up with stills from 5 or more real spots; a PlayMode sweep of sit and sleep acceptance at those spots |
| S4 | The existing stance tests still pass, and the water, tower and climb blocks are unchanged | Character EditMode and PlayMode runs |

## Automatic FAIL

- Any on-screen text or HUD element.
- Loosening a rule only to make a test pass, without a design ruling.

## Pod

**systems-designer** (rules, feedback form), **gameplay-engineer**, **audio-designer** if the feedback is a sound, **qa-lead**; **game-director** rules on the form of the feedback.

Related: the player-character pod (H9 rating log `TestResults/player-character/h9/noah-session-20261020-ratings.md`); `_bmad-output/design/player-stances.md` §5.
