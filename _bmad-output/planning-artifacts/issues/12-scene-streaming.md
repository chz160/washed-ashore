Investigate scene streaming for the open world (dynamic, chunk and additive loading, and Addressables), and recommend an approach and when to do it. **This is an investigation only:** no restructuring.

## What we know (checked 2026-10-14)

- **One scene:** `World.unity` is the only scene in the build (about 3.0 MB of YAML). It holds 20 terrain tiles of 1024 m, all vegetation, water, wildlife, birds, fish and the lookout tower.
- **No streaming code:** no `LoadSceneAsync`, no additive loading, and Addressables isn't installed.
- **The Addressables deferral** (`_bmad-output/planning-artifacts/deferred-addressables-content-streaming.md`, decided 2026-10-06) triggers when the north region opens (`MapConfig.gateOpen`, north of `northLineZ` 1986.7) **or** when web `Web.data` passes 100 MB. **The web trigger lapses with #14,** so this issue must re-base it. `MapConfig.cs:26-27` points to the note.
- **Build size:** Windows `Builds/Windows-BellsBend` is about **539 MB**.
- **Generator tools that rebuild `World.unity` content:** `BellsBendLevel` (menu *Washed Ashore/Level/Build Bells Bend (all)*), `BellsBendTiles`, and the `tools/terrain` pipeline. `WorldBuilder` is retired.
- **Prior thinking:** `bells-bend-td-note.md` §2 already sketches additive scenes per tile, `Tile_{ix}_{iz}.unity` with a 3×3 ring and hysteresis. Fish already stream logically in 32 m cells; that's not scene streaming.
- **Multiplayer:** the direction is a persistent, limited-population server world (#13).

## Pass criteria (what makes this investigation complete)

| # | Criterion |
|---|---|
| Q1 | **Baseline numbers** on the Windows build: cold load time to first controllable frame, peak and steady memory (managed, native and GPU), and build size by category |
| Q2 | **An options table:** additive scene per tile, per region, terrain-only streaming, Addressables for content, and hybrids. For each: build cost, runtime cost, the changes needed in `BellsBendLevel`, `BellsBendTiles` and `tools/terrain`, and the risk to the existing tests |
| Q3 | **The chunk unit,** recommended with reasons: the 1024 m tile, a region, or the north boundary. It should match the `bells-bend-td-note` §2 sketch or say why not |
| Q4 | **A spike:** one tile split into an additive scene and loaded or unloaded at runtime by script, with the load hitch (ms) and the memory change measured. Done on a throwaway branch and not merged |
| Q5 | **Multiplayer fit:** what a server or host must keep loaded (the whole world or streamed), and how that interacts with #13 |
| Q6 | **New triggers** for the deferral, now that the web size trigger has lapsed (for example memory, load time, Windows build size, or the gate opening). Update the deferral note's frontmatter, and write the recommendation, timing and a rough work estimate to `_bmad-output/poc/streaming-note.md` |

**Gate:** technical-director approves the note, and Noah picks the approach and timing.

## Automatic FAIL

- Restructuring `World.unity` on main.
- Recommending an approach without the baseline and spike numbers.

## Pod

**technical-director** (lead), **platform-tools-engineer** (spike and baseline) and **level-designer** (generator impact): 3 agents.

Related: #13, #14, #15 and #16 (populations should follow the chunk unit).
