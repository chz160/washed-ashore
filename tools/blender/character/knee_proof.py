"""K11/K12 proof and the fair knee and ankle stills (pod 49; td-knee-corrective.md Update 4, director stills11 reply).

blender -b -noaudio -P knee_proof.py -- --src --game --skel --tex --outdir [--force-fail shin_to_upper | boot_to_lower]

ONE evaluated mesh per case: the Blender armature evaluation (pose bones plus shape keys) is read back once; the K11 (shin rigid below the corrective band) and K12
(boot rigid to Foot, RMS) checks, the printed pose and the vertex-position hash all come from that array, and the stills are rendered from that same state.
A diagnostic image draws the posed UpperLeg, LowerLeg and Foot bones over the mesh (alpha composite of two renders, same camera, same state).
"""
import hashlib
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_checks as C  # noqa: E402
import reskin_io as R  # noqa: E402
import skeleton_stills as S  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402
from reskin_split import Fail  # noqa: E402

K11_LIMIT, K12_LIMIT = 0.010, 0.002   # m: shin slab centre vs a rigid shin; boot RMS vs Foot
ANKLE_DORSI, ANKLE_PLANTAR = 37.5, -32.5   # degrees from the bind, the pack range td-49 read (Run_Left L f13 and Run R f5)


def evaluate(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = ev.to_mesh()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    ev.to_mesh_clear()
    return co.reshape(-1, 3)


def bone_world(arm, name):
    pb = arm.pose.bones[name]
    return np.array(pb.head), np.array(pb.tail), np.array(pb.matrix)


def set_foot(arm, side, tail, deg):
    """The IK foot: translation to the shin's end, rotated by deg about the world X axis from its bind orientation."""
    pb = arm.pose.bones["Foot." + side]
    rest = pb.bone.matrix_local.to_3x3().to_4x4()
    m = Matrix.Translation(Vector(tail)) @ Matrix.Rotation(np.radians(deg), 4, "X") @ rest
    pb.matrix = m
    bpy.context.view_layer.update()


def make_bone_objects(arm):
    objs = []
    for nm, col in (("UpperLeg.L", (0.1, 0.8, 0.1, 1)), ("LowerLeg.L", (0.9, 0.1, 0.1, 1)), ("Foot.L", (0.1, 0.3, 1.0, 1))):
        h, t, _ = bone_world(arm, nm)
        L = float(np.linalg.norm(t - h))
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.008, radius2=0.008, depth=L)
        me = bpy.data.meshes.new("b_" + nm)
        bm.to_mesh(me)
        bm.free()
        ob = bpy.data.objects.new("b_" + nm, me)
        bpy.context.scene.collection.objects.link(ob)
        d = Vector(t - h).normalized()
        q = Vector((0, 0, 1)).rotation_difference(d)
        ob.matrix_world = Matrix.Translation(Vector((h + t) / 2)) @ q.to_matrix().to_4x4()
        mat = bpy.data.materials.new("m_" + nm)
        mat.diffuse_color = col
        ob.data.materials.append(mat)
        ob.hide_render = True
        objs.append(ob)
    return objs


def px(path):
    im = bpy.data.images.load(path)
    w, h = im.size
    a = np.empty(w * h * im.channels, dtype=np.float32)
    im.pixels.foreach_get(a)
    return a.reshape(h, w, im.channels)


def composite(mesh_png, bones_png, out):
    A, B = px(mesh_png), px(bones_png)
    al = B[:, :, 3:4]
    big = A.copy()
    big[:, :, :3] = A[:, :, :3] * (1 - al) + B[:, :, :3] * al
    im = bpy.data.images.new("c", A.shape[1], A.shape[0], alpha=True)
    im.pixels.foreach_set(big.ravel())
    im.filepath_raw = out
    im.file_format = "PNG"
    im.save()


def main():
    o = R.argv()
    os.makedirs(o["outdir"], exist_ok=True)
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    b["uv"] = b["loop_uv"]
    G = b["G"]
    gi = {n: k for k, n in enumerate(b["gnames"])}
    V = b["V"]
    j = skel["LowerLeg.L"]["h"]
    a0 = skel["UpperLeg.L"]["h"]
    ax = (j - a0) / np.linalg.norm(j - a0)
    dd = (V - j) @ ax
    perp = np.linalg.norm((V - j) - np.outer(dd, ax), axis=1)
    leg = (perp < 0.12) & (V[:, 0] > 0)
    collar = b["collars"]["L"]
    if o.get("force_fail") == "shin_to_upper":   # mid-shin weights partly moved to UpperLeg: K11 must go red
        m = leg & (dd > 0.10) & (dd < 0.22)
        G[m, gi["UpperLeg.L"]] = 0.3
        G[m, gi["LowerLeg.L"]] = 0.7
    if o.get("force_fail") == "boot_to_lower":   # boot weights partly moved to LowerLeg: K12 must go red
        m = leg & (V[:, 2] < collar - 0.03) & (V[:, 2] > 0.0)
        G[m, gi["LowerLeg.L"]] = 0.3
        G[m, gi["Foot.L"]] = 0.7
    shapes = {}
    for side in ("L", "R"):
        pl = C.cross_ratio(V, G, b["gnames"], skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0))["planes"]
        dl, aft = C.corrective_refined(V, G, b["gnames"], skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0), pl)
        shapes["Knee_%s_90" % side] = dl
        if max(aft["planes"].values()) > 1.10:
            raise Fail("Knee_%s_90 over-inflates: a band plane above 1.10 (%s)" % (side, aft["planes"]))
    hv = sum(b["W"][:, b["names"].index(n)] for n in ("mixamorig:Head", "mixamorig:HeadTop_End", "headfront") if n in b["names"]) >= 0.5
    arm, cam = S.scene({"skel": o["skel"], "tex": o["tex"]}, b)
    ob = bpy.data.objects["Survivor"]
    ob.shape_key_add(name="Basis", from_mix=False)
    keys = {}
    for k, dl in shapes.items():
        sk = ob.shape_key_add(name=k, from_mix=False)
        for i, v in enumerate(V):
            sk.data[i].co = (v + dl[i]).tolist()
        keys[k] = sk
    ankle_d = float((skel["Foot.L"]["h"] - j) @ ax)
    rest_foot = skel["Foot.L"]["h"]
    report = []

    def case(label, knee_deg, foot_deg, shape_on, shots, with_bones=False, ankle_only=None):
        S.reset(arm)
        for k, sk in keys.items():
            sk.value = 1.0 if (shape_on and k == "Knee_L_90") else 0.0
        if ankle_only is None:
            S.rot(arm, "LowerLeg.L", (1, 0, 0), knee_deg)
            tail = np.array(arm.pose.bones["LowerLeg.L"].tail)
            set_foot(arm, "L", tail, foot_deg)
        else:
            set_foot(arm, "L", rest_foot, ankle_only)
        bpy.context.view_layer.update()
        ev = evaluate(ob)
        h = hashlib.sha1(np.round(ev, 5).tobytes()).hexdigest()[:12]
        _, _, WLL = bone_world(arm, "LowerLeg.L")
        _, _, WF = bone_world(arm, "Foot.L")
        rLL = WLL @ np.linalg.inv(arm.pose.bones["LowerLeg.L"].bone.matrix_local)
        rF = WF @ np.linalg.inv(arm.pose.bones["Foot.L"].bone.matrix_local)
        devs = []
        for off in np.arange(0.10, ankle_d - 0.09, 0.02):   # below the corrective band (the shape reaches 10 cm below the knee)
            m = leg & (np.abs(dd - off) < 0.01)
            if m.sum() >= 6:
                devs.append(float(np.linalg.norm(ev[m].mean(0) - (rLL @ np.append(V[m].mean(0), 1.0))[:3])))
        k11 = max(devs) if devs else 0.0
        boot = leg & (V[:, 2] < collar - 0.03) & (V[:, 2] > 0.0)
        k12 = float(np.sqrt(((ev[boot] - (np.c_[V[boot], np.ones(int(boot.sum()))] @ rF.T)[:, :3]) ** 2).sum(1).mean())) if boot.any() else 0.0
        pose = {"knee_flexion_deg": knee_deg if ankle_only is None else 0, "foot_rotation_deg": foot_deg if ankle_only is None else ankle_only,
                "ik_foot_target": [round(float(x), 4) for x in np.array(arm.pose.bones["Foot.L"].head)], "shape_weight": 1.0 if shape_on else 0.0}
        print("CASE %-34s pose %s | evaluated-mesh hash %s | K11 shin %.4f m (limit %.3f) | K12 boot %.5f m (limit %.3f)" % (label, pose, h, k11, K11_LIMIT, k12, K12_LIMIT))
        report.append((label, k11, k12))
        for sname, loc, tgt, scale, size in shots:
            mesh_png = S.shoot_at(o, cam, "%s_%s" % (label, sname), loc, tgt, scale, size)
            if with_bones:
                for ob2 in bpy.data.objects:
                    if ob2.name.startswith("b_"):
                        bpy.data.objects.remove(ob2, do_unlink=True)
                bobjs = make_bone_objects(arm)
                ob.hide_render = True
                for x in bobjs:
                    x.hide_render = False
                sc = bpy.context.scene
                sc.display.shading.color_type = "MATERIAL"
                sc.render.film_transparent = True
                bpng = S.shoot_at(o, cam, "%s_%s_bones" % (label, sname), loc, tgt, scale, size)
                sc.render.film_transparent = False
                sc.display.shading.color_type = "TEXTURE"
                ob.hide_render = False
                for x in bobjs:
                    x.hide_render = True
                composite(mesh_png, bpng, os.path.join(o["outdir"], "%s_%s_overlay.png" % (label, sname)))
        return k11, k12

    side_shot = ("side", (5, 0, 0.45), (0, 0, 0.45), 1.3, (700, 800))
    tq_shot = ("tq", (3.0, -3.5, 0.9), (0, 0, 0.45), 1.3, (700, 800))
    ank_shot = ("side", (5, 0, 0.2), (0, 0, 0.2), 0.6, (700, 700))
    results = []
    results.append(case("knee90_footfollows_noshape", 90, 90, False, [side_shot, tq_shot], with_bones=True))
    results.append(case("knee90_footfollows_shape", 90, 90, True, [side_shot, tq_shot], with_bones=True))
    results.append(case("knee90_upright_noshape", 90, 0, False, [side_shot], with_bones=True))
    results.append(case("knee90_upright_shape", 90, 0, True, [side_shot], with_bones=True))
    results.append(case("knee45_footflat_shape", 45, 0, True, [side_shot, tq_shot]))
    results.append(case("ankle_dorsi", 0, 0, False, [ank_shot], ankle_only=ANKLE_DORSI))
    results.append(case("ankle_plantar", 0, 0, False, [ank_shot], ankle_only=ANKLE_PLANTAR))
    # K11 and K12 are fail-closed on the foot-follows cases and the knee-straight ankle cases (the upright foot is the unfair pose, shown for the diagnostic only)
    gated = [r for r in report if "upright" not in r[0]]
    bad = [(l, k11, k12) for l, k11, k12 in gated if k11 > K11_LIMIT or k12 > K12_LIMIT]
    if bad:
        raise Fail("K11/K12 red: %s" % bad)
    print("PROOF OK: K11 and K12 green on %d evaluated meshes" % len(gated))


if __name__ == "__main__":
    try:
        main()
    except Fail as e:
        print("PROOF FAIL:", e)
        od = R.argv().get("outdir")
        for f in (os.listdir(od) if od and os.path.isdir(od) else []):   # fail closed: the stills of a red run are not left behind
            if f.endswith(".png"):
                os.remove(os.path.join(od, f))
        sys.exit(2)
