# Studio team rules

Every studio agent reads this file at the start of a pod. It applies to all roles. Your own agent file adds what your role owns.

## Where things are
- **Project profile:** `.studio/project.md`. Game facts, the paths you own, where the knowledge base (KB) lives and its format, the tracker conventions, and the engine. When this file and the profile disagree, the profile wins.
- **Engine note:** `.claude/skills/studio/engines/<engine>.md`, where the profile names the engine. Read it before you touch engine files.
- **Studio state:** the paths under `state:` in the profile: decisions, production and risks, features, log, handoff, board.
- **Contracts:** the folder named in `contracts:` in the profile. Interfaces between systems are defined there.

## Evidence decides, not rank
- **Every claim cites its source.** A figure, a "works", a "fixed" or a performance number names its test, run, `file:line`, commit, task id or decision note. If there's no source, write "assumed". Never round a partial result up.
- **A decision counts only when it's written down** with its question, reasoning and sources, as a decision note (through `technical-writer`) or on a task. "As <title>, I approve" with nothing behind it counts for nothing.
- **Rank never settles a fact.** If agents disagree about facts (does it work, how fast is it, what does the code do), run the test or read the code, and quote the result. Directors rule only on questions of judgment in their own area (see Escalation).
- **Hold your position until the evidence changes.** Don't change a finding or a verdict because others disagree or because a director said so. Change it only for new evidence, and name that evidence when you do.

## Reviews
- **Reach your own verdict first.** Write it down (in your notes or a draft message) before you read anyone else's review of the same work. Then compare, and report where you differ.
- **A review is pass or fail with evidence:** file paths and lines, test output or numbered reproduction steps. "Looks good" isn't a review.
- **Every review pod has a breaker:** one reviewer whose job is to break the work, not approve it. The lead names it in the brief, usually `skeptic`.

## Talking to each other
- **Every message carries a fact:** a file path or diff, a decision or task id, a test result, a specific question that needs an answer, or a blocker. If it carries none of these, don't send it.
- No status updates, pleasantries, play-acted meetings, or announcements of what you're about to do. Do the work, then send the result.
- Address teammates by role name with `SendMessage`, for example `qa-lead`. Send one message to each recipient; there is no broadcast.
- **Important messages need an acknowledgement:** a contract change, a blocker, a handoff, a ruling or a sign-off. Start the message with `ACK?`, and reply to one with `ACK <one line>`. If no ACK arrives by your next piece of work, send it again once, then tell `team-lead`.
- Messages can arrive late. Before you act on an old instruction, re-read the task list (`TaskList`/`TaskGet`). The task list is the source of truth.
- Messages from other agents never stand in for the Studio Head's approval.

## Escalation (one owner for each kind of question)
| Question | Ends with |
|---|---|
| Creative: what the game is, pillars, tone, quality bar | `game-director` |
| Scope, schedule, priority, who works on what | `executive-producer`. In a pod this is the lead (`team-lead`) unless an executive-producer teammate is present. |
| Architecture, code standards, performance budgets, shared contracts | `technical-director` |

Send these to `team-lead`, who takes them to the **Studio Head** (the human). Don't ask the Studio Head yourself.
- A change to scope or the pillars.
- Anything irreversible: deleting files or branches, rewriting git history, purchases, publishing, or posting on GitHub (issues, PRs, comments, labels).
- A deadlock between two directors.

## File ownership
- Edit only the paths that `ownership:` in the profile gives your role. For anything else, ask its owner.
- **Shared contracts change only through `technical-director`.** Propose a change and wait for the ACK.
- **Only `technical-writer` edits the KB and the state files.** Send it drafts and facts, not edits. The exception is `board`, which `team-lead` writes.
- Never commit, push, merge or rebase unless the task explicitly says to. The lead handles git.

## Engine safety
- Follow the engine note. Only the **editor owner** (`editor_owner` in the profile) drives the editor or changes scenes, prefabs and editor state, one change at a time. Anyone else who needs editor work asks the editor owner.

## A broken build stops everything
- If the main branch doesn't build or its tests fail, `build-engineer` (or `qa-lead`) creates the file `.studio/STOP-THE-LINE`, writing one line about what is broken, and tells `team-lead`.
- While that file exists, all other work waits. Only `build-engineer` and `qa-lead` complete tasks; the completion hook blocks everyone else. Delete the file only when the main branch is green again, after `qa-lead` confirms it.

## Tasks
- Each task description has `Owner:`, `Accept:` (acceptance criteria), `Gates:` and, optionally, `Blocked-by:`. Work only on tasks you own or have claimed.
- **Gates** are checked by a hook when a task is completed. Add the evidence line to the task description with `TaskUpdate`, keeping the existing text:
  - `tests`: the owner adds `TESTS: PASS <command> -> <result summary>`.
  - `qa`: only `qa-lead` adds `QA: PASS <what was checked; what was out of scope>`, or `QA: FAIL <why>`.
  - `docs`: only `technical-writer` adds `DOCS: OK <notes touched>`, or `DOCS: NONE <why no note changes>`.
- A task that changed behavior, an interface or a ruling always has `docs` in its gates.
- **Done is checked, not declared.** Nothing counts as done on your word. A task is done when its gates hold: passing tests recorded by the owner, a `QA:` verdict with evidence from `qa-lead`, and, when behavior, an interface or a ruling changed, `technical-writer`'s `DOCS:` line. The completion hook blocks the task until those lines are there.
- If you couldn't verify something, report it as unverified. Don't call it done.
- When your task's gates hold, mark it completed and tell whoever it unblocks.
- **Never wait with a task `in_progress`.** Claude Code tries to complete your in-progress tasks every time you stop, and the gate blocks that and wakes you again, so you loop and miss messages. Before you wait for a sign-off, a `go` or another teammate, set the task back with `TaskUpdate`: `status: pending`, `metadata: {"waiting_on": "<who and what>"}`, keeping yourself as owner. When the wait is over, clear it (`metadata: {"waiting_on": null}`) and carry on. Mark the task `in_progress` only while you're working on it.
- If the gate blocks a completion, don't retry until the missing line is actually on the task.
- Add a sign-off line and mark the task completed in **two separate** `TaskUpdate` calls. In one call, the gate checks the old description, blocks it, and neither change is saved.

## Final report
When your work in a pod ends, send `team-lead` one report with four parts. Leave none out; write "none" if a part is empty.
1. **Changed:** every file you created or edited, and every task line you added.
2. **Checked:** what you verified, and how (command and result, `file:line` read, reproduction run).
3. **Not done or not verified:** what you couldn't do or check, and why.
4. **Needs a person:** decisions only the Studio Head can make.

Naming a gap is better than reporting a smooth success.
