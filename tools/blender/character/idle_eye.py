"""His eye height in a retargeted pose (pod 49): the final skeletonScale = 1.70 / eye (td ruling 4).
blender -b -noaudio -P idle_eye.py -- --src <source/Meshy_Survivor_char.fbx> --game <tools/blender/character/source/Farmer_Body_template.fbx> --skel <source/skeleton_human.json> --tex <png> --clips <retargeted fbx> --outdir <dir>
Skins the shipped weights (no shapes) with the pose bones of each action and takes the median z of the same bright face texels measure_eye uses."""
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_io as R  # noqa: E402
import reskin_map as M  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402


def main():
    o = R.argv()
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    b = fit_body(o, game, skel)
    V3, G, gn, W, names, lum = b["V"], b["G"], b["gnames"], b["W"], b["names"], b["lum"]
    hw = sum(W[:, names.index(n)] for n in M.HEAD_BONES if n in names)
    head = np.where(hw >= 0.5)[0]
    ym = V3[head, 1]
    face = (ym < np.percentile(ym, 30)) & (np.abs(V3[head, 0]) > 0.02) & (np.abs(V3[head, 0]) < 0.05)
    zs = V3[head, 2]
    sel = head[face & (lum[head] > 0.55) & (zs > 1.55) & (zs < 1.75)]
    print("EYE texels (rest pose, bind): %d, median z %.4f, height top %.4f" % (len(sel), float(np.median(V3[sel, 2])), float(V3[:, 2].max())))
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=o["clips"])
    arm = next(ob for k, ob in bpy.data.objects.items() if k not in before and ob.type == "ARMATURE")
    if not np.allclose(np.array(arm.matrix_world), np.eye(4), atol=1e-5):
        print("NOTE the clip armature object is not at identity:", np.array(arm.matrix_world).round(3).tolist())
    rest = {bn.name: np.array(bn.matrix_local) for bn in arm.data.bones}
    gi = {n: k for k, n in enumerate(gn)}
    Vh = np.c_[V3[sel], np.ones(len(sel))]
    arm.animation_data_create()
    top = float(V3[:, 2].max())
    for act in sorted(bpy.data.actions, key=lambda a: a.name):
        arm.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        ez = []
        for f in range(f0, f1 + 1):
            bpy.context.scene.frame_set(f)
            out = np.zeros((len(sel), 3))
            for n, pb in arm.pose.bones.items():
                k = gi.get(n)
                if k is None:
                    continue
                w = G[sel, k]
                if not (w > 1e-6).any():
                    continue
                Wm = np.array(pb.matrix) @ np.linalg.inv(rest[n])
                out += w[:, None] * (Vh @ Wm.T)[:, :3]
            ez.append(float(np.median(out[:, 2])))
        ez = np.array(ez)
        print("IDLE-EYE %-45s frames %d: eye z min %.4f mean %.4f max %.4f -> skeletonScale (mean) %.4f; height %.3f -> %.3f m" % (
            act.name, len(ez), ez.min(), ez.mean(), ez.max(), M.EYE_STAND / ez.mean(), top, top * M.EYE_STAND / ez.mean()))


if __name__ == "__main__":
    main()
