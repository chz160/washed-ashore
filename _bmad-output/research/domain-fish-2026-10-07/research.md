---
title: 'domain research: fish in the waters around Bells Bend'
type: 'domain'
topic: 'Fish of the Cheatham pool of the Cumberland River: roster, bands, visibility, behaviour and measurable targets for Washed Ashore'
decision: 'Fish roster and look (G1-G4), carp ruling (G2), visibility depth (G5), measurable targets (G6) and technique (item 5) for the fish pod; final report with outcome (F8 PASS) and findings'
source: 'hand-run deep-recon layout (standard preset) by f-designer, with f-artist, f-td, f-level and f-web sections'
status: final (targets of record F18; F8 PASS)
preset: 'standard'
validation: 'normal (two-source on G2 carp and G5 Secchi)'
created: '2026-10-07'
updated: '2026-10-08'
claims_verified: 11
claims_unverified: 3
---

# Domain research: fish in the waters around Bells Bend

**Decision this research serves:** what fish the pod puts in the Cheatham pool around Bells Bend, where they sit by bed depth and structure, how a player above murky water sees them, and the numbers QA tests (F2-F9). Spec: planning-artifacts/poc-spec-washed-ashore-fish.md. Greenlight notes N1-N7: studio:decisions greenlight/fish.

Lead: f-designer (systems-designer): items 1, 2, 4 (game practice), 6, 7 and G1, G2, G5, G6. Sections: f-artist (items 3 and 5, look; G3; G4 stills): artist-items-3-5.md; f-td (item 5 technique, WebGL2 cost, APPROVED): td-item-5-technique.md; f-level (structure anchors, routes): level-structure-anchors.md; f-web (web citations and cross-checks): web-sources.md. Run folder: brief.md, .memlog.md, digests/ (D1-D6), sections/.

## Executive summary (final, targets of record F18)

1. **Roster (G1): 17 census species in 3 visibility tiers**, all native or long-naturalised in the Cheatham pool [1][2]; the USACE master plan lists 74 species including every roster species (f-web, web-sources.md). Only sunfish, largemouth bass and common carp can show a body (gar became shadow-only under f-director's L1), on the shelf near the surface. Everything else is surface signs.
2. **Carp (G2): silver and bighead carp OUT; common carp stands in.** Neither was in the Cumberland in 1987: USGS records start in 2002 (bighead) and 2011 (silver) [5][6]. They reached Tennessee "via locks at Kentucky and Barkley dams" [7][4]. f-web adds that Barkley's only way upstream is the lock chamber (USFWS 2021), and that Tennessee carp populations are sustained by migration, not local spawning (TWRA/MICRA 2022). With no lockage after 1987, Barkley blocks the way.
3. **25 years on (N3):** more big old catfish, buffalo (can live past 100 [13][14]), drum, gar and bass; stocked walleye, striper and trout gone; sauger fewer; shad a little fewer as nutrients fall [15]; fish less wary [17][18]. River no-take reserves show +124% density with bigger fish gaining most [Koning 2020, an upper bound]; long-protected streams hold more fish but grow slower at high density [Watson 2022], i.e. food-limited, which matches N3.
4. **Visibility (G5):** measured Cheatham Secchi is **1.0 m** (p10 0.7, p90 1.2; n = 572, USACE 2000-2023 [29]). The landed shader is murkier (Secchi-equivalent ~0.65 m) and wins (N1). The draw cull of record is f-td's FishMurk contrast floor: visibility depth 1.57 m straight down, 1.10 m from the bluff top, 0.48 m from the shore eye at 5 m out, plus R_sub = 25 m for sub-surface bodies.
5. **Targets (G6, §7):** bands by bed depth at 10.0 / 8.0 / 5.35 adults per 100 m of bank (shelf / ramp / open to 50 m), structure 1.2-2.0x; 2.0 sightings/min and 1.0 spontaneous surface events/min on the 5 m/s shore walk; max 3 bodies at once; bluff 1.2-3.0 surface events/min at both McCord and Buzzard; scatter 4 m wading / 6 m swimming / 2.5 m dry bank, settle 45 s. Signs count only within a measured legible distance (F17/F18).
6. **Outcome (F8): PASS, no retune.** Noah rated the 3-minute shore walk "Right" and realistic "Yes". He saw one fish in about 6 minutes and asked whether that is realistic. From the bluff he saw no fish and judged that realistic for the distance.
7. **Main findings:**
   - Automated F7 counts 7-15 body sightings/min on the shore walk, against Noah's ~1 in 6 minutes. That gap is the most important open question (Limitations).
   - Physically sized surface signs are legible only ~10-15 m from the 1.7 m shore eye (30 m for a flee wake) and ~30-100 m from the bluff eyes.
   - Two of six graded bluff looks fell below 1.2/min. These are recorded F7 findings.
8. **Technique (item 5): APPROVED by f-td:** instanced meshes with a baked Swim vertex-animation texture, per-fish steering, population as a pure function of (seed, cell), analytic depth clamp, fish drawn after the water with their own-depth fade.

**Biggest caveat:** no Cheatham-specific fish density or flight-distance study was reachable. Band densities, event rates and scatter distances are INFERRED from habitat descriptions, reservoir biomass composition, one tailwater electrofishing CPUE and clear-water diver studies; each is marked. Noah's F8 PASS is the validation of the overall feel, not of each number.

## G1: Roster
Full table with sources: sections/designer-items-1-2-4-6-7.md §1.4. Stand-in mapping (G3): sections/artist-items-3-5.md (Fish2: sunfish, crappie, shad; Fish1: the rest; gar non-uniform scale tested in G4 with a signs-only fallback; paddlefish census-only).

| Tier | Meaning | Species |
|---|---|---|
| V1 | Placed in the top 0.6 m on the shelf, so a body can show | bluegill (+longear, redear), largemouth bass, common carp |
| V2 | Shadow or flash at most, never a body | spotted bass, smallmouth bass, white crappie (+black), smallmouth buffalo (+bigmouth, Fish1 per L2), longnose gar (+spotted, shortnose; L1: long dark basking shadow, snout/back dimple, slow V-wake, sink swirl) |
| V3 | Surface signs only; `neverDrawBody` (my recommendation for f-artist's G4 question) | channel, flathead and blue catfish, freshwater drum, sauger, white bass, gizzard shad (+threadfin), skipjack herring, paddlefish |
| Rulings | ruling/fish-roster-look FINAL: LOOK APPROVED with conditions; L1 gar V1 -> V2; L2 buffalo uses Fish1; roster APPROVED, carp OUT (common carp jumps <= 0.25/min); R1 gar stays V1 only if G4 stills read as gar, else V3; R2 paddlefish signs only; R3 Robertson lee allocation moved to the chute mouths and the slack side of the face; R4 structure = more fish along the shore, not deeper | f-director |
| Out | Stocked, gone by 2012; or lore | walleye, striped/hybrid bass, rainbow trout; silver and bighead carp |

Adult lengths (MDC "commonly", cm) [2]: bluegill 13-24, LMB 25-50, spotted bass 25-43, smallmouth 25-50, crappie 23-38, channel cat 30-80, flathead 38-114, blue cat 50-112, drum 30-50, buffalo 38-76, common carp 30-64, longnose gar 60-120 (150 max), sauger 30-38, white bass 23-38, gizzard shad 23-36, skipjack 30-41, paddlefish 100-180. TWRA's Angler's Guide harvest ranges (f-web) are broadly consistent.

## G2: Carp
Sections §1.3, digest D2 and web-sources.md item 2. **Recommendation: OUT** (no pre-1987 presence; no plausible natural spread past Barkley without lockage; no local reproduction to sustain them). Common carp stands in; its rare jumps sit inside the surface-event cap. Not sourced (lore choice): the state of Barkley's spillway gates after 25 years unattended. Ruling belongs to f-director.

## Item 2: Bands on our bed (rev F1: bed depth only)
| Band | Bed depth | Nominal width | Adults /100 m | Per m² | Schools /100 m |
|---|---|---|---|---|---|
| A Bank and shelf | 0-2 m (fish where >= 0.3 m) | 7 m | 10.0 | 0.0143 | 0.5 shad |
| B Drop-off and ramp | 2-9.9 m | 13 m | 8.0 | 0.00615 | 0.5 shad |
| C Open water | >= 9.9 m and <= 50 m from the bank (final: team-lead / f-td rev 4); nothing beyond | 30 m | 5.35 | 0.00178 | 0.5 shad, 0.2 skipjack |
| S Structure | A and B within 20 m of an anchor | anchor shoreline | 1.2-2.0x band density | sections §2.3 | +shad at mouths |

Every bank, bluffs included, has the same bed (f-level). Real places: sections/level-structure-anchors.md (McCord and Buzzard bluffs, unnamed faces, Cleeces Ferry landing, the slipway, Robertson Island face plus chute mouths with the dry chute fish-free, hollow mouths st 1543/1669/52/107). No fish north of the line; no snag anchors (none in the game).

## Item 3: How you'd actually see them
Look and rendering: sections/artist-items-3-5.md (f-artist). Designer inputs:
- **Signs over bodies (N1).** From the 1.7 m shore eye a 0.3 m-deep fish stays visible to ~7.9 m out, a 0.6 m fish to ~4.0 m, a 1.0 m fish to ~2.0 m (f-artist). So bank sightings are mostly signs: dimples and rises (sunfish), swirls and boils (bass), rolls, tails and mud puffs (carp), snout and back basking plus air gulps (gar [2]), nervous water and flips (shad), V-wakes and swirls from fish the player flushes, and rare jumps (carp, skipjack [2], bass).
- **Gaps.** About one sighting every 30 s, never more than 3 in 15 s, never a dead stretch over 75 s (§7).
- **Bluff top:** surface events and wakes only; R_sub keeps bodies out (N2).

## G5: Visibility depth
| Item | Value | Source |
|---|---|---|
| Measured Secchi, Cheatham pool (reference) | median 1.0 m, p10 0.7, p90 1.2, range 0.5-1.6 (n = 572 my pull of CHE stations; f-web n = 624 for the whole HUC, same median and spread); Bordeaux Bridge and Overall Creek stations bracketing Bells Bend: median 1.0, p10-p90 0.8-1.2 (f-web). April-October only; no winter or flood readings. The shader (~0.65 m) is below the measured p10 (0.7-0.8 m): murkier than about 90% of summer readings, but within the measured range (0.5-1.6 m). The shader wins (N1) | [29], web-sources.md |
| Shader-implied Secchi | ~0.65 m (k_avg 2.6 /m, Poole-Atkins 1.7/k [30]) | project shader, D4 |
| **Visibility depth (F7, cull of record)** | **1.57 m straight down; 1.10 m from the bluff top; 0.48 m from the shore eye at 5 m out** — FishMurk, (1-F)·T̄ < 0.02 at the body's shallowest point | f-td / f-artist, shader constants |
| **Sub-surface draw radius** | **R_sub = 25 m** from the camera (3 m fade) | f-artist rule V2 |
| Tier rule (ruling/fish-visibility-tiers) | FishMurk decides only drawn / not drawn and never overrides tiers: a V1 body reads only within 0.6 m; deeper but inside the floor = dim shadow at most; V2 shadow/flash only; V3 never a body | f-director |
| Reconciliation | Shader wins (N1). Secchi measures a white disc to extinction; FishMurk is a contrast floor on a dark fish, so the two differ by design. Fish draw after the water with their own-depth fade (not the bed-depth alpha) | f-artist §1.3, gate/fish-technique-addendum-1 |

## Item 4: What games do
Sections §4 and digest D5 (designer), web-sources.md items 6 (f-web, accessed 2026-10-07; mostly community wikis and guides, no developer talks found), and spawn bubbles/LOD in td-item-5-technique.md.

| Game | Spawn / population | How fish show | Player reception | Our take |
|---|---|---|---|---|
| Valheim (datamined) | Spawn 40-80 m from the player; 64 m zones checked every 4 s; 3-10 fish per zone in groups of 2-7 within 2 m; no spawn within 12-20 m of existing fish; mostly below 5 m depth | Bodies in clear water; occasional jumps | Players complain fish respawn 40-80 m away rather than at their dock | Our ring (35-50 m in, out of view or below the murk floor) is the same idea; persistence per (seed, cell) avoids the "respawn elsewhere" complaint near anchors |
| RDR2 | Fish exist before the rod comes out (the rod-spawns-fish idea is a misconception) | Ripples, splashes, jumps; bodies when reflection allows; Eagle Eye highlight | Players learn to read ripples and jumps | Copy the surface cues; never the highlight (N7) |
| Sons of the Forest | Limited spawn locations; spring to fall only | Visible stream fish; "fish also swim away as soon as you wade in" | (not found) | Matches our wading scatter |
| The Long Dark | Abstract: hut/hole time-skips, ~1 fish per 1-2 h | No visible swimming fish | (not found) | Too empty for us |
| theHunter: The Angler (2022; COTW has no fishing) | Habitat grader of 20+ inputs (depth, flow, turbidity, vegetation, time, temperature); better habitat = bigger fish | Fish finder / underwater views | (not found) | Our bands + structure uplift are a small version of the same habitat grading |

Pattern: surface cues + anchored local populations; avoid glow highlights, visible open-water schools, and abstraction to nothing. Reception (web-sources.md 6b): The Angler review (Shacknews 2022) says fish are found by "seeing a fish jump... or seeing fish below the surface" but "the animations are a bit clunky" (supports keeping bodies rare and motion slow, N6); RDR2 fish praised as realistic detail (GameRant 2021); Valheim players accept "only fish where you can see fish swimming" but dislike fish appearing 40-80 m away and spots that move, and patch 0.213.4 stopped far fish despawning. No number changes from it.

## Item 5: Technique
sections/td-item-5-technique.md (f-td, APPROVED) and sections/artist-items-3-5.md (f-artist). Designer sizing: cell 32 m; body sim radius 50 m in / 60 m out; surface-event radius 150 m; at most 48 simulated individuals, 80 instanced school members, 6 live surface events (caps 256 / 64 / 16). External references in both sections are being checked by f-web.

## Item 6: Reacting to the player
| Parameter | Value (range) |
|---|---|
| Trigger: wading / swimming / dry bank walk (shelf fish, player within 2 m of the waterline) | 4 m (3-6) / 6 m (4-8) / 2.5 m (2-3.5) |
| Reaction time | 0.2-0.4 s |
| Bottom fish | react only to a swimmer within 4 m (3D); slide away slowly; burst inside 2 m |
| Flee | burst 1.5-2.5 m/s for 1-2 s, then 0.6 m/s; away and down-slope; 8-15 m |
| Flee within 0.6 m of the surface | emits a swirl or V-wake (a surface event) |
| Settle | return starts at 20 s; back in band by 45 s (30-90); no published settle time exists (f-web), INFERRED |
Basis: unfished-fish FIDs at the low end of 0.27-7.2 m [18][19][17]; no freshwater FID study exists (f-web). Freshwater bound: in lakes and reservoirs, small fish (~22 cm) avoided a 15-25 hp boat mostly within 10 m (up to 15 m in clear water), "not a serious problem" beyond 10 m (Drastik & Kubecka 2005, Fisheries Research 72; via web-sources.md 5b). A wader or swimmer is far quieter, so 4 m wading / 6 m swimming sits well inside that bound. f-web reads the diver proxy as 1-3 m; I keep 4 m because a wader is louder than a diver and murk fish detect by vibration. INFERRED; first lever in an F8 retune (range 3-6).

## 7. Targets (G6) — targets of record F18 (_bmad-output/poc/fish-targets.json, sha256 02319b3d…; studio:design fish/targets, fish/targets-rev). The table below is the F1 set; later revisions (§12) added per-species detail, the bluff camera (§9), far-water signs and sign legibility gating (§8) without moving any acceptance band
Per-species values (bands, group, min bed depth, swim depth mode, clearance, cruise/burst speed, turn rates, idle share, spacing, event rates, neverDrawBody) are in the JSON.

| # | Target | Value | Accept band | Test | Basis |
|---|---|---|---|---|---|
| T1 | Adults per 100 m of bank by bed-depth band, shelf / ramp / open (nominal widths 7 / 13 / 30 m) | 10.0 / 8.0 / 5.35 (= 0.0143 / 0.00615 / 0.00178 per m²) | ±25% each (F3); species mix per f-qa's reading | census, 3 seeds | INFERRED [1][2][20][21] |
| T2 | Shad schools per 100 m (each band); skipjack (open) | 0.5; 0.2 | ±50% | census | INFERRED [2] |
| T3 | Structure-area density (shelf / ramp per m²) | bluff 0.0186/0.0080; hollow mouth and chute mouth 0.0286/0.0123; landing/slipway 0.0214/0.0092; island face 0.0171/0.0074; plus flathead per anchor | ±25% pooled per anchor type | census in anchor areas | [1][2] INFERRED |
| T4 | Minimum bed depth: per species >= clearance + margin + 0.028 m wave + max body height (global floor 0.35 m; bluegill/gar 0.35, LMB 0.4, carp 0.45, bottom species 1.0+); no fish north of the line | per JSON; 0 | 0 violations | census | INFERRED |
| T5 | Bed clearance; surface margin (except scripted rise/jump/bask), each + half body height | small shelf species 0.08 / 0.05 m, gar bask margin 0.03, others 0.15 / 0.10 m | 0 violations (F4) | PlayMode 120 s | N7 |
| T6 | Sightings per minute: f-level's Robertson-McCord route (903 m, 6 m inland), 5 m/s, eye 1.7 m, yaw +35° toward the water, 3 seeds | **2.0** | 1.4-2.8 | F7 | INFERRED |
| T7 | Spontaneous surface events per minute (in view, within 60 m, and from F17 only within the kind's legible distance) | **1.0** | 0.6-1.5 | F7 | INFERRED [2][22] |
| T8 | Jumps per minute (subset of T7) | 0.1 | 0-0.25 | F7 | N4 cap; INFERRED |
| T9 | Flush signs per 100 m of bank walked | 0.35 | 0.2-0.5 | F7 | INFERRED [18] |
| T10 | Longest gap without a sighting; densest burst; first sighting | <= 75 s; <= 3 in any 15 s; <= 30 s | same | F7 | N2 |
| T11 | Max fish bodies visible at once (shore); from the bluff top | **3**; **0** | <= 4; 0 | F7 render stats, F8 | N1, N2 |
| T12 | Visibility | FishMurk floor (1.57 / 1.10 / 0.48 m) + R_sub 25 m | 0 drawn beyond | F7 | G5 |
| T13 | Bluff-top surface events per minute, near + far in frame, each of McCord and Buzzard (F16 geometric crest pose, 180 s) | 2.0 | 1.2-3.0 per look | F7 | INFERRED |
| T14 | Scatter trigger wading / swimming / bank | 4 / 6 / 2.5 m | ±1 m | F6 | [17][18][19] INFERRED |
| T15 | Settle time back into band | 45 s | 30-90 s | F6 | INFERRED |
| T16 | Cell size; body sim radius in/out; surface-event radius | 32 m; 50 / 60 m; 150 m | — | F9 | [23][27], f-td |
| T17 | Caps: simulated individuals; instanced school members; live surface events | 48; 80; 6 | under f-td 256 / 64 / 16 | F9 | f-td |
| T18 | Motion: cruise 0.3-1.0 body lengths/s (gar 0.2), rest threshold 0.05 m/s, idle anim rate 0.25x, stride 0.7 BL per Swim cycle, max clip rate 12.5/s, school spacing min 0.6 BL / target 1.0 BL, turn <= 45-120°/s cruising | per species in JSON | F5 correlation >= 0.8, no overlaps, <= 45°/frame | F5 | INFERRED |

**Sighting definition (for F7 code and f-qa):** a *body* sighting is a fish or school in the camera frustum, within R_sub, drawable by FishMurk and not terrain-occluded from the eye for >= 0.5 s; it recounts only after 10 s out of view. A *surface event* is a sign (rise, dimple, swirl, V-wake, nervous water, roll/tail, gar gulp or bask, jump, busting school) whose origin is in the frustum, within 60 m (150 m from the bluff) and not occluded, counted once when it spawns. Sightings = bodies + surface events; a flee wake is a surface event, and a body seen with its own wake counts once.

Game-practice comparison for G6: §4 above.

## 8. Signs: physical footprints and legibility (F17-F18)
f-director's rulings (ruling/fish-sign-footprint-physical, ruling/fish-sign-legibility-distance): surface signs stay physical, with no distance scaling, and they count toward F7 and the bluff only within their measured legible distance.
- **Footprints:** ring radius = front speed × visible life. The front speed is 0.23 m/s, the capillary-gravity minimum phase speed (sourced, Wikipedia "Capillary wave"). Visible lives stay at their pre-result values: dimple 1.5 s, rise/swirl/jump ring 2.0 s, roll 2.5 s, gulp 1.5 s, wake 3 s. That gives max ring radii of 0.35-0.58 m. Cores and patches stay within the F16 drawn footprint. Longer lives were proposed and refused: f-qa ruled that raising pre-result estimates after a miss, with no per-kind source, is excluded. f-web found no measured rise-ring size or duration (web-sources.md Item 8/8c).
- **Floor (f-qa plan rev 4.2):** the leading ring's minor axis >= 6 px at 1920×1080 with a 70° vFOV, and Weber contrast >= 0.05 on linear Rec.709 luminance, both in the same frame. Measured by f-artist in slots #18/#20 (TestResults/fish-lookdev/sign-legibility.json).
- **Table of record (F18):** each cell is the minimum of three independent derivations: f-designer from the JSON, f-designer's pixel recompute, f-qa's image diff. Distances in m:

| Kind | Shore | McCord | Buzzard |
|---|---|---|---|
| Dimple, Rise, GarGulp | 10 | 30 | 30 |
| Swirl, RollOrTail, Jump | 15 | 30 | 30 |
| Wake (flee and V-wake) | 30 | 100 | 100 |
| NervousWater/Flip, Busting | 15 | 60 | 60 |
| GarBask | 0 | 0 | 0 |

- **What this means:** from the shore eye, flat water signs go sub-pixel beyond ~15-30 m because they're seen nearly edge-on. That fits the only sourced spotting distance found: sipping rises are spotted only "within 30 feet" (~9 m; Rosenbauer, Fly Fisherman 2018, via f-web). GarBask's 0 comes from the vertical-extent size rule applied to a long thin body line; f-qa's image measure found it >= 6 px at shore 15 m and Buzzard 30 m, so it may be under-credited (a note for future work).

## 9. Bluff view (F16)
The original bluff pose (landmark, 10° down) framed water with no fish in it, and the 50 m open-band edge (ruling/fish-depth-bands rev 4) left little near-water to see. Team-lead's final decision:
- **Geometric camera:** eye 1.7 m above the cliff CREST (the last sample within 1 m of the landmark's ground height on the anchor normal), pitched so the foot waterline sits 3° inside the bottom of the frame. That's ~23.5° at McCord and ~20° at Buzzard with the game's 70° vFOV.
- **Duration and grading:** 180 s per bluff, both bluffs graded.
- **Far-water signs:** sign-only events beyond the 50 m edge at the open band's per-area rate, with no new tunable and no fish (ruling/fish-far-water-signs).
- **Result:** graded looks were McCord 1.67 / 2.33 / 1.0 and Buzzard 0.67 / 2.33 / 1.33 per min. Two looks fell below 1.2 and are recorded findings, graded per look as agreed before the run.

## 10. F3 census: statistical power note
- Band densities passed at all three seeds. The species-mix checks are limited by counts, not by the sim: several species live in groups (white bass schools of 8-20, buffalo 3-8, drum 2-6, carp 2-4), and the thin open band holds only a few groups per seed. For example, open-band white bass is ~57 fish per seed, about 4 schools.
- So a ±25% per-seed share is unreachable even for a perfect sim.
- f-qa's pre-agreed reading (plan rev 3.x): per-seed shares are graded only where the expected number of groups per seed is >= 10; otherwise counts are pooled over the 3 seeds with a compound-Poisson 2σ tolerance (σ² = E[groups] × E[size²]) and a hard ±50% bound.
- Future census specs should state the expected group count per cell beside every share target, and size the census area or seed count so each graded share has >= 10 expected groups.

## 11. Documented option: retune #1 (not issued)
F8 passed, so no retune was made. Staged and unissued: E:\GitHub\washed-ashore\_bmad-output\poc\fish-targets-F19-retune1-draft.json.
- **Rule:** daytime depth for the V1 species, minBedDepth 1.0 m for bluegill (from 0.35), largemouth (0.45) and common carp (0.55).
- **Source:** MDC: bluegill "at midday move to deeper water or shade"; carp feed in very shallow water in the late evening and early morning; largemouth are most active at dawn and dusk.
- **Change:** data only. Band counts relocate to the outer shelf, putting V1 bodies 10 m or more from the shore eye.
- **When to use it:** if a future check rates the shore "too busy", or if F7 body counts must fall without a definition change.

## 12. Revision history of the targets (fish-targets.json)
- **F1-F5, pre-gate:**
  - bands by bed depth with nominal widths
  - structure as density uplift
  - FishMurk visibility, director R1-R4
  - per-species clearance and min depth
  - stride 0.7 BL with burst caps
  - behaviour and sign-duration blocks
  - gar V2 and buffalo Fish1
- **F6-F11:**
  - open band edge at 50 m (F7 void)
  - jump bodies drawn to 40 m, airborne only for carp and skipjack
- **F12-F15:**
  - min bed depths validated against measured bodies
  - spotted bass depth fix (a crappie change was refused and reverted)
  - surface-sign band-rate multiplier 1.0, calibrated on held-out seeds
  - startle turn 720°/s
- **F16:** geometric bluff camera at both bluffs, 180 s, far-water sign-only events.
- **F17:** physical sign footprints and legibleDistanceByKind.
- **F18 (record):** Buzzard column final, NervousWater/Busting 60 m. sha256 02319b3dbf5cb70caf3638b56b710af2e7000db17cecfaed7bb40830cfb07670.
- **Rules learned:**
  - From team-lead's guardrail: targets change only where the target itself was wrong, with f-qa's agreement first, never to make a failing check pass.
  - Test-read files are written only between slots.

## Sources
| # | Source | Publisher | Date | Accessed | Conf. |
|---|---|---|---|---|---|
| [1] | [Cheatham Reservoir](https://www.tn.gov/twra/fishing/where-to-fish/middle-tennessee-r2/cheatham-reservoir.html) | TWRA | live | 2026-10-07 | high |
| [2] | [MDC Field Guide](https://mdc.mo.gov/discover-nature/field-guide) (species pages: channel-catfish, flathead-catfish, ... see D1) | Missouri Dept. of Conservation | live | 2026-10-07 | medium |
| [3] | [Ridgway et al., Bigheaded carps in the lower Tennessee and Cumberland rivers](https://doi.org/10.1656/058.016.0309) | Southeastern Naturalist | 2017 | 2026-10-07 | high |
| [4] | [Vallazza et al., Silver Carp passage at three locks and dams](https://doi.org/10.1002/jwmg.70112) | J. Wildlife Management | 2025 | 2026-10-07 | high |
| [5] | [USGS NAS, bighead carp, Tennessee](https://nas.er.usgs.gov/api/v2/occurrence/search?species_ID=551&state=TN) | USGS | live | 2026-10-07 | high |
| [6] | [USGS NAS, silver carp, Tennessee](https://nas.er.usgs.gov/api/v2/occurrence/search?species_ID=549&state=TN) | USGS | live | 2026-10-07 | high |
| [7] | [Invasive Carp](https://www.tn.gov/twra/wildlife/fish/invasive-carp.html) | TWRA | live | 2026-10-07 | high |
| [8] | [Colvin et al., deterrent locations, Tennessee and Cumberland rivers](https://doi.org/10.3133/ofr20251039) | USGS OFR 2025-1039 | 2025 | 2026-10-07 | medium |
| [9] | [Lester et al., Biological effects within no-take marine reserves](https://doi.org/10.3354/meps08029) | MEPS | 2009 | 2026-10-07 | high |
| [10] | [Colombo, Channel catfish in exploited and unexploited reaches of the Wabash](https://thekeep.eiu.edu/cgi/viewcontent.cgi?article=1005&context=bio_fac) | EIU | 2004 | 2026-10-07 | medium |
| [11] | [Otis et al., A largemouth bass closed fishery](https://doi.org/10.1080/02705060.1998.9663636) | J. Freshwater Ecology | 1998 | 2026-10-07 | high |
| [12] | [Lewin et al., Biological impacts of recreational fishing](https://doi.org/10.1080/10641260600886455) | Rev. Fisheries Science | 2006 | 2026-10-07 | high |
| [13] | [Lackmann et al., Bigmouth buffalo centenarian longevity](https://doi.org/10.1038/s42003-019-0452-0) | Communications Biology | 2019 | 2026-10-07 | high |
| [14] | [Lackmann et al., Centenarian lifespans of buffalofishes](https://doi.org/10.1038/s41598-023-44328-8) | Scientific Reports | 2023 | 2026-10-07 | high |
| [15] | [Yurk, Total phosphorus and fishery productivity](http://hdl.handle.net/10919/50103) | Virginia Tech | 1989 | 2026-10-07 | medium |
| [16] | [Archer et al., Fish response to woody debris in a channelised river](https://doi.org/10.1080/02705060.2019.1614103) | J. Freshwater Ecology | 2019 | 2026-10-07 | medium |
| [17] | [Goetze et al., Fish wariness and fishing pressure](https://doi.org/10.1002/eap.1511) | Ecological Applications | 2017 | 2026-10-07 | high |
| [18] | [Januchowski-Hartley et al., Fear of Fishers](https://doi.org/10.1371/journal.pone.0022761) | PLoS ONE | 2011 | 2026-10-07 | high |
| [19] | [Sbragaglia et al., Spearfishing modulates FID](https://doi.org/10.1093/icesjms/fsy059) | ICES J. Mar. Sci. | 2018 | 2026-10-07 | high |
| [20] | [Rainwater & Houser, Beaver Lake cove biomass](https://doi.org/10.1577/1548-8659(1982)2<316:scabof>2.0.co;2) | NAJFM | 1982 | 2026-10-07 | medium |
| [21] | [Parisek et al., Reservoir ecosystems support large pools of fish biomass](https://doi.org/10.1038/s41598-024-59730-z) | Scientific Reports | 2024 | 2026-10-07 | high |
| [22] | [Fishing (RDR2)](https://reddead.fandom.com/wiki/Fishing) | Red Dead Wiki (weak) | live | 2026-10-07 | medium |
| [23] | [Rockstar wildlife of RDR2 at GDC](https://gdconf.com/article/learn-how-rockstar-breathed-life-into-the-wildlife-of-red-dead-redemption-2-at-gdc/) | GDC (via wildlife-density-brief) | 2019 | 2026-10-05 | medium |
| [24] | [Fish (Valheim)](https://valheim.fandom.com/wiki/Fish) | Valheim Wiki (weak) | live | 2026-10-07 | medium |
| [25] | [Fish (Sons of the Forest)](https://sonsoftheforest.fandom.com/wiki/Fish) | SotF Wiki (weak) | live | 2026-10-07 | medium |
| [26] | [Ice Fishing (The Long Dark)](https://thelongdark.fandom.com/wiki/Ice_Fishing) | TLD Wiki (weak) | live | 2026-10-07 | medium |
| [27] | [The systemic AI of Far Cry](https://www.gamedeveloper.com/programming/the-definition-of-artificial-insanity-the-systemic-ai-of-far-cry) | Game Developer (via wildlife-density-brief) | n/a | 2026-10-05 | medium |
| [28] | [theHunter COTW population notes](https://www.nexusmods.com/thehuntercallofthewild/articles/53) | Nexus (weak) | n/a | 2026-10-05 | low |
| [29] | [Water Quality Portal, Secchi, HUC 05130202](https://www.waterqualitydata.us/data/Result/search?huc=05130202&characteristicName=Depth,%20Secchi%20disk%20depth&mimeType=csv) (USACE Nashville District stations 3CHE/CHE200xx) | USGS/EPA WQP | 2000-2023 | 2026-10-07 | high |
| [30] | [Secchi disk (Poole-Atkins)](https://en.wikipedia.org/wiki/Secchi_disk) | Wikipedia (via water research [105]) | live | 2026-10-06 | medium |

Project inputs: water-td-note.md, water-swim-spec.md, BellsBendWater.shader, controller-tuning.md (walk 5 m/s), wildlife-density-brief.md, README.md (1987 collapse).

## Limitations
- **The F7-vs-F8 body gap (the main open question).** Automated F7 counted 7-15 body sightings/min on the shore walk (4.3-8.7 counting each group once), while Noah saw about one fish in 6 minutes and rated the shore "Right". Hypotheses, most likely first:
  1. The body-sighting definition counts any FishMurk-drawable body (contrast floor 0.02), not a human-noticeable one. Signs got a perception floor (6 px, Weber 0.05) and bodies did not.
  2. The F7 camera yaws 35° toward the water at a steady 5 m/s, while a player looks ahead and pauses.
  3. Loose-group members count per fish (the reading of record).
  4. 1080-line capture vs the player's display.
  The test for (1) is to re-score the probe's bodies with the sign floor; f-qa is checking it as information only. Lesson: sighting targets need a perception threshold, not a render threshold, and that should be agreed before the first graded run.
- **Search access.** No web search tool; general search engines blocked scripted queries. Sources were reached through primary APIs (USGS NAS, Water Quality Portal, OpenAlex abstracts, Wikipedia/Fandom APIs) and agency sites; f-web later supplied web-searched citations (web-sources.md). Abstract-only reads are marked medium.
- **INFERRED targets.** No Cheatham-specific density, flight-distance, rise-rate or settle-time measurements exist; those targets are INFERRED, and F8 validated the overall feel only.
- **Sign lifetimes.** No measured ring size or visible duration exists for rises, rolls or splashes. Lives stayed at pre-result values; f-web's physics notes (rings carried by ~3 cm waves, ~12 s clean-water decay) are judgement inputs, not sources.
- **GarBask legibility.** The 0 may under-credit a long, thin sign because of the vertical-extent size rule.
- **Player reception.** Evidence is thin: one review and a few community complaints (web-sources.md 6b).
- **Unverified:** Lester et al.'s effect sizes (only the abstract's direction is used). f-web verified f-td's technique references except BatchRendererGroup, which isn't supported on WebGL (f-td informed).
