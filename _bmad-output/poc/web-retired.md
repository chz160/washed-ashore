# Web build retired 2026-10-14

Noah decided on 2026-10-14 to drop the web (WebGL) build (issue #14): "going forward we'll just need to build for Windows, Linux, and Apple." The Mobile quality level went with it; PC is the only quality level.

- Targets: Windows, Linux, macOS (macOS is an unsigned test build for now).
- Removed: WebGL code paths, `Assets/WebGLTemplates/`, the WebGL build profile, `deploy-web.ps1`/`deploy-web.sh`, WebGL import overrides in our own assets, and the web shader-compile target.
- Hosting (Cloudflare Pages project, R2 bucket `washed-ashore-game-data`) is taken down by Noah, not by the repo.
- History only, not rewritten: `poc-spec-washed-ashore-web-build.md`, `web-qa-report.md`, `web-acceptance-checklist.md`, `td-web-note.md`, `poc-logs/*web*`, and web mentions in older specs and reports.
- The web criteria and the "breaking the web build = automatic FAIL" rules in those records no longer bind.
