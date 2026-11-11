# Knowledge-base format: plain Markdown

The default. For `technical-writer`. Plain `.md` files with standard relative links, readable on GitHub. The paths come from `kb:` and `state:` in the profile.

## Before any write
1. Read every file in `kb.rules_files`.
2. List the existing notes in the `state:` folders. Update a note rather than creating a near-duplicate.
3. If the profile lists tracker label prefixes, get the valid values with `gh label list` (read-only).

## Front matter (every note)
```yaml
type: decision | feature | production | log
status: (see below)
issues: []        # tracker numbers
source: ""        # where the facts come from: PR, commit, test run, task id
created: YYYY-MM-DD
updated: YYYY-MM-DD
```
- `status` for decisions: `proposed | accepted | superseded | contested`. Superseded notes keep their file and add `superseded_by: NNNN-name.md`.
- `status` for features: `planned | in-progress | shipped | parked`.

## Templates
**Decision** (`decisions/NNNN-short-name.md`, the next free number, never rewritten):
```
# NNNN Title
<two-line summary>
## Question
## Ruling      (quote the person or role who ruled, with the date)
## Why
## Alternatives rejected
## Consequences / follow-ups
```
**Feature** (`features/<name>.md`): a summary, then *Shipped*, *Open*, *Rulings* (links) and *Lessons*.
**Production** (`production/milestone.md`, `production/risks.md`): the milestone with its exit criteria, and a risk table with columns *risk, likelihood, impact, mitigation, owner, status*.
**Log** (`log.md`): newest entry first, `## YYYY-MM-DD | <pod> | <goal> | <issues>`, then the notes touched.

## Links
Use relative Markdown links (`[0003](../decisions/0003-x.md)`). Link issues as full URLs.
