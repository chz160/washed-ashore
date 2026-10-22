"""Per-frame arm figures of one action: shoulder flexion (upper arm in the sagittal plane, 0 hanging, + forward) and elbow flexion, both arms.

blender -b -noaudio -P arm_metrics.py -- --src <fbx> --action <name> --out <json>
Shoulder flexion = atan2(forward component, downward component) of the UpperArm bone's direction (head to the LowerArm head), rigs face -Y.
Elbow flexion = 180 degrees minus the interior angle at the elbow (straight arm = 0).
"""
import json
import math
import sys

import bpy
from mathutils import Vector


def args():
    a = sys.argv[sys.argv.index("--") + 1:]
    return {a[i][2:]: a[i + 1] for i in range(0, len(a), 2)}


def main():
    o = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=o["src"])
    arm = next(x for x in bpy.data.objects if x.type == "ARMATURE")
    act = next(a for a in bpy.data.actions if a.name.split("|")[-1] == o["action"])
    arm.animation_data_create()
    arm.animation_data.action = act
    sc = bpy.context.scene
    pb = arm.pose.bones
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    rows = []
    for f in range(f0, f1):
        sc.frame_set(f)
        row = {"frame": f}
        up = (Vector(pb["Neck"].head) - Vector(pb["Hips"].head)).normalized()
        fw = Vector((0.0, -1.0, 0.0))
        fw = (fw - up * fw.dot(up)).normalized()  # the torso frame: up = hips to neck, forward perpendicular to it
        for s in "LR":
            sh, el, wr = Vector(pb["UpperArm." + s].head), Vector(pb["LowerArm." + s].head), Vector(pb["Wrist." + s].head)
            d = el - sh
            row["shoulder_" + s] = round(math.degrees(math.atan2(-d.y, -d.z)), 1)
            row["elbow_" + s] = round(180.0 - math.degrees((sh - el).angle(wr - el)), 1)
            row["wrist_forward_of_shoulder_" + s] = round(-(wr - sh).y, 3)
            row["wrist_below_shoulder_" + s] = round(sh.z - wr.z, 3)
            hip = Vector(pb["Hips"].head)
            row["shoulder_torso_" + s] = round(math.degrees(math.atan2(d.dot(fw), -d.dot(up))), 1)
            row["wrist_fore_of_shoulder_torso_" + s] = round((wr - sh).dot(fw), 3)  # + in front, in the torso frame
            row["wrist_below_shoulder_torso_" + s] = round(-(wr - sh).dot(up), 3)
            row["wrist_pos_" + s] = [round(wr.x, 3), round(wr.y, 3), round(wr.z, 3)]
            row["wrist_forward_of_hip_" + s] = round(-(wr - hip).y, 3)  # + in front of the hip
            row["wrist_height_" + s] = round(wr.z, 3)  # above the floor
        row["hip_height"] = round(pb["Hips"].head.z, 3)
        row["chest_height"] = round(pb["Chest"].head.z, 3)
        row["chin_height"] = round(pb["Neck"].head.z, 3)
        rows.append(row)
    summary = {}
    for s in "LR":
        sh = [r["shoulder_" + s] for r in rows]
        el = [r["elbow_" + s] for r in rows]
        summary[s] = {"shoulder_forward_max": max(sh), "shoulder_back_max": min(sh), "shoulder_amplitude": round(max(sh) - min(sh), 1),
                      "elbow_min": min(el), "elbow_max": max(el)}
    with open(o["out"], "w") as fh:
        json.dump({"action": o["action"], "src": o["src"], "summary": summary, "frames": rows}, fh)
    print("ARMS", o["action"], json.dumps(summary))


main()
