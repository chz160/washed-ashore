# Megascans: getting them onto the shelf (Noah's steps)

Megascans come only through Epic's sanctioned route: Fab in the Epic Games Launcher, signed in with
Noah's Epic account. Our scripts never scrape, automate or call fab.com. They only organize and catalog
what the Launcher has already put in `Y:\AssetLibrary\megascans\_incoming\`.

## License, as recorded on 2026-11-13

- On **2026-11-05** Quixel announced that the whole Megascans library is free on Fab, with listings
  updating through **2026-11-12**
  ([Quixel announcement](https://quixel.com/news/update-megascans-are-going-free-again)).
- The license is the **Fab Standard License** ([fab.com/eula](https://www.fab.com/eula)). The summary
  on that page says it allows commercial use, use "with any compatible tools (usage is not limited to
  Unreal Engine)", no credit to the publisher, and sharing with collaborators on the project. It
  forbids standalone resale or redistribution.
- New with the 2026-11-05 change: AI training on Megascans and Megaplants is allowed. Listings show
  "Allows usage with AI: Yes".
- **Still to confirm, on a listing, before Phase 1:** that the listing shows the Fab Standard License
  with no Unreal-only restriction. Older guides (from before 2026-11) say free Megascans were for
  Unreal only. We only read the summary on fab.com/eula, not the full EULA text.
- Megaplants are Unreal 5.7+ procedural vegetation, so they're not usable in Unity. Skip them.

## Steps

1. **Check a listing.** On fab.com, open a Megascans listing such as a forest-floor surface. Confirm the
   price shows Free and the license shows the Fab Standard License. Tell Claude what the license section says, so
   it can be recorded with the date.
2. **Claim.** Add the assets you want to your library. No claim-all option or bundle was documented
   when this was written. If the Launcher's Fab tab or fab.com offers one, use it. Otherwise claim by
   collection or search, for the scope agreed in the Phase 0 report.
3. **Set the download location.** In the Epic Games Launcher, open Fab's settings and set the download
   or export folder to `Y:\AssetLibrary\megascans\_incoming\`. Epic's guide:
   <https://dev.epicgames.com/documentation/fab/exporting-assets-from-fab-in-launcher>.
4. **Format and quality.** Choose:
   - **3D assets and plants:** FBX.
   - **Surfaces, decals and imperfections:** the 4K or High tier, plus 2K if it costs little.
   - **Skip:** 8K and Raw.
   - **Texture format:** PNG, or EXR for displacement, if the Launcher asks.
5. **Batch download.** Select the claimed assets in the Launcher's Fab library and download them in
   batches of about 50 GB or less. The NAS has about 8 TB free, so space is not a concern.
6. **Hand back.** Tell Claude when a batch has landed. `ingest_megascans.py` (Phase 1) moves each
   asset to `megascans\<type>\<id>\` and writes its `asset.json`. It keeps any metadata JSON that
   Fab includes, such as tags, biome and physical size. Otherwise it derives what it can from the
   file and folder names and marks those fields as `derived`.
