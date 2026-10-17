---
description: Spin up a AAA-studio feature pod (designer → engineer → QA under a producer) for a feature
argument-hint: <feature description>
---

Run the studio feature-pod pattern from CLAUDE.md ("Feature pod pattern") for: $ARGUMENTS

1. First consult `game-director` (spawn it) for a greenlight check against the creative pillars — skip only if the user already made the call.
2. Decide pod composition: always `systems-designer` + `gameplay-engineer` + `qa-lead` + `executive-producer`; add `level-designer` (scene/level work), `technical-artist` (shader/VFX/asset work), `audio-designer` (sound), `narrative-designer` (text) when the feature touches their craft.
3. Spawn the whole pod in ONE message, every agent `run_in_background: true` with a `name`, chained via SendMessage exactly as CLAUDE.md shows.
4. Relay the producer's final report, including QA's verdict, to the user.
