"""Read-only: parse the Unity Editor log 'Build Report' (Unity prints it from the BuildReport after each player build) for
web builds and write the top assets plus per-folder totals. Usage: python web_size_breakdown.py (paths are fixed below)."""
import json, os, re
from collections import defaultdict

ROOT = "E:/GitHub/washed-ashore"
OUT = ROOT + "/TestResults/water-w4/builds/web-size-breakdown"
BUILDS = [
    ("water (W10, 2026-10-06 19:06-19:10 local)", "Builds/build-web-water.log", "Builds/Web/Build/Web.data.unityweb"),
    ("bells-bend-land deploy build (2026-10-06 16:27 local, pre-water)", "Builds/build-web-deploy.log", None),
    ("web-pod baseline (2026-10-04 23:59 local, project E:/GitHub/test-fps)", "Builds/build-web-baseline.log", "Builds/Web-baseline/Build/Web.data.unityweb"),
]
UNIT = {"kb": 1 / 1024, "mb": 1.0, "gb": 1024.0}


def mb(num, unit):
    return float(num) * UNIT[unit.lower()]


def folder(path, depth):
    if not (path.startswith("Assets/") or path.startswith("Packages/")):
        return "(built-in / other)"
    return "/".join(path.split("/")[:depth])


def parse(log):
    lines = open(os.path.join(ROOT, log), encoding="utf-8", errors="replace").read().splitlines()
    start = max(i for i, l in enumerate(lines) if l.strip() == "Build Report")
    cats, assets, complete = {}, [], None
    in_assets = False
    for l in lines[start + 1:]:
        m = re.match(r"^(Textures|Meshes|Animations|Sounds|Shaders|Other Assets|Levels|File headers|Total User Assets)\s+([\d.]+) (kb|mb|gb)", l)
        if m:
            cats[m.group(1)] = round(mb(m.group(2), m.group(3)), 2)
            continue
        m = re.match(r"^Complete build size\s+([\d.]+) (kb|mb|gb)", l)
        if m:
            complete = round(mb(m.group(1), m.group(2)), 2)
            continue
        if l.startswith("Used Assets and files"):
            in_assets = True
            continue
        if in_assets:
            m = re.match(r"^\s*([\d.]+) (kb|mb|gb)\s+[\d.]+% (.+)$", l)
            if not m:
                break
            assets.append((m.group(3).strip(), mb(m.group(1), m.group(2))))
    return cats, complete, assets


def main():
    report, text = [], []
    text.append("Web build size breakdown (read-only; source = the 'Build Report' Unity writes to the build's Editor log from the")
    text.append("BuildReport; sizes are Unity's UNCOMPRESSED per-asset sizes, Web.data on disk is Brotli-compressed)\n")
    for name, log, data in BUILDS:
        cats, complete, assets = parse(log)
        data_bytes = os.path.getsize(os.path.join(ROOT, data)) if data and os.path.exists(os.path.join(ROOT, data)) else None
        f2, f4 = defaultdict(float), defaultdict(float)
        for p, s in assets:
            f2[folder(p, 3)] += s
            f4[folder(p, 4)] += s
        top = sorted(assets, key=lambda a: -a[1])[:30]
        entry = {"build": name, "log": log, "web_data_bytes": data_bytes, "complete_build_size_mb": complete, "categories_mb": cats,
                 "asset_count": len(assets), "assets_total_mb": round(sum(s for _, s in assets), 2),
                 "top30": [{"path": p, "mb": round(s, 2)} for p, s in top],
                 "folders_depth3_mb": {k: round(v, 2) for k, v in sorted(f2.items(), key=lambda kv: -kv[1])},
                 "folders_depth4_mb": {k: round(v, 2) for k, v in sorted(f4.items(), key=lambda kv: -kv[1])}}
        report.append(entry)
        text.append(f"=== {name}\n log: {log}")
        text.append(f" Web.data on disk: {data_bytes if data_bytes is not None else 'n/a (output folder since replaced)'}"
                    + (f" bytes = {data_bytes / 1048576:.1f} MiB" if data_bytes else ""))
        text.append(f" Complete build size (Unity): {complete} MB; assets listed: {len(assets)}, sum {entry['assets_total_mb']} MB")
        text.append(" Categories (MB): " + ", ".join(f"{k} {v}" for k, v in cats.items()))
        text.append(" Top 30 by size (MB):")
        text += [f"   {s:8.2f}  {p}" for p, s in top]
        text.append(" Totals per folder (depth 3, MB):")
        text += [f"   {v:8.2f}  {k}" for k, v in entry["folders_depth3_mb"].items()]
        text.append("")
    # water vs pre-water deltas per depth-4 folder
    a, b = report[0]["folders_depth4_mb"], report[1]["folders_depth4_mb"]
    delta = {k: round(a.get(k, 0) - b.get(k, 0), 2) for k in set(a) | set(b)}
    delta = {k: v for k, v in sorted(delta.items(), key=lambda kv: -abs(kv[1])) if abs(v) >= 0.05}
    text.append("=== Delta water minus pre-water deploy build, per depth-4 folder (MB, |delta| >= 0.05):")
    text += [f"   {v:+8.2f}  {k}" for k, v in delta.items()]
    pa = dict((p, s) for p, s in parse(BUILDS[0][1])[2]); pb = dict((p, s) for p, s in parse(BUILDS[1][1])[2])
    new = sorted(((p, s) for p, s in pa.items() if p not in pb), key=lambda x: -x[1])
    gone = sorted(((p, s) for p, s in pb.items() if p not in pa), key=lambda x: -x[1])
    text.append(" Assets only in the water build: " + "; ".join(f"{p} {s:.2f}" for p, s in new[:20]))
    text.append(" Assets only in the pre-water build: " + "; ".join(f"{p} {s:.2f}" for p, s in gone[:20]))
    json.dump({"builds": report, "delta_water_minus_prewater_depth4_mb": delta,
               "only_in_water": [{"path": p, "mb": round(s, 2)} for p, s in new],
               "only_in_prewater": [{"path": p, "mb": round(s, 2)} for p, s in gone]}, open(OUT + ".json", "w"), indent=1)
    open(OUT + ".txt", "w", encoding="utf-8").write("\n".join(text) + "\n")
    print("\n".join(text))


main()
