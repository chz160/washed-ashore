"""Read-only: writes WaterMaps.png's two channels as the raw RG16 bytes the build stores (2 bytes per texel, no mips)."""
import sys
import numpy as np
from PIL import Image

im = Image.open("E:/GitHub/washed-ashore/Assets/World/BellsBend/Water/WaterMaps.png")
a = np.asarray(im)
rg = a[..., :2] if a.ndim == 3 else a
raw = np.ascontiguousarray(rg.astype(np.uint8)).tobytes()
open(sys.argv[1], "wb").write(raw)
print(f"{im.mode} {im.size} -> {len(raw)} bytes")
