"""Locate and show the front/side self-intersecting triangle pairs that the knee shape adds at knee 90 (td-49: region, height, trouser or skin; highlighted stills).
blender -b -noaudio -P knee_pairs.py -- --src --game --skel --tex --outdir
The pair triangles are taken from the same numpy pose (shape weight 1, foot following the shin) and drawn in red, depth-tested, over the evaluated mesh: a pair hidden
inside the cloth does not show; a visible one does."""
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_checks as C  # noqa: E402
import reskin_io as R  # noqa: E402
import skeleton_stills as S  # noqa: E402
from knee_proof import evaluate, set_foot  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402


def tri_obj(P, tris, name, col):
    me = bpy.data.meshes.new(name)
    verts = [tuple(P[v]) for t in tris for v in t]
    faces = [(3 * i, 3 * i + 1, 3 * i + 2) for i in range(len(tris))]
    me.from_pydata(verts, [], faces)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = col
    ob.data.materials.append(mat)
    return ob


def main():
    o = R.argv()
    os.makedirs(o["outdir"], exist_ok=True)
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    b["uv"] = b["loop_uv"]
    V, G, gn, polys, weld, lum = b["V"], b["G"], b["gnames"], b["polys"], b["weld"], b["lum"]
    shapes = {}
    for side in ("L", "R"):
        pl = C.cross_ratio(V, G, gn, skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0))["planes"]
        shapes[side] = C.corrective_refined(V, G, gn, skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0), pl)[0]
    pairs_by_side = {}
    for side in ("L", "R"):
        sgn = 1.0 if side == "L" else -1.0
        jn = skel["LowerLeg." + side]["h"]
        kax = (jn - skel["UpperLeg." + side]["h"]) / np.linalg.norm(jn - skel["UpperLeg." + side]["h"])
        band = (np.abs(V[:, 2] - jn[2]) < 0.22) & (V[:, 0] * sgn > -0.02)
        Vp0, _ = C.pose(V, G, gn, skel, {"LowerLeg." + side: ((1, 0, 0), 90)}, follow=(side,))
        Vp1, _ = C.pose(V + shapes[side], G, gn, skel, {"LowerLeg." + side: ((1, 0, 0), 90)}, follow=(side,))
        C.self_intersections_classified(Vp0, V, polys, weld, band, jn, kax)
        without = list(C.classify_report["pairs"])
        C.self_intersections_classified(Vp1, V, polys, weld, band, jn, kax)
        withs = list(C.classify_report["pairs"])
        pairs_by_side[side] = (withs, without, Vp1)
        print("PAIRS knee %s: front/side without the shape %d, with %d" % (side, len(without), len(withs)))
        for k, (ta, tb) in enumerate(withs):
            cen = (V[list(ta)].mean(0) + V[list(tb)].mean(0)) / 2
            rel = cen - jn
            uvs = b["uv_v"][list(ta) + list(tb)]
            lm = float(lum[list(ta) + list(tb)].mean())
            print("  pair %d: rest centre rel. to the knee (x %.3f, y %.3f, z %.3f) m, along the shin axis %.3f m, base colour luma %.2f (skin is about 0.35-0.55, dark patch under 0.1)" % (
                k, rel[0], rel[1], rel[2], float(rel @ kax), lm))
    arm, cam = S.scene({"skel": o["skel"], "tex": o["tex"]}, b)
    ob = bpy.data.objects["Survivor"]
    ob.shape_key_add(name="Basis", from_mix=False)
    sk = ob.shape_key_add(name="Knee_L_90", from_mix=False)
    for i, v in enumerate(V):
        sk.data[i].co = (v + shapes["L"][i]).tolist()
    sk.value = 1.0
    S.reset(arm)
    S.rot(arm, "LowerLeg.L", (1, 0, 0), 90)
    tail = np.array(arm.pose.bones["LowerLeg.L"].tail)
    set_foot(arm, "L", tail, 90)
    ev = evaluate(ob)
    withs, without, Vp1 = pairs_by_side["L"]
    keep = V[:, 2] > b["collars"]["L"] + 0.05   # above the boot band: the numpy pose keeps the foot upright, Blender turns it with the shin
    print("CHECK numpy pose vs Blender evaluation above the boot, max difference: %.5f m" % float(np.abs(ev[keep] - Vp1[keep]).max()))
    red = tri_obj(ev, [ta for ta, tb in withs] + [tb for ta, tb in withs], "pairs", (1.0, 0.0, 0.0, 1.0))
    sc = bpy.context.scene
    out = []
    for nm, loc in (("side", (5, 0, 0.45)), ("tq", (3.0, -3.5, 0.9)), ("back34", (3.0, 3.5, 0.9))):
        sc.display.shading.color_type = "TEXTURE"
        red.hide_render = True
        out.append(S.shoot_at(o, cam, "pairs_%s_plain" % nm, loc, (0, 0, 0.45), 1.3, (700, 800)))
        red.hide_render = False
        sc.display.shading.color_type = "MATERIAL"   # the red triangles take their material colour; the mesh turns plain grey, which still hides them if they are inside
        out.append(S.shoot_at(o, cam, "pairs_%s_highlight" % nm, loc, (0, 0, 0.45), 1.3, (700, 800)))
        ob.hide_render = True   # control: the pair triangles alone, to show they exist where the mesh is
        out.append(S.shoot_at(o, cam, "pairs_%s_pairs_only" % nm, loc, (0, 0, 0.45), 1.3, (700, 800)))
        ob.hide_render = False
    S.sheet(out, os.path.join(o["outdir"], "sheet_pairs.png"), 3)
    print("PAIRS OK", o["outdir"])


if __name__ == "__main__":
    main()
