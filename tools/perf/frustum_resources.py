#!/usr/bin/env python3
"""Phase 0 (I-4) descriptive report of the FrustumProbe "resources" block: frame timing (CPU and GPU), memory and texture
memory per window, plus the readback of the mip limit, lodBias, anti-aliasing and depth-priming keys.

Descriptive only. Nothing here is a verdict, and none of it is part of S1 (decision 0025) unless a plan pre-registers
the measure with its own threshold before any data. GPU time is None ("unavailable (<reason>)") when the player recorded
null; it is never read as 0.

Reads only finished launches (progress.txt beside the result ends in a DONE line; anything else is listed as skipped),
so it is safe to run after a runner exits and must not be pointed at a run in progress:
  <slot>/frustum-spots.json, else <slot>/frustum-windows.jsonl (spot launches), or <slot>/frustum-route.json (route).
Windows written before Phase 0 have no "resources" key and show "n/a (pre-Phase-0 harness)".

Per V window the delta is V minus the mean of the A windows either side in the same launch (positive = V uses more);
a field missing in any of the three gives None, never 0.

Phase 0 smoke runs with the new keys (mipLimit, lodBias, aa, depthPrime, post) go through this tool, never through
frustum_analysis.py, which is pinned to the #9 plan (blob 710f0d11) and reads unknown variants as NO EFFECT.

Usage (from the repo root):
  python tools/perf/frustum_resources.py --run-dir TestResults/<runs>/<run> [--out-dir <dir>]
  python tools/perf/frustum_resources.py --root TestResults/9-runs --run 20261112-1803
Writes resources.json and resources.md into --out-dir (default: TestResults/resources/<run folder name> under the
repo root, never the run folder itself, so frozen evidence such as the #9 runs is not added to). run_dir in the output is
the run folder relative to the repo root ('/' separators), or its folder name when it lies outside the repo, so the same
input gives the same bytes however the path is spelled.
"""
import argparse
import json
import os
import sys

MB = 1024.0 * 1024.0
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PRE_PHASE0 = "n/a (pre-Phase-0 harness)"
LABEL = "Descriptive only: not part of S1 unless a plan pre-registers it (decision 0025)."

# Numeric fields of extract() that get a bracket delta.
DELTA_KEYS = (
    "frame_p95_ms", "cpu_frame_p50_ms", "cpu_frame_p95_ms", "gpu_frame_p50_ms", "gpu_frame_p95_ms",
    "total_used_median_mb", "total_used_max_mb", "total_reserved_median_mb", "gc_used_median_mb", "gc_used_max_mb",
    "gc_reserved_median_mb", "gfx_used_median_mb", "gfx_used_max_mb", "system_used_median_mb",
    "texture_current_end_mb", "texture_total_end_mb", "texture_desired_end_mb", "texture_target_end_mb",
)


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def last_line(path):
    if not os.path.exists(path):
        return ""
    with open(path, encoding="utf-8") as f:
        lines = [l.strip() for l in f if l.strip()]
    return lines[-1] if lines else ""


def _get(d, *keys):
    for k in keys:
        if not isinstance(d, dict) or d.get(k) is None:
            return None
        d = d[k]
    return d


def _mb(v):
    return None if v is None else v / MB


def extract(window):
    """Flat descriptive record of one window (or route) row. Missing values are None, never 0."""
    rec = {
        "frame_p95_ms": _get(window, "frame_ms", "p95"),
        "readback": None,
    }
    rb = window.get("readback_after_timing") or window.get("readback_end")
    if isinstance(rb, dict):
        rec["readback"] = {k: rb.get(k) for k in (
            "texture_mip_limit", "lod_bias", "msaa", "camera_aa", "upscaling_filter", "stp_used",
            "render_post_processing", "allow_msaa", "depth_priming", "depth_priming_not_blocked_by_msaa")}
    res = window.get("resources")
    if not isinstance(res, dict):
        rec["harness"] = PRE_PHASE0
        for k in DELTA_KEYS:
            rec.setdefault(k, None)
        rec["gpu"] = PRE_PHASE0
        return rec
    rec["harness"] = "phase0"
    ft = res.get("frame_timing") or {}
    rec["timing_enabled"] = ft.get("enabled")
    rec["timed_frames"] = ft.get("timed_frames")
    rec["frames_accepted"] = ft.get("frames_accepted")
    rec["cpu_frame_p50_ms"] = _get(ft, "cpu_frame_ms", "median")
    rec["cpu_frame_p95_ms"] = _get(ft, "cpu_frame_ms", "p95")
    rec["gpu_frame_p50_ms"] = _get(ft, "gpu_frame_ms", "median")
    rec["gpu_frame_p95_ms"] = _get(ft, "gpu_frame_ms", "p95")
    rec["gpu_zero_frames"] = ft.get("gpu_zero_frames")
    if rec["gpu_frame_p95_ms"] is None:
        rec["gpu"] = "unavailable (%s)" % (ft.get("gpu_null_reason") or "no reason recorded")
    else:
        rec["gpu"] = "ok"
    mem = res.get("memory") or {}
    rec["total_used_median_mb"] = _mb(_get(mem, "total_used_bytes", "median"))
    rec["total_used_max_mb"] = _mb(_get(mem, "total_used_bytes", "max"))
    rec["total_reserved_median_mb"] = _mb(_get(mem, "total_reserved_bytes", "median"))
    rec["gc_used_median_mb"] = _mb(_get(mem, "gc_used_bytes", "median"))
    rec["gc_used_max_mb"] = _mb(_get(mem, "gc_used_bytes", "max"))
    rec["gc_reserved_median_mb"] = _mb(_get(mem, "gc_reserved_bytes", "median"))
    rec["gfx_used_median_mb"] = _mb(_get(mem, "gfx_used_bytes", "median"))
    rec["gfx_used_max_mb"] = _mb(_get(mem, "gfx_used_bytes", "max"))
    rec["system_used_median_mb"] = _mb(_get(mem, "system_used_bytes", "median"))
    rec["texture_memory_dev_median_mb"] = _mb(_get(mem, "texture_bytes", "median"))
    for when in ("start", "end"):
        tex = res.get("texture_" + when) or {}
        for field in ("current", "total", "desired", "target"):
            rec["texture_%s_%s_mb" % (field, when)] = _mb(tex.get(field + "_bytes"))
    return rec


def bracket_delta(v, a0, a1):
    """V minus the mean of the two bracketing A records, per DELTA_KEYS; None where any of the three is missing."""
    out = {}
    for k in DELTA_KEYS:
        x, p, q = v.get(k), a0.get(k), a1.get(k)
        out[k] = None if x is None or p is None or q is None else x - (p + q) / 2.0
    return out


def finished(slot_dir):
    line = last_line(os.path.join(slot_dir, "progress.txt"))
    parts = line.split(" ", 1)
    return len(parts) == 2 and parts[1].startswith("DONE")


def windows_of(slot_dir):
    """(mode, rows) for one finished launch folder, or (None, []) when it holds no probe result."""
    spots = os.path.join(slot_dir, "frustum-spots.json")
    jsonl = os.path.join(slot_dir, "frustum-windows.jsonl")
    route = os.path.join(slot_dir, "frustum-route.json")
    if os.path.exists(spots):
        return "spots", load(spots).get("windows", [])
    if os.path.exists(jsonl):
        with open(jsonl, encoding="utf-8") as f:
            return "spots", [json.loads(l) for l in f if l.strip()]
    if os.path.exists(route):
        r = load(route)
        r.setdefault("variant_id", _get(r, "variant", "id"))
        return "route", [r]
    return None, []


def run_label(run_dir):
    """The run folder as recorded in the output: repo-relative with '/' separators, else its folder name."""
    full = os.path.abspath(run_dir)
    try:
        rel = os.path.relpath(full, REPO_ROOT)
    except ValueError:   # another drive on Windows
        rel = os.pardir
    if rel == os.pardir or rel.startswith(os.pardir + os.sep):
        return os.path.basename(full)
    return rel.replace("\\", "/")


def default_out_dir(run_dir):
    return os.path.join(REPO_ROOT, "TestResults", "resources", os.path.basename(os.path.normpath(os.path.abspath(run_dir))))


def analyse(run_dir):
    slots = sorted(d for d in os.listdir(run_dir) if os.path.isdir(os.path.join(run_dir, d)))
    if any(os.path.exists(os.path.join(run_dir, f)) for f in ("frustum-spots.json", "frustum-windows.jsonl", "frustum-route.json")):
        slots = ["."]
    out = {"run_dir": run_label(run_dir), "label": LABEL, "launches": [], "skipped": []}
    for s in slots:
        d = os.path.normpath(os.path.join(run_dir, s))
        mode, rows = windows_of(d)
        if mode is None:
            continue
        if not finished(d):
            out["skipped"].append({"slot": s, "why": "progress.txt does not end in DONE (unfinished or still running)"})
            continue
        recs = []
        for i, w in enumerate(rows):
            rec = extract(w)
            rec.update({"spot": w.get("spot", w.get("route")), "window": w.get("window", i), "variant": w.get("variant_id"),
                        "valid": w.get("valid")})
            recs.append(rec)
        for i, rec in enumerate(recs):
            if mode != "spots" or rec["variant"] == "A":
                continue
            before = recs[i - 1] if i > 0 else None
            after = recs[i + 1] if i + 1 < len(recs) else None
            if before and after and before["variant"] == "A" and after["variant"] == "A" and before["spot"] == rec["spot"] == after["spot"]:
                rec["delta_vs_A"] = bracket_delta(rec, before, after)
        out["launches"].append({"slot": s, "mode": mode, "windows": recs})
    return out


def _f(v, nd=1):
    if v is None:
        return "n/a"
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, (int, float)):
        return ("%." + str(nd) + "f") % v
    return str(v)


def markdown(rep):
    lines = ["# FrustumProbe resources: %s" % rep["run_dir"], "", "_%s GPU time shows n/a with its reason when the player recorded null; it is never 0._" % rep["label"], ""]
    if rep["skipped"]:
        lines += ["Skipped: " + "; ".join("%s (%s)" % (s["slot"], s["why"]) for s in rep["skipped"]), ""]
    lines += ["| slot | spot | w | variant | valid | harness | frame p95 | cpu p50/p95 | gpu p50/p95 | total used med/max MB | GC used med MB | gfx used med MB | tex current end MB | mip | lodBias | msaa | cam AA | post | depth priming |",
              "|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|"]
    for L in rep["launches"]:
        for r in L["windows"]:
            rb = r.get("readback") or {}
            gpu = "%s / %s" % (_f(r.get("gpu_frame_p50_ms"), 3), _f(r.get("gpu_frame_p95_ms"), 3)) if r.get("gpu") == "ok" else r.get("gpu")
            lines.append("| %s | %s | %s | %s | %s | %s | %s | %s / %s | %s | %s / %s | %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
                L["slot"], r.get("spot"), r.get("window"), r.get("variant"), _f(r.get("valid")), r.get("harness"),
                _f(r.get("frame_p95_ms"), 3), _f(r.get("cpu_frame_p50_ms"), 3), _f(r.get("cpu_frame_p95_ms"), 3), gpu,
                _f(r.get("total_used_median_mb")), _f(r.get("total_used_max_mb")), _f(r.get("gc_used_median_mb")),
                _f(r.get("gfx_used_median_mb")), _f(r.get("texture_current_end_mb")),
                _f(rb.get("texture_mip_limit")), _f(rb.get("lod_bias"), 2), _f(rb.get("msaa")), _f(rb.get("camera_aa")),
                _f(rb.get("render_post_processing")), _f(rb.get("depth_priming"))))
    deltas = [(L["slot"], r) for L in rep["launches"] for r in L["windows"] if "delta_vs_A" in r]
    if deltas:
        lines += ["", "## V minus mean of the bracketing A windows (positive = V uses more; descriptive)", "",
                  "| slot | spot | variant | frame p95 ms | cpu p95 ms | gpu p95 ms | total used med MB | gfx used med MB | tex current end MB |",
                  "|---|---|---|---|---|---|---|---|---|"]
        for slot, r in deltas:
            d = r["delta_vs_A"]
            lines.append("| %s | %s | %s | %s | %s | %s | %s | %s | %s |" % (
                slot, r.get("spot"), r.get("variant"), _f(d["frame_p95_ms"], 3), _f(d["cpu_frame_p95_ms"], 3),
                _f(d["gpu_frame_p95_ms"], 3), _f(d["total_used_median_mb"]), _f(d["gfx_used_median_mb"]), _f(d["texture_current_end_mb"])))
    return "\n".join(lines) + "\n"


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--run-dir")
    ap.add_argument("--root")
    ap.add_argument("--run")
    ap.add_argument("--out-dir")
    a = ap.parse_args(argv)
    run_dir = a.run_dir or (os.path.join(a.root, a.run) if a.root and a.run else None)
    if not run_dir or not os.path.isdir(run_dir):
        ap.error("give --run-dir, or --root and --run, naming an existing folder")
    rep = analyse(run_dir)
    out = a.out_dir or default_out_dir(run_dir)
    os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "resources.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(rep, f, indent=1, sort_keys=True)
        f.write("\n")
    with open(os.path.join(out, "resources.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write(markdown(rep))
    print("wrote %s and %s (%d launches, %d skipped)" % (os.path.join(out, "resources.json"), os.path.join(out, "resources.md"),
                                                         len(rep["launches"]), len(rep["skipped"])))
    return 0


if __name__ == "__main__":
    sys.exit(main())
