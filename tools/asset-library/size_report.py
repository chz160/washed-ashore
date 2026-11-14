"""Phase 0 size report: what each scope and resolution choice would cost on disk.

    py tools/asset-library/size_report.py            # writes _catalog/size_report.md and .json

Scopes: A = relevance high + medium, B = A + low (general use), C = everything.
Sizes are the download sizes the APIs report (ambientCG zips; Poly Haven per-map files).
Megascans is not included: Fab has no public metadata API and we don't scrape it.
"""
from __future__ import annotations

import collections
import json
import re
import sys

from build_catalog import iter_records
from common import CATALOG, MIN_FREE_BYTES, free_bytes, guard_path, human, today, write_json

SCOPES = {"A": ("high", "medium"), "B": ("high", "medium", "low"), "C": ("high", "medium", "low", "none")}


def tnum(t: str) -> int:
    m = re.match(r"(\d+)k$", t)
    return int(m.group(1)) if m else 0


def order_for(rec, tex_fmt):
    if rec["type"] == "HDRI":
        return ["exr", "hdr", "zip"]
    if rec["type"] in ("model", "plant"):
        return ["fbx", f"hq-{tex_fmt}", "hq-png", "hq-jpg", "gltf", "blend", "usd"]
    other = "jpg" if tex_fmt == "png" else "png"
    return [tex_fmt, other, "exr", "obj"]


def pick(rec, target: int, tex_fmt: str):
    """(tier, variant, bytes) for the largest tier <= target (or the smallest tier if all are larger)."""
    tiers = sorted((t for t in rec.get("size_options", {}) if tnum(t)), key=tnum)
    if not tiers:
        return None
    below = [t for t in tiers if tnum(t) <= target]
    tier = below[-1] if below else tiers[0]
    opts = rec["size_options"][tier]
    for v in order_for(rec, tex_fmt):
        if v in opts and opts[v]:
            return tier, v, opts[v]
    v, b = max(((v, b) for v, b in opts.items() if "animated" not in v), key=lambda x: x[1], default=(None, 0))
    return (tier, v, b) if v else None


def cost(rec, plan):
    """plan: dict(main=4, extra=2 or None, tex='png'|'jpg', hdri=4). Returns list of (tier, bytes)."""
    out = []
    if rec["type"] == "HDRI":
        p = pick(rec, plan["hdri"], plan["tex"])
        return [(p[0], p[2])] if p else []
    native = rec.get("size_options", {}).get("native")
    if native:  # ambientCG substances: one .sbsar, no resolution tiers
        return [("native", native.get("sbsar") or max(native.values()))]
    p = pick(rec, plan["main"], plan["tex"])
    if p:
        out.append((p[0], p[2]))
    if plan.get("extra") and rec["type"] not in ("model", "plant"):
        q = pick(rec, plan["extra"], plan["tex"])
        if q and (not p or q[0] != p[0]):
            out.append((q[0], q[2]))
    return out


PLANS = {
    "4K PNG + 2K PNG (recommended)": dict(main=4, extra=2, tex="png", hdri=4),
    "4K PNG only": dict(main=4, extra=None, tex="png", hdri=4),
    "4K JPG + 2K JPG": dict(main=4, extra=2, tex="jpg", hdri=4),
    "2K PNG only": dict(main=2, extra=None, tex="png", hdri=2),
    "8K PNG + 2K PNG": dict(main=8, extra=2, tex="png", hdri=8),
}


def main() -> int:
    recs = list(iter_records())
    free = free_bytes()
    budget = free - MIN_FREE_BYTES
    rep = {"date": today(), "free_bytes": free, "budget_bytes": budget, "asset_counts": {}, "plans": {}}

    counts = collections.Counter((r["source"], r["type"], r["bells_bend_relevance"]) for r in recs)
    rep["asset_counts"] = {"|".join(k): v for k, v in counts.items()}

    for pname, plan in PLANS.items():
        rep["plans"][pname] = {}
        for scope, levels in SCOPES.items():
            by_src, by_type, by_tier, by_rel, total = tuple(collections.Counter() for _ in range(4)) + (0,)
            n = 0
            for r in recs:
                if r["bells_bend_relevance"] not in levels:
                    continue
                c = cost(r, plan)
                if not c:
                    continue
                n += 1
                for tier, b in c:
                    total += b
                    by_src[r["source"]] += b
                    by_type[r["type"]] += b
                    by_tier[tier] += b
                    by_rel[r["bells_bend_relevance"]] += b
            rep["plans"][pname][scope] = {"assets": n, "total": total, "by_source": dict(by_src),
                                          "by_type": dict(by_type), "by_tier": dict(by_tier),
                                          "by_relevance": dict(by_rel)}
    write_json(CATALOG / "size_report.json", rep)
    write_md(rep, recs)
    return 0


def write_md(rep, recs):
    L = [f"# Asset library size report (Phase 0, {rep['date']})", "",
         f"E: free now: **{human(rep['free_bytes'])}**. Floor: 200 GB free. "
         f"Budget for the library: **{human(rep['budget_bytes'])}**.", "",
         "Sizes are download sizes reported by the APIs. Megascans is not included (no public metadata API).", "",
         "## Assets cataloged", "", "| Source | Type | high | medium | low | none | total |", "|---|---|--:|--:|--:|--:|--:|"]
    st = collections.defaultdict(collections.Counter)
    for r in recs:
        st[(r["source"], r["type"])][r["bells_bend_relevance"]] += 1
    for (s, t), c in sorted(st.items()):
        L.append(f"| {s} | {t} | {c['high']} | {c['medium']} | {c['low']} | {c['none']} | {sum(c.values())} |")
    tot = collections.Counter(r["bells_bend_relevance"] for r in recs)
    L.append(f"| **all** | | {tot['high']} | {tot['medium']} | {tot['low']} | {tot['none']} | {len(recs)} |")

    L += ["", "## Totals by scope and plan", "", "| Plan | A: relevant | B: + general use | C: everything |", "|---|--:|--:|--:|"]
    for p, sc in rep["plans"].items():
        L.append(f"| {p} | " + " | ".join(f"{human(sc[s]['total'])} ({sc[s]['assets']})" for s in "ABC") + " |")

    for p in ("4K PNG + 2K PNG (recommended)",):
        sc = rep["plans"][p]
        for title, key in (("source", "by_source"), ("asset type", "by_type"), ("resolution tier", "by_tier"),
                           ("relevance", "by_relevance")):
            keys = sorted(set().union(*[sc[s][key].keys() for s in "ABC"]),
                          key=lambda k: (tnum(k) if key == "by_tier" else 0, k))
            L += ["", f"### {p}: by {title}", "", f"| {title} | A | B | C |", "|---|--:|--:|--:|"]
            for k in keys:
                L.append(f"| {k} | " + " | ".join(human(sc[s][key].get(k, 0)) for s in "ABC") + " |")
    guard_path(CATALOG / "size_report.md").write_text("\n".join(L) + "\n", encoding="utf-8")


if __name__ == "__main__":
    sys.exit(main())
