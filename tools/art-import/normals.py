"""Normal map decode/encode, renormalisation and the Y-convention detector.

Output is always OpenGL convention (Y+ up, Unity's). DirectX input negates G.
"""
from __future__ import annotations

import numpy as np

DETECT_THRESHOLD = 0.2   # first guess; see README "Detector" for what the prove run measured


def decode(v: np.ndarray, convention: str = "gl") -> np.ndarray:
    """0..1 HxWxC (C >= 2) -> unit-ish vectors HxWx3 in -1..1, GL convention."""
    if v.ndim != 3 or v.shape[2] < 2:
        raise ValueError(f"a normal map needs at least 2 channels, got shape {v.shape}")
    n =v[:, :, :3].astype(np.float64) * 2.0 - 1.0
    if v.shape[2] < 3:
        x, y = n[:, :, 0], n[:, :, 1]
        z = np.sqrt(np.maximum(0.0, 1.0 - x * x - y * y))
        n = np.stack([x, y, z], axis=2)
    if convention == "dx":
        n[:, :, 1] = -n[:, :, 1]
    elif convention != "gl":
        raise ValueError(f"unknown normal convention {convention!r}")
    return n


def renormalise(n: np.ndarray) -> np.ndarray:
    n = np.clip(n, -1.0, 1.0)
    length = np.sqrt(np.sum(n * n, axis=2, keepdims=True))
    flat = np.zeros_like(n)
    flat[:, :, 2] = 1.0
    safe = np.where(length > 1e-12, length, 1.0)
    return np.where(length > 1e-12, n / safe, flat)


def encode(n: np.ndarray) -> np.ndarray:
    return (n + 1.0) * 0.5


def _pearson(a: np.ndarray, b: np.ndarray) -> float:
    a = a.ravel() - a.mean()
    b = b.ravel() - b.mean()
    d = np.sqrt(np.sum(a * a) * np.sum(b * b))
    return float(np.sum(a * b) / d) if d > 0 else 0.0


def detect_y_convention(n_raw: np.ndarray, height: np.ndarray, threshold: float = DETECT_THRESHOLD):
    """Guess the Y convention of a normal map from a height map of the same size.

    n_raw is the map decoded WITHOUT any flip (as if GL). With image rows running down and GL Y up,
    a GL map has nx ~ -dh/dcol and ny ~ +dh/drow. Returns ("gl" | "dx" | None, corr_x, corr_y).
    """
    h = height[:, :, 0] if height.ndim == 3 else height
    dcol = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) * 0.5
    drow = (np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) * 0.5
    cx = _pearson(n_raw[:, :, 0], -dcol)
    cy = _pearson(n_raw[:, :, 1], drow)
    if cx > threshold and cy > threshold:
        conv = "gl"
    elif cx > threshold and cy < -threshold:
        conv = "dx"
    else:
        conv = None
    return conv, round(cx, 6), round(cy, 6)
