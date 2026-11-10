"""Shared helpers for the reskin scripts (pod 49): argument parsing, template and Meshy loading, eye measure, side check, weight mapping."""
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_map as M  # noqa: E402
from reskin_split import Fail  # noqa: E402


LAST_LOOPS = None


def argv():
    a = sys.argv[sys.argv.index("--") + 1:]
    o = {"src": None, "game": None, "tex": None, "outdir": None, "metal": None, "rough": None, "eye_z": None, "fit": [], "force_fail": None,
         "dry": False, "allow_s": False, "rot_tol": "0.05"}
    i = 0
    while i < len(a):
        k = a[i]
        if k in ("--dry", "--allow-s", "--allow-dk1", "--allow-band", "--allow-selfint", "--allow-shin", "--fbx-scale-all", "--allow-k4"):
            o[k.lstrip("-").replace("-", "_")] = True
            i += 1
            continue
        v = a[i + 1]
        if k == "--fit":
            o["fit"] = [x for x in v.split(",") if x]
        elif k == "--eye-z":
            o["eye_z"] = float(v)
        else:
            o[k.lstrip("-").replace("-", "_")] = v
        i += 2
    return o


def load_game(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    for o in [o for o in bpy.data.objects if o.type == "MESH"]:
        bpy.data.objects.remove(o, do_unlink=True)
    if not np.allclose(np.array(arm.matrix_world), np.eye(4), atol=1e-6):
        raise Fail("game armature is not at identity")
    bones = {b.name: {"h": np.array(b.head_local), "t": np.array(b.tail_local), "p": b.parent.name if b.parent else None,
                      "m": np.array(b.matrix_local)} for b in arm.data.bones}
    return arm, bones


def load_meshy(path):
    bpy.ops.import_scene.fbx(filepath=path)
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    arm = mesh.parent
    me = mesh.data
    mw = np.array(mesh.matrix_world)
    rot = mw[:3, :3] / np.cbrt(abs(np.linalg.det(mw[:3, :3])))
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    V = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    nv = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("normal", nv)
    N = nv.reshape(-1, 3) @ rot.T
    amw = np.array(arm.matrix_world)
    bones = {}
    for b in arm.data.bones:
        bones[b.name] = {"h": amw[:3, :3] @ np.array(b.head_local) + amw[:3, 3], "t": amw[:3, :3] @ np.array(b.tail_local) + amw[:3, 3],
                         "p": b.parent.name if b.parent else None}
    names = [g.name for g in mesh.vertex_groups]
    W = np.zeros((len(V), len(names)))
    for v in me.vertices:
        for g in v.groups:
            W[v.index, g.group] = g.weight
    li = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", li)
    try:
        ln = np.empty(len(me.loops) * 3)
        me.corner_normals.foreach_get("vector", ln)
        LN = ln.reshape(-1, 3) @ rot.T
    except Exception:  # noqa: BLE001
        LN = N[li]
    global LAST_LOOPS
    LAST_LOOPS = (LN, li)  # the source's per-loop normals (its custom split normals), read by the callers that carry them
    return mesh, V, N, W, names, bones


def mesh_uvs(me):
    lu = np.empty(len(me.loops) * 2)
    me.uv_layers[0].data.foreach_get("uv", lu)
    lu = lu.reshape(-1, 2)
    li = np.empty(len(me.loops), dtype=np.int64)
    me.loops.foreach_get("vertex_index", li)
    uv_v = np.zeros((len(me.vertices), 2))
    uv_v[li] = lu
    return uv_v, lu


def measure_eye(V, W, names, lum, override):
    """Eye height of the Meshy mesh at rest: median z of the bright (sclera, highlight) face texels beside the nose (hair and brows, the dark
    texels, sit about 7 cm higher; checked against the front still, eyes about 1.645 m). --eye-z overrides."""
    if override is not None:
        return override, "override"
    hw = sum(W[:, names.index(n)] for n in M.HEAD_BONES if n in names)
    m = hw >= 0.5
    ym = V[m, 1]
    face = (ym < np.percentile(ym, 30)) & (np.abs(V[m, 0]) > 0.02) & (np.abs(V[m, 0]) < 0.05)
    zs = V[m, 2]
    sel = face & (lum[m] > 0.55) & (zs > 1.55) & (zs < 1.75)
    if sel.sum() < 20:
        raise Fail("eye detection found %d bright face texels; pass --eye-z" % int(sel.sum()))
    return float(np.median(zs[sel])), "median z of %d bright face texels" % int(sel.sum())


def side_checks(mb, game, mapping):
    """Sides by POSITION: a Meshy Left/Right bone and the game bone it feeds must sit on the same side of the body."""
    for n, g in mapping.items():
        if ("Left" in n or "Right" in n) and g[-2:] in (".L", ".R"):
            ms = 1 if "Left" in n else -1
            gs = 1 if g.endswith(".L") else -1
            if np.sign(mb[n]["h"][0]) != ms or np.sign(game[g]["h"][0]) != gs or ms != gs:
                raise Fail("side check: %s (x %.3f) -> %s (x %.3f)" % (n, mb[n]["h"][0], g, game[g]["h"][0]))


def build_weights(W, names, game, mapping):
    gnames = list(game.keys())
    gi = {n: i for i, n in enumerate(gnames)}
    G = np.zeros((W.shape[0], len(gnames)))
    for i, n in enumerate(names):
        if n in mapping:
            G[:, gi[mapping[n]]] += W[:, i]
    return G, gi, gnames


def clean(G):
    """At most 4 weights, prune below 0.01, normalise."""
    out = np.zeros_like(G)
    for r in range(len(G)):
        row = G[r]
        keep = np.zeros(len(row), bool)
        keep[np.argsort(row)[-4:]] = True
        v = np.where(keep & (row >= 0.01), row, 0)
        s = v.sum()
        out[r] = v / s if s > 0 else v
    return out
