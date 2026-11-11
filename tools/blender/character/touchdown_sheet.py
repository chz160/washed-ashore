"""Labelled sheet of touchdown frames (keys -2..+3 around the touchdown and the mid-stance key), one row per clip, from clip_motion.py output.
python touchdown_sheet.py <motion dir> <out.png> <frames file: "clip f1,f2,..." per line> [scale]
Each cell: clip name, frame number, key label (-2, -1, 0 = touchdown, +1, +2, +3, then mid-stance)."""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

mdir, out, ff = sys.argv[1], sys.argv[2], sys.argv[3]
scale = float(sys.argv[4]) if len(sys.argv) > 4 else 1.0
rows = [l.split() for l in open(ff) if l.strip()]
def _font(size):
    """A label font that works on Windows, macOS and Linux. Set LABEL_FONT to a .ttf to choose one."""
    for path in (os.environ.get("LABEL_FONT"), "C:/Windows/Fonts/arial.ttf",
                 "/Library/Fonts/Arial.ttf", "/System/Library/Fonts/Supplemental/Arial.ttf",
                 "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"):
        if path and os.path.exists(path):
            return ImageFont.truetype(path, size)
    try:
        return ImageFont.load_default(size=size)  # Pillow 10.1 and later
    except TypeError:
        return ImageFont.load_default()


font = _font(22)
labels = ["key -2", "key -1", "touchdown", "key +1", "key +2", "key +3", "mid-stance"]
im0 = Image.open(os.path.join(mdir, rows[0][0], "f%03d.png" % int(rows[0][1].split(",")[0])))
w, h = im0.size
cap = 32
sheet = Image.new("RGB", (w * 7, (h + cap) * len(rows)), "white")
dr = ImageDraw.Draw(sheet)
for r, (clip, frames) in enumerate(rows):
    for c, f in enumerate(int(x) for x in frames.split(",")):
        sheet.paste(Image.open(os.path.join(mdir, clip, "f%03d.png" % f)).convert("RGB"), (c * w, r * (h + cap) + cap))
        dr.text((c * w + 6, r * (h + cap) + 4), "%s frame %d (%s)" % (clip, f, labels[c]), fill="black", font=font)
if scale != 1.0:
    sheet = sheet.resize((int(sheet.width * scale), int(sheet.height * scale)))
sheet.save(out)
print("saved", out, sheet.size)
