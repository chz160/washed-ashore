"""Rebuild _catalog/catalog.sqlite, catalog.csv and index.html from every asset.json in the library.

    py tools/asset-library/build_catalog.py

asset.json files are the source of truth; this script can be re-run at any time and never edits them.
"""
from __future__ import annotations

import csv
import html
import json
import os
import sqlite3
import sys
from pathlib import Path

import shutil

from common import CATALOG, LIB, STAGING, guard_path, log, today

SCALAR = ["source", "source_id", "name", "url", "type", "category", "category_original", "biome_or_region",
          "license", "license_url", "attribution_required", "license_checked_on", "normal_convention",
          "polycount", "lods", "billboards_or_impostors", "texel_density", "download_date",
          "bells_bend_relevance", "bells_bend_reason", "status", "notes", "thumbnail", "thumbnail_url"]
LISTS = ["tags", "tags_original", "available_resolutions", "available_formats", "maps", "candidate_for",
         "derived_fields", "real_world_size_mm"]


def iter_records():
    for src in sorted(p for p in LIB.iterdir() if p.is_dir() and not p.name.startswith("_")):
        for path in src.glob("*/*/asset.json"):
            if "_incoming" in path.parts:
                continue
            with open(path, encoding="utf-8") as f:
                rec = json.load(f)
            rec["_path"] = os.path.relpath(path.parent, LIB)
            yield rec


def build():
    recs = list(iter_records())
    CATALOG.mkdir(parents=True, exist_ok=True)
    db_path = guard_path(CATALOG / "catalog.sqlite")
    # SQLite locking is unreliable over SMB: build on local disk, then copy the finished file to the NAS.
    STAGING.mkdir(parents=True, exist_ok=True)
    tmp = STAGING / "catalog.sqlite.tmp"
    if tmp.exists():
        tmp.unlink()
    con = sqlite3.connect(tmp)
    cols = ", ".join(f'"{c}"' for c in SCALAR + LISTS)
    con.execute(f'CREATE TABLE assets (id TEXT PRIMARY KEY, path TEXT, {cols}, size_options TEXT, '
                f'downloaded TEXT, files TEXT, record TEXT)')
    con.execute("CREATE TABLE tags (id TEXT, tag TEXT)")
    con.execute("CREATE TABLE files (id TEXT, path TEXT, size INTEGER, sha256 TEXT)")
    for r in recs:
        rid = f"{r['source']}:{r['source_id']}"
        vals = [rid, r["_path"]] + [_scalar(r.get(c)) for c in SCALAR] + [json.dumps(r.get(c)) for c in LISTS]
        vals += [json.dumps(r.get("size_options")), json.dumps(r.get("downloaded")), json.dumps(r.get("files")),
                 json.dumps(r)]
        con.execute(f"INSERT INTO assets VALUES ({','.join('?' * len(vals))})", vals)
        con.executemany("INSERT INTO tags VALUES (?,?)", [(rid, t) for t in r.get("tags", [])])
        con.executemany("INSERT INTO files VALUES (?,?,?,?)",
                        [(rid, f.get("path"), f.get("size"), f.get("sha256")) for f in r.get("files", [])])
    for c in ("source", "type", "category", "bells_bend_relevance", "status", "license"):
        con.execute(f'CREATE INDEX ix_{c} ON assets("{c}")')
    con.execute("CREATE INDEX ix_tag ON tags(tag)")
    con.commit()
    con.close()
    shutil.copyfile(tmp, db_path)
    tmp.unlink()

    with open(guard_path(CATALOG / "catalog.csv"), "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["id", "path"] + SCALAR + LISTS)
        for r in recs:
            w.writerow([f"{r['source']}:{r['source_id']}", r["_path"]] + [_scalar(r.get(c)) for c in SCALAR]
                       + ["; ".join(map(str, r.get(c) or [])) for c in LISTS])

    write_html(recs)
    log(f"catalog: {len(recs)} records -> {CATALOG}")


def _scalar(v):
    if isinstance(v, (list, dict)):
        return json.dumps(v)
    return v


def _thumb(r):
    if r.get("thumbnail"):
        # relative to _catalog/index.html
        return Path(os.path.relpath(LIB / r["thumbnail"], CATALOG)).as_posix()
    return r.get("thumbnail_url") or ""


def write_html(recs):
    rows = [{
        "id": f"{r['source']}:{r['source_id']}", "n": r["name"], "s": r["source"], "t": r["type"],
        "c": r["category"], "co": r.get("category_original"), "g": r.get("tags", []),
        "res": r.get("available_resolutions", []), "dl": r.get("downloaded", {}).get("resolutions", []),
        "l": r.get("license"), "rel": r.get("bells_bend_relevance"), "why": r.get("bells_bend_reason"),
        "st": r.get("status"), "u": r.get("url"), "th": _thumb(r), "p": r["_path"].replace("\\", "/"),
        "cf": r.get("candidate_for", []), "m": r.get("maps", []),
    } for r in recs]
    data = json.dumps(rows, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    page = TEMPLATE.replace("__DATA__", data).replace("__DATE__", html.escape(today()))
    (guard_path(CATALOG / "index.html")).write_text(page, encoding="utf-8")


TEMPLATE = r"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Asset Library</title>
<style>
:root{--bg:#f6f5f2;--fg:#1d1d1b;--mut:#6b6a66;--card:#fff;--line:#dcdad4;--acc:#2f6b4f;--hi:#2f6b4f;--me:#9a7b1c;--lo:#7a7a7a;--no:#a33}
@media (prefers-color-scheme:dark){:root{--bg:#161615;--fg:#ecebe7;--mut:#a3a19b;--card:#21211f;--line:#353431;--acc:#7fc3a0}}
*{box-sizing:border-box}body{margin:0;font:14px/1.4 system-ui,Segoe UI,sans-serif;background:var(--bg);color:var(--fg)}
header{position:sticky;top:0;background:var(--bg);border-bottom:1px solid var(--line);padding:10px 16px;z-index:2}
h1{font-size:16px;margin:0 0 8px}.bar{display:flex;flex-wrap:wrap;gap:6px;align-items:center}
input,select{font:inherit;padding:5px 7px;border:1px solid var(--line);border-radius:6px;background:var(--card);color:var(--fg)}
input[type=search]{flex:1 1 220px}#count{color:var(--mut);margin-left:auto}
main{display:grid;grid-template-columns:repeat(auto-fill,minmax(190px,1fr));gap:10px;padding:12px 16px}
.c{background:var(--card);border:1px solid var(--line);border-radius:8px;overflow:hidden;display:flex;flex-direction:column}
.c img{width:100%;aspect-ratio:1;object-fit:cover;background:var(--line)}.b{padding:7px 9px;display:grid;gap:3px}
.n{font-weight:600}.k{color:var(--mut);font-size:12px}.r{font-size:11px;font-weight:600;text-transform:uppercase}
.r.high{color:var(--hi)}.r.medium{color:var(--me)}.r.low{color:var(--lo)}.r.none{color:var(--no)}
a{color:var(--acc)}#more{display:block;margin:0 auto 24px;padding:8px 16px}
</style></head><body>
<header><h1>Asset Library <span class="k">catalog built __DATE__ · thumbnails load from the source until an asset is downloaded</span></h1>
<div class="bar"><input type="search" id="q" placeholder="Search name, tags, category, id">
<select id="f-s"><option value="">All sources</option></select><select id="f-t"><option value="">All types</option></select>
<select id="f-c"><option value="">All categories</option></select><select id="f-rel"><option value="">Any relevance</option></select>
<select id="f-st"><option value="">Any status</option></select><select id="f-l"><option value="">Any license</option></select>
<select id="f-res"><option value="">Any resolution</option></select><span id="count"></span></div></header>
<main id="g"></main><button id="more">Show more</button>
<script>
const D=__DATA__;const $=s=>document.querySelector(s);const F={s:'s',t:'t',c:'c',rel:'rel',st:'st',l:'l'};
function opts(id,vals){const el=$('#f-'+id);[...new Set(vals)].filter(Boolean).sort().forEach(v=>{const o=document.createElement('option');o.value=o.textContent=v;el.appendChild(o)})}
for(const k in F)opts(k,D.map(r=>r[F[k]]));opts('res',D.flatMap(r=>r.res));
let shown=0,list=[];const esc=s=>String(s??'').replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
function filt(){const q=$('#q').value.toLowerCase().split(/\s+/).filter(Boolean);
list=D.filter(r=>{for(const k in F){const v=$('#f-'+k).value;if(v&&r[F[k]]!==v)return false}
const res=$('#f-res').value;if(res&&!r.res.includes(res))return false;
if(!q.length)return true;const h=(r.n+' '+r.id+' '+r.c+' '+r.co+' '+r.g.join(' ')+' '+r.cf.join(' ')).toLowerCase();return q.every(w=>h.includes(w))});
const order={high:0,medium:1,low:2,none:3};list.sort((a,b)=>(order[a.rel]??4)-(order[b.rel]??4)||a.n.localeCompare(b.n));
$('#g').innerHTML='';shown=0;more();$('#count').textContent=list.length+' of '+D.length}
function more(){const frag=list.slice(shown,shown+120).map(r=>`<div class="c"><img loading="lazy" alt="" src="${esc(r.th)}">
<div class="b"><span class="n">${esc(r.n)}</span><span class="r ${esc(r.rel)}" title="${esc(r.why)}">${esc(r.rel)} · ${esc(r.why)}</span>
<span class="k">${esc(r.s)} · ${esc(r.t)} · ${esc(r.c)}</span><span class="k">${esc(r.res.join(' '))} · ${esc(r.l)} · ${esc(r.st)}</span>
<span class="k">${esc(r.g.slice(0,8).join(', '))}</span><span class="k"><a href="${esc(r.u)}" target="_blank" rel="noopener">source</a> · ${esc(r.p)}</span></div></div>`).join('');
$('#g').insertAdjacentHTML('beforeend',frag);shown+=120;$('#more').style.display=shown<list.length?'':'none'}
document.querySelectorAll('input,select').forEach(e=>e.addEventListener('input',filt));$('#more').onclick=more;filt();
</script></body></html>
"""

if __name__ == "__main__":
    build()
    sys.exit(0)
