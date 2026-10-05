---
title: 'Wildlife density brief: Washed Ashore walk-around (Phase 0 / A1)'
owner: systems-designer
spec: _bmad-output/planning-artifacts/poc-spec-washed-ashore-wildlife.md
consumers: level-designer (A2, A3), gameplay-engineer (A4, A5, A6), technical-artist (A8), qa-lead (A6, A7)
created: '2026-10-05'
status: approved by game-director 2026-10-05T01:03:48-05:00
---

# Wildlife density brief

**Player problem:** a 3-minute walk on a 512 m x 512 m map (0.262 km²) should feel like wild country. That means animals turn up often enough that the land feels inhabited, but never so many that it reads as a petting zoo.
**Loop served:** the explore and walk loop. Wildlife is ambient presence and has no combat.

Source rules: every URL below was supplied by wl-research. **(weak)** marks a forum, wiki or mod-site source. **INFERRED** marks my own arithmetic or design judgement, not a sourced fact.

## 1. Real-world baseline

| Species | Density | Grouping | Source |
|---|---|---|---|
| Red deer | Scotland averages about 10/km²; at 5/km² or less, woodland regenerates | Females in grass-rich habitat in groups of up to 40; stags apart in spring and summer in small groups of 4-5; in the rut a stag holds 10-15 hinds | https://www.johnmuirtrust.org/resources/943-deer-management-faq-july-2021 , https://www.nature.scot/doc/deer-management-scotland-frequently-asked-questions-faqs , https://www.wildlifeonline.me.uk/animals/article/red-deer-behaviour-social-structure |
| White-tailed deer | Summer 1.5-9.7/km²; carrying capacity about 3-10/km² | Most common doe group is 4 (a doe, her yearling and 2 fawns); bucks in bachelor groups of 2 to 6 or more | https://www.esf.edu/aec/adks/mammals/wtd.php , https://www.deerfriendly.com/deer-population-control/Recommended-Deer-Density , https://pmc.ncbi.nlm.nih.gov/articles/PMC4668922/ |
| Red fox | Rural UK 0.16-1.2/km²; near villages 2.7-13.4/km² | Solitary or in pairs; family groups only at the den | https://www.wildlifeonline.me.uk/animals/article/red-fox-abundance-population , https://link.springer.com/article/10.1007/s13364-012-0074-0 , https://iere.org/how-big-is-a-foxs-territory/ (weak) |
| Grey wolf | About 4.4 wolves/100 km² (Italy) | Packs average about 5.9 (Montana); territories 80-3,000 km² | https://frontiersinzoology.biomedcentral.com/articles/10.1186/s12983-018-0281-x , https://fwp.mt.gov/binaries/content/assets/fwp/conservation/wolf/sells-et-al.-2022.-competition-prey-and-mortalities-influence-gray-wolf-group-size.pdf , https://animaldiversity.org/accounts/Canis_lupus/ |

Habitat: deer use forest edges, openings and brushy fields (esf.edu); red deer hinds hold the grass-rich ground (wildlifeonline).

**Scaled to 0.262 km² (INFERRED):** red deer about 1.3-2.6 animals, white-tailed deer about 0.4-2.5, fox about 0.04-0.3 rural, wolf about 0.01. Every species' home range is bigger than the whole map (red deer stag range 800 ha or more, https://www.wildlifeonline.me.uk/animals/article/red-deer-territory-home-range). The map is therefore **a slice of their range**, not a self-contained habitat. Strict realism gives "one small deer group, perhaps a fox, no wolves".

## 2. What games do

| Game | Technique | Source |
|---|---|---|
| Far Cry 3/4 | Only keeps NPCs active within 500 m of the player; "up to 20 animals" at once; spawns ahead of the player using heading and speed; "if the player didn't see it, it didn't happen" | https://www.gamedeveloper.com/programming/the-definition-of-artificial-insanity-the-systemic-ai-of-far-cry |
| Far Cry 5 | Hunting zones about 200 m across where a species may appear | https://farcry.fandom.com/wiki/Far_Cry_5_animals (weak) |
| RDR2 | Spawn areas near the player with only 1-2 of a set active; never spawns in view; spawning matched to habitat across biomes | https://gamefaqs.gamespot.com/boards/200179-red-dead-redemption-2/77210577 (weak), https://besjournals.onlinelibrary.wiley.com/doi/full/10.1002/pan3.10242 , https://gdconf.com/article/learn-how-rockstar-breathed-life-into-the-wildlife-of-red-dead-redemption-2-at-gdc/ |
| theHunter: Call of the Wild | A persistent simulated population; herds tied to "need zones" (feed, drink, rest); about 3,000-3,900 animals per reserve of about 64 km² | https://www.nexusmods.com/thehuntercallofthewild/articles/53 (weak), https://steamcommunity.com/app/518790/discussions/0/133259227510751032/ (weak) |
| The Long Dark | Wolves from fixed spawn points in the player's region, with deliberately varied respawn | https://steamcommunity.com/app/305620/discussions/0/4411921001879001241/ (weak), https://steamcommunity.com/app/305620/discussions/0/3041607080046090266/ (weak) |
| Horizon Zero Dawn | World-data maps (moisture, distance to river and so on) drive placement density | https://www.guerrilla-games.com/read/gpu-based-procedural-placement-in-horizon-zero-dawn |
| Minecraft | Spawn ring 24-128 blocks, instant despawn beyond 128 | https://minecraft.wiki/w/Simulation_distance (weak) |

Perceived density: no rigorous source exists. Players reject encounters "every few feet" (https://www.resetera.com/threads/whats-your-preferred-level-of-density-in-open-world-games.14032/ , weak), and good games alternate dense clusters with empty space (https://forum.quartertothree.com/t/open-world-encounter-density/132123/6 , weak).

**Conclusions (INFERRED):**
- theHunter runs about 47 animals/km² across 9 species, which is about 5/km² per species. Total density is roughly 5-10x real life, but per species it stays close to real deer numbers.
- Far Cry's 500 m bubble covers our whole map, so a spawn bubble buys nothing. **Use a persistent population placed by habitat (theHunter model)**, all placed at scene load before the first rendered frame, so nothing pops in where the player can see it. This POC has no respawn or despawn.
- Presence comes from **frequent sightings of a small, grouped population** placed at habitat features near where the player walks. It does not come from a large headcount.

## 3. Targets

### 3.1 Population (A3): 15 total, inside the outer bound of 6-40

| Species | Groups | Group size | Total | Rationale |
|---|---|---|---|---|
| Deer (hinds and young) | 2 herds | 4 each | 8 | A doe group of 4 is the modal size (PMC4668922). 2 herds is about 30/km², 3x the Scottish average, which matches the game-convention uplift (INFERRED) |
| Stag | 1 bachelor group | 3 | 3 | Stags live apart from hinds in small groups (wildlifeonline) |
| Fox | 2 solitary | 1 each | 2 | Solitary. About 7.6/km², at the village-edge real-world band (springer) |
| Wolf | 1 pair | 2 | 2 | **A visiting pair, not a resident population.** Real density rounds to 0 (INFERRED). The spec requires the species, and transient wolves are game convention (The Long Dark) |

Total of 15 sits inside wl-research's game-plausible band of 8-15 and under Far Cry's 20 active animals. Group cohesion (A3 check): every member stays within this radius of its group centroid while not fleeing: deer herd 12 m, stag group 15 m, wolf pair 10 m.

**Retune envelope for A7 (cannot be exceeded without director re-approval):** total 10-24; deer herds 1-3 of 3-5 each; stag groups 1-2 of 2-4 (one may be solitary); fox 1-3 solitary; wolf exactly 1 pair.

### 3.2 Habitat placement rules (A3, level-designer)

Each group gets an **anchor point**. Members spawn within the cohesion radius of their anchor and wander on a leash around it. Anchors are randomised per seed within these rules:

| Rule | Value |
|---|---|
| All anchors | On the NavMesh (`NavMesh.SamplePosition` within 2 m); ≥ 40 m from PlayerSpawn; ≥ 30 m from the terrain edge; ≤ 170 m from the terrain centre (256, 256), inside the edge rise; ≥ 50 m from every other anchor; **15–35 m perpendicular distance from the walk-route polyline (3.5)** (R1; was ≤ 60 m. The 15 m minimum is the director's addition, so nothing anchors on the trail itself). That last rule is the "place near the player's path" convention (Far Cry, RDR2) |
| Deer herd | Meadow: grass splat weight ≥ 0.6 and slope ≤ 20°, within 40 m of a tree instance (forest edge, per esf.edu); the 2 herd anchors are ≥ 100 m apart, which keeps the in-view count at 8 or fewer |
| Stag group | Slope ≤ 25°; ≥ 60 m from each deer herd anchor (the sexes live apart) |
| Fox | Within 25 m of a `Bush_Common`/`Bush_Common_Flowers` instance or a tree; slope ≤ 25° |
| Wolf pair | ≥ 100 m from PlayerSpawn; ≥ 60 m from each deer herd anchor; slope ≤ 30° |
| Wander leash (anchor to wander target) | Deer 20 m, stag 25 m, fox 30 m, wolf 35 m (R1; was 30/40/50/60). After a flee, the group returns at walk speed once calm |
| Route coverage (R0, see Retunes) | Split the closed route polyline (3.5) into **6 arcs of equal length** (about 147 m each, measured from W0). Each of the 6 group anchors is assigned to a **different arc**: its nearest point on the route must lie in that arc (the single nearest point on the polyline; an exact tie goes to the lower arc number). Arc-to-group assignment is shuffled per seed, except that the wolf pair may not take arc 1 or arc 6 (the arcs touching W0) |
| Arc adjacency (R1) | Adjacency is cyclic (arc 6 touches arc 1). The 2 deer arcs are not adjacent to each other, and the stag arc is not adjacent to either deer arc. **Each seed draws its assignment from the set of valid assignments** (those satisfying this rule and the wolf bar), so a seed never fails on combinatorics. If an anchor then fails geometrically, the generator draws another valid assignment (up to 20 draws) before the seed fails |
| Approach sightline (R1) | From at least one route point 40–80 m **before** the anchor's nearest route point (in walk direction, sampled every 5 m), a ray from ground + 1.65 m to the anchor's ground + 1.0 m is unobstructed by terrain or tree colliders |
| Seeds | A6 uses seeds **101, 202, 303**. Each seed changes the anchors and wander, never the rules or counts. **The authored World.unity and the A7 Windows build use seed 101**, so the A3 eval and Noah's feel check look at a population A6 has measured |

If a seed can't satisfy every rule after 200 attempts per anchor, log it as an error and fail the run. Don't relax the rules silently.

### 3.3 Behaviour tuning (A4, A5, gameplay-engineer)

All values are serialized fields with these defaults. Speeds are ground speed in m/s, and animations must match them (A4).

| Param | Deer | Stag | Fox | Wolf |
|---|---|---|---|---|
| Walk speed | 1.3 | 1.4 | 1.2 | 1.5 |
| Run / retreat speed | 9.0 | 9.0 | 7.0 | 6.0 (trot) |
| Idle or graze bout | 4-12 s | 4-12 s | 3-8 s | 5-15 s |
| Wander leg length | 5-20 m | 8-25 m | 10-30 m | 15-40 m |
| Share of time idle or grazing when undisturbed | ~60% | ~60% | ~40% | ~50% |
| **Alert distance** (stop, face the player, no grazing) | 50 m | 55 m | 35 m | n/a |
| **Flee trigger distance** | 35 m | 40 m | 25 m | n/a |
| Flee ends when the player is ≥ | 70 m away, or after 6 s (R0; was 10 s) | 75 m / 6 s (R0; was 10 s) | 50 m / 5 s (R0; was 8 s) | n/a |
| **Keep-distance minimum** (retreat when the player is closer) | n/a | n/a | n/a | 45 m |
| Retreat ends when the player is ≥ | n/a | n/a | n/a | 60 m (then stops, faces the player) |
| Calm-down before resuming wander | 6 s | 6 s | 4 s | 4 s |
| NavMeshAgent `angularSpeed` (WL-BUG-7 fix) | 600°/s (was 300) | 600°/s (was 300) | 600°/s (was 300) | 300°/s (unchanged) |
| `modelYawOffset` (fix value, not tuning) | 180° | 180° | 180° | 180° |

- **WL-BUG-7 (a bug-fix tuning value, recorded per wl-producer; not a response to a rating, so no A7 slot is spent):** at 300°/s, the 180° pivot at flee onset took about 0.6 s and prey visibly shuffled sideways, so prey `angularSpeed` goes to 600°/s for deer, stag and fox; this is confirmed in WildlifeTuning.asset by wl-qa. `modelYawOffset` = 180° on all 4 species is a fix value, not a tuning value: the Quaternius models face −Z. Flee distances, caps and A6 bands are unchanged. wl-director's ruling on the feel is pending, and it will be recorded here.

- **The herd flees as one:** when any member triggers, every member of the group flees. The flee direction is away from the player, picked with `NavMesh.SamplePosition`, with a ±30° jitter per member.
- Flee and retreat speeds all beat the player's 5.0 m/s walk speed (controller-tuning.md), so the A5 test, which walks the player at the group, must show the distance growing.
- **Sources for the thresholds.**
  - Wolves moved away from an approaching human at 17-310 m, mean 106 m (https://cdnsciencepub.com/doi/10.1139/Z07-099, primary; supported by https://www.frontiersin.org/journals/ecology-and-evolution/articles/10.3389/fevo.2022.792916/full).
  - White-tailed deer alert and flight distances plateau near 250 m and 750 m in unhabituated areas (https://digitalcommons.usu.edu/cgi/viewcontent.cgi?article=1031&context=wdmconference).
  - Red deer flee earlier in hunting season and after repeated approaches (https://esajournals.onlinelibrary.wiley.com/doi/10.1002/ecs2.4281).
  - No fox study was found (a gap).
  - **The 25-55 m values are INFERRED design choices.** They keep fleeing animals inside the 80 m visibility radius, so the player sees the flight. The wolf's real mean of 106 m is beyond 80 m, so wolves would never be visible. 45 m sits inside the observed 17-310 m range.
- **Speed sources.**
  - Real top speeds are about 13 m/s for white-tailed deer (https://www.speedofanimals.com/animals/white_tailed_deer, community) and about 14 m/s for red fox (https://en.wikipedia.org/wiki/Fastest_animals, community).
  - Wolves hold 56-64 km/h over several km (https://en.wikipedia.org/wiki/Hunting_behavior_of_gray_wolves, community).
  - The run speeds above are sustained flee speeds below these bursts (INFERRED). The wolf deliberately trots rather than bolts.

### 3.4 Sighting targets (A6), the pass bands

**Definitions (gameplay-engineer implements these exactly):**
- Sample every 0.5 s of game time for 180 s, giving **360 samples** per run. t = 0 is 1.0 s after the player is grounded at spawn.
- An animal is **visible** in a sample when all of these hold:
  - its renderer bounds intersect the camera frustum (`GeometryUtility.TestPlanesAABB`);
  - the distance from the camera to the bounds centre is ≤ 80 m;
  - at least one of two raycasts, from the camera to the bounds centre and to the top-centre, reaches the animal unblocked. The occluder mask is the terrain (including tree colliders) plus Default, and excludes animals and the player.
- A **gap** is a maximal run of consecutive samples with 0 visible animals, measured as its count × 0.5 s. The leading gap, from the start of the run to the first sighting, and the trailing gap both count.

**Pass bands. Every one of the 3 runs (seeds 101, 202, 303) must land inside every band:**

| Metric | Target | Pass band |
|---|---|---|
| Sighting rate (% of samples with ≥ 1 visible) | 40% | **25% - 55%** |
| Longest gap | ≤ 30 s | **≤ 45 s** |
| First sighting (leading gap) | ≤ 20 s | **≤ 30 s** |
| Max animals in view at once | 4-6 | **3 - 8** (the lower bound proves a group reads as a group) |
| Samples with ≥ 5 visible ("busy" guard) | ≤ 5% | **≤ 10%** |
| Per-species sighting (Deer, Stag, Fox, Wolf) | each seen | **Each species visible in ≥ 2 consecutive samples (≥ 1 s)** |

Why these numbers (INFERRED): at 25-55%, the player has animals in view for roughly 45-100 s of the 180 s walk, split into several episodes, so the walk alternates presence with empty land, as the BotW and density threads suggest. A gap of 45 s or less means at most about 225 m of walking without seeing anything. A pre-placement estimate (FOV 70° vertical, about 100° horizontal at 16:9, an 80 m cone of about 5,700 m², 6 groups inside a usable interior of about 70,000 m², and roughly 30% tree occlusion) predicts about 30-40%, which sits in the band.

### 3.5 Fixed walk route

Offsets are (dx, dz) in metres from PlayerSpawn (world 205, 205; yaw 45°). The waypoints form a closed loop of about 880 m straight-line. All of them stay ≤ 145 m from the terrain centre, inside the edge rise.

| WP | dx | dz | Leg length (m) |
|---|---|---|---|
| W0 | 0 | 0 | (start, PlayerSpawn) |
| W1 | +40 | +40 | 56.6 (along the clear lane) |
| W2 | +120 | +30 | 80.6 |
| W3 | +170 | +100 | 86.0 |
| W4 | +110 | +170 | 92.2 |
| W5 | +30 | +180 | 80.6 |
| W6 | -40 | +120 | 92.2 |
| W7 | -50 | +30 | 90.6 |
| W8 | +20 | -40 | 99.0 |
| W9 | +110 | -30 | 90.6 |
| W10 = W0 | 0 | 0 | 114.0 (closes the loop) |

**Walker rules:**
- The player moves through `CharacterController.Move` at walkSpeed 5.0 m/s and follows the corners of `NavMesh.CalculatePath` between consecutive waypoints, so trees can't stall it. If the loop finishes before 180 s, it continues from W1.
- Camera yaw = travel heading + 25° × sin(2π·t / 10 s), a scan that models a player looking around. Pitch stays fixed at 0°.
- level-designer must check every waypoint with `NavMesh.SamplePosition` within 3 m after the bake. If one is off-mesh, nudge it by ≤ 10 m and record the new offset in this brief (in a "Route adjustments" note). The route is then frozen for all A6 runs and retunes.

### 3.6 Output file

The output file is `TestResults/wildlife-sightings.json`, with one entry per run:
`seed, samples (360), sightingRate, longestGapSec, firstSightingSec, maxVisible, pctSamplesGe5, perSpeciesMaxConsecutive {Deer, Stag, Fox, Wolf}, population {species: count}, groups [{species, size, anchor}], pass (bool per band)`, plus a per-sample array of the visible count and species.

## 4. Which species fit

**Wild only: Deer, Stag, Fox, Wolf.** The setting is an uninhabited temperate pine and broadleaf hill country. WorldBuilder places Pine, CommonTree, DeadTree, grass meadows and rock, with no buildings, fences or roads. Cow, Bull, Donkey, Alpaca and Horse need pasture, fences or a farmstead to read as believable. Husky and Shiba Inu imply an owner. With no human structures, any of them would break the "wild country" question this POC asks, so all are excluded.

## 5. A7 retune ladder (at most 2 retunes, all inside the 3.1 envelope)

- **"Too empty":** first add 1 fox (to 3). Next, add 1 deer herd of 4, or grow both herds to 5. Last, cut the alert and flee distances by 20%.
- **"Too busy":** first shrink the deer herds to 3. Next, remove 1 deer herd. Last, raise the flee distances by 20%.

The A6 bands stay the same unless game-director re-approves new ones. A retune that pushes any band out of range forces a re-run and is recorded below.

## Retunes

### Pre-A7 tuning, R0: placement correction (2026-10-05, from A6 run 2; iteration 1 of the producer's 2-iteration timebox)

| Value | Before | After | Reason |
|---|---|---|---|
| Route-coverage rule (3.2) | none | 6 equal arcs, one group anchor per arc, the wolf not in arcs 1 or 6 | Groups clumped on part of the loop on seeds 101 and 303 |
| Deer flee time cap (3.3) | 10 s | 6 s | Long flees carried deer out of the 80 m visibility radius |
| Stag flee time cap (3.3) | 10 s | 6 s | Same |
| Fox flee time cap (3.3) | 8 s | 5 s | Same |

This is not an A7 retune and doesn't use either of the 2 A7 slots: Noah hasn't rated anything yet. The population, the flee trigger distances and the A6 bands are all **unchanged**.

- **Evidence (wl-engineer, A6 run 2):**
  - seed 101: rate 18.9%, wolf never seen
  - seed 202: rate 38.1%, PASS
  - seed 303: rate 18.1%, gap 63 s
  - The timelines show long empty stretches. On 101 and 303 the 6 groups bunched onto part of the loop, which left legs with no group within 80 m. Long flees also carried deer out of the 80 m radius.
- **Diagnosis:** the "≤ 60 m from the route" rule controls distance to the route but not distribution along it. A population of the size the director approved fails only on seeds where it clumps, and 202, the evenly spread seed, passes in the middle of the band.
- **Changes:**
  1. Placement: the route-coverage rule in 3.2 (6 equal arcs, one group per arc, no wolf in the arcs touching W0).
  2. Behaviour: the flee time cap goes from 10 s to 6 s for deer and stag, and from 8 s to 5 s for fox. The distance end-conditions are unchanged.
- **Kept in reserve for A7:** the section 5 ladder (+1 fox, a deer herd, a 20% cut to the flee distances). The population stays at 15.
- **Release:** wl-producer released R0 as inside the approved envelope, and wl-director then approved it. wl-engineer implements the route-coverage rule in WildlifeRules.cs; wl-level reviews it by reading only.
- **Director approval:** quoted verbatim under "## Approval", R0.
- **Slot rule if R0 fails (wl-producer's scope ruling; every change still needs wl-director's ruling before release):**
  - A section 5 ladder step (a change to headcount or herd size) **spends an A7 slot**.
  - A placement or behaviour correction at the same headcount is **pre-A7 iteration 2** and spends no slot.
  - After iteration 2, failures escalate to wl-producer and wl-director.

### Pre-A7 tuning, R1: sightline placement (APPROVED 2026-10-05, iteration 2 of 2, the last pre-A7 iteration; ruling verbatim under "## Approval", R1)

**Evidence (wl-engineer, R0 run, TestResults/wildlife-sightings.json):**

| Seed | Visible | In frame ≤ 80 m (before raycasts) | In range ≤ 80 m (any direction) | Longest gap | Result |
|---|---|---|---|---|---|
| 101 | 46.1% | 83.9% | 100% | 22.5 s | PASS (reproduced at 47.5% and 46.1%) |
| 202 | 16.7% | 43.1% | 90.8% | 49.5 s | FAIL |
| 303 | 14.7% | 26.4% | 66.4% | 61.5 s | FAIL (about 25% of samples have nothing within 80 m) |

**Diagnosis:** R0 fixed coverage along the route, and 202 now has an animal within 80 m 91% of the time. The loss now has three parts:
- **Occlusion:** the animal is in frame but behind trees. The deer "≤ 40 m of a tree" rule and the fox cover rule put anchors behind trunks.
- **Framing:** the animal is in range but to the side of or behind the camera. Anchors up to 60 m off the route are mostly seen side-on.
- **Leash drift:** on 303, a 50-60 m wander leash on top of a 60 m route offset lets groups drift beyond 80 m.

The measurement in 3.4 and the camera scan in 3.5 stay unchanged, because changing them would be fitting the test.

| Value | Before | After | Reason |
|---|---|---|---|
| Min anchor distance from route (3.2, new, director's addition) | none | 15 m | No group anchors on the trail itself, which would read as staged |
| Max anchor distance from route (3.2, `maxFromRoute`) | 60 m | 35 m | Fewer trees between the path and the animal; more of the walk sees it ahead. Tighter than the approved ≤ 60 m, so it stays inside the approval |
| Wander leash (3.2) | deer 30 / stag 40 / fox 50 / wolf 60 m | deer 20 / stag 25 / fox 30 / wolf 35 m | Keeps anchor plus leash at ≤ 70 m from the route, inside the 80 m radius |
| **Approach-sightline rule** (3.2, new, needs code) | none | From at least one route point 40-80 m **before** the anchor's nearest route point, in walk direction, a ray from eye height (+1.65 m) to the anchor (+1.0 m) is unobstructed by terrain and tree colliders | Places animals where an approaching walker can see them ahead. This is the Far Cry "spawn ahead of the player using heading" convention (gamedeveloper.com source in section 2), and it targets both occlusion and framing |
| Arc adjacency (3.2, new) | none | The 2 deer herd arcs are not adjacent to each other, and the stag arc is not adjacent to either deer arc. Adjacency is cyclic, so arc 6 touches arc 1 | Groups now sit closer to the route, so this keeps a herd of 4 and the stag group of 3 from showing together (the "≥ 5 visible ≤ 10%" guard and the 8 cap). It also matches the sexes living apart |

**Robustness (wl-producer: design for any seed, not these 3).** Results swung widely between seeds: 202 went from 38% to 17%, and 101 from 19% to 46%. R1 therefore changes rules that hold for every placement, namely line of sight, distance to the route and leash. It doesn't move any particular anchor.
- **Feasibility.** wl-level's R0 per-arc probe (4 m cells, Deer/Stag/Fox/Wolf) found every arc can host every species. The wolf on arc 5 is thin at 46 cells, and the 35 m limit and the sightline rule will shrink it further.
- **Retry order.** Arc assignments are drawn only from the valid set (director's condition). If an anchor fails 200 attempts, the generator draws another valid assignment, up to 20 draws, using the same seed stream. Only then does the seed fail. No rule is relaxed.
- **Ceiling watch (director).** Seed 101 passed at 46% before R1, and moving groups closer could push it past 55% or past the "≥ 5 visible" guard. If that happens, it is a FAIL like any other.
- **Known limitation (wl-level, from the prefab files):** tree colliders are trunk capsules only (for example Pine_1 has radius 0.35 m), and bushes have no collider. So the R1 sightline ray and the A6 visibility raycasts both see through canopies, low pine branches and bushes. A6 is consistent but **optimistic** compared with what Noah sees on screen. If A7 comes back "too empty" while A6 passes, this is the first hypothesis. The candidate fix is to reject a ray that passes within about 2 m of a tree instance below canopy height. It is **not adopted** in R1: the visibility test is frozen by the director, and changing it now would move the target mid-iteration.
- **If R1 fails:** the bands will not be widened. The options are an A7 ladder step (spends a slot) or recording a FAIL with its reason. wl-director owns that call.
- **Robustness check (diagnostic, not gating).** After the 3 gating seeds, A6 also runs seeds 404, 505, 606, 707 and 808, reported separately in the JSON. These don't change the A6 verdict. If 2 or more of the 5 miss a band, I report R1 as seed-fragile to wl-producer and wl-director, even if 101, 202 and 303 pass.

**Unchanged:** the population of 15 and its grouping, the flee triggers (35/40/25 m), the wolf keep-distance (45 m), the flee caps from R0, the habitat rules other than distance to route, the A6 bands, the visibility definition, the camera scan, and seeds 101/202/303. Section 5 ladder items such as the −20% flee distances are **not** used, so this spends no A7 slot. If a seed can't satisfy the rules, it fails the run; the rules aren't relaxed.

**R1 result** (wl-engineer, run 02:36–02:47; WildlifeRules.cs aff44298…, reviewed PASS by wl-level; `TestResults/wildlife-sightings.json`):

| Seed | Rate | Longest gap | First | Max | ≥ 5 visible | Species | Foliage rate (non-gating) | Verdict |
|---|---|---|---|---|---|---|---|---|
| 101 (gating, shipped in builds) | 34.2% | 22.5 s | 3.5 s | 4 | 0% | all | 26.7% | PASS |
| 202 (gating) | 32.8% | 29 s | 4 s | 4 | 0% | all | 23.9% | PASS |
| 303 (gating) | 33.6% | 28 s | 2 s | 6 | 1.1% | all | 25.0% | PASS |
| 404 (robustness) | 37.8% | | | | | | | in band |
| 505 (robustness; R1-A draw 2) | 37.5% | | | | | | | in band |
| 606 (robustness; R1-A draw 2) | 23.1% | | | | | | | misses the 25% floor only |
| 707 (robustness) | 34.2% | | | | | | | in band |
| 808 (robustness) | 46.7% | | | | | | | in band |

- **A6: all 3 gating seeds pass every band.** All three sit near the 40% target and well clear of the 55% ceiling and the busy guard.
- **Robustness:** 1 of the 5 seeds misses a band, below the threshold of 2 that would mark R1 seed-fragile. **R1 is not seed-fragile.**
- **A7 risk (INFERRED):** the canopy-aware rate runs about 8 points lower (24-27%), which puts what is actually on screen at the 25% floor. wl-director's advance guidance, recorded before any ruling:
  - **Primary proposal:** apply the foliage-aware ray rejection (a ray is blocked within about 2 m of a tree instance below canopy height) to R1's **approach-sightline placement rule**. That moves animals to where they are actually visible. Applying it to the A6 measurement alone would change nothing Noah sees.
  - **Alternative:** section 5 ladder step 1 (+1 fox, to 3).
  - Each option is presented with its foliage-aware rate.
  - **Slot rule (wl-producer's ruling):** once Noah has rated the walk, **any** change made in response spends 1 of the 2 A7 slots and requires an A6 re-run. That includes the foliage-aware placement change. The pre-A7 exemption ended with R1.

## Approval

Approval received from wl-director (game-director) and recorded 2026-10-05T01:03:48-05:00. The A1 approval record of record is `studio:decisions` / `wildlife/brief-approval` (stored verbatim by main, 2026-10-05T06:03:42Z; qa confirmed it is byte-identical to the quote below). This section is the backup copy. The ruling, verbatim:

> APPROVED: Wildlife density brief (2026-10-05). Species are wild only: Deer, Stag, Fox, Wolf. Population is 15: 2 deer herds of 4, 1 stag group of 3, 2 solitary foxes and 1 visiting wolf pair, placed by habitat at load and kept for the whole session, with anchors ≥ 40 m from spawn and ≤ 60 m from the route. Flee distances: deer 35 m, stag 40 m, fox 25 m; wolves keep ≥ 45 m. A6 pass bands: each of seeds 101, 202 and 303 must, over 360 samples, show a sighting rate of 25–55%, a longest gap ≤ 45 s, a first sighting ≤ 30 s, max in view 3–8, samples with ≥ 5 visible ≤ 10%, and each species visible in ≥ 2 consecutive samples. A7 retune envelope: total 10–24 with exactly 1 wolf pair; the bands don't change without my re-approval. (game-director)

Director's non-blocking note: name the seed for the authored World.unity and the A7 build. Applied: seed 101 (section 3.2).

### R0 (pre-A7 placement correction)

Approved by wl-director on 2026-10-05. The changes are recorded in sections 3.2 and 3.3 and under "## Retunes", R0. The ruling, verbatim:

> APPROVED R0: Wildlife brief revision R0 (2026-10-05). It is a pre-A7 placement correction and does not use an A7 slot. Each of the 6 group anchors goes in a different one of 6 equal-length arcs of the route, shuffled per seed, and the wolf pair may not take the 2 arcs touching W0. The flee time cap is cut to 6 s for deer and stag and to 5 s for fox; the distance end-conditions stay as they were. Unchanged: the population of 15 (2 deer herds of 4, 1 stag group of 3, 2 foxes, 1 wolf pair), the flee triggers (35/40/25 m), the wolf keep-distance (45 m), every A6 band, and seeds 101, 202 and 303. Conditions: all 3 seeds are re-run, 202 included; the seeds may not be swapped; and a seed that can't satisfy every placement rule fails the run rather than having the rules relaxed. (game-director)

### R1 (pre-A7 iteration 2 of 2)

Approved by wl-director on 2026-10-05. The changes are recorded in section 3.2 and under "## Retunes", R1. The ruling, verbatim:

> APPROVED R1: Wildlife brief revision R1 (2026-10-05). It is pre-A7 iteration 2 of 2 and does not use an A7 slot. Group anchors sit 15–35 m from the route; the 15 m minimum is my addition, so no animal anchors on the trail itself. Wander leashes are cut to deer 20 m, stag 25 m, fox 30 m and wolf 35 m. New approach-sightline rule: from at least one route point 40–80 m before the anchor in walk direction, a ray from eye height (+1.65 m) to the anchor (+1.0 m) is unobstructed by terrain or tree colliders. New arc-adjacency rule (cyclic): the 2 deer arcs are not adjacent to each other, and the stag arc is not adjacent to either deer arc. Arcs are assigned by drawing one of the assignments that satisfies the rule; a seed must never fail because of how arcs were combined. Unchanged: the population of 15 and its grouping, the flee triggers (35/40/25 m), the wolf keep-distance (45 m), the R0 flee caps, the other habitat rules, every A6 band, the visibility test, the camera scan, and seeds 101, 202 and 303. Conditions: all 3 seeds are re-run; the seeds may not be swapped; and a seed that can't satisfy every geometric rule fails the run rather than having the rules relaxed. (game-director)

### R1-A (amendment to R1: redraws and the robustness batch)

Approved by wl-director on 2026-10-05. The ruling, verbatim:

> APPROVED R1-A: Amendment to wildlife revision R1 (2026-10-05). R1 stands as approved, including the 15–35 m route offset and drawing arcs only from valid assignments. (1) If an anchor fails its 200 placement attempts, the arc assignment is redrawn from the valid assignments, up to 20 times, before the seed fails. Every redraw uses the seed's own RNG, so a given seed always gives the same result, and no placement rule is relaxed. Each redraw is logged in wildlife-sightings.json: the redraw count, which anchor failed on which arc, and the final assignment. If all 20 redraws fail, the seed fails. (2) Seeds 404, 505, 606, 707 and 808 run as a non-gating robustness batch alongside the gating run and are reported separately. If 2 or more of the 5 miss any band, R1 is reported as seed-fragile to wl-director and wl-producer even if 101, 202 and 303 pass. (game-director)
