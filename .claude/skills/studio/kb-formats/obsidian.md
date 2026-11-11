# Knowledge-base format: Obsidian vault

For `technical-writer` when `kb.format: obsidian`. **The vault's own rules file (listed in `kb.rules_files`, usually the vault's `CLAUDE.md`) is binding, and it beats this file.** Studio sessions don't load a vault's `CLAUDE.md` on their own, so read it yourself.

## Skills
Use the Obsidian skills: `obsidian:obsidian-markdown`, `obsidian:obsidian-bases` and `obsidian:obsidian-cli`. Teammates don't get skills from agent frontmatter. They load from settings, so the project's `.claude/settings.json` must enable `obsidian@obsidian-skills`. If the skills are missing, say so in your report and follow the rules below by hand.

## Before any write (every time)
1. Read the vault rules file in full, then `Home.md` and the top of `log.md`.
2. List the existing notes in each vault folder, with their `aliases`. Update an existing note rather than creating a near-duplicate.
3. If the profile lists tracker label prefixes (such as `tech:` and `area:`), run `gh label list`. Property values must match a label exactly.

## Rules (unless the vault rules file says otherwise)
- Start every new note from the vault's templates (usually `_meta/templates/`). Open with a two- or three-line summary, and keep notes under about 150 lines.
- Link the first mention of a related note with a `[[wikilink]]`. Link issues as full URLs in the body; their numbers go in `issues:`.
- Preserve YAML properties, wikilinks, embeds, block references and callouts exactly.
- **Rename or move notes only with the Obsidian CLI** (`obsidian rename`, `obsidian move`), so links update. It needs Obsidian running, and it always exits 0, so read the note back afterwards. If Obsidian isn't running, report the rename instead of doing it.
- Never edit `.obsidian/`. Never create a new top-level vault folder without the Studio Head's OK. State that the vault doesn't allow goes in the `state:` paths outside the vault (for example `.studio/`).
- Never edit notes a person wrote, or the folders listed in `kb.human_only`.
- `_meta/` templates and index views change only with a person's OK.
