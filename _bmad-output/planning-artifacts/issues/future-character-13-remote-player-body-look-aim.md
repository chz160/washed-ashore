Decide and build what **other players** see of a survivor: the head (hidden from its owner), where they are looking, and a cheap far version. *(td addition to the spec's R1 list.)*

## What we know (checked 2026-10-18)

- **Owner view:** the Head slot is `ShadowsOnly` for the local player, and the body does not pitch or lean with the camera (`_bmad-output/research/technical-player-character-2026-10-18/research.md` A.1; director gate item 1). A remote player would see a survivor who never looks up or down.
- **Research** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` C.3): true-first-person setups drive pitch and yaw with about 10 additive aim-offset poses. Unreal hides the owner's body but draws it for others and in shadows.
- **Arm swing is capped today, for every viewer (director amend6, 2026-10-18).** The placeholder Jog and Sprint clips were re-authored with a smaller arm swing, so no hand enters the first-person frame at pitch −15..+30. The shadow body and the first-person view body share one pose, so the cap applies to both, and so to anything that shows the shadow body. **When this issue is built, the full arm swing comes back for the shadow, for other players and for third person, and the cap moves to the first-person VIEW body only** (for example a view-body-only arm adjustment applied after the pose copy). The full-swing clips stay in `tools/blender/character/` source for that.
- **Networking** doesn't exist yet (#13). Character LOD for crowds is in future-character-07 and #11.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| P1 | Remote players draw the head; the owner never does (render-layer or per-camera rule, no duplicate rig) | Two-camera test; pitch sweep for the owner |
| P2 | Spine and head pitch follow the camera pitch for others (−85..+85) without changing the owner's camera or tower feel | PlayMode; owner camera Y log |
| P3 | Look pitch replicates as one quantised byte or less per tick (after #13) | Network profiler |
| P4 | Remote characters use LOD from future-character-07 | Triangle counts |
| P5 | Remote players and the owner's shadow show the full Jog/Sprint arm swing; only the owner's first-person view body keeps the cap (0 hand rays inside the frame at Jog 5 and Sprint 8, pitch −15..+30, every phase) | Two-camera stills; first-person hand-ray sweep |

## Automatic FAIL

- Any change to the owner's camera (bob, sway, roll, FOV) from aim poses.
- Networking code on main before #13 rules.

## Pod

**gameplay-engineer** (lead), **technical-artist** (aim poses), **qa-lead**: 3 agents.

Related: #13, #11; future-character-06, -07, -12.
