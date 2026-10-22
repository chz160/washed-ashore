Plant the survivor's feet on slopes and on the lookout tower's stair treads. Without foot IK the body floats above sloped ground, and it is visible when looking down.

## What we know (checked 2026-10-18)

- **Today:** the body mesh sits h below the capsule foot point, where h is the controller's measured hover (its skin width): **h = 0.0500 m**, measured 2026-10-18 (`TestResults/player-character/eng-hover1.xml`). So the feet touch flat ground. Nothing else (td ruling `ruling/player-character-eye-gap-drop-scale`; `_bmad-output/design/player-stances.md` §4).
- **Float on slopes** [derived, not measured on the slope: a capsule on a slope θ hovers (r + h)(1/cosθ − 1) above the ground once the body is dropped by h; designer-2, `player-stances.md` §4]:

  | Slope | Off the tower (r 0.4) | On the tower (r 0.25) |
  |---|---|---|
  | 10° | 0.007 m | 0.005 m |
  | 20° | 0.029 m | 0.019 m |
  | 30° | 0.070 m | 0.046 m |
  | 45° (terrain limit) | 0.186 m | 0.124 m |
  | 52-56.3° (tower ramps) | — | **0.19-0.24 m** |

- **Director ruling** (`gate/player-character-phase0` item 5, 2026-10-18): the tower-ramp float is **accepted as placeholder** and logged here. The ruling was given on an earlier estimate of 0.24-0.29 m; the corrected figure (0.187 m at 52°, 0.237 m at 56°, 0.241 m on the 56.3° top flight) is smaller, so it stays inside what was accepted. **The ground-probe body drop was not approved** ("a new system on the tower, a risk to C3"). If Noah's feel check rates the stairs "not right" because of the float, it goes back to the director.
- **The tower walks on invisible ramps** (`WalkableTowerBuilder`, `WALK_Ramps`), not the treads, so IK targets must come from the visible treads, not the ramp collider.
- **Rig:** Generic, not Humanoid (`_bmad-output/research/technical-player-character-2026-10-18/research.md` A.2), so no Humanoid IK goals. Animation Rigging two-bone IK or a scripted two-bone solve works on Generic.
- **Tower feel rules (greenlight C3):** no new head bob, sway, roll or FOV change. The camera is on `CameraPivot`, never a bone, so pelvis motion must not move the eye.

## Gate

- [ ] technical-director approves the IK method (Animation Rigging package vs a scripted solve; a package install needs Noah).
- [ ] game-director confirms that a tower IK pass is in scope (it was refused as a quick fix under C3).

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| K1 | The planted foot is within 3 cm of the surface on 0-45° terrain and on every flight's visible treads | PlayMode foot-plant log |
| K2 | The pelvis drops by the lower foot; knees never invert | PlayMode check; stills |
| K3 | **The camera pivot does not move** because of IK (0.0 cm vertical delta vs IK off) on every flight | Camera Y log per frame, tower |
| K4 | TowerWalkTests pass unchanged (up 26.6 s ±10%); stair speeds 1.15 / 1.5 unchanged | Test XML |
| K5 | Foot slide stays ≤ 10 cm per stride at walk 5 and sprint 8 on flat ground | player-character H4 measure |
| K6 | Frame cost within the perf budget (Windows FPS within 5% of IK off) | FPS JSON |

## Automatic FAIL

- Any camera bob, sway, roll or FOV change on the tower.
- Changing the tower ramps, speeds or capsule.
- A package install without Noah's approval.

## Pod

**technical-artist** (lead), **gameplay-engineer**, **qa-lead**: 3 agents.

Related: #8; future-character-13, -14.

