# w-qa W10 grader: reads the shore-walk JSON (+ optional R2 re-run JSON) and the player look shots, applies the plan's bars.
#   python w10_grade.py <shore.json> [<r2.json> | -] [<look-shot dir>]
# Bars (w-td, 2026-10-07): shore route = spec bar (median >= 60 fps, >= 90% of frames >= 60) + p99 < 16.6 ms, median reported
# for the record; the +1.5 ms budget (median <= 8.18 ms, p99 < 16.6) binds like-for-like on the R2 re-run only.
import json, sys, glob, os
import numpy as np
from PIL import Image

R2_BASE_MEDIAN_MS = 6.68  # Bells Bend R2 of record (bells-bend-qa-report.md)
shore = json.load(open(sys.argv[1]))
r2 = json.load(open(sys.argv[2])) if len(sys.argv) > 2 and sys.argv[2] != '-' else None
shots = sys.argv[3] if len(sys.argv) > 3 else None


def grade(name, d):
    share = d['share_frames_at_or_above_60']
    if not isinstance(share, (int, float)):
        raise SystemExit(f"{name}: share field not numeric ({share!r}); require the rebuilt player (w-td)")
    ok = d['median_fps'] >= 60 and share >= 0.90
    print(f"{name}: samples {d['samples']} duration {d['duration_s']} s median {d['median_ms']} ms ({d['median_fps']} fps) p95 {d['p95_ms']} "
          f"p99 {d['p99_ms']} max {d['max_ms']} share>=60 {share:.4f} -> {'PASS' if ok else 'FAIL'} (spec bar: median >= 60 fps, >= 90% >= 60)")
    est = d['duration_s'] * 1000.0 / d['median_ms']
    print(f"  validity: samples {d['samples']} vs duration/median {est:.0f} (ratio {d['samples'] / est:.2f}; ~0.8-1.2 expected for one instance)")
    return ok


print(f"build: {shore.get('platform')} dev={shore.get('development')} {shore.get('gpu')} {shore.get('resolution')} {shore.get('window_mode')} "
      f"vsync={shore.get('vsync')} route={shore.get('route_name')} {shore.get('route_m')} m wildlife={shore.get('wildlife')} birds={shore.get('birds')}")
a = grade('shore walk (whole route)', shore)
b = grade('shore leg', shore['shore_leg'])
p99 = shore['p99_ms'] < 16.6 and shore['shore_leg']['p99_ms'] < 16.6
print(f"shore p99 bar: whole {shore['p99_ms']} / leg {shore['shore_leg']['p99_ms']} (< 16.6) -> {'PASS' if p99 else 'FAIL'}; "
      f"median {shore['median_ms']} ms reported for the record (no pre-water baseline)")
td = True
if r2:
    grade('R2 re-run on the water build', r2)
    delta = r2['median_ms'] - R2_BASE_MEDIAN_MS
    td = delta <= 1.5 and r2['p99_ms'] < 16.6
    print(f"TD budget (R2 like-for-like): median {r2['median_ms']} vs {R2_BASE_MEDIAN_MS} -> {delta:+.2f} ms (bar <= +1.5, i.e. <= 8.18), "
          f"p99 {r2['p99_ms']} (bar < 16.6) -> {'PASS' if td else 'FAIL'}")
if shots:
    for f in sorted(glob.glob(os.path.join(shots, '*.png'))):
        im = Image.open(f); px = np.asarray(im)
        opaque = px.ndim == 2 or px.shape[2] == 3 or (px[..., 3] == 255).all()
        rgb = np.asarray(im.convert('RGB')).astype(float); h = rgb.shape[0]
        reg = rgb[int(h * 0.55):int(h * 0.95)].reshape(-1, 3)  # lower part of the frame, mostly water in the look poses
        be = reg[:, 2] - (reg[:, 0] + reg[:, 1]) / 2
        print(f"shot {os.path.basename(f)}: opaque={opaque} lower-region mean RGB {reg.mean(0).round(0)} blue-excess p50 {np.median(be):.1f} p95 {np.percentile(be, 95):.1f}")
print('W10 Windows FPS:', 'PASS' if (a and b and p99 and td) else 'FAIL')
