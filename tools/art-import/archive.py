"""Safe extraction of a source zip. The archive is untrusted data.

Only entries whose base name classifies as a map image are written, flat (base name only), into a fresh
folder. The whole archive is refused on any unsafe entry name, a symlink, too many entries, or an entry or
total size over the limits (checked on the declared sizes first, then on the bytes actually inflated).
zipfile checks each entry's CRC32 as it is read.
"""
from __future__ import annotations

import os
import stat
import zipfile

import maps
from common import guard_write, sha256_file

MAX_ENTRIES = 64
MAX_ENTRY_BYTES = 64 * 1024 * 1024
MAX_TOTAL_BYTES = 512 * 1024 * 1024


class ArchiveError(Exception):
    pass


def check_entry_name(name: str) -> None:
    if "\\" in name:
        raise ArchiveError(f"entry uses a backslash: {name!r}")
    if name.startswith("/") or (len(name) >= 2 and name[1] == ":") or ":" in name:
        raise ArchiveError(f"entry has an absolute path or drive: {name!r}")
    if any(part == ".." for part in name.split("/")):
        raise ArchiveError(f"entry escapes the folder: {name!r}")
    if "\x00" in name:
        raise ArchiveError(f"entry name has a NUL: {name!r}")


def extract(zip_path, into, *, inputs=()) -> dict:
    """Extract the map images of zip_path into the empty folder `into`.

    Returns {"name": <zip base name>, "sha256": <zip sha256>, "entries": [{"name", "crc", "size"}],
    "skipped": [<entry names not extracted>]}.
    """
    guard_write(into, inputs=inputs)
    if os.path.exists(into):
        if not os.path.isdir(into) or os.listdir(into):
            raise ArchiveError(f"extraction folder must be new or empty: {into}")
    with zipfile.ZipFile(zip_path) as z:
        infos = z.infolist()
        if len(infos) > MAX_ENTRIES:
            raise ArchiveError(f"{len(infos)} entries; the limit is {MAX_ENTRIES}")
        total = 0
        chosen = []
        skipped = []
        seen = set()
        for info in infos:
            check_entry_name(info.filename)
            mode = info.external_attr >> 16
            if stat.S_ISLNK(mode):
                raise ArchiveError(f"entry is a symlink: {info.filename!r}")
            if info.file_size > MAX_ENTRY_BYTES:
                raise ArchiveError(f"entry over {MAX_ENTRY_BYTES} bytes: {info.filename!r}")
            total += info.file_size
            if total > MAX_TOTAL_BYTES:
                raise ArchiveError(f"archive over {MAX_TOTAL_BYTES} bytes uncompressed")
            if info.is_dir():
                continue
            base = info.filename.rsplit("/", 1)[-1]
            if maps.classify(base) is None:
                skipped.append(info.filename)
                continue
            if base.lower() in seen:
                raise ArchiveError(f"two entries share the name {base!r}")
            seen.add(base.lower())
            chosen.append((info, base))
        os.makedirs(into, exist_ok=True)
        entries = []
        inflated = 0   # actual bytes written; header sizes can lie
        for info, base in sorted(chosen, key=lambda c: c[1]):
            dst = os.path.join(into, base)
            guard_write(dst, inputs=inputs)
            written = 0
            with z.open(info) as src, open(dst, "xb") as out:
                while True:
                    chunk = src.read(1 << 20)
                    if not chunk:
                        break
                    written += len(chunk)
                    inflated += len(chunk)
                    if written > MAX_ENTRY_BYTES:
                        raise ArchiveError(f"entry grew past {MAX_ENTRY_BYTES} bytes: {info.filename!r}")
                    if inflated > MAX_TOTAL_BYTES:
                        raise ArchiveError(f"archive grew past {MAX_TOTAL_BYTES} bytes uncompressed")
                    out.write(chunk)
            entries.append({"name": base, "crc": f"{info.CRC:08x}", "size": written})
    return {"name": os.path.basename(os.fspath(zip_path)), "sha256": sha256_file(zip_path),
            "entries": entries, "skipped": sorted(skipped)}
