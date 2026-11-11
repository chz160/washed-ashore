---
type: decision
status: accepted
tech: [character-art, probe-tests]
area: [player-body, player]
issues: [49]
prs: []
source: "td-49 B36; baton 23 run b23-sac"
decided_by: "Noah (W-49-4, amend8g carry-over); director-49 (W-49-1, W-49-2)"
created: 2026-11-10
updated: 2026-11-11
supersedes: ""
superseded_by: ""
---
# 0012 Look-down Bar 2: the waived cells for the Survivor

_When the player looks down, his chest and belly need not show in 17 named cells (a stance or gait at a pitch). Everywhere else they must, and the legs must show everywhere. Bar 1 (no holes) is waived nowhere._

## Context
The look-down gate has two bars:
- **Bar 1:** no holes or back faces where the player looks. It is "what the player sees": a ray fails if it hits nothing, hits a surface more than G behind, or hits another body part (Noah, 2026-11-04, "What the player sees (Recommended)"). G is 35 mm on the legs and 10 mm elsewhere.
- **Bar 2 (amend5b):** torso in the bottom band > 0 AND legs or feet > 0, on every idle row and every settled gait row, at Stand +60..+85 and Crouch +80..+85.

The table that keeps Bar 1 clean and the walking hands in view can't also show the torso in some cells. Baton 14e found 156 Bar 2 misses while walking and jogging at +60 and steeper. The misses are single cycle points; other points of the same cells show the torso.

## Options considered
Put to Noah (`gate/pod49-walk-jog-bar2-to-noah`):
1. Accept the current look with the waivers below.
2. Keep the torso visible by retuning the table, trading against the hands.

## Decision
Noah, 2026-11-10 (relayed by team-lead):

> "When I tested in the Windows-BellsBend build when I look down while running I can see the chest and belly of the player charater. Unless something has draskically changed I think thats all good"

Team-lead read this as option 1 and told Noah that reading. **It stands unless Noah objects**; if he does, the cells go back to the director and td. No non-test file under `Assets` changed after that build (2026-11-04 16:26), so it shows the current state.

The torso clause is waived in these cells only (B36):

| Waiver | By | Cells |
|---|---|---|
| W-49-1 | director | Stand idle +60 |
| W-49-2 | director | Stand idle +65 |
| amend8g (1), carried over from the Farmer | Noah | Sprint8 at +60..+85; Jog5 at +60, +65 |
| W-49-4 (new) | Noah | Walk1.15 and Walk1.5 at +60, +65, +70; Jog5 at +70, +75, +80 |

**Full bar kept:** Stand idle +70..+85; Walk1.15 and Walk1.5 +75..+85; Jog5 +85; Crouch idle +80/+85; CrouchWalk1.75 +80/+85. The legs clause is waived nowhere.

## Consequences
- Proof of record: baton 23, `LookDownTests` `2f55d70e`. `StandAndCrouch_LookDown_BackOffsetSweepAndConfiguredTables` PASSED (2,988 s) and `Bar2_HandWorked` PASSED. Exactly 17 "WAIVED bar 2" lines print, each naming its waiver. The 9 full-bar cells met it on 24 of 24 rows (qa sign-off).
- Any torso miss outside these cells, or any legs miss, is a must-keep failure. td can't waive it.
- W-49-2 holds on data only: no still at Stand idle +65 exists (S6 open item).
- The cells are tied to the Survivor and the landed look-down table (`LookDownOffset` `936e724c`, `World.unity` `1b3971b7`).
- td's review of `LookDownTests` `2f55d70e` is not yet on record (qa condition C2). QA read the diff in full; it matches B36 plus two print-only extras.

## Related
[[player-character-body]], [[the-survivor]], [[0015-fold-gaps-accepted-w-49-5]]
