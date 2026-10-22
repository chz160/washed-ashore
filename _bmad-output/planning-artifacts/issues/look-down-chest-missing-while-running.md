**Bug (player-character pod, found 2026-10-19):** when you look steeply down while sprinting or jogging, your chest or belly doesn't show at the bottom of the screen, as the look-down bar requires. Walking and standing are fine.

## What we know

- **Bar of record:** director amend5b. Look-down bar 2 says the chest or belly is in the bottom 20% of the frame, with legs and feet beyond. It applies in full at Stand +60..+85. The torso clause is already waived at Stand +50..+55 and Crouch +40..+75. Bar 1 (no neck stump, shoulder cap or back faces) is absolute everywhere.
- **Measured** (qa-2 R2, `TestResults/player-character/runs/20261019-100525/lookdown/lookdown-sweep.csv`; td recounted it and got the same figures). The table lists settled cycle points with no torso in the bottom band:

  | Gait | Pitch | Points missing bar 2 |
  |---|---|---|
  | Sprint 8 m/s | +60, +65, +70, +75 | 24 of 24 each |
  | Sprint 8 m/s | +80 | 4 of 24 |
  | Sprint 8 m/s | +85 | 1 of 24 |
  | Jog 5 m/s | +60 | 24 of 24 |
  | Jog 5 m/s | +65 | 11 of 24 |
  | Walk 1.15 / 1.5 m/s | all | 0 |

  Bar 1 is 0 in every one of these rows. The legs (13–25 rays) and the swinging arms fill the bottom of the frame instead.
- **Why earlier runs passed:** qa-run3's rows at +60 and up were taken while the gait was still blending in, not at steady speed. The test asserts bar 2 only for standing still, so the moving rows were never checked.
- **td's recommendation** (QA report line 276): the director waives bar 2 for exactly these cells, because they read as a running body. The fix is a speed-dependent back offset in the `LookDownOffset` tuning. That is a product change late in the pod: it would void the Windows builds, the release-build check and the look-down results, and it risks the view body shifting when a sprint starts or stops.
- **Director's ruling (amend8g, 2026-10-19): WAIVER ACCEPTED** for exactly these cells: settled Sprint 8 at Stand +60..+85 and Jog 5 at +60/+65, on the men's Farmer. Bar 1 is not waived anywhere. Idle, Walk and Jog +70..+85 keep the full bar. The director's reasoning: every frame shows trouser legs and boots at the bottom, swinging forearms and hands at the sides, and the full-body shadow running ahead, so it reads as your own body mid-stride. A speed-dependent fix would void the builds and risk a view-body jump when a sprint starts or stops. Full text: `_bmad-output/qa/player-character-qa-report.md`, "Director amend8g".
- **Test (L3), done:** engineer-2's #26 run `f26-lookdown` asserts bar 2 on settled gait rows wherever the full bar applies, and prints WAIVED with the figure in the waived cells. qa-2 verified it at 23:34. All settled rows read bar 1 = 0, including moving frames at -85..+35, which were measured for the first time.
- **If Noah dislikes it in the H9 feel check**, his call stands and this issue becomes the fix (td's speed-dependent offset).

## Stills

qa-2 run `qa24-stills-a2` (2026-10-19 22:58): 3 cases x 8 gait phases. All 24 frames were settled at full speed, with bar 1 = 0, back faces 0 and arm-in-body 0. The 16:9 still is the measured frame. The 21:9 still is taken one frame later (up to about 0.33 s), so its phase and bar 1 were not measured, and its side margins are outside the ray grid (td, QA report line 279).

**Sprint 8, +60**

![Stand_+60_Sprint8_waiver_16x9.png](https://raw.githubusercontent.com/chz160/washed-ashore/issue-assets/issue-37/Stand_%2B60_Sprint8_waiver_16x9.png)

![Stand_+60_Sprint8_waiver_21x9.png](https://raw.githubusercontent.com/chz160/washed-ashore/issue-assets/issue-37/Stand_%2B60_Sprint8_waiver_21x9.png)

**Sprint 8, +75**

![Stand_+75_Sprint8_waiver_16x9.png](https://raw.githubusercontent.com/chz160/washed-ashore/issue-assets/issue-37/Stand_%2B75_Sprint8_waiver_16x9.png)

![Stand_+75_Sprint8_waiver_21x9.png](https://raw.githubusercontent.com/chz160/washed-ashore/issue-assets/issue-37/Stand_%2B75_Sprint8_waiver_21x9.png)

**Jog 5, +60**

![Stand_+60_Jog5_waiver_16x9.png](https://raw.githubusercontent.com/chz160/washed-ashore/issue-assets/issue-37/Stand_%2B60_Jog5_waiver_16x9.png)

![Stand_+60_Jog5_waiver_21x9.png](https://raw.githubusercontent.com/chz160/washed-ashore/issue-assets/issue-37/Stand_%2B60_Jog5_waiver_21x9.png)

Images are hosted on the `issue-assets` branch (images only). Source files: `TestResults/player-character/runs/qa24-stills-a2/lookdown-stills/`, with figures in `waiver-stills-summary.txt`.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| L1 | **Done (amend8g, waived).** The director rules: waive these exact cells, or fix | Decision record; QA report |
| L2 | If fixed: bar 2 met on every settled Sprint 8 and Jog 5 row at Stand +60..+85, with bar 1 still 0 everywhere and no view-body shift when sprint starts or stops | LookDown sweep; a sprint start/stop pop check |
| L3 | **Done (f26-lookdown, qa-verified).** Either way: the test asserts bar 2 on settled gait rows wherever the full bar applies, and prints "WAIVED" with the figure in waived cells | `LookDownTests` source and output |
| L4 | Walk, idle and Jog +70..+85 keep the full bar | LookDown sweep |

## Automatic FAIL

- Widening bar 2 or any tolerance to make these rows pass without a director ruling.
- Changing walk, sprint or jog speeds.

## Pod

**gameplay-engineer** (test, and any offset fix), **qa-lead**; **game-director** rules. td owns any fix plan.

Related: the player-character pod (#24 regrade); #35.
