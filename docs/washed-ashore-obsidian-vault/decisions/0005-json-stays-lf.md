---
type: decision
status: accepted
tech: [build-pipeline]
area: []
issues: [75, 62]
prs: [77]
source: "Issue #75 body; issue #62 body (the split ruling it amends); PR #77"
decided_by: "Noah"
created: 2026-10-30
updated: 2026-10-30
supersedes: ""
superseded_by: ""
---
# 0005 JSON stays LF everywhere

_JSON files are LF on disk on every platform, Windows included. Stored hashes of JSON then match everywhere without per-hash normalising._

## Context
#62 split line endings by who writes a file (Noah's ruling, 2026-10-25). Unity-written files got `eol=lf`; human-edited files got `text=auto`, which put `*.json` on CRLF on Windows. In this repo JSON is data that code writes and reads back, and several readers store a SHA-256 of the raw bytes. The fish brief hash read `02319b3dbf5cb70c` on Windows and `9affab9a88cf1337` for the LF blob (#75).

## Options considered
- Normalise line endings inside each hash check.
- Make JSON LF everywhere.

## Decision
Issue #75:

> Ruling (Noah, 2026-10-26): make JSON LF everywhere instead of normalising each hash.

## Consequences
- Good, because `*.json`, `*.jsonl` and `*.geojson` are `eol=lf` in `.gitattributes`, and the fish and terrain hash checks pass on every platform (PR #77).
- Good, because the path-specific JSON lines from #62 are gone.
- Bad, because a checkout from before PR #77 keeps CRLF JSON on disk until it is re-checked out, and the fish hash checks fail until then.
- Any stored hash taken from a CRLF copy had to be re-baselined: `FishTuning.asset` `briefSha16` and `FishRenderSet.asset` `footprintsSource`.
- How to work with it: [[line-endings-and-content-hashes]].

## Related
#76 (stale `StructureAnchors.asset` `sourceSha16`)
