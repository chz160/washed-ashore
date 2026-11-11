"""Read-only check: the Survivor's eye point on the SKINNED mesh against the rigid Head-bone point the clips were authored with (pod 49, td-49 PlayMode eye gap).
blender -b -noaudio -P eye_check.py -- --src <Meshy char.fbx> --game <Farmer_Body_template.fbx> --skel <skeleton_human.json> --tex <png> --clips <authored fbx> --eyejson <eye-point-Survivor.json> --names Stand_Idle:1,Squat_Hold:30,...
For each clip:frame the pose bones of the clip are copied onto Skeleton_Human; the eye vertices (survivor_eye.py's selection) are read after skinning (mean x,y, median z, armature space, unscaled)
and compared with Head-bone world matrix x eye_in_head_bone_local (the point author_clips' rig.eye() uses). Prints both and the difference (up, forward = -Y), and the head pitch."""
import json
import math
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_io as R  # noqa: E402
import reskin_map as M  # noqa: E402
import skeleton_stills as S  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402
from standidle_stills import copy_pose  # noqa: E402


def main():
    o = R.argv()
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    V3, W, names, lum = b["V"], b["W"], b["names"], b["lum"]
    hw = sum(W[:, names.index(n)] for n in M.HEAD_BONES if n in names)
    head = np.where(hw >= 0.5)[0]
    ym = V3[head, 1]
    face = (ym < np.percentile(ym, 30)) & (np.abs(V3[head, 0]) > 0.02) & (np.abs(V3[head, 0]) < 0.05)
    zs = V3[head, 2]
    sel = head[face & (lum[head] > 0.55) & (zs > 1.55) & (zs < 1.75)]
    eye_local = np.array(json.load(open(o["eyejson"]))["Survivor_Head.fbx"]["eye_in_head_bone_local"])
    b["uv"] = b["loop_uv"]
    arm, cam = S.scene({"skel": o["skel"], "tex": o["tex"]}, b)
    ob = bpy.data.objects["Survivor"]
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=o["clips"])
    src = next(x for k, x in bpy.data.objects.items() if k not in before and x.type == "ARMATURE")
    src.animation_data_create()
    for item in o["names"].split(","):
        name, fr = item.split(":")
        act = next(a for a in bpy.data.actions if a.name.split("|")[-1].split(".")[0] == name)
        src.animation_data.action = act
        copy_pose(arm, src, int(fr))
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        P = np.array([me.vertices[int(i)].co[:] for i in sel])
        ev.to_mesh_clear()
        skinned = np.array([P[:, 0].mean(), P[:, 1].mean(), float(np.median(P[:, 2]))])
        hb = arm.pose.bones["Head"]
        Hm = np.array(hb.matrix)
        rigid = (Hm @ np.append(eye_local, 1.0))[:3]
        d = skinned - rigid
        fwd = np.array(hb.matrix.to_3x3() @ __import__("mathutils").Vector((0, 1, 0)))
        pitch = math.degrees(math.atan2(-fwd[1], fwd[2]))   # Head bone axis (up in the T bind) against vertical, forward = -Y
        print("EYECHECK %-16s frame %s skinned (x %.4f, y %.4f, z %.4f) rigid (x %.4f, y %.4f, z %.4f) skinned - rigid: up %.4f m, forward %.4f m, lateral %.4f m; head bone pitch %.1f deg" % (
            name, fr, *skinned, *rigid, d[2], -d[1], d[0], pitch))
    print("EYECHECK OK")


if __name__ == "__main__":
    main()
