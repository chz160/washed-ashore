"""Full-arm probe and fore-aft gates (pod 49; td-49 gates of record G-A1..G-A4, director gate/pod49-arm-foreaft-gate).
blender -b -noaudio -P arm_probe.py -- <authored.fbx> <clip,clip,...> [<out.json>]
Per clip and arm over one cycle: wrist height against the shoulder, elbow flexion, shoulder swing, palm facing, and the wrist's fore-aft position against the pelvis and the chest.
Palm normal = (Middle1 - Wrist) x (Thumb1 - Pinky1) in the chest frame, sign fixed per side so that Stand_Idle (hands at the sides, palms toward the thighs) gives palm-in = +1.
Fore-aft convention: clip world frame, root forward = -Y, measured along Y; for a clip whose pelvis or chest yaw exceeds 15 degrees off -Y the figures are measured along the Chest
bone's forward axis projected to the floor, per frame (the clip is listed). Measures only; gait_gates.py applies the gates of record to every *_Full Jog/Sprint clip:
  G-A1 per arm, once per cycle, the wrist is >= 0.05 m behind the midpoint of the UpperLeg heads;
  G-A2 every frame, the wrist is <= 0.30 (Jog) / 0.36 (Sprint) m ahead of the Chest head;
  G-A3 at each arm's forward extreme the forearm (LowerArm head -> Wrist head) is within 60 degrees of +Z;
  G-A4 elbow Jog 80-115, Sprint 80-100 degrees, the two arms in opposition (correlation of the left and right wrist fore-aft < -0.5), palm toward the body (>= 0.5)."""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

a = sys.argv[sys.argv.index("--") + 1:]
path, names = a[0], a[1].split(",")
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
W = arm.matrix_world
sc = bpy.context.scene
acts = {x.name.split("|")[-1].split(".")[0]: x for x in bpy.data.actions}

YAW_SWITCH = 15.0
AXES = (Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1)), Vector((-1, 0, 0)), Vector((0, -1, 0)), Vector((0, 0, -1)))


def set_frame(name, f):
    arm.animation_data_create()
    arm.animation_data.action = acts[name]
    sc.frame_set(f)


def forward_axis(bone):
    m = (W @ arm.pose.bones[bone].matrix).to_3x3()
    return max(AXES, key=lambda v: (m @ v).dot(Vector((0, -1, 0))))


set_frame("Stand_Idle", 1)
CHEST_AXIS, BODY_AXIS = forward_axis("Chest"), forward_axis("Body")


def frame_data(side):
    pb = arm.pose.bones
    sh = W @ pb["UpperArm." + side].head
    el = W @ pb["LowerArm." + side].head
    wr = W @ pb["Wrist." + side].head
    ua = (el - sh).normalized()
    fa = (wr - el).normalized()
    f = (W @ pb["Middle1." + side].head) - wr
    t = (W @ pb["Thumb1." + side].head) - (W @ pb["Pinky1." + side].head)
    n = f.cross(t).normalized()
    chest_m = (W @ pb["Chest"].matrix).to_3x3()
    nc = chest_m.inverted() @ n
    cf = chest_m @ CHEST_AXIS
    cf_floor = Vector((cf.x, cf.y, 0.0)).normalized()
    bf = (W @ pb["Body"].matrix).to_3x3() @ BODY_AXIS
    pelvis = ((W @ pb["UpperLeg.L"].head) + (W @ pb["UpperLeg.R"].head)) / 2
    chest = W @ pb["Chest"].head
    fwd_p, fwd_c = -(wr - pelvis).y, -(wr - chest).y                               # along -Y
    fwd_p_cf, fwd_c_cf = (wr - pelvis).dot(cf_floor), (wr - chest).dot(cf_floor)   # along the chest forward
    return {"wrist_dz": wr.z - sh.z, "elbow": math.degrees(ua.angle(fa)), "swing": math.degrees(math.atan2(-ua.y, -ua.z)), "nc": nc,
            "fwd_p": fwd_p, "fwd_c": fwd_c, "fwd_p_cf": fwd_p_cf, "fwd_c_cf": fwd_c_cf,
            "forearm_off_up": math.degrees(math.acos(max(-1.0, min(1.0, fa.z)))),
            "yaw_c": math.degrees(math.atan2(cf_floor.x, -cf_floor.y)), "yaw_p": math.degrees(math.atan2(bf.x, -bf.y))}


def run(name):
    f0, f1 = int(acts[name].frame_range[0]), int(acts[name].frame_range[1])
    rows = {"L": [], "R": []}
    for f in range(f0, f1 + 1):
        set_frame(name, f)
        for s in "LR":
            rows[s].append(frame_data(s))
    return rows


ref = run("Stand_Idle")
sign = {s: (1.0 if ref[s][0]["nc"].x >= 0 else -1.0) for s in "LR"}
out = {}
for name in names:
    rows = run(name)
    yaw_c = max(abs(x["yaw_c"]) for x in rows["L"])
    yaw_p = max(abs(x["yaw_p"]) for x in rows["L"])
    use_cf = yaw_c > YAW_SWITCH or yaw_p > YAW_SWITCH
    kp, kc = ("fwd_p_cf", "fwd_c_cf") if use_cf else ("fwd_p", "fwd_c")
    res = {"yaw_chest_max_deg": round(yaw_c, 1), "yaw_pelvis_max_deg": round(yaw_p, 1), "measured_along": "chest forward" if use_cf else "-Y"}
    for s in "LR":
        r = rows[s]
        pin = [sign[s] * x["nc"].x for x in r]
        ext = max(r, key=lambda x: x[kc])   # the arm's forward extreme
        res[s] = {"wrist_dz_max": round(max(x["wrist_dz"] for x in r), 3), "wrist_dz_min": round(min(x["wrist_dz"] for x in r), 3), "elbow_min": round(min(x["elbow"] for x in r), 1),
                  "elbow_max": round(max(x["elbow"] for x in r), 1), "swing_fwd_max": round(max(x["swing"] for x in r), 1), "swing_back_max": round(-min(x["swing"] for x in r), 1),
                  "palm_in_min": round(min(pin), 2), "behind_pelvis_max": round(-min(x[kp] for x in r), 3), "ahead_of_chest_max": round(max(x[kc] for x in r), 3),
                  "forearm_off_up_at_fwd": round(ext["forearm_off_up"], 1)}
    L, R = [x[kp] for x in rows["L"]], [x[kp] for x in rows["R"]]
    mL, mR = sum(L) / len(L), sum(R) / len(R)
    res["alternation_corr"] = round(sum((x - mL) * (y - mR) for x, y in zip(L, R)) / (math.sqrt(sum((x - mL) ** 2 for x in L) * sum((y - mR) ** 2 for y in R)) + 1e-12), 2)
    out[name] = res
    print("ARM %-16s yaw chest %.1f pelvis %.1f (measured along %s), alternation %.2f" % (name, yaw_c, yaw_p, res["measured_along"], res["alternation_corr"]))
    for s in "LR":
        print("ARM %-16s %s %s" % (name, s, res[s]))
over = [n for n, r in out.items() if r["measured_along"] != "-Y"]
print("ARM clips measured along the chest forward (yaw > %.0f deg): %s" % (YAW_SWITCH, over or "none"))
if len(a) > 2:
    json.dump(out, open(a[2], "w"), indent=1)
