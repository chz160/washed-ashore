# Board (snapshot of the task list, kept by team-lead)

Updated: 2026-11-13 21:16 UTC. #9 pod complete, with every task closed. The team is shut down. Nothing is committed.
Pod: tech/review for issue #9 (frustum culling investigation). Branch: frustum-culling-9 (from main at eaf238d).
Outcome: qa-lead's verdict (TestResults/9-qa-verdict.md) is Q1, Q2, Q4 and Q6 PASS; Q3 and Q5 INCONCLUSIVE, accepted by the Studio Head; no automatic FAIL. The note (_bmad-output/poc/perf-frustum-note.md, blob c6e108c4) recommends no setting change as measured.
Studio Head rulings: TestResults/9-studio-head-rulings.md. Agents launch reviewed measurement runs without asking (0026 as amended).

Open for the Studio Head:
- Commit: which evidence to force-add (TestResults/ is gitignored; technical-writer suggests 9 small files in .studio/handoff.md; 9-runs/ is 6.5 GB). Keep MC39_Glass.mat out of the #9 commits.
- Post .studio/drafts/issue-f9-fish-counter.md (the BellsBendFpsWalk fish counter reads 0).
- Follow-ups to raise: the cause of FAR's main-thread spikes; a zone/view far clip (unmeasured); fixing the harness edge-still aim and noise repeat before #10/#11.

| # | Task | Owner | Status |
|---|---|---|---|
| 23 | Culling counters and trial flags | platform-tools-engineer | completed |
| 24 | Census | platform-tools-engineer | completed |
| 25 | Measurement plan (rev 2, 710f0d11, addenda 1-7) | technical-director | completed |
| 26 | Player build and run script (b7843ce9; script 7d16a719) | platform-tools-engineer | completed |
| 27 | Analysis | platform-tools-engineer | completed (QA PASS) |
| 28 | Pop-in judgement | technical-artist | completed (QA PASS) |
| 29 | Recommendation note (c6e108c4) | platform-tools-engineer | completed (QA PASS, td PASS) |
| 30 | Approve the note | technical-director | completed (PASS c6e108c4) |
| 31 | Try to break the note | skeptic | completed (PASS) |
| 32 | #9 verdict | qa-lead | completed |
| 33 | Close-out | technical-writer | completed (DOCS OK) |
