# Items 4 and 7: the player in water; swim numbers

Author: w-designer (systems-designer), 2026-10-06. Owner of the merge: w-td.
Build spec that uses these numbers: `_bmad-output/poc/water-swim-spec.md` (r3, approved: `ruling/water-swim-numbers`, `-r2`).

## Source status

I drafted this section without a web tool. **w-web then checked every citation live on 2026-10-06**
(scratchpad `water-web-sources.md`, section "Item 4/7"), and §4a carries w-web's corrections. Sources are numbered [201]–[210];
the source table is at the end of this section. None of the conclusions and none of the §7 numbers changed.

Player-reception statements in §4a are design readings of the games' known reputations, not quotes from these pages.

## 4a. How survival games handle wading and swimming

| Game | Wading | Swimming | Breath / drowning | Boundary over water | Reception | Src |
|---|---|---|---|---|---|---|
| The Long Dark | Falling into water soaks clothing (raises hypothermia/frostbite risk) | **None**: no swimming at all | No drowning. Weak ice cracks on a hidden 5 s timer; falling through means a fade to black, then a safe spot: Warmth 0%, Hypothermia, −10% Condition, all clothing 100% wet | Weak ice "serves as boundary along the ocean" | Accepted: water is a cold hazard, never a route. It shows a survival game can make water a hazard with no swimming | [201] |
| DayZ | Slower with depth | Surface swim; fast swimming costs −5 stamina/s, normal swimming recovers +1/s | Normal swimming can't drown you. Drowning exists only if your head stays under without entering the swim state (e.g. prone in water) for minutes. Clothing and items get soaked | Sea at the map edge leads nowhere | Players swim to escape pursuers; soaked gear is the real cost | [202] |
| Rust | Wetness rises with submersion and lowers body temperature | Surface swim; dive with a tank | Fully submerged at wetness 100: "Drowning", fast health loss and death (status effect) | Ocean beyond the map (edge kill and sharks unverified, M) | Clear but status/HUD-driven | [203] |
| Subnautica | n/a (ocean game) | Core verb | Oxygen used per second rising with depth (1/s shallower than 100 m, 3/s at 100–200 m, 5/s deeper); PDA alerts at 30 and 10; out of air: ~4 s fade plus ~4 s to death. 45 s base is M | **The Void (Crater Edge)**: terrain ends near 4000 m. PDA: "Warning: Entering ecological dead zone. Adding report to databank." One Ghost Leviathan on entry, a second at 40 s, a third at 80 s; they retreat when you return | The standard-bearer for a diegetic soft boundary: you can go, the world warns, then escalates, and backing off ends it | [204] |
| The Forest | Shallows slow you; swimming stows items | Surface swim; can dive (breath meter underwater; a rebreather and tank give 300 s) | Breath shown only underwater | Sharks in deep ocean "where the water is too deep to stand in", visible long before they're a danger; they charge and bite when close | Sharks read naturally as "don't go out there" | [205] |
| Green Hell | Leeches attach while walking through forest or water (−1 sanity each, not fatal) | Swimming; stamina drain is M | 25 s of breath underwater, then ~10 s (at full health) before drowning | Rivers are hazards inside the map (piranhas; the black caiman kills instantly without armour) | Water feels dangerous through fauna and after-effects, not meters | [206] |

Comparators outside the brief (they define the boundary pattern):

| Game | Pattern | Lesson | Src |
|---|---|---|---|
| Red Dead Redemption (2010) | John drowns almost at once above waist depth (stamina vanishes, he sinks); RDR2 deliberately repeats it for John in the epilogue | **Cautionary tale.** Sudden, unexplained drowning reads as arbitrary. Drowning must be staged and legible | [207] |
| Zelda: Breath of the Wild | When the stamina wheel empties while swimming, Link sinks, loses one heart and returns to land | Drowning as a **soft fail with a shore respawn** keeps exploring safe to try | [208] |
| Sea of Thieves | Devil's Shroud at the edge: sea and sky turn red, the hull takes repeated damage, and the ship sinks and respawns. Loading-screen line: "Beyond the edge you may stray, a fateful end if you stay..." | Out-of-bounds made **diegetic and escalating**, not a wall | [209] |
| GTA V | Sharks in deep ocean | Fauna as the boundary for an open sea (M) | [210] |

**Takeaways.**
1. Water is a cost, not a route. Every accepted example makes water expensive: soaking and cold ([201], [203]), stamina ([202]), fauna ([205], [206]).
2. Drowning must be staged and survivable if you turn back ([207] vs [204], [208]). [204]'s leviathans retreat when you return, which is the same turn-back rule our 4b design uses.
3. The boundary warns before it kills, and escalates ([204]: one leviathan, then more at 40 s and 80 s; [209]; [201]'s weak-ice ocean edge). Our backstop stays the hard floor, and drowning is the readable layer on top.
4. Fail forward, on theme: a drowned player **washes ashore** on the nearest bank. That's [208]'s return to land, and [201]'s "fade, then a safe spot", in our premise.
5. No meters. [203] and [204] lean on HUD meters; we can't. [201], [205] and [206] show hazard can read without them.

## 4b. Drowning as a soft world boundary (CONCRETE DESIGN; research only, NOT built)

**Why it matters here.** L3 makes everything outside the polygon water (5 whole tiles; terrain X −2087..2009, Z −2492..2628).
South of the line there is **no far bank**. Over water, drowning is a boundary in every direction, not only at the north line.
Until it is built, the backstop and `WorldBoundsClamp` are the boundary (W8).

**Inputs (already built):** signed shore distance from `BellsBendLevelMaps.ShoreDistance` (2 m grid, ±127 m, negative in water,
measured to the playable-polygon edge); `MapConfig.northLineZ`; `BarrierBuilder.LineOffset` (20); swim speed 1.8 m/s (§7).
The real Cumberland at Bells Bend is roughly 150-250 m wide, so ~75-125 game m after the 1:2 squeeze. Mid-channel is ≤ ~60 m from a bank.

**Model: one hidden pull value P in [0, 1].** There is no bar. P only drives cues, and its inputs are position and time, so it is deterministic and server-checkable.

| Zone | Where | dP/dt |
|---|---|---|
| Safe | ≤ **40 m** out from shore | −1/10 s (full recovery in 10 s) |
| Warning | 40-**70 m** out | +1/40 s |
| Drowning | > 70 m out | +1/8 s |
| North water | in water north of `northLineZ − LineOffset` (past the fence line) | +1/8 s at any distance |
| Grounded (wading or standing) | anywhere | −1/5 s |

**Worked numbers at 1.8 m/s:**
- Straight out from 40 m: P = 0.42 at 70 m (16.7 s); drowns at ~78 m.
- Turn back at 60 m (P 0.28): reach safety with P 0.56. Turn back at 70 m (P 0.42): reach safety with P **0.83**, alive but struggling.
- **Point of no return ≈ 72 m out. Anyone who turns back anywhere in the warning band survives.**
- North water: P fills after 8 s, about 14 m past the fence line and 6 m short of the north line. The clamp remains the authoritative floor (teleports, Rigidbodies, multiplayer).

**Cue timeline (no HUD bars):**

| Stage | Trigger | Cues |
|---|---|---|
| 0 Uneasy | enter the warning band | River sound swells; the character's breathing becomes audible; stroke cadence slows; haze over open water thickens (TA fog) |
| 1 Tiring | P ≥ 0.33 | Ragged breathing; speed ×0.85; water slaps the lens every ~6 s (0.3 s splash, brief low-pass) |
| 2 Struggling | P ≥ 0.66 | Eye above water drops 0.35 → 0.10 m; slaps every ~3 s; speed ×0.7; 20% vignette, 30% desaturation; heartbeat |
| 3 Going under | P ≥ 1 | Input lost; sink over 3 s into murk; muffled audio; black at 5 s |
| Wash ashore | after the fade | Respawn lying on the nearest **in-bounds low-bank station** (L3c stations, south of the line), facing inland. The cost (time, soaked gear, item loss) waits for the death/inventory systems |

**Stamina and breath, later.** Keep P place-based. Don't feed a land stamina system into it, so the boundary stays deterministic.
If diving ever comes in scope: hidden breath 20 s, cues from 10 s (tight-chest audio, darkening), forced surfacing, no damage before 20 s.
The deferred duck-under (≤ 2.0 s, see the swim spec) needs no breath system.

**Later tunables:** `safeDistance` 40, `drownDistance` 70, `warnRate` 1/40, `drownRate` 1/8, `recoverRate` 1/10, `groundRecoverRate` 1/5,
stage thresholds 0.33 / 0.66 / 1.0, `sinkSeconds` 3, `fadeSeconds` 5.
**Later acceptance:** a turn-back sweep at every distance ≤ 70 m never drowns; with the clamp disabled in a test build, 0 swims reach the north line; every stage cue fires in order.

## 7. Recommended swim numbers (BUILT NOW; approved: ruling/water-swim-numbers and -r2)

Baseline, live controller (`PlayerController.cs`, World.unity, `controller-tuning.md`): walk 5, sprint 8 m/s, accel 20, decel 25,
jump 1.2 m, gravity −20; CC height 1.8, radius 0.4, eye 1.65, stepOffset 0.4, slopeLimit 45.
Bank (design brief c, rev 14): lip W + 0.3 with a 0.31 m riser; 18° shelf to the bed at W − 2 (~7.1 m out), so depth = 0.325·s − 0.3.
Depth = water surface − ground under the player. Per w-td's tech pick, the surface is near-flat (vertical-only displacement of a few cm,
no Gerstner horizontal shift) and one C# function returns the exact height and a current vector at any (x, z, t). Swim/wade logic queries
that height; the current vector is not applied to the player (no push, `ruling/water-swim-numbers-r2`).

| Parameter | Value | Why |
|---|---|---|
| Wade starts | depth > 0.05 m | Ignore ripple at the lip |
| Wade speed multiplier (on walk) | 0.9 @ 0.05 m · 0.75 @ 0.3 (shin) · 0.6 @ 0.6 (knee) · 0.5 @ 1.0 (waist) · 0.4 @ 1.35 (chest), linear | Heavy and clearly slower: 4.5 → 2.0 m/s |
| Sprint in water | below 0.3 m only (× curve); sprint-stroke while swimming | You can't sprint chest-deep |
| Jump in water | below 0.6 m only; never while swimming | |
| Wade accel / decel | 8 / 10 m/s² | Land is 20 / 25 |
| **Swim enters / exits** | depth **1.35 / 1.15 m** | Chest depth; 0.2 m hysteresis. Not at the brief's W − 2: at 2 m deep the eyes are 0.35 m under water |
| Eye above water while swimming | **0.35 m** (float depth 1.30) | Camera never clips the surface (near plane 0.1) |
| **Swim speed** | **1.8 m/s** (36% of walk) | Below the deepest wade (2.0). With normal input, speed only falls with depth (stroke means). Sprint-stroke 2.4 beats chest-deep wading, which the director accepted (ruling/water-swim-numbers note a) |
| Sprint-stroke | **2.4 m/s** | Still under half of walk; no stamina |
| Back / strafe swim | ×0.6 | |
| Stroke surge | ±20% at 0.9 Hz | The laboured read without a HUD |
| Swim accel / decel; overspeed decel | 3 / 2; 6 m/s² | A sprint-jump in (8 m/s) bleeds to swim speed in ~1 s, ~5 m |
| Current push | none | Visual only (one vector is wrong on a bend) |
| Duck-under | deferred (not built in the POC) | Allowed, not required; less W8 surface |
| Exit on low banks | no special climb: swim ends ~4.5 m out, wade up the 18° shelf, 0.31 m lip < stepOffset 0.4 | Ledge assist only if W6 snags, capped at 0.45 m |
| Phase 1 tuning band (no re-approval) | swim 1.5-2.2, sprint-stroke ≤ 2.8, chest wade multiplier 0.35-0.5 | |

Real-world anchors (general physiology, not a cited study): walking ~1.4 m/s; recreational swimming ~0.5-1.0 m/s; waist-deep
walking roughly half of land speed. Our walk is 3.6× real, so a proportional swim would be ~2-3 m/s. We go lower on purpose (director: hazard, not shortcut).

## Sources

| [n] | Claim it supports | Publisher | Pub date | Accessed | Confidence |
|---|---|---|---|---|---|
| [201] | TLD has no swimming; falling through weak ice = fade, safe spot, Warmth 0%, Hypothermia, −10% Condition, clothing 100% wet; weak ice is the ocean boundary; wet clothing raises hypothermia risk | [The Long Dark Wiki (Fandom): Weak Ice](https://thelongdark.fandom.com/wiki/Weak_Ice); [Clothing](https://thelongdark.fandom.com/wiki/Clothing) | living wiki, undated | 2026-10-06 (w-web) | high |
| [202] | DayZ fast swimming −5 stamina/s, swimming recovery +1/s; normal swimming can't drown you, but drowning exists with the head submerged outside the swim state | [DayZ Wiki (Fandom): Stamina](https://dayz.fandom.com/wiki/Stamina); [DayZ Forums: "Drowning is real"](https://forums.dayz.com/topic/176790-drowning-is-real/); [Steam Community discussion](https://steamcommunity.com/app/221100/discussions/0/144513524086083205/) | wiki living; forum threads undated in check | 2026-10-06 (w-web) | high (stamina); medium (drowning detail, forum sources) |
| [203] | Rust wetness from submersion lowers body temperature; Drowning status when fully submerged at wetness 100; diving tank | [Rust Wiki (Fandom): Status Effects](https://rust.fandom.com/wiki/Status_Effects); [Diving Tank](https://rust.fandom.com/wiki/Diving_Tank) | living wiki, undated | 2026-10-06 (w-web) | high; ocean-edge kill and sharks medium (not on page) |
| [204] | Subnautica Void (Crater Edge): terrain ends; PDA "Entering ecological dead zone"; Ghost Leviathans at 0/40/80 s, retreat on return; oxygen rates by depth, alerts, ~8 s to death out of air | [Subnautica Wiki (Fandom): Crater Edge](https://subnautica.fandom.com/wiki/Crater_Edge); [Oxygen](https://subnautica.fandom.com/wiki/Oxygen) | living wiki, undated | 2026-10-06 (w-web) | high; 45 s base oxygen medium |
| [205] | The Forest sharks in water too deep to stand in, visible before the danger; swimming stows items; underwater breath meter, 300 s with a rebreather | [The Forest Wiki (Fandom): Shark](https://theforest.fandom.com/wiki/Shark); [Swimming](https://theforest.fandom.com/wiki/Swimming) | living wiki, undated | 2026-10-06 (w-web) | high |
| [206] | Green Hell 25 s breath + ~10 s to drowning; piranhas, black caiman; leeches picked up in water (−1 sanity) | [Green Hell Wiki (Fandom): Swimming](https://greenhell.fandom.com/wiki/Swimming); [Leeches](https://greenhell.fandom.com/wiki/Leeches) | living wiki, undated | 2026-10-06 (w-web) | high; stamina drain while swimming medium |
| [207] | RDR1: John Marston drowns almost at once above waist depth; repeated deliberately in RDR2's epilogue | [rdr2.org: Can John Marston swim?](https://www.rdr2.org/news/red-dead-redemption-john-marston-swim-actually/); [Wikipedia: John Marston](https://en.wikipedia.org/wiki/John_Marston) | article undated in check; Wikipedia living | 2026-10-06 (w-web) | high |
| [208] | BotW: empty stamina wheel while swimming → sink, lose one heart, return to land | [Zelda Wiki (Fandom): Stamina Wheel](https://zelda.fandom.com/wiki/Stamina_Wheel) | living wiki, undated | 2026-10-06 (w-web) | high |
| [209] | Sea of Thieves Devil's Shroud: red sea and sky, repeated hull damage, sink and respawn; loading-screen warning line | [Sea of Thieves Wiki (Fandom): Devil's Shroud](https://seaofthieves.fandom.com/wiki/Devil%27s_Shroud) | living wiki, undated | 2026-10-06 (w-web) | high |
| [210] | GTA V has sharks in deep ocean | [GTA Wiki (Fandom): Sharks (animal)](https://gta.fandom.com/wiki/Sharks_(animal)) | living wiki, undated | URL found 2026-10-06 (w-web), page content not fetched | medium |

Uncited by design: player-reception lines in §4a (design readings of reputation), the real-world speed anchors in §7 (general
physiology, not a study), and the Cumberland width estimate in §4b (order of magnitude only; it doesn't drive any number).
