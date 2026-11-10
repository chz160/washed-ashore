"""Stills of named clips at named frames on the Survivor (pod 49): side, front three-quarter, shadow silhouette side, all at the same camera and light.
blender -b -noaudio -P clip_stills.py -- --src --game --skel --tex --outdir --clips <authored fbx> --names Squat_Hold,Sit_Loop,... [--frame 30] [--strip 4]
(--strip N: instead of one frame, N frames evenly across each clip's loop, side shadow silhouette only, one row per clip: the D2 motion read)
The pose bones of the clip are copied onto Skeleton_Human (armature space); the Survivor deforms through its weights with the knee shapes ON at the matching knee angle (angle / 90 as a plain linear weight, a stand-in for td-49's driver curve)."""
import math
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_checks as C  # noqa: E402
import reskin_io as R  # noqa: E402
import skeleton_stills as S  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402
from standidle_stills import copy_pose  # noqa: E402


def main():
    o = R.argv()
    os.makedirs(o["outdir"], exist_ok=True)
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    b["uv"] = b["loop_uv"]
    V, G, gn = b["V"], b["G"], b["gnames"]
    shapes = {}
    for side in ("L", "R"):
        pl = C.cross_ratio(V, G, gn, skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0))["planes"]
        shapes[side] = C.corrective_refined(V, G, gn, skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0), pl)[0]
    arm, cam = S.scene({"skel": o["skel"], "tex": o["tex"]}, b)
    ob = bpy.data.objects["Survivor"]
    ob.shape_key_add(name="Basis", from_mix=False)
    keys = {}
    for side in ("L", "R"):
        sk = ob.shape_key_add(name="Knee_%s_90" % side, from_mix=False)
        for i, v in enumerate(V):
            sk.data[i].co = (v + shapes[side][i]).tolist()
        keys[side] = sk
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=o["clips"])
    src = next(x for k, x in bpy.data.objects.items() if k not in before and x.type == "ARMATURE")
    src.animation_data_create()
    sc = bpy.context.scene
    out = []
    for name in o["names"].split(","):
        act = next(a for a in bpy.data.actions if a.name.split("|")[-1] == name)
        src.animation_data.action = act
        if "strip" in o:
            n = int(o["strip"])
            f0, f1 = act.frame_range
            zc = 0.9 if name.startswith(("Stand", "Jog", "Sprint", "Jump")) else 0.7   # camera aim height: standing figure vs lowered stances
            for k in range(n):
                fr = int(round(f0 + (f1 - f0) * k / n))
                copy_pose(arm, src, fr)
                hz = float(np.array(arm.pose.bones["Hips"].head)[2])
                sc.display.shading.light = "FLAT"
                sc.display.shading.color_type = "SINGLE"
                sc.display.shading.single_color = (0.04, 0.04, 0.04)
                sc.world.color = (0.85, 0.85, 0.85)
                out.append(S.shoot_at(o, cam, "%s_strip%d_f%d" % (name, k, fr), (6, 0, zc), (0, 0, zc), 2.2, (500, 800)))
            continue
        fr = int(o.get("frame", 30))
        copy_pose(arm, src, fr)
        # knee shape weight from the knee flexion angle of the posed bones: the angle between UpperLeg and LowerLeg directions
        for side in ("L", "R"):
            u = np.array(arm.pose.bones["UpperLeg." + side].tail) - np.array(arm.pose.bones["UpperLeg." + side].head)
            l = np.array(arm.pose.bones["LowerLeg." + side].tail) - np.array(arm.pose.bones["LowerLeg." + side].head)
            ang = math.degrees(math.acos(max(-1, min(1, float(u @ l / (np.linalg.norm(u) * np.linalg.norm(l)))))))
            keys[side].value = min(ang / 90.0, 1.0)
        print("CLIP %s frame %d knee flexion L %.0f R %.0f degrees, shape weights %.2f %.2f" % (name, fr, keys["L"].value * 90, keys["R"].value * 90, keys["L"].value, keys["R"].value))
        hz = float(np.array(arm.pose.bones["Hips"].head)[2])
        sc.display.shading.light = "STUDIO"
        sc.display.shading.color_type = "TEXTURE"
        sc.world.color = (0.42, 0.42, 0.44)
        out.append(S.shoot_at(o, cam, "%s_f%d_side" % (name, fr), (6, 0, hz), (0, 0, hz), 2.0, (700, 800)))
        out.append(S.shoot_at(o, cam, "%s_f%d_tq" % (name, fr), (3.5, -4.5, hz + 0.4), (0, 0, hz), 2.0, (700, 800)))
        sc.display.shading.light = "FLAT"
        sc.display.shading.color_type = "SINGLE"
        sc.display.shading.single_color = (0.04, 0.04, 0.04)
        sc.world.color = (0.85, 0.85, 0.85)
        out.append(S.shoot_at(o, cam, "%s_f%d_shadow_side" % (name, fr), (6, 0, hz), (0, 0, hz), 2.0, (700, 800)))
    S.sheet(out, os.path.join(o["outdir"], "sheet_strip.png" if "strip" in o else "sheet_clips.png"), int(o["strip"]) if "strip" in o else 3)
    print("CLIP STILLS OK")


if __name__ == "__main__":
    main()
