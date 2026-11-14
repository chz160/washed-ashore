# tools/asset-library: the local asset shelf

A browsable library of free, realistic assets from Poly Haven, ambientCG and Megascans, kept
**outside the repo** on the NAS at `\\tank\public\AssetLibrary\`, which is mapped as `Y:\AssetLibrary\`.
Nothing here imports into Unity. Python 3.13, standard library only. Owner: platform-tools-engineer.

The scripts use the UNC path, because a mapped drive letter exists only in one logon session.
Downloads are staged and verified on local disk at `E:\AssetLibrary-staging\` (`ASSET_LIBRARY_STAGING`)
and then extracted to the NAS. `catalog.sqlite` is built locally and copied over, because SQLite
locking is unreliable over SMB.

```
\\tank\public\AssetLibrary\
  <source>\<type-dir>\<asset-id>\asset.json   one record per asset (source of truth), plus files once downloaded
  megascans\_incoming\                          where the Epic Games Launcher drops Megascans
  _catalog\catalog.sqlite | catalog.csv | index.html | taxonomy.md | size_report.md
  _catalog\raw\                                 cached API metadata
  _logs\                                        download logs, failures.jsonl
```

Set `ASSET_LIBRARY_ROOT` to use another root. The scripts refuse to write inside `E:\GitHub\washed-ashore`.

## Re-run everything (Phase 0: metadata only)

From the repo root, in PowerShell use `py`:

```
python tools/asset-library/fetch_metadata.py      # API metadata into _catalog/raw (about 25 min, mostly Poly Haven)
python tools/asset-library/build_inventory.py     # raw metadata into asset.json files, plus taxonomy.md
python tools/asset-library/build_catalog.py       # asset.json files into sqlite, csv and index.html
python tools/asset-library/size_report.py         # size_report.md and size_report.json
```

Each step is idempotent:
- `fetch_metadata.py` skips Poly Haven file trees whose `files_hash` is unchanged. Pass `--refresh`
  to refetch them all.
- `build_inventory.py` keeps download state.
- Team edits survive a rebuild if you set the matching `*_edited: true` flag in `asset.json`. The flags
  are `bells_bend_relevance_edited`, `candidate_for_edited` and `notes_edited`.

## Being polite to the sources

- Requests run one at a time, at least 0.5 s apart on Poly Haven and 3 s apart on ambientCG.
- Every request sends the descriptive User-Agent in `common.py`. Poly Haven's API terms require one.
- Errors back off exponentially, honour `Retry-After`, and give up after 6 tries.
- We use official APIs only: Poly Haven `api.polyhaven.com`, ambientCG `/api/v3`. We never scrape either
  website; Poly Haven's site terms forbid it.
- Megascans come only through Fab in the Epic Games Launcher, on Noah's account. See
  [MEGASCANS.md](MEGASCANS.md). Never scrape or automate fab.com.

## Updating the catalog

- **Fix a relevance call:** edit `bells_bend_relevance` in that asset's `asset.json`, set
  `bells_bend_relevance_edited: true`, then run `build_catalog.py`.
- **Change the rules:** edit the keyword lists in `taxonomy.py`, then run `build_inventory.py` and
  `build_catalog.py`.
- **Shortlist an asset:** set `status` to `shortlisted`, `rejected` or `imported`, then run `build_catalog.py`.

## Adding a source

1. In `fetch_metadata.py`, add a `fetch_<source>()` that caches API responses under
   `_catalog/raw/<source>/`. Use official APIs only, and record the terms you read in the source's
   section below.
2. In `build_inventory.py`, add a `<source>_records()` generator:
   - Start each record with `base_record(...)`.
   - Fill `size_options` as `{tier: {variant: bytes}}`, with tiers like `2k` and `4k` and variants
     like `png`, `jpg`, `exr` and `fbx`.
   - Fill `maps` with the shared map names from `MAP_NAMES`.
   - Set the license fields as shown on the source today.
3. Add the source's category names to `CATEGORY_RULES` in `taxonomy.py` if needed, then rebuild.
   Check the "Observed mapping" table in `taxonomy.md` for anything that landed in `misc`.

## Sources and terms (checked 2026-11-13)

| Source | License | API and terms |
|---|---|---|
| Poly Haven | CC0 1.0, no attribution needed | [our-api](https://polyhaven.com/our-api) (free, including commercial use and bulk download); a unique User-Agent is required; [API ToS](https://github.com/Poly-Haven/Public-API/blob/master/ToS.md) forbids degrading the service |
| ambientCG | CC0 1.0, no attribution needed | [v3 docs](https://docs.ambientcg.com/api/v3/); v2 is deprecated; no rate limit published |
| Megascans | Fab Standard License | Free on Fab since 2026-11-05/12; via the Epic Games Launcher only. See MEGASCANS.md |

## Field notes

- **ambientCG:** `dimensions` are taken as centimetres. The unit is undocumented; the record lists it
  under `derived_fields`.
- **ambientCG categories:** ambientCG has no per-asset category. The category is the asset id without
  its number, for example `Concrete049` gives `Concrete`.
- **Poly Haven textures:** the size estimate counts albedo, normal (GL, Unity's convention), roughness,
  AO, height and any opacity or metal maps. It skips the DX normal, the packed `arm` and `rough_ao`
  maps, and the `blend`, `gltf` and `mtlx` wrappers.
- **Size tiers:** a tier means the largest one available up to the target. An asset whose maximum is
  2K counts its 2K files toward "4K".

## Phase 1: downloading

Noah approved this plan on 2026-11-13:
- **Scope:** C, everything.
- **Textures:** 4K + 2K PNG.
- **HDRIs:** 4K. Poly Haven sends EXR; the ambientCG zip contains an EXR.
- **Models:** FBX at 4K textures. ambientCG models only come as OBJ, which is allowed as the only option.
- **Substances:** `.sbsar`.

```
python tools/asset-library/download.py --dry-run     # what's left, in GB
python tools/asset-library/download.py               # run (resumable; re-run after any stop)
python tools/asset-library/download.py --max-rate 5  # cap total bandwidth at 5 MB/s
python tools/asset-library/download.py --verify      # also re-hash files already downloaded
python tools/asset-library/build_catalog.py          # refresh sqlite, csv and index.html afterwards
```

How it works:
- **Staging and verification:** files are staged in `E:\AssetLibrary-staging`. Each one is checked
  against the API's size, and its MD5 on Poly Haven, before it is copied to the NAS and hashed
  (SHA-256 recorded in `asset.json`).
- **Archives:** ambientCG zips are checked, extracted into `<tier>-<variant>\` and deleted. Add
  `--keep-archives` to keep them. `Thumbs.db`, `desktop.ini` and `__MACOSX` entries are skipped.
- **Re-runs:** an item is skipped when every recorded file is on disk at its recorded size.
  `--verify` also re-checks the SHA-256. A full re-hash of about 880 GB over the network is slow,
  so it isn't the default.
- **Logs:** progress goes to `_logs\download-<date>.log`, failures to `_logs\failures.jsonl`.
  Failures are retried once at the end of each run.
- **Run time:** at about 12 MB/s (the connection's ceiling), the full 880 GB takes about 20 hours.

Measured on the first downloads:
- ambientCG PNG normal and height maps are 16-bit, and its zips include both GL and DX normals.
- Poly Haven height maps are 16-bit PNG.

Still to come: `ingest_megascans.py`, to organize and catalog Megascans from `_incoming` once the
first Launcher batch lands, so the script can follow the real folder layout.
