# tools/art-import: library textures to ship-size PNGs

Stages 1 and 2 of the art pipeline in the high-res environment report (section 8). Stage 3, the Unity import
rules, is `Assets/Editor/Art/LibraryTextureImport/`. Owner: platform-tools-engineer.

- **Stage 1, select and verify** (`verify.py`): pick each map by its file name, check sha256 and licence,
  and check that the role has the maps it needs.
- **Stage 2, repack and downscale** (`repack.py`): float64 maths, one rounding to 8-bit, packed mask maps,
  PNG output and a manifest entry per asset.

## Rules

- **The asset library is read-only.** `\\tank\public\AssetLibrary`, `Y:\AssetLibrary` and
  `ASSET_LIBRARY_ROOT` are only ever read. `common.guard_write()` refuses any write under them, under the
  repo's `_staging/`, or under an input folder. It compares normcased absolute path strings and never calls
  `Path.resolve()`, so it never touches the network share.
- **Nothing is written under `Assets/` without `--allow-assets`.**
- **Source zips are untrusted data.** `archive.py` extracts only map images, flat, into a new or empty
  folder. It refuses `..`, absolute or drive paths, backslashes, symlinks, more than 64 entries, an entry
  over 64 MB or a total over 512 MB. Both limits are checked on the declared sizes and again on the bytes
actually inflated. zipfile checks each entry's CRC32.

## Install and run

The toolchain is pinned, because output bytes depend on it (see "PNG and determinism"): **Python 3.13**,
numpy 2.4.2, Pillow 10.4.0 (libjpeg-turbo 3.0.3), pypng 0.20220715.0 and zlib 1.3.1. `requirements.txt`
pins them with hashes; `common.PINNED_TOOLCHAIN` holds the same values.

Install them into a venv outside the repo (not the user site), once per machine:

```
py -3.13 -m venv %LOCALAPPDATA%\washed-ashore\art-import-venv
%LOCALAPPDATA%\washed-ashore\art-import-venv\Scripts\python -I -m pip install --require-hashes -r tools/art-import/requirements.txt
```

**Run with `-I`.** The venv's own site-packages still load in isolated mode; `-I` drops the user site,
`PYTHON*` variables, and the script folder and cwd from `sys.path`. The entry point adds only its own
folder. Never run Python from inside an extraction folder. Below, `<py>` is the venv's `Scripts\python`.

```
# Every recipe in a folder: extract each zip into <work>/<source>/<id>/ (fresh), verify, repack into <out>
<py> -I tools/art-import/art_import.py run --recipes tools/art-import/recipes/ambientcg ^
    --staging _staging/ambientcg --work <scratch>/work --out <scratch>/out

# One step at a time
<py> -I tools/art-import/art_import.py extract --archive <zip> --into <new folder>
<py> -I tools/art-import/art_import.py verify  --recipe <json> --source-dir <folder> [--asset-json <asset.json>]
<py> -I tools/art-import/art_import.py repack  --recipe <json> --source-dir <folder> --out <folder> [--asset-json ...] [--allow-assets]
```

Exit codes: 0 ok, 1 a verify, repack or toolchain error, 2 usage. A recipe that fails (a bad zip, a
mislabelled image, a gray normal map, a missing map) is reported as `FAIL <recipe>` and the rest of the
batch still runs; the exit code is then 1. `run --ship-size N` overrides every recipe's size; it's for
proving the downscale only, so keep its output in a scratch folder.

`run` and `repack` refuse to write when the running toolchain differs from the pin, and name each
difference. `--allow-toolchain-drift` writes anyway, with a warning; outputs made that way must not be
committed.

To fill the Unity project, set `--out Assets/World/BellsBend/Art/Library --allow-assets` (decision I-2).
Outputs land in `<source>/<id>/`, and the manifest goes to `Data/art/art-manifest.json` (report section 8,
Stage 5), never under `Assets/`. `--manifest <path>` overrides it (a path under `Assets/` is refused). For any
other `--out` the manifest defaults to `<out>/art-import-manifest.json`, so a scratch run never touches the
project's manifest. A manifest whose out folder is in the repo records it as `out` (repo-relative) and
refuses a run into a different folder.

## Tests

```
<py> -I -m unittest discover -s tools/art-import/tests -t tools/art-import
```

`-t tools/art-import` puts the tool folder on `sys.path` (the tests import `common`, `maps` and the other
modules directly) and makes `tests` a package (`tests/__init__.py`). Every fixture image is generated into a
temp folder; nothing binary is committed and nothing reads the library. The PNG read-back tests need pypng
and are skipped without it.

## Recipe (`art-import-recipe/1`)

One JSON file per asset, LF, under `recipes/<source>/`. Unknown keys are errors.

| Key | Meaning |
|---|---|
| `schema` | `"art-import-recipe/1"` |
| `library_id`, `source`, `source_id` | `library_id` is `<source>:<source_id>` |
| `role` | `terrain-layer`, `lit`, `tree` or `plant` |
| `ship_size` | Long edge in pixels: a power of two from 256 to 4096, no larger than the source (no upscale). The aspect is kept, so Ground078 ships at 1024x512 |
| `tile_metres` | `[x, y]`; each must divide 1024 m (the terrain tile), like `AmbientCgLayers.CheckTileSizes` |
| `height_metres` | Real height range of the height map, > 0. The ambientCG values are **provisional manual guesses** |
| `license` | `CC0-1.0` or `LicenseRef-Fab-Standard` |
| `normal.convention` | `gl`, `dx` or `detect`. Only used when the file name doesn't say (Megascans `_Normal`); `detect` needs a height map |
| `input.archive`, `input.archive_sha256` | Source zip name and its pinned sha256 (or null) |
| `unity.layer_key`, `unity.slot`, `unity.legacy_smoothness` | Stage 4 data: TerrainLayer name, splat ID and the old constant smoothness (for comparison only) |
| `notes` | Optional |

## Map names (Stage 1)

The stem is split on `_`, `-` and `.` and scanned from the right; two-token names win over one-token names.

| Role | Tokens |
|---|---|
| albedo | Color, BaseColor, Albedo, diff, diffuse |
| normal_gl / normal_dx | NormalGL, nor_gl / NormalDX, nor_dx |
| normal_unknown | Normal (Megascans; the recipe or the detector decides) |
| roughness / gloss | Roughness, rough / Gloss, Glossiness |
| ao | AmbientOcclusion, AO |
| height | Displacement, disp, Height |
| metallic | Metalness, Metallic, metal |
| opacity | Opacity, Alpha |
| packed_arm | arm (Poly Haven: R AO, G roughness, B metal) |
| packed_rough_ao | rough_ao: **refused** until its layout is verified |

Only `.jpg`, `.jpeg` and `.png` count. Two files claiming one role is an error, except that NormalGL beats
NormalDX, roughness beats gloss, and separate maps beat arm; the loser is listed as `unused`.

| Role | Required |
|---|---|
| terrain-layer | albedo, a normal, roughness or gloss or arm, AO or arm, height |
| lit | albedo, a normal, roughness or gloss or arm (AO defaults to 1, metal to 0) |
| tree, plant | albedo, a normal (a plant without opacity is flagged) |

Hashes are checked against `asset.json` `files[]` when it's given (an unlisted file or a mismatch is an
error). Without it, the result is flagged `unverified_no_asset_json`, and each file is tied to the archive:
`archive:sha256` when the recipe pins the zip hash, else `archive:crc32`. The licence comes from
`asset.json` when present (and the recipe must agree), else from the recipe (`license_from_recipe`). Fab
needs `acquired_via: fab`; `bridge` is refused as Unreal-only.

`asset.json` `maps` (tools/asset-library's vocabulary) is compared with the roles found by file name, and
a difference only raises a flag such as `metadata_maps_disagree:folder_only=opacity;metadata_only=height`.
Only roles both tools name are compared (albedo, the normals, roughness, AO, height, metallic, opacity and
the packed maps), so translucency, emission, bump, specular and mask_soil are ignored, and every normal
counts as one role (the library files a bare `normal` as `normal_gl`).

## Outputs and packing (Stage 2), the Stage 3 suffix contract

`<out>/<source>/<id>/<id>_<Suffix>.png`. The suffix is what the Unity postprocessor keys on.

| Suffix | Roles | Content |
|---|---|---|
| `_Albedo` | all | sRGB RGB, resampled in linear light |
| `_AlbedoAlpha` | lit, tree, plant with opacity | sRGB RGB plus linear alpha |
| `_Normal` | all | OpenGL convention (Y+), renormalised |
| `_TerrainMask` | terrain-layer | R metallic, G AO, B height stretched 0.5–99.5 percentile to 0–1, A 1 − roughness |
| `_Mask` | lit, tree, plant | R metallic (0), G AO (1), B 0, A 1 − roughness (raw: no gamma, no squaring) |

- Decode: 8-bit /255, 16-bit /65535, float64 throughout; one rounding at the end, `floor(x*255+0.5)`.
- Resampling is separable Lanczos-3 with wrap-around edges, so tiling survives a downscale. The same
  size passes through untouched.
- Data maps stored as RGB use the channel mean; a real colour spread is flagged `gray_map_not_gray:<map>`.
- The terrain mask's `lo`/`hi` (the stretch range, in 0–1 source units) go in the manifest for Stage 4's
  height remap, with the recipe's `height_metres`.
- The albedo check computes luma Y' on the 8-bit output and flags `albedo_soft_range` (p1 < 30 or p99 > 240)
  or `albedo_strict_range` (p1 < 50 or p99 > 243). It never changes pixels.
- `smoothness.mean` (mean of the A channel) is recorded next to `unity.legacy_smoothness`, so the move
  from one constant per layer to per-texel smoothness can be measured.

### Normal convention detector

`normals.detect_y_convention()` correlates the normal map (decoded with no flip) with the height map's
gradient: a GL map has nx ~ −dh/dcol and ny ~ +dh/drow. It answers `gl` when both correlations are > 0.2,
`dx` when x > 0.2 and y < −0.2, otherwise none. With `detect`, no answer is an error that asks for an
explicit value. With an explicit or named convention, a disagreement is flagged `normal_convention_disagrees`
and no answer is flagged `normal_detector_inconclusive`. Every unused normal (for example NormalDX) is run
through the detector too and recorded under `normal.alternates`.

Measured on the 7 ambientCG layers (2026-11-13): every NormalGL gave `gl` and every NormalDX gave `dx`, with
|corr| from 0.56 (Grass001) to 0.95 (Ground054). The threshold isn't tuned; it has not yet been tried on real
Megascans assets.

## PNG and determinism

- The writer is our own (`pixels.encode_png`): IHDR, one IDAT and IEND only (no gAMA, sRGB, tIME or
  text), 8-bit, Paeth filter on every row, zlib level 9.
- **The primary check is the pixel hash**: sha256 of `"art-import-pixels/1 WxHxC u8\n"` plus the raw
  pixels. Pixels depend on the JPEG decoder (Pillow's libjpeg-turbo) and file bytes on the zlib build, so
  both are stable only on a pinned toolchain. The versions are recorded in every manifest entry, and
  `run` and `repack` refuse a toolchain that differs from `common.PINNED_TOOLCHAIN`.
- An output is not rewritten when its manifest entry's pixel and file hashes match the file on disk, or
  when the file is in the writer's own form (IHDR, IDAT and IEND only, with the same IHDR) and decodes
  (with pypng) to the same pixel hash. That keeps the mtime and the bytes. A file re-saved by an image
  editor (extra chunks, a palette) is rewritten.
- Outputs a recipe no longer makes (a role change turns `_Mask` into `_TerrainMask`; an opacity map turns
  `_Albedo` into `_AlbedoAlpha`) are removed, with any `.meta` beside them, when the previous manifest
  entry lists them. Any other `<id>_*.png` in the folder is flagged `stale_output:<file>`, not removed.
- The manifest (see "Install and run" for where it goes) is LF, sorted keys, indent 2, assets sorted by
  `library_id`. It holds no timestamps, no absolute paths and no per-run state; whether a file was written
  is printed by `run`, not stored. A rebuild of the same inputs with the same toolchain gives a
  byte-identical tree.
