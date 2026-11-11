export const meta = {
  name: 'studio-docs-audit',
  description: 'Compare the studio knowledge base with the code and with itself, and return verified mismatches for technical-writer',
  whenToUse: 'Before a milestone review, or when the notes feel stale. Read-only: it reports and never edits.',
  phases: [
    { title: 'Map', detail: 'read the profile and list the knowledge-base notes' },
    { title: 'Audit', detail: 'three lenses: notes vs code, notes vs notes, status vs tracker' },
    { title: 'Verify', detail: 'one skeptic per lens tries to refute each mismatch' },
  ],
}

const MISMATCHES = {
  type: 'object',
  properties: {
    mismatches: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          note: { type: 'string', description: 'path of the note' },
          claim: { type: 'string', description: 'what the note says, quoted' },
          actual: { type: 'string', description: 'what the code, other note or tracker shows, with its path, line or issue' },
          kind: { type: 'string', enum: ['code', 'dead-link', 'orphan', 'duplicate', 'property', 'conflict', 'stale-status', 'unsourced-figure'] },
          suggested_fix: { type: 'string' },
        },
        required: ['note', 'claim', 'actual', 'kind'],
      },
    },
  },
  required: ['mismatches'],
}

phase('Map')
const map = await agent(
  `Read .studio/project.md. Return the knowledge-base path, its format, the rules files, the human-only folders, the state paths, and a list of every note (relative paths) in the knowledge base and the state paths. Read-only.`,
  {
    schema: {
      type: 'object',
      properties: {
        kb_path: { type: 'string' },
        format: { type: 'string' },
        rules_files: { type: 'array', items: { type: 'string' } },
        human_only: { type: 'array', items: { type: 'string' } },
        notes: { type: 'array', items: { type: 'string' } },
      },
      required: ['kb_path', 'notes'],
    },
  },
)
log(`${map.notes.length} notes in ${map.kb_path} (${map.format || 'markdown'})`)
const context = `Knowledge base: ${map.kb_path} (format ${map.format || 'markdown'}). Rules files: ${(map.rules_files || []).join(', ') || 'none'}. Notes:\n${map.notes.join('\n')}`

const LENSES = [
  {
    key: 'code',
    prompt: `Check every note's claims about the code and assets: file paths, class, method and field names, settings, layers, numeric figures with a stated source, and "shipped" behavior. Open the code and compare. Report claims that the repo contradicts or no longer supports, and figures that have no source and aren't marked "assumed".`,
  },
  {
    key: 'internal',
    prompt: `Check the notes against each other and against the rules files: dead links, orphan notes (nothing links to them), duplicate names or topics, missing or unknown properties, decisions that conflict while both are "accepted", and superseded notes without a link to what replaced them.`,
  },
  {
    key: 'status',
    prompt: `Check status against reality. Feature, decision and production notes whose status disagrees with the tracker (use read-only "gh issue view <n> --json state,title" for the issues they cite), with the handoff, or with newer notes. Also flag risks and open items older than the latest handoff that it no longer mentions.`,
  },
]

const results = await pipeline(
  LENSES,
  (l) =>
    agent(`Read-only knowledge-base audit, lens "${l.key}". Never edit anything.\n${context}\n\n${l.prompt}\nQuote each claim exactly, and give the evidence for what's actually true.`, {
      phase: 'Audit',
      label: `audit:${l.key}`,
      schema: MISMATCHES,
    }),
  (found, l) => {
    if (!found || !found.mismatches.length) return []
    return agent(
      `Read-only. Your job is to refute each reported mismatch below: open the note and the evidence and check it. Answer real=false when the note is in fact right, or when the evidence is weak. Return one verdict for each item, in order.\n${JSON.stringify(found.mismatches, null, 2)}`,
      {
        phase: 'Verify',
        label: `verify:${l.key}`,
        schema: {
          type: 'object',
          properties: {
            verdicts: {
              type: 'array',
              items: { type: 'object', properties: { real: { type: 'boolean' }, reason: { type: 'string' } }, required: ['real', 'reason'] },
            },
          },
          required: ['verdicts'],
        },
      },
    ).then((v) => found.mismatches.map((m, i) => ({ ...m, lens: l.key, verdict: v && v.verdicts[i] })))
  },
)

const all = results.filter(Boolean).flat()
const confirmed = all.filter((m) => m.verdict && m.verdict.real)
return {
  kb: map.kb_path,
  notes_checked: map.notes.length,
  confirmed,
  rejected: all.length - confirmed.length,
  next: 'Lead: send this list to technical-writer. It plans the fixes, gets a go, and never edits human-only notes.',
}
