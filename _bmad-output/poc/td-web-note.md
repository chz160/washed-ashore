---
title: 'TD note: shipping Washed Ashore to the web'
owner: technical-director
inputs: [web-qa-report.md, friction-log.md (WF1-WF76), PlayerController.cs, ProjectSettings, QualitySettings, Settings/*RPAsset]
date: '2026-10-05'
note: 'Written to disk by the lead session on web-td''s behalf (the technical-director agent has no Write tool). Content is web-td''s, verbatim.'
---

# Recommendation: WebGL2 now. Web is a secondary, gated target. The dual-target pipeline is sustainable with 4 changes

The spike passed (1 assist). The final build is 20.2 MB, has 0 console errors and runs at a vsync-capped 59.9 FPS at 1934x1085 with no grass or tree cuts. Windows stayed unchanged. These numbers come from one small scene on an RTX 4060 Ti in Chrome. They prove the pipeline works, not that a full game fits the budgets.

## WebGL2 vs WebGPU
Ship WebGL2. Revisit WebGPU only when a feature needs compute.
- WebGL2 met every budget with headroom, it runs on every desktop browser we target, and Unity's WebGL2 path is the mature one. In Unity, WebGPU is still an opt-in graphics API, and browser support is uneven outside Chromium. Taking it on now adds a second shader and driver matrix and buys nothing we measured.
- WebGL2's real limit is no compute shaders. That rules out VFX Graph, GPU-driven foliage and instancing, and compute-based water or deformation on the web.
- Rule: while web is a target, every rendering feature must have a WebGL2 path or be platform-gated. If design needs a compute-only feature, spike WebGPU then, with WebGL2 kept as the fallback API.

## Risks of shipping to the web
- Size (High): 20.2 MB for one scene, 12.7 MB of which is data. The real game is 10-100x the content, so a single .data file won't scale. Needs an Addressables remote-content layout with a first-load budget (proposal: 50 MB or less to first gameplay).
- Memory (High): not measured in the spike. The wasm heap starts at 32 MB and grows, and browsers often cap well below the wasm32 limit. 116k detail instances plus 735 trees on one terrain is the hot spot. Measure peak heap before production.
- Min-spec performance (High): the FPS was vsync-capped on a discrete GPU at DPR 1. Integrated-GPU laptops and DPR 2 screens (4x the pixels) were not tested. Pick a min-spec machine and re-measure. Render scale 1.0 costs about 1.56x the fill of the old 0.8.
- Shader variants (Med): URP rewrites prefilter keys on every web build, and stripped post-process shaders warn on load. Variant count drives data size and first-frame hitches. Add an explicit stripping config and a warm-up collection.
- Pointer lock and browsers (Med): the browser owns Esc, so the game can't bind it and any future pause menu needs another key. Chrome can reject a re-lock attempt made immediately after an Esc exit. Agents can't automate Esc (WF55), so release and recapture stay manual QA. Only Chrome was tested. Edge and Firefox, the spec's next targets, are untested, as is Firefox's mouse-delta scaling.
- Build time and toolchain (Med): web builds take 603 s cold and 204 s incremental, against 37.5 s for Windows. UnityLinker crashed non-deterministically (WF44), and provenance recorded exitCode 0 on a failed build. CI must gate on the process exit code plus the "Build Finished" line, retry once, then do a clean Library/Bee build.
- Decompression and hosting (Med): the JS Brotli fallback works on plain http but costs main-thread startup time (not measured). For production, serve native Content-Encoding: br over HTTPS and turn the fallback off; keep it for local and dev. There's no threading (webGLThreadsSupport 0, which needs COOP/COEP headers), so everything runs on one thread.
- Audio autoplay (Med): the POC has no audio. Browsers suspend audio until a user gesture. The click-to-play click provides one, so nothing may play before it. Confirm that any chosen audio middleware supports WebGL before we commit to it.
- Mobile: out of scope, and should stay out. It would need touch input, no pointer lock, and tight iOS memory limits. Mobile_RPAsset is only a name: it's the web quality level, not a mobile commitment.
- Branding (Low): productName washed-ashore is the tab title. Fix it in the bootstrap script.

## What to cut first if budgets fail
1. Frame time, in this order, only through level-0 (web-only) overrides:
   1. grass detail density and distance;
   2. tree distance, billboard start and max mesh trees;
   3. shadow distance and cascades;
   4. HDR off on Mobile_RPAsset;
   5. render scale below 1.0, last and with TD sign-off.
2. Size:
   1. per-texture WebGL max 512 for distant and detail textures, and lower crunch quality;
   2. strip unused URP and post-processing variants;
   3. raise managed stripping (currently default) and set release exception support to none;
   4. then Addressables remote bundles.
3. Memory: cut detail density first, then terrain resolution, then stream audio.
4. Never cut: the Windows look, the platform-gated input seam, or the one-line click-to-play.

## Is the dual-target pipeline sustainable? Yes, with conditions
The architecture is right. It's one project, with the differences held in per-platform importer overrides, a web-only quality level and one tested platform seam in code (no #if). Most of the ~50 min critical-path loss was one-offs: ~26 min module download and ~11 min agent handoff. The recurring cost is web build time. Conditions:
1. Nightly CI builds both targets in a separate CI clone. The gates are exit code, Build/ byte budget, the EditMode and PlayMode suites, and the linker retry policy.
2. Make web overrides automatic. An AssetPostprocessor rule should apply the WebGL texture override on import. Today it was a one-time script, so every new texture would regress the size budget.
3. Replace the byte-identical AF1 with a Standalone-reachability guard: fail if a Standalone-included quality level, RP asset or Standalone/Default importer block changes, plus the Windows behaviour tests. Six mid-run amendments showed the byte rule fights engine side effects.
4. Platform code standard: platform differences live behind small pure static gates with EditMode coverage of each platform branch. Promote the cursor lock to an ICursorLock seam so the click and lock-state wiring is unit-tested too.

Whether to ship Washed Ashore on the web is a product call for game-director and executive-producer. Technically it's viable on WebGL2 within these budgets. The risks still open are memory, min-spec FPS, and browsers other than Chrome.
