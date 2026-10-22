**Bug (Noah's H9 feel check, 2026-10-20):** jump "not working while running, finicky while walking". Noah was running on open ground, not falling, and Space did nothing.

## What we know

- **Jump needs the ground on the exact press frame.** At `PlayerController.cs:220` the jump only fires when `controller.isGrounded` is true on the frame Space is pressed, with no grace window and no input buffer. `isGrounded` comes from the previous `controller.Move`.
- **Likely cause (read from source, not verified in a run):** running over bumpy or downhill terrain, the horizontal step leaves the capsule in the air for a frame or two. `isGrounded` reads false, and a press on those frames is dropped. The stick velocity is -2 m/s (`groundedStickVelocity`, line 33). The ground snap (lines 230-234) is only switched on in the tower flight zones (`TowerWalkZones.cs:67`), so open terrain has none. That fits "worse running than walking".
- **Probably older than the player-character pod.** On `main` (144f54c) the line was already `controller.isGrounded && CanJump && JumpPressed()`. The pod added `stance == Stance.Stand` and the stance-consumed frame check, which don't apply when standing. Not confirmed on a pre-pod build.
- The Player.log has no error lines for it.

## Fix direction (td to confirm)

- **Coyote time:** a jump is allowed for a short window (about 0.10-0.15 s) after the player was last grounded, if they haven't jumped since.
- **Jump buffer:** a press up to about 0.10-0.15 s before landing jumps on landing.
- Optional: ground snap on open terrain while walking downhill, never after a jump.
- Jump height (1.2 m) and walk/sprint speeds stay as they are.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| J1 | Repro first: a PlayMode test runs Sprint 8 and Walk 5 over real World terrain (downhill and bumpy segments) pressing jump at fixed intervals, and reports the share of presses that jump. It fails on the current code | Test output before the fix |
| J2 | After the fix, every press made while running or walking on the ground (or within the grace window) jumps, on the same route | Same test after the fix |
| J3 | No double jump: a second press in the air, or after the grace window, does nothing. No jump from a lowered stance, in swim or in a climb area (current rules kept) | PlayMode tests |
| J4 | Jump height 1.2 m and the speed table are unchanged; tower stairs feel unchanged (TowerWalk timing within its bar) | Speed table test; TowerWalk |
| J5 | Noah re-rates jump "right" in a feel check | Rating log |

## Automatic FAIL

- Changing jump height, gravity or move speeds to hide the bug.
- Allowing jumps while falling from a ledge beyond the grace window.

## Pod

**gameplay-engineer**, **qa-lead**; **technical-director** confirms the fix approach.

Related: the player-character pod (H9 rating log `TestResults/player-character/h9/noah-session-20261020-ratings.md`); #38 (sit and sleep feedback).
