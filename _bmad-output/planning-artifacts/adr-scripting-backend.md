---
title: 'ADR: scripting backend for Windows and Linux players'
status: proposed (Noah to confirm)
date: 2026-10-17
issue: '#14'
author: systems-designer (pod14)
---

# ADR: scripting backend for Windows and Linux

## Decision (proposed)

Ship **Windows and Linux on Mono**, for now. macOS is already decided: an unsigned Mono build (Noah, 2026-10-14).
*Noah to confirm.*

## Context

- Windows is on Mono today. ProjectSettings `scriptingBackend` has no Standalone entry, so Standalone uses the
  default, Mono. Every POC build, FPS baseline and test run so far used Mono.
- Linux IL2CPP from a Windows machine needs Unity's Linux IL2CPP toolchain package, on top of Linux Build Support.
  Mono needs only the module.
- Our CPU-side work is small: wildlife and fish AI, a perf harness, no networking yet. Nobody has yet measured whether
  scripts or rendering limit the frame time. The profiling in #9-#11 will show it.

## Options

| | Mono | IL2CPP |
|---|---|---|
| Build from Windows | Win, Linux, macOS with modules only | Win: + IL2CPP module and VS C++ (neither installed). Linux: + toolchain package. macOS: needs a Mac |
| Build time | short | several times longer (C++ compile) |
| CPU speed | baseline | faster on script-heavy code |
| Matches current baselines | yes | no, FPS baselines would need re-measuring |
| Reverse-engineering | easy (managed DLLs) | harder |

## Consequences

- One backend on all three platforms, so FPS figures can be compared across them, and the current Windows baselines
  (#14 R4 ±5%) stay valid.
- Nothing extra has to be installed beyond the Linux and Mac Build Support modules.
- **Revisit** if a measured CPU bottleneck in scripts appears (the #9-#11 profiling, or multiplayer under #13), or
  before a store release, where IL2CPP's protection and speed may matter. Moving later means installs (Noah), a settings
  change, and re-measuring baselines. Code changes are unlikely; check for reflection the linker strips.

## Build input

From build-engineer (pod14 'build'), 2026-10-17:

- All three platforms build as Mono today. The only `scriptingBackend` entry is `Android: 1`.
- **IL2CPP isn't available on this machine even for Windows.** The Windows player has no IL2CPP variant installed,
  and it would also need the Visual Studio C++ toolchain. Linux IL2CPP also needs Unity's Linux toolchain package.
  None of these may be installed without Noah. So IL2CPP would be an install decision, not just a settings change.
- Build time: an incremental Windows Mono batch build took about 25 s (538 MB player). Build estimates that IL2CPP
  would add many minutes, since the project uses Burst/Jobs. This is an estimate, not a measurement.
- macOS: the architecture is set to universal (x64+arm64) by reflection on the Mac module's
  `UserBuildSettings.architecture`. Whether 6000.6 allows that can only be confirmed once the module is installed.
  The provenance json records `macos_architecture`.
- Build's recommendation matches this ADR: Mono on all three. Revisit IL2CPP only for a measured need (Windows FPS)
  or alongside the later macOS signing/notarisation issue.
