# Derived from td-49's pod49/td/gait.py (same knee and hip definitions); measures only: the gates are in gait_gates.py.
"""Measure the Jog and Sprint clips (all directions, capped and _Full) and the pack's Run and Walk.
blender -b -noaudio -P gait_report.py -- <authored.fbx> [<out.json>]
Per clip: support-leg knee flexion (deg, 0 straight, td-49's gait.py definition) at touchdown, mid-stance and over the stance; hip height over the rest hip height (cycle mean);
per stance run of each leg: touchdown frame, mid-stance frame, touchdown and mid-stance knee, hip rise from touchdown to mid-stance, the pelvis (mean hip joint) height at keys -2..+3
around the touchdown and at mid-stance; the knee change per key in stance (the snap), including the loop seam; the hip height range (bob) and its largest second difference per key;
torso lean. Stance = the foot within 3 cm of the lowest foot height of the cycle."""
import json
import math
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=args[0])
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
sc = bpy.context.scene
W = arm.matrix_world
rest_hip = (W @ arm.data.bones["UpperLeg.L"].head_local).z


def leg(sd):
    h = W @ arm.pose.bones["UpperLeg." + sd].head
    k = W @ arm.pose.bones["LowerLeg." + sd].head
    a = W @ arm.pose.bones["Foot." + sd].head
    return 180.0 - math.degrees((h - k).angle(a - k)), h, a


res = {}
for act in bpy.data.actions:
    name = act.name.split("|")[-1].split(".")[0]
    if not (name.startswith(("Jog", "Sprint")) or name in ("Run", "Walk")):
        continue
    arm.animation_data_create()
    arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    K = []   # per key: frame, flex L, flex R, foot z L/R, horizontal hip-foot distance L/R, pelvis (mean hip) z, torso pitch
    pitch, cpitch = [], []
    for f in range(f0, f1 + 1):
        sc.frame_set(f)
        L, R = leg("L"), leg("R")
        hd = lambda x: math.hypot(x[1].x - x[2].x, x[1].y - x[2].y)
        K.append({"f": f, "flex": {"L": L[0], "R": R[0]}, "fz": {"L": L[2].z, "R": R[2].z}, "hd": {"L": hd(L), "R": hd(R)}, "hz": (L[1].z + R[1].z) / 2})
        nk = W @ arm.pose.bones["Neck"].head
        hp = (W @ arm.pose.bones["UpperLeg.L"].head + W @ arm.pose.bones["UpperLeg.R"].head) / 2
        v = nk - hp
        pitch.append(math.degrees(math.atan2(-v.y, v.z)))
        cv = nk - (W @ arm.pose.bones["Chest"].head)
        cpitch.append(math.degrees(math.atan2(-cv.y, cv.z)))
    cyc = K[:-1] if len(K) > 2 else K   # the last key repeats the first
    n = len(cyc)
    zmin = min(min(k["fz"].values()) for k in cyc)
    band = zmin + 0.03
    runs = []
    for s in "LR":
        flags = [k["fz"][s] <= band for k in cyc]
        for st in [i for i in range(n) if flags[i] and not flags[(i - 1) % n]]:
            m = 0
            while flags[(st + m) % n]:
                m += 1
            idx = [(st + j) % n for j in range(m)]
            mid = min(idx, key=lambda i: cyc[i]["hd"][s])
            hz_td = cyc[st]["hz"]
            upto = [(st + j) % n for j in range(((mid - st) % n) + 1)]
            sd = lambda i: abs(cyc[(i + 1) % n]["hz"] - 2 * cyc[i]["hz"] + cyc[i - 1]["hz"])
            td_sd = max(sd((st + j) % n) for j in range(-3, 4))   # the pelvis second difference around the touchdown (keys -3..+3)
            runs.append({"leg": s, "hip_second_diff_touchdown_m": round(td_sd, 5), "touchdown_frame": cyc[st]["f"], "mid_frame": cyc[mid]["f"], "keys": m, "knee_touchdown": round(cyc[st]["flex"][s], 2), "knee_mid": round(cyc[mid]["flex"][s], 2),
                         "hip_rise_td_to_mid_m": round(max(cyc[i]["hz"] for i in upto) - hz_td, 4), "hip_z_stance_range_m": round(max(cyc[i]["hz"] for i in idx) - min(cyc[i]["hz"] for i in idx), 4),
                         "pelvis_z_keys_m2_to_p3": [round(cyc[(st + j) % n]["hz"], 4) for j in range(-2, 4)], "pelvis_z_mid": round(cyc[mid]["hz"], 4)})
    snap = {"max_deg_per_key": 0.0, "frame": None, "leg": None}
    for i in range(n):
        a, b = cyc[i], cyc[(i + 1) % n]   # includes the loop seam last -> first
        for s in "LR":
            if a["fz"][s] <= band or b["fz"][s] <= band:
                dd = abs(b["flex"][s] - a["flex"][s])
                if dd > snap["max_deg_per_key"]:
                    snap = {"max_deg_per_key": round(dd, 2), "frame": a["f"], "leg": s}
    snap["deg_per_s"] = round(snap["max_deg_per_key"] * 60.0, 1)
    hz = [k["hz"] for k in cyc]
    d2 = max(abs(hz[(i + 1) % n] - 2 * hz[i] + hz[i - 1]) for i in range(n))
    d2_at = max(range(n), key=lambda i: abs(hz[(i + 1) % n] - 2 * hz[i] + hz[i - 1]))
    res[name] = {"runs": runs, "knee_snap": snap, "hip_mean_over_rest": round(sum(hz) / n / rest_hip, 3), "rest_hip_m": round(rest_hip, 3),
                 "bob_m": round(max(hz) - min(hz), 4), "hip_second_diff_max_m": round(d2, 5), "hip_second_diff_max_frame": cyc[d2_at]["f"],
                 "knee_touchdown_min": min((r["knee_touchdown"] for r in runs), default=None), "knee_touchdown_max": max((r["knee_touchdown"] for r in runs), default=None),
                 "hip_second_diff_touchdown_max_m": max((r["hip_second_diff_touchdown_m"] for r in runs), default=None), "knee_mid_max": max((r["knee_mid"] for r in runs), default=None), "hip_rise_max_m": max((r["hip_rise_td_to_mid_m"] for r in runs), default=None),
                 "torso_pitch_deg": round(sum(pitch) / len(pitch), 1), "chest_lean_deg": round(sum(cpitch) / len(cpitch), 1)}
    r = res[name]
    print("GAIT %-16s touchdown knee %s..%s mid %s hip/rest %.3f | snap %.2f deg/key (%.0f deg/s) at frame %s leg %s | bob %.3f m, hip rise td->mid %.3f m, 2nd diff %.5f m (around touchdown %.5f, cycle max at frame %s) | lean %.1f" % (
        name, r["knee_touchdown_min"], r["knee_touchdown_max"], r["knee_mid_max"], r["hip_mean_over_rest"], snap["max_deg_per_key"], snap["deg_per_s"], snap["frame"], snap["leg"], r["bob_m"],
        r["hip_rise_max_m"] or 0.0, d2, r["hip_second_diff_touchdown_max_m"] or 0.0, r["hip_second_diff_max_frame"], r["chest_lean_deg"]))
    for ru in runs[:2]:
        print("GAIT %-16s   leg %s touchdown frame %s mid frame %s: pelvis z keys -2..+3 %s, mid %s, stance range %.3f m" % (name, ru["leg"], ru["touchdown_frame"], ru["mid_frame"], ru["pelvis_z_keys_m2_to_p3"], ru["pelvis_z_mid"], ru["hip_z_stance_range_m"]))
if len(args) > 1:
    with open(args[1], "w") as fh:
        json.dump(res, fh, indent=1)
print("GAIT MEASURED", len(res), "clips")
