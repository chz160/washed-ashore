"""Shared helpers for the art import pipeline: version, write guard, hashing and LF JSON I/O.

The asset library (\\\\tank\\public\\AssetLibrary, mapped as Y:\\AssetLibrary) is READ-ONLY to this tool.
guard_write() refuses any write under a library root or an input folder. Library paths are compared as
normcased absolute strings and are never passed to Path.resolve(), which would touch the network share.
"""
from __future__ import annotations

import hashlib
import json
import os
import platform
import tempfile
import zlib
from pathlib import Path

TOOL_VERSION = "0.1.0"
RECIPE_SCHEMA = "art-import-recipe/1"
MANIFEST_SCHEMA = "art-import-manifest/1"
PIXEL_HASH_TAG = "art-import-pixels/1"

TOOL_DIR = Path(os.path.dirname(os.path.abspath(__file__)))
REPO = TOOL_DIR.parent.parent
REPO_ASSETS = REPO / "Assets"
REPO_STAGING = REPO / "_staging"

DEFAULT_READONLY_ROOTS = (r"\\tank\public\AssetLibrary", r"Y:\AssetLibrary")


class GuardError(Exception):
    """A write was refused."""


def norm(p) -> str:
    """normcase(abspath(p)) without touching the file system beyond the cwd (no resolve, no stat)."""
    return os.path.normcase(os.path.abspath(os.fspath(p)))


def is_under(p, root) -> bool:
    a, r = norm(p), norm(root)
    if a == r:
        return True
    r = r if r.endswith(os.sep) else r + os.sep
    return a.startswith(r)


def readonly_roots() -> list[str]:
    roots = list(DEFAULT_READONLY_ROOTS)
    env = os.environ.get("ASSET_LIBRARY_ROOT")
    if env:
        roots.insert(0, env)
    return roots


def guard_write(p, *, inputs=(), allow_assets: bool = False):
    """Return p if writing there is allowed; raise GuardError otherwise.

    Refused: anything under a library root (always read-only), the repo's _staging folder, any of the
    given input folders, and the repo's Assets/ unless allow_assets is set.
    """
    for root in readonly_roots():
        if is_under(p, root):
            raise GuardError(f"refusing to write under the read-only asset library {root}: {p}")
    for root in (REPO_STAGING, *inputs):
        if root and is_under(p, root):
            raise GuardError(f"refusing to write under an input folder {root}: {p}")
    if not allow_assets and is_under(p, REPO_ASSETS):
        raise GuardError(f"refusing to write under {REPO_ASSETS} without --allow-assets: {p}")
    return p


def sha256_bytes(b: bytes) -> str:
    return hashlib.sha256(b).hexdigest()


def sha256_file(p) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def canonical_json(obj) -> str:
    return json.dumps(obj, sort_keys=True, separators=(",", ":"), ensure_ascii=True)


def dump_json(obj) -> str:
    """The on-disk form: sorted keys, indent 2, LF, trailing newline."""
    return json.dumps(obj, sort_keys=True, indent=2, ensure_ascii=True) + "\n"


def read_json(p):
    with open(p, "r", encoding="utf-8") as f:
        return json.load(f)


def write_bytes_atomic(p, data: bytes, *, inputs=(), allow_assets: bool = False) -> None:
    guard_write(p, inputs=inputs, allow_assets=allow_assets)
    d = os.path.dirname(os.path.abspath(p))
    os.makedirs(d, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=".tmp-", dir=d)
    try:
        with os.fdopen(fd, "wb") as f:
            f.write(data)
        os.replace(tmp, p)
    except BaseException:
        try:
            os.remove(tmp)
        except OSError:
            pass
        raise


def write_text_if_changed(p, text: str, *, inputs=(), allow_assets: bool = False) -> bool:
    """Write LF text atomically unless the file already holds exactly these bytes. Returns True if written."""
    data = text.encode("utf-8")
    try:
        with open(p, "rb") as f:
            if f.read() == data:
                return False
    except FileNotFoundError:
        pass
    write_bytes_atomic(p, data, inputs=inputs, allow_assets=allow_assets)
    return True


# The toolchain the committed outputs were built with. Pixels depend on Pillow's libjpeg-turbo and numpy's
# float paths; PNG bytes depend on the zlib build. requirements.txt pins the same versions.
PINNED_TOOLCHAIN = {"python": "3.13", "numpy": "2.4.2", "pillow": "10.4.0", "libjpeg_turbo": "3.0.3",
                    "zlib": "1.3.1", "pypng": "0.20220715.0"}


def toolchain_drift(tc: dict | None = None) -> list[str]:
    """Differences between the running toolchain and PINNED_TOOLCHAIN ("key: have X, pinned Y"); empty if pinned.

    Python is compared on major.minor only.
    """
    tc = toolchain() if tc is None else tc
    out = []
    for key, want in sorted(PINNED_TOOLCHAIN.items()):
        have = tc.get(key)
        if key == "python" and isinstance(have, str):
            have = ".".join(have.split(".")[:2])
        if have != want:
            out.append(f"{key}: have {have}, pinned {want}")
    return out


def toolchain() -> dict:
    """Versions that decide output bytes. Recorded in every manifest entry."""
    import numpy

    tc = {"python": platform.python_version(), "numpy": numpy.__version__, "zlib": zlib.ZLIB_RUNTIME_VERSION}
    try:
        import PIL
        from PIL import features

        tc["pillow"] = PIL.__version__
        tc["libjpeg_turbo"] = features.version("libjpeg_turbo")
    except ImportError:
        tc["pillow"] = None
        tc["libjpeg_turbo"] = None
    try:
        import png as _pypng

        tc["pypng"] = getattr(_pypng, "__version__", "unknown")
    except ImportError:
        tc["pypng"] = None
    return tc
