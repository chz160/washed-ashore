# Game studio pod: design (Phase 1)

## 1. Step 0: what the docs say today (Claude Code 2.1.322, docs read 2026-11-10)

All of your constraints hold: **one team per session**, **teammates can't spawn teammates**, **the lead is fixed**, **`/resume` doesn't restore in-process teammates**, **start with 3–5 teammates** (a recommendation, not a limit; the studio has no cap, at the Studio Head's call on 2026-11-11), and **`skills` frontmatter is not applied to teammates**. Teammates do load `CLAUDE.md`, MCP servers and skills from settings. The docs say nothing about message timing to a busy agent. I'll measure it in the smoke test.

Corrections and new facts that change the design:
1. **The task-list tools are off on your models.** `TaskCreate/TaskUpdate/TaskList` are on by default only up to Opus 4.7 and Sonnet 4.6. On Opus or Sonnet 5.5 you need `CLAUDE_CODE_ENABLE_TODO_TOOLS=1`. In-process teammates get them when the lead's session has them. Without the tools, `TaskCompleted` never fires.
2. **Turning teams on changes behavior everywhere.** With teams on, any subagent Claude names becomes a teammate. So I enable teams **per game repo**, never in user settings.
3. **Hook inputs.** `TaskCompleted` and `TaskCreated` receive `task_id`, `task_subject`, `task_description` and `teammate_name`. **They don't receive the owner or metadata.** Exit code 2 blocks the action and feeds stderr back to the model. `TeammateIdle` receives `teammate_name` and `team_name`; exit code 2 keeps the teammate working.
4. **Workflows.** `agent()` takes `agentType` (any of our roles) but **no name**. Whether a workflow agent can use `SendMessage` is undocumented, and we don't need it: workflows return results to the lead. Saved workflows live in `.claude/workflows/*.js` (project) or `~/.claude/workflows/` (user) and run as `/name`.
5. **Split panes don't work in Windows Terminal or VS Code.** Use in-process mode (the default).
6. **Plugin agents are namespaced** (`studio:game-director`), and plugin skills run as `/studio:studio`. That would break your role names and `/studio init`.

## 2. Old agents → new definitions

**Every role keeps:** what it owns, what it never does, its escalation rules, and its model tier. **Every role loses:** `mcp__claude-flow__*` tools, memory keys, swarm tools, `skills:` (they load from settings), Unity paths, and Washed Ashore references. Each memory read becomes "read the knowledge-base note"; each memory write becomes "send a draft to technical-writer" or "write to your owned directory".

| Old | New | Change and why |
|---|---|---|
| executive-producer | **lead playbook** + agent | The main session follows its rules through the skill, because convening pods is the lead's job. The agent file stays as a teammate for planning pods. Task tools become the shared task list; risk and sprint notes become knowledge-base notes. `Task` is dropped. |
| game-director, technical-director | same | `Task` spawning is dropped: they ask the lead for people instead. The technical director now also owns **shared contracts**. |
| systems-, level-, narrative-designer | same | Spec paths come from the profile. The level designer's "exclusive scene owner via EP" becomes the engine-note editor lock. |
| gameplay-, platform-tools-, build-engineer | same | Unity CLI steps move to `engines/unity.md`. Build-engineer keeps stop-the-line. |
| art-director, technical-artist, audio-designer | same | Unity steps move to the engine note. |
| qa-lead | same | The bug database moves from memory to the issue tracker. Sign-off becomes a line on the task (see §4). |
| data-analyst, community-manager | same, **optional** | Turned on per game in the profile. Community-manager stays draft-only. |
| technical-writer | **rewritten** | Vault path, labels and `_bmad-output` are replaced by the profile. It takes over Ruflo memory: decisions, production and risk notes, feature status, log and handoff. Markdown is the default; the Obsidian rules apply only when `kb.format: obsidian`. Plan, then wait for a go. It is the docs gate. |

**Nothing is merged or dropped.** Gaps:
- **Netcode and server authority.** Added as a new **optional `network-engineer`** (approved by the Studio Head on 2026-11-11), turned on when `multiplayer` isn't `none`. It owns replication, authority, prediction and transport, with architecture under technical-director.
- **Smaller gaps** (no new roles proposed): UX/UI design, split today between art-director and gameplay-engineer; accessibility; and localization QA.

## 3. Chosen design: the thinnest one that works

Everything lives in this repo's `.claude/` folder for now (the Studio Head's call), so it is versioned with the game. Moving it to user level later means copying the same files to `~/.claude/`.
- **17 plain agent files** in `.claude/agents/`, each game-agnostic and engine-agnostic. The shared rules live once, in `skills/studio/team-rules.md`, and every agent reads them.
- **One skill, `.claude/skills/studio/`.**
  - `SKILL.md` branches on `init | status | handoff | <goal>`.
  - Alongside it: `playbook.md`, `team-rules.md`, `profile-template.md`, `engines/unity.md` and `kb-formats/{markdown,obsidian}.md`.
  - Teammates can't get skills from frontmatter, so **every brief tells the teammate to read these files**.
- **One hook script, `.claude/hooks/studio-gate.js`.** It is Node, has no dependencies and runs in exec form through `${CLAUDE_PROJECT_DIR}`, so it works on every OS. It does nothing without `.studio/project.md`, and nothing for tasks that have no `Owner:` line.
- **`.claude/settings.json`** (committed): the two env flags, the Obsidian plugin and the two hooks.
- **Two workflows** in `.claude/workflows/`.

**Rejected:**
- **A plugin, user-level install or an install script.** Not needed while it lives in this repo. A plugin would also namespace the role names and `/studio`.
- **Inbox files in the fallback.** Named subagents already have `SendMessage`, so inbox files would duplicate it.
- **A `TaskCreated` hook.** The lead writes every task from a template, so the check isn't needed.
- **Running the test suite inside the hook.** Unity tests take minutes and clash with an open Editor. The hook checks recorded evidence instead, and a profile may name a fast `gate.check_command`.
- **A long-lived executive-producer teammate.** It costs tokens and duplicates the lead.

## 4. Profile (`.studio/project.md`) and quality gates

The profile is YAML front matter followed by free-text notes:
```yaml
title: ""; genre: ""; pitch: ""; pillars: []; milestone: pre-production   # one of the 8-step ladder
engine: unity; engine_version: ""; language: csharp; platforms: []; multiplayer: none | p2p | server-authoritative
kb: { path: docs/, format: markdown | obsidian, rules_files: [], human_only: [] }
state: { decisions, production, features, log, handoff, board }   # paths; may sit outside the KB
contracts: docs/contracts/
tracker: { kind: github, labels: { tech: "tech:", area: "area:" } }
roles: { optional_on: [], editor_owner: technical-artist | build-engineer }
ownership: { gameplay-engineer: [Assets/Scripts/], technical-artist: [Assets/Art/, Assets/Shaders/] }
pods: { feature: [...] }          # overrides the defaults
gate_check_command: ""
```
Studio state lives in the `state:` paths. Each one may sit in the knowledge base or in `.studio/`, for example when a vault forbids new top-level folders. The lead also keeps `.studio/board.md`, a snapshot of the task list, because the list itself is scoped to one session.

**Task format.** The lead writes the description with these lines: `Owner:`, `Accept:`, `Gates: tests qa docs` (any subset), and `Blocked-by:`.
- **Sign-off.** QA, the writer and the test runner sign off by appending lines with `TaskUpdate`: `TESTS: PASS <cmd> <result>`, `QA: PASS <scope>` and `DOCS: OK <notes>` or `DOCS: NONE <why>`. A `.studio/STOP-THE-LINE` file blocks every completion except build-engineer's and qa-lead's.
- **`TaskCompleted`** blocks the task (exit code 2 with the reason) if any listed gate has no matching line.
- **`TeammateIdle`** reads the session's task files. If the teammate owns an open, unblocked task, it nudges it, at most twice in a row.

## 5. Location

Everything is in `E:\GitHub\washed-ashore\.claude\` (`agents/`, `skills/studio/`, `hooks/`, `workflows/`, `settings.json`), plus `.studio/project.md`. Nothing in the studio files names this game; the profile carries all of it. A second game needs only a copy of `.claude/` and `/studio init`.

## 6. Walkthrough: `/studio review the networking section of the architecture doc`

1. The lead reads `.studio/project.md`, `handoff.md`, the latest decisions, the risks and `contracts/`. It confirms the team and task tools are on; if they're off, it runs the fallback (named background subagents plus `board.md`).
2. It picks the **review** pod: technical-director, qa-lead, a `skeptic` (technical-director type, told to find holes) and technical-writer. It spawns each one by name with its agent type and a brief: goal, profile path, engine note, owned paths, who to message, and the rules.
3. It creates the tasks:
   - T1: technical-director, review. `Gates: qa`
   - T2: skeptic, holes. `Gates: qa`
   - T3: qa-lead, check the testability of the claims. Blocked by T1 and T2.
   - T4: technical-writer, close-out. Blocked by T3. `Gates: docs`
4. Teammates claim their tasks and message each other by role. Important messages need an acknowledgement; if none comes, the lead re-sends or escalates. A director deadlock, scope question or irreversible step goes to you.
5. The writer plans its notes and the lead approves them. The writer writes the review note, any `proposed` decision, and the log and handoff, then adds `DOCS: OK`. The gate lets T4 close.
6. The lead shuts the team down and reports the outcome, the files changed and what needs you.

## 7. Housekeeping found

No name clashes. There are no Ruflo or claude-flow files in this repo; `_bmad-output/brainstorming/.../.memlog.md` only mentions Ruflo. `C:\Tools\ruflo` is out of scope (the Studio Head's call). The vault's `CLAUDE.md` still names "ruflo memory" as the home for working state. It is a person's file, so it's left for the Studio Head.
