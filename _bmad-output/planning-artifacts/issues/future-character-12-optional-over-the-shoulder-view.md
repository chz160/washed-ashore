Decide whether Washed Ashore gets an **optional** over-the-shoulder third-person view, and on what terms, so it doesn't break multiplayer fairness, the tower or interactions. **Today no player-facing third person exists** (greenlight C2); the H7 debug camera is dev-builds only.

## What we know (checked 2026-10-18)

- **The body is ready for it:** full-body true first person on one rig (director gate item 1), so a third-person view needs no second animation set (`_bmad-output/research/technical-player-character-2026-10-18/research.md` C.3). Placeholder gaps it would expose: no foot IK (future-character-11), the swim pose (-08), remote look-aim (-13).
- **How others offer it** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` C.1, C.5):
  - Server rule: DayZ `disable3rdPerson`; Arma `thirdPersonView` 0 / 1 / 2 (vehicles only); SCUM `AllowFirstPerson` / `AllowThirdPerson`.
  - Separate queues: PUBG added first-person servers by player demand (2017-18), only where population filled them.
  - First person only: Rust, which withheld third person until the camera "can't see through walls".
  - Single-player toggle: RDR2.
  - **The server must be able to force the view both ways.**
- **Fairness** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` C.4): third person sees over and around cover. Counters are first-person-only pools (which split the population; in a persistent permadeath world that means separate worlds or characters), server-side visibility culling (Valorant Fog of War; NGO `CheckObjectVisibility`), and first-person aiming. **Culling limits wallhacks but does not remove third-person peeking**, because the allowed camera is the peek.
- **Camera tech** (`_bmad-output/research/technical-player-character-2026-10-18/research.md` C.2): Cinemachine 3 Third Person Follow (shoulder offset, camera side 0-1 for a blended shoulder swap, built-in collision); Deoccluder (transparent layers for foliage, minimum occlusion time). Aim from the muzzle toward the camera's hit point; a camera-only trace lets players shoot through cover.
- **Tower and interactions:** 0.62-0.72 m flights and a small cab leave no room for a shoulder camera. Interact rays must start at the eye, not the third-person camera.
- **Arm swing is capped today, for every viewer (director amend6, 2026-10-18).** The placeholder Jog and Sprint clips were re-authored with a smaller arm swing, so no hand enters the first-person frame at pitch −15..+30. The shadow body and the first-person view body share one pose, so the cap applies to both, and so to anything that shows the shadow body. **When this issue is built, the full arm swing comes back for the shadow, for other players and for third person, and the cap moves to the first-person VIEW body only** (for example a view-body-only arm adjustment applied after the pose copy). The full-swing clips stay in `tools/blender/character/` source for that.
- **Water:** the camera never goes underwater (C4), in any view.

## td recommendation (input to the gate)

Worlds default to **first person only**. Third person is a **per-world server rule, off by default**, forced off inside the tower footprint, the cab and interiors, and whenever collision shortens the boom below a threshold. Aiming is in first person, and interaction rays always start at the eye. Server visibility culling is built with #13 either way.

## Gate

- [ ] game-director rules on whether third person fits the pillars (observation, survival) at all.
- [ ] Noah picks the offer model (server rule, queue, or none).
- [ ] #13 picks the netcode, since fairness depends on server authority.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| V1 | The view is a server-authoritative setting; a client can't enable it where the world forbids it | PlayMode / two-client test |
| V2 | Forced first person inside the tower footprint, the cab and interiors, with a blend ≤ 0.3 s and **no change to tower feel** (no bob, sway, roll or FOV change) | PlayMode camera log |
| V3 | Camera collision: 0 frames with the camera inside geometry on the tower approach, in the forest and at walls; foliage never pushes the camera | Automated sweep |
| V4 | Interaction and aim rays start at the eye: no interaction through a wall from third person | PlayMode test |
| V5 | The camera stays above water in every view and swim pose | Eye-vs-surface log |
| V6 | Noah's feel check of the toggle | Noah's verdict |
| V7 | Full arm swing restored for the shadow body, other players and the third-person view; the first-person view body keeps the cap (0 hand rays inside the frame at Jog 5 and Sprint 8, pitch −15..+30, every phase) | Hand-ray sweep in first person; third-person stills of Jog and Sprint |

## Automatic FAIL

- A player-facing third-person view shipped before the director's and Noah's rulings.
- Interaction rays from the third-person camera.
- Any change to the tower's first-person feel.

## Pod

**technical-director** (lead), **gameplay-engineer**, **systems-designer** (fairness rules), **qa-lead**: 4 agents.

Related: #13; future-character-08, -11, -13.
