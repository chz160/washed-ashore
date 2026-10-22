"""Four gait phases of one action as bone positions, plus mid-stance numbers (hip height, knee flexion of both legs, torso pitch).

blender -b -noaudio -P gait_stills.py -- --src <fbx> --action <Run|Sprint|Sprint_Full> --out <json>
Phases (left foot as the reference): contact (first frame of its plant), mid-stance (middle of the plant), toe-off (last plant frame),
mid-flight (middle of the stretch where neither foot is in its plant band; the frame of lowest combined foot height if the gait has none).
Plant = foot bone head within 3 cm of that foot's lowest height. Draw with draw_gait.py (Pillow, normal Python).
"""
import json
import math
import sys

import bpy
from mathutils import Vector

SKIP = ("Index", "Middle", "Ring", "Pinky", "Thumb", "PT.", "_end")
SKIP_NO_FINGERS = ("PT.", "_end")


def args():
    a = sys.argv[sys.argv.index("--") + 1:]
    return {a[i][2:]: a[i + 1] for i in range(0, len(a), 2)}


def angle(a, b):
    return math.degrees(a.angle(b))


def main():
    o = args()
    skip = SKIP_NO_FINGERS if o.get("fingers") == "1" else SKIP
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=o["src"])
    arm = next(x for x in bpy.data.objects if x.type == "ARMATURE")
    act = next(a for a in bpy.data.actions if a.name.split("|")[-1] == o["action"])
    arm.animation_data_create()
    arm.animation_data.action = act
    sc = bpy.context.scene
    pb = arm.pose.bones
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    frames = list(range(f0, f1))  # the closing key repeats the first
    foot = {"L": [], "R": []}
    for f in frames:
        sc.frame_set(f)
        for s in "LR":
            foot[s].append(Vector(pb["Foot." + s].head))
    lows = {s: min(p.z for p in foot[s]) for s in "LR"}
    plant = {s: [i for i, p in enumerate(foot[s]) if p.z < lows[s] + 0.03] for s in "LR"}
    n = len(frames)
    # rotate the cycle so the left plant is one contiguous run
    start = next(i for i in plant["L"] if (i - 1) % n not in plant["L"])
    run = []
    i = start
    while i in plant["L"] and len(run) < n:
        run.append(i)
        i = (i + 1) % n
    contact, toe_off, mid = run[0], run[-1], run[len(run) // 2]
    flight = [i for i in range(n) if i not in plant["L"] and i not in plant["R"]]
    if flight:  # the middle of the first contiguous flight stretch after the left toe-off
        seq = []
        i = (toe_off + 1) % n
        while i in flight and len(seq) < n:
            seq.append(i)
            i = (i + 1) % n
        mid_flight = seq[len(seq) // 2] if seq else flight[len(flight) // 2]
    else:
        mid_flight = min(range(n), key=lambda k: foot["L"][k].z + foot["R"][k].z)
    phases = [("contact", contact), ("mid-stance", mid), ("toe-off", toe_off), ("mid-flight", mid_flight)]

    def pose(idx):
        sc.frame_set(frames[idx])
        return {b.name: [list(b.head), list(b.tail)] for b in pb if not any(k in b.name for k in skip)}

    def metrics(idx):
        sc.frame_set(frames[idx])
        out = {}
        out["hip_height_m"] = round(pb["Hips"].head.z, 3)
        for s in "LR":
            hip, knee, ank = Vector(pb["UpperLeg." + s].head), Vector(pb["LowerLeg." + s].head), Vector(pb["Foot." + s].head)
            out["knee_flexion_deg_" + s] = round(180.0 - angle(hip - knee, ank - knee), 1)
        spine = Vector(pb["Neck"].head) - Vector(pb["Hips"].head)
        fwd = -spine.y  # the rigs face -Y
        out["torso_pitch_forward_deg"] = round(math.degrees(math.atan2(fwd, spine.z)), 1)
        return out

    result = {"src": o["src"], "action": o["action"], "frames_in_cycle": n, "phases": []}
    for label, idx in phases:
        result["phases"].append({"label": label, "frame": frames[idx], "bones": pose(idx), "metrics": metrics(idx)})
    with open(o["out"], "w") as fh:
        json.dump(result, fh)
    ms = result["phases"][1]["metrics"]
    print("GAIT", o["action"], "mid-stance", ms, "phases", [(p["label"], p["frame"]) for p in result["phases"]])


main()
