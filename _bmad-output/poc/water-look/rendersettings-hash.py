"""Hash the RenderSettings (and LightmapSettings-independent) block of World.unity, for before/after comparison."""
import hashlib, re, sys

path = sys.argv[1] if len(sys.argv) > 1 else r"E:\GitHub\washed-ashore\Assets\Scenes\World.unity"
text = open(path, encoding="utf-8").read()
docs = re.split(r"(?m)^--- ", text)
blk = [d for d in docs if re.search(r"(?m)^RenderSettings:", d)]
if len(blk) != 1:
    sys.exit(f"expected 1 RenderSettings block, found {len(blk)}")
b = blk[0]
print("RenderSettings sha256:", hashlib.sha256(b.encode()).hexdigest()[:16], f"({len(b.splitlines())} lines)")
for f in (path, r"E:\GitHub\washed-ashore\Assets\World\BellsBend\Water\WaterTile.asset",
          r"E:\GitHub\washed-ashore\Assets\World\BellsBend\Water\WaterRipple.png",
          r"E:\GitHub\washed-ashore\Assets\World\BellsBend\Water\WaterMaps.png"):
    try: print("file sha256_16:", hashlib.sha256(open(f, "rb").read()).hexdigest()[:16], f)
    except FileNotFoundError: print("missing:", f)
for key in ("m_Fog:", "m_FogColor", "m_FogMode", "m_FogDensity", "m_LinearFogStart", "m_LinearFogEnd", "m_SkyboxMaterial", "m_DefaultReflectionMode"):
    for line in b.splitlines():
        if line.strip().startswith(key):
            print(" ", line.strip())
