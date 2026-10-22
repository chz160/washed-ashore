Replace the player-character pod's script-authored placeholder clips with better locomotion and stance animation, and close the strafe and back gaps. *(td addition to the spec's R1 list.)*

## What we know (checked 2026-10-18)

- **The pack locomotion is slow:** Walk ≈ 1.34 m/s and Run ≈ 3.0 m/s, in place, both genders (artist measurement, `_bmad-output/research/technical-player-character-2026-10-18/research.md` A.2). The game runs at walk 5 and sprint 8 (unchanged), so the pod authored a ~5 m/s jog and a ~8 m/s sprint by script at a playback rate ≤ 1.4 (td ruling, A.2 departure).
- **Strafe and back foot slide is not graded yet.** Noah narrowed the pod's H4 to forward movement only, at walk 5 and sprint 8 m/s ("narrowed by Noah", 2026-10-18); strafe and back slide were information only and come here. The pack has strafe and back clips only at Run speed (Run_Left/Right/Back, about 3.0 m/s). Below 0.7 × that speed (about 2.1 m/s, which includes the tower speeds 1.15 and 1.5) they blend with Idle and the feet slide. The pack clips also slide by themselves: Walk 0.048 m and Run 0.066 m per plant (manifest, max extent within a plant at 4× the key rate, 3 cm band). The authored Jog/Sprint strafe and back variants read 0.000 in Blender. Figures and the known-limit note are in `_bmad-output/poc/character-clip-manifest.md`.
- **Slide measure of record:** per plant (foot within 3 cm of its lowest height), the largest horizontal distance between any two world positions of the foot, sampled every frame in-game on the shadow body (`player-stances.md` §3).
- **Script-authored stances:** Jump (start/air/land), Crouch idle and walk, Squat, Sit and Sleep were authored in `tools/blender/character/` and "will look like a placeholder" (artist section §5). Noah's H9 feel check rated them; see `_bmad-output/qa/player-character-qa-report.md`.
- **Hands are splayed in every clip (director amend8b, 2026-10-19).** The Quaternius pack's hand pose has spread fingers, and every pack and script-authored clip inherits it. The re-authored Sprint was accepted with relaxed hands waived for this pod; the splay reads worst on the full-swing arms (the shadow, the debug camera, later other players).
- **Watch note, Sprint mid-flight knees:** the re-authored Sprint (hip 0.812 m at mid-stance, stance knee 48°, rate 1.379 at 8 m/s) shows both knees at about 80° in mid-flight, where the pack Run's leading leg extends (26°/34°). It may read as a tucked hop; it's an H9 watch item (`_bmad-output/poc/character-stills/gait/sprint_reauthor_sheet.png`).
- **Diagonal sprint slides about 0.19-0.20 m per plant (measured 2026-10-19, `TestResults/player-character/f23-n3b.xml` and `runs/f23-n4b/`).** At 8 m/s on both rigs: forward 0.002 m and back 0.001-0.002 m (both asserted), left and right 0.006-0.007 m, all four diagonals 0.194-0.199 m (report-only, over the 0.10 bar). Likely cause (td, read only, not proven): the `Stand` tree is Freeform Directional 2D with Sprint samples only on the axes, so a diagonal blends two perpendicular clips that cover about 0.71 of the ground, and LocomotionRate (1.381) doesn't make it up. A phase offset inside the clips is not ruled out (contact frames not read). The same layout is in the Women controller and all six `_Shadow` overrides. Fix options: authored diagonal clips, or a rate gain of 1/blend-magnitude. **The gain makes steps up to 41% faster, a look change that needs the game-director's approval first** (director amend8f). td ruling: QA report lines 265-269.
- **Sprint mid-stance arms read as a boxer's guard (director amend8f, 2026-10-19).** In the debug-cam side stills (`runs/f23-n4b/viewsync/debugcam_Sprint8_midstance_side_*.png`) both hands sit forward of the chest at once. Accepted for this pod as a known item; mid-flight arm opposition is fine.
- **Sources:** Mixamo is out (Adobe login). Other CC0 sets (Quaternius animation libraries) mean a download, which needs Noah's approval with file name, URL, size and licence.

## Pass criteria

| # | Criterion | Evidence |
|---|---|---|
| Q1 | Walk-speed strafe and back clips per gender (replacing the Idle blend below about 2.1 m/s); foot slide ≤ 10 cm on every plant, by the measure of record, **forward, strafe and back**, at every speed from 1.15 to 8 m/s, including the tower speeds | Foot-plant measurement per direction and speed |
| Q2 | Every clip's measured ground speed in the manifest; playback rate ≤ 1.4 everywhere | Manifest; EditMode check |
| Q3 | Every clip has a recorded game-compatible licence | Manifest |
| Q4 | The facing report shows the same decision for every new FBX; 0 `ambiguous` | `TestResults/model-facing-report.json` |
| Q5 | Noah rates each stance "right" from first person and the debug camera | Noah's verdict |
| Q6 | Relaxed hands across ALL clips (pack and authored, both rigs): fingers in a curled rest, no splay, checked at every phase of every clip; the first-person view hand count stays 0 where the cap requires it | Hand close-up sheet per clip; a finger-spread measure per clip in the manifest |
| Q7 | Sprint mid-flight: the leading leg extends toward contact (knee ≤ 40° at mid-flight), keeping the stance figures (hip ≥ 0.81 m, stance knee ≤ 50°) and rate ≤ 1.4 | Side sheet against the pack Run; rate and H4 re-checked |
| Q8 | Diagonal sprint and jog: foot slide ≤ 10 cm on every plant at 5 and 8 m/s in all four diagonals, both rigs; any rate-gain fix has the director's approval for the faster cadence | `Sprint8_AllDirections_BothRigs` diagonal rows turned from report-only back to asserted |
| Q9 | Sprint mid-stance: the hands don't sit forward of the chest together (no guard look); mid-flight opposition and the arm cap (first-person hand count 0) are kept | Debug-cam side stills, mid-stance and mid-flight; director's look |

## Automatic FAIL

- A download without Noah's approval.
- Hand-made Blender steps.
- Changing walk, sprint, jump or tower speeds to fit a clip.

## Pod

**technical-artist** (lead), **qa-lead**: 2 agents.

Related: future-character-08, -11.
