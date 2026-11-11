# Lead playbook

How the lead, doing the executive producer's job, runs a goal from `/studio <goal>` to done.

## Pods
**There is no size cap.** A pod has the roles the goal needs, and `technical-writer` always joins for the close-out. Start from the default pod, and add or drop roles to fit the goal. Each teammate costs tokens, so don't spawn a role that has no task. If a pod goes past 5, say why in the one-line pod announcement to the Studio Head. Read the overrides under `pods:` in the profile; they beat these defaults. Roles that are off in the profile (`roles.optional_on`) are never spawned.

| Pod | Members | Use for |
|---|---|---|
| feature | systems-designer, gameplay-engineer, technical-artist, qa-lead (+ technical-writer at the close) | Building a feature |
| design | game-director, systems-designer, narrative-designer or level-designer, technical-director (+ writer) | Debating and deciding. Directors rule; designers propose. |
| tech | technical-director, gameplay-engineer, platform-tools-engineer, qa-lead (+ writer) | Technical approach. **One member is named `skeptic`, and its job is to break the proposed approach.** |
| pipeline | build-engineer, technical-artist, art-director, qa-lead (+ writer) | Asset, build or CI pipeline work |
| review | technical-director, qa-lead, technical-writer, plus `skeptic` (agent type technical-director) | Reviewing a doc, design or change. Usually changes no code. **`skeptic`'s job is to break the work, not approve it.** |

- **Reviewers work independently.** In any pod with more than one reviewer, each reviewer's brief says to write its verdict before reading the others'. Don't route one reviewer's findings to another until both have sent their own.

- If the profile's `multiplayer` isn't `none` and the goal touches sync or authority, swap `network-engineer` in for the least relevant engineer.
- If a pod needs someone mid-way (a teammate asks, or a question goes outside the pod's roles), spawn that role then, with a full brief and its own task.
- Two members of the same type need different names: the role name, and a job name such as `skeptic`.

## Run a goal
1. **Load the state.** Read:
   - `.studio/project.md`
   - The engine note
   - `state.handoff`
   - `state.board`
   - The production and risk notes
   - The 10 most recent decision notes, by number
   - The contracts index
   - `.studio/STOP-THE-LINE`. If it exists, the only pod you may run is build-engineer, qa-lead and technical-writer, until the main branch is green.
2. **Check the machinery.**
   - **Teams:** run `echo $CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS` in Bash; it should print `1`.
   - **Task tools:** check that `TaskCreate` is among your tools.
   - **Fallback**, if either is missing: tell the Studio Head and offer to fix the settings (a restart is needed). If they decline, run the pod as named background subagents (Agent with `name` and `run_in_background: true`), which message each other with `SendMessage`. You keep the board in `state.board` and check the gates yourself before you accept "done", because the hook only fires for the task list.
3. **Choose the pod.** Tell the Studio Head in one line: the pod, its members, why, and the expected tasks. If the goal changes scope or the pillars, ask first. Otherwise go ahead.
4. **Create the tasks before you spawn**, so teammates find them on arrival. Use the task format below. Each task has exactly one owner role. **Set each task's owner in the call right after creating it**, before anything else: an idle teammate can claim an unowned task within seconds. The same goes for tasks you add mid-pod.
5. **Spawn every member in one message.** Use the Agent tool with `name` set to the role name (or job name), the agent type set to the role, and the prompt set to the **brief** below. Teammates don't see this conversation; the brief is all they get.
6. **Run the pod.**
   - Answer escalations by following the table in `team-rules.md`. If the disagreement is about facts, don't pick a side: ask the owner to run the test or read the code.
   - **Rule from the files, not from messages.** Before a ruling about code, a test run or a task, read the file, its timestamps, `git diff` or `TaskGet` yourself. Messages cross: a teammate may have changed the file since it wrote. Don't send a second ruling on the same point until the first is acknowledged; if you must change a ruling, label it `FINAL, supersedes <time>`.
   - **You alone edit task descriptions** (moving an Accept line, for example). Do it with `TaskGet` immediately before, and tell the owner. Everyone else uses metadata keys (see `team-rules.md`), so their sign-offs can't be overwritten. Only you set `allow_protected` on a task, and only when its goal is to change a protected path.
   - **The engine lock:** when several roles need the engine, set the order in a message to each of them. A performance run holds the lock too.
   - The hook checks that sign-offs exist; you check that they hold evidence. Send back a `signoff_*` value that cites no command, file or check.
   - Don't relay chatter. Forward only messages that carry a fact (see "Talking to each other" in `team-rules.md`).
   - Re-send a message that wasn't acknowledged, once, and then escalate.
   - Hold questions for the Studio Head until you have several, unless one is blocking. Ask only about scope, pillars, irreversible actions or a deadlock between directors.
   - If any message reports a red main branch, enforce stop-the-line.
   - Keep `state.board` current at each milestone in the pod: a snapshot of `TaskList`. **Claude Code clears the task list once every task is completed**, so the board is the only record afterwards.
   - **Freeze the findings** when the consolidation or verification task starts: tell the reviewers that anything later goes to technical-writer as report text, not as new findings.
   - If a teammate keeps reporting the same wait, check `.studio/gate.log` (if it exists) for a run of blocked completions: that teammate is waiting with a task in progress. Tell it to follow the waiting rule in `team-rules.md`.
7. **Close out.**
   - When the work tasks are done, the writer's close-out task unblocks. It sends you a plan, with the task left `pending` and `waiting_on` set; you check it and reply `go` (or ask for changes). Put changes in the same message as the `go`, so it takes one round.
   - It writes the notes, the log and the handoff, and sets `signoff_docs`.
   - Check every task is completed or explicitly carried over in the handoff. Update `state.board` with the final state **before** the last task completes.
8. **Shut down.** Ask each teammate by name to shut down, and wait until each one stops. Update `state.board`.
9. **Report** to the Studio Head:
   - The outcome, in the first line
   - The files changed
   - Decisions recorded
   - What needs them
   - Rough token use: the sum of each teammate's usage from its idle and completion notices, plus yours, if you can see it

## Task format
- **Subject:** `<verb> <object>`, short.
- **Description:**
```
Owner: <role name as spawned>
Goal: <one or two sentences>
Accept:
- <testable criterion>
- ...
Gates: <any of: tests qa docs>, or none
Blocked-by: <task subjects or ids>, or none
Inputs: <files, notes, issues>
```
- Add `docs` to `Gates:` whenever the task changes behavior, an interface or a ruling.
- Add `qa` to every task that produces work for the game.
- Add `tests` to tasks that change code.
- The writer's close-out task is blocked by all the work tasks and has `Gates: docs`.
- Set dependencies with the task tool's dependency field as well as the `Blocked-by:` line, and set the task's owner field to the same name as `Owner:`. The idle hook reads the owner field.
- The completion hook gates only tasks that have an `Owner:` line, so a task without one is never gated. Studio tasks always have one.

## Brief template
Teammates don't see this conversation; the brief is all they get. Fill every line. A vague brief is the most common reason a pod goes wrong.
```
Name: <name>, agent type <role>. Pod: <pod> for "<title>" (<engine>).
team-lead convened this pod. The Studio Head is the human.

Goal: <goal, verbatim from the Studio Head>
Your job in it: <one line: the question you answer or the thing you build>
Why now: <milestone, handoff or decision>

Read first:
- .claude/skills/studio/team-rules.md
- .studio/project.md
- .claude/skills/studio/engines/<engine>.md
- <writer only: .claude/skills/studio/kb-formats/<format>.md and every file in kb.rules_files>
- <the specific docs, notes, issues and code paths this job needs>

Tasks: <ids and subjects>. Claim with TaskUpdate when you start work. The task list is the source of truth.
Takes in: <exact inputs: files, task ids, whose findings arrive and when>
Produces: <exact outputs: files and format, task metadata keys (signoff_*, review_*, note_*), messages and who receives them>
Done means: <each Accept line of the task>, checked by <qa-lead / reviewer / test command>; the hook needs <gate lines>.
Review mode: <reviewers only: "write your verdict before reading others'"; skeptic only: "your job is to break this, not approve it">
Team:
- <name>: <role>, <its job in this pod>
- ...
May edit: <paths from ownership.<role>, or "nothing; send drafts">
Never: <the never-list, e.g. edit Assets/, post on GitHub, commit>
Final report: the four-part format in team-rules.md.
```
