"""Turn cached API metadata (_catalog/raw) into one asset.json per asset, plus _catalog/taxonomy.md.

    py tools/asset-library/build_inventory.py

Writes E:\\AssetLibrary\\<source>\\<type-dir>\\<id>\\asset.json. New assets start as
status "not_downloaded". Re-running keeps team edits (fields marked *_edited) and download state.
No network access; run fetch_metadata.py first.
"""
from __future__ import annotations

import collections
import re
import sys
from pathlib import Path

from common import CATALOG, RAW, asset_dir, guard_path, log, merge_record, read_json, today, write_json
from taxonomy import acg_category, normalize_category, normalize_tags, relevance, split_camel

PH_LICENSE = {"license": "CC0 1.0", "license_url": "https://polyhaven.com/license",
              "attribution_required": False}
ACG_LICENSE = {"license": "CC0 1.0", "license_url": "https://docs.ambientcg.com/license/",
               "attribution_required": False}

PH_TYPE_DIR = {0: "hdris", 1: "textures", 2: "models"}
ACG_TYPE_DIR = {"material": "materials", "substance": "substances", "decal": "decals", "atlas": "atlases",
                "3d-model": "models", "plain-image": "images", "brush": "brushes", "terrain": "terrains",
                "hdri": "hdris", "hdri-element": "hdris"}
ACG_TYPE = {"material": "material", "substance": "material", "decal": "decal", "atlas": "atlas",
            "3d-model": "model", "plain-image": "image", "brush": "brush", "terrain": "terrain",
            "hdri": "HDRI", "hdri-element": "HDRI"}
IMPERFECTION_CATS = {"SurfaceImperfections", "SurfaceImperfectionsSubstance", "Fingerprints",
                     "FingerprintsSubstance", "Smear", "Scratches", "ScratchesSubstance", "Chip",
                     "WrinklesBrush", "Leaking", "LeakingSubstance"}

# Map names: source key (or suffix) -> shared name
MAP_NAMES = {"diffuse": "albedo", "diff": "albedo", "color": "albedo", "nor_gl": "normal_gl",
             "nor_dx": "normal_dx", "normal": "normal_gl", "rough": "roughness", "roughness": "roughness",
             "ao": "ao", "ambient-occlusion": "ao", "displacement": "height", "disp": "height",
             "metal": "metallic", "metallic": "metallic", "metalness": "metallic", "opacity": "opacity",
             "alpha": "opacity", "translucent": "translucency", "scattering": "translucency",
             "emission": "emission", "emissive": "emission", "bump": "bump", "spec": "specular",
             "arm": "packed_arm", "rough_ao": "packed_rough_ao", "soil": "mask_soil"}
PACKED_LAYOUT = {"packed_arm": "R=AO, G=roughness, B=metallic",
                 "packed_rough_ao": "R=roughness? G=AO? (Poly Haven rough_ao; verify on download)"}

# Poly Haven texture maps we would download (skip DX normal, packed duplicates, blend/gltf/mtlx wrappers).
PH_SKIP = {"blend", "gltf", "mtlx", "fbx", "usd", "nor_dx", "arm", "rough_ao"}


def map_name(key: str) -> str:
    k = key.lower()
    if k in MAP_NAMES:
        return MAP_NAMES[k]
    for suffix, name in MAP_NAMES.items():
        if k.endswith("_" + suffix):
            return name
    return k


def tier_key(res: str) -> str:
    m = re.match(r"(\d+)k", res.lower())
    return f"{int(m.group(1))}k" if m else res.lower()


def base_record(source, sid, name, url, rtype, cat_orig, cat_text, tags_orig, extra_tags, lic, type_dir):
    rec = {
        "source": source, "source_id": sid, "name": name, "url": url, "type": rtype,
        "category": normalize_category(cat_text), "category_original": cat_orig,
        "tags": normalize_tags(tags_orig, extra_tags), "tags_original": list(tags_orig),
        "biome_or_region": None,
        **lic, "license_checked_on": today(), "license_source": "shown on source site/API at catalog time",
        "available_resolutions": [], "available_formats": [], "size_options": {},
        "maps": [], "packed_maps": {}, "normal_convention": None,
        "real_world_size_mm": None, "texel_density": None, "polycount": None, "lods": None,
        "billboards_or_impostors": None,
        "downloaded": {"resolutions": [], "formats": []}, "files": [], "download_date": None,
        "bells_bend_relevance": None, "bells_bend_reason": None, "candidate_for": [],
        "status": "not_downloaded", "notes": "", "thumbnail": None, "thumbnail_url": None,
        "derived_fields": [], "_type_dir": type_dir,
    }
    return rec


# ---------------------------------------------------------------- Poly Haven
def ph_records():
    assets = read_json(RAW / "polyhaven" / "assets.json") or {}
    missing = 0
    for aid, a in assets.items():
        tree = read_json(RAW / "polyhaven" / "files" / f"{aid}.json")
        if tree is None:
            missing += 1
            continue
        t = a["type"]
        attrs = a.get("attributes", {}) or {}
        extra = []
        for k in ("weather", "time_of_day", "environment", "condition", "setting", "surface_use", "material"):
            v = attrs.get(k)
            if isinstance(v, list):
                extra += v
            elif isinstance(v, str):
                extra.append(v)
        rtype = {0: "HDRI", 1: "material", 2: "model"}[t]
        cat = a.get("category") or "/".join(a.get("categories", []))
        if t == 2 and cat.startswith("Nature/") and any(s in cat for s in ("Plants", "Trees", "Ground Cover")):
            rtype = "plant"
        rec = base_record("polyhaven", aid, a["name"], f"https://polyhaven.com/a/{aid}", rtype, cat,
                          ("hdri " if t == 0 else "") + cat, a.get("tags", []),
                          extra + list(a.get("categories", [])),
                          PH_LICENSE, PH_TYPE_DIR[t])
        rec["thumbnail_url"] = a.get("thumbnail_url")
        rec["description"] = a.get("description")
        rec["authors"] = list((a.get("authors") or {}).keys())
        rec["attributes"] = attrs
        if a.get("coords"):
            rec["biome_or_region"] = f"lat/lon {a['coords']}"
        if t == 1 and a.get("dimensions"):
            rec["real_world_size_mm"] = a["dimensions"]
        if t == 2:
            rec["polycount"] = a.get("polycount")
            rec["lods"] = attrs.get("lods", a.get("lods"))
            rec["real_world_size_mm"] = a.get("dimensions")  # units undocumented for models
            if rec["real_world_size_mm"]:
                rec["derived_fields"].append("real_world_size_mm (Poly Haven model units undocumented)")
            rec["texel_density"] = a.get("texel_density")
        opts = collections.defaultdict(dict)
        maps = set()
        if t == 0:
            for res, fmts in (tree.get("hdri") or {}).items():
                for fmt, f in fmts.items():
                    opts[tier_key(res)][fmt] = f["size"]
            rec["available_formats"] = sorted({f for r in opts.values() for f in r})
        elif t == 1:
            map_keys = [k for k in tree if not k.startswith("_") and k not in PH_SKIP]
            maps = {map_name(k) for k in tree if not k.startswith("_") and k not in ("blend", "gltf", "mtlx")}
            tiers = set().union(*[set(tree[k].keys()) for k in map_keys]) if map_keys else set()
            for res in tiers:
                for variant, order in (("jpg", ("jpg", "png", "exr")), ("png", ("png", "jpg", "exr"))):
                    total = 0
                    for k in map_keys:
                        fm = tree[k].get(res, {})
                        f = next((fm[o] for o in order if o in fm), None)
                        total += f["size"] if f else 0
                    opts[tier_key(res)][variant] = total
            rec["available_formats"] = sorted({fmt for k in map_keys for r in tree[k].values() for fmt in r}
                                              | ({"blend"} if "blend" in tree else set())
                                              | ({"gltf"} if "gltf" in tree else set()))
        else:
            maps = {map_name(k) for k in tree if not k.startswith("_")
                    and k not in ("blend", "gltf", "fbx", "usd")}
            for fmt in ("fbx", "gltf", "usd", "blend"):
                for res, d in (tree.get(fmt) or {}).items():
                    f = d.get(fmt)
                    if f:
                        opts[tier_key(res)][fmt] = f["size"] + sum(i["size"] for i in f.get("include", {}).values())
            rec["available_formats"] = [f for f in ("fbx", "gltf", "usd", "blend") if f in tree]
        rec["size_options"] = {k: dict(v) for k, v in opts.items()}
        rec["available_resolutions"] = sort_tiers(opts.keys())
        rec["maps"] = sorted(maps)
        rec["packed_maps"] = {m: PACKED_LAYOUT[m] for m in maps if m in PACKED_LAYOUT}
        if maps & {"normal_gl", "normal_dx"}:
            rec["normal_convention"] = "GL and DX both available (GL chosen for Unity)"
        yield rec
    if missing:
        log(f"polyhaven: {missing} assets have no cached file tree yet (re-run fetch_metadata.py)")


# ---------------------------------------------------------------- ambientCG
ACG_ATTR = re.compile(r"^(?:(HQ|SQ|LQ)-)?(\d+)K(?:-(JPG|PNG))?(-ANIMATED)?$")


def acg_records():
    data = read_json(RAW / "ambientcg" / "assets.json") or {"assets": []}
    for a in data["assets"]:
        aid, at = a["id"], a["type"]
        cat = acg_category(aid)
        rtype = ACG_TYPE.get(at, at)
        if cat in IMPERFECTION_CATS:
            rtype = "imperfection"
        if cat in ("3DTreeStump",):
            rtype = "plant"
        rec = base_record("ambientcg", aid, a.get("title") or aid, a.get("url") or f"https://ambientcg.com/a/{aid}",
                          rtype, cat, split_camel(cat) + " " + at, a.get("tags", []), [a.get("technique") or ""],
                          ACG_LICENSE, ACG_TYPE_DIR.get(at, at))
        th = a.get("thumbnails") or {}
        rec["thumbnail_url"] = th.get("256-PNG") or th.get("512-PNG") or next(iter(th.values()), None)
        rec["description"] = a.get("shortDescription")
        rec["technique"] = a.get("technique")
        rec["collections"] = a.get("collections")
        dims = a.get("dimensions") or {}
        if any(dims.values()):
            # ambientCG dimensions are in centimetres (e.g. Concrete049 = 110 x 110); unit not stated in v3 docs.
            rec["real_world_size_mm"] = [dims.get("width", 0) * 10, dims.get("height", 0) * 10,
                                         dims.get("depth", 0) * 10]
            rec["derived_fields"].append("real_world_size_mm (ambientCG cm assumed)")
        maps = {map_name(m) for m in a.get("maps", [])}
        rec["maps"] = sorted(maps)
        if "normal_gl" in maps:
            rec["normal_convention"] = "ambientCG ships GL and DX normals in the zip (assumed; verify on download)"
            rec["derived_fields"].append("normal_convention")
        opts = collections.defaultdict(dict)
        urls = {}
        for d in a.get("downloads", []):
            attr, ext, size = d["attributes"], d.get("extension", "zip"), d.get("size") or 0
            m = ACG_ATTR.match(attr)
            if attr.startswith("COMPILED"):
                tier, variant = "native", "sbsar-xl" if "XL" in attr else "sbsar"
            elif m:
                q, k, fmt, anim = m.groups()
                tier = f"{int(k)}k"
                variant = (fmt or ext).lower()
                if q:
                    variant = f"{q.lower()}-{variant}"
                if anim:
                    variant += "-animated"
                if ext not in ("zip",) and not fmt:
                    variant = ext
            else:
                tier, variant = "other", attr.lower()
            opts[tier][variant] = size
            urls[f"{tier}/{variant}"] = d.get("url")
        rec["size_options"] = {k: dict(v) for k, v in opts.items()}
        rec["available_resolutions"] = sort_tiers(opts.keys())
        rec["available_formats"] = sorted({v for t in opts.values() for v in t})
        rec["_download_urls"] = urls
        yield rec


def sort_tiers(tiers):
    def k(t):
        m = re.match(r"(\d+)k$", t)
        return (0, int(m.group(1))) if m else (1, t)
    return sorted(tiers, key=k)


def main() -> int:
    counts = collections.Counter()
    mapping = collections.Counter()
    seen = set()
    for gen in (ph_records(), acg_records()):
        for rec in gen:
            level, reason = relevance(rec)
            rec["bells_bend_relevance"], rec["bells_bend_reason"] = level, reason
            type_dir = rec.pop("_type_dir")
            d = asset_dir(rec["source"], type_dir, rec["source_id"])
            guard_path(d)
            path = d / "asset.json"
            old = read_json(path)
            rec = merge_record(rec, old)
            if old:
                rec["first_cataloged"] = old.get("first_cataloged", today())
            else:
                rec["first_cataloged"] = today()
            write_json(path, rec)
            seen.add(str(path))
            counts[(rec["source"], rec["type"])] += 1
            mapping[(rec["source"], rec["category_original"].split("/")[0] if rec["source"] == "polyhaven"
                     else rec["category_original"], rec["category"])] += 1
    for (s, t), n in sorted(counts.items()):
        log(f"{s:10} {t:12} {n}")
    write_taxonomy(mapping)
    return 0


def write_taxonomy(mapping):
    from taxonomy import CATEGORY_RULES, TAG_SYNONYMS
    lines = ["# Asset library taxonomy", "",
             "Generated by `tools/asset-library/build_inventory.py`. Each record keeps its source's own "
             "category (`category_original`) and tags (`tags_original`) next to the shared ones.", "",
             "## Shared types", "",
             "`material`, `model`, `plant`, `atlas`, `decal`, `HDRI`, `imperfection`, plus `terrain`, "
             "`brush` and `image` for the few ambientCG assets that fit none of those.", "",
             "## Shared categories (rule order: first match wins)", "",
             "| Shared category | Matches source categories containing |", "|---|---|"]
    lines += [f"| `{n}` | {', '.join(k)} |" for n, k in CATEGORY_RULES]
    lines += ["| `misc` | anything else |", "", "## Observed mapping", "",
              "| Source | Source category | Shared category | Assets |", "|---|---|---|---|"]
    lines += [f"| {s} | {c} | `{n}` | {k} |" for (s, c, n), k in sorted(mapping.items())]
    lines += ["", "## Tag synonyms", "", "| Source tag | Shared tag |", "|---|---|"]
    lines += [f"| {a} | {b} |" for a, b in sorted(TAG_SYNONYMS.items())]
    lines += ["", "Numeric tags and id echoes (for example `concrete049`) are dropped. Poly Haven attributes "
              "(weather, time of day, condition, setting) and the ambientCG technique are added as tags.", ""]
    p = guard_path(CATALOG / "taxonomy.md")
    p.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main())
