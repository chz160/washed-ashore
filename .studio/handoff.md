# Handoff 2026-11-13
Goal and pod: issue #9, is frustum culling doing the most it can? Tech/review pod on branch `frustum-culling-9`. Measure and recommend only; nothing committed, no live setting changed.
Done (with evidence):
- Harness, census, plan and measurement run (#23-#27): `Assets/Scripts/Perf/FrustumProbe.cs`, `tools/perf/frustum_matrix.ps1`, `TestResults/9-culling-census.json`, plan `TestResults/9-measurement-plan.md` (710f0d11), release build b7843ce9, runs in `TestResults/9-runs/`.
- Note `_bmad-output/poc/perf-frustum-note.md` (b0bdfce9): technical-director #30 PASS, skeptic #31 PASS, qa-lead #29 PASS.
- qa-lead's #32 verdict (`TestResults/9-qa-verdict.md`): Q1, Q2, Q4, Q6 PASS; Q3 and Q5 INCONCLUSIVE; no automatic FAIL. Result: no change recommended as measured. A 600 m far clip saves 1.6-3.5 ms p95 but changes the skyline at 3 of 4 spots (technical-artist #28).
- Vault: decisions 0025, 0026, 0027; pattern `frustum-culling-harness-and-measurement-rule`; feature `frustum-culling-investigation`; log entry; Home.md link lines.
Not verified:
- Five rows still INCONCLUSIVE after the rerun (SHADOW at park, ridge_top, valley_floor, cab; FISHOFF at fish_water). The Studio Head accepted them and finished #9 (`TestResults/9-studio-head-rulings.md` item 4); no third set, no GPU-timing follow-up.
- Engine-side shadow-caster culling at 50 m; LAYER pop-in where the cull acted; shadow cost at 4 of 5 spots; Q4 covers 517 of 551 renderers (edit time). More in the note's "Not measured" list (13 items).
- The writer ran no tests or measurements, and read the #9 note only in part (first 60 and last 55 lines plus qa-lead's verdict).
- 0027's sources in #25 task metadata and qa-lead's 6c numbers were taken from messages, not read.
Open tasks (subject, owner, status, blocked by):
- #29 Write the frustum culling recommendation note, platform-tools-engineer, pending (signoff_docs set "NONE"; note delivered, blob b0bdfce9).
- #33 Close out the #9 pod, technical-writer, done with this file (blocked only by #29's status).
Decisions made this session: [[0025-savings-rule-s1-perf-trials]], [[0026-agents-may-launch-the-player]] (amended: no ask for pre-reviewed runs), [[0027-reruns-replace-and-pre-data-facts-are-blind]]. 0016 unedited. Earlier proposed 0017-0022 untouched.
Risks and blockers:
- Process breaches of 0026 disclosed by qa-lead: platform-tools-engineer's Python during set 1 and qa-lead's during the rerun.
- Addendum 6c was set after the rule-setter saw set 1 route numbers; accepted by the Studio Head with disclosure.
- Known harness defects for #10/#11: edge-still aim (no line of sight, body in frame), no same-frame repeat on edge pairs, `BellsBendFpsWalk.CountFish` reads 0 by construction.
- `ProjectSettings/` and `Packages/` clean (`git status -- ProjectSettings Packages` empty, 2026-11-13).
- Uncommitted work mixes: commit #9 files selectively. Not #9: `Assets/World/Lookout/MC39_Glass.mat` (kept by the Studio Head, out of #9 commits), `0002-desktop-targets-web-retired.md`, `features/player-controller.md`, `patterns/collider-geometry-for-a-steered-capsule.md`, `world/lookout-tower.md`, decisions 0017-0022, `tools/asset-library/`.
- #9 vault files: `decisions/0025-*.md`, `0026-*.md`, `0027-*.md`, `patterns/frustum-culling-harness-and-measurement-rule.md`, `features/frustum-culling-investigation.md`, `log.md` (top entry; the file also holds earlier uncommitted entries), `Home.md` (4 appended lines).
Needs the Studio Head (Noah):
- Post the issue draft `.studio/drafts/issue-f9-fish-counter.md` (fish counter reads 0).
- Commit and PR for #9 (title ends with (#9)), selectively; keep MC39_Glass.mat out.
- Decide which evidence to force-add: `TestResults/` is gitignored (`.gitignore:76`), so files need `git add -f`. Suggested small set: `9-measurement-plan.md`, `9-analysis.md`, `9-analysis.json`, `9-analysis-notes.md`, `9-qa-verdict.md`, `9-studio-head-rulings.md`, `9-popin-review.md`, `9-culling-census.json`, `9-run-sheet.md`. `TestResults/9-runs/` is 6.5 GB (1,705 stills), so Noah decides whether to add any of it. Several vault notes cite these files.
Follow-ups (none started):
- Edge-still aim (no line of sight, body in frame) and a noise repeat on edge pairs: platform-tools-engineer.
- Plan lessons for #10 and #11: route sample band invalid on a fixed-duration walk; median-only arm test hides tail cuts (see the pattern note).
- A zone or view-based far clip: unmeasured.
- Fix `BellsBendFpsWalk.CountFish` (issue draft above).
Next pod suggested: #10 or #11 perf pod (platform-tools-engineer, technical-director, qa-lead, technical-artist); first tasks: reuse FrustumProbe, fix `CountFish`, write the plan under 0025 and 0027.
