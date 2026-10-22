Make the women's rig pass the first-person checks the default men's Farmer passes, so it can be offered as a player look.

## What we know (checked 2026-10-19)

- **The player is always the scene's Men_Farmer.** The women's rig is reached only through the test kit (`PC_RIG=women`) and the dev-only `RebuildWith` hook, which the release build strips (H7). The player-character pod graded H5 and the look-down bars on the men's Farmer only, and the women's rig is unscaled (`_bmad-output/qa/player-character-qa-report.md` lines 179, 214, 454). td ruling: the women's failures are not a gate fail (same report, line 272, 2026-10-19 09:59 CDT).
- **Women's pass f23-w1** (`TestResults/player-character/f23-w1.xml`, evidence in `runs/f23-w1/`): 13 of 18 tests passed. 2 of the failures were the forced-red tests, failing by design. 3 real failures pass on the men's rig:
  - **`EyeEnds_LowAndHigh`:** the camera sees back faces (inside the mesh) in the pitch sweep at squat, sit and sleep, 1 to 81 per pose. Crouch passes, and eye errors are within 0.015 m.
  - **`SleepPitchEnds`:** 106 and 107 back faces at pitch −45 and −85.
  - **`StandAndCrouch_LookDown_BackOffsetSweepAndConfiguredTables`:** shoulder bones hit the no-neck bar at Stand +60 (6 hits) and +65 (7), and at Walk/Jog cycle points from about +65 to +85. Jog +80/+85 also has torso-band hits of 1–2. The `LookDownOffset` tables were tuned on the men's body.
  - **Report-only:** at Sprint 8, pitch 0/+15/+30, 57/84/47 arm vertices are inside the body. Men read 0.
- **Likely causes (engineer-2, read only, no Unity run, all unverified):**
  - One `LookDownOffset` table seems to serve both bodies. That's inferred, not read from the tuning code. In the static sweep, women need about 0.05 m more back offset at +60, +65, +80 and +85, where every hit is `Shoulder.L`/`Shoulder.R`.
  - The low-stance back faces (squat 1–2, sit 13, sleep 81–107) are wrong-side faces close to the camera, and the eye point itself measures correct. They haven't been located: the sleep stills don't show where they are. Locating them needs a small diagnostic in a Unity run, which the pod didn't spend (td, QA report line 273, 10:00 CDT).
  - Whether the women's rig gets its own look-down tables is a design question for this issue (td line 273).
- **Passed on women:**
  - foot slide: Jog 5 at 0.018 m, Sprint 8 at 0.002 m;
  - view/shadow sync: 0.061 mm with 0 resyncs;
  - pose bounds, wrists included;
  - interpenetration positive control: 7 → 1340 of 3276;
  - hand rays: 0 at every pitch for Jog and Sprint.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| W1 | Women's eye height matched by the same rule as men's (scale or clip eye offset), with eye error ≤ 0.015 m at every stance | `EyeEnds_LowAndHigh` under `PC_RIG=women` |
| W2 | 0 back faces in the pitch sweeps at every stance, sleep included | `EyeEnds_LowAndHigh`, `SleepPitchEnds` |
| W3 | Look-down bar 1 (no neck or shoulder) everywhere and bar 2 where the men's rig has it, with women's own `LookDownOffset` tables if needed | LookDown sweep under `PC_RIG=women` |
| W4 | Sprint arm-inside-body 0 at pitch −15..+30, like the men's rig | Hand-ray and interpenetration files |
| W5 | All men's rig results unchanged | Default-rig run vs the pod's final run |

## Automatic FAIL

- Changing the men's tables or tolerances to make women pass.
- Hand-made Blender steps.

## Pod

**technical-artist** (lead), **gameplay-engineer**, **qa-lead**: 3 agents. The director decides when the women's rig becomes a player choice (with future-character-01, creator UI).

Related: future-character-01, -02, -14.
