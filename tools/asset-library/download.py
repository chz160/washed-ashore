"""Phase 1: download the approved files for every cataloged asset, verify them and record them.

    python tools/asset-library/download.py --dry-run              # plan and total size, no downloads
    python tools/asset-library/download.py --limit 10             # small test batch
    python tools/asset-library/download.py                        # everything, in batches
    python tools/asset-library/download.py --ids polyhaven:rocky_trail,ambientcg:Bark007
    python tools/asset-library/download.py --verify               # re-hash files already on disk

Approved plan (Noah, 2026-11-13): scope C (everything), textures 4K + 2K PNG, HDRIs 4K (EXR on
Poly Haven), models as FBX at 4K textures (glTF or .blend only where FBX is missing),
ambientCG substances as .sbsar.

- Resumable and idempotent. A re-run skips any item whose files are recorded in asset.json and are on
  disk at the recorded size. --verify also re-hashes them against the recorded SHA-256.
- Every file is checked against the API's size (and MD5 on Poly Haven) in local staging, then copied
  to the NAS and hashed (SHA-256). ambientCG zips are extracted into the asset folder and deleted
  (keep them with --keep-archives).
- Polite: at most 2 downloads at a time per source, a short gap between files, backoff and Retry-After.
- Before each batch, free space on the library drive is checked against the 200 GB floor; the run
  stops cleanly if the batch would cross it.
- Failures go to _logs/failures.jsonl and are retried once at the end.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import hashlib
import json
import os
import posixpath
import re
import shutil
import struct
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

from build_catalog import iter_records
from common import (LIB, LOGS, RAW, STAGING, USER_AGENT, budget_left, guard_path, human, log, read_json,
                    today, write_json)
from size_report import pick

PER_SOURCE = 2          # parallel downloads per source
GAP = 0.25              # seconds between file starts per source
BATCH_BYTES = 25 * 1024**3
STAGING_RESERVE = 30 * 1024**3
PH_SKIP = {"blend", "gltf", "mtlx", "fbx", "usd", "nor_dx", "arm", "rough_ao"}
REL_ORDER = {"high": 0, "medium": 1, "low": 2, "none": 3}

class _QuotingRedirect(urllib.request.HTTPRedirectHandler):
    """ambientCG redirects some files to URLs with raw spaces and '^'; encode them before following."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        newurl = urllib.parse.quote(newurl, safe=":/?=&%#~+@!$'()*,;")
        return super().redirect_request(req, fp, code, msg, headers, newurl)


_opener = urllib.request.build_opener(_QuotingRedirect)
JUNK = {"thumbs.db", "desktop.ini", ".ds_store"}


class _RateLimit:
    """Optional total download cap in bytes per second (--max-rate)."""

    def __init__(self):
        self.rate = 0
        self.lock = threading.Lock()
        self.t0 = time.monotonic()
        self.sent = 0

    def take(self, n):
        if not self.rate:
            return
        with self.lock:
            self.sent += n
            ahead = self.sent / self.rate - (time.monotonic() - self.t0)
        if ahead > 0:
            time.sleep(ahead)


_limit = _RateLimit()
_sem = {s: threading.BoundedSemaphore(PER_SOURCE) for s in ("polyhaven", "ambientcg")}
_last = {s: 0.0 for s in _sem}
_gap_lock = threading.Lock()
_fail_lock = threading.Lock()
_stats = {"bytes": 0}
_stats_lock = threading.Lock()


class Item:
    """One planned download unit: a tier/variant of an asset, made of files or one archive."""

    def __init__(self, key, files, archive=None):
        self.key = key            # e.g. "4k/png"
        self.files = files        # [{url, dest, size, md5}] (dest relative to the asset folder)
        self.archive = archive    # {url, size, name, dest_dir} for ambientCG zips

    @property
    def size(self):
        return self.archive["size"] if self.archive else sum(f["size"] for f in self.files)


# ------------------------------------------------------------------ planning
def planned_tiers(rec):
    if rec["type"] == "HDRI":
        p = pick(rec, 4, "png")
        return [p[:2]] if p else []
    if "native" in rec.get("size_options", {}):
        n = rec["size_options"]["native"]
        return [("native", "sbsar" if "sbsar" in n else next(iter(n)))]
    out = []
    p = pick(rec, 4, "png")
    if p:
        out.append(p[:2])
    if rec["type"] not in ("model", "plant"):
        q = pick(rec, 2, "png")
        if q and (not p or q[0] != p[0]):
            out.append(q[:2])
    return out


def ph_items(rec, tree):
    items = []
    for tier, variant in planned_tiers(rec):
        res = next((r for r in _ph_res_keys(tree) if r.lower() == tier), tier)
        files = []
        if rec["type"] == "HDRI":
            f = tree["hdri"][res][variant]
            files.append(_f(f, f"{tier}/{_base(f['url'])}"))
        elif rec["type"] == "material":
            order = ("png", "jpg", "exr") if variant == "png" else ("jpg", "png", "exr")
            for k, v in tree.items():
                if k.startswith("_") or k in PH_SKIP or not isinstance(v, dict) or res not in v:
                    continue
                fm = v[res]
                f = next((fm[o] for o in order if o in fm), None)
                if f:
                    files.append(_f(f, f"{tier}/{_base(f['url'])}"))
        else:  # model / plant: one scene file plus the textures it includes
            f = tree[variant][res][variant]
            d = f"{tier}-{variant}"
            files.append(_f(f, f"{d}/{_base(f['url'])}"))
            for inc, fi in (f.get("include") or {}).items():
                files.append(_f(fi, f"{d}/{_safe_rel(inc)}"))
        items.append(Item(f"{tier}/{variant}", files))
    return items


def _ph_res_keys(tree):
    keys = set()
    for v in tree.values():
        if isinstance(v, dict):
            keys |= {k for k in v if re.match(r"\d+k$", k, re.I)}
    return keys


def acg_items(rec):
    items = []
    urls = rec.get("_download_urls") or {}
    for tier, variant in planned_tiers(rec):
        url = urls.get(f"{tier}/{variant}")
        if not url:
            continue
        size = rec["size_options"][tier][variant]
        name = urllib.parse.parse_qs(urllib.parse.urlparse(url).query).get("file", [f"{rec['source_id']}_{tier}"])[0]
        if name.lower().endswith(".zip"):
            items.append(Item(f"{tier}/{variant}", [], archive={"url": url, "size": size, "name": name,
                                                                  "dest_dir": f"{tier}-{variant}"}))
        else:
            items.append(Item(f"{tier}/{variant}", [{"url": url, "dest": f"{tier}/{name}", "size": size,
                                                    "md5": None}]))
    return items


def _f(f, dest):
    return {"url": f["url"], "dest": dest, "size": f["size"], "md5": f.get("md5")}


def _base(url):
    return urllib.parse.unquote(posixpath.basename(urllib.parse.urlparse(url).path))


def _safe_rel(p):
    parts = [x for x in Path(p).as_posix().split("/") if x not in ("", ".", "..")]
    return "/".join(parts)


def items_for(rec):
    if rec["source"] == "polyhaven":
        tree = read_json(RAW / "polyhaven" / "files" / f"{rec['source_id']}.json")
        return ph_items(rec, tree) if tree else []
    if rec["source"] == "ambientcg":
        return acg_items(rec)
    return []


# ------------------------------------------------------------------ transfer
def _throttle(source):
    with _gap_lock:
        wait = _last[source] + GAP - time.monotonic()
        _last[source] = max(time.monotonic(), _last[source] + GAP)
    if wait > 0:
        time.sleep(wait)


def fetch(url, part: Path, expect_size: int, source: str, retries=6):
    """Stream url to part (resuming with Range). Returns (sha256, md5) of the full file."""
    part.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(retries):
        have = part.stat().st_size if part.exists() else 0
        if expect_size and have > expect_size:
            part.unlink()
            have = 0
        headers = {"User-Agent": USER_AGENT}
        if have and (not expect_size or have < expect_size):
            headers["Range"] = f"bytes={have}-"
        try:
            if not (expect_size and have == expect_size):
                with _sem[source]:
                    _throttle(source)
                    req = urllib.request.Request(url, headers=headers)
                    with _opener.open(req, timeout=120) as r:
                        mode = "ab" if have and r.status == 206 else "wb"
                        with open(part, mode) as out:
                            while chunk := r.read(1 << 20):
                                out.write(chunk)
                                _limit.take(len(chunk))
                                with _stats_lock:
                                    _stats["bytes"] += len(chunk)
            got = part.stat().st_size
            if expect_size and got != expect_size:
                raise IOError(f"size mismatch: got {got}, expected {expect_size}")
            return _hashes(part)
        except urllib.error.HTTPError as e:
            if e.code == 416:          # range past the end: start over
                part.unlink(missing_ok=True)
            elif e.code in (400, 401, 403, 404, 410):
                raise
            delay = _retry_after(e) or min(300, 5 * 2 ** attempt)
            log(f"HTTP {e.code} {url}: waiting {delay:.0f}s")
            time.sleep(delay)
        except (urllib.error.URLError, TimeoutError, ConnectionError, IOError) as e:
            delay = min(300, 5 * 2 ** attempt)
            log(f"{type(e).__name__} {url}: {e}; retry in {delay:.0f}s")
            time.sleep(delay)
    raise RuntimeError(f"gave up after {retries} attempts")


def _retry_after(e):
    v = e.headers.get("Retry-After") if e.headers else None
    try:
        return float(v) if v else None
    except ValueError:
        return 60.0


def _hashes(path: Path):
    s, m = hashlib.sha256(), hashlib.md5()
    with open(path, "rb") as f:
        while b := f.read(1 << 20):
            s.update(b)
            m.update(b)
    return s.hexdigest(), m.hexdigest()


def _sha256(path: Path):
    return _hashes(path)[0]


def png_info(path: Path):
    """(bit depth, color type) from a PNG header, or None."""
    try:
        with open(path, "rb") as f:
            head = f.read(26)
        if head[:8] == b"\x89PNG\r\n\x1a\n":
            return head[24], head[25]
    except OSError:
        pass
    return None


COLOR_TYPES = {0: "gray", 2: "rgb", 3: "indexed", 4: "gray+alpha", 6: "rgba"}


def file_entry(path: Path, sha, item_key):
    e = {"path": path.relative_to(LIB).as_posix(), "size": path.stat().st_size, "sha256": sha, "item": item_key}
    info = png_info(path) if path.suffix.lower() == ".png" else None
    if info:
        e["png"] = f"{info[0]}-bit {COLOR_TYPES.get(info[1], info[1])}"
    return e


def copy_hashed(src: Path, dst: Path):
    dst.parent.mkdir(parents=True, exist_ok=True)
    tmp = dst.with_name(dst.name + ".part")
    shutil.copyfile(src, tmp)
    os.replace(tmp, dst)


def extract_zip(zpath: Path, dest: Path):
    """Extract into dest, refusing paths that escape it. Returns the extracted file paths."""
    out = []
    dest_r = dest.resolve()
    with zipfile.ZipFile(zpath) as z:
        bad = z.testzip()
        if bad:
            raise IOError(f"corrupt member in zip: {bad}")
        for m in z.infolist():
            if m.is_dir() or "__MACOSX" in m.filename or Path(m.filename).name.lower() in JUNK:
                continue
            rel = _safe_rel(m.filename)
            target = (dest / rel).resolve()
            if dest_r not in target.parents:
                raise IOError(f"unsafe path in zip: {m.filename}")
            target.parent.mkdir(parents=True, exist_ok=True)
            tmp = target.with_name(target.name + ".part")
            with z.open(m) as src, open(tmp, "wb") as dst:
                shutil.copyfileobj(src, dst, 1 << 20)
            os.replace(tmp, target)
            out.append(target)
    return out


# ------------------------------------------------------------------ per asset
def asset_path(rec) -> Path:
    return LIB / rec["_path"]


def item_complete(rec, item, verify):
    done = (rec.get("downloaded") or {}).get("items", {}).get(item.key)
    if not done or not done.get("complete"):
        return False
    files = [f for f in rec.get("files", []) if f.get("item") == item.key]
    if not files:
        return False
    for f in files:
        p = LIB / f["path"]
        if not p.exists() or p.stat().st_size != f["size"]:
            return False
        if verify and _sha256(p) != f["sha256"]:
            log(f"{rec['source']}:{rec['source_id']} {f['path']}: SHA-256 mismatch, re-downloading")
            return False
    return True


def process(rec, args):
    rid = f"{rec['source']}:{rec['source_id']}"
    adir = guard_path(asset_path(rec))
    path = adir / "asset.json"
    rec = read_json(path) or rec          # freshest copy
    rec["_path"] = os.path.relpath(adir, LIB)
    items = items_for(rec)
    if not items:
        return rid, 0, "no downloadable items"
    dl = rec.setdefault("downloaded", {})
    dl.setdefault("items", {})
    got = 0
    errors = []
    for item in items:
        if item_complete(rec, item, args.verify):
            continue
        stage = STAGING / rec["source"] / rec["source_id"] / item.key.replace("/", "_")
        try:
            new_files = []
            if item.archive:
                a = item.archive
                zp = stage / a["name"]
                fetch(a["url"], zp, a["size"], rec["source"])
                dest = adir / a["dest_dir"]
                for p in extract_zip(zp, dest):
                    new_files.append(file_entry(p, _sha256(p), item.key))
                if args.keep_archives:
                    copy_hashed(zp, adir / "_archives" / a["name"])
                got += a["size"]
            else:
                for f in item.files:
                    sp = stage / f["dest"]
                    sha, md5 = fetch(f["url"], sp, f["size"], rec["source"])
                    if f.get("md5") and md5 != f["md5"]:
                        sp.unlink(missing_ok=True)
                        raise IOError(f"MD5 mismatch for {f['url']}")
                    dp = adir / f["dest"]
                    copy_hashed(sp, dp)
                    if dp.stat().st_size != f["size"]:
                        raise IOError(f"copy to NAS truncated: {dp}")
                    new_files.append(file_entry(dp, sha, item.key))
                    got += f["size"]
            rec["files"] = [f for f in rec.get("files", []) if f.get("item") != item.key] + new_files
            dl["items"][item.key] = {"complete": True, "date": today(), "bytes": item.size}
            shutil.rmtree(stage, ignore_errors=True)
        except Exception as e:  # noqa: BLE001 - logged and retried at the end
            errors.append(f"{item.key}: {e}")
            record_failure(rid, item.key, e)
    finish_record(rec, items, adir)
    write_json(path, {k: v for k, v in rec.items() if k != "_path"})
    return rid, got, "; ".join(errors)


def finish_record(rec, items, adir):
    dl = rec["downloaded"]
    complete = [i.key for i in items if dl["items"].get(i.key, {}).get("complete")]
    dl["resolutions"] = sorted({k.split("/")[0] for k in complete})
    dl["formats"] = sorted({k.split("/")[1] for k in complete})
    if complete:
        rec["download_date"] = rec.get("download_date") or today()
    if len(complete) == len(items) and rec.get("status") in (None, "not_downloaded"):
        rec["status"] = "downloaded"
    # maps actually present, from file names
    present = set()
    for f in rec.get("files", []):
        n = Path(f["path"]).stem.lower()
        for pat, name in MAP_PATTERNS:
            if re.search(pat, n):
                present.add(name)
                break
    if present:
        rec["maps"] = sorted(present)
        if "normal_gl" in present:
            rec["normal_convention"] = ("GL (Unity) and DX both present" if "normal_dx" in present
                                        else "GL (Unity)")
    # local thumbnail
    if not rec.get("thumbnail") and rec.get("thumbnail_url"):
        ext = Path(urllib.parse.urlparse(rec["thumbnail_url"]).path).suffix or ".png"
        tp = adir / f"thumbnail{ext}"
        try:
            req = urllib.request.Request(rec["thumbnail_url"], headers={"User-Agent": USER_AGENT})
            with _sem[rec["source"]]:
                _throttle(rec["source"])
                with urllib.request.urlopen(req, timeout=60) as r:
                    data = r.read()
            tp.write_bytes(data)
            rec["thumbnail"] = tp.relative_to(LIB).as_posix()
        except Exception as e:  # noqa: BLE001 - thumbnails are optional
            log(f"thumbnail failed for {rec['source_id']}: {e}")


MAP_PATTERNS = [
    (r"normalgl|nor_gl", "normal_gl"), (r"normaldx|nor_dx", "normal_dx"), (r"_color|_diff|albedo|basecolor", "albedo"),
    (r"rough_ao", "packed_rough_ao"), (r"_arm", "packed_arm"), (r"roughness|_rough", "roughness"),
    (r"ambientocclusion|_ao", "ao"), (r"displacement|_disp|height", "height"), (r"metalness|metallic|_metal", "metallic"),
    (r"opacity|alpha", "opacity"), (r"scattering|translucen", "translucency"), (r"emission", "emission"),
]


def record_failure(rid, item, err):
    with _fail_lock:
        LOGS.mkdir(parents=True, exist_ok=True)
        with open(LOGS / "failures.jsonl", "a", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps({"time": _dt.datetime.now().isoformat(timespec="seconds"), "id": rid,
                                "item": item, "reason": str(err)}) + "\n")


# ------------------------------------------------------------------ heartbeat (read by watchdog.py)
HEARTBEAT = STAGING / "heartbeat.json"
_hb = {"state": "starting", "detail": ""}


def _write_heartbeat(last_bytes=[-1], last_change=[time.time()]):
    b = _stats["bytes"]
    now = time.time()
    if b != last_bytes[0]:
        last_bytes[0], last_change[0] = b, now
    tmp = HEARTBEAT.with_suffix(".tmp")
    tmp.write_text(json.dumps({"pid": os.getpid(), "time": now, "bytes": b, "last_progress": last_change[0],
                               "state": _hb["state"], "detail": _hb["detail"],
                               "argv": sys.argv[1:]}), encoding="utf-8")
    os.replace(tmp, HEARTBEAT)


def _heartbeat_loop():
    while True:
        try:
            _write_heartbeat()
        except OSError:
            pass
        time.sleep(30)


# ------------------------------------------------------------------ driver
def select(args):
    recs = list(iter_records())
    if args.ids:
        want = set(args.ids.split(","))
        recs = [r for r in recs if f"{r['source']}:{r['source_id']}" in want]
    if args.source:
        recs = [r for r in recs if r["source"] == args.source]
    recs.sort(key=lambda r: (REL_ORDER.get(r.get("bells_bend_relevance"), 4), r["source"], r["source_id"]))
    # Alternate sources within each relevance level, so both sources' download slots stay busy.
    mixed = []
    for lvl in sorted({REL_ORDER.get(r.get("bells_bend_relevance"), 4) for r in recs}):
        groups = {}
        for r in recs:
            if REL_ORDER.get(r.get("bells_bend_relevance"), 4) == lvl:
                groups.setdefault(r["source"], []).append(r)
        lists = list(groups.values())
        for i in range(max(len(x) for x in lists)):
            mixed += [x[i] for x in lists if i < len(x)]
    recs = mixed
    if args.limit:
        # a mixed sample: spread the limit over types
        by_type = {}
        for r in recs:
            by_type.setdefault((r["source"], r["type"]), []).append(r)
        sample, i = [], 0
        while len(sample) < args.limit and any(by_type.values()):
            for k in sorted(by_type):
                if by_type[k] and len(sample) < args.limit:
                    sample.append(by_type[k].pop(0))
            i += 1
        recs = sample
    return recs


def remaining_bytes(rec):
    return sum(i.size for i in items_for(rec) if not item_complete(rec, i, False))


def run(recs, args):
    total = len(recs)
    todo = [(r, remaining_bytes(r)) for r in recs]
    todo = [(r, b) for r, b in todo if b > 0]
    left_bytes = sum(b for _, b in todo)
    log(f"{total} assets selected; {len(todo)} need downloads, {human(left_bytes)} to fetch")
    if args.dry_run:
        return []
    failed = []
    start = time.monotonic()
    done_bytes = 0
    i = 0
    while i < len(todo):
        batch, bsize = [], 0
        while i < len(todo) and (not batch or bsize + todo[i][1] <= BATCH_BYTES):
            batch.append(todo[i][0])
            bsize += todo[i][1]
            i += 1
        lib_left = budget_left()
        stage_free = shutil.disk_usage(STAGING if STAGING.exists() else STAGING.anchor).free
        if bsize > lib_left:
            log(f"STOP: next batch needs {human(bsize)} but only {human(lib_left)} is left above the "
                f"200 GB floor on {LIB}")
            _hb["state"], _hb["detail"] = "stopped", "disk floor reached"
            break
        if stage_free < STAGING_RESERVE:
            log(f"STOP: staging drive has only {human(stage_free)} free")
            _hb["state"], _hb["detail"] = "stopped", "staging drive full"
            break
        with ThreadPoolExecutor(max_workers=PER_SOURCE * 2) as ex:
            futs = [ex.submit(process, r, args) for r in batch]
            for fu in as_completed(futs):
                try:
                    rid, got, err = fu.result()
                except Exception as e:  # noqa: BLE001
                    rid, got, err = "?", 0, str(e)
                done_bytes += got
                if err:
                    failed.append(rid)
                    log(f"FAILED {rid}: {err}")
        el = time.monotonic() - start
        rate = _stats["bytes"] / el if el else 0
        eta = (left_bytes - done_bytes) / rate if rate else 0
        log(f"progress: {i}/{len(todo)} assets, {human(done_bytes)} of {human(left_bytes)}, "
            f"{human(rate)}/s, ETA {eta / 3600:.1f} h, {len(failed)} failed")
    return failed


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--limit", type=int)
    ap.add_argument("--ids")
    ap.add_argument("--source", choices=["polyhaven", "ambientcg"])
    ap.add_argument("--verify", action="store_true", help="re-hash files already on disk")
    ap.add_argument("--keep-archives", action="store_true")
    ap.add_argument("--max-rate", type=float, default=0, help="total download cap in MB/s (0 = no cap)")
    args = ap.parse_args()
    STAGING.mkdir(parents=True, exist_ok=True)
    _limit.rate = args.max_rate * 1024 * 1024
    full_run = not (args.dry_run or args.limit or args.ids or args.source)
    if full_run:
        threading.Thread(target=_heartbeat_loop, daemon=True).start()
    _hb["state"] = "running"
    recs = select(args)
    failed = run(recs, args)
    if failed and not args.dry_run:
        log(f"retrying {len(failed)} failed assets once")
        retry = [r for r in recs if f"{r['source']}:{r['source_id']}" in set(failed)]
        failed = run(retry, args)
    log(f"finished; {len(failed)} assets still failing (see {LOGS / 'failures.jsonl'})")
    if _hb["state"] == "running":
        _hb["state"], _hb["detail"] = "finished", f"{len(failed)} assets failing"
    if full_run:
        _write_heartbeat()
    return 0 if not failed else 1


if __name__ == "__main__":
    sys.exit(main())
