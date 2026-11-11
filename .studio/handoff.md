# Handoff 2026-11-11
Goal and pod: issue #21, trees block the player again and bushes slow you down. Feature pod on branch `trees-bushes-21`; committed there and opened as a PR (not merged).
Done (with evidence):
- Tree colliders on all 11 LOD prefabs, sized to the trunk, rebuilt by script (#14; `TestResults/21-tree-rebuild.log`, `21-dead-rebuild.log`; EditMode 34/34, `21-l2-after-editmode.xml`).
- Bush slowdown system and tuning asset (#15, #20): `PlayerController.cs:209`, `Assets/Scripts/Gameplay/Bush/`, `Assets/Settings/BushSlowdownTuning.asset` linked in `World.unity`. EditMode 11/11, PlayMode 10/10 with the tree tests (`21-bush-tree-playmode-final4.xml`).
- Regression and FPS (#17): tower 7/8 = baseline (`KF_S1_OneFrameBump_144fps` fails in both), water 13/13, wildlife 20/20, spawn 8/8, stance 65/65, FPS 6.77 vs 7.00 ms (-3.3%, `21-after-fps.json`).
- qa-lead's #21 verdict: all of T1-T3 and B1-B4 PASS, no automatic-FAIL (qa-lead message to technical-writer; task #18 completed).
Not verified:
- Bush multipliers are assumed, not playtested.
- macOS and Linux not run; no stance baseline; no visual playtest of the trees.
- Whether the FPS build had the tuning asset linked (qa-lead, #17).
- Known limits: unfitted tree capsules stand out 0.24-0.40 m beyond the trunk; TwistedTree_1 root flare clips up to 0.71 m; stand-out test is axis-only.
Open tasks (subject, owner, status, blocked by): none open except this close-out (#19, technical-writer, done with this file).
Decisions made this session: [[0023-bush-slowdown-design]], [[0024-tree-collider-sized-to-trunk]]. Also earlier proposed notes 0017-0022 (untouched, still proposed).
Risks and blockers:
- Uncommitted changes mix: `ProjectSettings/` is clean, but `features/player-controller.md`, `Home.md`, `0002-desktop-targets-web-retired.md`, `MC39_Glass.mat` and 0017-0022 are other changes the Studio Head kept out; commit #21 files selectively.
- After any full tree rebuild, run `git checkout --` on the impostor `T_*_Impostor.png` and `.png.meta` files (Capture adds a WebGL block).
- Studio follow-up: shared task descriptions were overwritten by concurrent full rewrites (the DOCS line on #14 was dropped three times). Append only, never rewrite another role's task text.
Needs the Studio Head:
- Commit and PR for #21 (title ends with (#21)).
- Playtest the slowdown numbers and the tree collision feel.
- Game director question: may rustle in bushes be audible to wildlife or players (pillar "scarcity and risk")?
- Whether to apply the proposed player-controller.md line, and whether to post the draft comment on #21.
Next pod suggested: playtest-and-tune pod (systems-designer, gameplay-engineer, qa-lead), first tasks: playtest the bush multipliers; decide on rustle audio with game-director; run macOS/Linux and a stance baseline.
