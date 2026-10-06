"""Fetch USGS 3DEP 1 m bare-earth DEM chunks in UTM 16N and cache them.

Chunks sit on a fixed global lattice (CHUNK m squares, pixel centres on whole
UTM metres), so a changed footprint reuses every chunk already on disk.
Cache: <cache>/dem_e<E>_n<N>_<CHUNK>.tif (+ .json with the server's extent).
"""
import json
import math
import time
import urllib.parse
import urllib.request
from pathlib import Path

import numpy as np

from tiff import read_tiff

SERVICE = "https://elevation.nationalmap.gov/arcgis/rest/services/3DEPElevation/ImageServer/exportImage"
EPSG = 26916
CHUNK = 4096
NODATA = -9999.0


def _get(url, timeout=300, tries=4):
    for attempt in range(tries):
        try:
            with urllib.request.urlopen(url, timeout=timeout) as resp:
                return resp.read()
        except Exception as exc:  # network flake: retry with backoff
            if attempt == tries - 1:
                raise
            wait = 5 * 2 ** attempt
            print(f"  fetch failed ({exc}); retry in {wait}s")
            time.sleep(wait)


def _fetch_chunk(e0, n0, tif_path, log):
    params = {
        "bbox": f"{e0 - 0.5},{n0 - 0.5},{e0 + CHUNK - 0.5},{n0 + CHUNK - 0.5}",
        "bboxSR": EPSG, "imageSR": EPSG, "size": f"{CHUNK},{CHUNK}",
        "format": "tiff", "pixelType": "F32", "noData": NODATA, "compression": "None",
        "interpolation": "RSP_BilinearInterpolation", "f": "json",
    }
    t0 = time.time()
    meta = json.loads(_get(SERVICE + "?" + urllib.parse.urlencode(params)))
    if "href" not in meta:
        raise RuntimeError(f"exportImage error: {meta}")
    ext = meta["extent"]
    if (meta["width"], meta["height"]) != (CHUNK, CHUNK) or abs(ext["xmin"] - (e0 - 0.5)) > 1e-3 \
            or abs(ext["ymin"] - (n0 - 0.5)) > 1e-3:
        raise RuntimeError(f"server changed the grid: {meta}")
    data = _get(meta["href"])
    tif_path.write_bytes(data)
    meta["request"] = params
    meta["fetched_utc"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    meta["seconds"] = round(time.time() - t0, 1)
    tif_path.with_suffix(".json").write_text(json.dumps(meta, indent=1))
    log(f"  fetched {tif_path.name}: {len(data) / 1e6:.1f} MB in {meta['seconds']} s")


def load_mosaic(e_min, n_min, width, height, cache_dir, log=print, refetch=False):
    """Return float32 [height, width] DEM (row 0 = south) whose pixel (r, c) is the
    elevation at UTM (e_min + c, n_min + r). NaN where the service has no data."""
    cache_dir = Path(cache_dir)
    cache_dir.mkdir(parents=True, exist_ok=True)
    out = np.full((height, width), np.nan, dtype=np.float32)
    ce0, cn0 = math.floor(e_min / CHUNK), math.floor(n_min / CHUNK)
    ce1, cn1 = math.floor((e_min + width - 1) / CHUNK), math.floor((n_min + height - 1) / CHUNK)
    stats = {"chunks": 0, "fetched": 0, "cached": 0}
    for ci in range(ce0, ce1 + 1):
        for cj in range(cn0, cn1 + 1):
            e0, n0 = ci * CHUNK, cj * CHUNK
            tif = cache_dir / f"dem_e{e0}_n{n0}_{CHUNK}.tif"
            stats["chunks"] += 1
            if refetch or not tif.exists() or not tif.with_suffix(".json").exists():
                _fetch_chunk(e0, n0, tif, log)
                stats["fetched"] += 1
            else:
                stats["cached"] += 1
            img = np.flipud(read_tiff(tif)).astype(np.float32)
            img[img <= NODATA + 1] = np.nan
            # overlap of this chunk with the requested window
            c_lo, c_hi = max(e0, e_min), min(e0 + CHUNK, e_min + width)
            r_lo, r_hi = max(n0, n_min), min(n0 + CHUNK, n_min + height)
            out[r_lo - n_min:r_hi - n_min, c_lo - e_min:c_hi - e_min] = \
                img[r_lo - n0:r_hi - n0, c_lo - e0:c_hi - e0]
    log(f"DEM mosaic {width}x{height} px: {stats['chunks']} chunks "
        f"({stats['fetched']} fetched, {stats['cached']} from cache)")
    return out, stats
