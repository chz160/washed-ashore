**Bug (Noah's manual tower check, 2026-10-21):** "the snagging is a LOT better but when going up the stairs I can still snag on the railing if I'm slightly angled toward the railing. Going down the stairs this doesn't seem to happen and I don't get snagged at all."

## What we know

- **Tower walking comes from the restart (commit 99a9d73, PR #7):** invisible ramps on the eight flights, `TowerWalkZones` for the slope limit, a slimmer capsule inside the tower, ground snap on the ramps, and speed caps (1.15 m/s on flights, 1.5 m/s on upper landings).
- **It happens only going up, angled toward the rail.** That points to the capsule's side contact with the railing collider while it climbs the ramp slope. Likely sources are the slim capsule's radius against the rail's collider, the step offset inside the tower, or the rail collider standing inside the ramp's walk width. Not verified.
- **Not from the player-character pod.** Its only tower change (`TowerWalkZones.cs:61`) saves `StandingHeight` on entry, and the tower radius is unchanged.
- **Overlaps #8 (improve lookout tower stair traversal).** This is a concrete repro for it.

## Repro

1. Walk to the tower base and climb any flight.
2. Turn slightly (about 10-20°) toward the railing side while holding forward.
3. The player stops or catches on the rail. Going down the same way doesn't snag.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| R1 | A PlayMode test climbs every flight holding forward at fixed yaw offsets toward each rail (for example ±10°, ±20°, ±30°) and reports any stop (speed under half the flight cap for more than 0.2 s). It fails on the current code at the angles Noah describes | Test output before the fix |
| R2 | After the fix, no stop at any of those offsets, up and down | Same test |
| R3 | The rail still stops the player from going over or through it | PlayMode test pushing into the rail |
| R4 | TowerWalk timing stays in its bar (base to cab 26.6 s ±10%), and the flight and landing speed caps are unchanged | TowerWalk results |
| R5 | Noah re-walks it and rates it right | Rating log |

## Automatic FAIL

- Removing the rail collider, or making it walk-through.
- Changing the tower speed caps to hide the snag.

## Pod

**gameplay-engineer**, **level-designer**, **qa-lead**; **technical-director** if the capsule size inside the tower changes.

Related: #7, #8, #32; the player-character H8 manual check (`TestResults/player-character/h9/noah-session-20261020-ratings.md`).
