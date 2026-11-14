"""Recipe loading and validation (schema art-import-recipe/1). See README.md for the field table."""
from __future__ import annotations

from common import RECIPE_SCHEMA, canonical_json, read_json, sha256_bytes

ROLES = ("terrain-layer", "lit", "tree", "plant")
CONVENTIONS = ("gl", "dx", "detect")
TERRAIN_TILE_METRES = 1024.0

_TOP_REQUIRED = {"schema", "library_id", "source", "source_id", "role", "ship_size", "tile_metres",
                 "height_metres", "license", "normal", "input", "unity"}
_TOP_OPTIONAL = {"notes"}
_INPUT_KEYS = {"archive", "archive_sha256"}
_UNITY_REQUIRED = {"layer_key", "slot"}
_UNITY_OPTIONAL = {"legacy_smoothness"}


class RecipeError(Exception):
    pass


def _num(v) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def validate(r) -> list[str]:
    e: list[str] = []
    if not isinstance(r, dict):
        return ["recipe must be a JSON object"]
    keys = set(r)
    for k in sorted(_TOP_REQUIRED - keys):
        e.append(f"missing key: {k}")
    for k in sorted(keys - _TOP_REQUIRED - _TOP_OPTIONAL):
        e.append(f"unknown key: {k}")
    if r.get("schema") != RECIPE_SCHEMA:
        e.append(f"schema must be {RECIPE_SCHEMA!r}")
    for k in ("library_id", "source", "source_id", "license"):
        if k in r and (not isinstance(r[k], str) or not r[k]):
            e.append(f"{k} must be a non-empty string")
    if isinstance(r.get("source"), str) and isinstance(r.get("source_id"), str) and isinstance(r.get("library_id"), str):
        if r["library_id"] != f"{r['source']}:{r['source_id']}":
            e.append("library_id must be '<source>:<source_id>'")
        for k in ("source", "source_id"):
            if any(c in r[k] for c in "/\\:.") or r[k] in ("", "..", "."):
                e.append(f"{k} must not contain path characters")
    if "role" in r and r["role"] not in ROLES:
        e.append(f"role must be one of {', '.join(ROLES)}")
    s = r.get("ship_size")
    if "ship_size" in r:
        if not isinstance(s, int) or isinstance(s, bool) or s < 256 or s > 4096 or s & (s - 1):
            e.append("ship_size must be a power of two from 256 to 4096 (the long edge)")
    t = r.get("tile_metres")
    if "tile_metres" in r:
        if not (isinstance(t, list) and len(t) == 2 and all(_num(v) and v > 0 for v in t)):
            e.append("tile_metres must be [x, y] with both > 0")
        else:
            for v in t:
                q = TERRAIN_TILE_METRES / v
                if abs(q - round(q)) > 1e-6:
                    e.append(f"tile_metres {v} doesn't divide {TERRAIN_TILE_METRES:g} m; it would seam at tile borders")
    if "height_metres" in r and not (_num(r["height_metres"]) and r["height_metres"] > 0):
        e.append("height_metres must be > 0")
    n = r.get("normal")
    if "normal" in r:
        if not isinstance(n, dict) or set(n) != {"convention"} or n.get("convention") not in CONVENTIONS:
            e.append(f"normal must be {{\"convention\": one of {', '.join(CONVENTIONS)}}}")
    i = r.get("input")
    if "input" in r:
        if not isinstance(i, dict):
            e.append("input must be an object")
        else:
            for k in sorted(set(i) - _INPUT_KEYS):
                e.append(f"unknown key: input.{k}")
            a = i.get("archive")
            if a is not None and (not isinstance(a, str) or "/" in a or "\\" in a or not a.lower().endswith(".zip")):
                e.append("input.archive must be a .zip file name (no folders) or null")
            h = i.get("archive_sha256")
            if h is not None and (not isinstance(h, str) or len(h) != 64 or any(c not in "0123456789abcdef" for c in h)):
                e.append("input.archive_sha256 must be 64 lower-case hex characters or null")
    u = r.get("unity")
    if "unity" in r:
        if not isinstance(u, dict):
            e.append("unity must be an object")
        else:
            for k in sorted(_UNITY_REQUIRED - set(u)):
                e.append(f"missing key: unity.{k}")
            for k in sorted(set(u) - _UNITY_REQUIRED - _UNITY_OPTIONAL):
                e.append(f"unknown key: unity.{k}")
            if "layer_key" in u and not isinstance(u["layer_key"], str):
                e.append("unity.layer_key must be a string")
            if "slot" in u and u["slot"] is not None and (not isinstance(u["slot"], int) or isinstance(u["slot"], bool) or u["slot"] < 0):
                e.append("unity.slot must be an integer >= 0 or null")
            if "legacy_smoothness" in u and not (_num(u["legacy_smoothness"]) and 0 <= u["legacy_smoothness"] <= 1):
                e.append("unity.legacy_smoothness must be in 0..1")
    if "notes" in r and not isinstance(r["notes"], str):
        e.append("notes must be a string")
    return e


def load(path) -> dict:
    r = read_json(path)
    errs = validate(r)
    if errs:
        raise RecipeError(f"{path}: " + "; ".join(errs))
    return r


def recipe_sha256(r) -> str:
    """Hash of the canonical JSON form, so whitespace and key order don't change it."""
    return sha256_bytes(canonical_json(r).encode("utf-8"))
