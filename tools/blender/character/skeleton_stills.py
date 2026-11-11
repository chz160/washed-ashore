"""Stills for the Skeleton_Human look check (pod 49, director D1): his mesh bound by rotation only to Skeleton_Human, in T and in a set of bends.

blender -b -noaudio -P skeleton_stills.py -- --src <source/Meshy_Survivor_char.fbx> --game <tools/blender/character/source/Farmer_Body_template.fbx> --tex <base colour png> --skel <source/skeleton_human.json> --outdir <dir>

Workbench renders (texture colour, neutral grey background; scene light comes later, in game). Writes one PNG per view plus sheet_*.png.
"""
import json
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_skeleton_human as K  # noqa: E402
import reskin_map as M  # noqa: E402
import reskin_io as R  # noqa: E402
from reskin_fingers import straighten  # noqa: E402
from reskin_fit import repose, watch_vertices  # noqa: E402
from reskin_split import Fail  # noqa: E402
from reskin_tex import luma_at  # noqa: E402


def body(o):
    """His mesh after the rotation-only repose and finger straightening, with weights onto the game bone names. Returns arrays."""
    garm, game = R.load_game(o["game"])
    mesh, V, N, W, names, mb = R.load_meshy(o["src"])
    me = mesh.data
    uv_v, lu = R.mesh_uvs(me)
    lum = luma_at(o["tex"], uv_v)
    floor = float(V[:, 2].min())
    hip = (mb[M.P + "LeftUpLeg"]["h"][:2] + mb[M.P + "RightUpLeg"]["h"][:2]) / 2
    ghip = (game["UpperLeg.L"]["h"][:2] + game["UpperLeg.R"]["h"][:2]) / 2
    c = np.array([0.0, hip[1], floor])
    g = np.array([0.0, ghip[1], 0.0])
    V2, N2, head2, RAK = repose(V, N, W, names, mb, game, {}, (1.0, c, g), 0.0, M.LIMB_TARGET_P4)
    c[2] = float(V2[:, 2].min())
    V3 = V2 - c + g
    weld = np.unique(np.round(V * 1e5).astype(np.int64), axis=0, return_inverse=True)[1].ravel()
    edges = np.array([[e.vertices[0], e.vertices[1]] for e in me.edges])
    G, gi, gnames = R.build_weights(W, names, game, dict(M.MESHY_TO_GAME))
    N3 = N2
    for side, hn in (("L", "mixamorig:LeftHand"), ("R", "mixamorig:RightHand")):
        hh = head2[hn] - c + g
        res, info = straighten(V3, N3, W[:, names.index(hn)], hh, game, edges, side, M.PALM_FRACTION, weld)
        if res is None:
            raise Fail("finger groups not found on the %s hand" % side)
        V3, N3, wts = res
        ws = G[:, gi["Wrist." + side]].copy()
        moved = np.zeros(len(V3))
        for bn, wv in wts.items():
            G[:, gi[bn]] += ws * wv
            moved += wv
        G[:, gi["Wrist." + side]] = ws * (1 - np.clip(moved, 0, 1))
    G = R.clean(G)
    wid = watch_vertices(V, lum, mb["mixamorig:RightHand"]["h"])
    cen = V3[wid].mean(0)
    G[wid] = 0.0
    G[wid, gi["LowerArm.R"]] = 1.0  # rigid watch: one bone at weight 1.0
    polys = [tuple(p.vertices) for p in me.polygons]
    return {"V": V3, "N": N3, "G": G, "gnames": gnames, "polys": polys, "uv": lu, "watch": wid}


def scene(o, b):
    sk = json.load(open(o["skel"]))
    out = {n: {"p": d["p"], "h": np.array(d["h"]), "t": np.array(d["t"]), "R": np.array(d["m"])[:3, :3]} for n, d in sk.items()}
    order = sorted(out, key=lambda n: K.R_depth(out, n))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    arm = K.make_armature(out, order)
    me = bpy.data.meshes.new("Survivor")
    me.from_pydata([tuple(v) for v in b["V"]], [], b["polys"])
    me.polygons.foreach_set("use_smooth", [True] * len(me.polygons))
    layer = me.uv_layers.new(name="UVMap")
    layer.data.foreach_set("uv", b["uv"].ravel())
    try:
        if b.get("LN") is not None:
            me.normals_split_custom_set([tuple(n) for n in b["LN"]])
        else:
            me.normals_split_custom_set_from_vertices([tuple(n) for n in b["N"]])
    except Exception as e:  # noqa: BLE001
        print("NOTE custom normals not set:", e)
    mat = bpy.data.materials.new("Survivor")
    mat.use_nodes = True
    img = bpy.data.images.load(o["tex"])
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = img
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    me.materials.append(mat)
    obj = bpy.data.objects.new("Survivor", me)
    bpy.context.scene.collection.objects.link(obj)
    for gi, gname in enumerate(b["gnames"]):
        col = np.where(b["G"][:, gi] > 0)[0]
        if len(col) == 0:
            continue
        vg = obj.vertex_groups.new(name=gname)
        for k in col:
            vg.add([int(k)], float(b["G"][k, gi]), "REPLACE")
    obj.parent = arm
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light = "STUDIO"
    sh.color_type = "TEXTURE"
    sh.show_shadows = False
    world = bpy.data.worlds.new("W")
    world.color = (0.42, 0.42, 0.44)
    sc.world = world
    sc.render.film_transparent = False
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.type = "ORTHO"
    sc.collection.objects.link(cam)
    sc.camera = cam
    return arm, cam


def reset(arm):
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix()
    bpy.context.view_layer.update()


def rot(arm, bone, axis, deg):
    """Rotate a posed bone about its own head by deg around a WORLD axis (armature space)."""
    pb = arm.pose.bones[bone]
    cur = pb.matrix.copy()
    h = cur.translation.copy()
    pb.matrix = Matrix.Translation(h) @ Matrix.Rotation(np.radians(deg), 4, axis) @ Matrix.Translation(-h) @ cur
    bpy.context.view_layer.update()


def follow_ankle(arm, side):
    """The IK foot is not a child of the shin: put it on the shin's end, as the clips' IK would."""
    sh = arm.pose.bones["LowerLeg." + side]
    pb = arm.pose.bones["Foot." + side]
    tail = sh.tail.copy()
    cur = pb.matrix.copy()
    cur.translation = tail
    pb.matrix = cur
    bpy.context.view_layer.update()


def shoot(o, cam, name, loc, rot_deg, scale, size=(700, 950), look=None):
    cam.location = loc
    cam.rotation_euler = tuple(np.radians(rot_deg))
    cam.data.ortho_scale = scale
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.filepath = os.path.join(o["outdir"], name + ".png")
    bpy.ops.render.render(write_still=True)
    return sc.render.filepath


def sheet(paths, out, cols):
    imgs = []
    for p in paths:
        im = bpy.data.images.load(p)
        w, h = im.size
        a = np.empty(w * h * im.channels, dtype=np.float32)
        im.pixels.foreach_get(a)
        imgs.append(a.reshape(h, w, im.channels)[:, :, :4])
        bpy.data.images.remove(im)
    h, w = max(i.shape[0] for i in imgs), max(i.shape[1] for i in imgs)
    rows = (len(imgs) + cols - 1) // cols
    big = np.ones((rows * h, cols * w, 4), dtype=np.float32)
    for k, im in enumerate(imgs):
        r, c = divmod(k, cols)
        y1 = (rows - r) * h
        big[y1 - im.shape[0]:y1, c * w:c * w + im.shape[1]] = im
    out_img = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
    out_img.pixels.foreach_set(big.ravel())
    out_img.filepath_raw = out
    out_img.file_format = "PNG"
    out_img.save()


def look(cam, loc, target):
    cam.location = Vector(loc)
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()


def shoot_at(o, cam, name, loc, target, scale, size=(700, 700)):
    look(cam, loc, target)
    cam.data.ortho_scale = scale
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.filepath = os.path.join(o["outdir"], name + ".png")
    bpy.ops.render.render(write_still=True)
    return sc.render.filepath


def flex_thumb(arm, side, degs=(40, 45, 50)):
    """Curl the thumb chain toward the palm (palm down in the T bind, so toward -Z), each bone about its own current axis."""
    for i, deg in enumerate(degs):
        pb = arm.pose.bones["Thumb%d.%s" % (i + 1, side)]
        d = (pb.tail - pb.head).normalized()
        axis = d.cross(Vector((0, 0, -1))).normalized()
        rot(arm, pb.name, axis, deg)


def main():
    o = R.argv()
    os.makedirs(o["outdir"], exist_ok=True)
    b = body(o)
    arm, cam = scene(o, b)
    full = {"size": (700, 950), "scale": 2.15}
    zc = 0.95
    t = []
    t.append(shoot(o, cam, "T_front", (0, -5, zc), (90, 0, 0), 2.15))
    t.append(shoot(o, cam, "T_side", (5, 0, zc), (90, 0, 90), 2.15))
    t.append(shoot(o, cam, "T_back", (0, 5, zc), (90, 0, 180), 2.15))
    sheet(t, os.path.join(o["outdir"], "sheet_T.png"), 3)
    # elbows about 90 degrees, seen from above
    reset(arm)
    rot(arm, "LowerArm.L", (0, 0, 1), -90)
    rot(arm, "LowerArm.R", (0, 0, 1), 90)
    e = [shoot(o, cam, "elbow90_top", (0, 0, 5), (0, 0, 0), 1.9, (900, 700)), shoot(o, cam, "elbow90_front", (0, -5, 1.2), (90, 0, 0), 1.9, (900, 700))]
    # wrist flex on the RIGHT wrist (the watch): up and down 50 degrees
    w = []
    for nm, deg in (("up", 50), ("down", -50)):
        reset(arm)
        rot(arm, "Wrist.R", (0, 1, 0), deg)
        w.append(shoot(o, cam, "wristR_%s" % nm, (-0.78, -5, 1.45), (90, 0, 0), 0.7, (700, 700)))
    reset(arm)
    w.append(shoot(o, cam, "wristR_neutral", (-0.78, -5, 1.45), (90, 0, 0), 0.7, (700, 700)))
    sheet(w, os.path.join(o["outdir"], "sheet_wrist.png"), 3)
    # knees 30 and 90, left leg, seen from his left side
    k = []
    for deg in (30, 90):
        reset(arm)
        rot(arm, "LowerLeg.L", (1, 0, 0), deg)
        follow_ankle(arm, "L")
        k.append(shoot(o, cam, "knee%d_side" % deg, (5, 0, 0.55), (90, 0, 90), 1.3, (700, 800)))
    # ankle flex with the boot: toes up 25, toes down 35
    a = []
    for nm, deg in (("up25", 25), ("down35", -35)):
        reset(arm)
        rot(arm, "Foot.L", (1, 0, 0), deg)
        a.append(shoot(o, cam, "ankle_%s_side" % nm, (5, 0, 0.2), (90, 0, 90), 0.6, (700, 700)))
    sheet(e + k + a, os.path.join(o["outdir"], "sheet_bends.png"), 3)
    # arms raised overhead (the shoulder bones keep the template rotation while his clavicle is horizontal)
    reset(arm)
    rot(arm, "UpperArm.L", (0, 1, 0), -100)
    rot(arm, "UpperArm.R", (0, 1, 0), 100)
    up = [shoot(o, cam, "arms_raised_front", (0, -5, 1.1), (90, 0, 0), 2.6, (800, 950)), shoot(o, cam, "arms_raised_side", (5, 0, 1.1), (90, 0, 90), 2.6, (800, 950))]
    sheet(up, os.path.join(o["outdir"], "sheet_raised.png"), 2)
    # thumbs at rest and bent: RIGHT hand (the watch) from three quarters and from the fingertip side, LEFT hand from three quarters
    th = []
    for side, tgt, cams in (("R", (-0.85, -0.05, 1.45), [(-1.5, -1.9, 1.95), (-3.5, -0.05, 1.5)]), ("L", (0.85, -0.05, 1.45), [(1.5, -1.9, 1.95)])):
        for ci, cl in enumerate(cams):
            for state in ("rest", "bent"):
                reset(arm)
                if state == "bent":
                    flex_thumb(arm, side)
                th.append(shoot_at(o, cam, "thumb%s_%d_%s" % (side, ci, state), cl, tgt, 0.5))
    sheet(th, os.path.join(o["outdir"], "sheet_thumb.png"), 2)
    with open(os.path.join(o["outdir"], "sizes.txt"), "w") as f:
        f.write("Every still shows Skeleton_Human at TRUE size (skeletonScale 1.0). In game the Survivor appearance scales it by 1.0266 (director-approved eye match).\n")
    print("STILLS OK", o["outdir"])


if __name__ == "__main__":
    try:
        main()
    except Fail as e:
        print("STILLS FAIL:", e)
        sys.exit(2)
