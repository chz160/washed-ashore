"""The Survivor's eye point in the Head bone's local frame, in the format measure_head_eye.py writes (author_clips.py --eye-json / --eye-key).
blender -b -noaudio -P survivor_eye.py -- --src <source/Meshy_Survivor_char.fbx> --game <tools/blender/character/source/Farmer_Body_template.fbx> --skel <source/skeleton_human.json> --tex <png> --out <eye-point.json>
Method: the same bright face texels as measure_eye (median z), x and y as the mean of those vertices, in the T bind of Skeleton_Human."""
import json
import os
import sys

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
    V3, W, names, lum = b["V"], b["W"], b["names"], b["lum"]
    hw = sum(W[:, names.index(n)] for n in M.HEAD_BONES if n in names)
    head = np.where(hw >= 0.5)[0]
    ym = V3[head, 1]
    face = (ym < np.percentile(ym, 30)) & (np.abs(V3[head, 0]) > 0.02) & (np.abs(V3[head, 0]) < 0.05)
    zs = V3[head, 2]
    sel = head[face & (lum[head] > 0.55) & (zs > 1.55) & (zs < 1.75)]
    if len(sel) == 0:
        raise SystemExit("survivor_eye: no eye vertices matched (bright face texels at z 1.55 to 1.75 m); check the mesh and texture")
    p = np.array([0.0, float(V3[sel, 1].mean()), float(np.median(V3[sel, 2]))])
    Hm = skel["Head"]["m"]
    local = (np.linalg.inv(Hm) @ np.append(p, 1.0))[:3]
    hh = skel["Head"]["h"]
    res = {"Survivor_Head.fbx": {"eye_in_head_bone_local": [round(float(x), 5) for x in local], "up_m": round(float(p[2] - hh[2]), 4),
                                 "forward_m": round(float(-(p[1] - hh[1])), 4), "lateral_m": round(float(p[0] - hh[0]), 4), "eye_vertices": int(len(sel)),
                                 "method": "median z of bright face texels, mean x and y (survivor_eye.py)"}}
    os.makedirs(os.path.dirname(o["out"]), exist_ok=True)
    with open(o["out"], "w") as f:
        json.dump(res, f, indent=1)
    print("SURVIVOR EYE", json.dumps(res))


if __name__ == "__main__":
    main()
