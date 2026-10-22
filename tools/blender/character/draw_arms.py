"""Upper-body zoom sheet of gait_stills.py JSON files (arms and hands visible): one row per clip, four phases per row, one fixed camera.

python draw_arms.py <out.png> <label1>=<clip1.json> ...    (Pillow, normal python; run gait_stills.py with --fingers 1 to see the fingers)
"""
import json
import sys

from PIL import Image, ImageDraw

import os

FRONT = os.environ.get("VIEW") == "front"  # VIEW=front: seen from in front (character's left on the right), both arms drawn
W, H, SC = 300, 330, 330.0
CX, CZ = (0.0 if FRONT else -0.07), 1.35  # fixed camera centre for every cell


def pt(p, ox):
    if FRONT:
        return (ox + W / 2 + (p[0] - CX) * SC, H / 2 - (p[2] - CZ) * SC)
    return (ox + W / 2 + (-(p[1] - CX)) * SC, H / 2 - (p[2] - CZ) * SC)


rows = []
for a in sys.argv[2:]:  # label=phases.json|arms.json  (arms.json = arm_metrics.py output, for the hand-path trace of the whole cycle)
    label, files = a.split("=", 1)
    phases, _, arms = files.partition("|")
    rows.append((label, json.load(open(phases)), json.load(open(arms))["frames"] if arms else None))
sheet = Image.new("RGB", (W * 4, H * len(rows)), (30, 30, 36))
d = ImageDraw.Draw(sheet)
for r, (label, data, frames) in enumerate(rows):
    for c, ph in enumerate(data["phases"]):
        ox, oy = c * W, r * H
        for name, (h, t) in ph["bones"].items():
            if name in ("Root", "Body"):
                continue
            arm = any(k in name for k in ("Shoulder", "UpperArm", "LowerArm", "Wrist", "Index", "Middle", "Ring", "Pinky", "Thumb"))
            col = ((255, 150, 80) if name.endswith(".L") else (110, 190, 255)) if arm else (120, 120, 130)
            a, b = pt(h, ox), pt(t, ox)
            d.line([(a[0], oy + a[1]), (b[0], oy + b[1])], fill=col, width=4 if arm else 2)
        if frames:  # the wrist's path over the whole cycle, one trace per arm, and the wrist now
            for side, colr in (("L", (255, 200, 120)), ("R", (150, 215, 255))):
                path = [pt(f["wrist_pos_" + side], ox) for f in frames]
                path = [(x, oy + y) for x, y in path]
                d.line(path + [path[0]], fill=colr, width=1)
        d.text((ox + 4, oy + 4), f"{label}  {ph['label']} (frame {ph['frame']})", fill=(255, 255, 255))
sheet.save(sys.argv[1])
print("wrote", sys.argv[1])
