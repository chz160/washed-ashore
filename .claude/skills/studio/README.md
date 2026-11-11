# Game studio pod

A team of game-studio agents that run on Claude Code's agent teams. You're the **Studio Head**. The main Claude session is the **team lead** and does the executive producer's job: it convenes a **pod** of role agents for a goal, runs a shared task board, and closes out with a knowledge-base update and a handoff.

It uses only Claude Code features: agent definitions, one skill, one hook script, two saved workflows and project settings. It needs no API key and no extra services.

## What's where

All the studio files live in this repo's `.claude/` folder, so they're versioned with the game. The only game-specific file is the profile, `.studio/project.md`.

| Path | What it is |
|---|---|
| `.claude/agents/*.md` | 17 role agents: game-director, executive-producer, technical-director, systems-designer, level-designer, narrative-designer, gameplay-engineer, network-engineer (optional), platform-tools-engineer, build-engineer, art-director, technical-artist, audio-designer, qa-lead, technical-writer, data-analyst (optional), community-manager (optional) |
| `.claude/skills/studio/SKILL.md` | The `/studio` command |
| `.claude/skills/studio/playbook.md` | How the lead runs a goal: pods, task format, briefs, close-out |
| `.claude/skills/studio/team-rules.md` | Rules every agent follows: messaging, escalation, file ownership, gates, waiting |
| `.claude/skills/studio/profile-template.md` | The format of `.studio/project.md` |
| `.claude/skills/studio/engines/unity.md` | Engine rules (add `godot.md` or `unreal.md` for other engines) |
| `.claude/skills/studio/kb-formats/` | Knowledge-base rules: `markdown.md` (default) and `obsidian.md` |
| `.claude/hooks/studio-gate.js` | Quality gates on task completion, and the idle nudge |
| `.claude/workflows/studio-sweep.js`, `studio-docs-audit.js` | `/studio-sweep <check>` and `/studio-docs-audit` |
| `.claude/settings.json` | Turns on agent teams and task tools, registers the hooks |
| `.studio/project.md` | This game's profile |
| `.studio/board.md`, `.studio/handoff.md` | The board snapshot (kept by the lead) and the session handoff (kept by technical-writer) |
| `.studio/gate.log` | Optional. If the file exists, the hook appends one line per hook call. Delete it to stop tracing. |

## Requirements

- Claude Code with agent teams. The project settings set `CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1` and `CLAUDE_CODE_ENABLE_TODO_TOOLS=1`. The second one is needed on Opus 5.5 and Sonnet 5.5, where the task tools are off by default.
- Node.js on `PATH`, for the hook.
- `gh`, signed in, if the profile's tracker is GitHub. Agents only read from it.
- In-process teammates, which is the default. Split panes don't work in Windows Terminal or VS Code.

## Set up a new game

1. Copy this repo's `.claude/agents/`, `.claude/skills/studio/`, `.claude/hooks/studio-gate.js` and `.claude/workflows/studio-*.js` into the new repo's `.claude/`. Don't copy `.studio/`.
2. Start Claude Code in the new repo and run `/studio init`. It:
   - Lists any Ruflo or claude-flow leftovers and asks what to do. It never deletes them.
   - Reads the README, design docs, engine files and tracker labels, and drafts the profile.
   - Asks you, in at most two rounds, only what it couldn't find.
   - Writes `.studio/project.md`, and shows the settings change before merging it into `.claude/settings.json`.
3. **Restart Claude Code.** Settings and new agent files load only at startup. Before a restart, spawning a role fails with "Agent type '…' not found".
4. Approve the project hooks if Claude Code asks. Without them, the gates don't run.

The engine must have a note in `engines/`. For an engine without one, copy `unity.md` and adapt it before the first pod.

## Start a session

```
/studio <goal in plain words>
```

For example: `/studio review the multiplayer rules in docs/physics-guidelines.md against #13`.

The lead then works through these steps:
1. Loads the profile, the handoff, the board and recent decisions.
2. Checks that teams and task tools are on.
3. Announces the pod in one line.
4. Creates the tasks and spawns every member with a brief.
5. Runs the pod.
6. Has technical-writer send a plan, gives it a `go`, and lets it write the notes and the handoff.
7. Shuts the team down.
8. Reports to you, starting with the outcome.

Other commands:
- `/studio status`: read-only. Shows stop-the-line, what needs you, blockers, the board, risks and the milestone.
- `/studio handoff`: writes the handoff now.
- `/studio-sweep <check>`: a read-only sweep of the codebase for one check, with every finding verified by a skeptic.
- `/studio-docs-audit`: a read-only check of the knowledge base against the code and the tracker.

`/studio` runs only when you type it. Claude won't start a pod on its own.

## How a pod works

- **Roles are jobs, not characters.** Each agent file says what the role owns, takes in and produces, what done means, and how the work is checked. Directors rule on questions of judgment in their own area: game-director on creative, executive-producer on scope and schedule, technical-director on architecture. Designers and engineers propose and build.
- **Evidence decides, not rank.** Every claim cites a test, run, `file:line`, commit or task, or says "assumed". A ruling counts only once it's written down with its reasoning. A factual disagreement is settled by running the test or reading the code. Reviewers write their own verdict before reading others', and a `skeptic` tries to break the work. The full rules are in `team-rules.md`.
- **The board is the source of truth.** Each task has one owner and a description with `Owner:`, `Goal:`, `Accept:`, `Gates:`, `Blocked-by:` and `Inputs:` lines.
- **Gates.** When a task with an `Owner:` line is completed, the hook checks that every gate listed has its sign-off line on the task:
  - `tests`: `TESTS: PASS …`, added by the owner.
  - `qa`: `QA: PASS …`, added only by qa-lead.
  - `docs`: `DOCS: OK …` or `DOCS: NONE …`, added only by technical-writer.

  A missing line blocks the completion and tells the agent what's missing.
- **Stop-the-line.** If the main branch is broken, build-engineer creates `.studio/STOP-THE-LINE`. Until it's deleted, only build-engineer and qa-lead can complete tasks.
- **Waiting.** A teammate that waits on someone sets its task to `pending`, with `waiting_on` metadata. If it waits with a task `in_progress`, it loops: Claude Code retries the completion every time the teammate stops, and the gate blocks it each time. The rule is in `team-rules.md`, and every block message repeats it.
- **Messages** go teammate to teammate by name. Important ones start with `ACK?` and need an `ACK`. A message reaches a teammate only between its tool calls, so a busy teammate reads it late.
- **Knowledge base.** Only technical-writer edits it. It plans first and waits for `go`. Decisions are numbered and never rewritten, only superseded. It never edits notes a person wrote or folders listed in `kb.human_only`. Every figure is sourced or marked "assumed".

## Add a role

1. Write `.claude/agents/<role>.md`. Copy a similar agent and keep the same structure:
   - Frontmatter with `name`, `description` (what the role does, not a title), `model` and `color`, plus `disallowedTools` if the role must not edit files.
   - One line of scope ("You handle X for this project") and the "Before any work" line.
   - Sections: **Owns**, **Takes in**, **Produces** (exact files, formats and task lines), **Done means**, **How it's checked**, **Rules for this role** and **Final report**.

   Don't give it a personality or backstory. Keep it free of game names; game facts go in the profile.
2. Add its owned paths under `ownership:` in each game's profile.
3. If the role is optional, list it under `roles.optional_on` in the profiles that use it.
4. Add it to the escalation table in `team-rules.md` if it rules on anything.
5. Restart Claude Code.

## Add or change a pod

- **For every game:** edit the pod table in `playbook.md`.
- **For one game:** add a `pods:` entry to that game's profile. It overrides the default:
  ```yaml
  pods:
    feature: [systems-designer, gameplay-engineer, technical-artist, qa-lead]
  ```
- **Size:** there's no cap on pod size. The lead picks the roles the goal needs, and says why when a pod has more than 5. technical-writer always joins for the close-out. Each teammate costs tokens.

## Step in during a pod

- **Talk to the lead** in the main session. The lead relays to teammates.
- **Talk to a teammate directly:** in in-process mode, press Shift+Down to cycle through teammates, then type.
- **Stop everything:** tell the lead "stop the pod". It asks each teammate to shut down.
- **Stop the line:** create `.studio/STOP-THE-LINE` with one line about why.
- **What reaches you:** the lead asks you only about scope or pillar changes, irreversible actions (deleting, rewriting history, purchases, publishing, posting on GitHub) and deadlocks between directors. Everything else is settled inside the pod.
- **Decisions** are written as `status: proposed`. Accepting one is your call, made outside the pod.

## Resume work

`/resume` doesn't bring teammates back. The studio's memory is in files instead:
- `.studio/handoff.md`: what was done, open tasks, decisions, risks, what needs you, and the suggested next pod.
- `.studio/board.md`: the last board snapshot. Claude Code clears the task list once every task is complete, so the board is the record.
- The knowledge base: decisions, features and the log.

To pick up, start a session and run `/studio status`, then `/studio <next goal>`. The lead reads the handoff and the board first.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| "Agent type 'technical-director' not found" | Agents added during a session aren't loaded. Restart Claude Code. |
| The lead says teams or task tools are off | The settings aren't applied yet. Restart, or check `.claude/settings.json` `env`. |
| A teammate keeps reporting the same block | It's waiting with a task `in_progress`. Point it to the waiting rule in `team-rules.md`. A run of blocks in `.studio/gate.log` confirms it. |
| A sign-off line didn't save | The line and `completed` were sent in one update. Send them as two updates. |
| The gate never fires | The task has no `Owner:` line, there's no `.studio/project.md`, or the hooks weren't approved. |
| A message seems lost | The teammate is busy; it reads messages between tool calls. Send it again once, then ask the lead. |

## What the first smoke test showed (2026-11-11)

A review pod (technical-director, a skeptic, network-engineer, qa-lead and technical-writer) reviewed the multiplayer rules without changing code. It took about 6 minutes from spawn to close-out. It used about 95k output tokens and 15M cache-read tokens across the five teammates.
- qa-lead sent work back twice before signing off.
- The gate blocked completions without sign-offs.
- The writer planned, waited for `go`, and wrote five proposed decisions, a feature update, a log entry and the handoff.

It also found the waiting loop, the one-update sign-off quirk, and a hook bug that read the wrong task folder. All three are fixed, and a writer-only retest passed.
