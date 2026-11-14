"""Stage 1: select and verify the source maps of one recipe. Reads only; never writes.

The source folder is read flat (its top level only). File names decide the map roles; asset.json
metadata that disagrees only raises a flag. Hashes are checked against asset.json files[] when it is
present, else against the archive the files came from.
"""
from __future__ import annotations

import os

import maps
from common import sha256_file

LICENSE_ALLOWLIST = ("CC0-1.0", "LicenseRef-Fab-Standard")
_LICENSE_NAMES = {
    "cc0": "CC0-1.0", "cc0 1.0": "CC0-1.0", "cc0-1.0": "CC0-1.0", "cc0 1.0 universal": "CC0-1.0",
    "creative commons zero": "CC0-1.0", "public domain (cc0)": "CC0-1.0",
    "fab standard license": "LicenseRef-Fab-Standard", "fab standard": "LicenseRef-Fab-Standard",
    "licenseref-fab-standard": "LicenseRef-Fab-Standard",
}

# Roles both art-import and tools/asset-library (build_inventory.MAP_NAMES, download.MAP_PATTERNS) name.
# asset.json maps[] also holds translucency, emission, bump, specular and mask_soil, which art-import never
# classifies, and has no gloss; those are left out of the comparison. Normals are compared as one "normal"
# role, because build_inventory calls a bare "normal" normal_gl where maps.py says normal_unknown.
METADATA_SHARED_ROLES = frozenset({"albedo", "normal_gl", "normal_dx", "normal_unknown", "roughness", "ao", "height",
                                   "metallic", "opacity", "packed_arm", "packed_rough_ao"})

# Preference when one role is claimed by more than one file: the first present is used, the rest are unused.
_NORMAL_PREFERENCE = ("normal_gl", "normal_dx", "normal_unknown")


def normalise_license(s):
    if not isinstance(s, str):
        return None
    return _LICENSE_NAMES.get(" ".join(s.strip().lower().split()), s.strip())


def _required(role: str, have: set) -> list[str]:
    missing = []
    if "albedo" not in have:
        missing.append("albedo")
    if not have & set(maps.NORMAL_ROLES):
        missing.append("normal (gl, dx or unknown)")
    if role in ("terrain-layer", "lit") and not have & {"roughness", "gloss", "packed_arm"}:
        missing.append("roughness, gloss or arm")
    if role == "terrain-layer":
        if not have & {"ao", "packed_arm"}:
            missing.append("ao or arm")
        if "height" not in have:
            missing.append("height")
    return missing


def _fold_normals(roles) -> set:
    return {"normal" if r in maps.NORMAL_ROLES else r for r in roles}


def metadata_disagreement(meta_maps, found_roles):
    """The flag for asset.json maps[] against the roles found by file name, or None when they agree.

    Only METADATA_SHARED_ROLES are compared. The flag names both differences, e.g.
    "metadata_maps_disagree:folder_only=opacity;metadata_only=height".
    """
    meta = _fold_normals({m for m in meta_maps if isinstance(m, str)} & METADATA_SHARED_ROLES)
    have = _fold_normals(set(found_roles) & METADATA_SHARED_ROLES)
    if meta == have:
        return None
    folder_only = "+".join(sorted(have - meta)) or "-"
    metadata_only = "+".join(sorted(meta - have)) or "-"
    return f"metadata_maps_disagree:folder_only={folder_only};metadata_only={metadata_only}"


def _match_listed(rel_posix: str, files: list):
    """asset.json files[] entries whose path ends with the file's relative path (whole components)."""
    want = rel_posix.split("/")
    out = []
    for f in files:
        p = str(f.get("path", "")).replace("\\", "/").split("/")
        if len(p) >= len(want) and p[-len(want):] == want:
            out.append(f)
    return out


def verify(recipe: dict, source_dir, asset_json: dict | None = None, archive_info: dict | None = None) -> dict:
    errors: list[str] = []
    flags: list[str] = []
    found: dict[str, list[str]] = {}
    for name in sorted(os.listdir(source_dir)):
        if not os.path.isfile(os.path.join(source_dir, name)):
            continue
        role = maps.classify(name)
        if role is not None:
            found.setdefault(role, []).append(name)

    chosen: dict[str, str] = {}
    unused: list[dict] = []
    for role, names in sorted(found.items()):
        if len(names) > 1:
            errors.append(f"more than one file claims {role}: {', '.join(names)}")
            continue
        chosen[role] = names[0]
    normals = [r for r in _NORMAL_PREFERENCE if r in chosen]
    for r in normals[1:]:
        unused.append({"file": chosen.pop(r), "map": r, "reason": f"{normals[0]} preferred"})
    if "roughness" in chosen and "gloss" in chosen:
        unused.append({"file": chosen.pop("gloss"), "map": "gloss", "reason": "roughness preferred"})
    if "packed_arm" in chosen and {"ao", "roughness", "metallic"} <= set(chosen):
        unused.append({"file": chosen.pop("packed_arm"), "map": "packed_arm", "reason": "separate maps preferred"})
    if "packed_rough_ao" in chosen:
        errors.append(f"{chosen['packed_rough_ao']}: the rough_ao packing is refused until its layout is verified")

    role = recipe["role"]
    for m in _required(role, set(chosen)):
        errors.append(f"missing required map for {role}: {m}")
    if role == "plant" and "opacity" not in chosen:
        flags.append("plant_without_opacity")
    conv = recipe["normal"]["convention"]
    if conv == "detect" and "height" not in chosen:
        errors.append("normal convention 'detect' needs a height map; set gl or dx in the recipe")

    # Metadata is never trusted over names, only compared.
    if asset_json is not None and isinstance(asset_json.get("maps"), list):
        flag = metadata_disagreement(asset_json["maps"], found)
        if flag:
            flags.append(flag)

    for u in unused:
        u["sha256"] = sha256_file(os.path.join(source_dir, u["file"]))
    sources = []
    for r, name in sorted(chosen.items()):
        sources.append({"file": name, "map": r, "sha256": sha256_file(os.path.join(source_dir, name)),
                        "size": os.path.getsize(os.path.join(source_dir, name))})

    # Hashes.
    want_archive = recipe["input"].get("archive")
    pin = recipe["input"].get("archive_sha256")
    if asset_json is not None:
        files = asset_json.get("files") or []
        for s in sources:
            hits = _match_listed(s["file"], files)
            if not hits:
                errors.append(f"{s['file']}: not listed in asset.json files[]")
            elif len(hits) > 1:
                errors.append(f"{s['file']}: matches {len(hits)} asset.json files[] entries")
            elif hits[0].get("sha256") != s["sha256"]:
                errors.append(f"{s['file']}: sha256 {s['sha256']} doesn't match asset.json {hits[0].get('sha256')}")
            else:
                s["verified_against"] = "asset.json"
    else:
        flags.append("unverified_no_asset_json")
        basis = "none"
        if archive_info is not None:
            if want_archive and archive_info["name"] != want_archive:
                errors.append(f"archive is {archive_info['name']}, the recipe names {want_archive}")
            if pin:
                if archive_info["sha256"] != pin:
                    errors.append(f"archive sha256 {archive_info['sha256']} doesn't match the recipe pin {pin}")
                basis = "archive:sha256"
            else:
                basis = "archive:crc32"
            crc = {e["name"]: e for e in archive_info.get("entries", [])}
            for s in sources:
                if s["file"] not in crc:
                    errors.append(f"{s['file']}: not extracted from the archive")
        else:
            flags.append("unverified_no_archive")
        for s in sources:
            s["verified_against"] = basis

    # Licence.
    rec_lic = normalise_license(recipe.get("license"))
    if asset_json is not None:
        lic = normalise_license(asset_json.get("license"))
        basis = "asset.json"
        if rec_lic != lic:
            errors.append(f"recipe licence {recipe.get('license')!r} doesn't match asset.json {asset_json.get('license')!r}")
        via = asset_json.get("acquired_via")
        if via == "bridge":
            errors.append("acquired_via bridge: Bridge-era assets are Unreal-only")
        if lic == "LicenseRef-Fab-Standard" and via != "fab":
            errors.append("Fab Standard License needs asset.json acquired_via 'fab'")
    else:
        lic, basis = rec_lic, "recipe"
        flags.append("license_from_recipe")
        if lic == "LicenseRef-Fab-Standard":
            errors.append("Fab Standard License needs an asset.json with acquired_via 'fab'")
    if lic not in LICENSE_ALLOWLIST:
        errors.append(f"licence {lic!r} is not on the allowlist ({', '.join(LICENSE_ALLOWLIST)})")

    return {"maps": dict(sorted(chosen.items())), "sources": sources, "unused": unused,
            "license": lic, "license_basis": basis, "flags": sorted(set(flags)), "errors": errors,
            "archive": None if archive_info is None else {"name": archive_info["name"], "sha256": archive_info["sha256"]}}
