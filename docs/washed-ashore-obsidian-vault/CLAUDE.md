# Washed Ashore vault: rules for agents and people

This vault holds the project's long-lived, human-readable knowledge: technical lessons, rulings, feature status and notes about the game world. Read it in Obsidian. It is committed with the game repo; changes go through git like code.

## Where things live

| Kind of thing | Home | The vault does |
|---|---|---|
| Work to do, bugs, work state | GitHub issues | links by number in `issues:` |
| Code, tests, specs the build needs | the repo (`Assets/`, `docs/`) | links, never copies |
| Planning and POC history | `_bmad-output/` | links, never copies |
| Standing engineering rules | `docs/physics-guidelines.md` | links; a pattern that becomes a rule moves there |
| Agent working state (hashes, rulings in progress, raw figures) | ruflo memory (`studio:*` namespaces) | cites the key in `source:` |
| Finished knowledge a person would read in six months | **this vault** | owns it |

## Folders

| Folder | Holds | Who writes |
|---|---|---|
| `patterns/` | One technical lesson per technique (stairs and ramps, collider geometry, probe tests...) | technical-writer agent |
| `decisions/` | One ruling per note, numbered `NNNN-short-name.md` | technical-writer agent |
| `features/` | One status note per feature: shipped, open, rulings | technical-writer agent |
| `world/` | Places, creatures, the player: design notes on things in the game | people and the technical-writer agent |
| `team-notes/` | Notes by the human developers | people only; agents read, never edit |
| `_meta/` | `templates/`, `bases/` (index views), `glossary.md` | technical-writer agent, with people's OK |
| `attachments/` | Images and other files | anyone |

No new top-level folder without the project owner's OK. No subfolders except under `world/` and `_meta/`.

## Syntax

- Link notes with `[[wikilinks]]`. Link the first mention of a note, not every mention.
- Preserve YAML properties, wikilinks, embeds, block references and callouts exactly.
- File names: lowercase kebab-case, no spaces (`stairs-and-ramps.md`). Names must be unique across the vault.
- Dates: `YYYY-MM-DD`.
- Every new note starts from its template in `_meta/templates/`.

## Properties (frontmatter)

Every note has these. Lists stay lists even with one value.

```yaml
type: pattern | decision | feature | world | log
status: (see below)
tech: []        # values of the GitHub tech: labels, without the prefix
area: []        # values of the GitHub area: labels, without the prefix
issues: []      # issue numbers, e.g. [80, 86]
prs: []
source: ""      # where the facts come from: PR, commit, or ruflo key
created: YYYY-MM-DD
updated: YYYY-MM-DD
```

- `status` for patterns and world notes: `active | superseded | archived`.
- `status` for decisions: `proposed | accepted | superseded | contested`.
- `status` for features: `planned | in-progress | shipped | parked`.
- `tech:` and `area:` values must match a GitHub label (`gh label list`). A new value needs a new label first.
- Superseded or archived notes stay where they are (links never break) and set `superseded_by: "[[note]]"` when one replaces them.

## Rules for agents

1. **Only the technical-writer agent edits the vault.** Other pod agents send it drafts.
2. **Never edit `team-notes/`** or any note a person wrote, unless a person asks. Propose changes in your own note or in the pod report.
3. **Search before creating.** Check titles, `aliases` and the Bases views. If a note on the topic exists, update it. One lesson or ruling is one note.
4. **Patterns, features and world notes** are edited in place: keep them short, bump `updated`. Git keeps the history.
5. **Decisions are never rewritten.** A new ruling gets a new note; the old one gets `status: superseded` and `superseded_by`. If two rulings conflict, mark both `contested` and ask the game director or technical director. Do not pick one.
6. **Every figure names its source** (test, run, commit, issue). Mark anything not measured as "assumed". Long evidence stays in the issue or PR; the note links to it.
7. **Open with a two- or three-line summary.** Keep a note under about 150 lines; split it if it grows past that.
8. **Rename or move notes only with the Obsidian CLI** (`obsidian rename`, `obsidian move`), so links update. It needs Obsidian running and always exits 0: read the note back after any CLI write.
9. **Never edit `.obsidian/` by hand.** Settings change in Obsidian.
10. **At each pod's end:** update the notes, add one entry at the top of `log.md`, and list every file changed in the pod report so a person can review the git diff.

## Regular check (read-only, report only)

Dead links, orphan notes, missing properties, unknown `tech:`/`area:` values, duplicate names, and notes whose `issues:` have all closed while `status` still says open. Fixes come as a separate change.
