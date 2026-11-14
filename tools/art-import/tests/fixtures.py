"""Generated test fixtures. Nothing binary is committed; every image is made here into a temp folder."""
from __future__ import annotations

import json
import os

import numpy as np

from common import sha256_file


def heightfield(h: int, w: int) -> np.ndarray:
    y, x = np.mgrid[0:h, 0:w].astype(np.float64)
    return np.sin(2 * np.pi * x / w) + np.cos(2 * np.pi * y / h)


def gl_normal_from_height(hf: np.ndarray, strength: float = 8.0) -> np.ndarray:
    """Analytic-ish GL normal (0..1 RGB) of a height field: n ~ (-dh/dcol, +dh/drow, 1)."""
    dcol = (np.roll(hf, -1, axis=1) - np.roll(hf, 1, axis=1)) * 0.5
    drow = (np.roll(hf, -1, axis=0) - np.roll(hf, 1, axis=0)) * 0.5
    n = np.stack([-dcol * strength, drow * strength, np.ones_like(hf)], axis=2)
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return (n + 1) / 2


def _save(path, arr01):
    from PIL import Image

    a = np.clip(np.floor(arr01 * 255 + 0.5), 0, 255).astype(np.uint8)
    img = Image.fromarray(a)
    img.save(path, format="JPEG", quality=95, subsampling=0)


def make_set(folder, asset_id="Fx001", size=(256, 256), maps=("Color", "NormalGL", "NormalDX", "Roughness",
                                                                 "AmbientOcclusion", "Displacement")):
    """ambientCG-style JPEG maps for a periodic synthetic surface. Returns {map: path}."""
    os.makedirs(folder, exist_ok=True)
    h, w = size
    hf = heightfield(h, w)
    rng = np.random.default_rng(1234)
    out = {}
    for m in maps:
        if m == "Color":
            base = np.stack([0.45 + 0.1 * np.sin(2 * np.pi * np.arange(w) / w)[None, :].repeat(h, 0)] * 3, axis=2)
            base[:, :, 1] += 0.05
            arr = np.clip(base + rng.normal(0, 0.02, base.shape), 0, 1)
        elif m == "NormalGL":
            arr = gl_normal_from_height(hf)
        elif m == "NormalDX":
            arr = gl_normal_from_height(hf)
            arr[:, :, 1] = 1 - arr[:, :, 1]
        elif m == "Roughness":
            arr = np.full((h, w), 0.8) + rng.normal(0, 0.03, (h, w))
        elif m == "AmbientOcclusion":
            arr = 0.85 + 0.1 * (hf / 2)
        elif m == "Displacement":
            arr = 0.5 + 0.2 * hf
        elif m == "Metalness":
            arr = np.zeros((h, w))
        elif m == "Opacity":
            arr = (hf > 0).astype(np.float64)
        else:
            raise ValueError(m)
        p = os.path.join(folder, f"{asset_id}_1K-JPG_{m}.jpg")
        _save(p, np.clip(arr, 0, 1))
        out[m] = p
    return out


def recipe(**over):
    r = {
        "schema": "art-import-recipe/1", "library_id": "ambientcg:Fx001", "source": "ambientcg", "source_id": "Fx001",
        "role": "terrain-layer", "ship_size": 256, "tile_metres": [4, 4], "height_metres": 0.05,
        "license": "CC0-1.0", "normal": {"convention": "gl"},
        "input": {"archive": None, "archive_sha256": None}, "unity": {"layer_key": "Test", "slot": 0},
    }
    r.update(over)
    return r


def asset_json(folder, files, **over):
    """An asset.json shaped like tools/asset-library's, listing the given files with their real sha256."""
    a = {"source": "ambientcg", "source_id": "Fx001", "license": "CC0 1.0", "maps": [],
         "files": [{"path": f"ambientcg/materials/Fx001/1K-JPG/{os.path.basename(p)}", "size": os.path.getsize(p),
                    "sha256": sha256_file(p), "item": "1K/JPG"} for p in files]}
    a.update(over)
    return a


def write_json(path, obj):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f)
