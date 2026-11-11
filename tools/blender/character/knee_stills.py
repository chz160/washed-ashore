"""Stills for the DK1 corrective blend shapes (pod 49): knee 90 and a first-person elbow 90 without and with the shapes; the head piece is removed for the
first-person view. blender -b -noaudio -P knee_stills.py -- --src --game --skel --tex --outdir (same arguments as p4_stills.py)."""
import os
import sys

import bmesh
import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_checks as C  # noqa: E402
import reskin_io as R  # noqa: E402
import reskin_map as M  # noqa: E402
import skeleton_stills as S  # noqa: E402
from p4_stills import FP_FOV, shoot_persp  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402
from reskin_split import Fail  # noqa: E402

JOINTS = (("Knee", "UpperLeg", "LowerLeg", "knee."), ("Elbow", "UpperArm", "LowerArm", "elbow."))


def shapes_for(b, skel):
    """Knees only (td Update 2), refined like the exporter does."""
    V3, G, gn = b["V"], b["G"], b["gnames"]
    out = {}
    for side in ("L", "R"):
        pl = C.cross_ratio(V3, G, gn, skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0))["planes"]
        out["Knee_%s_90" % side] = C.corrective_refined(V3, G, gn, skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0), pl)[0]
    return out


def main():
    o = R.argv()
    os.makedirs(o["outdir"], exist_ok=True)
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    b["uv"] = b["loop_uv"]
    shapes = shapes_for(b, skel)
    hv = sum(b["W"][:, b["names"].index(n)] for n in M.HEAD_BONES if n in b["names"]) >= 0.5
    arm, cam = S.scene({"skel": o["skel"], "tex": o["tex"]}, b)
    ob = bpy.data.objects["Survivor"]
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bm.faces.ensure_lookup_table()
    gone = [f for f in bm.faces if sum(1 for v in f.verts if hv[v.index]) * 2 >= len(f.verts)]
    bmesh.ops.delete(bm, geom=gone, context="FACES_ONLY")   # the first-person body has no head piece
    bm.to_mesh(ob.data)
    bm.free()
    ob.shape_key_add(name="Basis", from_mix=False)
    keys = {}
    for k, dl in shapes.items():
        sk = ob.shape_key_add(name=k, from_mix=False)
        for i, v in enumerate(b["V"]):
            sk.data[i].co = (v + dl[i]).tolist()
        keys[k] = sk

    def corr(on):
        for k, sk in keys.items():
            sk.value = 1.0 if on else 0.0

    out = []
    for label, on in (("nocorr", False), ("corr", True)):
        corr(on)
        S.reset(arm)
        S.rot(arm, "LowerLeg.L", (1, 0, 0), 90)
        S.follow_ankle(arm, "L")
        out.append(S.shoot_at(o, cam, "knee90_%s_side" % label, (5, 0, 0.45), (0, 0, 0.45), 1.3, (700, 800)))
        out.append(S.shoot_at(o, cam, "knee90_%s_threequarter" % label, (3.0, -3.5, 0.9), (0, 0, 0.45), 1.3, (700, 800)))
    S.sheet(out, os.path.join(o["outdir"], "sheet_knee90.png"), 4)
    fp = []
    for label, on in (("nocorr", False), ("corr", True)):
        corr(on)
        S.reset(arm)
        for sd, sg in (("L", 1), ("R", -1)):
            S.rot(arm, "UpperArm." + sd, (0, 1, 0), 90 * sg)
            S.rot(arm, "LowerArm." + sd, (1, 0, 0), -90)
        fp.append(shoot_persp(o, cam, "elbow90_fp_%s" % label, (0.0, -0.08, 1.67), (0.0, -0.45, 1.05), FP_FOV, (960, 540)))
    S.sheet(fp, os.path.join(o["outdir"], "sheet_elbow90_fp.png"), 2)
    print("STILLS OK", o["outdir"])


if __name__ == "__main__":
    try:
        main()
    except Fail as e:
        print("STILLS FAIL:", e)
        sys.exit(2)
