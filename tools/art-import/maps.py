"""File name -> map role. File names are trusted over any metadata.

The stem is split into lower-case tokens on "_", "-" and ".". Tokens are scanned from the RIGHT, because
every source puts the map name after the asset name (ambientCG <id>_<res>-<fmt>_<Map>, Poly Haven
<name>_<map>_<res>, Megascans <id>_<res>_<Map>), so an asset name such as "metal_plate" can't win.
At each position a two-token map name (nor_gl, nor_dx, rough_ao) is tried before a single token.
"""
from __future__ import annotations

import os
import re

IMAGE_EXTS = (".jpg", ".jpeg", ".png")

BIGRAMS = {
    ("nor", "gl"): "normal_gl",
    ("nor", "dx"): "normal_dx",
    ("rough", "ao"): "packed_rough_ao",
}

UNIGRAMS = {
    "color": "albedo", "basecolor": "albedo", "albedo": "albedo", "diff": "albedo", "diffuse": "albedo",
    "normalgl": "normal_gl",
    "normaldx": "normal_dx",
    "normal": "normal_unknown",
    "roughness": "roughness", "rough": "roughness",
    "gloss": "gloss", "glossiness": "gloss",
    "ambientocclusion": "ao", "ao": "ao",
    "displacement": "height", "disp": "height", "height": "height",
    "metalness": "metallic", "metallic": "metallic", "metal": "metallic",
    "opacity": "opacity", "alpha": "opacity",
    "arm": "packed_arm",
}

NORMAL_ROLES = ("normal_gl", "normal_dx", "normal_unknown")
ALL_ROLES = tuple(sorted(set(UNIGRAMS.values()) | set(BIGRAMS.values())))

_SPLIT = re.compile(r"[_\-.]+")


def tokens(stem: str) -> list[str]:
    return [t for t in _SPLIT.split(stem.lower()) if t]


def classify_stem(stem: str):
    t = tokens(stem)
    for i in range(len(t) - 1, -1, -1):
        if i >= 1 and (t[i - 1], t[i]) in BIGRAMS:
            return BIGRAMS[(t[i - 1], t[i])]
        if t[i] in UNIGRAMS:
            return UNIGRAMS[t[i]]
    return None


def classify(filename: str):
    """Map role for an image file name, or None (not an image, or no map token: e.g. a preview <Id>.png)."""
    base = os.path.basename(filename.replace("\\", "/"))
    stem, ext = os.path.splitext(base)
    if ext.lower() not in IMAGE_EXTS:
        return None
    return classify_stem(stem)
