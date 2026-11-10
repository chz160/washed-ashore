"""Head/body split, neck cap, object build, FBX export and re-import check for reskin_survivor.py (pod 49)."""
import hashlib
import os
import math
import subprocess

import bpy
import numpy as np


class Fail(Exception):
    pass


def vertex_normals(V, polys):
    N = np.zeros_like(V)
    for p in polys:
        for k in range(1, len(p) - 1):
            a, b, c = V[p[0]], V[p[k]], V[p[k + 1]]
            n = np.cross(b - a, c - a)
            for i in (p[0], p[k], p[k + 1]):
                N[i] += n
    ln = np.linalg.norm(N, axis=1)
    N /= np.maximum(ln, 1e-12)[:, None]
    return N


def split_polys(polys, head_vertex):
    head, body = [], []
    for i, p in enumerate(polys):
        n = sum(1 for v in p if head_vertex[v])
        (head if n * 2 >= len(p) else body).append(i)
    return head, body


def top4(row):
    """At most 4 weights, normalised (the cap centre averages a ring of vertices)."""
    keep = np.zeros(len(row), bool)
    keep[np.argsort(row)[-4:]] = True
    v = np.where(keep, row, 0.0)
    return v / v.sum()


def seam_cap(polys, body_ids, head_vertex, V, G, uv_v, weld):
    """Fan caps for the body's open neck loop, found on POSITION-welded ids (the Meshy mesh splits vertices along UV seams). Seam = body edges used by
    exactly one body face whose both ends touch the head piece."""
    edge_count = {}
    rep = {}
    for i in body_ids:
        p = polys[i]
        for v in p:
            rep.setdefault(int(weld[v]), v)
        for a, b in zip(p, p[1:] + p[:1]):
            wa, wb = int(weld[a]), int(weld[b])
            k = (min(wa, wb), max(wa, wb))
            edge_count[k] = edge_count.get(k, 0) + 1
    head_w = {int(weld[v]) for p in polys if any(head_vertex[v] for v in p) for v in p}
    seam = [k for k, c in edge_count.items() if c == 1 and k[0] in head_w and k[1] in head_w]
    adj = {}
    for a, b in seam:
        adj.setdefault(a, set()).add(b)
        adj.setdefault(b, set()).add(a)
    comps, seen = [], set()
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
        comps.append(comp)
    caps = []
    for comp in comps:
        if len(comp) < 8:
            continue  # a stray open edge, not the neck loop
        ring_v = [rep[w] for w in comp]
        pts = V[ring_v]
        c = pts.mean(0)
        ang = np.arctan2(pts[:, 1] - c[1], pts[:, 0] - c[0])
        ordered = [ring_v[i] for i in np.argsort(ang)]
        caps.append({"ring": ordered, "centre": c - np.array([0, 0, 0.03]), "g": top4(G[ring_v].mean(0)), "uv": uv_v[ring_v].mean(0)})
    return caps, len(seam), [len(c) for c in comps]


def boundary_caps(polys, body_ids, V, G, uv_v, weld, src_open, lum=None, near=0.08, dark_pct=10):
    """Caps for EVERY boundary edge of the body piece that the source mesh did not already have (position-welded): one triangle per edge, reversed against the body face
    that owns it (so the cap is consistent with the body's winding), joined to the centre of its connected boundary component. Works on the body faces' own vertices, so
    the exported mesh has no open edge left at the neck (td-49 / qa-49: the old seam cap used one representative vertex per position and missed loop fragments).
    The cap's colour comes from a dark inner-collar texel (director-49, gate/pod49-neck-cap-look): the UV of the vertex at the dark_pct-th percentile of luma among the vertices
    within `near` metres of the component's centre, so the cap reads as the shadowed inside of the flannel collar. Returns (caps, report): cap = {"ring": vertices used, "tris": [(own vertex a, own vertex b)], "centre", "g", "uv"}."""
    cnt, owner = {}, {}
    for i in body_ids:
        p = polys[i]
        for a, b in zip(p, p[1:] + p[:1]):
            wa, wb = int(weld[a]), int(weld[b])
            if wa == wb:
                continue
            k = (min(wa, wb), max(wa, wb))
            cnt[k] = cnt.get(k, 0) + 1
            owner[k] = (a, b)
    new = {k for k, c in cnt.items() if c == 1} - set(src_open)
    adj = {}
    for a, b in new:
        adj.setdefault(a, set()).add(b)
        adj.setdefault(b, set()).add(a)
    comp_of, comps = {}, []
    for s0 in adj:
        if s0 in comp_of:
            continue
        stack, comp = [s0], []
        comp_of[s0] = len(comps)
        while stack:
            c = stack.pop()
            comp.append(c)
            for n in adj[c]:
                if n not in comp_of:
                    comp_of[n] = len(comps)
                    stack.append(n)
        comps.append(comp)
    caps, report = [], []
    for ci, comp in enumerate(comps):
        edges = [k for k in new if comp_of[k[0]] == ci]
        own = {}
        for k in edges:
            for v in owner[k]:
                own[int(weld[v])] = v
        ring = list(own.values())
        pts = V[ring]
        c = pts.mean(0)
        uv = uv_v[ring].mean(0)
        pick = None
        if lum is not None:
            near_ids = np.nonzero(np.linalg.norm(V - c, axis=1) < near)[0]
            order = near_ids[np.argsort(lum[near_ids])]
            vi = int(order[int(len(order) * dark_pct / 100.0)])
            uv = uv_v[vi]
            pick = {"vertex": vi, "uv": [round(float(uv[0]), 4), round(float(uv[1]), 4)], "luma": round(float(lum[vi]), 3), "candidates": int(len(order))}
        caps.append({"ring": ring, "tris": [owner[k] for k in edges], "centre": c - np.array([0, 0, 0.03]), "g": top4(G[ring].mean(0)), "uv": uv})
        report.append({"edges": len(edges), "vertices": len(comp), "centre": c.round(3).tolist(), "z": [round(float(pts[:, 2].min()), 3), round(float(pts[:, 2].max()), 3)], "collar_texel": pick})
    return caps, report


def build_object(name, V, polys, loop_uv, normals, G, gnames, ids, material, caps, arm, loop_normals=None, shapes=None, patches=()):
    ids = list(ids)
    used = sorted({v for i in ids for v in polys[i]} | {v for c in caps for v in c["ring"]})
    remap = {v: k for k, v in enumerate(used)}
    verts = [tuple(V[v]) for v in used]
    nrm = [tuple(normals[v]) for v in used]
    gw = [G[v] for v in used]
    fpolys = [tuple(remap[v] for v in polys[i]) for i in ids]
    uvs = []
    for i in ids:
        uvs.extend(loop_uv[i])
    for pt in patches:   # backing patches (td-49 H3 order: [original][backing][cap, centre last]): three new vertices (the rim's weights), one face facing out, the loop's texel
        base = len(verts)
        for k in range(3):
            verts.append(tuple(pt["points"][k]))
            nrm.append(tuple(pt["normal"]))
            gw.append(pt["g"][k])
        fpolys.append((base, base + 1, base + 2))
        uvs.extend([pt["uv"]] * 3)
        pt["report"].update({"exported_rim_vertices": [remap[v] for v in pt["rim"]], "exported_patch_vertices": [base, base + 1, base + 2], "exported_face": len(fpolys) - 1})
    remap_centre = {}
    for c in caps:
        ci = len(verts)
        remap_centre[id(c)] = ci
        verts.append(tuple(c["centre"]))
        nrm.append((0.0, 0.0, -1.0))   # the cap is the shadowed inside of the collar: its normal points away from the sun (director-49)
        gw.append(c["g"])
        for a, b in c["tris"]:   # (a, b) is the body face's own direction of the boundary edge: the cap runs b -> a
            fpolys.append((remap[b], remap[a], ci))
            uvs.extend([c["uv"]] * 3)
    for c in caps:   # double-sided cap (td-49 CAP FIX): the mirror of every cap triangle (same vertices, reversed winding), so a triangle that folds in the Sleep pose is never culled
        ci = remap_centre[id(c)]
        for a, b in c["tris"]:
            fpolys.append((remap[a], remap[b], ci))
            uvs.extend([c["uv"]] * 3)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], fpolys)
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))   # from_pydata makes flat faces, which would override the carried normals
    ncap_polys = len(fpolys) - len(ids)   # the cap triangles and their mirrors are the polygons after the body faces
    for pg in me.polygons[len(ids):]:     # sharp edges around every cap face: the cap's custom (0,0,-1) normals must not merge with the smooth fans of the ring vertices (td-49 / qa-49: 14 corners had been averaged)
        for li in pg.loop_indices:
            me.edges[me.loops[li].edge_index].use_edge_sharp = True
    layer = me.uv_layers.new(name="UVMap")
    for k, uv in enumerate(uvs):
        layer.data[k].uv = (float(uv[0]), float(uv[1]))
    if loop_normals is not None:   # the source's own split normals, carried per loop (td T4)
        LN, starts = loop_normals
        ln = []
        for i in ids:
            ln.extend(tuple(LN[starts[i] + k]) for k in range(len(polys[i])))
        for pt in patches:
            ln.extend([tuple(pt["normal"])] * 3)
        for c in caps:
            ln.extend([(0.0, 0.0, -1.0)] * (3 * len(c["tris"])))
        for c in caps:   # the mirror triangles keep the cap's normal (0,0,-1), so both sides shade as the shadowed inside of the collar (director: no beige side)
            ln.extend([(0.0, 0.0, -1.0)] * (3 * len(c["tris"])))
        me.normals_split_custom_set(ln)   # a failure here is a refusal, not a silent fallback (td-49)
        nrm = None
    if nrm is not None:
        me.normals_split_custom_set_from_vertices(nrm)
    me.materials.append(material)
    obj = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(obj)
    for gi, gname in enumerate(gnames):
        col = [(k, w[gi]) for k, w in enumerate(gw) if w[gi] > 0]
        if not col:
            continue
        vg = obj.vertex_groups.new(name=gname)
        for k, w in col:
            vg.add([k], float(w), "REPLACE")
    if shapes:   # corrective blend shapes (bind-space deltas); the cap vertices (appended last) keep their base position
        obj.shape_key_add(name="Basis", from_mix=False)
        for sname, delta in shapes.items():
            sk = obj.shape_key_add(name=sname, from_mix=False)
            for k, v in enumerate(used):
                sk.data[k].co = (V[v] + delta[v]).tolist()
    obj.parent = arm
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    return obj, len(verts), len(fpolys)


def export_fbx(arm, obj, path, scale_all=False):
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    obj.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=False,
                             path_mode="STRIP", use_mesh_modifiers=False, use_custom_props=True,
                             **({"apply_scale_options": "FBX_SCALE_ALL"} if scale_all else {}))   # scale_all: the cm unit factor is applied to the data (vertices, shape deltas, bones) instead of a x100 model scale


def git_hash(path):
    """RAW content hash (git hash-object --no-filters), the identity of the file bytes."""
    try:
        return subprocess.check_output(["git", "hash-object", "--no-filters", path], text=True).strip()
    except Exception:  # noqa: BLE001
        return "sha1:" + hashlib.sha1(open(path, "rb").read()).hexdigest()


REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))


def git_hashes(path, target="Assets/Art/Character/Skeleton_Human.fbx"):
    """{'raw': hash of the bytes (--no-filters), 'lfs_pointer': what git stores for it at `target` in this repo: git hash-object --path <target> <file>, cwd = repo root (td K5)}."""
    out = {"raw": git_hash(path), "lfs_pointer": None, "target": target}
    try:
        out["lfs_pointer"] = subprocess.check_output(["git", "hash-object", "--path", target, os.path.abspath(path)], cwd=REPO_ROOT, text=True).strip()
    except Exception:  # noqa: BLE001
        pass
    return out


def open_edges(polys, weld):
    """Boundary edges (used by exactly one face) on POSITION-welded vertex ids, as a set of (a, b) pairs."""
    cnt = {}
    for p in polys:
        for a, b in zip(p, p[1:] + p[:1]):
            wa, wb = int(weld[a]), int(weld[b])
            if wa != wb:
                k = (min(wa, wb), max(wa, wb))
                cnt[k] = cnt.get(k, 0) + 1
    return {k for k, c in cnt.items() if c == 1}


def verify_export(path, game, expect_mesh, rot_tol=0.05, expect_shapes=()):
    """Fresh scene: re-import the FBX and compare it with the game skeleton (td F1, fail-closed): same 79 names, every bind matrix within 1e-4 m
    and the rotation tolerance of the game's, one mesh with the expected name, at most 4 weights per vertex. Raises Fail on any mismatch."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if len(arms) != 1 or arms[0].name != "CharacterArmature":
        raise Fail("%s: armature node is %s" % (path, [a.name for a in arms]))
    bones = {b.name: b for b in arms[0].data.bones}
    if set(bones) != set(game):
        raise Fail("%s: bone set differs: missing %s extra %s" % (path, sorted(set(game) - set(bones))[:5], sorted(set(bones) - set(game))[:5]))
    worst_t, worst_r = 0.0, 0.0
    for n, b in bones.items():
        m = np.array(b.matrix_local)
        gm = game[n]["m"]
        worst_t = max(worst_t, float(np.abs(m[:3, 3] - gm[:3, 3]).max()))
        c = (np.trace(m[:3, :3].T @ gm[:3, :3]) - 1.0) / 2.0
        worst_r = max(worst_r, float(np.degrees(np.arccos(np.clip(c, -1, 1)))))
    # td asked for 0.01 degrees; Blender's own FBX round trip of the untouched Farmer measures up to 0.048 on near-vertical and finger bones,
    # so rot_tol 0.05 is the noise floor of THIS check (ruling asked of td-49); the Unity bindpose test covers the rest.
    if worst_t > 1e-4 or worst_r > rot_tol:
        raise Fail("%s: bind matrices differ from the game skeleton: %.6f m, %.4f degrees" % (path, worst_t, worst_r))
    if len(meshes) != 1 or meshes[0].name != expect_mesh:
        raise Fail("%s: mesh names %s" % (path, [m.name for m in meshes]))
    got = [k.name for k in meshes[0].data.shape_keys.key_blocks] if meshes[0].data.shape_keys else []
    missing = [s for s in expect_shapes if s not in got]
    if missing:
        raise Fail("%s: blend shapes missing after re-import: %s (found %s)" % (path, missing, got))
    maxw = max((len([g for g in v.groups if g.weight > 1e-6]) for v in meshes[0].data.vertices), default=0)
    if maxw > 4:
        raise Fail("%s: a vertex has %d weights" % (path, maxw))
    return {"bones": len(bones), "bind_max_translation_m": round(worst_t, 7), "bind_max_rotation_deg": round(worst_r, 5), "max_weights": maxw, "shape_keys": got,
            "verts": len(meshes[0].data.vertices)}


def pos_key(p):
    """A position as a tuple of python floats rounded to 0.1 mm (the same on the source and the re-imported side)."""
    return tuple(round(float(x), 4) for x in p)


def exported_open_edges(path, tol=1e-5):
    """Open (boundary) edges of the RE-IMPORTED piece, welded by position, with their loops (td-49 DK4, fail-closed in the caller), and the custom property 'source'."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    ob = next(o for o in bpy.data.objects if o.type == "MESH")
    me = ob.data
    key, w = {}, []
    V = np.array([v.co for v in me.vertices])
    for v in V:
        w.append(key.setdefault(tuple(np.round(v / tol).astype(int)), len(key)))
    cnt = {}
    for p in me.polygons:
        vs = [w[x] for x in p.vertices]
        for a, b in zip(vs, vs[1:] + vs[:1]):
            if a != b:
                k = (min(a, b), max(a, b))
                cnt[k] = cnt.get(k, 0) + 1
    op = [k for k, c in cnt.items() if c == 1]
    rep = {}
    for i, x in enumerate(w):
        rep.setdefault(x, i)
    adj = {}
    for a, b in op:
        adj.setdefault(a, set()).add(b)
        adj.setdefault(b, set()).add(a)
    seen, loops = set(), []
    for s0 in adj:
        if s0 in seen:
            continue
        st, comp = [s0], []
        seen.add(s0)
        while st:
            c = st.pop()
            comp.append(c)
            for n in adj[c]:
                if n not in seen:
                    seen.add(n)
                    st.append(n)
        P = np.array([V[rep[c]] for c in comp])
        loops.append({"vertices": len(comp), "z": [round(float(P[:, 2].min()), 3), round(float(P[:, 2].max()), 3)]})
    loops.sort(key=lambda q: -q["z"][1])
    pos = {tuple(sorted((pos_key(V[rep[a]]), pos_key(V[rep[b]])))) for a, b in op}
    return {"edges": len(op), "loops": loops, "source_property": ob.get("source"), "positions": pos}


def cap_double_sided(path):
    """Fail-closed check on the exported Body, read from the FBX itself (Blender's importer drops a face that duplicates another with reversed winding): every cap triangle (a face using the
    last vertex, the cap centre) has its reverse among the faces. Returns (cap triangles, cap triangles without their reverse)."""
    from fbx_read import geometry
    v, polys = next(iter(geometry(path).values()))
    centre = len(v) - 1
    tris = [t for t in polys if centre in t and len(t) == 3]
    def edge(t):
        i = t.index(centre)
        return (t[(i + 1) % 3], t[(i + 2) % 3])   # the ring edge in the triangle's own direction
    have = {edge(t) for t in tris}
    return len(tris), sum(1 for t in tris if (edge(t)[1], edge(t)[0]) not in have)


def cap_corner_normals(path, ref=None, tol=5e-4):   # Blender stores custom normals compressed: the landed final4a cap corners already deviate by up to 1.4e-4 from (0,0,-1)
    """FBX-direct (td-49 / qa-49): every corner of every cap face (both windings) carries exactly (0,0,-1) in the file, and, given a reference Body FBX (the landed final4a), the corner normals of the
    non-cap loops are unchanged against it. Returns (cap corners, cap corners at (0,0,-1), non-cap loops changed vs the reference or None, non-cap loops compared)."""
    import numpy as np
    from fbx_read import geometry, normals
    v, polys = next(iter(geometry(path).values()))
    n, _m = normals(path)
    centre = len(v) - 1
    cap, k = [], 0
    for p in polys:
        for _ in p:
            cap.append(centre in p)
            k += 1
    cap = np.array(cap)
    corners = n[cap]
    ok = int((np.abs(corners - np.array([0.0, 0.0, -1.0])).max(axis=1) < tol).sum())
    changed, compared = None, 0
    if ref:
        vr, pr = next(iter(geometry(ref).values()))
        nr, _ = normals(ref)
        cr, c0 = np.array([(len(vr) - 1) in p for p in pr for _ in p]), len(vr) - 1
        body_new, body_ref = n[~cap], nr[~cr]
        compared = len(body_ref)
        changed = int((np.abs(body_new - body_ref).max(axis=1) > tol).sum()) if len(body_new) == len(body_ref) else compared
    return len(corners), ok, changed, compared


def open_loops(polys, body_ids, V, weld, src_open):
    """The Body piece's source open loops (welded components of `src_open` that sit on body faces): [{"w": welded ids, "verts": own vertex per welded id, "centre", "zmin", "zmax"}], sorted by z descending."""
    own = {}
    for i in body_ids:
        for v in polys[i]:
            own.setdefault(int(weld[v]), v)
    adj = {}
    for a, b in src_open:
        if a in own and b in own:
            adj.setdefault(a, set()).add(b)
            adj.setdefault(b, set()).add(a)
    seen, out = set(), []
    for s0 in adj:
        if s0 in seen:
            continue
        stack, comp = [s0], []
        seen.add(s0)
        while stack:
            c = stack.pop()
            comp.append(c)
            for n in adj[c]:
                if n not in seen:
                    seen.add(n)
                    stack.append(n)
        verts = [own[c] for c in comp]
        pts = V[verts]
        out.append({"w": comp, "verts": verts, "centre": pts.mean(0), "zmin": float(pts[:, 2].min()), "zmax": float(pts[:, 2].max())})
    out.sort(key=lambda L: -L["zmax"])
    return out


def backing_patches(loops, rule, V, N, G, uv_v, uv_for, offset=0.008, scale=1.4):
    """One backing patch per selected open loop (td-49 H1/H2 + director-49): rule = (zlo, zhi, vmin, vmax, expect, kind); the script selects the loops by the rule and FAILS unless exactly `expect` match.
    The patch is a triangle on the rim's first three vertices (3-vertex loops; larger loops use their three widest-apart vertices): copies of the rim positions pushed `offset` metres inward along the rim's mean
    outward normal and scaled `scale` about the loop centre, the rim's skin weights, winding so that the face looks OUT through the tear, the texel `uv_for(centre, kind)` gives. The rim edges stay open."""
    zlo, zhi, vmin, vmax, expect, kind = rule
    chosen = [L for L in loops if zlo <= L["centre"][2] <= zhi and vmin <= len(L["verts"]) <= vmax]
    if len(chosen) != expect:
        raise Fail("backing patches (%s): the rule z %.3f..%.3f, %d..%d vertices matches %d open loops, expected exactly %d (centres %s)" % (
            kind, zlo, zhi, vmin, vmax, len(chosen), expect, [np.round(L["centre"], 3).tolist() for L in chosen]))
    out = []
    for L in chosen:
        vs = L["verts"]
        if len(vs) > 3:
            P = V[vs]
            d = np.linalg.norm(P[:, None] - P[None], axis=2)
            i, j = np.unravel_index(d.argmax(), d.shape)
            k = int(np.argmax(np.minimum(d[i], d[j])))
            vs = [vs[i], vs[j], vs[k]]
        c = L["centre"]
        nout = N[vs].mean(0)
        nout /= np.linalg.norm(nout) + 1e-12
        pts = [c + scale * (V[v] - c) - offset * nout for v in vs]
        n = np.cross(pts[1] - pts[0], pts[2] - pts[0])
        if np.dot(n, nout) < 0:
            vs, pts = [vs[0], vs[2], vs[1]], [pts[0], pts[2], pts[1]]
        uv = uv_for(c, kind)
        out.append({"rim": vs, "points": pts, "g": [G[v] for v in vs], "uv": uv, "normal": (0.0, 0.0, -1.0) if kind == "dark" else tuple(nout.tolist()),
                    "report": {"kind": kind, "centre": np.round(c, 4).tolist(), "rim_vertices": [int(v) for v in vs], "offset_m": offset, "scale": scale, "uv": [round(float(uv[0]), 4), round(float(uv[1]), 4)]}})
    return out
