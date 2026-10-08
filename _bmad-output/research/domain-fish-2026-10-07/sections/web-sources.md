# Web sources for the fish pod (f-web, 2026-10-07)

All URLs accessed 2026-10-07. "Quote" means text copied from the source; "INFERRED" means my reasoning from sourced facts; "UNSOURCED" means I could not find a source and the number must not be used as fact.

## Summary

- **Item 1 (roster):** USACE lists 74 fish species in Cheatham Lake and names every roster species, including bighead and silver carp, as present today [1.1]. TWRA's Angler's Guide gives adult length ranges and habitat for each one [1.3]. No online TWRA or TVA survey gives species-by-species abundance for Cheatham itself. The nearest quantitative survey on the Cumberland is KDFWR electrofishing in the Barkley tailwater (2016), where threadfin and gizzard shad dominate by count, then longear sunfish, bluegill and largemouth bass [1.4].
- **Item 2 (carp ruling): carp OUT under the 1987 lore.** Silver and bighead carp first came to the US in 1973. Neither was recorded in Tennessee until 2000 [2.1-2.3], and they reached Lake Barkley in the early 2000s [2.4]. In 1987 they were not in the Cumberland. Spreading on their own after 1987 would mean getting past Barkley Dam, and the only way upstream there is through the lock chamber [2.6]. Cheatham is the only Cumberland dam that goes to "open river", where the gates lift clear of the water in floods [1.1]. Also, no carp reproduction has been detected in the Tennessee or Cumberland rivers, so populations there depend on fish arriving from downstream [2.5]. Common carp should stand in.
- **Item 3 (25 years unfished):** No direct analogue exists for an unfished, unmanaged US reservoir. The freshwater evidence shows that without fishing, numbers and biomass rise, big fish gain most, and growth then slows as food runs short [3.1-3.3]. Stocked species (striped bass, walleye, trout) would fade (INFERRED).
- **Item 4 (Secchi):** USACE measurements at Bells Bend give a median of 1.0 m (10th-90th percentile 0.8-1.2 m; whole pool 0.5-1.6 m; April-October only). The shader's equivalent of about 0.65 m is below the Bells Bend 10th percentile (0.8 m), so it is murkier than about 90% of samples. It is still inside the measured min-max (0.6-1.5 m at Bells Bend). Per N1 the shader wins.
- **Item 5 (visible behaviour):** Shad ripple the surface at dawn and dusk, gar bask and gulp air, and skipjack drive shad to the surface; all sourced. Common carp surface behaviour and freshwater flight-initiation distance are UNSOURCED; marine no-take studies are the proxy (fish there flee about 1-2 m later).
- **Item 6 (games):** Valheim is the only game with hard numbers (datamined): fish spawn 40-80 m from the player, 3-10 per 64 m zone, in groups of 2-7. RDR2 and Sons of the Forest show fish through surface signs, and their fish flee when the player wades in. The Angler places fish with a habitat grader.
- **Item 7 (TD refs):** 6 of f-td's 7 references check out. Ref 5 is wrong: BatchRendererGroup is **not supported on WebGL at all** (Unity 6.0 manual), so f-td's rejection of BRG stands on firmer ground. RGBAHalf on WebGL2 is unverified.

## Item 1. Species of the Cumberland River / Cheatham Lake

### 1a. Presence (Cheatham Lake as a whole)

[1.1] USACE Nashville District, *Cheatham Lake Master Plan* (May 2018), section 2-08 "Aquatic Fauna". https://upload.wikimedia.org/wikipedia/commons/a/af/Cheatham_Lake_master_plan_-_USACE-p16021coll7-12386.pdf (USACE document mirrored on Wikimedia Commons; draft also at https://usace.contentdm.oclc.org/digital/api/collection/p16021coll7/id/4268/download)
- Quote: "A total of 74 fish species and two hybrids have been found in Cheatham Lake."
- Quote: "the black basses (largemouth bass, smallmouth, and spotted bass), temperate basses (white and black), crappie (white and black), and sauger are the most targeted sportfish species. Smallmouth bass are common in the Cheatham Reservoir."
- Quote: "The rough fish include the catfish (blue, channel, and flathead), bullheads (brown, black, and yellow), carp (common, bighead and silver), buffalo (smallmouth, bigmouth, and black), drum, gar (spotted, shortnose, and longnose), bowfin, redhorse (river, black, and golden), carpsuckers, and paddlefish. The dominant forage fishes include gizzard and threadfin shad and skipjack herring located in the riverine of Cheatham Reservoir."
- Geometry: dam at Cumberland River Mile 148.6-148.7; the lake runs 67.5 miles up to Old Hickory Dam (Mile 216.2), 7,450 acres at normal pool elevation 385 ft; a 300 ft x 9 ft navigation channel is maintained. Bells Bend (river mile ~175-190) is mid-pool.
- Quote: "Water quality in Cheatham Lake, which is ecologically more like a river, is always highly dependent upon the quality of upstream, regulated releases."

[1.2] TWRA, *Cheatham Reservoir* page. https://www.tn.gov/content/tn/twra/fishing/where-to-fish/middle-tennessee-r2/cheatham-reservoir.html
- Quote: "Cheatham Reservoir is a 7,450-acre riverine impoundment that meanders through Nashville and downstream to Ashland City."
- Quote: "Largemouth Bass, Spotted Bass, and Smallmouth Bass all contribute to the Cheatham Reservoir black bass fishery. Largemouth Bass are the most abundant."
- Quote: "White and Black Crappie contribute to the Cheatham crappie fishery, but White Crappie are the predominant species."
- Quote: "Channel Catfish are the predominant species, but large Flathead and Blue Catfish provide a trophy component to this fishery."
- Quote: "TWRA stocks both species [sauger and walleye] in Cheatham Reservoir". Productive catfish areas are "the Cumberland River channel at the confluence of major embayments".

### 1b. Adult length and habitat by species (TWRA)

[1.3] TWRA, *Angler's Guide to Tennessee Fish Including Aquatic Nuisance Species* (Feb 2018). https://www.tn.gov/content/dam/tn/twra/documents/fishing/anglersguide.pdf
The lengths are TWRA's angler-harvest average and range, not a survey of the full adult population. The 25-year no-fishing case shifts toward the upper end of each range (see item 3).

| Species | Avg / range (in) | Avg / range (m) | Habitat (TWRA wording, condensed) | Group (TWRA) |
|---|---|---|---|---|
| Channel catfish | 16 / 10-38 | 0.41 / 0.25-0.97 | Medium to large rivers, reservoirs | not stated |
| Blue catfish | 18 / 14-45 | 0.46 / 0.36-1.14 | "deep areas of large rivers... chutes and pools with currents" | not stated |
| Flathead catfish | 18 / 12-40 | 0.46 / 0.30-1.02 | "deep holes scoured by currents, such as... eddies adjacent to bridge pilings and in tailraces"; eats live fish | not stated |
| Largemouth bass | ~15 / 8-24 | 0.38 / 0.20-0.61 | Calm, warmer, often more turbid water; "cover, such as fallen trees, brush and stumps" | not stated |
| Smallmouth bass | 14-18 / 8-22 | 0.36-0.46 / 0.20-0.56 | "near ledges and rocky areas where the water is usually clear" | not stated |
| Spotted bass | ~13 / 8-18 | 0.33 / 0.20-0.46 | "preference for rocky areas" | not stated |
| Sauger | ~16 / 10-22 | 0.41 / 0.25-0.56 | Large rivers and reservoirs, turbid-tolerant; "near dams or river islands" | not stated |
| White crappie | 6-14 (normal) | 0.15-0.36 | Slow-moving areas of large rivers; "more tolerant of muddy waters than black crappie" | not stated |
| Black crappie | ~11 / 6-14 | 0.28 / 0.15-0.36 | Quiet, warm water, vegetation, sandy/muddy bottoms | not stated |
| Bluegill | 7 / 4-11 | 0.18 / 0.10-0.28 | Quiet, shallow, warm water; spawning beds "usually grouped together" | colonial nesters |
| Freshwater drum | 10 / 8-30 | 0.25 / 0.20-0.76 | Large impoundments and rivers; "mainly bottom feeders" | not stated |
| Longnose gar | ~20 / 12-36 | 0.51 / 0.30-0.91 | Larger streams and reservoirs, "prefer the warmest waters"; gar "gulp" air into the swim bladder | not stated |
| Spotted gar | 18 / 12-26 | 0.46 / 0.30-0.66 | Quiet, clear, vegetated water; more common in west Tennessee | not stated |
| Smallmouth buffalo | 22 / 14-34 | 0.56 / 0.36-0.86 | Clear water of larger rivers and reservoirs | not stated |
| Bigmouth buffalo | ~24 / 16-38 | 0.61 / 0.41-0.97 | "shallow portions of large rivers"; eats plankton and bottom organisms | not stated |
| Common carp | 15 / 10-32 | 0.38 / 0.25-0.81 | Muddy, warm, low-oxygen water; bottom feeder that "stirs up the bottom" | not stated |
| Gizzard shad | ~8 / 2-14 | 0.20 / 0.05-0.36 | "occur in schools"; calm, warm, productive water | **schools** |
| Threadfin shad | 1-6 | 0.03-0.15 | Lakes, larger rivers, reservoirs; plankton | not stated in TWRA (see 1.4 counts) |
| Skipjack herring | ~12 / 9-20 | 0.30 / 0.23-0.51 | Fast water below dams; feeds "often in schools, when they force groups of small shad to the surface" | **schools** |
| Silver carp | up to 60 lb (no length range given) | NAS: "1 m and 27 kg" [2.1] | "often feeding in schools at the water's surface" | **schools** |
| Bighead carp | to 3 ft, 100 lb | ~0.9 | same as silver carp | **schools** |

TWRA does not say whether most species are solitary or school (blank cells above): **UNSOURCED** here. f-designer needs another source for those cells or must mark them INFERRED.

### 1c. Relative abundance: nearest quantitative data on the Cumberland

[1.4] KDFWR, *Annual Performance Report, Commercial and Sport Investigations* (period 1 April 2016 - 31 March 2017), Project IV "Kentucky Lake Tailwater and Lake Barkley Tailwater Sport Fish Assessments". https://fw.ky.gov/Fish/Documents/2017_CSI_APR.pdf
Pulsed-DC boat electrofishing, 900 s runs, on the Cumberland River below Barkley Dam (about 150 river miles downstream of Cheatham Dam). Boat electrofishing samples fish in shallow water along the shore. It under-samples deep channel species such as blue catfish, drum and buffalo, so read these as shoreline counts.
- Spring 2016, Barkley tailwater: 1,242 fish, 42 species, 2.75 h. Quote: "Longear sunfish were the most abundant species captured with a CPUE of 110.1 fish/hr. Other prevalent rough fish species caught during spring sampling at Barkley Tailwater were silver carp (24.3 fish/hr) and smallmouth buffalo (23.1 fish/hr). Prominent sport fish captured in Barkley Tailwater during spring sampling were bluegill (69.4 fish/hr) and largemouth bass (63.7 fish/hr)."
- Fall 2016, Barkley tailwater: 11,468 fish, 33 species, 1.99 h. Quote: "Threadfin shad were the most abundant species captured in Barkley Tailwater (4,598.5 fish/hr). Other prevalent rough fish species caught... were gizzard shad (208.7 fish/hr) and longear sunfish (101.6 fish/hr). Abundant sport fish species captured included largemouth bass (48.2 fish/hr) and bluegill (46.5 fish/hr)."
- Other Barkley fall 2016 CPUE from the report's table (fish/h): smallmouth buffalo 14.9, redear sunfish 8.0, smallmouth bass 7.2, white bass 6.7, grass carp 5.0, silver carp 4.0, white crappie 3.5, spotted bass 1.8, yellow bass 1.8, skipjack herring 0.5, longnose gar 0.4. (The PDF text extraction garbles the column order for some rows. I only list rows where all three columns are present.)

INFERRED, for f-designer: by count, shad far outnumber everything else (schooling forage), then sunfish and black bass along the shore. Big bottom fish (buffalo, catfish, drum, gar) are few per hour of shoreline sampling, though they are a large share of the biomass. No Cheatham-specific CPUE table was found online. TWRA reservoir reports for Cheatham are **not available online (UNSOURCED)**; ask TWRA Region 2 if exact numbers are needed.

## Item 2. Silver and bighead carp

### 2a. Arrival dates

[2.1] USGS NAS, *Silver Carp, Species Profile*. https://nas.er.usgs.gov/queries/factsheet.aspx?SpeciesID=549
- Quote: "It was first brought into the United States in 1973 when a private fish farmer imported Silver Carp into Arkansas."
- As of 1993, NAS says the literature authors were "unaware of any records of the species in the state of Tennessee". NAS search summary: first Tennessee observation **2000**.
- Size: "1 m and 27 kg". Behaviour: "known for leaping out of the water when startled (e.g., by noises such as a boat motor)."

[2.2] USGS NAS, *Bighead Carp, Species Profile*. https://nas.er.usgs.gov/queries/factsheet.aspx?SpeciesID=551
- Quote: "first imported into the United States in 1973 by a private fish farmer in Arkansas"; escaped into open waters "during the early 1980s", appearing in the Ohio and Mississippi rivers. Size up to 1.4 m and 40 kg. A big escape in April 1994 (Osage River, Missouri) sped up their spread.

[2.3] USGS NAS, *Silver Carp collections, Tennessee*. https://nas.er.usgs.gov/queries/CollectionInfo.aspx?SpeciesID=549&State=TN
- Lower Cumberland records: "Cumberland River, Lake Barkley, Below Cheatham" (2016) and "Johnson Creek embayment near Cheatham Dam" (2017).

[2.4] Ridgway, J. L. and Bettoli, P. W. (2017). Distribution, age structure, and growth of bigheaded carps in the lower Tennessee and Cumberland rivers. *Southeastern Naturalist* 16(3): 426-442. https://www.eaglehill.us/SENAonline/articles/SENA-16-3/19-Ridgway.shtml (author PDF: https://usgs-cru-individual-data.s3.amazonaws.com/pbettoli/intellcont/Ridgway%20and%20Bettoli_2017_SENA%20publication-1.pdf)
- Quote: Lake Barkley: "Bighead Carp have been reported there since 2002."
- Quote: "In 2010, a Bighead Carp was collected 1 reservoir above Lake Barkley in Cheatham Lake at Cumberland River km 239."
- Quote: "the leading edges of Silver Carp invasion in Tennessee waters were the tailwater below Cheatham Dam in Lake Barkley". The fieldwork ran in 2015 and 2016.
- On how they passed: "Barges have the ability to entrain, retain, and even transport small fish". Also: "Migrating upstream through a navigation lock at an early life-history stage would be a heretofore unobserved phenomenon, though not impossible."

[2.5] TWRA / KDFWR, *2022 Annual Technical Report, Tennessee and Cumberland River sub-basin, Invasive Carp Partnership* (MICRA). https://micrarivers.org/wp-content/uploads/2023/08/2022-TNCR-ER.pdf
- Quote: "No larval or juvenile invasive carp were collected during sampling efforts, suggesting a continued lack of successful reproduction in the Tennessee and Cumberland rivers and that populations in Tennessee are driven primarily by migration."
- Cheatham gill nets 2022: 39 silver carp in June (4.87/net), 6 in September (0.75/net). Mean total length 841 mm (June) and 879 mm (September); range 728-985 mm. Quote: "on average, silver carp were larger in Cheatham and Pickwick reservoirs."
- Quote: "Few invasive carp (n=3) were observed and collected in Old Hickory Reservoir." Old Hickory is the next pool upstream of Cheatham, so the front is at Old Hickory today.
- Electrified dozer trawl, Cheatham, summer 2022: 59 silver carp evaded capture and 5 were caught. Fall: 139 evaded, 8 caught. "Silver carp that jumped or evaded capture were counted."

### 2b. Passage at the dams

[2.6] USFWS, *High Tech Battle Waged Against Invasive Carp* (Oct 2021). https://www.fws.gov/story/2021-10/high-tech-battle-waged-against-invasive-carp
- About Barkley Lock and Dam, quoting Kentucky's fisheries coordinator: "The only way to get through it, and further upstream, is past the BAFF and through the lock chamber."
- Silver and bighead carp reached Lakes Barkley and Kentucky about a decade before 2021, coming up the Mississippi, then the Ohio, then the Cumberland. They can leap "as high as 10 feet".

[2.7] Vallazza, J. M. et al. (2025). Silver Carp passage at three locks and dams on the Tennessee and Cumberland rivers from 2016-2019. *Journal of Wildlife Management*. https://wildlife.onlinelibrary.wiley.com/doi/abs/10.1002/jwmg.70112 (the publisher page returned 403; facts below are from the search abstract and the USGS data release https://catalog.data.gov/dataset/data-for-dam-passage-analysis-of-silver-carp-at-three-locks-and-dams-on-the-tennessee-and-)
- 465 silver carp were tagged. They made 37 upstream and 57 downstream passages of the Barkley, Kentucky and Pickwick dams in 2016-2019. 89% of upstream passages were in April-August, at 12-31 C.

[2.8] USGS OFR 2025-1039, *Evaluating deterrent locations and sequence in the Tennessee and Cumberland Rivers...* https://pubs.usgs.gov/publication/ofr20251039/full
- Upstream and downstream movement "can occur through lock chambers" and "over a dam spillway". Old Hickory gets a recruitment potential of 0.00.

[1.1] again (USACE Cheatham Master Plan): at high water "all the tainter gates are raised clear of the water and the river is allowed to flow almost unimpeded through the project... Cheatham is the only Corps of Engineers project in the Cumberland Basin at which such 'open river' conditions occur."

### 2c. Ruling input for N4 (INFERRED from the sources above)

1. **Before 1987, in the Cumberland: no.** The species were in US aquaculture from 1973 and escaped in the early 1980s [2.1, 2.2]. Tennessee had no records before 2000, and Lake Barkley none before 2002 [2.1, 2.4].
2. **Spreading on their own after 1987 with no lock operation: implausible.** To reach Bells Bend, carp must pass Barkley Dam first, and there the only route upstream is the lock chamber [2.6]. Cheatham Dam is the one Cumberland dam that goes to open river in floods [1.1], so it is not the barrier. Barkley is. **UNSOURCED:** whether 25 years with nobody operating the gates would leave Barkley's spillway gates open or failed. If the lore wants that, it is a lore choice, not a research finding.
3. **Even if a few got through, they would not persist:** no reproduction has been detected in the Tennessee or Cumberland rivers, and populations there depend on fish arriving from downstream [2.5]. A sealed Cheatham pool would not keep a population for 25 years. INFERRED: no lifespan source was checked.
4. **Recommendation:** carp OUT, common carp stands in (N4).

### 2d. Jumping behaviour (still useful for any surface-jump design)

[2.9] Vetter, B. J., Casper, A. F. and Mensinger, A. F. (2017). Characterization and management implications of silver carp (*Hypophthalmichthys molitrix*) jumping behavior in response to motorized watercraft. *Management of Biological Invasions* 8(1): 113-124. https://doi.org/10.3391/mbi.2017.8.1.11 (PDF: https://www.reabic.net/journals/mbi/2017/1/MBI_2017_Vetter_etal.pdf)
- Quote: "most boat transits (57.9%) stimulated five or more fish to jump... the vast majority of fish (> 90.0%) jumped after the boat had passed their position but avoided the area directly astern (< 4.0 m). Furthermore, 79.8% of fish vectored away from the moving watercraft."
- Median jump distance from the boat: 5.6 m (100 hp motor) and 5.1 m (150 hp).
- Jump height, from search-result summaries of the related literature: "up to 3 m"; USFWS: "as high as 10 feet" [2.6].

[2.10] Vetter, B. J. et al. Broadband sound can induce jumping behavior in invasive silver carp. *Proceedings of Meetings on Acoustics* 27, 010021. https://pubs.aip.org/asa/poma/article/27/1/010021/835345/
- Outboard-motor sound (0.06-10 kHz) played from a slow boat (3-6 km/h) made wild fish jump. Sound alone, without a moving hull, is enough.

INFERRED: jumps need a loud trigger (motor noise). In a world with no boats, silver carp would jump rarely, which fits a low surface-event cap. Common carp also leap, but no source on how often was checked: **UNSOURCED**.

## Item 3. A fish community with no fishing for decades and no dam operation

There is no study of a temperate US reservoir left unfished and unmanaged for 25 years. **UNSOURCED** as a direct analogue. The sources below cover the parts of the question.

[3.1] Koning, A. A., Perales, K. M., Fluet-Chouinard, E. and McIntyre, P. B. (2020). A network of grassroots reserves protects tropical river fish diversity. *Nature* 588: 631-635. https://www.nature.com/articles/s41586-020-2944-y (summary: https://www.unr.edu/nevada-today/news/2020/freshwater-fish-reserves)
- 23 community no-take river reserves (Mae Ngao, Salween basin, Thailand) compared with fished river next to them. Quote: "reserves held 27% more fish species", "124% higher fish density", "an astounding 2,247% higher biomass on average". Larger-bodied fish gained the most.
- Quote: "In many reserves, the abundance and size of fish seeking protection was evident by eye from the river bank in the dry season."
- Caveat: tropical, clear water, and much more fishing pressure outside than Cheatham had. This is the upper bound of the effect, not a number to copy.

[3.2] Watson, Hickford and Schiel (2022). Interacting effects of density and temperature on fish growth rates in freshwater protected populations. *Proc. R. Soc. B*. https://pmc.ncbi.nlm.nih.gov/articles/PMC8767183/
- New Zealand streams closed to whitebait fishing since the mid-1960s held about 10 times more juveniles than open streams. Quote: "when population densities were high, compensatory responses of far slower growth rates were strongest." At 20 C, high-density fish grew at "less than half the rate of low-density fish."
- Use: with no fishing, numbers rise until food limits them, then growth slows. This supports N3's "limited by food/habitat".

[3.3] Siegel, Welsh, Taylor and Phelps (2023). Size structure, age, growth, and mortality of flathead catfish in the Robert C. Byrd Pool of the Ohio and Kanawha rivers. *J. Southeastern Assoc. Fish and Wildlife Agencies* 10. https://www.usgs.gov/publications/size-structure-age-growth-and-mortality-flathead-catfish-robert-c-byrd-pool-ohio-and (PDF: https://seafwa.org/sites/default/files/journal-articles/J10_02_Siegel%20et%20al%2010-16.pdf)
- A navigation pool much like Cheatham, with light harvest. Quote: "We documented a high-density population (mean CPUE = 49 fish h-1) with low mortality (A = 11.8%), characterized by slow growing individuals with a maximum recorded age of 36." The broad size structure "likely is maintained only through low harvest and high rates of catch and release."
- Use: the best available picture of a lightly fished big-river flathead population. Many age classes, many big old fish, slow growth.

[3.4] Januchowski-Hartley, F. A. et al. (2011). Fear of Fishers: Human Predation Explains Behavioral Changes in Coral Reef Fishes. *PLoS ONE* 6(8): e22761. https://journals.plos.org/plosone/article?id=10.1371%2Fjournal.pone.0022761
- Flight-initiation distance (FID) rose with fishing pressure. FID in the no-take area was similar to lightly fished areas. "Overall, FID ranged from 27 to 722 cm." At higher fishing pressure, larger fish fled from farther away. Marine; see item 5.

[3.5] Philipp, D. P. et al. (2009). Selection for vulnerability to angling in largemouth bass. *Trans. Am. Fish. Soc.* 138(1): 189-199. https://onlinelibrary.wiley.com/doi/10.1577/T06-243.1
- Vulnerability to angling is heritable (realized heritability 0.146). Angling selects for less catchable bass over generations. INFERRED: 25 years without angling (several bass generations) would let bolder, more catchable behaviour come back.

No dam operation, sourced pieces:
- Cheatham is run-of-the-river with "no flood control capability", and its water quality "is closely tied to the flow regime which is governed by dam releases" from Old Hickory [1.1]. With Old Hickory not operated either, the pool gets unregulated flow, with natural floods and low flows. Effects on the fish community: **UNSOURCED**.
- Native migratory fish use the locks too. The Barkley lock deterrent study tracked freshwater drum, paddlefish, smallmouth buffalo and lake sturgeon passing the lock (USGS data release: https://catalog.data.gov/dataset/data-release-for-invasive-silver-carp-and-native-species-passage-probabilities-in-relation). INFERRED: closed locks cut off movement between pools for all species, not only carp. Cheatham's open-river floods still pass fish between Cheatham and Barkley [1.1].
- Stocked species fade: TWRA stocks sauger, walleye and striped bass in Cheatham, and trout below Percy Priest [1.1, 1.2]. INFERRED: with stocking stopped for 25 years, striped bass, walleye and trout drop out or go rare. Sauger is native and found in all mainstem reservoirs [1.3], so it stays, at a lower level. Exact numbers **UNSOURCED**.

## Item 4. Water clarity (Secchi) at Bells Bend

[4.1] USACE Nashville District Secchi disk depths, retrieved from the Water Quality Portal (USGS/EPA), HUC 05130202 (Cheatham Lake watershed). Query: https://www.waterqualitydata.us/data/Result/search?huc=05130202&characteristicName=Depth%2C%20Secchi%20disk%20depth&mimeType=csv&dataProfile=narrowResult (stations: https://www.waterqualitydata.us/data/Station/search?huc=05130202&characteristicName=Depth%2C%20Secchi%20disk%20depth&mimeType=csv). Downloaded 2026-10-07; my computation over the CSV.
- 624 USACE readings, 2000-2023, 21 mainstem stations from Old Hickory tailwater to the Harpeth mouth. Sampled April to October only. One 387.1 m value (a data-entry error) was dropped.
- **Whole pool: median 1.0 m; 10th-90th percentile 0.7-1.2 m; min 0.5 m, max 1.6 m.** The monthly median is 0.8 m in May and 0.9-1.0 m in other months.
- **Bells Bend bracket:** stations CHE20007 "Nashville At Bordeaux Bridge" (upstream) and CHE20006 "Mouth Of Overall Cr. - Two Miles Downstream Of Clees Ferry" (downstream). n=121: **median 1.0 m, 10th-90th percentile 0.8-1.2 m, range 0.6-1.5 m.**
- Gaps: no winter readings and no readings during floods. INFERRED: clarity is lower then. Also, all readings are under regulated releases.
- USACE also runs 22 water-quality stations on Cheatham, sampled 2-3 times a year [1.1, section "Water Quality Stations"].

[4.2] Poole, H. H. and Atkins, W. R. G. (1929): Kd = 1.7 / Secchi depth. Confirmed via the Secchi relationship review in *Frontiers in Marine Science* (2024), https://www.frontiersin.org/journals/marine-science/articles/10.3389/fmars.2024.1265382/full, which also notes F = 1.4 fits turbid estuaries better (Holmes 1970). Also https://en.wikipedia.org/wiki/Secchi_disk.
- Measured Secchi 1.0 m gives Kd of about 1.7 per m (1.4 with the turbid-water constant). Secchi 0.7-1.2 m gives Kd of about 1.4-2.4 per m.
- Compared with the shader (f-td T7): shader extinction averages ~2.6 per m, which implies a Secchi of ~0.65 m. That is clearer than the measured minimum of 0.5 m, but murkier than the measured median of 1.0 m. Under N1 the shader wins. The record should say the game water is murkier than about 90% of measured summer samples at Bells Bend (10th percentile 0.8 m there; 0.7 m pool-wide), but still within the measured min-max (0.6-1.5 m at Bells Bend; 0.5-1.6 m pool-wide). Corrected after f-td's review; my earlier "near the 5th-10th percentile" was wrong. INFERRED mapping, as f-td noted.

## Item 5. What fish behaviour is visible from shore

| Sign | Source | What it says |
|---|---|---|
| Shad dimpling and rippling the surface | [5.1] Outdoor Alabama (ADCNR), Threadfin Shad https://outdooralabama.com/other-species/threadfin-shad | Quote: "they can be seen rippling the surface at dawn and dusk." |
| Shad schools near the surface, leaping and skipping | [5.2] Missouri Dept. of Conservation, Gizzard Shad https://mdc.mo.gov/discover-nature/field-guide/gizzard-shad | Quote: "travels in large, constantly moving schools near the water's surface and frequently leaps clear of the water or skips along the surface on its side". Most active at dusk and night. |
| Skipjack driving shad to the surface | [1.3] TWRA Angler's Guide | Quote: skipjack feed "often in schools, when they force groups of small shad to the surface." |
| Gar basking and gulping air | [1.3] TWRA; [5.3] MDC Longnose Gar https://mdc.mo.gov/discover-nature/field-guide/longnose-gar | TWRA: gars "gulp" air into the swim bladder. MDC: "they often bask near the surface". At spawning: "they break the surface, splashing so noisily they may be heard from a considerable distance." |
| Gar air-breath rate | [5.4] Saksena, V. P. (1975), Effects of temperature and light on aerial breathing of the longnose gar, *Ohio J. Sci.* (volume and year as given by search, 72(1), 1975, conflict and are unverified); and the shortnose gar companion paper (https://kb.osu.edu/server/api/core/bitstreams/c462bc90-45c2-5a8b-a9e4-5af56dbed3b7/content), both found via search; full text returned 403 | Search abstracts: the breathing rate rises with temperature. Shortnose gar: "less than one per hour between 10.0 and 15.5 C, but increasing markedly beyond 15.5 C". Longnose gar breathed more at night above 54 F (12 C) and more by day below it. Exact breaths-per-hour table **not read (UNSOURCED number)**. |
| Silver carp jumping | [2.9], [2.10] | Triggered by motor noise. See item 2d. |
| Common carp surface behaviour | MDC Common Carp https://mdc.mo.gov/discover-nature/field-guide/common-carp | Only says they "sometimes" feed in very shallow water. Rolling, jumping and spawning splashes: **UNSOURCED**. |
| Fish visible from the bank in unfished water | [3.1] Koning et al. 2020 | Large fish "evident by eye from the river bank in the dry season" (clear tropical water). |

Flight-initiation distance:
- No freshwater study measured FID of North American river fish to a person on shore or wading: **UNSOURCED**.
- Marine reef proxy [3.4]: snorkeler approach starting 8-10 m away; FID ranged 0.27-7.22 m across species and sites; fish in no-take and lightly fished areas let people come closer, and big fish under heavy fishing fled earliest.
- Summary of reserve studies (CORAL Magazine, https://www.coralmagazine.com/2012/11/14/innocent-fish-happy-fishermen/): fish in fished areas "took flight at distances a metre or two further away than ones living within the reserve."
- INFERRED for the design: an unfished population at Bells Bend sits at the low end, so a scatter radius of about 1-3 m from a wader is reasonable, with bigger fish no warier than small ones (N3: less wary, still scatter). Mark it INFERRED in the G6 table.

## Item 6. How games stage ambient fish

No developer talk on ambient fish was found for RDR2, The Long Dark, Valheim or Sons of the Forest (**UNSOURCED**). Most of what follows is from community wikis and guides, which are secondary sources; the numbers come from datamined game files, not studio statements.

| Game | How fish are shown | Spawn and counts | Source |
|---|---|---|---|
| **Valheim** (Iron Gate) | Fish models swim visibly in the water. | General spawn system: "between 40 - 80 meters of a random player in the zone". Zones are 64 x 64 m, processed every 4 s while a player is present. Fish spawners: interval 1-2 min, 50-60% chance, **3-10 fish per zone**, groups of 2-6 or 2-7 within a 2 m group radius, no new spawn within 12-20 m of existing fish, most "Below -5 m" depth, some "From -3 to -1.5 m". | https://valheim.gamecore.wiki/en/game_mechanics/spawn-zones/ (datamined community wiki). Steam players: fish "refresh within 40 to 80 meters away from the player" every 120 s at 50%, which frustrates people fishing from one dock: https://steamcommunity.com/app/892970/discussions/0/4339851480063887730/ |
| **Red Dead Redemption 2** (Rockstar) | Fish exist in the water before the rod comes out. Players find spots from "ripples in the water from surfacing fish, splashes, or fish jumping out of the water". Fish are visible directly only "if reflections don't obstruct vision". With the rod out, Eagle Eye highlights fish silhouettes. | Not published (**UNSOURCED**). The wiki calls the belief that the rod "makes fish spawn" a misconception. | Search summaries of https://reddead.fandom.com/wiki/Fishing (the page itself returned HTTP 402) and https://holdtoreset.com/red-dead-redemption-2-fishing-guide-bait-lures-and-legendary-fish/ |
| **The Long Dark** (Hinterland) | Ice fishing is abstracted. In early versions it happened only inside fishing huts; the player picks how many hours to fish, like sleeping, and the catch is rolled. No visible swimming fish was found in any source (**UNSOURCED** either way). | Players report about 1 fish per hour, or 1 per 2 hours at some huts. Holes were pre-placed in map geometry. | https://thelongdark-archive.fandom.com/wiki/Ice_Fishing (search summary; page returned 402); https://steamcommunity.com/app/305620/discussions/0/3264459260621981040/ |
| **Sons of the Forest** (Endnight) | Visible fish in streams and lakes, speared from the bank. Quote: "Fish also swim away as soon as you wade in." Players are told to "Crouch, use cover, and throw from close range." Fish spawn spring to fall, not in winter. | Not published (**UNSOURCED**). | https://www.4netplayers.com/en/blog/sons-of-the-forest/nutrition-guide/ ; https://www.gamerguides.com/sons-of-the-forest/database/wildlife/aquatic/fish |
| **theHunter: Call of the Wild** (Expansive Worlds) | Has no fishing; the studio made fishing a **separate game**, *Call of the Wild: The Angler* (2022). | Developer diary (Nathanael van den Berg, 20 Jul 2022): fish are placed by a systemic grader using "over 20 different pieces of data", including water depth, time of day, temperature, flow speed, turbidity and bed vegetation. "If the grade is good and the minimum conditions are met, a fish can be found in that location." "The better the grade, the bigger the fish". Spawn radius, counts and LOD are not given. | https://cotwtheangler.com/news/developer-diary-systemic-approach-spawning-fish/ ; https://avalanchestudios.com/stories/expansive-worlds-reveals-call-of-the-wild-the-angler |

What carries over (INFERRED):
- Valheim's numbers are the only hard published numbers, and they match the TD plan: spawn in a ring away from the player (40-80 m), a small cap per 64 m zone (3-10), small groups (2-7), and a suppression distance (12-20 m).
- RDR2 and Sons of the Forest both use surface signs (ripples, splashes, jumps) to show where fish are, and fish that flee when the player enters the water. That is the N1/N2 approach.
- The Angler's habitat grader (depth, flow, turbidity, vegetation, bigger fish in better habitat) is the same idea as the cell function plus band densities.
- Player reception: the one documented complaint is Valheim fish respawning 40-80 m away instead of at a fixed dock. There is no sourced reception for the rest (**UNSOURCED**).

## Item 7. Checking f-td's technique references (td-item-5-technique.md)

| # | Reference as cited by f-td | Result | Evidence |
|---|---|---|---|
| 1 | Unity Scripting API, Graphics.RenderMeshInstanced | **VERIFIED**, with details to add | https://docs.unity3d.com/ScriptReference/Graphics.RenderMeshInstanced.html (page shows Unity 6.6). "You can only render a maximum of 1023 instances at once": 511 with the default matrices, 1023 with `#pragma instancing_options assumeuniformscaling`. The material needs `Material.enableInstancing = true` or it throws `InvalidOperationException`; it also throws if the platform lacks instancing, so check `SystemInfo.supportsInstancing`. Quote: Unity "uses the bounds to cull and sort all the instances of this Mesh as a single entity", so per-fish culling must happen on the CPU before submit, as 5.4 plans. A non-uniform gar scale is incompatible with `assumeuniformscaling`, but at <= 64 drawn that pragma isn't needed. |
| 2 | Unity Technologies, Animation-Instancing (GitHub) | **VERIFIED** (exists; old) | https://github.com/Unity-Technologies/Animation-Instancing. README: "designed to instance Characters(SkinnedMeshRender)", "needs at least Unity5.4". No render pipeline is named, so it is a reference for the idea, not a URP-ready dependency. |
| 3 | Reynolds 1987, Flocks, Herds, and Schools, SIGGRAPH 87, Computer Graphics 21(4), 25-34 | **VERIFIED** | ACM DL https://dl.acm.org/doi/10.1145/37401.37406 (doi 10.1145/37401.37406): ACM SIGGRAPH Computer Graphics 21(4), pp. 25-34, Aug 1987. Rules (https://www.red3d.com/cwr/boids/): separation, alignment, cohesion. |
| 4 | Reynolds 1999, Steering Behaviors for Autonomous Characters, GDC 1999 | **VERIFIED** | https://www.red3d.com/cwr/steer/. Behaviours: seek, flee, pursue, evade, wander, arrival, obstacle avoidance, containment, wall following, path following, flow field following, plus leader following, unaligned collision avoidance, queuing, and flocking (separation + alignment + cohesion). |
| 5 | Unity Manual, BatchRendererGroup: "constant-buffer mode on OpenGL ES / WebGL" | **PARTLY WRONG** | Unity 6.0 manual https://docs.unity3d.com/6000.0/Documentation/Manual/batch-renderer-group-how.html lists BRG platforms as Windows (DX11, DX12, Vulkan), UWP, Linux (Vulkan), macOS (Metal), iOS, Android (Vulkan and OpenGL ES 3.x), PS4/PS5, Xbox, Switch. **WebGL is not listed.** The constant-buffer (UBO) mode applies to GLES (Unity BRG GLES sample: https://unity.com/blog/engine-platform/batchrenderergroup-sample-high-frame-rate-on-budget-devices). Unity's web graphics guidance recommends "Strip all" BRG variants for projects that don't use BRG (https://docs.unity3d.com/2022.3/Documentation/Manual/web-optimization-graphics.html). BRG also requires the SRP Batcher. The **rejection of BRG stands, and is stronger**: it is unsupported on web, not just unproven. Replace source 5's wording. |
| 6 | Khronos WebGL 2.0 spec: instancing and vertex texture fetch as core | **VERIFIED** (instancing directly; VTF via ES 3.0) | https://registry.khronos.org/webgl/specs/latest/2.0/: "It is derived from OpenGL ES 3.0." `vertexAttribDivisor`, `drawArraysInstanced` and `drawElementsInstanced` are defined, and ANGLE_instanced_arrays moved to core. VTF: the OpenGL ES 3.0 quick reference card gives `gl_MaxVertexTextureImageUnits` a minimum of **16** (https://www.khronos.org/files/opengles3-quick-reference-card.pdf), so VTF is guaranteed. Unity's WebGL2 page says "WebGL2 almost matches with the OpenGL ES 3.0 functionality" (https://docs.unity3d.com/Manual/WebGL2.html) but does not mention instancing. Confirm `SystemInfo.supportsInstancing` in the F9 web build. **Not verified this run:** RGBAHalf (RGBA16F) sampling on WebGL2. It is an ES 3.0 core sized format, but I did not fetch a page saying so, so treat it as UNVERIFIED; the RGBA32F or RGBA8-encoded fallback is f-artist's call. |
| 7 | Poole and Atkins 1929, Secchi ~ 1.7/Kd | **VERIFIED, formula restated** | The relationship is Kd = 1.7 / Secchi depth (equivalently Secchi = 1.7 / Kd). F = 1.4 is suggested for turbid water (Holmes 1970). Source: https://www.frontiersin.org/journals/marine-science/articles/10.3389/fmars.2024.1265382/full (review). The original J. Mar. Biol. Assoc. UK 16: 297-324 was not opened. See item 4 for measured Secchi. |
| - | Vertex animation textures as a technique (5.2) | **VERIFIED** as an established technique | SideFX Labs Vertex Animation Textures node docs https://www.sidefx.com/docs/houdini/nodes/out/labs--vertex_animation_textures-3.0.html. Unity examples: https://github.com/keijiro/HdrpVatExample (HDRP, Shader Graph), https://github.com/codewriter-packages/Mesh-Animation (bakes vertex positions per frame to a texture). |
| - | GPU instancing with custom shaders in URP | **Note for f-td / f-artist** | Unity 6.6 manual https://docs.unity3d.com/Manual/GPUInstancing.html: "If you use the Universal Render Pipeline (URP) or High Definition Render Pipeline (HDRP), GPU instancing works with custom shaders only if you disable the Scriptable Render Pipeline (SRP) Batcher or make a shader incompatible with the SRP Batcher." This applies to GameObject instancing. Direct `RenderMeshInstanced` calls are a separate path, but the fish shader should be checked against it in the G4 look-dev scene. |

## Item 7b. f-artist's [K] WebGL2 claims (artist-items-3-5.md)

| Claim (artist-items-3-5.md) | Result | Evidence |
|---|---|---|
| L109: "WebGL has no compute shaders, so skinning runs on the CPU in wasm" | **VERIFIED**, with a correction to the reason | Compute shaders: Unity Manual "Compare WebGPU and WebGL2" (6000.7 beta) https://docs.unity3d.com/6000.7/Documentation/Manual/web-graphics-apis-intro.html says compute shaders are "Not natively supported" in WebGL2. CPU skinning: Unity staff (Jonas Echterhoff, 20 Jan 2017), https://discussions.unity.com/t/no-gpu-skinning-on-webgl-2/651166, quote: "WebGL 2 supports transform feedback, so it is in theory capable of running our GPU Skinning implementation. However, in all our tests, it was consistently slower then CPU skinning in current WebGL 2 implementations, which is why we disabled it." So skinning on WebGL2 does run on the CPU, but the stated reason is speed, not the lack of compute. Whether Unity 6 still disables it: **UNVERIFIED** (the post is from 2017); check in the Profiler on the web build. |
| L110: "<= 511 instances per batch, fewer on GLES3 within the 16 KB UBO" | **511: VERIFIED** (RenderMeshInstanced page, item 7 #1). **16 KB: secondary sources only** | Unity sizes the per-batch array as "the maximum constant buffer size on the target device" divided by the per-instance struct size; `maxcount` defaults to 500 (Unity Manual, gpu-instancing-shader, docs.unity.cn mirror https://docs.unity.cn/Manual/gpu-instancing-shader.html). The 16 KB figure appears only in Catlike Coding (https://catlikecoding.com/unity/tutorials/rendering/part-19/) and Unity's BRG GLES blog ("a widely accepted value is 16 KiB", https://unity.com/blog/engine-platform/batchrenderergroup-sample-high-frame-rate-on-budget-devices). INFERRED: two float4x4 matrices per instance (128 B) give ~128 instances per 16 KB batch, which is plenty at <= 64 drawn. |
| L113: "Instancing and vertex texture fetch are in core GLES3 / WebGL2" | **VERIFIED** | Khronos WebGL 2.0 spec, "derived from OpenGL ES 3.0", with drawArraysInstanced, drawElementsInstanced and vertexAttribDivisor in core. The OpenGL ES 3.0 reference card gives `gl_MaxVertexTextureImageUnits` a minimum of 16. See item 7 #6. |
| L119: "BatchRendererGroup / Entities Graphics rely on SSBO / compute paths that WebGL2 lacks or emulates" | **Conclusion VERIFIED, reason partly wrong** | BRG is not supported on WebGL (Unity 6.0 BRG platform list, item 7 #5). But BRG does not strictly need SSBOs: on GLES it falls back to UBO/constant-buffer mode (Unity BRG GLES blog above). Reword as "BRG is not supported on the WebGL platform". Entities Graphics was not checked separately (it is built on BRG). |
| L120: "DrawMeshInstancedIndirect needs compute buffers" | **VERIFIED** | Unity ScriptReference, Graphics.RenderMeshIndirect (the successor API): "This function only works on platforms that support compute shaders." https://docs.unity3d.com/ScriptReference/Graphics.RenderMeshIndirect.html. The Unity 6.7 web graphics comparison also names indirect rendering as a WebGPU feature. |

## Item 6b. Player reception of ambient fish (follow-up for f-designer and team-lead)

| Game | Reception found | Source |
|---|---|---|
| **The Angler** (Expansive Worlds, 2022) | Reviewer Bill Lavoy (Shacknews, 30 Aug 2022): fish are found by "seeing a fish jump and targeting that spot, or seeing fish below the surface and dropping your line on top of them", but "it's rough to watch them move below the surface of the water because the animations are a bit clunky." | https://www.shacknews.com/article/132077/cotw-the-angler-review |
| **RDR2** (Rockstar) | Players praise fish behaviour as realistic detail: a Reddit find that fish use waterfalls to travel "Just as they might in reality", some landing on a ledge instead. The coverage frames it as part of the game's "attention to detail". | GameRant, Tom Bowen, 16 Apr 2021, https://gamerant.com/red-dead-redemption-2-fish-waterfall-migration-detail/ |
| **Valheim** (Iron Gate) | Players accept visible fish as the rule: "you should only fish where you can see fish swimming in the water"; "if you don't SEE any fish, you likely won't catch anything" (Steam, March 2022). Complaints: respawns take ~3-5 in-game days, spots move, and fish refresh 40-80 m from the player rather than at a dock. Patch 0.213.4 changed "Fish no longer despawn when far away" and added "Fish will wobble and splash in the water when they try to escape". | https://steamcommunity.com/app/892970/discussions/0/3185739394664972760/ ; https://steamcommunity.com/app/892970/discussions/0/4339851480063887730/ ; https://www.pcgamesn.com/valhiem/update-0-213-4-patch-notes |
| **Sons of the Forest** (Endnight) | No sentiment about ambient fish was found; Steam complaints are about fish despawning from drying racks, which doesn't apply. **UNSOURCED** | - |
| **The Long Dark** | No sentiment about fish visibility was found. **UNSOURCED** | - |
| **theHunter: Call of the Wild** | **Confirmed: no fishing.** Expansive Worlds released fishing as a separate game, described in the press release as "a new open world fishing experience" (Avalanche Studios Group). | https://avalanchestudios.com/stories/expansive-worlds-reveals-call-of-the-wild-the-angler |

What carries over (INFERRED):
- The one critical review note is about clunky underwater animation (The Angler).
- Praise goes to natural behaviour (RDR2).
- Complaints are about respawn logic and where fish appear (Valheim).

This supports N1/N2: mostly surface signs, brief sightings, and no visible spawning.

## Item 5b. Freshwater disturbance distance, settle time, surface rates

[5.5] Draštík, V. and Kubečka, J. (2005). Fish avoidance of acoustic survey boat in shallow waters. *Fisheries Research* 72. https://www.sciencedirect.com/science/article/abs/pii/S0165783604002577 (abstract via search; the publisher page returned 403)
- Two lakes and two reservoirs, measured with horizontal sonar. Quote: "most avoidance behaviour was found with small fish (TS < -40 dB, 22 cm) at distances under 10 m, with some indications of avoidance up to a distance of 15 m from the survey boat only in the clear lake Wallersee... at distances over 10 m, the avoidance of small boats (5-6 m long, 15-25 HP two-stroke engine) appears not to be a serious problem in shallow waters."
- Use: freshwater fish react to a **motor boat** within about 10 m, and up to 15 m in clear water. A wader is quieter (INFERRED: smaller radius). This is the best freshwater number found.

[5.6] Wheeland, L. J. and Rose, G. A. (2015). Quantifying fish avoidance of small acoustic survey vessels in boreal lakes and reservoirs. *Ecology of Freshwater Fish* 24(1): 67-76. https://onlinelibrary.wiley.com/doi/10.1111/eff.12126 (abstract via search)
- Drifting vs motoring passes from 5.5 m boats. Avoidance was not significant at one lake (median avoidance coefficient 0.81), but was significant at the Lac du Bonnet reservoir. Response depends on site and fish.

[5.7] Stuart, I. G. et al. (2006). Managing a migratory pest species: a selective trap for common carp. *N. Am. J. Fish. Manage.* 26(4): 888-893. https://onlinelibrary.wiley.com/doi/10.1577/M05-205.1 (abstract via search)
- Quote: "Trapped common carp display a pronounced escape behavior of jumping out of the water; this behavior is not exhibited by most Australian native fishes." The jumping cage separated 88% of adult carp. Common carp leap obstacles up to ~1 m.
- Use: common carp jumps are an escape or obstacle response. No source gives an open-water rate (**UNSOURCED**).

Settle/return time after disturbance:
- **UNSOURCED.** No freshwater study measured how long fish take to come back after a wader or boat passes. The only sourced recovery times are physiological, after being hooked (cardiovascular 1-3 h, metabolic 8-12 h; Cooke et al., https://www3.carleton.ca/fecpl/pdfs/C%20and%20R%20LMB%20SMB%20MS.pdf). Those are not ambient scatter-and-return; don't use them for that.
- Design value must be INFERRED.

Surface-activity rates:
- **Gar air breathing:**
  - Shortnose gar: under 1 breath per hour at 10-15.5 C, rising sharply above 15.5 C (Ohio J. Sci. paper, search abstract).
  - Spotted gar: breaths per hour rise from 55 to 80 F, and are higher at night (Roth et al., https://www.semanticscholar.org/paper/8852a5ddf79aafcc73df3422869ca88d23715f9f).
  - Spotted gar 20 to 30 C raises pulmonary ventilation (Smatresk & Cameron, J. Exp. Biol. 96: 281, https://journals.biologists.com/jeb/article/96/1/281/23343/).
  - **No sourced breaths-per-hour number for warm water.** The full-text tables were paywalled or returned 403.
- **Sunfish rises:** **UNSOURCED** (no rate study found).
- **Shad flipping:** qualitative only. Threadfin "rippling the surface at dawn and dusk" (Outdoor Alabama); gizzard shad "frequently leaps clear of the water or skips along the surface", active at dusk and night (MDC). No rate (**UNSOURCED**).
- **Common carp jumping:** escape or obstacle response [5.7]; no rate (**UNSOURCED**).

## Item 1b. TWRA Region 2 electrofishing CPUE (Cheatham / Old Hickory)

- **Old Hickory** (TWRA page, https://www.tn.gov/content/tn/twra/fishing/where-to-fish/middle-tennessee-r2/old-hickory-reservoir.html). Quote: "The 2019 spring electrofishing catch rate of Largemouth Bass exceeding 15 inches was 27/hour indicative of high relative abundance."
  - Also on that page: white crappie are the most abundant crappie; channel catfish predominate, with large flathead and blue cats; striped bass rely entirely on stocking because natural reproduction fails.
- **Cheatham:** no TWRA CPUE figure is published online (**UNSOURCED**). The Cheatham page has only qualitative rankings [1.2]. The only Cheatham catch rates online are invasive-carp gill nets and trawls [2.5].
- INFERRED: TWRA Region 2 reservoir reports are not posted publicly. Asking TWRA Region 2 is the only route to species-by-species CPUE.

## Item 8. Size and lifetime of fish surface disturbances (team-lead request)

**Bottom line:** no source states a measured ring diameter or visible duration for a fish rise, carp roll or jump splash (**UNSOURCED**). The angling literature describes these signs only qualitatively. The physics of small surface ripples gives a defensible INFERRED range, and it points to **short** lifetimes: a few seconds or less.

### 8a. Angling literature (qualitative; no sizes or durations)

[8.1] Rosenbauer, T. (2018). Understanding Trout Rise Forms. *Fly Fisherman*, 5 Mar 2018. https://www.flyfisherman.com/editorial/understanding-trout-rise-forms/152245
- Sipping rise: "concentric, unhurried rise with no splash and often no bubbles". It is "hard to spot", and you need to be "within 30 feet of the fish" (~9 m) to see it. **The only spotting distance found.**
- Classic rise: "head poking above the surface, often followed by its dorsal fin and a wag of the tail", with "one or more relatively large bubbles".
- Jumping rises: "not very common", often a single jump with "no follow-up activity".

[8.2] Richards, C. (1984; republished). Reading Riseforms. *Fly Fisherman*. https://www.flyfisherman.com/editorial/reading-riseforms/477351
- Rise types:
  - "Quiet dimple or soft swirl": "Almost noiseless".
  - "Big, showy swirls": large fish.
  - "Splashy swirls": "fairly small fish make a lot of noise, disproportionate to their size".
  - "Head and tail rise": medium to large fish in slow water.
  - "Porpoise roll": "large fish feed leisurely in slow currents".
- Subsurface signs: "Fish flashing" in deep runs; "Tailing" fish "rooting on the bottom".
- Use: splash size does not scale reliably with fish size. Big fish in slow water make slow rolls, not splashes.

[8.3] Schullery, P. (2011). Reading the Rise. *MidCurrent*, 10 Mar 2011. https://midcurrent.com/history/reading-the-rise/
- History of rise-form names: "bulges", "smutting" and "sipping" rises, "slash", "plunge". Quotes Davy (1828) on "the size of the tranquil undulation that follows their rise", and notes bubbles in "a dissipating riseform". No sizes or durations.

[8.4] RiverBum, Reading Trout Rise Forms. https://riverbum.com/fly-fishing-blog/reading-trout-rise-forms/
- "small rings" for sipping and a "large, pronounced ring" for splashy rises. Rings are hidden in broken (riffled) water. No numbers.

Carp and buffalo:
- Bowfishing and angling guides name the visible signs as "mudding" (silt clouds), backs or tails breaking the surface in shallows, and early-morning surface swirls (search summaries of bowhunter.com https://www.bowhunter.com/editorial/tactics_bh_bowfishing_0610/309813 and https://www.wired2fish.com/fishing-tips/carp-fishing-how-to-catch-carp; not fetched).
- Common carp "rolling" or "crashing": described qualitatively only. The parasite-dislodging explanation is from a non-authoritative site (scienceinsights.org), so treat it as **UNSOURCED**.
- Roll or boil diameter: **UNSOURCED**.

Jumping fish (silver carp as the only measured case; carp are OUT, but this bounds any jump):
- [8.5] Stell, E. (2018). Leaping behavior in Silver Carp... M.S. thesis, University of Mississippi. https://egrove.olemiss.edu/etd/374/. Quote: "mean leap heights of 124 cm with a maximum of 276.08 cm"; horizontal distance mean 207.02 cm, max 482.34 cm; burst speeds about 4.7-6.3 m/s. Most leaps (66.1%) left the water at 46-65 degrees (search abstract).
- Splash ring size from a jump: **UNSOURCED**.

### 8b. Ripple physics (sourced constants; ring sizes and lifetimes INFERRED)

[8.6] Wikipedia, Capillary wave. https://en.wikipedia.org/wiki/Capillary_wave. For air-water, the critical wavelength is "1.7 cm" with a minimum phase speed of "0.23 m/s". At that wavelength group velocity equals phase velocity. For shorter capillary waves, group velocity is 1.5 times the phase velocity.

[8.7] Armaroli, A. et al. (2018). Viscous damping of gravity-capillary waves: dispersion relations and nonlinear corrections. arXiv:1805.06777. https://ar5iv.labs.arxiv.org/html/1805.06777. Clean-surface viscous amplitude damping rate is **2 nu k^2**.

[8.8] Surface films damp ripples more strongly: "Organic surface films are visible as slicks on the water surface because capillary waves (wavelength <1.7 cm) are rapidly damped out" (ScienceDirect chapter "Organic Sea Surface Films", https://www.sciencedirect.com/science/chapter/bookseries/abs/pii/S0422989408703313; abstract via search). Lab confirmation: Cambridge JFM, surfactant effects on gravity-capillary wave dissipation, https://www.cambridge.org/core/journals/journal-of-fluid-mechanics/article/experimental-investigation-of-surfactant-effects-on-gravitycapillary-wave-dissipation-and-surface-flow/34CA9B8C58953FD5F64EDBF75BDEE2C3

INFERRED from 8.6-8.8. My arithmetic: nu = 1.0e-6 m^2/s for water at 20 C, k = 2 pi / wavelength.

| Ripple wavelength | Clean-water amplitude e-folding time 1/(2 nu k^2) | Ring growth speed (group velocity) |
|---|---|---|
| 5 mm | ~0.3 s | ~0.45 m/s (1.5 x phase speed ~0.3 m/s; INFERRED) |
| 1.7 cm | ~3.7 s | ~0.23 m/s |
| 5 cm | ~32 s, but low-steepness long waves are hard to see | ~0.18 m/s (computed from the gravity-capillary dispersion relation; phase speed ~0.30 m/s) |

- A dimple's fine ripples die in well under a second. The visible ring is carried by ~1-2 cm waves that fade over a few seconds. The ring grows at ~0.2-0.3 m/s, so after 2-4 s its **diameter is roughly 1-2 m**, and its amplitude has also dropped from spreading (~1/sqrt(r)) and damping. INFERRED.
- **Evidence for shorter durations:** river water carries natural organic films, which damp these ripples faster than clean water [8.8]. Current or any wind riffle hides rings entirely [8.4]. So, for a given wavelength, clean-water times are an upper bound. A first design estimate was about 1.5-3 s visible life for a dimple and 3-5 s for a big roll or jump splash. **Revised in 8c:** the big-roll range widens to 3-10 s on dead-calm water. Both are INFERRED; no measurement found.
- Spotting distance: sipping rises need about 9 m (30 ft) [8.1]. Bigger swirls and splashes are visible farther, but no figure was found (**UNSOURCED**). Glare and viewing angle matter: low-angle light shows "a wrinkle in the surface" (search summary of the Fly Fisherman sources).

### 8c. Contrary evidence: rings may last longer than 8b suggests

[8.9] Le Méhauté, B. (1988). Gravity-capillary rings generated by water drops. *J. Fluid Mech.* 197: 415-427. https://www.cambridge.org/core/journals/journal-of-fluid-mechanics/article/abs/gravitycapillary-rings-generated-by-water-drops/329DFD5008A3D8E83EB4D01FAC19ED51
- Quote: "the super-k_m components prevail at a short distance from the drop, whereas only the sub-k_m ones remain at a larger distance." The short, fast-damped ripples die close in. What travels out is the **longer** ring waves, and those damp slowly.

[8.10] Craeye, C., Sobieski, P. W., Bliven, L. F. and Guissard, A. (1999). Ring-waves generated by water drops impacting on water surfaces at rest. *IEEE J. Oceanic Eng.* 24(3): 323-332, doi 10.1109/48.775294. https://ieeexplore.ieee.org/document/775294/ (abstract via search)
- The measured characteristic wavenumber was about 0.2 per mm, i.e. a wavelength of ~3 cm. A secondary summary (https://www.researchgate.net/publication/225615602_Experiments_on_ring_wave_packet_generated_by_water_drop) gives the "largest waves" as amplitude ~100 micrometres and wavelength ~30 mm. Only about 1% of the drop's energy went into ring waves.
- INFERRED (my arithmetic): clean-water amplitude e-folding at a ~3 cm wavelength is 1/(2 nu k^2), about **12 s**, not 3.7 s. A rise or roll puts far more energy in than a raindrop, so its ring is bigger. In clean, flat water, then, viscosity alone does not end the ring within 3 s. **The ring fades mainly because it spreads and flattens**, falling below the background roughness and glare, and because of surface films [8.8].
- No source measures that visibility threshold (**UNSOURCED**).

Net assessment:
- Two lines of evidence point to SHORT visible life (about 1-3 s for dimples): fine ripples die fast [8.7], and river films and riffles damp or hide rings [8.4, 8.8].
- One line points LONGER: the dominant ~3 cm ring waves of a strong disturbance persist ~10 s in clean, flat water [8.9, 8.10]. On a glassy calm slack-water surface, a big roll's ring could plausibly stay faintly visible for 5-10 s as it widens to several metres. INFERRED.
- So the range for a big roll or splash on dead-calm water is 3-10 s. This is a judgement call for Noah's F8 check, not a sourced number.
