"""Build Skeleton_Human from a Mixamo-rigged Meshy body (pod 49, P4; td-49 adr/49-p4-realistic-skeleton item 1).

blender -b -noaudio -P make_skeleton_human.py -- --src <meshy char.fbx> --game <tools/blender/character/source/Farmer_Body_template.fbx> --tex <base colour png> --outdir <dir> [--dry] [--eye-z M]

Same 79 bone names and hierarchy as the Quaternius Men skeleton (the Farmer bind is the template), the SAME per-bone rotation matrix (axes and roll), and
joint positions taken from the Meshy body in a T bind: arms and legs rotated onto the template directions (rotation only, no scaling), joints from the
rotated rig, left and right averaged and mirrored (symmetrised), IK feet (children of Root) at his ankles, finger chains from the straightened fingers.
Writes Skeleton_Human.fbx (armature only) and skeleton_human.json (every bone: parent, head, tail, 4x4 matrix), prints the joint table and the
symmetry error, and re-imports the FBX to check it. Fails closed.
"""
import json
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_map as M  # noqa: E402
import reskin_io as R  # noqa: E402
from reskin_fingers import straighten  # noqa: E402
from reskin_fit import repose  # noqa: E402
from reskin_split import Fail, git_hashes  # noqa: E402
from reskin_tex import luma_at  # noqa: E402

SYM_TOL_M = 0.002    # td-49: symmetry error 2 mm or less

# game bone <- Meshy bone whose (rotated) head becomes that bone's head
HEAD_SRC = {"Hips": "Hips", "Abdomen": "Spine", "Torso": "Spine1", "Chest": "Spine2", "Neck": "Neck", "Head": "Head"}
SIDE_SRC = {"Shoulder": "Shoulder", "UpperArm": "Arm", "LowerArm": "ForeArm", "Wrist": "Hand", "UpperLeg": "UpLeg", "LowerLeg": "Leg", "Foot": "Foot"}


def mirror(p):
    return np.array([-p[0], p[1], p[2]])


def build_table(game, head2, sym_pre):
    """New head position for every game bone (before symmetrising), from the rotated Meshy rig."""
    P = M.P
    new = {}
    for gname, mname in HEAD_SRC.items():
        new[gname] = head2[P + mname].copy()
    for side, long in (("L", "Left"), ("R", "Right")):
        for gb, mb in SIDE_SRC.items():
            new["%s.%s" % (gb, side)] = head2[P + long + mb].copy()
    return new


def main():
    o = R.argv()
    garm, game = R.load_game(o["game"])
    mesh, V, N, W, names, mb = R.load_meshy(o["src"])
    me = mesh.data
    uv_v, _ = R.mesh_uvs(me)
    lum = luma_at(o["tex"], uv_v)
    eye_z, how = R.measure_eye(V, W, names, lum, o["eye_z"])
    floor = float(V[:, 2].min())
    hip = (mb[M.P + "LeftUpLeg"]["h"][:2] + mb[M.P + "RightUpLeg"]["h"][:2]) / 2
    ghip = (game["UpperLeg.L"]["h"][:2] + game["UpperLeg.R"]["h"][:2]) / 2
    c = np.array([0.0, hip[1], floor])  # the body midline is Meshy x = 0 (the legs of the Meshy rig are asymmetric, so the hip midpoint is not it)
    g = np.array([0.0, ghip[1], 0.0])
    # rotation-only repose onto the template directions; shoulders and the spine stay as modelled (the template bone rotations are copied below)
    V2, N2, head2, RAK = repose(V, N, W, names, mb, game, {}, (1.0, c, g), 0.0, M.LIMB_TARGET_P4)
    floor2 = float(V2[:, 2].min())
    c[2] = floor2
    xf = lambda p: (p - c) + g  # noqa: E731
    head3 = {k: xf(v) for k, v in head2.items()}
    V3 = V2 - c + g
    # fingers: straightened chains give the finger joints
    weld = np.unique(np.round(V * 1e5).astype(np.int64), axis=0, return_inverse=True)[1].ravel()
    edges = np.array([[e.vertices[0], e.vertices[1]] for e in me.edges])
    hh = {sd: head3[M.P + nm + "Hand"] for sd, nm in (("L", "Left"), ("R", "Right"))}
    chains = {}
    for side, hn in (("L", M.P + "LeftHand"), ("R", M.P + "RightHand")):
        res, info = straighten(V3, N2, W[:, names.index(hn)], hh[side], game, edges, side, M.PALM_FRACTION, weld)
        if res is None:
            raise Fail("finger groups not found on the %s hand: %s" % (side, info))
        for nm, pts in info["chains"].items():
            chains["%s.%s" % (nm, side)] = pts
    new = build_table(game, head3, None)
    new["Head_end"] = np.array([new["Head"][0], new["Head"][1], float(V3[:, 2].max())])  # the template head bone is vertical
    for nm, pts in chains.items():
        base, n = nm.split(".")[0], len(pts) - 1
        side = nm.split(".")[1]
        for i in range(n):
            new["%s%d.%s" % (base, i + 1, side)] = pts[i].copy()
    for sd, long in (("L", "Left"), ("R", "Right")):
        new["Foot." + sd] = head3[M.P + long + "Foot"].copy()
    # symmetry error of the SOURCE, then average L and mirrored R
    pairs = [(b, b[:-2] + ".R") for b in new if b.endswith(".L") and b[:-2] + ".R" in new]
    errs = sorted(((float(np.abs(new[l] - mirror(new[r])).max()), l) for l, r in pairs), reverse=True)
    src_err = errs[0][0]
    print("SOURCE ASYMMETRY, worst five pairs (m):", [(b, round(e, 4)) for e, b in errs[:5]])
    before = {k: v.copy() for k, v in new.items()}
    if o["force_fail"] != "skip_average":  # the forced-failure case skips the averaging, so the symmetry check below must refuse
        for l, r in pairs:
            m = (new[l] + mirror(new[r])) / 2
            new[l], new[r] = m, mirror(m)
    L2 = {"LowerArm": 0.02, "Wrist": 0.02, "LowerLeg": 0.03, "UpperLeg": 0.03}  # director L2: how far symmetrising may move a visible joint off his mesh (m)
    moves = {}
    for gb, lim in L2.items():
        for sd in ("L", "R"):
            k = "%s.%s" % (gb, sd)
            moves[k] = float(np.linalg.norm(new[k] - before[k]))
    print("SYMMETRISING moved (m; limits elbow 0.02, wrist 0.02, knee 0.03, hip 0.03):", {k: round(v, 4) for k, v in moves.items()})
    bad = {k: round(v, 4) for k, v in moves.items() if v > L2[k.split('.')[0]]}
    if bad:
        raise Fail("symmetrising moved a visible joint off his mesh beyond L2: %s" % bad)
    sym_now = max(float(np.abs(new[l] - mirror(new[r])).max()) for l, r in pairs)
    print("SYMMETRY error of the skeleton before export: %.5f m (limit %.3f)" % (sym_now, SYM_TOL_M))
    if sym_now > SYM_TOL_M:
        raise Fail("symmetry error %.4f m exceeds %.3f m" % (sym_now, SYM_TOL_M))
    # bones tied to height or kept from the template
    r_h = new["UpperLeg.L"][2] / game["UpperLeg.L"]["h"][2]
    hipc = (new["UpperLeg.L"] + new["UpperLeg.R"]) / 2
    oldc = (game["UpperLeg.L"]["h"] + game["UpperLeg.R"]["h"]) / 2
    new["Root"] = game["Root"]["h"].copy()
    new["Body"] = hipc + (game["Body"]["h"] - oldc) * r_h
    for sd in ("L", "R"):
        new["PT." + sd] = game["PT." + sd]["h"].copy()
    sym_ankle = float(abs(new["Foot.L"][0] + new["Foot.R"][0]))
    # leaves (*_end) and any bone not yet placed: follow the parent's tail, keep the template's vector
    out = {}
    order = sorted(game, key=lambda n: R_depth(game, n))
    for n in order:
        if n not in new:
            new[n] = None
    for n in order:
        b = game[n]
        Rg = b["m"][:3, :3]
        if new[n] is None:  # leaf or helper: head = parent's NEW tail
            p = b["p"]
            new[n] = out[p]["t"].copy()
        # bone length: distance to the child whose template head sits at this bone's template tail
        kids = [k for k in game if game[k]["p"] == n and np.linalg.norm(game[k]["h"] - b["t"]) < 0.004]
        old_len = float(np.linalg.norm(b["t"] - b["h"]))
        if kids and new.get(kids[0]) is not None:
            length = float(np.linalg.norm(new[kids[0]] - new[n]))
        else:
            length = old_len
        if n == "LowerLeg.L" or n == "LowerLeg.R":
            length = float(np.linalg.norm(new["Foot." + n[-1]] - new[n]))
        out[n] = {"p": b["p"], "h": new[n], "t": new[n] + Rg[:, 1] * length, "R": Rg}
    # hips / body / feet: Foot parent is Root, PT parent is Root (template hierarchy copied)
    table = []
    for n in order:
        d = out[n]["h"] - game[n]["h"]
        table.append({"bone": n, "head": out[n]["h"].round(4).tolist(), "template": game[n]["h"].round(4).tolist(), "delta": d.round(4).tolist()})
    print("JOINT TABLE (Skeleton_Human head vs template head, metres): elbow, wrist, knee, ankle first")
    first = ["LowerArm.L", "Wrist.L", "LowerLeg.L", "Foot.L", "LowerArm.R", "Wrist.R", "LowerLeg.R", "Foot.R"]
    for row in sorted(table, key=lambda r: (r["bone"] not in first, first.index(r["bone"]) if r["bone"] in first else 0)):
        print("  %-14s head %s  template %s  delta %s" % (row["bone"], row["head"], row["template"], row["delta"]))
    print("SYMMETRY error of the source rig before averaging: %.4f m; after averaging 0 by construction; ankle |x| %.4f" % (src_err, sym_ankle))
    print("HEIGHTS: hip %.3f, shoulder %.3f, neck %.3f, head %.3f, head top %.3f, ankle %.3f; eye %.3f (%s)" % (
        new["UpperLeg.L"][2], new["UpperArm.L"][2], new["Neck"][2], new["Head"][2], new["Head_end"][2], new["Foot.L"][2], eye_z - floor, how))
    print("EYE-MATCH: his eye %.4f m above the floor; body-frame target %.2f (controller %.2f + foot drop %.2f); the appearance carries skeletonScale %.4f (the skeleton stays true size)" % (
        eye_z - floor, M.EYE_STAND, M.EYE_STAND_CONTROLLER, M.FOOT_DROP, M.EYE_STAND / (eye_z - floor)))
    scaled = float(V3[:, 2].max()) * M.EYE_STAND / (eye_z - floor)
    print("HEIGHT: mesh %.3f m, scaled by that skeletonScale = %.3f m (band %.2f-%.2f m)" % (float(V3[:, 2].max()), scaled, M.HEIGHT_BAND[0], M.HEIGHT_BAND[1]))
    if not (M.HEIGHT_BAND[0] <= scaled <= M.HEIGHT_BAND[1]):
        print("HEIGHT ROUTE: %.3f m is outside %.2f-%.2f m: route to team-lead for Noah (the run carries on)" % (scaled, M.HEIGHT_BAND[0], M.HEIGHT_BAND[1]))
    if not (M.HEIGHT_STOP[0] <= scaled <= M.HEIGHT_STOP[1]):
        raise Fail("scaled height %.3f m is outside the hard stop %.2f-%.2f m" % (scaled, M.HEIGHT_STOP[0], M.HEIGHT_STOP[1]))
    report = {"skeleton_scale": round(M.EYE_STAND / (eye_z - floor), 4), "table": table, "source_symmetry_error_m": round(src_err, 5), "eye_rel": round(eye_z - floor, 4)}
    os.makedirs(o["outdir"], exist_ok=True)
    meta = {n: {"p": d["p"], "h": d["h"].tolist(), "t": d["t"].tolist(), "m": np.vstack([np.column_stack([d["R"], d["h"]]), [0, 0, 0, 1]]).tolist()} for n, d in out.items()}
    with open(os.path.join(o["outdir"], "skeleton_human.json"), "w") as f:
        json.dump(meta, f, indent=1)
    if o["dry"]:
        print("SKELETON OK (dry, nothing exported)")
        return
    bpy.data.objects.remove(garm, do_unlink=True)  # the template armature holds the name CharacterArmature
    arm = make_armature(out, order)
    path = os.path.join(o["outdir"], "Skeleton_Human.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
    floor = roundtrip_floor(o["game"], o["outdir"])
    print("ROUND-TRIP FLOOR of the untouched template (Blender FBX export then import): %.4f degrees; tolerance floor + 0.01 = %.4f" % (floor, floor + 0.01))
    chk = verify(path, out, game, floor + 0.01)
    chk["roundtrip_floor_deg"] = round(floor, 5)
    print("EXPORT Skeleton_Human raw hash / LFS-pointer hash:", git_hashes(path), chk)
    chk["hashes"] = git_hashes(path)
    with open(os.path.join(o["outdir"], "skeleton_report.json"), "w") as f:
        json.dump({**report, "verify": chk}, f, indent=1)
    print("SKELETON OK")


def R_depth(game, n):
    d = 0
    while game[n]["p"]:
        n, d = game[n]["p"], d + 1
    return d


def make_armature(out, order):
    data = bpy.data.armatures.new("CharacterArmature")
    obj = bpy.data.objects.new("CharacterArmature", data)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode="EDIT")
    ebs = {}
    for n in order:
        d = out[n]
        eb = data.edit_bones.new(n)
        eb.head = tuple(d["h"])
        eb.tail = tuple(d["h"] + np.array([0.0, 0.1, 0.0]))
        length = float(np.linalg.norm(d["t"] - d["h"]))
        eb.matrix = Matrix([list(r) for r in np.vstack([np.column_stack([d["R"], d["h"]]), [0, 0, 0, 1]])])
        eb.length = max(length, 1e-4)
        if d["p"]:
            eb.parent = ebs[d["p"]]
        ebs[n] = eb
    bpy.ops.object.mode_set(mode="OBJECT")
    return obj


def roundtrip_floor(template_path, outdir):
    """Re-export the untouched template armature through the same exporter and measure how far Blender's own round trip moves the bone axes."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=template_path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    for o in [o for o in bpy.data.objects if o.type == "MESH"]:
        bpy.data.objects.remove(o, do_unlink=True)
    before = {b.name: np.array(b.matrix_local) for b in arm.data.bones}
    tmp = os.path.join(outdir, "_template_roundtrip.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=tmp, use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False, bake_anim=False, path_mode="STRIP")
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=tmp)
    arm2 = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    worst = 0.0
    for b in arm2.data.bones:
        cc = (np.trace(np.array(b.matrix_local)[:3, :3].T @ before[b.name][:3, :3]) - 1) / 2
        worst = max(worst, float(np.degrees(np.arccos(np.clip(cc, -1, 1)))))
    os.remove(tmp)
    return worst


def verify(path, out, game, rot_tol):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arms = [a for a in bpy.data.objects if a.type == "ARMATURE"]
    if len(arms) != 1 or arms[0].name != "CharacterArmature":
        raise Fail("armature node is %s" % [a.name for a in arms])
    bones = {b.name: b for b in arms[0].data.bones}
    if set(bones) != set(game):
        raise Fail("bone set differs from the template")
    wt, wr = 0.0, 0.0
    for n, b in bones.items():
        if (b.parent.name if b.parent else None) != game[n]["p"]:
            raise Fail("hierarchy differs at %s" % n)
        m = np.array(b.matrix_local)
        wt = max(wt, float(np.abs(m[:3, 3] - out[n]["h"]).max()))
        cc = (np.trace(m[:3, :3].T @ game[n]["m"][:3, :3]) - 1) / 2
        wr = max(wr, float(np.degrees(np.arccos(np.clip(cc, -1, 1)))))
    sym = 0.0
    for n in bones:
        if n.endswith(".L") and n[:-2] + ".R" in bones:
            l = np.array(bones[n].head_local)
            r = np.array(bones[n[:-2] + ".R"].head_local)
            sym = max(sym, float(np.abs(l - mirror(r)).max()))
    if wt > 1e-4 or wr > rot_tol or sym > SYM_TOL_M:
        raise Fail("verify: head %.6f m, rotation %.4f deg, symmetry %.5f m" % (wt, wr, sym))
    return {"bones": len(bones), "head_max_m": round(wt, 7), "rot_max_deg": round(wr, 5), "symmetry_max_m": round(sym, 6)}


if __name__ == "__main__":
    try:
        main()
    except Exception as e:  # noqa: BLE001  (a Fail is a refused check; anything else is a crash: both leave nothing behind and exit nonzero)
        print("SKELETON FAIL:" if isinstance(e, Fail) else "SKELETON CRASH:", repr(e))
        od = R.argv()["outdir"]
        for f in ("Skeleton_Human.fbx", "skeleton_human.json", "skeleton_report.json", "_template_roundtrip.fbx"):
            if od and os.path.exists(os.path.join(od, f)):
                os.remove(os.path.join(od, f))
        sys.exit(2 if isinstance(e, Fail) else 3)
