"""art-import: Stage 1 (select and verify) and Stage 2 (repack and downscale) of the art pipeline.

Run from the repo root with the pinned venv's Python in isolated mode (see README):
    <venv>/Scripts/python -I tools/art-import/art_import.py <command> ...
Exit codes: 0 ok, 1 a verify, repack or toolchain error, 2 usage.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import argparse  # noqa: E402
import zipfile  # noqa: E402

import archive  # noqa: E402
import pixels  # noqa: E402
import repack as repack_mod  # noqa: E402
import verify as verify_mod  # noqa: E402
from common import GuardError, dump_json, read_json, toolchain_drift  # noqa: E402
from recipe import RecipeError, load as load_recipe  # noqa: E402


# Every failure that bad input can cause. A recipe that hits one is recorded as failed; the batch goes on.
INPUT_ERRORS = (RecipeError, archive.ArchiveError, repack_mod.RepackError, GuardError, pixels.PixelError,
                zipfile.BadZipFile, OSError, ValueError)


class ToolchainError(Exception):
    pass


def check_toolchain(allow_drift: bool) -> None:
    """Refuse to write outputs on a toolchain other than common.PINNED_TOOLCHAIN unless drift is allowed."""
    drift = toolchain_drift()
    if not drift:
        return
    msg = "toolchain differs from the pin (" + "; ".join(drift) + ")"
    if not allow_drift:
        raise ToolchainError(msg + "; install requirements.txt into the venv, or pass --allow-toolchain-drift")
    print(f"warning: {msg}; output bytes may differ from a pinned build", file=sys.stderr)


def _print_json(obj):
    sys.stdout.write(dump_json(obj))


def cmd_extract(a):
    _print_json(archive.extract(a.archive, a.into, inputs=(os.path.dirname(os.path.abspath(a.archive)),)))
    return 0


def cmd_verify(a):
    r = load_recipe(a.recipe)
    aj = read_json(a.asset_json) if a.asset_json else None
    v = verify_mod.verify(r, a.source_dir, aj)
    _print_json(v)
    return 1 if v["errors"] else 0


def cmd_repack(a):
    r = load_recipe(a.recipe)
    aj = read_json(a.asset_json) if a.asset_json else None
    v = verify_mod.verify(r, a.source_dir, aj)
    if v["errors"]:
        _print_json(v)
        return 1
    check_toolchain(a.allow_toolchain_drift)
    entry, log = repack_mod.repack(r, v, a.source_dir, a.out, manifest_path=a.manifest, allow_assets=a.allow_assets)
    _print_json({"entry": entry, "run": log})
    return 0


def run_all(recipes_dir, staging, work, out, *, ship_size=None, manifest_path=None, allow_assets=False,
            out_stream=sys.stdout):
    """Extract, verify and repack every recipe in recipes_dir. Returns (results, failures)."""
    results, failures = [], []
    for name in sorted(os.listdir(recipes_dir)):
        if not name.endswith(".json"):
            continue
        path = os.path.join(recipes_dir, name)
        try:
            r = load_recipe(path)
            if ship_size is not None:
                r = dict(r, ship_size=ship_size)
            if not r["input"].get("archive"):
                raise RecipeError(f"{name}: run needs input.archive")
            zpath = os.path.join(staging, r["input"]["archive"])
            into = os.path.join(work, r["source"], r["source_id"])
            info = archive.extract(zpath, into, inputs=(staging,))
            v = verify_mod.verify(r, into, None, info)
            if v["errors"]:
                raise repack_mod.RepackError("; ".join(v["errors"]))
            entry, log = repack_mod.repack(r, v, into, out, manifest_path=manifest_path, allow_assets=allow_assets,
                                           inputs=(staging, work))
            results.append((entry, log))
        except INPUT_ERRORS as e:
            failures.append((name, str(e)))
            print(f"FAIL {name}: {e}", file=sys.stderr)
    for entry, log in results:
        written = {l["file"]: l for l in log}
        out_stream.write(f"{entry['library_id']}  archive {entry['archive']['sha256']}\n")
        n = entry["normal"]
        out_stream.write(f"  normal {n['file_map']} detected={n['detected']} corr_x={n['corr_x']} corr_y={n['corr_y']}"
                         + "".join(f" | alt {x['map']} detected={x['detected']} corr_x={x['corr_x']} corr_y={x['corr_y']}"
                                   for x in n["alternates"]) + "\n")
        if entry["height_stretch"]:
            out_stream.write(f"  height lo={entry['height_stretch']['lo']} hi={entry['height_stretch']['hi']}\n")
        s = entry["albedo_stats"]
        out_stream.write(f"  albedo p1={s['p1']} p99={s['p99']} mean={s['mean']}  smoothness mean={entry['smoothness']['mean']}"
                         f" (legacy {entry['smoothness']['legacy_constant']})\n")
        for o in entry["outputs"]:
            l = written[o["file"]]
            state = "written" if l["written"] else f"skipped({l['skip_basis']})"
            out_stream.write(f"  {o['file']} {o['width']}x{o['height']}x{o['channels']} px={o['pixel_sha256']}"
                             f" file={o['file_sha256']} {state}\n")
        for l in log:
            if l.get("removed"):
                out_stream.write(f"  {l['file']} removed (no longer an output)\n")
        out_stream.write(f"  flags: {', '.join(entry['flags']) or '-'}\n")
    return results, failures


def cmd_run(a):
    check_toolchain(a.allow_toolchain_drift)
    _, failures = run_all(a.recipes, a.staging, a.work, a.out, ship_size=a.ship_size, manifest_path=a.manifest,
                          allow_assets=a.allow_assets)
    return 1 if failures else 0


def _writer_args(s):
    s.add_argument("--manifest", help="manifest path; default Data/art/art-manifest.json for an --out under Assets/, "
                                      "else <out>/art-import-manifest.json. Never under Assets/")
    s.add_argument("--allow-toolchain-drift", action="store_true",
                   help="write outputs even when the toolchain differs from the pin (bytes may differ)")


def main(argv=None):
    p = argparse.ArgumentParser(prog="art_import", description=__doc__.splitlines()[0])
    sub = p.add_subparsers(dest="cmd", required=True)
    s = sub.add_parser("extract", help="extract the map images of a zip into a new folder")
    s.add_argument("--archive", required=True)
    s.add_argument("--into", required=True)
    s.set_defaults(fn=cmd_extract)
    s = sub.add_parser("verify", help="Stage 1 on one recipe and source folder")
    s.add_argument("--recipe", required=True)
    s.add_argument("--source-dir", required=True)
    s.add_argument("--asset-json")
    s.set_defaults(fn=cmd_verify)
    s = sub.add_parser("repack", help="Stages 1 and 2 on one recipe and source folder")
    s.add_argument("--recipe", required=True)
    s.add_argument("--source-dir", required=True)
    s.add_argument("--asset-json")
    s.add_argument("--out", required=True)
    s.add_argument("--allow-assets", action="store_true", help="allow writing under the repo's Assets/")
    _writer_args(s)
    s.set_defaults(fn=cmd_repack)
    s = sub.add_parser("run", help="extract, verify and repack every recipe in a folder")
    s.add_argument("--recipes", required=True)
    s.add_argument("--staging", required=True, help="folder holding the source zips (read only)")
    s.add_argument("--work", required=True, help="extraction folder; each recipe gets a fresh <source>/<id>/")
    s.add_argument("--out", required=True)
    s.add_argument("--ship-size", type=int, help="override every recipe's ship_size (proving runs only)")
    s.add_argument("--allow-assets", action="store_true")
    _writer_args(s)
    s.set_defaults(fn=cmd_run)
    a = p.parse_args(argv)
    try:
        return a.fn(a)
    except (ToolchainError, *INPUT_ERRORS) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
