# Project profile template

`/studio init` writes this to `.studio/project.md` in the game repo. The studio's agents, skill and hooks never name a game; everything game-specific lives here. YAML front matter first, then free-text notes.

```markdown
---
# The game
title: ""
genre: ""
pitch: ""                 # one or two sentences
pillars: []               # 3–5 creative pillars
milestone: concept        # concept | pre-production | vertical-slice | production | alpha | beta | gold | live-ops
milestone_exit: []        # exit criteria for the current milestone

# Tech
engine: unity             # selects .claude/skills/studio/engines/<engine>.md
engine_version: ""
language: csharp
platforms: []             # e.g. [windows, linux, macos]
multiplayer: none         # none | local | p2p | server-authoritative | relay

# Knowledge base, kept by technical-writer
kb:
  path: docs/
  format: markdown        # markdown | obsidian. Selects kb-formats/<format>.md
  rules_files: []         # files the writer must obey, e.g. a vault's CLAUDE.md
  human_only: []          # folders agents read but never edit
state:                    # studio memory, written by technical-writer (the board by the lead)
  decisions: docs/decisions/
  production: docs/production/
  features: docs/features/
  log: docs/log.md
  handoff: .studio/handoff.md
  board: .studio/board.md
contracts: docs/contracts/   # interface contracts; only technical-director changes them

# Tracker
tracker:
  kind: github            # github | none
  labels: {}              # e.g. { tech: "tech:", area: "area:" }, prefixes notes must match
  conventions: ""         # e.g. "one issue per pod; branch <slug>-<issue>"

# Roles and pods
roles:
  optional_on: []         # any of: network-engineer, data-analyst, community-manager
  editor_owner: technical-artist   # the one role that drives the editor
ownership:                # paths each role may edit; anything unlisted, ask its owner
  systems-designer: []
  level-designer: []
  narrative-designer: []
  gameplay-engineer: []
  network-engineer: []
  platform-tools-engineer: []
  build-engineer: []
  technical-artist: []
  art-director: []
  audio-designer: []
  qa-lead: []
  data-analyst: []
  community-manager: []
pods: {}                  # override a default pod, e.g. { feature: [systems-designer, gameplay-engineer, qa-lead] }

# Quality gate
gate_check_command: ""    # optional fast command the completion hook runs for tasks with the tests gate (must finish in under 2 minutes)
protected_paths: [ProjectSettings/, Packages/]   # engine and package settings; the completion hook blocks studio tasks while these are dirty (team-lead can allow it per task with metadata allow_protected)
---

# Notes for every agent
Game-specific rules every pod must know, written short. Link to the docs; don't copy them.
- Engineering rules: <link>
- Import rules: <link>
- Things not to touch: <paths>
```
