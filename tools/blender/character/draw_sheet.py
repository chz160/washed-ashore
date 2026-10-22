"""Draw stick-figure contact sheets from measure_clips.py --shots JSON (needs Pillow; run with normal python).

python draw_sheet.py <shots.json> <out.png> [side|front]
"""
import json
import sys

from PIL import Image, ImageDraw

shots = json.load(open(sys.argv[1]))
view = sys.argv[3] if len(sys.argv) > 3 else "side"
W, H, SC = 300, 360, 150.0


def pt(p, ox):
    hx = -p[1] if view == "side" else -p[0]  # side: forward (-Y) to the right; front: character left to the right
    return (ox + W / 2 + hx * SC, H - 20 - p[2] * SC)


sheet = Image.new("RGB", (W * 4, H * len(shots)), (30, 30, 36))
d = ImageDraw.Draw(sheet)
for r, (name, frames) in enumerate(shots.items()):
    for c, fr in enumerate(frames):
        ox, oy = c * W, r * H
        d.line([(ox, oy + H - 20), (ox + W, oy + H - 20)], fill=(90, 90, 90))
        for bn, (h, t) in fr.items():
            col = (255, 140, 90) if bn.endswith(".L") else (110, 190, 255) if bn.endswith(".R") else (230, 230, 230)
            a, b = pt(h, ox), pt(t, ox)
            d.line([(a[0], oy + a[1]), (b[0], oy + b[1])], fill=col, width=2)
        d.text((ox + 4, oy + 4), f"{name} {c}", fill=(255, 255, 255))
sheet.save(sys.argv[2])
