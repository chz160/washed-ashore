"""Gait figures of one action over the whole cycle: knee and elbow flexion ranges, flight, hip heights, torso lean, finger spread, stride and cadence.

blender -b -noaudio -P clip_metrics.py -- --src <fbx> --action <name> --label <text> --speed <clip m/s> --out <json>
Flexion = 180 degrees minus the interior joint angle. Plant = foot bone head within 3 cm of that foot's lowest height. Flight = frames where neither foot is
planted. Finger spread = the angle between the Index1 and Pinky1 bone axes on the left hand (smaller = more relaxed / closed).
"""
import json
import math
import sys

import bpy
from mathutils import Vector


def args():
    a = sys.argv[sys.argv.index("--") + 1:]
    return {a[i][2:]: a[i + 1] for i in range(0, len(a), 2)}


def flex(a, joint, b):
    return 180.0 - math.degrees((a - joint).angle(b - joint))


def main():
    o = args()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=o["src"])
    arm = next(x for x in bpy.data.objects if x.type == "ARMATURE")
    act = next(a for a in bpy.data.actions if a.name.split("|")[-1] == o["action"])
    arm.animation_data_create()
    arm.animation_data.action = act
    sc = bpy.context.scene
    fps = sc.render.fps / sc.render.fps_base
    pb = arm.pose.bones
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    frames = list(range(f0, f1))
    rows = []
    for f in frames:
        sc.frame_set(f)
        h = lambda n: Vector(pb[n].head)
        row = {
            "hip": pb["Hips"].head.z,
            "footL": h("Foot.L"), "footR": h("Foot.R"),
            "kneeL": flex(h("UpperLeg.L"), h("LowerLeg.L"), h("Foot.L")),
            "kneeR": flex(h("UpperLeg.R"), h("LowerLeg.R"), h("Foot.R")),
            "elbowL": flex(h("UpperArm.L"), h("LowerArm.L"), h("Wrist.L")),
            "elbowR": flex(h("UpperArm.R"), h("LowerArm.R"), h("Wrist.R")),
            "torso": math.degrees(math.atan2(-(h("Neck") - h("Hips")).y, (h("Neck") - h("Hips")).z)),
        }
        ia = pb["Index1.L"].tail - pb["Index1.L"].head
        pa = pb["Pinky1.L"].tail - pb["Pinky1.L"].head
        row["spread"] = math.degrees(ia.angle(pa)) if ia.length > 1e-6 and pa.length > 1e-6 else 0.0
        rows.append(row)
    n = len(rows)
    low = {s: min(r["foot" + s].z for r in rows) for s in "LR"}
    planted = [{s: r["foot" + s].z < low[s] + 0.03 for s in "LR"} for r in rows]
    flight = [i for i in range(n) if not planted[i]["L"] and not planted[i]["R"]]
    stance = [i for i in range(n) if planted[i]["L"]]
    mid = stance[len(stance) // 2] if stance else 0
    cycle_s = n / fps
    speed = float(o["speed"])
    out = {
        "label": o["label"], "cycle_s": round(cycle_s, 3), "frames": n,
        "mid_stance_hip_m": round(rows[mid]["hip"], 3),
        "peak_hip_in_flight_m": round(max(rows[i]["hip"] for i in flight), 3) if flight else None,
        "peak_hip_m": round(max(r["hip"] for r in rows), 3),
        "flight_fraction_of_cycle": round(len(flight) / n, 3),
        "flight_s_per_step": round(len(flight) / n * cycle_s / 2.0, 3),
        "stance_knee_flexion_at_mid_stance_deg": round(rows[mid]["kneeL"], 1),
        "peak_knee_flexion_deg": {"L": round(max(r["kneeL"] for r in rows), 1), "R": round(max(r["kneeR"] for r in rows), 1)},
        "elbow_flexion_deg": {"min": round(min(min(r["elbowL"], r["elbowR"]) for r in rows), 1), "max": round(max(max(r["elbowL"], r["elbowR"]) for r in rows), 1)},
        "torso_lean_deg": {"mean": round(sum(r["torso"] for r in rows) / n, 1), "min": round(min(r["torso"] for r in rows), 1), "max": round(max(r["torso"] for r in rows), 1)},
        "finger_spread_deg_mean": round(sum(r["spread"] for r in rows) / n, 1),
        "clip_speed_mps": speed,
        "stride_m": round(speed * cycle_s, 2), "step_m": round(speed * cycle_s / 2.0, 2),
        "cadence_steps_per_s_at_rate_1": round(2.0 / cycle_s, 2),
    }
    with open(o["out"], "w") as fh:
        json.dump(out, fh, indent=1)
    print("METRICS", json.dumps(out))


main()
