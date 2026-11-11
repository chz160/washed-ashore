export const meta = {
  name: 'studio-sweep',
  description: 'Run one check across the codebase and return verified findings for the studio lead to turn into tasks',
  whenToUse: 'The same check repeated across many files, e.g. /studio-sweep "physics queries without a layer mask" or "hard-coded player-facing strings". Read-only.',
  phases: [
    { title: 'Scope', detail: 'split the files the check applies to into at most 4 groups' },
    { title: 'Sweep', detail: 'one read-only checker per group' },
    { title: 'Verify', detail: 'one skeptic per group tries to refute each finding' },
  ],
}

// Read-only fan-out. It never edits files. The lead turns confirmed findings into tasks.
const check = typeof args === 'string' ? args : args && args.check
if (!check) throw new Error('Pass the check, e.g. /studio-sweep "physics queries without a layer mask"')

const MAX_GROUPS = 4
const FINDINGS = {
  type: 'object',
  properties: {
    findings: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          file: { type: 'string' },
          line: { type: 'number' },
          evidence: { type: 'string', description: 'the exact offending code or text' },
          why: { type: 'string' },
          severity: { type: 'string', enum: ['high', 'medium', 'low'] },
        },
        required: ['file', 'line', 'evidence', 'why', 'severity'],
      },
    },
  },
  required: ['findings'],
}
const VERDICTS = {
  type: 'object',
  properties: {
    verdicts: {
      type: 'array',
      items: {
        type: 'object',
        properties: { real: { type: 'boolean' }, reason: { type: 'string' } },
        required: ['real', 'reason'],
      },
    },
  },
  required: ['verdicts'],
}

phase('Scope')
const scope = await agent(
  `You are scoping a read-only codebase sweep. The check: "${check}".
If .studio/project.md exists, read it for the engine and the ownership paths of each role.
Find the files this check applies to, and split them into at most ${MAX_GROUPS} non-overlapping groups of roughly equal size (folders or globs).
Exclude generated, cached and vendor content (for example Library/, Temp/, Logs/, node_modules/, vendor/, Builds/).
For each group, name the role that owns those paths, if the profile says.`,
  {
    schema: {
      type: 'object',
      properties: {
        groups: {
          type: 'array',
          items: {
            type: 'object',
            properties: {
              name: { type: 'string' },
              paths: { type: 'array', items: { type: 'string' } },
              owner_role: { type: 'string' },
            },
            required: ['name', 'paths'],
          },
        },
        excluded: { type: 'array', items: { type: 'string' } },
      },
      required: ['groups'],
    },
  },
)
const groups = scope.groups.slice(0, MAX_GROUPS)
if (scope.groups.length > MAX_GROUPS) log(`Scope returned ${scope.groups.length} groups; only the first ${MAX_GROUPS} are swept.`)

const results = await pipeline(
  groups,
  (g) =>
    agent(
      `Read-only. Apply this check: "${check}" to these paths: ${g.paths.join(', ')}.
Report only concrete violations of the check, each with its file, line and the exact code or text as evidence. Nothing outside the check. Return an empty list if there are none.`,
      { phase: 'Sweep', label: `sweep:${g.name}`, schema: FINDINGS },
    ),
  (found, g) => {
    if (!found || !found.findings.length) return []
    return agent(
      `Read-only. Your job is to refute each finding below: open the file and check whether the evidence is really there, and does it really break the check "${check}" given the surrounding code (for example, is it test code, already handled, or a false match)? Answer real=false when in doubt. Return one verdict for each finding, in the same order.
${JSON.stringify(found.findings, null, 2)}`,
      { phase: 'Verify', label: `verify:${g.name}`, schema: VERDICTS },
    ).then((v) =>
      found.findings.map((f, i) => ({ ...f, group: g.name, owner_role: g.owner_role || '', verdict: v && v.verdicts[i] })),
    )
  },
)

const all = results.filter(Boolean).flat()
const confirmed = all.filter((f) => f.verdict && f.verdict.real)
return {
  check,
  swept: groups.map((g) => `${g.name}: ${g.paths.join(', ')}`),
  excluded: scope.excluded || [],
  confirmed,
  rejected: all.length - confirmed.length,
  next: 'Lead: group the confirmed findings by owner_role and create one task per owner, with Gates: tests qa.',
}
