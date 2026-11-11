---
# The game
title: "Washed Ashore"
genre: "open-world first-person survival RPG (multiplayer, permadeath)"
pitch: "A quarter century after the collapse, you wash ashore with nothing. Open-world, multiplayer survival RPG with permadeath, realistic scarcity, and a world that remembers."   # README.md
pillars:                  # README.md "Gameplay Pillars", short form
  - "Scarcity and risk from the first minute"
  - "Skills grow through repetition; progression reduces inexperience, not raw power"
  - "Grounded world, no supernatural; every space is reachable and meaningful"
  - "Death is meaningful (a new character, a fresh start)"
  - "Solo and online share one survival loop and one world"
milestone: pre-production # Studio Head, 2026-11-11
milestone_exit: []        # not yet written; executive-producer task

# Tech
engine: unity
engine_version: "6000.6.4f1"   # ProjectSettings/ProjectVersion.txt
language: csharp
platforms: [windows, linux, macos]   # README.md; macOS is an unsigned test build; web retired (#14)
multiplayer: server-authoritative    # docs/physics-guidelines.md: planned, not built (#13), Netcode for GameObjects

# Knowledge base, kept by technical-writer
kb:
  path: docs/washed-ashore-obsidian-vault/
  format: obsidian
  rules_files: [docs/washed-ashore-obsidian-vault/CLAUDE.md]
  human_only: [docs/washed-ashore-obsidian-vault/team-notes/, docs/washed-ashore-obsidian-vault/Welcome.md]
state:
  # The vault rules: knowledge goes in the vault, work to do and risks go in GitHub issues,
  # and agent working state (once Ruflo memory) becomes plain files outside the vault.
  decisions: docs/washed-ashore-obsidian-vault/decisions/
  features: docs/washed-ashore-obsidian-vault/features/
  log: docs/washed-ashore-obsidian-vault/log.md
  production: github-issues  # milestone and risks live as issues; the writer drafts, a person posts
  handoff: .studio/handoff.md
  board: .studio/board.md
contracts: docs/contracts/   # none yet; created by technical-director when the first one is needed

# Tracker
tracker:
  kind: github
  labels: { tech: "tech:", area: "area:" }
  conventions: "One issue per pod or task; branch <slug>-<issue>; PR title ends with (#issue). Specs and POC history live in _bmad-output/ (link, never copy)."

# Roles and pods
roles:
  optional_on: [network-engineer]   # Studio Head, 2026-11-11
  editor_owner: technical-artist     # Studio Head, 2026-11-11
ownership:
  systems-designer: [docs/design/]
  level-designer: [Assets/Scenes/]   # changes applied by the editor owner
  narrative-designer: [docs/narrative/]
  gameplay-engineer: [Assets/Scripts/, Assets/Tests/]
  network-engineer: [Assets/Scripts/Net/, Assets/Tests/]
  platform-tools-engineer: [Assets/Editor/, tools/, .gitattributes, .gitignore]
  build-engineer: [Assets/Editor/Build/, scripts/, release-windows.ps1, release-windows.sh, release-linux.ps1, release-linux.sh, release-macos.ps1, release-mac.sh]
  technical-artist: [Assets/Art/, Assets/World/, Assets/Settings/, Assets/Editor/Art/, Assets/Editor/Wildlife/, tools/blender/]
  art-director: [docs/art/]
  audio-designer: [Assets/Audio/]
  qa-lead: [Assets/Tests/, TestResults/]
  data-analyst: []
  community-manager: []
pods: {}

# Quality gate
gate_check_command: ""   # Unity tests are too slow for a hook; QA records the test evidence
---

# Notes for every agent
- **Read the repo's `CLAUDE.md`.** It's binding: build size and the Addressables trigger (raise it before opening the northern region), model facing (never compensate in code), line endings, and hashing with `git hash-object`.
- **Motion and physics:** follow `docs/physics-guidelines.md` for anything that moves, collides or runs physics queries. Every spec states its motion model.
- **Multiplayer:** server-authoritative is planned, not built (#13). Write code to be replayable on a fixed tick, and don't add networking without a technical-director ADR.
- **Specs and POC history:** `_bmad-output/planning-artifacts/` and `_bmad-output/poc/`. Link to them; never copy them.
- **Not ours to edit:** `Library/`, `Logs/`, `UserSettings/`, `Builds/`, `vendor/`, `Assets/ThirdParty/`, `_bmad-output/` (people's planning history), and the vault's `team-notes/`.
- The Studio Head works on the current branch. Never commit, push or open PRs unless a task says so.
