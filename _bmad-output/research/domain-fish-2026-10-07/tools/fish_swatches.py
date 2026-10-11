# G3 tint swatches: back / belly / fins (sRGB hex), saturation capped for the muted river palette (N5).
# Writes a swatch card PNG (opaque RGB) and prints the capped values. Pure stdlib.
import colorsys, struct, sys, zlib

S_CAP = 0.40      # HSV saturation ceiling (N5: no saturation)
V_CAP = 0.82      # nothing near white: bellies are dull, never bright (no glow read under murk)

SW = [  # species, back, belly, fins, mottle (0..1)
    ("channel catfish", "5A6052", "B8B49C", "4E5048", 0.10),
    ("flathead catfish", "6B5A3E", "B5A783", "5A4C36", 0.35),
    ("blue catfish", "5E6670", "C2C4BE", "565C62", 0.00),
    ("largemouth bass", "5C6440", "C9C4A0", "6A6A48", 0.20),
    ("smallmouth bass", "6E5E3C", "C2B48E", "6A5634", 0.25),
    ("spotted bass", "646040", "C8C0A0", "64604A", 0.25),
    ("sauger", "66603E", "CFC8AA", "6A6448", 0.35),
    ("crappie", "5E6656", "B9BCB2", "5A5E52", 0.30),
    ("bluegill / sunfish", "4F5A48", "A08E5E", "4A4E40", 0.10),
    ("freshwater drum", "6E7270", "BEBEB4", "6A6C68", 0.00),
    ("gar", "5E5A3E", "B3A986", "5A5438", 0.40),
    ("buffalo", "5E5A48", "B0AA92", "56524A", 0.05),
    ("common carp", "6C6038", "B8A570", "6A5236", 0.10),
    ("gizzard / threadfin shad", "646C68", "C6C8C0", "60645E", 0.00),
    ("skipjack herring", "566A66", "C8CCC4", "5E6462", 0.00),
    ("paddlefish (census only)", "5E6468", "B4B6B2", "585E62", 0.00),
    ("white bass", "6A706A", "C4C6BE", "646862", 0.00),
]

def cap(hexs):
    r, g, b = (int(hexs[i:i + 2], 16) / 255 for i in (0, 2, 4))
    h, s, v = colorsys.rgb_to_hsv(r, g, b)
    r, g, b = colorsys.hsv_to_rgb(h, min(s, S_CAP), min(v, V_CAP))
    return tuple(round(c * 255) for c in (r, g, b)), s

def png(path, w, h, rows):
    raw = b"".join(b"\x00" + bytes(row) for row in rows)
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    with open(path, "wb") as f:
        f.write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
                + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

cell, gap = 48, 4
W, H = 3 * cell + 2 * gap, len(SW) * (cell + gap)
img = [[(0x80, 0x80, 0x80)] * W for _ in range(H)]
for i, (name, *cols, mottle) in enumerate(SW):
    out = []
    for j, c in enumerate(cols):
        rgb, s0 = cap(c)
        out.append("%02X%02X%02X%s" % (*rgb, "*" if s0 > S_CAP else ""))
        for y in range(i * (cell + gap), i * (cell + gap) + cell):
            for x in range(j * (cell + gap), j * (cell + gap) + cell):
                img[y][x] = rgb
    print(f"| {name} | #{out[0]} | #{out[1]} | #{out[2]} | {mottle:.2f} |")
TIER = {"bluegill / sunfish": "V1", "largemouth bass": "V1", "common carp": "V1", "gar": "V1",
        "spotted bass": "V2", "smallmouth bass": "V2", "crappie": "V2", "buffalo": "V2"}
if len(sys.argv) > 1:
    png(sys.argv[1], W, H, [[c for px in row for c in px] for row in img])
    print("wrote", sys.argv[1])
if len(sys.argv) > 2:  # labelled card (f-director): species, tier and hex per row; needs Pillow
    from PIL import Image, ImageDraw, ImageFont
    font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", 18)
    lw, head = 300, 34
    card = Image.new("RGB", (lw + W + 3 * 130, H + head), (0x80, 0x80, 0x80))
    d = ImageDraw.Draw(card)
    for j, t in enumerate(("back", "belly", "fins")):
        d.text((lw + j * (cell + gap) + 4, 8), t, fill=(20, 20, 20), font=font)
    d.text((lw + W + 10, 8), "hex (back / belly / fins), mottle", fill=(20, 20, 20), font=font)
    for i, (name, *cols, mottle) in enumerate(SW):
        y0 = head + i * (cell + gap)
        d.text((8, y0 + 14), f"{name}  [{TIER.get(name, 'V3')}]", fill=(15, 15, 15), font=font)
        hexes = []
        for j, c in enumerate(cols):
            rgb, _ = cap(c)
            hexes.append("#%02X%02X%02X" % rgb)
            d.rectangle([lw + j * (cell + gap), y0, lw + j * (cell + gap) + cell - 1, y0 + cell - 1], fill=rgb)
        d.text((lw + W + 10, y0 + 14), " / ".join(hexes) + f"   m {mottle:.2f}", fill=(15, 15, 15), font=font)
    card.save(sys.argv[2])
    print("wrote", sys.argv[2])
