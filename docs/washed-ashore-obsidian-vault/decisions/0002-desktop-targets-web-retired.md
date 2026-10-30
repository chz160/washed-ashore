---
type: decision
status: accepted
tech: [build-pipeline]
area: []
issues: [14]
prs: [20]
source: "Issue #14 body; PR #20"
decided_by: "Noah"
created: 2026-10-30
updated: 2026-10-30
supersedes: ""
superseded_by: ""
---
# 0002 Desktop targets only; web build retired

_The game targets Windows, Linux and macOS. The web (WebGL) version and the Mobile quality level are removed, and macOS ships as an unsigned Mono test build for now._

## Context
The project started with a web build. Its constraints (WebGL2 sampler limits, GLES3 shader targets, a Mobile quality level) shaped code and tests. An earlier carry-over line already read "NOAH: Windows only unless he asks for web" (`tower-restart-rulings.md` section 1a).

## Decision
Issue #14:

> Drop the web (WebGL) version. From now on the game targets **Windows, Linux and macOS** only (Noah's decision, 14 Oct 2026).

> **Remove the Mobile quality level** (Noah's decision, 2026-10-14). [...] **PC becomes the only quality level and the default on every platform.**

> **macOS: an unsigned Mono build for now** (Noah's decision, 2026-10-14). The only Mac on the team belongs to the writer, so there's no IL2CPP, signing or notarisation yet.

It also says: "Web criteria and the 'breaking the web build' automatic FAIL in older specs no longer bind."

## Consequences
- Good, because shader checks now cover D3D, Vulkan, GL Core and Metal. A skipped target never counts as compiled (PR #20).
- Good, because there are batchmode builds for all three platforms (`BellsBendPlatformBuild.cs`, Mono, universal unsigned macOS) and `-StageOnly` release scripts.
- Bad, because the Linux and Mac builds have not been run on Linux or Mac machines, and the Windows FPS check was not done (PR #20, "Not yet done"). The Mono backend choice is in `adr-scripting-backend.md`.
- Hosted web content (Cloudflare Pages, R2 bucket) was Noah's to take down. Whether that is done is not recorded here.
- The retirement note is `_bmad-output/poc/web-retired.md`. The older web POC files are kept as history.

## Related
[[line-endings-and-content-hashes]]
