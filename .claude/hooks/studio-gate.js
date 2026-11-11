#!/usr/bin/env node
// Studio quality gates for agent teams. No dependencies.
//
//   TaskCompleted: blocks completion (exit 2) unless
//     - the main branch isn't stopped (.studio/STOP-THE-LINE), or the completer is build-engineer or qa-lead;
//     - the task description has a "Gates:" line, and every listed gate has its sign-off line:
//         tests -> "TESTS: PASS ..."   qa -> "QA: PASS ..."   docs -> "DOCS: OK ..." or "DOCS: NONE ..."
//     - gate_check_command from the profile (if set) passes, for tasks with the tests gate.
//   TeammateIdle: if the teammate still owns open, unblocked tasks, sends it back to work (exit 2),
//     at most MAX_NUDGES times in a row.
//
// Does nothing in a repo without .studio/project.md, or for tasks without an "Owner:" line (not studio tasks).

const fs = require('fs');
const os = require('os');
const path = require('path');
const { execSync } = require('child_process');

const MAX_NUDGES = 2;
const STOP_EXEMPT = ['build-engineer', 'qa-lead'];
const SIGNOFF = {
  tests: { re: /^TESTS:\s*PASS\b/im, need: 'TESTS: PASS <command> -> <result>, added by the task owner' },
  qa: { re: /^QA:\s*PASS\b/im, need: 'QA: PASS <scope>, added by qa-lead' },
  docs: { re: /^DOCS:\s*(OK|NONE)\b/im, need: 'DOCS: OK <notes> or DOCS: NONE <why>, added by technical-writer' },
};

// Claude Code also tries to complete a teammate's in-progress tasks each time it stops. A block then wakes it
// again, so a teammate that waits with a task in progress loops (seen in the first smoke test). Every block
// says how to wait without looping.
const HOW_TO_WAIT = 'If you are waiting on someone, stop retrying: TaskUpdate this task to status "pending" with metadata {"waiting_on": "<who and what>"}, keep yourself as owner, then wait. Set it back to in_progress, with waiting_on null, when you resume.';

function block(message) {
  process.stderr.write(message + '\n' + HOW_TO_WAIT + '\n');
  process.exit(2);
}

const input = JSON.parse(fs.readFileSync(0, 'utf8') || '{}');
const root = process.env.CLAUDE_PROJECT_DIR || input.cwd || process.cwd();
const profilePath = path.join(root, '.studio', 'project.md');
if (!fs.existsSync(profilePath)) process.exit(0);
const stopLine = path.join(root, '.studio', 'STOP-THE-LINE');

// Opt-in trace: create .studio/gate.log and every hook call is appended to it.
const traceFile = path.join(root, '.studio', 'gate.log');
const trace = (verdict) => {
  if (!fs.existsSync(traceFile)) return;
  const { hook_event_name, task_id, task_subject, teammate_name, team_name, session_id } = input;
  fs.appendFileSync(traceFile, JSON.stringify({ at: new Date().toISOString(), hook_event_name, task_id, task_subject, teammate_name, team_name, session_id, verdict }) + '\n');
};
const _exit = process.exit;
process.exit = (code) => { trace(code === 2 ? 'blocked' : 'allowed'); _exit(code); };

if (input.hook_event_name === 'TaskCompleted') taskCompleted();
else if (input.hook_event_name === 'TeammateIdle') teammateIdle();
process.exit(0);

function taskCompleted() {
  const who = input.teammate_name || 'team-lead';
  const desc = input.task_description || '';
  const what = `Task "${input.task_subject}" can't be completed yet.`;
  if (!/^Owner:/im.test(desc)) return; // not a studio task (studio tasks always have an Owner: line)

  if (fs.existsSync(stopLine) && !STOP_EXEMPT.includes(who)) {
    const why = fs.readFileSync(stopLine, 'utf8').trim();
    block(`${what} STOP-THE-LINE: the main branch is broken (${why}). Only build-engineer and qa-lead complete tasks until it is green. Keep your task in progress and help if asked.`);
  }

  const gatesLine = desc.match(/^Gates:\s*(.*)$/im);
  if (!gatesLine) {
    block(`${what} Its description has no "Gates:" line. team-lead: add one with TaskUpdate (tests, qa, docs, or none).`);
  }
  const gates = gatesLine[1].toLowerCase().split(/[\s,]+/).filter((g) => SIGNOFF[g]);

  const missing = gates.filter((g) => !SIGNOFF[g].re.test(desc)).map((g) => `- ${g}: needs "${SIGNOFF[g].need}"`);
  if (missing.length) {
    block(`${what} Missing sign-off lines in its description:\n${missing.join('\n')}\nAsk the role named, or add your own line with TaskUpdate (keep the existing text).`);
  }

  if (gates.includes('tests')) {
    const cmd = (fs.readFileSync(profilePath, 'utf8').match(/^gate_check_command:\s*"?([^"\n]*)"?/m) || [])[1];
    if (cmd && cmd.trim()) {
      try {
        execSync(cmd, { cwd: root, stdio: 'pipe', timeout: 120000 });
      } catch (e) {
        const out = String((e.stdout || '') + (e.stderr || '')).slice(-1500);
        block(`${what} gate_check_command failed: ${cmd}\n${out}`);
      }
    }
  }
}

function teammateIdle() {
  const name = input.teammate_name;
  const team = input.team_name;
  if (!name || !team) return;

  const counterFile = path.join(os.tmpdir(), `studio-idle-${team}-${name}.txt`);
  const open = openTasksOwnedBy(team, name);
  if (!open.length) {
    fs.rmSync(counterFile, { force: true });
    return;
  }
  const nudges = Number(fs.existsSync(counterFile) ? fs.readFileSync(counterFile, 'utf8') : 0);
  if (nudges >= MAX_NUDGES) return; // let it idle; the lead sees it in the task list
  fs.writeFileSync(counterFile, String(nudges + 1));

  const list = open.map((t) => `- #${t.id} ${t.subject} (${t.status})`).join('\n');
  block(`You still own open tasks:\n${list}\nCarry on with them. If you're blocked, tell team-lead who or what you're waiting for.`);
}

// Task files live in ~/.claude/tasks/<folder>/<id>.json. In the smoke test they were in the folder named after
// the full session id, while a folder named after the team existed but stayed empty. So use the first of
// (session id, team name) that holds task files, else the most recently changed folder that does.
function taskDir(team) {
  const base = path.join(os.homedir(), '.claude', 'tasks');
  if (!fs.existsSync(base)) return null;
  const hasTasks = (d) => fs.existsSync(d) && fs.statSync(d).isDirectory() &&
    fs.readdirSync(d).some((f) => f.endsWith('.json'));
  for (const name of [input.session_id, team]) {
    if (name && hasTasks(path.join(base, name))) return path.join(base, name);
  }
  const dirs = fs.readdirSync(base).map((d) => path.join(base, d)).filter(hasTasks)
    .sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
  return dirs[0] || null;
}

// The task file format isn't documented, so read the files defensively.
function openTasksOwnedBy(team, name) {
  const dir = taskDir(team);
  if (!dir) return [];
  const tasks = [];
  for (const file of fs.readdirSync(dir)) {
    if (!file.endsWith('.json')) continue;
    try {
      const t = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
      tasks.push({ ...t, id: t.id ?? path.basename(file, '.json') });
    } catch { /* skip unreadable or partial files */ }
  }
  const done = new Set(tasks.filter((t) => t.status === 'completed').map((t) => String(t.id)));
  return tasks.filter((t) =>
    t.owner === name &&
    t.status !== 'completed' && t.status !== 'deleted' &&
    !(t.metadata && t.metadata.waiting_on) && // the teammate said it's waiting on someone
    (t.blockedBy || []).every((id) => done.has(String(id))));
}
