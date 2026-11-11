"""Encodes the frame sequences of clip_motion.py: a labelled PNG strip (frame numbers) and MP4s (real time and half speed) with a moving ground-tick line.
python label_motion.py <motion dir> <out dir> <title> <speed m/s clip space> <clip> [<clip> ...]   (clips side by side, same frame count; e.g. Jog_Full Jog)
Ground ticks move backward at the given speed (the clips are in place; the stance foot's ground speed is the clip's own)."""
import glob, os, subprocess, sys
from PIL import Image, ImageDraw, ImageFont

mdir, out, title, speed = sys.argv[1], sys.argv[2], sys.argv[3], float(sys.argv[4])
clips = sys.argv[5:]
os.makedirs(out, exist_ok=True)
PXM = 800 / 2.2           # pixels per metre of the ortho camera
FLOOR = int(400 + 0.9 * PXM)   # y of z = 0 (camera aimed at z = 0.9)
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


font = _font(24)
seqs = {c: sorted(glob.glob(os.path.join(mdir, c, "f*.png"))) for c in clips}
n = len(seqs[clips[0]])
fr0 = int(os.path.basename(seqs[clips[0]][0])[1:4])
assert all(len(v) == n for v in seqs.values()), "clips differ in frame count"


def cell(c, i, t):
    im = Image.open(seqs[c][i]).convert("RGB")
    d = ImageDraw.Draw(im)
    for k in range(-1, 5):
        x = int((k * 0.5 * PXM + t * speed * PXM) % (2.5 * PXM))   # ticks every 0.5 m moving to the right (the body faces left)
        d.line([(x, FLOOR), (x, FLOOR + 14)], fill=(70, 70, 70), width=3)
    d.line([(0, FLOOR), (500, FLOOR)], fill=(70, 70, 70), width=2)
    d.text((8, 6), "%s  frame %d" % (c, fr0 + i), fill="black", font=font)
    return im


def frame(i):
    t = i / 60.0
    row = Image.new("RGB", (500 * len(clips), 800), "white")
    for k, c in enumerate(clips):
        row.paste(cell(c, i, t), (500 * k, 0))
    return row


# PNG strip: 8 frames evenly across the cycle, one row per clip
idx = [int(round(j * n / 8)) % n for j in range(8)]
strip = Image.new("RGB", (500 * 8, 800 * len(clips)), "white")
for r, c in enumerate(clips):
    for j, i in enumerate(idx):
        strip.paste(cell(c, i, i / 60.0), (500 * j, 800 * r))
strip = strip.resize((strip.width // 2, strip.height // 2))
strip.save(os.path.join(out, "%s_strip.png" % title))
# MP4: five cycles
tmp = os.path.join(out, "_f_" + title)
os.makedirs(tmp, exist_ok=True)
total = 5 * n
for g in range(total):
    t = g / 60.0
    row = Image.new("RGB", (500 * len(clips), 800), "white")
    for k, c in enumerate(clips):
        row.paste(cell(c, g % n, t), (500 * k, 0))
    row.save(os.path.join(tmp, "%04d.png" % g))
for rate, tag in ((60, "realtime"), (30, "halfspeed")):
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(rate), "-i", os.path.join(tmp, "%04d.png"), "-pix_fmt", "yuv420p", "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2",
                    os.path.join(out, "%s_%s.mp4" % (title, tag))], check=True)
for f in glob.glob(os.path.join(tmp, "*.png")):
    os.remove(f)
os.rmdir(tmp)
print("saved", out, title, n, "frames per cycle, 5 cycles")
