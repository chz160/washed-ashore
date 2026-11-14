"""Phase 0: pull metadata only (no asset files) from Poly Haven and ambientCG into _catalog/raw/.

    py tools/asset-library/fetch_metadata.py                 # both sources
    py tools/asset-library/fetch_metadata.py --source polyhaven
    py tools/asset-library/fetch_metadata.py --refresh       # refetch even if cached

Poly Haven: GET /assets?type=all, then GET /files/{id} once per asset (sequential, >= 0.5 s apart),
cached per asset and skipped on re-run unless files_hash changed. ambientCG: GET /api/v3/assets in
pages of 500 plus /api/v3/categories.
"""
from __future__ import annotations

import argparse
import sys

from common import RAW, http_get_json, log, read_json, write_json

PH_API = "https://api.polyhaven.com"
ACG_API = "https://ambientcg.com/api/v3"
ACG_INCLUDE = ("type,releaseDate,shortDescription,title,url,tags,colors,dimensions,"
               "maps,technique,downloads,relations,collections,thumbnails")


def fetch_polyhaven(refresh: bool) -> None:
    out = RAW / "polyhaven"
    assets = http_get_json(f"{PH_API}/assets?type=all")
    write_json(out / "assets.json", assets)
    log(f"polyhaven: {len(assets)} assets listed")
    files_dir = out / "files"
    done = fetched = 0
    for i, (aid, meta) in enumerate(sorted(assets.items()), 1):
        cache = files_dir / f"{aid}.json"
        cached = read_json(cache)
        if cached and not refresh and cached.get("_files_hash") == meta.get("files_hash"):
            done += 1
            continue
        try:
            tree = http_get_json(f"{PH_API}/files/{aid}", min_interval=0.5)
        except Exception as e:  # logged; re-run picks it up
            log(f"polyhaven: files/{aid} failed: {e}")
            continue
        tree["_files_hash"] = meta.get("files_hash")
        write_json(cache, tree)
        fetched += 1
        if fetched % 100 == 0:
            log(f"polyhaven: {i}/{len(assets)} file trees ({fetched} fetched, {done} cached)")
    log(f"polyhaven: done, {fetched} fetched, {done} already cached")


def fetch_ambientcg() -> None:
    out = RAW / "ambientcg"
    write_json(out / "categories.json", http_get_json(f"{ACG_API}/categories", min_interval=2))
    assets, offset = [], 0
    while True:
        page = http_get_json(f"{ACG_API}/assets?sort=alphabet&limit=500&offset={offset}&include={ACG_INCLUDE}",
                             min_interval=3, timeout=180)
        assets.extend(page["assets"])
        log(f"ambientcg: {len(assets)}/{page['totalResults']}")
        if not page.get("nextPageHttp") or not page["assets"]:
            break
        offset += 500
    ids = [a["id"] for a in assets]
    if len(ids) != len(set(ids)):
        log(f"ambientcg: warning, {len(ids) - len(set(ids))} duplicate ids across pages")
    write_json(out / "assets.json", {"totalResults": page["totalResults"], "assets": assets})


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", choices=["polyhaven", "ambientcg", "all"], default="all")
    ap.add_argument("--refresh", action="store_true")
    a = ap.parse_args()
    if a.source in ("ambientcg", "all"):
        fetch_ambientcg()
    if a.source in ("polyhaven", "all"):
        fetch_polyhaven(a.refresh)
    return 0


if __name__ == "__main__":
    sys.exit(main())
