"""Shared helpers for the asset library tools: paths, polite HTTP, disk budget and record I/O.

The library lives on the NAS: default \\\\tank\\public\\AssetLibrary, the share Noah maps as Y:.
Override it with ASSET_LIBRARY_ROOT. The UNC path is used instead of Y: because a mapped drive letter
exists only in one logon session. Downloads are staged and verified on local disk (STAGING, default
E:\\AssetLibrary-staging) before they are extracted to the NAS. Nothing here touches the Unity project.
"""
from __future__ import annotations

import datetime as _dt
import email.utils
import json
import os
import shutil
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

LIB = Path(os.environ.get("ASSET_LIBRARY_ROOT", r"\\tank\public\AssetLibrary"))
STAGING = Path(os.environ.get("ASSET_LIBRARY_STAGING", r"E:\AssetLibrary-staging"))
CATALOG = LIB / "_catalog"
RAW = CATALOG / "raw"          # cached API responses (metadata only)
LOGS = LIB / "_logs"

# Poly Haven requires a unique User-Agent naming the software (API ToS 2.4).
USER_AGENT = "WashedAshoreAssetLibrary/0.1 (offline studio asset catalog; low-rate, sequential)"

# Disk budget: always leave at least this much free on the library drive.
MIN_FREE_BYTES = 200 * 1024**3

REPO_FORBIDDEN = Path(r"E:\GitHub\washed-ashore").resolve()

PROJECT_FIELDS = ("bells_bend_relevance", "candidate_for", "status", "notes")


def today() -> str:
    return _dt.date.today().isoformat()


def guard_path(p: Path) -> Path:
    """Refuse to write anywhere inside the Unity repo."""
    rp = Path(p).resolve()
    if rp == REPO_FORBIDDEN or REPO_FORBIDDEN in rp.parents:
        sys.exit(f"refusing to write inside the repo: {rp}")
    return rp


def log(msg: str) -> None:
    stamp = _dt.datetime.now().strftime("%H:%M:%S")
    print(f"[{stamp}] {msg}", flush=True)


_last_call = [0.0]


def http_get(url: str, *, min_interval: float = 0.5, retries: int = 6, timeout: int = 60) -> bytes:
    """GET with a minimum gap between calls, exponential backoff and Retry-After support."""
    for attempt in range(retries):
        wait = _last_call[0] + min_interval - time.monotonic()
        if wait > 0:
            time.sleep(wait)
        _last_call[0] = time.monotonic()
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=timeout) as r:
                return r.read()
        except urllib.error.HTTPError as e:
            if e.code in (404, 400, 403):
                raise
            delay = _retry_after(e.headers.get("Retry-After")) or min(120, 2 ** (attempt + 1))
            log(f"HTTP {e.code} on {url}; retry in {delay:.0f}s")
            time.sleep(delay)
        except (urllib.error.URLError, TimeoutError, ConnectionError) as e:
            delay = min(120, 2 ** (attempt + 1))
            log(f"{type(e).__name__} on {url}: {e}; retry in {delay:.0f}s")
            time.sleep(delay)
    raise RuntimeError(f"giving up on {url} after {retries} attempts")


def _retry_after(v: str | None) -> float | None:
    if not v:
        return None
    try:
        return float(v)
    except ValueError:
        d = email.utils.parsedate_to_datetime(v)
        return max(0.0, (d - _dt.datetime.now(d.tzinfo)).total_seconds())


def http_get_json(url: str, **kw):
    return json.loads(http_get(url, **kw).decode("utf-8"))


def read_json(path: Path, default=None):
    try:
        with open(path, encoding="utf-8") as f:
            return json.load(f)
    except FileNotFoundError:
        return default


def write_json(path: Path, obj) -> None:
    """Write JSON with LF endings (data files are eol=lf in this studio) atomically."""
    path = guard_path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, indent=2, ensure_ascii=False)
        f.write("\n")
    os.replace(tmp, path)


def free_bytes() -> int:
    LIB.mkdir(parents=True, exist_ok=True)
    return shutil.disk_usage(LIB).free


def budget_left() -> int:
    """Bytes that may still be written before the 200 GB floor is reached."""
    return free_bytes() - MIN_FREE_BYTES


def asset_dir(source: str, asset_type_dir: str, asset_id: str) -> Path:
    return LIB / source / asset_type_dir / asset_id


def merge_record(new: dict, old: dict | None) -> dict:
    """Keep team-edited project fields and download state from an existing asset.json."""
    if not old:
        return new
    for k in ("bells_bend_relevance", "candidate_for", "notes"):
        if old.get(k) and old.get(f"{k}_edited"):
            new[k] = old[k]
            new[f"{k}_edited"] = True
    if old.get("status") and old["status"] != "not_downloaded":
        new["status"] = old["status"]
    for k in ("files", "downloaded", "thumbnail", "download_date"):
        if old.get(k):
            new[k] = old[k]
    return new


def human(n: float) -> str:
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if abs(n) < 1024 or unit == "TB":
            return f"{n:,.1f} {unit}" if unit != "B" else f"{int(n)} B"
        n /= 1024
    return str(n)
