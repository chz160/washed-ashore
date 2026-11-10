"""Side-shadow frame sequences of named clips on the Survivor (pod 49): every key frame of one cycle, same camera and light as clip_stills.py.
blender -b -noaudio -P clip_motion.py -- --src --game --skel --tex --outdir --clips <authored fbx> --names Jog_Full,Sprint_Full,Jog,Sprint
Writes <outdir>/<clip>/f<frame>.png (frame numbers as in the FBX); encode and label with label_motion.py.
Optional: --cam "x,y,z,tx,ty,tz,ortho_scale" for another view (default side, 6,0,0.9 -> 0,0,0.9, 2.2); --frames 1,11,21 for chosen frames only; --size 500,800."""
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_io as R  # noqa: E402
import skeleton_stills as S  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402
from standidle_stills import copy_pose  # noqa: E402


def main():
    o = R.argv()
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    b["uv"] = b["loop_uv"]
    arm, cam = S.scene({"skel": o["skel"], "tex": o["tex"]}, b)
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=o["clips"])
    src = next(x for k, x in bpy.data.objects.items() if k not in before and x.type == "ARMATURE")
    src.animation_data_create()
    sc = bpy.context.scene
    if "textured" in o:   # --textured 1: lit and textured instead of the shadow silhouette
        sc.display.shading.light = "STUDIO"
        sc.display.shading.color_type = "TEXTURE"
        sc.world.color = (0.42, 0.42, 0.44)
    else:
        sc.display.shading.light = "FLAT"
        sc.display.shading.color_type = "SINGLE"
        sc.display.shading.single_color = (0.04, 0.04, 0.04)
        sc.world.color = (0.85, 0.85, 0.85)
    for name in o["names"].split(","):
        act = next(a for a in bpy.data.actions if a.name.split("|")[-1].split(".")[0] == name)
        src.animation_data.action = act
        f0, f1 = (int(x) for x in act.frame_range)
        d = os.path.join(o["outdir"], name)
        os.makedirs(d, exist_ok=True)
        frames = [int(x) for x in o["frames"].split(",")] if "frames" in o else range(f0, f1)   # the last key repeats the first (loop): one cycle = f0 .. f1-1
        cv = [float(x) for x in o["cam"].split(",")] if "cam" in o else [6, 0, 0.9, 0, 0, 0.9, 2.2]
        size = tuple(int(x) for x in o["size"].split(",")) if "size" in o else (500, 800)
        for fr in frames:
            copy_pose(arm, src, fr)
            S.shoot_at({**o, "outdir": d}, cam, "f%03d" % fr, tuple(cv[:3]), tuple(cv[3:6]), cv[6], size)
        print("MOTION", name, f0, f1 - 1)
    print("MOTION OK")


if __name__ == "__main__":
    main()
