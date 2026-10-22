Let the survivor sit on things in the world (logs, stumps, steps, chairs, a bed edge) and **sit on the lookout tower's top deck to take in the view**, which the director asked for at the Phase 0 gate.

## What we know (checked 2026-10-18)

- **Sitting today is on the ground only:** eye 0.80, capsule 0.95, enter 1.0 s, exit 0.8 s, only when still on dry ground, with slope ≤ 25° and a clear leg box (`_bmad-output/research/technical-player-character-2026-10-18/research.md` A.3/A.4; `_bmad-output/design/player-stances.md`).
- **The whole tower blocks every lowered stance** (director gate item 4, `gate/player-character-phase0` in `studio:decisions`): "the tower is a climb and a lookout, not a resting place, in this pod. Sitting on the top deck to take in the view goes in as a future issue."
- **Tower eye rules:** R8 eye 0.20 m from every tower surface, 0.15 m on the ordinary walking line, 0.08 m absolute floor, near plane 0.10 (`LookoutTuning.EyeClearanceR8`; `ruling/lookout-tower-near-plane-eye-rule-amend3`). These were measured for the standing eye only.
- **designer-2's proposal** (player-character pod): authored sit points (seat height, facing, leg room) found by the Interactor ray, with the existing prompt line "E  Sit" (no new UI). Eye = seat height + 0.79 (seated eye, Pheasant *Bodyspace*). The capsule snaps onto the point. Exit goes back along the facing, with the headroom fallback Stand → Crouch → Squat.
- **The firefinder stool in the cab** is the obvious observation seat. The firefinder already overrides the view.

## Gate

- [ ] game-director picks which tower seats exist (top deck and/or cab stool) and whether sitting there changes the tower's "no rest" rule elsewhere.
- [ ] An R8 / 0.15 / 0.08 m sweep at each tower seat's seated eye, by the tower's own method, **before** any tower seat is enabled.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| O1 | Sit points on at least 3 seat heights (low log, chair, bed edge) give eye = seat + 0.79 ±3 cm | PlayMode stance test |
| O2 | No camera inside the seat or body at pitch −85..+85 on every sit point | Pitch sweep |
| O3 | Snap onto the point within 0.2 s with no visible pop; exit with headroom fallback | PlayMode; twin-diff stills |
| O4 | **Tower top deck:** sitting is allowed only at authored deck points, and the seated eye passes R8 0.20 / 0.15 / 0.08 m and the 0.10 near plane | The tower eye report at the seated eye |
| O5 | The only UI is the existing prompt line ("E  Sit") | Scene audit |
| O6 | TowerWalkTests pass unchanged (up 26.6 s ±10%); tower speeds 1.15 / 1.5 unchanged | Test XML |

## Automatic FAIL

- Any lowered stance on the tower outside an authored, measured seat.
- HUD elements.
- Changing tower speeds or the tower capsule.

## Pod

**systems-designer** (lead), **gameplay-engineer**, **level-designer** (sit points), **qa-lead**: 4 agents.

Related: #8; future-character-10.
