"""Stage 2: repack and downscale one verified recipe into ship-size PNGs, plus its manifest entry.

Maths is float64 from decode to the single rounding step (pixels.to_u8). The manifest holds no
timestamps, no absolute paths and no per-run state, so a rebuild of the same inputs gives the same
bytes. Whether each output was written this run goes in the run log, not the manifest.
"""
from __future__ import annotations

import os

import numpy as np

import colour
import normals
import pack
import pixels
import resample
from common import (MANIFEST_SCHEMA, REPO, REPO_ASSETS, TOOL_VERSION, dump_json, guard_write, is_under, read_json,
                    sha256_file, toolchain, write_text_if_changed)
from recipe import recipe_sha256

GRAY_SPREAD_LIMIT = 4.0 / 255.0
MANIFEST_NAME = "art-import-manifest.json"
REPO_MANIFEST = os.path.join(REPO, "Data", "art", "art-manifest.json")   # report section 8, Stage 5


class RepackError(Exception):
    pass


def _gray(arr: np.ndarray, label: str, flags: list) -> np.ndarray:
    """A data map as HxW. RGB uses the channel mean; a real colour difference is flagged."""
    c = arr.shape[2]
    if c in (1, 2):
        return arr[:, :, 0]
    rgb = arr[:, :, :3]
    spread = rgb.max(axis=2) - rgb.min(axis=2)
    if float(np.percentile(spread, 99)) > GRAY_SPREAD_LIMIT:
        flags.append(f"gray_map_not_gray:{label}")
    return rgb.mean(axis=2)


def _resize2(x: np.ndarray, oh: int, ow: int) -> np.ndarray:
    return np.clip(resample.resize(x[:, :, None], oh, ow)[:, :, 0], 0.0, 1.0)


def _out_path(out_root, rel: str) -> str:
    return os.path.join(out_root, *rel.split("/"))


def _emit(out_root, rel, role, u8, prev_outputs, guard, run_log):
    path = _out_path(out_root, rel)
    px = pixels.pixel_sha256(u8)
    written, basis = True, "new"
    if os.path.exists(path):
        file_sha = sha256_file(path)
        prev = prev_outputs.get(rel)
        if prev and prev.get("pixel_sha256") == px and prev.get("file_sha256") == file_sha:
            written, basis = False, "manifest"
        else:
            try:
                with open(path, "rb") as f:
                    data = f.read()
                # Only a file in the writer's own form (IHDR, IDAT, IEND; same IHDR) may be kept by pixels.
                if pixels.is_writer_form(data, u8.shape):
                    existing, depth = pixels.read_png(path)
                    if depth == 8 and pixels.pixel_sha256(existing) == px:
                        written, basis = False, "pixels"
            except pixels.PixelError:
                pass
            if written:
                basis = "changed"
    if written:
        pixels.write_png(path, u8, **guard)
    h, w, c = u8.shape
    run_log.append({"file": rel, "written": written, "skip_basis": None if written else basis})
    return {"file": rel, "role": role, "width": w, "height": h, "channels": c,
            "pixel_sha256": px, "file_sha256": sha256_file(path)}


def _decode_normal(arr: np.ndarray, convention: str, name: str) -> np.ndarray:
    try:
        return normals.decode(arr, convention)
    except ValueError as e:
        raise RepackError(f"{name}: {e}") from e


def _remove_stale(out_root, rel_dir, sid, outputs, prev_outputs, guard, flags, run_log):
    """Outputs this recipe no longer makes. Files the previous manifest entry recorded are ours: they are
    removed (with a Unity .meta beside them). Any other <id>_*.png in the folder is only flagged."""
    now = {o["file"] for o in outputs}
    for rel in sorted(set(prev_outputs) - now):
        name = rel[len(rel_dir) + 1:]
        if not rel.startswith(f"{rel_dir}/") or "/" in name or not name.startswith(f"{sid}_") or name in ("", ".", ".."):
            raise RepackError(f"manifest lists an output outside {rel_dir}/: {rel!r}")
        path = _out_path(out_root, rel)
        for p in (path, path + ".meta"):
            if os.path.exists(p):
                guard_write(p, **guard)
                os.remove(p)
        run_log.append({"file": rel, "written": False, "skip_basis": None, "removed": True})
    folder = _out_path(out_root, rel_dir)
    if os.path.isdir(folder):
        for name in sorted(os.listdir(folder)):
            rel = f"{rel_dir}/{name}"
            if name.startswith(f"{sid}_") and name.lower().endswith(".png") and rel not in now:
                flags.append(f"stale_output:{name}")


def default_manifest_path(out_root) -> str:
    """REPO_MANIFEST (Data/art/art-manifest.json) for an out folder under the repo's Assets/, else
    <out>/art-import-manifest.json, so a scratch run never touches the project's manifest."""
    if is_under(out_root, REPO_ASSETS):
        return REPO_MANIFEST
    return os.path.join(out_root, MANIFEST_NAME)


def _out_label(out_root):
    """The out folder as a repo-relative POSIX path when it is inside the repo, else None (no absolute paths)."""
    if not is_under(out_root, REPO):
        return None
    return os.path.relpath(os.path.abspath(out_root), os.path.abspath(REPO)).replace(os.sep, "/")


def load_manifest(path) -> dict:
    if path and os.path.exists(path):
        m = read_json(path)
        if m.get("schema") != MANIFEST_SCHEMA:
            raise RepackError(f"{path}: unknown manifest schema {m.get('schema')!r}")
        return m
    return {"schema": MANIFEST_SCHEMA, "assets": []}


def save_manifest(path, manifest: dict, entry: dict, out_label=None, **guard) -> bool:
    assets = [a for a in manifest["assets"] if a["library_id"] != entry["library_id"]] + [entry]
    new = {"schema": MANIFEST_SCHEMA, "assets": sorted(assets, key=lambda a: a["library_id"])}
    if out_label is not None:
        new["out"] = out_label
    return write_text_if_changed(path, dump_json(new), **guard)


def repack(recipe: dict, vres: dict, source_dir, out_root, *, manifest_path=None, allow_assets=False, inputs=()):
    """Return (manifest entry, run log). vres is verify.verify()'s result and must have no errors.

    manifest_path defaults to default_manifest_path(out_root). It may never be under Assets/ (a manifest
    there would become a Unity TextAsset); output PNGs stay under out_root. A manifest records the out folder
    it describes when that folder is in the repo, and refuses a run into a different one.
    """
    if vres["errors"]:
        raise RepackError("verify failed: " + "; ".join(vres["errors"]))
    guard = {"inputs": (source_dir, *inputs), "allow_assets": allow_assets}
    flags = list(vres["flags"])
    chosen = vres["maps"]
    manifest_path = manifest_path or default_manifest_path(out_root)
    if is_under(manifest_path, REPO_ASSETS):
        raise RepackError(f"the manifest may not live under Assets/: {manifest_path}")
    out_label = _out_label(out_root)
    manifest = load_manifest(manifest_path)
    if manifest.get("out") != out_label and manifest["assets"]:
        raise RepackError(f"{manifest_path} describes out folder {manifest.get('out')!r}, not {out_label!r}")
    prev = next((a for a in manifest["assets"] if a["library_id"] == recipe["library_id"]), None)
    prev_outputs = {o["file"]: o for o in (prev or {}).get("outputs", [])}

    src = {}
    for role, name in chosen.items():
        arr, info = pixels.decode(os.path.join(source_dir, name))
        src[role] = arr
    sizes = {role: a.shape[:2] for role, a in src.items()}
    if len(set(sizes.values())) != 1:
        raise RepackError(f"source maps differ in size: {sizes}")
    h, w = next(iter(sizes.values()))
    try:
        oh, ow = resample.ship_dims(h, w, recipe["ship_size"])
    except ValueError as e:
        raise RepackError(str(e)) from e

    rel_dir = f"{recipe['source']}/{recipe['source_id']}"
    sid = recipe["source_id"]
    outputs, run_log = [], []

    # Albedo, filtered in linear light.
    a = src["albedo"]
    rgb = np.repeat(a[:, :, :1], 3, axis=2) if a.shape[2] in (1, 2) else a[:, :, :3]
    if a.shape[2] in (2, 4):
        flags.append("albedo_alpha_ignored")
    lin = resample.resize(colour.srgb_to_linear(rgb), oh, ow)
    alb = colour.linear_to_srgb(np.clip(lin, 0.0, 1.0))
    alb_u8 = pixels.to_u8(alb)
    albedo_stats, aflags = pack.albedo_stats(alb_u8)
    flags += aflags
    if recipe["role"] != "terrain-layer" and "opacity" in src:
        alpha = _resize2(_gray(src["opacity"], "opacity", flags), oh, ow)
        rgba = np.concatenate([alb_u8, pixels.to_u8(alpha)[:, :, None]], axis=2)
        outputs.append(_emit(out_root, f"{rel_dir}/{sid}_AlbedoAlpha.png", "AlbedoAlpha", rgba, prev_outputs, guard, run_log))
    else:
        outputs.append(_emit(out_root, f"{rel_dir}/{sid}_Albedo.png", "Albedo", alb_u8, prev_outputs, guard, run_log))

    # Normal: the convention comes from the file name; the recipe only decides an unnamed one.
    nrole = next(r for r in ("normal_gl", "normal_dx", "normal_unknown") if r in src)
    rec_conv = recipe["normal"]["convention"]
    conv_in = {"normal_gl": "gl", "normal_dx": "dx"}.get(nrole, rec_conv)
    if nrole != "normal_unknown" and rec_conv in ("gl", "dx") and rec_conv != conv_in:
        flags.append("recipe_convention_overridden_by_name")
    raw = _decode_normal(src[nrole], "gl", chosen[nrole])
    height_src = _gray(src["height"], "height", flags) if "height" in src else None
    detected = cx = cy = None
    if height_src is not None:
        detected, cx, cy = normals.detect_y_convention(raw, height_src)
    if conv_in == "detect":
        if detected is None:
            raise RepackError(f"normal convention not detectable (corr_x {cx}, corr_y {cy}); set gl or dx in the recipe")
        conv_used = detected
    else:
        conv_used = conv_in
        if height_src is not None:
            if detected is None:
                flags.append("normal_detector_inconclusive")
            elif detected != conv_in:
                flags.append("normal_convention_disagrees")
    n = _decode_normal(src[nrole], conv_used, chosen[nrole])
    n = normals.renormalise(np.clip(resample.resize(n, oh, ow), -1.0, 1.0))
    outputs.append(_emit(out_root, f"{rel_dir}/{sid}_Normal.png", "Normal",
                         pixels.to_u8(normals.encode(n)), prev_outputs, guard, run_log))
    alternates = []
    for u in vres["unused"]:
        if u["map"] in ("normal_gl", "normal_dx", "normal_unknown") and height_src is not None:
            alt, _ = pixels.decode(os.path.join(source_dir, u["file"]))
            d, ax, ay = normals.detect_y_convention(_decode_normal(alt, "gl", u["file"]), height_src)
            alternates.append({"file": u["file"], "map": u["map"], "detected": d, "corr_x": ax, "corr_y": ay})

    # Data maps, each resampled on its own, then packed.
    data = {}
    if "packed_arm" in src:
        ao, rough, metal = pack.unpack_arm(src["packed_arm"])
        data.update({"ao": ao, "roughness": rough, "metallic": metal})
    for key in ("ao", "roughness", "gloss", "metallic", "height"):
        if key in src:
            data[key] = _gray(src[key], key, flags)
    data = {k: _resize2(v, oh, ow) for k, v in sorted(data.items())}
    shape = (oh, ow)
    if "roughness" in data or "gloss" in data:
        smooth = pack.smoothness(data.get("roughness"), data.get("gloss") if "roughness" not in data else None)
    else:
        smooth = np.zeros(shape)
        flags.append("smoothness_default_0")
    stretch = None
    if recipe["role"] == "terrain-layer":
        mask, lo, hi, sflags = pack.terrain_mask(shape, metal=data.get("metallic"), ao=data.get("ao"),
                                                 height=data["height"], smooth=smooth)
        flags += sflags
        stretch = {"lo": round(lo, 8), "hi": round(hi, 8)}
        outputs.append(_emit(out_root, f"{rel_dir}/{sid}_TerrainMask.png", "TerrainMask",
                             pixels.to_u8(mask), prev_outputs, guard, run_log))
    else:
        mask = pack.lit_mask(shape, metal=data.get("metallic"), ao=data.get("ao"), smooth=smooth)
        outputs.append(_emit(out_root, f"{rel_dir}/{sid}_Mask.png", "Mask",
                             pixels.to_u8(mask), prev_outputs, guard, run_log))

    _remove_stale(out_root, rel_dir, sid, outputs, prev_outputs, guard, flags, run_log)

    entry = {
        "schema": MANIFEST_SCHEMA,
        "library_id": recipe["library_id"],
        "recipe_sha256": recipe_sha256(recipe),
        "recipe": recipe,
        "tool_version": TOOL_VERSION,
        "toolchain": toolchain(),
        "license": vres["license"],
        "license_basis": vres["license_basis"],
        "archive": vres["archive"],
        "sources": vres["sources"],
        "unused": vres["unused"],
        "source_size": [w, h],
        "normal": {"file_map": nrole, "convention_in": conv_in, "convention_used": conv_used,
                   "detected": detected, "corr_x": cx, "corr_y": cy, "alternates": alternates},
        "height_stretch": stretch,
        "smoothness": {"mean": round(float(smooth.mean()), 6),
                       "legacy_constant": recipe["unity"].get("legacy_smoothness")},
        "albedo_stats": albedo_stats,
        "outputs": outputs,
        "flags": sorted(set(flags)),
    }
    save_manifest(manifest_path, manifest, entry, out_label, **guard)
    return entry, run_log
