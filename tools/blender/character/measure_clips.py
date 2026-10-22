"""Independent check of any rig FBX (pack or authored): ground speed, foot-plant drift, eye height per action.

blender -b -noaudio -P measure_clips.py -- --src <fbx> --report <json> [--shots <dir> --shot-actions A,B]
Re-imports the FBX, so it measures what Unity will read. Eye = Head bone head + (0, -0.08, +0.103) in the
standing head frame (same definition as rigkit.py).
With --shots <json> it dumps bone positions for 4 frames per --shot-actions action (draw_sheet.py draws them).
"""
import json
import os
import sys

import bpy
from mathutils import Matrix, Vector

FWD = Vector((0, -1, 0))
FLAT_BAND = 0.006  # m: frames at the foot's lowest height (speed fit)
PLANT_BAND = 0.03  # m above the foot's lowest height, spec convention (designer-2 section 4)


def runs_of(idx):
    runs, cur = [], []
    for i in idx:
        if cur and i != cur[-1] + 1:
            runs.append(cur)
            cur = []
        cur.append(i)
    if cur:
        runs.append(cur)
    return runs


HAND_POINTS = ("Wrist", "Index4", "Middle4", "Pinky4", "Thumb3")
PITCHES = (-15, -10, -5, 0, 5, 10, 15, 20, 25, 30)  # degrees, positive = looking down (the pitch sweep's sign)


def hand_view(arm, act, scene, pb, opt):
    """Samples of hand points (wrist and fingertips) inside the first-person frustum, per camera pitch: FOV 70 vertical, 16:9, near 0.1.
    The camera sits at --camera x,y,z (armature space, fixed: the camera never follows the body) looking along -Y pitched down by p.
    Conservative: the real body is further back (bodyBackOffset and the look-down offset), which only hides more."""
    import math
    crouch = act.name.split("|")[-1].startswith("Crouch") and "camera-crouch" in opt
    cx, cy, cz = (float(v) for v in opt["camera-crouch" if crouch else "camera"].split(","))
    cam = Vector((cx, cy, cz))
    tv, th = math.tan(math.radians(35.0)), math.tan(math.radians(35.0)) * 16.0 / 9.0
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    counts = {str(p): 0 for p in PITCHES}
    total = 0
    for f in range(f0, f1):
        for sub in (0.0, 0.25, 0.5, 0.75):
            scene.frame_set(f, subframe=sub)
            pts = []
            for side in ("L", "R"):
                for n in HAND_POINTS:
                    b = pb.get(f"{n}.{side}")
                    if b:
                        pts.append(arm.matrix_world @ b.head)
            total += 1
            for p in PITCHES:
                pr = math.radians(p)
                fwd = Vector((0, -math.cos(pr), -math.sin(pr)))
                up = Vector((0, -math.sin(pr), math.cos(pr)))
                right = Vector((-1, 0, 0))
                for q in pts:
                    d = q - cam
                    z = fwd.dot(d)
                    if z > 0.1 and abs(up.dot(d)) / z <= tv and abs(right.dot(d)) / z <= th:
                        counts[str(p)] += 1
                        break
    return {"samples": total, "frames_with_a_hand_in_view": counts}


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:]
    return {argv[i][2:]: argv[i + 1] for i in range(0, len(argv), 2)}


def main():
    opt = parse()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=opt["src"])
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    arm.animation_data_create()
    idle = next((a for a in bpy.data.actions if a.name.split("|")[-1] == "Idle"), None)
    pb = arm.pose.bones
    arm.animation_data.action = idle
    scene.frame_set(1)
    hm0 = pb["Head"].matrix.copy()
    eye_local = hm0.inverted() @ (hm0.translation + Vector((0, -0.08, 0.103)))
    if "eye-json" in opt:  # measured eye point in the Head bone's local frame (measure_head_eye.py)
        with open(opt["eye-json"]) as fh:
            eye_local = Vector(json.load(fh)[opt["eye-key"]]["eye_in_head_bone_local"])
    out = {"fps": fps, "clips": {}}
    for act in sorted(bpy.data.actions, key=lambda a: a.name):
        name = act.name.split("|")[-1]
        arm.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        L, R, E, B = [], [], [], []
        L4, R4 = [], []  # 4x sub-frame samples: the foot keeps moving between 30 fps keys
        for f in range(f0, f1 + 1):
            for sub in (0.0, 0.25, 0.5, 0.75):
                if f == f1 and sub > 0:
                    continue
                scene.frame_set(f, subframe=sub)
                L4.append((arm.matrix_world @ pb["Foot.L"].head).copy())
                R4.append((arm.matrix_world @ pb["Foot.R"].head).copy())
            scene.frame_set(f)
            L.append((arm.matrix_world @ pb["Foot.L"].head).copy())
            R.append((arm.matrix_world @ pb["Foot.R"].head).copy())
            E.append((pb["Head"].matrix @ eye_local).copy())
            B.append(pb["Body"].head.copy())
        speeds, slides = [], []
        for feet in (L, R):
            lo = min(p.z for p in feet)
            lows = [i for i, p in enumerate(feet) if p.z < lo + FLAT_BAND]
            for run in runs_of(lows):
                if len(run) >= 3:  # least-squares slope of position against time
                    ts = [(i - run[0]) / fps for i in run]
                    tm = sum(ts) / len(ts)
                    d = sum(((t - tm) * (feet[i] - feet[run[0]]) for t, i in zip(ts, run)), Vector((0, 0, 0))) / sum((t - tm) ** 2 for t in ts)
                    speeds.append((d.length, -d.normalized() if d.length > 1e-6 else Vector((0, 0, 0))))
        clip_speed = sum(s for s, _ in speeds) / len(speeds) if speeds else 0.0
        mdir = speeds[0][1] if speeds else Vector((0, 0, 0))
        if clip_speed > 0.2:
            # world drift of a plant (foot within PLANT_BAND of lowest) if the body moves at exactly the
            # clip's own ground speed (playback rate = game speed / clip speed cancels out); sampled 4x between the keys
            for feet in (L4, R4):  # sampled at 4x the key rate
                lo = min(p.z for p in feet)
                for run in runs_of([i for i, p in enumerate(feet) if p.z < lo + PLANT_BAND]):
                    if len(run) >= 2:  # largest horizontal distance between any two samples of the plant, body travel removed
                        pts = []
                        for i in run:
                            q = feet[i] - feet[run[0]]
                            q.z = 0
                            pts.append(q + mdir * clip_speed * (i - run[0]) / (4 * fps))
                        slides.append(max((a - b).length for a in pts for b in pts))
        speeds = [s for s, _ in speeds]
        hv = hand_view(arm, act, scene, pb, opt) if "camera" in opt and name.startswith(("Jog", "Sprint", "Walk", "Run", "CrouchWalk", "Crouch_Idle")) else None
        out["clips"][name] = {
            "hand_in_view": hv,
            "frames": f1 - f0 + 1, "duration_s": round((f1 - f0) / fps, 3),
            "clip_speed_mps": round(clip_speed, 3) if clip_speed > 0.2 else None,
            "plant_speed_mps_min": round(min(speeds), 3) if speeds else None,
            "plant_speed_mps_max": round(max(speeds), 3) if speeds else None,
            "plant_drift_m_max": round(max(slides), 3) if slides else None,
            "foot_z_min": round(min(min(p.z for p in L), min(p.z for p in R)), 3),
            "eye_z_min": round(min(e.z for e in E), 3), "eye_z_max": round(max(e.z for e in E), 3),
            "eye_y_mean": round(sum(e.y for e in E) / len(E), 3),
        }
    with open(opt["report"], "w") as fh:
        json.dump(out, fh, indent=1)
    if "shots" in opt:
        render_shots(arm, opt)
    print("MEASURED", len(out["clips"]), "->", opt["report"])


def render_shots(arm, opt):
    """Dump bone head/tail positions for start / quarter / half / end frames (drawn by draw_sheet.py)."""
    scene = bpy.context.scene
    pb = arm.pose.bones
    skip = ("Index", "Middle", "Ring", "Pinky", "Thumb", "PT.", "_end")
    shots = {}
    for name in opt["shot-actions"].split(","):
        act = next((a for a in bpy.data.actions if a.name.split("|")[-1] == name), None)
        if not act:
            continue
        arm.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        frames = []
        for f in (f0, f0 + (f1 - f0) // 4, f0 + (f1 - f0) // 2, f1):
            scene.frame_set(f)
            frames.append({b.name: [list(b.head), list(b.tail)] for b in pb if not any(s in b.name for s in skip)})
        shots[name] = frames
    os.makedirs(os.path.dirname(opt["shots"]) or ".", exist_ok=True)
    with open(opt["shots"], "w") as fh:
        json.dump(shots, fh)


main()
