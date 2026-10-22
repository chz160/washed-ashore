"""Side-view stick-figure sheet from gait_stills.py JSON files: one row per clip, four phases per row, one fixed camera for every cell.

python draw_gait.py <out.png> <label1>=<clip1.json> <label2>=<clip2.json> ...    (needs Pillow; run with normal python)
"""
import json
import sys

from PIL import Image, ImageDraw

W, H, SC = 300, 380, 170.0
CENTER_Y = -0.07  # the Root bone's y: the same world camera for every clip


def pt(p, ox):
    return (ox + W / 2 + (-(p[1] - CENTER_Y)) * SC, H - 24 - p[2] * SC)  # forward (-Y) to the right


out = sys.argv[1]
rows = []
for a in sys.argv[2:]:
    label, path = a.split("=", 1)
    rows.append((label, json.load(open(path))))
sheet = Image.new("RGB", (W * 4, H * len(rows)), (30, 30, 36))
d = ImageDraw.Draw(sheet)
for r, (label, data) in enumerate(rows):
    for c, ph in enumerate(data["phases"]):
        ox, oy = c * W, r * H
        d.line([(ox, oy + H - 24), (ox + W, oy + H - 24)], fill=(110, 110, 110))
        hip = ph["bones"]["Hips"][0]
        hy = oy + H - 24 - hip[2] * SC
        d.line([(ox, hy), (ox + W, hy)], fill=(70, 70, 40))  # hip height line
        for name, (h, t) in ph["bones"].items():
            col = (255, 140, 90) if name.endswith(".L") else (110, 190, 255) if name.endswith(".R") else (230, 230, 230)
            a, b = pt(h, ox), pt(t, ox)
            d.line([(a[0], oy + a[1]), (b[0], oy + b[1])], fill=col, width=3)
        m = ph["metrics"]
        d.text((ox + 4, oy + 4), f"{label}  {ph['label']} (frame {ph['frame']})", fill=(255, 255, 255))
        d.text((ox + 4, oy + 18), f"hip {m['hip_height_m']} m  knees {m['knee_flexion_deg_L']}/{m['knee_flexion_deg_R']}  torso {m['torso_pitch_forward_deg']} deg", fill=(200, 200, 160))
sheet.save(out)
print("wrote", out)
