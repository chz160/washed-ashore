---
type: pattern
status: active
tech: [build-pipeline]
area: []
issues: [62, 75, 61, 73, 74]
prs: [71, 72, 77]
source: "Issue #62 (Noah's ruling 2026-10-25); issue #75 (Noah's ruling 2026-10-26); PR #71, #72, #77"
created: 2026-10-30
updated: 2026-10-30
aliases: [gitattributes, CRLF, git hash-object, byte-identical rebuild]
---
# Line endings and content hashes

_Store LF in the repo. Files Unity writes are LF on disk everywhere; files people edit follow the platform. JSON is data, so it is LF everywhere. Approve files by their git blob hash, not the bytes on disk. Generators must give byte-identical output on every run, or hash checks mean nothing._

## Problem
- Unity rewrote `ProjectSettings.asset` and other YAML with LF. With `core.autocrlf true`, git then showed them as changed with no content diff (seen after a Windows build in #40).
- `git add` printed CRLF warnings on every commit.
- Hashes taken on Windows didn't match the same file on Mac or Linux. Example: `FishTuning.asset` `briefSha16` was `02319b3dbf5cb70c` (CRLF copy), while the LF blob hashes to `9affab9a88cf1337` (#75).
- Tests and tools dirtied the tree. An EditMode test rewrote a tracked report (#61), and editor tools wrote reports into `_bmad-output/` (#74).

## When it applies
Any new file type, generator, editor tool that writes files, or check that stores a hash.

## What to do
- **`.gitattributes` (PR #71, amended by PR #77):**
  - `* text=auto` catch-all.
  - Human-edited types (`.cs`, `.md` and similar) use `text=auto`: CRLF on Windows, LF on Mac and Linux.
  - Unity-written types (`.unity`, `.prefab`, `.asset`, `.meta` and others) use `eol=lf`.
  - `*.json`, `*.jsonl` and `*.geojson` use `eol=lf` ([[0005-json-stays-lf]]).
  - `.sh` is lf; `.bat` and `.cmd` are crlf. LFS lines stay below the text rules.
- **Hash the blob:** `git hash-object <file>`, not a hash of the bytes on disk. A `text=auto` file can be CRLF on disk and LF in git.
- **Writers append `'\n'`,** not `AppendLine`, which writes CRLF on Windows (PR #71). The terrain writers write `\n` (PR #77).
- **Tests write nothing tracked.** Assert on returned data. Reports go under `TestResults/`, which git ignores (PR #77).
- **Builders are deterministic** (PR #72):
  - Sort anything that comes back in no fixed order. `LoadAllAssetsAtPath` returns clips in no fixed order.
  - Don't delete and recreate assets: that gives new random fileIDs. Build to a scratch path, compare a full description, and keep the old file if equal.
- **After an attribute change, re-check out.** Existing checkouts keep old endings until then. The steps are in PR #77's "After you pull". Close the Unity Editor first.

## Evidence
- 1589 `eol=lf` files are LF on disk, and 494 `.cs`/`.md` files are CRLF on disk on Windows. No CRLF warnings on `git add`. The rebuild-twice check gave byte-identical prefab and scene (PR #71).
- Three character builder runs in a row gave byte-identical output across all 23 assets (PR #72).

## Does not apply when
Binary assets under LFS. Their `-text` rules win.

## Related
- [[0005-json-stays-lf]]
- #76: `StructureAnchors.asset` `sourceSha16` has been stale since the 10-14 terrain change (PR #77 follow-up).
