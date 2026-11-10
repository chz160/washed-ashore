"""Labelled contact sheet: rows = clips (in the order given), columns = files matching <clip>_<tag>*.png sorted; each cell captioned with the file stem (clip + frame).
python label_sheet.py <dir> <out.png> <clip,clip,...> [scale]"""
import glob, os, re, sys
from PIL import Image, ImageDraw, ImageFont
d, out, clips = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
rows = []
for c in clips:
    fs = sorted((f for f in glob.glob(os.path.join(d, c + "_*.png")) if "sheet" not in f and (re.fullmatch(re.escape(c) + r"_strip\d+_f\d+\.png", os.path.basename(f)) or re.fullmatch(re.escape(c) + r"_f\d+_.*\.png", os.path.basename(f)))), key=lambda f: (int(os.path.basename(f).split("_f")[-1].split(".")[0].split("_")[0]) if "strip" in f else 0, f))
    rows.append(fs)
cols = max(len(r) for r in rows)
im0 = Image.open(rows[0][0]); w, h = im0.size; cap = 36
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 24)
sheet = Image.new("RGB", (cols * w, len(rows) * (h + cap)), "white")
dr = ImageDraw.Draw(sheet)
for r, fs in enumerate(rows):
    for c, f in enumerate(fs):
        sheet.paste(Image.open(f).convert("RGB"), (c * w, r * (h + cap) + cap))
        stem = os.path.basename(f)[:-4].replace("_strip", " strip ").replace("_f", " frame ", 1) if False else os.path.basename(f)[:-4]
        dr.text((c * w + 8, r * (h + cap) + 4), stem, fill="black", font=font)
if len(sys.argv) > 4:
    sheet = sheet.resize((int(sheet.width * float(sys.argv[4])), int(sheet.height * float(sys.argv[4]))))
sheet.save(out)
print("saved", out, sheet.size)
