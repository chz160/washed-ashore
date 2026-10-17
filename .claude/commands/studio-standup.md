---
description: Studio standup — report current milestone, sprint tasks, risks, bugs, and build health
---

Produce a studio standup report:

1. Search memory namespace `studio/production` for the current milestone, sprint snapshot, and open risks.
2. Search `studio/qa` for open bugs by severity and recent sign-offs.
3. Search `studio/engineering` for recent build incidents and open ADR work.
4. Run `npx ruflo task list` and `npx ruflo swarm status` for the live task/agent ledger.
5. Report: milestone + exit-criteria status, what moved since the last snapshot, top risks, blocker bugs, build health. Flag anything that needs a `game-director`, `executive-producer`, or `technical-director` decision.
