"""Stand_Idle head-pose comparison stills for the director (pod 49): the same frame, same light, three head poses.
blender -b -noaudio -P standidle_stills.py -- --src --game --skel --tex --outdir --clips h0.fbx,h-2.fbx,h-4.fbx --labels 0,2,4
For each authored Stand_Idle FBX the pose bones are copied onto Skeleton_Human (armature space), the Survivor mesh deforms through its own weights, and a shadow silhouette
(front and side) and a textured head-and-shoulders profile are rendered at frame 1 and at the loop's two head-nod extremes (frames 74 and 314)."""
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_io as R  # noqa: E402
import skeleton_stills as S  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402
from reskin_checks import depth  # noqa: E402

FRAMES = (1, 74, 314)


def copy_pose(arm, src, frame):
    bpy.context.scene.frame_set(frame)
    skel = {pb.name: {"p": pb.parent.name if pb.parent else None} for pb in arm.pose.bones}
    order = sorted(skel, key=lambda n: depth(skel, n))
    for n in order:
        if n in src.pose.bones:
            arm.pose.bones[n].matrix = src.pose.bones[n].matrix.copy()
            bpy.context.view_layer.update()


def main():
    o = R.argv()
    os.makedirs(o["outdir"], exist_ok=True)
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    b["uv"] = b["loop_uv"]
    clips = o["clips"].split(",")
    labels = o["labels"].split(",")
    arm, cam = S.scene({"skel": o["skel"], "tex": o["tex"]}, b)
    ob = bpy.data.objects["Survivor"]
    sc = bpy.context.scene
    shots = {}
    for path, lab in zip(clips, labels):
        before = set(bpy.data.objects.keys())
        before_acts = set(bpy.data.actions.keys())
        bpy.ops.import_scene.fbx(filepath=path)
        src = next(x for k, x in bpy.data.objects.items() if k not in before and x.type == "ARMATURE")
        act = next(a for a in bpy.data.actions if a.name not in before_acts and a.name.split("|")[-1].startswith("Stand_Idle"))
        src.animation_data_create()
        src.animation_data.action = act
        for fr in FRAMES:
            copy_pose(arm, src, fr)
            # shadow silhouette, front and side (flat dark on light grey)
            sc.display.shading.light = "FLAT"
            sc.display.shading.color_type = "SINGLE"
            sc.display.shading.single_color = (0.04, 0.04, 0.04)
            sc.world.color = (0.85, 0.85, 0.85)
            if fr == 1:
                shots[(lab, "front")] = S.shoot_at(o, cam, "h%s_f%d_front_shadow" % (lab, fr), (0, -6, 0.95), (0, 0, 0.95), 2.1, (600, 900))
                shots[(lab, "side")] = S.shoot_at(o, cam, "h%s_f%d_side_shadow" % (lab, fr), (6, 0, 0.95), (0, 0, 0.95), 2.1, (600, 900))
            sc.display.shading.light = "STUDIO"
            sc.display.shading.color_type = "TEXTURE"
            sc.world.color = (0.42, 0.42, 0.44)
            if fr == 1:
                shots[(lab, "sidefull")] = S.shoot_at(o, cam, "h%s_f%d_side_textured" % (lab, fr), (6, 0, 0.95), (0, 0, 0.95), 2.1, (600, 900))
            hz = float(np.array(arm.pose.bones["Head"].head)[2])
            shots[(lab, "prof%d" % fr)] = S.shoot_at(o, cam, "h%s_f%d_profile" % (lab, fr), (6, -0.1, hz + 0.05), (0, -0.1, hz + 0.05), 0.75, (600, 600))
        for x in [x for k, x in bpy.data.objects.items() if k not in before]:
            bpy.data.objects.remove(x, do_unlink=True)
    rows = []
    for lab in labels:
        rows += [shots[(lab, "front")], shots[(lab, "side")]] + [shots[(lab, "prof%d" % f)] for f in FRAMES]
    S.sheet(rows, os.path.join(o["outdir"], "sheet_standidle_heads.png"), 5)
    post = []
    for lab in labels:
        post += [shots[(lab, "front")], shots[(lab, "side")], shots[(lab, "sidefull")]] + ([o["ref"]] if o.get("ref") else [])
    S.sheet(post, os.path.join(o["outdir"], "sheet_posture.png"), 4 if o.get("ref") else 3)
    print("STANDIDLE STILLS OK", o["outdir"])


if __name__ == "__main__":
    main()
