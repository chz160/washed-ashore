"""Channel packing for mask maps, the Poly Haven arm unpack, the height stretch and the albedo check.

All inputs are float64 HxW arrays in 0..1 at the output size; the outputs are float64 HxWx4.
"""
from __future__ import annotations

import numpy as np

STRETCH_PERCENTILES = (0.5, 99.5)
FLAT_EPS = 1e-6
SOFT_RANGE = (30, 240)
STRICT_RANGE = (50, 243)


def smoothness(rough=None, gloss=None) -> np.ndarray:
    """A = 1 - roughness, raw (no gamma, no squaring); gloss is used as it is."""
    if rough is not None:
        return 1.0 - rough
    if gloss is not None:
        return gloss
    raise ValueError("needs roughness or gloss")


def _filled(shape, value, x):
    return np.full(shape, value, dtype=np.float64) if x is None else x


def lit_mask(shape, *, metal=None, ao=None, smooth) -> np.ndarray:
    """URP Lit mask: R metallic (default 0), G AO (default 1), B 0 (detail mask unused), A smoothness."""
    return np.stack([_filled(shape, 0.0, metal), _filled(shape, 1.0, ao), np.zeros(shape), smooth], axis=2)


def height_stretch(h: np.ndarray):
    """Stretch the 0.5-99.5 percentile range of h to 0..1. Returns (b, lo, hi, flags)."""
    lo, hi = np.percentile(h, STRETCH_PERCENTILES, method="linear")
    lo, hi = float(lo), float(hi)
    if hi - lo < FLAT_EPS:
        return np.full(h.shape, 0.5), lo, hi, ["height_flat"]
    return np.clip((h - lo) / (hi - lo), 0.0, 1.0), lo, hi, []


def terrain_mask(shape, *, metal=None, ao=None, height, smooth):
    """Terrain mask: R metallic, G AO, B stretched height, A smoothness. Returns (rgba, lo, hi, flags)."""
    b, lo, hi, flags = height_stretch(height)
    rgba = np.stack([_filled(shape, 0.0, metal), _filled(shape, 1.0, ao), b, smooth], axis=2)
    return rgba, lo, hi, flags


def unpack_arm(rgb: np.ndarray):
    """Poly Haven arm: R AO, G roughness, B metal. Returns (ao, rough, metal)."""
    return rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]


def albedo_stats(u8_rgb: np.ndarray) -> tuple[dict, list]:
    """Luma Y' of the 8-bit sRGB output; flags only, never a fix."""
    rgb = u8_rgb[:, :, :3].astype(np.float64)
    y = 0.2126 * rgb[:, :, 0] + 0.7152 * rgb[:, :, 1] + 0.0722 * rgb[:, :, 2]
    p1, p99 = (float(v) for v in np.percentile(y, (1, 99), method="linear"))
    stats = {
        "p1": round(p1, 4), "p99": round(p99, 4), "mean": round(float(y.mean()), 4),
        "outside_soft": round(float(np.mean((y < SOFT_RANGE[0]) | (y > SOFT_RANGE[1]))), 6),
        "outside_strict": round(float(np.mean((y < STRICT_RANGE[0]) | (y > STRICT_RANGE[1]))), 6),
    }
    flags = []
    if p1 < SOFT_RANGE[0] or p99 > SOFT_RANGE[1]:
        flags.append("albedo_soft_range")
    if p1 < STRICT_RANGE[0] or p99 > STRICT_RANGE[1]:
        flags.append("albedo_strict_range")
    return stats, flags
