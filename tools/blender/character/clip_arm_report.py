"""Per-clip prints for item 3 (td-49 / director): the largest upper-arm elevation above horizontal, the largest ankle flexion each way from the bind, and
Sprint's arm-high frame. blender -b -noaudio -P clip_arm_report.py -- <clips.fbx> <out.json>
Elevation = angle of (LowerArm head - UpperArm head) above the horizontal plane (0 hanging is -90). Ankle angle = angle between the LowerLeg and Foot bones,
reported as the change from the bind (positive = dorsiflexion, toes up)."""
import json
import math
import sys

import bpy
import numpy as np

path, out = sys.argv[sys.argv.index("--") + 1:][:2]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
arm.animation_data_create()


def vec(h, t):
    return np.array(t) - np.array(h)


def ankle_angle(pb_ll, pb_ft):
    a = vec(pb_ll.head, pb_ll.tail)
    b = vec(pb_ft.head, pb_ft.tail)
    c = float(a @ b / (np.linalg.norm(a) * np.linalg.norm(b)))
    return math.degrees(math.acos(max(-1, min(1, c))))


bind = {}
arm.animation_data.action = None
for _pb in arm.pose.bones:   # the bind: every pose bone at rest
    _pb.matrix_basis.identity()
bpy.context.view_layer.update()
for s in "LR":
    bind[s] = ankle_angle(arm.pose.bones["LowerLeg." + s], arm.pose.bones["Foot." + s])
print("ANKLE bind angle (L, R):", round(bind["L"], 1), round(bind["R"], 1))
kbind = {}
for s_ in "LR":
    kbind[s_] = ankle_angle(arm.pose.bones["UpperLeg." + s_], arm.pose.bones["LowerLeg." + s_])   # same helper: the angle between two bones' directions
print("KNEE bind angle (L, R):", round(kbind["L"], 1), round(kbind["R"], 1))
rep = {}
for act in sorted(bpy.data.actions, key=lambda a: a.name):
    arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    best = {"arm": (-999, None, None), "up": (-999, None, None), "down": (999, None, None)}
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        for s in "LR":
            ua, la = arm.pose.bones["UpperArm." + s], arm.pose.bones["LowerArm." + s]
            v = vec(ua.head, la.head)
            el = math.degrees(math.asin(v[2] / np.linalg.norm(v)))
            if el > best["arm"][0]:
                best["arm"] = (el, f, s)
            d = ankle_angle(arm.pose.bones["LowerLeg." + s], arm.pose.bones["Foot." + s]) - bind[s]
            kf = ankle_angle(arm.pose.bones["UpperLeg." + s], arm.pose.bones["LowerLeg." + s]) - kbind[s]
            if kf > best.get("knee", (-999, None, None))[0]:
                best["knee"] = (kf, f, s)
            # the angle between the bones decreases when the toes go up (dorsiflexion): report up as bind - angle
            up = -d
            if up > best["up"][0]:
                best["up"] = (up, f, s)
            if up < best["down"][0]:
                best["down"] = (up, f, s)
    rep[act.name.split("|")[-1]] = {"max_upper_arm_elevation_deg": round(best["arm"][0], 1), "at": [best["arm"][1], best["arm"][2]],
                                    "ankle_dorsiflexion_max_deg": round(best["up"][0], 1), "ankle_plantarflexion_max_deg": round(-best["down"][0], 1), "knee_flexion_max_deg": round(best["knee"][0], 1), "knee_flexion_at": [best["knee"][1], best["knee"][2]],
                                    "frames": [f0, f1]}
with open(out, "w") as fh:
    json.dump(rep, fh, indent=1)
top = sorted(rep.items(), key=lambda kv: -kv[1]["max_upper_arm_elevation_deg"])[:8]
print("ARM largest upper-arm elevation above horizontal (deg), clip (frame, side):")
for n, r in top:
    print("  %-22s %6.1f  (frame %s, %s)" % (n, r["max_upper_arm_elevation_deg"], r["at"][0], r["at"][1]))
if "Sprint" in rep:
    print("ARM Sprint arm-high frame:", rep["Sprint"]["max_upper_arm_elevation_deg"], "deg at frame", rep["Sprint"]["at"])
print("ANKLE clips beyond 40 degrees up or 50 down from the bind (director sees these as stills first):")
for n, r in rep.items():
    if r["ankle_dorsiflexion_max_deg"] > 40 or r["ankle_plantarflexion_max_deg"] > 50:
        print("  %-22s up %.1f down %.1f" % (n, r["ankle_dorsiflexion_max_deg"], r["ankle_plantarflexion_max_deg"]))
print("KNEE largest flexion from the bind (deg):")
for n, r in sorted(rep.items(), key=lambda kv: -kv[1]["knee_flexion_max_deg"])[:10]:
    print("  %-22s %6.1f (frame %s, %s)" % (n, r["knee_flexion_max_deg"], r["knee_flexion_at"][0], r["knee_flexion_at"][1]))
print("CLIPS reported:", len(rep))
