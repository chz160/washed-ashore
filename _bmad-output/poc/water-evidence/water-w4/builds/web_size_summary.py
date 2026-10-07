"""Read-only: joins web-object-sizes.json, web-compressed-sizes.json and web-compressed-tiles.json into the side-by-side
table w-td asked for (uncompressed vs LZ4HC vs shipped Brotli, plus offline Brotli q11 of source data)."""
import json
from collections import defaultdict

B = "E:/GitHub/washed-ashore/TestResults/water-w4/builds/"
obj = json.load(open(B + "web-object-sizes.json"))
comp = json.load(open(B + "web-compressed-sizes.json"))
tiles = json.load(open(B + "web-compressed-tiles.json"))
MB = 1e6


def group(o):
    n = o["name"]
    if o["type"] == "TerrainData" or n.startswith("SplatAlpha"):
        return "Terrain tiles (20 TerrainData + 40 SplatAlpha maps)"
    if n in ("WaterMaps", "WaterRipple") or n.startswith("BellsBendWater"):
        return "Water (WaterMaps, WaterRipple, shader, material)"
    if o["type"] == "Texture2D":
        return "Other textures (kits, ground, splash)"
    return o["type"] + "s" if o["type"] in ("Shader", "Mesh", "AnimationClip") else "Everything else in the bundle"


g = defaultdict(lambda: [0, 0, 0])
for o in obj["all"]:
    v = g[group(o)]
    v[0] += o["bytes"]; v[1] += o["lz4"]; v[2] += o["brotli"]
lines = ["Web build: uncompressed vs shipped (read-only; Builds/Web/Build/Web.data.unityweb, 2026-10-06 19:10 build)",
         f"Web.data on disk {obj['webDataBytes']:,} B. It is Brotli q11 (recompressing the decompressed data at q11 gives "
         f"{comp['recompressed_q11']:,} B; q9 {comp['recompressed_q9']:,}). Inside: data.unity3d = UnityFS bundle, "
         f"{obj['bundleUnpacked'] / MB:.1f} MB unpacked -> {obj['bundleLz4'] / MB:.1f} MB LZ4HC (2,783 blocks) -> "
         f"{obj['dataUnity3dShippedBytes'] / MB:.2f} MB shipped; IL2CPP metadata + default resources etc. "
         f"{obj['otherContainerEntriesShipped'] / MB:.2f} MB shipped.",
         "Per-object 'shipped' = its share of each LZ4HC block it spans, Brotli'd per block and scaled to the real total "
         "(estimate; group totals are within the per-block approximation, all objects sum to "
         f"{sum(o['brotli'] for o in obj['all']) / MB:.2f} MB).", "",
         f"{'group':58s} {'raw MB':>8s} {'LZ4HC MB':>9s} {'shipped MB':>11s} {'% of Web.data':>13s}"]
for k, v in sorted(g.items(), key=lambda kv: -kv[1][2]):
    lines.append(f"{k:58s} {v[0] / MB:8.2f} {v[1] / MB:9.2f} {v[2] / MB:11.2f} {100 * v[2] / obj['webDataBytes']:12.1f}%")
lines.append(f"{'Non-bundle entries (IL2CPP global-metadata, default resources)':58s} {'':8s} {'':9s} "
             f"{obj['otherContainerEntriesShipped'] / MB:11.2f} {100 * obj['otherContainerEntriesShipped'] / obj['webDataBytes']:12.1f}%")
lines += ["", "Top 30 objects by shipped bytes:", f"{'type':12s} {'name':38s} {'raw MB':>8s} {'LZ4HC MB':>9s} {'shipped MB':>11s}"]
for o in obj["topByShipped"][:30]:
    lines.append(f"{o['type']:12s} {(o['name'] or '(unnamed)')[:38]:38s} {o['bytes'] / MB:8.2f} {o['lz4'] / MB:9.2f} {o['brotli'] / MB:11.2f}")
wm = next(o for o in obj["all"] if o["name"] == "WaterMaps")
src = next(s for s in comp["sources"] if "WaterMaps" in s["file"])
lines += ["", "WaterMaps specifically (RG16, 2048x2560x2 = 10,485,760 texel bytes, no mips):",
          f"  in build: object {wm['bytes']:,} B raw -> {wm['lz4']:,} B LZ4HC -> ~{wm['brotli']:,} B shipped",
          f"  offline: raw RG16 bytes Brotli q11 alone -> {src['brotli']:,} B ({100 * src['brotli'] / src['bytes']:.1f}%)",
          "  (the build's LZ4HC pass before Brotli is why the shipped share is ~2x the standalone Brotli)",
          "", "Terrain tiles, offline cross-check: Brotli q11 of the 20 TD_*.asset source files "
          f"{tiles['raw'] / MB:.1f} MB -> {tiles['brotli_q11'] / MB:.2f} MB (build share above: "
          f"{g['Terrain tiles (20 TerrainData + 40 SplatAlpha maps)'][2] / MB:.2f} MB)."]
open(B + "web-size-compressed.txt", "w", encoding="utf-8").write("\n".join(lines) + "\n")
print("\n".join(lines))
