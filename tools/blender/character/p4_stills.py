"""Stills of the SHIPPED body for the director (pod 49, item 4): hands at rest (solid and wireframe), the right hand and watch at wrist flex, knee 90, a
first-person elbow at 90, and the K2 facet twin against the untouched Meshy mesh. Uses the same arrays the exporter writes (fit_body).

blender -b -noaudio -P p4_stills.py -- --src <source/Meshy_Survivor_char.fbx> --game <tools/blender/character/source/Farmer_Body_template.fbx> --skel <source/skeleton_human.json> --tex <base colour png> --outdir <dir>
Workbench renders, texture colour, neutral grey, STUDIO light (the scene's sun and FOV come later, in game). The first-person camera uses an assumed FOV (70).
"""
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_io as R  # noqa: E402
import skeleton_stills as S  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402
from reskin_split import Fail  # noqa: E402

FP_FOV = 70.0   # ASSUMED; the in-game authored FOV was not read for this still


def shoot_persp(o, cam, name, loc, target, fov, size):
    cam.data.type = "PERSP"
    cam.data.sensor_width = 36.0
    cam.data.lens = 18.0 / np.tan(np.radians(fov) / 2)
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    sc = bpy.context.scene
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.filepath = os.path.join(o["outdir"], name + ".png")
    bpy.ops.render.render(write_still=True)
    cam.data.type = "ORTHO"
    return sc.render.filepath


def wire(on):
    bpy.context.scene.display.shading.type = "WIREFRAME" if on else "SOLID"


def px(path):
    im = bpy.data.images.load(path)
    w, h = im.size
    a = np.empty(w * h * im.channels, dtype=np.float32)
    im.pixels.foreach_get(a)
    pass  # keep the image: removing it broke the second read
    return a.reshape(h, w, im.channels)[:, :, :3]


def do_twin(o, b, arm, cam, suffix):
    """The untouched Meshy mesh against a re-skin built from the same arrays, same camera and light, torso view."""
    S.reset(arm)
    bpy.ops.import_scene.fbx(filepath=o["src"])
    src = next(ob for ob in bpy.data.objects if ob.type == "MESH" and ob.name != "Survivor")
    sarm = src.parent
    mw = np.array(src.matrix_world)
    co = np.empty(len(src.data.vertices) * 3)
    src.data.vertices.foreach_get("co", co)
    Vs = co.reshape(-1, 3) @ mw[:3, :3].T + mw[:3, 3]
    shift = (b["V0"] - Vs).mean(0)
    sarm.location = sarm.location + Vector(shift)
    src.data.materials.clear()
    src.data.materials.append(bpy.data.materials["Survivor"])
    mine = bpy.data.objects["Survivor"]
    shots = {}
    for tag, vis in (("source", src), ("reskin", mine)):
        for ob in (src, mine, sarm, arm):
            ob.hide_render = True
        vis.hide_render = False
        if vis is src:
            sarm.hide_render = False
        shots[tag] = S.shoot_at(o, cam, "twin_%s%s" % (tag, suffix), (0, -5, 1.15), (0, 0, 1.15), 0.7, (700, 700))
    a, c = px(shots["source"]), px(shots["reskin"])
    diff = np.abs(a - c).max(2)
    changed = float((diff > 8 / 255).mean())
    bg = (np.abs(a - a[0, 0]).max(2) > 8 / 255) | (np.abs(c - c[0, 0]).max(2) > 8 / 255)
    in_body = float(((diff > 8 / 255) & bg).sum() / max(bg.sum(), 1))
    img = bpy.data.images.new("diff", 700, 700, alpha=True)
    d4 = np.ones((700, 700, 4), dtype=np.float32)
    d4[:, :, :3] = (diff > 8 / 255)[:, :, None]
    img.pixels.foreach_set(d4.ravel())
    img.filepath_raw = os.path.join(o["outdir"], "twin_diff%s.png" % suffix)
    img.file_format = "PNG"
    img.save()
    print("K2 FACET TWIN%s (torso, same camera and light): changed pixels %.4f of the frame, %.4f of the body pixels (threshold 8/255)" % (suffix, changed, in_body))
    return in_body


def main():
    o = R.argv()
    os.makedirs(o["outdir"], exist_ok=True)
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    b["uv"] = b["loop_uv"]
    arm, cam = S.scene({"skel": o["skel"], "tex": o["tex"]}, b)
    out = []
    # hands at rest: three quarters and from the fingertip side, solid then wireframe (open faces show as holes in the wireframe)
    for side, tgt, cams in (("R", (-0.85, -0.05, 1.47), [(-1.5, -1.9, 1.95), (-3.5, -0.05, 1.5)]), ("L", (0.85, -0.05, 1.47), [(1.5, -1.9, 1.95), (3.5, -0.05, 1.5)])):
        for ci, cl in enumerate(cams):
            for w in (False, True):
                wire(w)
                S.reset(arm)
                out.append(S.shoot_at(o, cam, "hand%s_%d_%s" % (side, ci, "wire" if w else "solid"), cl, tgt, 0.5))
    wire(False)
    S.sheet(out, os.path.join(o["outdir"], "sheet_hands_rest.png"), 4)
    # right hand and watch at the wrist-flex extremes (+-50 until item 3 prints the shipped extremes)
    fl = []
    for nm, deg in (("up", 50), ("down", -50)):
        S.reset(arm)
        S.rot(arm, "Wrist.R", (0, 1, 0), deg)
        fl.append(S.shoot_at(o, cam, "watch_%s_threequarter" % nm, (-1.5, -1.9, 1.95), (-0.8, -0.05, 1.47), 0.5))
        fl.append(S.shoot_at(o, cam, "watch_%s_front" % nm, (-0.78, -5, 1.47), (-0.78, 0, 1.47), 0.5))
        fl.append(S.shoot_at(o, cam, "watch_%s_top" % nm, (-0.78, -0.05, 5), (-0.78, -0.05, 1.47), 0.5))
    S.reset(arm)
    fl.append(S.shoot_at(o, cam, "watch_rest_threequarter", (-1.5, -1.9, 1.95), (-0.8, -0.05, 1.47), 0.5))
    S.sheet(fl, os.path.join(o["outdir"], "sheet_watch.png"), 3)
    # knee 90 (left leg, his left side) and a first-person elbow at 90
    S.reset(arm)
    S.rot(arm, "LowerLeg.L", (1, 0, 0), 90)
    S.follow_ankle(arm, "L")
    k = [S.shoot_at(o, cam, "knee90_side", (5, 0, 0.45), (0, 0, 0.45), 1.3, (700, 800))]
    S.reset(arm)
    for sd, sg in (("L", 1), ("R", -1)):
        S.rot(arm, "UpperArm." + sd, (0, 1, 0), 90 * sg)
        S.rot(arm, "LowerArm." + sd, (1, 0, 0), -90)
    k.append(shoot_persp(o, cam, "elbow90_firstperson", (0.0, -0.08, 1.67), (0.0, -0.55, 1.05), FP_FOV, (960, 540)))
    S.reset(arm)
    for sd in ("L", "R"):   # a bone-posed sitting approximation (NOT the clip): thighs forward, shins down
        S.rot(arm, "UpperLeg." + sd, (1, 0, 0), -90)
    k.append(S.shoot_at(o, cam, "sit_approx_side", (5, 0, 0.8), (0, 0, 0.8), 1.8, (800, 800)))
    S.sheet(k, os.path.join(o["outdir"], "sheet_knee_elbow.png"), 3)
    twin_posed = do_twin(o, b, arm, cam, "")
    # identity twin: the same pipeline without the repose (V0 and the source loop normals), so any difference is the pipeline, not the 7 mm the repose moves the torso
    b2 = dict(b)
    b2["V"], b2["LN"] = b["V0"], b["LN0"]
    arm2, cam2 = S.scene({"skel": o["skel"], "tex": o["tex"]}, b2)
    twin_id = do_twin(o, b2, arm2, cam2, "_identity")
    with open(os.path.join(o["outdir"], "stills_notes.txt"), "w") as f:
        f.write("Stills show the shipped body (mitten hands) at TRUE size. Wrist flex +-50 until item 3 prints the shipped extremes. Sit is a bone-posed approximation, not the clip.\n"
                "First-person elbow: camera at the eye point (z 1.67), FOV %.0f ASSUMED, Workbench studio light (not the scene sun).\n"
                "K2 twin (torso view, changed body-pixel fraction): posed %.4f, identity (no repose) %.4f\n" % (FP_FOV, twin_posed, twin_id))
    print("STILLS OK", o["outdir"])


if __name__ == "__main__":
    try:
        main()
    except Fail as e:
        print("STILLS FAIL:", e)
        sys.exit(2)
