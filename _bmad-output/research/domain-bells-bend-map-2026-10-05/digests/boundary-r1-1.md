# Digest: north land boundary (boundary-r1-1)

Dimension: how to stop the player going north at an east–west line. Accessed 2026-10-05. Sources retrieved this run: 10 (S1–S10). Confidence is high when a primary or official page was fetched and quoted, medium when only a search snippet or fan wiki was seen, and low when it rests on a single forum or community post.

Sources:
- S1: Gareth Griffiths, "Defining Boundaries: Creating Credible Obstacles In Games", Game Developer, 2008-07-01. https://www.gamedeveloper.com/design/defining-boundaries-creating-credible-obstacles-in-games (fetched)
- S2: "Invisible wall", Wikipedia, no date (live page). https://en.wikipedia.org/wiki/Invisible_wall (fetched)
- S3: "Boundary", Battlefield Wiki (Fandom), no date. https://battlefield.fandom.com/wiki/Boundary (search snippet only)
- S4: "Crater Edge", Subnautica Wiki (Fandom), no date. https://subnautica.fandom.com/wiki/Crater_Edge (search snippet only)
- S5: "Debug Plains", DayZ Wiki (Fandom), no date. https://dayz.fandom.com/wiki/Debug_Plains (search snippet only)
- S6: "Character Controller component reference", Unity 6 Manual, Unity Technologies, no date. https://docs.unity3d.com/6000.0/Documentation/Manual/class-CharacterController.html (fetched)
- S7: "Authority", Netcode for GameObjects 2.7 manual, Unity Technologies, no date. https://docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/terms-concepts/authority.html (fetched)
- S8: "Paint holes in the terrain", Unity Manual, Unity Technologies, no date. https://docs.unity3d.com/Manual/terrain-PaintHoles.html (search snippet only)
- S9: division.zone, the "Dark Zone" page (fetched) and "Update 1.6 in Detail: Dark Zone Revamp…", 2017-01-21 (search snippet only). https://division.zone/zones/dark-zone/ and https://division.zone/2017/01/21/update-1-6-in-detail-dark-zone-revamp-legendary-difficulty-exotics-and-premium-shop/
- S10: "UnityTest attribute", Unity Test Framework 1.4 manual, Unity Technologies, no date. https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-attribute-unitytest.html (fetched)

## Claims

| # | Claim | Source | Publisher | pub_date | accessed | Confidence | Class |
|---|---|---|---|---|---|---|---|
| C1 | Invisible walls "fail both the visibility and affordance rules completely". The cited case is Battlefield 2, where tree lines that look the same as passable ones turn out to be impassable. | S1 | Game Developer | 2008-07-01 | 2026-10-05 | high | design critique (practitioner) |
| C2 | A barrier that looks the same as one the player can cross elsewhere confuses players. The cited case is Call of Duty 4 fences that are sometimes jumpable and sometimes a boundary. | S1 | Game Developer | 2008-07-01 | 2026-10-05 | high | design critique |
| C3 | Natural barriers that fit the world's logic are recommended. The cited example is the earthquake chasms in Gears of War. Boundaries must stay consistent with the game's rules and with real-world expectations. | S1 | Game Developer | 2008-07-01 | 2026-10-05 | high | design guidance |
| C4 | Most players accept that they can't leave the playable area. Immersion breaks most when a small obstacle (a knee-high fence) blocks a character who can normally jump it. | S2 | Wikipedia | n/a | 2026-10-05 | medium | reception (secondary) |
| C5 | Some games replace walls with lethal threats. Mercenaries: Playground of Destruction uses "restricted areas subject to lethal airstrikes". | S2 | Wikipedia | n/a | 2026-10-05 | medium | example |
| C6 | Glitches and cheats can get players through invisible walls into unused or empty level space. | S2 | Wikipedia | n/a | 2026-10-05 | medium | robustness |
| C7 | In Battlefield, crossing the map boundary starts a countdown of 3 to 15 seconds (it varies by game) with an on-screen warning. If the countdown runs out, the player dies and can't be revived. In Battlefield 1 the screen also turns black and white, an announcer says "return to the combat area", and the player is executed for desertion. | S3 | Battlefield Wiki (Fandom) | n/a | 2026-10-05 | medium | example (soft boundary) |
| C8 | In Battlefield 1, a player on a stationary gun when the countdown ends takes no damage (the gun takes it), and this was exploited. Edge cases in a soft boundary need testing. | S3 | Battlefield Wiki (Fandom) | n/a | 2026-10-05 | medium | robustness (exploit) |
| C9 | Battlefield 6 raised the out-of-bounds timer for vehicles on one map (Manhattan Bridge) from 5 to 10 seconds. Vehicles need different tuning. | S3 (search result set) | Battlefield Wiki / press snippet | n/a | 2026-10-05 | low | example (vehicles) |
| C10 | Subnautica's Crater Edge ("Void") is a border zone with no resources. Entering it spawns Ghost Leviathans, and more spawn the longer the player stays. They retreat when the player returns. Past 8,192 m the player and any piloted vehicle are teleported back near the map centre as a final backstop. | S4 | Subnautica Wiki (Fandom) | n/a | 2026-10-05 | medium | example (layered: hazard plus hard teleport) |
| C11 | DayZ has no invisible wall at the edge of Chernarus. Past the 15.36 km map the player walks onto procedurally generated, empty "Debug Plains" with no loot and no penalty. | S5 | DayZ Wiki (Fandom) | n/a | 2026-10-05 | medium | example (no boundary) |
| C12 | In The Division, the Dark Zone is a walled quarantine zone. Players enter over marked wall sections or through gated checkpoints (decontamination chambers) and can leave only through the checkpoints. | S9 | division.zone | n/a | 2026-10-05 | high | example (diegetic quarantine wall) |
| C13 | The Division's Update 1.6 expanded the Dark Zone north with three new areas (DZ07–DZ09) around Central Park, nearly doubling its playable size. This is a shipped case of a walled, diegetic boundary being moved in a later update. | S9 (Update 1.6 article, snippet) | division.zone | 2017-01-21 | 2026-10-05 | medium | example (expansion) |
| C14 | A Unity CharacterController "does not react to forces on its own" and "can not be affected by objects through physics". Two colliders can overlap by up to the Skin Width. Unity recommends a Skin Width above 10% of the radius so the character doesn't get stuck. | S6 | Unity | n/a | 2026-10-05 | high | technical (official) |
| C15 | The CharacterController Slope Limit stops the character climbing slopes steeper than the set value. Step Offset sets the tallest step it climbs (0.1–0.4 m for a 2 m human). | S6 | Unity | n/a | 2026-10-05 | high | technical (official) |
| C16 | Server authority is used by performance-sensitive and competitive games, such as FPS games, where "having a central server authority is necessary to minimize cheating and the effects of bad actors". The page gives no specific guidance on validating movement. | S7 | Unity | n/a | 2026-10-05 | high | technical (official) |
| C17 | Unity removes painted terrain holes from the Terrain Collider automatically. In URP, Terrain Holes must be enabled in the URP Asset, or the holes disappear in builds. | S8 | Unity | n/a | 2026-10-05 | medium | technical (official, snippet) |
| C18 | In Play Mode, a `[UnityTest]` runs as a coroutine, so tests can `yield return null`, `WaitForFixedUpdate` or `WaitForSeconds` to simulate frames and physics steps before asserting. | S10 | Unity | n/a | 2026-10-05 | high | technical (official) |

## Techniques table

"Immersion" means fit with a grounded, non-supernatural setting. "Robustness" means resistance to slipping past on its own, without a backstop.

| Technique | Example games (evidence) | Immersion | Robustness alone | Expandable later? |
|---|---|---|---|---|
| Invisible wall (hidden collider) | Battlefield 2 tree lines (C1); a common map edge (C4) | Low. Fails visibility and affordance (C1). Acceptable if the player can see why (C4). | Medium. Glitches still get through (C6). | Yes, trivially (delete or move it) |
| Soft boundary: warning, then countdown, then death or turn-around | Battlefield series (C7, C8); vehicle tuning in BF6 (C9) | Low to medium. A HUD warning is gamey. BF1 framed it as desertion. | Medium. Edge-case exploits exist (C8). Needs separate vehicle timing (C9). | Yes (move the volume) |
| Lethal hazard or threat zone | Mercenaries airstrikes (C5); Subnautica Ghost Leviathans (C10); RDR2 Guarma "invisible sniper" (Leads, unverified) | Medium to high when the hazard fits the fiction (a minefield, a contaminated zone). Invisible killers break immersion. | Medium. It deters, but a determined player can still try. Needs a backstop (Subnautica has one: the teleport). | Yes (the hazard can be cleared in a story update) |
| Nothing out there (empty terrain, no penalty) | DayZ Debug Plains (C11) | Medium. No wall, but the empty world shows. | None as a block. The player really leaves the authored map. | Yes, but you'd be building onto an exposed void |
| Diegetic built barrier (wall, fence, checkpoint gate) | The Division quarantine walls and checkpoints (C12, C13) | High in a grounded post-collapse setting | High when paired with a collider. Must be visibly unclimbable and consistent (C2, C4). | Yes. The Division moved its Dark Zone boundary north in Update 1.6 (C13). A gate can "open". |
| Natural impassable terrain (cliff, chasm, ravine, river cut) | Gears of War chasms (C3); The Long Dark cliffs (unverified, Leads) | High. "Credible obstacle" (C3). | Medium to high. A CharacterController can't climb slopes above its Slope Limit (C15), but terrain seams and holes leak (C17). | Harder. Needs a bridge or ramp added in an update. |

Fit for this game: the diegetic built barrier (washed-out road or collapsed bridge, plus a fence, floodwall or checkpoint) and natural terrain fit "familiar and grounded" best. A hazard zone fits only if its cause is grounded (contamination, flooding). Avoid invisible killers and HUD countdowns as the main mechanism. Keep them, if at all, as a silent backstop. This is an inference from C1–C5 and C12, not a sourced verdict.

## Recommended layered design

This is an inference that synthesises C1–C18. Exact values are suggestions to tune, not sourced figures.

1. **Layer A: diegetic barrier at about z = Zline − 10 to 30 m.** Build a continuous, visibly unclimbable structure across the full east–west width, in one consistent visual language (C2). For example, a flood-scoured ravine or washed-out embankment backed by a chain-link or concrete floodwall with razor wire, and one blocked checkpoint or collapsed road bridge where the main road meets the line. The checkpoint is the future progression gate. Model it as a prefab with a `Closed` state now and an `Open` state for later, the way The Division moved its walled Dark Zone (C12, C13). Never let a low obstacle be the boundary (C4).
2. **Layer B: hard collider backstop just north of Layer A.** Place an invisible box collider wall (or a few overlapping ones) spanning the full width plus overlap into the water on both ends. Make it thick (≥ 2 m, well above the CharacterController skin width, C14) and tall (well above the maximum jump or terrain height). Keep it on a dedicated `WorldBounds` layer. Don't paint terrain holes within a few metres of the line, because holes are cut from the collider (C17). If URP holes are used elsewhere, enable them in the URP Asset (C17).
3. **Layer C: authoritative position clamp.** On the server (or host), each tick: if a player or vehicle position has z > Zline (allowing for the agreed axis), snap it back to the last valid position south of the line and log an anomaly. This catches tunnelling, teleport glitches and modified clients. Server authority exists for exactly this reason (C16). Unity documents no ready-made validator (C16), so it must be custom. Subnautica's teleport is the shipped precedent for this kind of last-resort reset (C10).
4. **Layer D (optional): soft, in-world warning.** As the player nears Layer A, show diegetic cues: signage ("FLOOD ZONE – NO ENTRY"), a radio burst, or a guard's challenge. Use no HUD countdown and no death timer, because Battlefield-style timers are gamey and exploit-prone (C7, C8).
5. **Vehicles and physics objects.** Apply Layers B and C to every networked rigidbody or vehicle, not only the CharacterController. Battlefield needed separate vehicle tuning (C9), and a CharacterController ignores forces (C14), so vehicles take a different code path.
6. **Expansion.** Treat Zline as data (a ScriptableObject or a server config value), so an update can move Layers B and C and switch the gate prefab to `Open` without code changes (inference, informed by C13).

## Testable acceptance ideas

- **AT1, walk, sprint and jump sweep (PlayMode, `[UnityTest]`, C18).** For N = 50 evenly spaced x positions across the line (plus both shoreline ends), spawn the player 20 m south and drive input north for 10 s in each mode: walk, sprint, sprint + jump spam, and crouch. Step with `WaitForFixedUpdate`. Assert `player.position.z <= Zline` on every frame.
- **AT2, diagonal and grazing approach.** Repeat AT1 at 15°, 45° and 75° headings in both directions. Assert the same invariant.
- **AT3, high-speed and teleport probe.** Set the player's velocity, or call `CharacterController.Move` with a single displacement of 5 m, 20 m and 100 m across the line. Assert the result is clamped. This tests Layer B, and Layer C when the collider is bypassed with `transform.position`.
- **AT4, server clamp.** In a host + client test, have the client write a position north of the line directly (simulating a cheat). Assert the server snaps the player south within 1 tick and logs the anomaly.
- **AT5, collider coverage scan (EditMode).** Raycast north from a grid (x every 1 m, y from terrain to terrain + 10 m) south of the line. Assert every ray hits a `WorldBounds` collider before z = Zline + 0.1. This detects gaps, terrain seams and missing overlap at the shores.
- **AT6, no terrain holes near the line (EditMode).** Sample `TerrainData.GetHoles` in the band Zline ± 10 m. Assert there are no holes (C17).
- **AT7, vehicle and rigidbody push.** Spawn each vehicle prefab and a physics crate. Apply a large forward force northward. Assert z stays ≤ Zline after 10 s.
- **AT8, gate data-driven.** Change Zline and set the gate to `Open` in a test config. Assert AT1 passes at the new line and the old line is now passable.

## Leads

- RDR2 Guarma "invisible sniper", and the snipers or bounty at the north edge in RDR2. Only a forum snippet was seen (igta5.com Guarma exploration guide). Unverified.
- The Long Dark uses cliffs and Transition Zones between regions. Only search snippets were seen (thelongdark.fandom.com/wiki/Region). Unverified.
- Deni Albar, "Design of Area Boundaries in Video Game with an Open World Concept", Atlantis Press, 2024, DOI 10.2991/978-2-38476-442-6_5. The fetch gave only a vague summary. Read the PDF for its taxonomy.
- Wayline, "Invisible Walls: Guiding Freedom in Open-World Games". Not read.
- The Netcode for GameObjects NetworkTransform manual and its server-authoritative mode (docs.unity3d.com/Packages/com.unity.netcode.gameobjects@2.7/manual/components/helper/networktransform.html). Read it next for clamp placement.
- division.zone Update 1.6 article. Fetch it to confirm how the old north wall was presented and opened.

## Could not find

- No primary developer talk (GDC or similar) on boundary design was retrieved. The best design source is the 2008 Game Developer feature (S1).
- No retrieved evidence for the GTA V or Far Cry soft-boundary behaviour, Fallout 76 map edges, or Rust map edges (radiation, or the edge-of-world kill). Not stated.
- No Unity documentation on CharacterController tunnelling at high speed, or on continuous collision for a CharacterController. AT3 must establish this empirically.
- No player-reception data (surveys or metrics) comparing boundary techniques. Only qualitative critique (S1, S2).
