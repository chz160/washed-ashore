---
title: 'Bird density brief: Washed Ashore walk-around (Phase 0 / B1)'
owner: systems-designer (bd-designer)
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-birds.md
precedent: _bmad-output/poc/wildlife-density-brief.md (approved; R0, R1, R1-A), wildlife-qa-report.md
consumers: level-designer (B3, B5 placement), gameplay-engineer (B3-B6), technical-artist (B2, scale), qa-lead (B6, B7)
created: '2026-10-05'
status: approved by game-director (bd-director) 2026-10-05 (final ruling), with amendments A1-A4; record studio:decisions / greenlight/birds-brief
---

# Bird density brief

**Player problem:** on a 3-minute walk across the 512 m x 512 m map (26.2 ha), the sky and the ground should both feel alive. There is never an empty sky for long, never a swarm, and the robins near the path let you get close before they go.
**Loop served:** the explore and walk loop. Birds are ambient presence: no audio, no interaction, no hunting (spec, out of scope).

> **Sources not verified this session.** WebSearch and WebFetch are disabled in this session (Noah's setting; friction BD1), and team-lead ruled out any workaround. **No citation below was fetched or checked today.** If web access is enabled later, verifying the citations is a follow-up task. The numbers don't change unless a source contradicts them. **Director amendment A3:** these citations are accepted for B1. Their DOIs must be fetched and verified before this brief becomes the tuning baseline for the real game. That does not block Phase 1.

**Source rules.** Sources are tagged:
- **(lit, not re-fetched)**: a peer-reviewed paper or standard reference cited from the designer's knowledge, with its DOI or stable URL. The exact figures quoted from it are **approximate** and should be treated as ranges, not point values.
- **(pod, not re-fetched)**: a source an earlier pod retrieved (the birds research.md or the wildlife brief), cited from that document and not fetched again today.
- **(weak)**: a fan wiki or forum, or the designer's play knowledge.
- **INFERRED**: my own arithmetic or design judgement, not a sourced fact.

## 1. Real-world baseline

### 1.1 Ground songbirds (American robin)

| Fact | Value | Source |
|---|---|---|
| Habitat | Lawns, short grass, open woodland, forest edges and clearings; forages on the ground by run-stop-peck; rare in closed forest interior | Birds of the World, American Robin (Vanderhoff et al.), https://birdsoftheworld.org/bow/species/amerob/cur/introduction (lit, not re-fetched) |
| Breeding density | High in edge and lawn habitat (of the order of 1-5 birds/ha), low in closed forest (well under 1/ha) | Same (lit, approximate, not re-fetched) |
| Social structure | Territorial pairs when breeding; loose foraging groups and large roosting flocks outside the breeding season | Same (lit, not re-fetched) |
| Flight-initiation distance (FID) of thrush-sized songbirds | Roughly 5-20 m. Urban and path-side birds flush at about half the distance of rural ones | Møller 2008, *Behav Ecol Sociobiol* 63:63-75, doi:10.1007/s00265-008-0636-y ; Díaz et al. 2013, *PLoS ONE* 8:e64634, doi:10.1371/journal.pone.0064634 (lit, approximate, not re-fetched) |
| FID depends on how far away the approach starts | Birds approached from farther away flush earlier | Blumstein 2003, *J Wildl Manage* 67:852-857, doi:10.2307/3802692 (lit, not re-fetched) |
| Body size and FID | Larger species flush earlier; small passerines tolerate closer approach | Blumstein 2006, *Anim Behav* 71:389-399, doi:10.1016/j.anbehav.2005.05.010 (lit, not re-fetched) |
| Alert before flight | Birds stop foraging and look up at an "alert distance" larger than the FID | Fernández-Juricic, Jimenez & Lucas 2001, *Environ Conserv* 28:263-269, doi:10.1017/S0376892901000273 (lit, not re-fetched) |
| Escape flight | Short: flushed ground songbirds usually fly to cover or open ground tens of metres away, then resume foraging | Birds of the World (lit, not re-fetched); 20 m minimum comes from the spec (B4) |

**Scaled to 26.2 ha (INFERRED).** The map is clustered forest and meadow (WorldBuilder: about 495 trees placed by Perlin noise, grass splat everywhere that isn't steep or rocky). If about 40% of it (about 10 ha) is open or edge, real robins would number roughly 10-30, with fewer than half on the ground at any moment. The outer bound of 4-30 already matches reality, so **robins need no game uplift in headcount, only placement near the path**.

### 1.2 Corvid and pigeon flocks (the recoloured Pigeon reads as a crow or raven)

| Fact | Value | Source |
|---|---|---|
| American crow groups | Family groups of about 2-15 (cooperative breeders); daytime foraging groups of a few to tens; winter roosts of hundreds to thousands | Birds of the World, American Crow (Verbeek & Caffrey), https://birdsoftheworld.org/bow/species/amecro/cur/introduction (lit, not re-fetched) |
| Common raven groups | Territorial pairs; non-breeders gather in loose groups of a few to dozens at food or roosts; soars in thermals and along ridges | Birds of the World, Common Raven (Boarman & Heinrich), https://birdsoftheworld.org/bow/species/comrav/cur/introduction (lit, not re-fetched) |
| Movement modes | Circling or soaring over a food source or thermal; ridge soaring along ridgelines; commuting in loose straight streams between roost and feeding areas | Same two accounts (lit, not re-fetched) |
| Flight height | Commuting and local flights mostly tens of metres above ground; soaring ravens go well above 100 m | Same (lit, approximate, not re-fetched) |
| Flap versus glide | Large birds use intermittent flap-gliding: flap to gain height or speed, glide to save energy. Crows flap most of the time; ravens and pigeons glide a lot more | Rayner 1985, *J Theor Biol* 117:47-77, doi:10.1016/S0022-5193(85)80164-8 ; Tobalske 2007, *J Exp Biol* 210:3135-3146, doi:10.1242/jeb.000273 (lit, not re-fetched) |
| Flock motion model | Separation, alignment, cohesion (boids) is the standard model for flock motion | Reynolds 1987, SIGGRAPH '87, doi:10.1145/37401.37406 (lit, not re-fetched) |

**Scaled (INFERRED):** a corvid family group of 4-7 is the realistic unit for a small valley. A real sky over 26 ha would often hold zero to one such group. Two to three groups in the air at once is a game uplift of about 2-3x, the same kind of uplift the wildlife brief accepted for deer.

## 2. What games do

| Game | Technique | Source |
|---|---|---|
| The Long Dark | Crows circle over carcasses and act as a signal to the player; birds fly in V formations and vanish in bad weather | Birds research.md [7] (pod, not re-fetched; weak: fan wiki) |
| Red Dead Redemption 2 | Birds placed by habitat; perch and flush from riverbanks; descend on carcasses | Birds research.md [7] (pod, not re-fetched; Audubon article); wildlife brief: https://gdconf.com/article/learn-how-rockstar-breathed-life-into-the-wildlife-of-red-dead-redemption-2-at-gdc/ (pod, not re-fetched) |
| Far Cry 3/4 | Ambient life kept active only in a bubble around the player (about 500 m), spawned ahead along the player's heading; "if the player didn't see it, it didn't happen" | https://www.gamedeveloper.com/programming/the-definition-of-artificial-insanity-the-systemic-ai-of-far-cry (pod, not re-fetched; from the wildlife brief) |
| Living World Project (devlog) | Distant flocks are cheap sprites or simple meshes | Birds research.md [7] (pod, not re-fetched) |
| Firewatch, Valheim | Distant circling birds over open ground; small birds near the player that fly off on approach | Designer's play knowledge, no source retrieved (weak, not re-fetched) |

No source gives a shipped bird count or density (birds research.md, "biggest caveat"). **Conclusions (INFERRED):**
- **Seen versus existing.** Games show birds far more often than their headcount would suggest by putting them where the player looks: robins on path edges, flocks over clearings that sit in the forward view. The wildlife pod proved this at 15 animals; birds use the same lever.
- **Spawn bubble.** Far Cry's bubble is bigger than our map, so a bubble buys nothing. **All birds are placed at scene load and persist** (the wildlife model). A flushed robin **re-lands** in habitat; despawning is only the B4 fallback when no landing spot is clear, and it must happen out of view.
- **Placement.** Ground birds go on open grass at forest edges, never under canopy or deep in forest (RDR2, Birds of the World). Flocks hang over clearings, ridgelines and points of interest, where a ground-level camera can see sky above the tree line.

## 3. Targets

### 3.1 Population (B3, B5): 16 robins and 3 flocks of 15 crows, inside every outer bound

**Robins: 16 in 7 patches** (outer bound 4-30).

| Patch type | Patches | Robins per patch | Total | Purpose |
|---|---|---|---|---|
| Trail-edge | 3 | 3, 2, 2 | 7 | The walker passes within the FID, so these produce the flushes |
| Meadow | 4 | 3, 2, 2, 2 | 9 | Seen foraging 10-25 m off the path; mostly not flushed |

**Flocks: 3 flocks of 6, 5 and 4 crows = 15** (outer bounds: 1-4 flocks, 3-12 each).
- Sizes match a corvid family group (Birds of the World).
- **15 flock birds plus at most about 5 robins within readable range** keeps the in-view count under the 20 cap by construction. The B6 band still gates it.

**Retune envelope for B7 (no change past this without director re-approval; every value is inside the spec's outer bounds):**
- Robins 8-26 in 4-10 patches of 1-4.
- Flocks 2-4 of 3-9 birds, with **at most 18 flock birds in total**.
- Robin FID 6-12 m.
- Flock altitude floor 25-45 m, ceiling 50-80 m AGL. The B6 altitude band tracks the configured band (A2).

### 3.2 Habitat placement rules (B3, B5; level-designer)

Placement uses the wildlife conventions: route arcs, seeds, 200 attempts per anchor, up to 20 redraws, and no silent relaxation. **Birds need no NavMesh**; robins sample terrain height directly.

| Rule | Value |
|---|---|
| All robin patches | Grass splat weight ≥ 0.6; slope ≤ 15°; nearest tree instance (not rock) **6-30 m** away (edge, but outside the canopy); no rock instance within 3 m; ≥ 20 m from PlayerSpawn; ≤ 170 m from the terrain centre (256, 256); ≥ 40 m from every other patch centre; ≥ 10 m from every wildlife group anchor |
| Trail-edge patch | Patch centre **4-6 m** perpendicular from the route polyline (wildlife brief 3.5). Robins on a path edge are real behaviour, so the wildlife 15 m minimum doesn't apply |
| Off the trail (all robins, A4) | Every robin's start point, hop target and landing spot is ≥ 3 m from the route polyline; hops that would cross the 3 m line are rejected |
| Meadow patch | Patch centre **10-25 m** perpendicular from the route polyline |
| Robin spread | Each robin starts within 6 m of its patch centre and ≥ 1.0 m from every other robin. Its hop leash is 4 m around its own start point and must stay on grass splat ≥ 0.6 |
| Robin arcs | Split the closed route into **8 equal arcs** (about 110 m each, from W0). The 7 patches take 7 different arcs (nearest point on the polyline; a tie goes to the lower arc), and one random arc stays empty. The 3 trail-edge arcs are **pairwise non-adjacent** (cyclic). Assignments are drawn only from the valid set, as in the wildlife R1-A |
| Robin approach sightline | From at least one route point 10-30 m **before** the patch's nearest route point, a ray from eye height (+1.65 m) to the patch centre (+0.2 m) is clear of terrain and trunk colliders **and** passes the foliage-aware test (no tree instance within 2 m horizontally of the ray between its base and crown top; `WildlifeFoliage` logic). This is the wildlife director's primary proposal, adopted up front |
| Flock orbit centre (primary POI) | One of: (a) **clearing**: no tree instance within 20 m, where 'tree' excludes bushes and rocks (Bush_*, Rock_*) (the WildlifeRules convention) (A5; was 30 m); (b) **ridgeline or hilltop**, a terrain point higher than every point within 40 m; (c) a **point of interest** placed by level-designer (none exists yet, so (a) or (b) for this POC). Horizontal distance from the nearest route point **50-150 m**; ≤ 190 m from the terrain centre; ≥ 120 m from every other flock's primary POI |
| Flock arcs | Split the route into **3 equal arcs** (about 293 m each). Each flock's primary POI takes a different arc, shuffled per seed, so the sky is covered around the whole loop |
| Flock commute (secondary POI) | Each flock also has a secondary POI meeting the same rules, in the next arc (cyclic). It **dwells 25-40 s** (R1; was 50-90) at one POI, then commutes to the other in a loose stream at cruise speed. The first dwell starts at a random phase so the 3 flocks don't move together |
| Seeds | B6 uses **101, 202, 303** (gating) and **404, 505, 606, 707, 808** (robustness, non-gating; seed-fragile if 2 or more miss a band). **World.unity and the B7 Windows build use seed 101.** Bird RNG streams are separate from the wildlife streams, so wildlife placement on every seed is unchanged (B8) |

If any anchor or patch fails 200 attempts and all 20 redraws, the seed fails and is logged. The rules are never relaxed silently.

### 3.3 Behaviour tuning (B3-B5; gameplay-engineer)

All values are serialized fields with these defaults, in a `BirdTuning` ScriptableObject (a separate asset from `WildlifeTuning`).

**Robin, on the ground (B3):**

| Param | Default |
|---|---|
| Clip bouts | PeckGround 1-3 s; Hop 1-3 hops of about 0.3 m each; Idle 1.5-4 s; Preen 2-5 s; ScratchGround 1-2 s |
| Clip weights | PeckGround 35%, Hop 25%, Idle 20%, Preen 10%, ScratchGround 10%; never the same clip twice in a row |
| Desync | Random start clip and normalised time per robin; playback speed 0.9-1.1x per robin |
| **Alert distance** | **14 m**: stop foraging, play an Idle variant facing the player |
| **Flight-initiation distance (FID)** | **8 m**, horizontal distance from the player to the robin |
| Social flush | When a robin flushes, every patch-mate within 8 m of it flushes after a 0.2-0.8 s random delay. Each counts as its own flush event |
| Flush sequence (B4) | Flutter 0.3-0.6 s, then Fly. Climb to **4-10 m** above ground, heading away from the player ±40° |
| Landing | Lands **25-40 m** away on robin-patch habitat (3.2) ≥ 12 m from the route polyline, then calms for 20 s (no re-alert, and Hop and Peck only). Path check: a capsule cast of radius 0.3 m from the start to the landing spot is clear of terrain and tree colliders. Up to 8 headings are tried; if none is clear, the robin **despawns out of view** (B4 fallback) |
| Re-flush cap | At most 2 flushes per robin per run |

**Why 8 m (INFERRED from 1.1):** it sits in the low-middle of the 5-20 m songbird range, which fits path-side, habituated birds (Møller 2008). It gives about 1.6 s of warning at the 5.0 m/s walk speed. The flush happens inside the roughly 25-30 m range at which a 25 cm robin is readable on screen (about 6 px tall at 30 m, 70° FOV, 1080p), so the player sees it.

**Flocks (B5):**

| Param | Default |
|---|---|
| Altitude, measured above the terrain directly below each bird (AGL) | Band **25-50 m** (R1; was 30-60), orbit mean 30-45 m so the ±5 m wave stays inside the band. Also ≥ the **crown top + 10 m**, where the crown top is the highest crown (`WildlifeFoliage.PrototypeHeight` × heightScale + base y) of any tree instance within 30 m horizontally. bd-level reports the tallest crown in the scene so the floor can be checked |
| Orbit | Radius **35-55 m** (R1; was 20-35) around the POI; airspeed 9-12 m/s (lap about 20-38 s); direction random per flock |
| Altitude wave | Each flock's target altitude follows a sine of ±5 m amplitude with a period of 20-30 s, so climbing and level-or-descending segments alternate |
| Flap/glide rule (B5) | **Flapping when vertical speed > +0.3 m/s; Gliding otherwise.** Minimum dwell 1.0 s per clip, with a 0.25 s crossfade |
| Commute | Cruise 12 m/s, level flight at 35-45 m AGL (R1; was 40-50, kept inside the new band), loose stream with a spacing of 4-8 m |
| Spacing | Boids separation with a target neighbour distance of **≥ 3 m**; hard floor **1.5 m** centre to centre (B5's "never overlap"). Cohesion keeps every bird within 15 m of the flock centroid |
| Visual scale (technical-artist) | Wingspan about **1.0-1.2 m** (between a crow and a raven), so that a bird is still 3-5 px across at 200 m |

### 3.4 Sighting targets (B6), the pass bands

**Method (reuse the wildlife harness unchanged where it overlaps):**
- Same walker and route (wildlife brief 3.5), the same camera (16:9, vertical FOV 70°, pitch fixed at 0°, scan ±25° / 10 s), 360 samples at 0.5 s, t = 0 at 1.0 s after grounding. Same seeds as 3.2.
- **Bird visibility.** All of these must hold:
  - the bird's renderer bounds intersect the camera frustum;
  - its distance is within range: **robins ≤ 30 m**, **flock birds ≤ 200 m**;
  - at least one of the two rays (to the bounds centre, or to the top-centre) is unblocked by terrain, tree colliders or Default;
  - **and** that same ray passes the foliage-aware test (`WildlifeFoliage.SeesThrough`).

  The foliage test is **gating for birds** (it was only non-gating for wildlife). This puts the wildlife director's canopy caveat into effect before placement rather than after the fact.
- **Max birds in view** counts every bird (robins and flock birds) that is in frame, ≤ 200 m away and unoccluded. This is a deliberate superset, used as the conservative check against the 20 cap.
- A **flock is in view** in a sample when **≥ 2 of its birds** are visible. The **sky is empty** when 0 flock birds are visible. An **empty-sky gap** is a maximal run of empty-sky samples × 0.5 s, and the leading and trailing gaps count.
- A **ground bird is met** the first time in a run that it is visible (robin rules) in ≥ 2 consecutive samples. Each robin counts once per run, including after it re-lands.
- A **flush event** is a robin entering Flutter because of the player (social flushes included). It is **seen** if that robin is visible (robin rules) in the flush sample or in the next sample.
- **Glide share** = (flock-bird samples in Gliding) / (all flock-bird samples in flight, excluding commute takeoff or landing, which don't exist in this POC).
- **Birds are excluded from the wildlife harness.** They are on their own `Birds` layer, have no colliders in the wildlife occluder mask, and don't count as animals, so `wildlife-sightings.json` is unchanged (B8).

**Known bias (INFERRED):** pitch 0° means a flock within about 40-70 m horizontally (directly overhead) is out of frame, although a real player would look up. The sky metrics are therefore **pessimistic**, the opposite of the wildlife canopy bias. The bands allow for this, and the camera won't be changed to fit the test.

### TARGETS (B6 gating unless marked; every one of seeds 101, 202 and 303 must land inside every gating band)

| Metric | Target | Pass band | Criterion |
|---|---|---|---|
| **Flock-in-view %** (samples with ≥ 1 flock in view) | 45% | **30-65%** | B6 |
| **Longest empty-sky gap** | ≤ 20 s | **≤ 45 s** (R1; was ≤ 30 s) | B6 |
| **First flock sighting** (leading empty-sky gap) | ≤ 10 s | **≤ 30 s** (gating, R1; was ≤ 20 s non-gating) | B6 |
| **Ground birds met per minute** (distinct robins met / 3) | 3.5 /min (about 10-11 robins) | **1.7-5.0 /min (5-15 robins)** | B6 |
| **Flush events per 3-minute walk** | 6 | **3-12** | B6 |
| Flushes seen (share of flush events) | ≥ 60% | **≥ 40%** | B6 |
| **Max birds in view at once** (all birds) | 6-12 | **3-20** (20 is the spec's hard cap) | B6 |
| Busy guard: samples with ≥ 15 birds in view | ≤ 2% | **≤ 5%** | B6 |
| **Flock count and size** | 3 flocks: 6, 5, 4 (15 birds) | Exactly as configured; within the 3.1 envelope after retunes | B5 / B6 JSON |
| **Glide/flap share** (glide share of flock-bird flight samples) | 55% glide | **40-75% glide** | B5 (60 s) and B6 |
| Flap-glide rule compliance | 100% | **≥ 95%** of flock-bird samples obey the 3.3 rule (the 1.0 s dwell explains the rest) | B5 |
| **Robin flight-initiation distance** (A1) | Configured FID, 8.0 m by default | **Player-triggered flushes fire at the configured FID -1.0/+0.5 m (7.0-8.5 m by default)**, measured horizontally. Social flushes count as events but are excluded from the distance band (B4 per-frame check; B6 logs each flush distance and whether it was player-triggered or social) | B4, B6 |
| Robin escape | climbs 4-10 m; lands 25-40 m away | Distance from the player grows, height gain ≥ 2 m, landing ≥ 20 m away or despawn out of view, 0 collider overlaps | B4 |
| **Flock altitude band** (A2) | Configured band, 25-50 m AGL by default (R1), mean 30-45 m | **Every flock-bird sample within [floor - 5, ceiling + 10] m AGL of the configured band (20-60 m by default, R1), ≥ the crown top + 5 m, and inside the absolute limits of 20-90 m AGL** | B5 (60 s) and B6 |
| Flock spacing | ≥ 3 m | **Never < 1.5 m** between any two birds of a flock | B5 |
| Robin population | 16 in 7 patches | Exactly as configured, every robin on grass splat ≥ 0.6 at spawn | B3 |

**Pre-placement estimate (INFERRED):**
- **Sky.** At 16:9 the horizontal FOV is about 102°, or 28% of the compass. A 25 m orbit seen from about 100 m widens that to about 36%. A flock is inside the 40-200 m annulus for roughly 80% of the loop, and is clear of canopy about 65% of the time. That gives about 19% per flock and **about 47%** for 3 independent flocks, which sits on the target. With 2 flocks the estimate drops to about 34%, near the floor, which is why the population is 3.
- **Ground.** Patches are next to the route and clear of the approach sightline, so about 70% of 16 robins are met: about 11, or **3.7 /min**. About 80% of the 7 trail-edge robins flush: **about 6**.

### 3.5 Output file

`TestResults/bird-sightings.json`, with one entry per run:
`seed, samples (360), flockInViewPct, longestEmptySkySec, firstFlockSec, groundMetCount, groundMetPerMin, flushEvents, flushSeenPct, flushDistances[], maxBirdsInView, pctSamplesGe15, glideShare, flapRuleCompliance, altitudeMinAGL, altitudeMaxAGL, minCrownClearance, minFlockSpacing, flocks [{size, primaryPOI, secondaryPOI, arc}], patches [{type, size, centre, arc}], redraws, pass (bool per band)`, plus a per-sample array (visible robins, visible flock birds per flock, and flush flags).

Also report, non-gating, the same sky and ground metrics **without** the foliage test, so the size of the canopy correction is visible.

## 4. Where they go (summary for level-designer)

- **Ground birds:** short open grass (splat ≥ 0.6, slope ≤ 15°) at forest edges, 6-30 m from the nearest tree. That is the clearing margin, never under canopy or in deep forest, and never on rock or steep ground. 3 patches sit on the path verge (4-6 m) (they flush); 4 sit 10-25 m out in the meadow (they're watched, not flushed).
- **Flocks:** over clearings (no tree instance within 20 m, where 'tree' excludes bushes and rocks (Bush_*, Rock_*); A5, was 30 m) and ridgelines or hilltops, 50-150 m from the route so they sit in the forward view above the tree line, with one flock per third of the loop. Each flock commutes between two such spots every 25-40 s (R1). Any future point of interest (a wreck, a carcass) becomes a POI candidate: The Long Dark's crows over a carcass is the model.

## 5. B7 retune ladder (at most 2 retunes; each one re-runs B6; all within the 3.1 envelope)

The sky and the ground are rated separately and retuned separately. One retune may change both if both are off.

| Rating | Step 1 | Step 2 | Step 3 |
|---|---|---|---|
| Sky too empty | (Removed by R1: moving POIs closer has no effect in the calibrated model) | Add a 4th flock of 3 (total 18, the cap) | Lower the altitude band to 25-50 m AGL (already the R1 default) |
| Sky too busy | Shrink the 6-flock to 4 (total 13) | Remove the 4-flock (2 flocks, 11) | Raise the altitude band to 40-75 m AGL |
| Ground too empty | Add a 4th trail-edge patch of 2 (total 18) | Grow every patch by 1, max 4 per patch (at most 26, the envelope cap) | FID 8 → 10 m (flushes start earlier and are more visible) |
| Ground too busy | Remove 1 meadow patch (total 13-14) | Trail-edge patches go to 1-2 robins | FID 8 → 6 m (flushes start later, so fewer patch-mates are caught in a social flush) |

The B6 bands stay the same unless the director re-approves them. A retune that pushes a band out of range forces a re-run and is recorded below.

## Retunes

### R0: flock POI feasibility (A5, 2026-10-05; a pre-placement rule amendment, not a B7 retune, so it spends no slot)

| Value | Before | After | Reason |
|---|---|---|---|
| Flock clearing POI (3.2, clause (a)) | No tree instance within 30 m, with bushes counted as trees (as read) | No tree instance within **20 m**, where 'tree' excludes bushes and rocks (Bush_*, Rock_*) (the WildlifeRules convention) | bd-level measured World.unity (735 instances, tallest crown 11.3 m): 0 qualifying ridges, and 0 clearings 50-150 m from the route in thirds 1 and 2. Placement failed on 8/8 seeds. Under A5 it passes 8/8 seeds with 1-2 flock draws |

Unchanged: the ridge/hilltop option, every other flock-POI rule, every robin rule as placed, the population, the altitude, every B6 band and the B7 envelope. Binding record: the lead's `studio:decisions` / `amendment/birds-brief-A5` (bd-director's `greenlight/birds-brief-A5` is void). Condition: bd-level re-dry-runs 8/8 seeds with this narrower scope before placing. The ruling is quoted under "## Approval", A5.

### R1: pre-B7 sky correction (2026-10-05; no B7 slot)

| Value | Before | After | Reason |
|---|---|---|---|
| Longest empty-sky gap band (3.4) | ≤ 30 s | **≤ 45 s** (target stays ≤ 20 s) | B6 attempt 1 failed the sky on all 3 seeds (flock-in-view 18.3 / 26.9 / 38.6%; gaps 81 / 41 / 47.5 s). The designer's Monte Carlo of the B6 walker, calibrated to those runs, shows the limit is azimuth (flocks outside the view cone), and the 30 s band was an uncalibrated estimate |
| First flock sighting (3.4) | ≤ 20 s, non-gating | **≤ 30 s, gating** | So the walk can't open on an empty sky (R1 final) |
| Orbit radius (3.3) | 20-35 m | 35-55 m | Wider sweep across the view cone |
| Dwell at each POI (3.2) | 50-90 s | 25-40 s | More movement across the sky |
| Altitude band (3.3) | 30-60 m AGL | 25-50 m AGL (A2 gate 20-60 m, ≥ crown + 5 m); commute 35-45 m AGL | Lower flocks sit lower in the frame; inside the B7 envelope |
| Ladder "Sky too empty" step 1 (5) | POIs closer | Removed | No effect in the model |
| **Attempt 3 (conditional)** | 3 flocks of 6/5/4 | **Only if attempt 2 misses any gating band on any gating seed:** 4 flocks of 5/4/4/3 = 16 birds, one per route quarter, re-placed by bd-level under the A5 POI rules; attempt 2 tuning kept. If attempt 3 misses, B6 sky is recorded as FAIL with no further iteration or band widening | bd-engineer's diagnostics: losses are azimuth (40-60%) and range > 200 m (21-41%); above-frame is only 1.5-4% |

The record (`studio:decisions` / `amendment/birds-brief-R1`, final, stored 02:23:29Z; it replaces the 02:22Z text), verbatim:

> APPROVED R1 (final, lead-OK'd; replaces the 02:22Z text of this key): Bird brief R1, pre-B7 sky correction (2026-10-05). Per bd-producer's ruling it uses no B7 slot. Cause: B6 sky bands failed on all 3 gating seeds. bd-designer's calibrated model shows the limit is flocks sitting outside the walker's view cone in azimuth, and the 30 s gap band was an uncalibrated estimate. Band changes: (1) the B6 longest empty-sky gap band goes from ≤ 30 s to ≤ 45 s, matching wildlife; the target stays ≤ 20 s. (2) The first-flock sighting (the leading empty-sky gap) becomes GATING at ≤ 30 s (was non-gating at ≤ 20 s), so the walk can't open on an empty sky. Attempt 2, BirdTuning only: flock orbit radius 35-55 m (was 20-35); dwell 25-40 s at each POI (was 50-90); altitude band 25-50 m AGL (was 30-60), inside the B7 envelope, so the A2 gate tracks it at 20-60 m AGL and ≥ the crown top + 5 m. Attempt 3, only if attempt 2 misses any gating band on any gating seed: add a 4th flock, giving 4 flocks of 5/4/4/3 = 16 birds (inside the envelope of 2-4 flocks of 3-9 and ≤ 18 birds), with bd-level re-placing one flock per route quarter under the A5 POI rules; attempt 2 tuning is kept. Ladder: "Sky too empty" step 1 (POIs closer) is removed, because the model shows it has no effect. Unchanged: flock-in-view 30-65% and every other B6 band, including the swarm guards (max in view ≤ 20; ≥ 15 in view in ≤ 5% of samples); the camera, scan and visibility test; seeds 101/202/303; the B7 envelope; A1-A4; amendment/birds-brief-A5. Rejected: report-only sky bands (a spec change, which is the PM's to make), route-fitted or counter-walk flock tours (fitting the test), and any camera or visibility change. Stop condition: if attempt 3 misses on any gating seed, there is no further sky iteration and no further band widening. B6 sky is recorded as FAIL with its numbers, B7 goes to Noah as planned, and his sky rating plus the FAIL go to Noah's PM. (game-director)

## Approval

Approval received from bd-director (game-director) on 2026-10-05. The record of record is `studio:decisions` / `greenlight/birds-brief`, and this is the backup copy. Amendments A1 and A2 are applied in the 3.4 TARGETS table, A3 in the sources note at the top, and A4 in 3.2 and section 4. The final ruling, verbatim:

> APPROVED (final, supersedes both earlier same-day rulings): Bird density brief (B1, 2026-10-05), with amendments A1-A4, which are binding. The binding version is the AGL brief on disk; the HAC revision is withdrawn. Population: 16 robins in 7 patches (3 trail-edge patches with centres 4-6 m off the route, sized 3/2/2; 4 meadow patches 10-25 m off, sized 3/2/2/2) and 3 crow flocks of 6, 5 and 4 (15 birds). Everything is placed at load and persists, with seed 101 for World.unity and the B7 build. Robin alert is at 14 m and FID at 8 m; patch-mates within 8 m flush socially; a flushed robin climbs 4-10 m and lands 25-40 m away, ≥ 12 m from the route, or despawns out of view; at most 2 flushes per robin. Flocks orbit at a radius of 20-35 m over a clearing or ridge 50-150 m from the route, one per route third, and commute between 2 POIs every 50-90 s. Altitude is 30-60 m AGL and ≥ the crown top + 10 m. Flap when vertical speed > +0.3 m/s, glide otherwise. B6 gating bands for each of seeds 101/202/303 over 360 samples, with the foliage-aware visibility test gating: flock-in-view 30-65%; longest empty-sky gap ≤ 30 s; ground birds met 1.7-5.0/min; flushes 3-12 per walk, ≥ 40% of them seen; max birds in view 3-20; samples with ≥ 15 birds in view ≤ 5%; glide share 40-75%; flap rule obeyed in ≥ 95% of samples; flock spacing never < 1.5 m. A1: the FID band tracks the configured FID. Player-triggered flushes fire at FID -1.0/+0.5 m (7.0-8.5 m by default). Social flushes count as events but are excluded from the distance band. A2: the altitude band tracks the configured band. Every flock-bird sample must sit within [floor - 5, ceiling + 10] m AGL (25-70 m by default) and ≥ the crown top + 5 m, with absolute limits of 20-90 m AGL. A3: the (lit) citations are accepted for B1 because web tools were disabled (BD1). Their DOIs must be fetched and verified before the brief becomes the tuning baseline for the real game; this does not block Phase 1. A4: keep robins off the trail. Every robin's start point, hop target and landing spot is ≥ 3 m from the route polyline, and hops that would cross that line are rejected. B7 envelope: robins 8-26 in 4-10 patches of 1-4; flocks 2-4 of 3-9 with ≤ 18 flock birds in total; FID 6-12 m; altitude floor 25-45 m and ceiling 50-80 m AGL. Bands don't change without my re-approval. Robustness seeds 404-808 are non-gating, and the run is seed-fragile if 2 or more of them miss a band. A seed that can't satisfy the rules fails; the rules are never relaxed. (game-director)

Director's note: "Good brief. Adopting the foliage test up front, and calling out the pitch-0 sky bias, were the right calls."

**Reconciliation note (bd-designer).** Three rulings were issued on 2026-10-05 because messages crossed: rev 1 (the AGL draft), rev 2 (the HAC revision) and this final ruling, which supersedes both. The final ruling **keeps AGL altitude**; the HAC revision stays withdrawn, and A2's ≥ crown + 5 m floor covers the canopy concern. It also **adds A4** from that revision: trail-edge patch centres move from 2-6 m to 4-6 m, and every robin start, hop and landing is ≥ 3 m from the route.

### A5

The binding A5 is the lead's record, `studio:decisions` / `amendment/birds-brief-A5`. bd-director's earlier A5 (`greenlight/birds-brief-A5`) is void and now reads "superseded"; the director has no creative objection to the lead's A5. It is applied in 3.2, section 4 and Retunes R0. The record, verbatim:

> FINAL A5, lead ruling 2026-10-06 ~01:33Z, narrowed per bd-designer and superseding the earlier A5 text. Brief 3.2, "Flock orbit centre" clause (a) becomes: "clearing: no tree instance within 20 m, where 'tree' excludes Bush_* and Rock_* prototypes (the WildlifeRules convention)." Scope is the flock-POI clearing rule ONLY. Robin rules, including "nearest tree 6–30 m", keep their as-written reading with bushes counted. Foliage occlusion still counts bushes. Ridge/hilltop, the 50–150 m route band, ≥120 m between POIs, one primary per third, all B6 bands and the population are unchanged. This is a placement correction (R0), not a B7 retune. Condition: bd-level re-dry-runs 8/8 seeds with this narrower scope before placing.
