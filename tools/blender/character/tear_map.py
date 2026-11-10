"""Maps hit-triangle rest positions onto the Survivor Body's open (tear) loops.
python tear_map.py <Survivor_Body.fbx> [<hits.csv: id,x,y,z per line, metres in the file's frame>] [radius]
Lists the Body's welded open-edge loops (0.1 mm weld; id, vertex count, centre, z range, bounding extent) read from the FBX itself; with a CSV, prints for every hit the nearest loop and the distance
from the hit's rest position to the nearest open EDGE, and flags hits within `radius` (default 0.01 m). Plain python (numpy): no Blender."""
import sys

import numpy as np

from fbx_read import geometry


def loops(path, tol=1e-4):
    v, polys = next(iter(geometry(path).values()))
    key, w = {}, []
    for p in v:
        w.append(key.setdefault(tuple(np.round(p / tol).astype(int)), len(key)))
    cnt, first = {}, {}
    for f in polys:
        for a, b in zip(f, f[1:] + f[:1]):
            if w[a] != w[b]:
                k = (min(w[a], w[b]), max(w[a], w[b]))
                cnt[k] = cnt.get(k, 0) + 1
                first.setdefault(k, (a, b))
    edges = [first[k] for k, c in cnt.items() if c == 1]
    adj = {}
    for a, b in edges:
        adj.setdefault(w[a], set()).add(w[b])
        adj.setdefault(w[b], set()).add(w[a])
    rep = {}
    for i, x in enumerate(w):
        rep.setdefault(x, i)
    seen, out = set(), []
    for s in adj:
        if s in seen:
            continue
        stack, comp = [s], []
        seen.add(s)
        while stack:
            c = stack.pop()
            comp.append(c)
            for n in adj[c]:
                if n not in seen:
                    seen.add(n)
                    stack.append(n)
        comp_edges = [(a, b) for a, b in edges if w[a] in comp]
        pts = np.array([v[rep[c]] for c in comp])
        out.append({"verts": len(comp), "centre": pts.mean(0), "zmin": float(pts[:, 2].min()), "zmax": float(pts[:, 2].max()), "extent": (pts.max(0) - pts.min(0)), "edges": comp_edges,
                    "vertex_indices": sorted(rep[c] for c in comp)})
    out.sort(key=lambda L: -L["zmax"])
    for i, L in enumerate(out):
        L["id"] = i
    return v, out


def dist_point_segment(p, a, b):
    ab = b - a
    t = np.clip(np.dot(p - a, ab) / (np.dot(ab, ab) + 1e-18), 0.0, 1.0)
    return float(np.linalg.norm(p - (a + t * ab)))


if __name__ == "__main__":
    path = sys.argv[1]
    v, L = loops(path)
    print("OPEN LOOPS in", path, ":", len(L), "loops,", sum(len(x["edges"]) for x in L), "edges")
    for x in L:
        print("  loop %2d: %d vertices, centre (%.3f, %.3f, %.3f), z %.3f..%.3f, extent (%.3f, %.3f, %.3f) m%s" % (
            x["id"], x["verts"], *x["centre"], x["zmin"], x["zmax"], *x["extent"], "  [2 vertices = slit]" if x["verts"] == 2 else ""))
    if len(sys.argv) > 2:
        radius = float(sys.argv[3]) if len(sys.argv) > 3 else 0.01
        for line in open(sys.argv[2]):
            if not line.strip() or line.startswith("#"):
                continue
            hid, x, y, z = line.strip().split(",")[:4]
            p = np.array([float(x), float(y), float(z)])
            best = min(((dist_point_segment(p, v[a], v[b]), l["id"]) for l in L for a, b in l["edges"]))
            print("HIT %s (%.3f, %.3f, %.3f): nearest open edge %.4f m, loop %d (%d vertices)%s" % (hid, *p, best[0], best[1], L[best[1]]["verts"], "  NEAR (<= %.3f m)" % radius if best[0] <= radius else ""))
