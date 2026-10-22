"""Author the missing stance clips for one Quaternius character rig and export them as an FBX.

blender -b -noaudio -P author_clips.py -- --src <Animations.fbx> --out <Authored.fbx> --report <report.json> [--only A,B]
Reads the pack's Animations.fbx (rest pose + Idle as the neutral pose), writes new actions on the same
armature and bone names. Speeds in the report are measured from the authored foot paths; poses are authored.
"""
import json
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from rigkit import Rig, smooth, ANKLE_Z  # noqa: E402
from poses import build, fit_eye, lerp_pose, stand_pose, gait  # noqa: E402

# arm pose of the jog and sprint gaits: swing (deg, backward amplitude), forward swing scale, elbow flex (deg), outward lean (deg).
# Overrides come from --arms-file <json> while tuning. These values pass the hand-in-view check (measure_clips.py): 0 samples with a hand
# in the first-person frustum at pitch -15..+30 for Jog and Sprint in every direction (earlier: swing 35 / 55, elbow 85 / 95, no limit).
ARM_BASE = {"Jog": dict(swing=28, elbow=-30, arm_fwd=0.25, abduct=14.0), "Sprint": dict(swing=50, elbow=-35, arm_fwd=0.15, abduct=16.0)}
# Sprint legs (director amend8: hips at pack-Run height, stance knee <= 50 degrees, heel recovery, a real flight, lean 8-12 degrees, rate <= 1.4 by
# a longer flight rather than deeper knees): cycle 0.667 s, stance about 0.11 s per foot (the stance foot moves at 5.80 m/s on the body, whatever the scale: rate 1.379 at 8 m/s),
# flight 0.19 s per step with the hip highest in flight, hip joint ~0.87 m at mid-stance (k 0.90), swing foot lifted 0.30 m.
SPRINT_N, SPRINT_S, SPRINT_ONBODY_SPEED = 20, 0.62, 5.80  # cycle frames at 30 fps, stance travel (m), stance-foot speed ON THE BODY (m/s): rate 1.379 at 8 m/s
SPRINT_LEGS = dict(lean=18, lift=0.34, k=0.91, bob=0.06)
# full-swing arms (the pre-fix pose): built as the extra clips Jog_Full ... SprintRight_Full, NOT in the controller. Held for the view-body-only cap
# fallback (director amend6) and for the future third-person and remote-player issues.
ARM_FULL = {"Jog": dict(swing=35, elbow=-85, arm_fwd=1.0, abduct=0.0), "Sprint": dict(swing=55, elbow=-65, arm_fwd=1.0, abduct=0.0)}
# Sprint full arms (director amend8c): MEASURED targets, symmetrical: the shoulder swings about -23 +/- 62 degrees (amplitude 52 times the foot travel, which reaches 1.2 of the half stance: forward 40, back 85; the wrist ends up behind the hip on the back swing and chin-high in front; pack Run 20 / -85 on one
# arm), the elbow stays 80 to 100 degrees (90 +/- 8: more flexed forward, opening as the arm goes back), so the forearm is never parked at the chest.
# v2 is what lands (director amend8e: the forward swing may reach about chin height); v3 (below) meets the stricter first bar on every frame - the wrist
# 0.16 to 0.31 m below the shoulder and 0.155 m behind it, elbow constant 81 degrees, amplitude 89 instead of 125 - and stays here, unused, in case it is wanted.
SPRINT_FULL_ARMS_V3 = dict(centre=-38.5, amplitude=37.0, elbow=81.5, elbow_swing=0.0, torso_frame=True)
SPRINT_FULL_ARMS = dict(centre=-23.0, amplitude=52.0, elbow=90.0, elbow_swing=7.0, torso_frame=False)  # v2: shoulder 44 forward / 81 back (world frame), elbow 82 to 99
ARMS = {"Jog": {}, "Sprint": {}}
FPS = 60  # key rate: the planted foot must not move between keys while it is inside the 3 cm plant band
K = FPS // 30  # frame-count multiplier: the clip lengths below are written for 30 fps
DIRS = {"": (0.0, -1.0), "Back": (0.0, 1.0), "Left": (1.0, 0.0), "Right": (-1.0, 0.0)}
SKELETON_SCALE = 1.0  # uniform scale the body plays on (men: set by td from the measured stand eye); clip eye targets are divided by it
EYE_BIAS = 0.05  # the body mesh sits 0.05 m below the player origin (designer-2 section 4), so the clip's eye above the floor is the stance eye + 0.05
EYE_STAND, EYE_CROUCH, EYE_SQUAT, EYE_SIT, EYE_SLEEP = (e + EYE_BIAS for e in (1.65, 1.20, 1.00, 0.80, 0.20))  # designer-2 player-stances.md section 3
CROUCH_CLIP_SPEED = 1.786  # m/s ON THE BODY (the clip is authored at this / SKELETON_SCALE): puts crouch speeds 1.25..2.50 at rates 0.70..1.40 (designer-2 section 3a)


def sequence(keys, n):
    """keys: [(fraction, Pose)] sorted; returns n+1 poses with smoothstep between keys."""
    out = []
    for i in range(n + 1):
        t = i / n
        for j in range(len(keys) - 1):
            if keys[j][0] <= t <= keys[j + 1][0] + 1e-9:
                a, b = keys[j], keys[j + 1]
                u = smooth((t - a[0]) / max(b[0] - a[0], 1e-9))
                out.append(lerp_pose(a[1], b[1], u))
                break
    return out


def breathe(P, n, amp_deg=1.2, dz=0.004):
    out = []
    for i in range(n + 1):
        Q = P.copy()
        w = math.sin(2 * math.pi * i / n)
        Q.breath = amp_deg * w
        Q.pelvis.z += dz * w
        out.append(Q)
    return out


def arms(P, swing, elbow, abd=0.0):
    for s in "LR":
        P.arm[s] = [swing, elbow, abd]
    return P


def make_all(rig):
    st = stand_pose(rig)
    build(rig, st)
    anchor_y = rig.eye().y
    st0 = st.copy()
    fit_eye(rig, st, EYE_STAND, anchor_y)  # standing eye 1.65 (designer-2 section 3)
    if build(rig, st) > 0.002:  # shorter rig (women): 1.65 is out of leg reach, keep the k=0.975 stand
        st = st0
        build(rig, st)
    stand_eye = rig.eye().z
    C = {}  # name -> (poses, loop, info)

    def gaitclip(name, N, sf, S, d, **kw):
        P, info = gait(rig, N, sf, S, d, anchor_y=anchor_y, fps=FPS, **kw)
        C[name] = (P, True, info)

    def sprint_sf():  # stance fraction of the cycle so that the stance foot moves at SPRINT_ONBODY_SPEED / SCALE in clip space
        return SPRINT_S / (SPRINT_ONBODY_SPEED / SKELETON_SCALE) / (SPRINT_N / 30.0)

    for dn, d in DIRS.items():
        gaitclip("Jog" + dn, 21 * K, 0.23, 0.70, d, lean=8, lift=0.16, k=0.86, bob=0.05, **{**ARM_BASE["Jog"], **ARMS["Jog"]})
        gaitclip("Sprint" + dn, SPRINT_N * K, sprint_sf(), SPRINT_S, d, **SPRINT_LEGS, **{**ARM_BASE["Sprint"], **ARMS["Sprint"]})
        gaitclip("Jog" + dn + "_Full", 21 * K, 0.23, 0.70, d, lean=8, lift=0.16, k=0.86, bob=0.05, **ARM_FULL["Jog"])
        gaitclip("Sprint" + dn + "_Full", SPRINT_N * K, sprint_sf(), SPRINT_S, d, **SPRINT_LEGS, **ARM_FULL["Sprint"], arm_abs=SPRINT_FULL_ARMS)
        sf = 0.62
        gaitclip("CrouchWalk" + dn, 24 * K, sf, CROUCH_CLIP_SPEED / SKELETON_SCALE * sf * 24 / 30, d, lean=25, swing=8, elbow=-30,
                 lift=0.10, k=0.6, bob=0.015, eye_target=EYE_CROUCH)

    def lowered(eye, lean, vary="z", pz=None, a=(0, 0, 0)):
        P = st.copy()
        arms(P, *a)
        P.lean = lean
        if pz is not None:
            P.pelvis.z = pz
        fit_eye(rig, P, eye, anchor_y, vary=vary)
        return P

    crouch = lowered(EYE_CROUCH, 25, a=(-12, -30, 6))
    squat = lowered(EYE_SQUAT, 35, a=(-35, -35, 4))
    sit = lowered(EYE_SIT, 0, vary="lean", pz=0.05, a=(-50, -20, 0))
    C["Crouch_Idle"] = (breathe(crouch, 30 * K), True, {})
    C["Squat_Enter"] = (sequence([(0, st), (1, squat)], 12 * K), False, {})
    C["Squat_Hold"] = (breathe(squat, 30 * K), True, {})
    C["Squat_Exit"] = (sequence([(0, squat), (1, st)], 12 * K), False, {})
    C["Sit_Enter"] = (sequence([(0, st), (0.45, squat), (1, sit)], 30 * K), False, {})
    C["Sit_Loop"] = (breathe(sit, 45 * K, 1.0, 0.003), True, {})
    C["Sit_Exit"] = (sequence([(0, sit), (0.55, squat), (1, st)], 24 * K), False, {})

    # sleep on the back, head under the camera anchor, body extending forward (-Y) [designer-2 lie box]
    lie = st.copy()
    lie.body_rx = -90.0
    arms(lie, 0, 0, 0)
    reach = (rig.leg1 + rig.leg2) * 0.97
    for _ in range(6):
        for s, sg in (("L", 1.0), ("R", -1.0)):
            hip = rig.world("UpperLeg." + s).translation  # hip joint of the previous pass
            lie.feet[s] = [Vector((hip.x, hip.y - reach, lie.pelvis.z)), 0.0]
        build(rig, lie)
        e = rig.eye()
        lie.pelvis.z += EYE_SLEEP - e.z
        lie.pelvis.y += anchor_y - e.y
    build(rig, lie)
    C["Sleep_Down"] = (sequence([(0, st), (0.4, sit), (1, lie)], 45 * K), False, {})
    C["Sleep_Loop"] = (breathe(lie, 60 * K, 1.5, 0.002), True, {})
    C["Sleep_Wake"] = (sequence([(0, lie), (0.6, sit), (1, st)], 45 * K), False, {})

    # range-end variants (designer-2 section 3a): hidden head at the ends of the eye-height ranges, with the transition clips that hand over to
    # the holds at the same end pose (td: otherwise the body jumps by up to 0.10 m at the enter / exit boundary). Sleep Low is the record pose.
    ends = {k: tuple((e + EYE_BIAS) / SKELETON_SCALE for e in v) for k, v in {"Crouch": (1.10, 1.30), "Squat": (0.90, 1.10), "Sit": (0.75, 0.90)}.items()}
    lie_hi = lie.copy()
    for _ in range(6):
        for s, sg in (("L", 1.0), ("R", -1.0)):
            hip = rig.world("UpperLeg." + s).translation
            lie_hi.feet[s] = [Vector((hip.x, hip.y - reach, lie_hi.pelvis.z)), 0.0]
        build(rig, lie_hi)
        e = rig.eye()
        lie_hi.pelvis.z += (0.25 + EYE_BIAS) / SKELETON_SCALE - e.z
        lie_hi.pelvis.y += anchor_y - e.y
    build(rig, lie_hi)
    for suffix, i in (("_Low", 0), ("_High", 1)):
        crouch_x = lowered(ends["Crouch"][i], 25, a=(-12, -30, 6))
        squat_x = lowered(ends["Squat"][i], 35, a=(-35, -35, 4))
        sit_x = lowered(ends["Sit"][i], 0, vary="lean", pz=0.05, a=(-50, -20, 0))
        C["Crouch_Idle" + suffix] = (breathe(crouch_x, 30 * K), True, {})
        C["Squat_Hold" + suffix] = (breathe(squat_x, 30 * K), True, {})
        C["Sit_Loop" + suffix] = (breathe(sit_x, 45 * K, 1.0, 0.003), True, {})
        C["Squat_Enter" + suffix] = (sequence([(0, st), (1, squat_x)], 12 * K), False, {})
        C["Squat_Exit" + suffix] = (sequence([(0, squat_x), (1, st)], 12 * K), False, {})
        C["Sit_Enter" + suffix] = (sequence([(0, st), (0.45, squat), (1, sit_x)], 30 * K), False, {})
        C["Sit_Exit" + suffix] = (sequence([(0, sit_x), (0.55, squat), (1, st)], 24 * K), False, {})
        for dn, d in DIRS.items():
            gaitclip("CrouchWalk" + dn + suffix, 24 * K, 0.62, CROUCH_CLIP_SPEED / SKELETON_SCALE * 0.62 * 24 / 30, d, lean=25, swing=8,
                     elbow=-30, lift=0.10, k=0.6, bob=0.015, eye_target=ends["Crouch"][i])
    C["Sleep_Loop_High"] = (breathe(lie_hi, 60 * K, 1.5, 0.002), True, {})
    C["Sleep_Down_High"] = (sequence([(0, st), (0.4, sit), (1, lie_hi)], 45 * K), False, {})
    C["Sleep_Wake_High"] = (sequence([(0, lie_hi), (0.6, sit), (1, st)], 45 * K), False, {})

    # jump
    pre = st.copy()
    pre.pelvis.z -= 0.07
    pre.lean = 10
    arms(pre, 20, -20, 5)
    ext = st.copy()
    ext.pelvis.z += 0.03
    arms(ext, -65, -25, 20)
    for s in "LR":
        ext.feet[s][0].z = ANKLE_Z + 0.09
        ext.feet[s][1] = 35.0
    air_a, air_b = st.copy(), st.copy()
    for P, (zl, zr, yl, yr) in ((air_a, (0.30, 0.20, -0.12, 0.10)), (air_b, (0.24, 0.30, -0.02, -0.14))):
        arms(P, -25, -40, 35)
        P.lean = 6
        P.feet["L"][0].z, P.feet["R"][0].z = ANKLE_Z + zl, ANKLE_Z + zr
        P.feet["L"][0].y += yl
        P.feet["R"][0].y += yr
    C["Jump_Start"] = (sequence([(0, pre), (1, ext)], 4 * K), False, {})
    C["Jump_Air"] = (sequence([(0, air_a), (0.5, air_b), (1, air_a)], 15 * K), True, {})
    land = st.copy()
    land.pelvis.z -= 0.30
    land.lean = 25
    arms(land, -30, -50, 15)
    C["Jump_Land"] = (sequence([(0, air_a), (0.3, land), (1, st)], 8 * K), False, {})
    return C, stand_eye, anchor_y


def eye_stats(rig, poses):
    zs, ys, err = [], [], 0.0
    for P in poses:
        err = max(err, build(rig, P))
        e = rig.eye()
        zs.append(e.z)
        ys.append(e.y)
    return {"eye_z_min": round(min(zs), 3), "eye_z_max": round(max(zs), 3), "eye_z_mean": round(sum(zs) / len(zs), 3),
            "eye_y_mean": round(sum(ys) / len(ys), 3), "ik_reach_error_max_m": round(err, 4)}


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    opt = {argv[i][2:]: argv[i + 1] for i in range(0, len(argv), 2)}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=opt["src"])
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    idle = next(a for a in bpy.data.actions if a.name.split("|")[-1] == "Idle")
    eye_local = None
    if "eye-json" in opt:  # measured eye point in the Head bone's local frame (measure_head_eye.py)
        with open(opt["eye-json"]) as fh:
            eye_local = json.load(fh)[opt["eye-key"]]["eye_in_head_bone_local"]
    rig = Rig(arm, idle, eye_local=eye_local)
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    if "arms-file" in opt:  # JSON file {"Sprint": {"arm_fwd": 0.3, ...}} for tuning
        with open(opt["arms-file"]) as fh:
            ARMS.update(json.load(fh))
    global EYE_BIAS, EYE_STAND, EYE_CROUCH, EYE_SQUAT, EYE_SIT, EYE_SLEEP, SKELETON_SCALE
    EYE_BIAS = float(opt.get("eye-bias", EYE_BIAS))  # = the body's drop below the player origin (measured hover h)
    SKELETON_SCALE = float(opt.get("scale", SKELETON_SCALE))
    # clip eye (unscaled clip space) = (stance eye + bias) / scale, so that on the scaled body the eye is stance eye + bias
    EYE_STAND, EYE_CROUCH, EYE_SQUAT, EYE_SIT, EYE_SLEEP = ((e + EYE_BIAS) / SKELETON_SCALE for e in (1.65, 1.20, 1.00, 0.80, 0.20))
    clips, stand_eye, anchor_y = make_all(rig)
    only = set(opt["only"].split(",")) if "only" in opt else None
    arm.animation_data.action = None
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.render.fps_base = 1.0
    report = {"skeleton_scale": SKELETON_SCALE, "eye_bias": EYE_BIAS, "fps": FPS, "stand_eye_z": round(stand_eye, 3), "eye_anchor_y": round(anchor_y, 3), "clips": {}}
    for name, (poses, loop, info) in clips.items():
        if only and name not in only and not any(name.startswith(o) for o in only):
            continue
        stats = eye_stats(rig, poses)
        act = bpy.data.actions.new(name)
        arm.animation_data.action = act
        for i, P in enumerate(poses):
            build(rig, P)
            rig.key_frame(1 + i)
        act.use_fake_user = True
        arm.animation_data.action = None
        info = dict(info)
        info.update(stats)
        info.update({"frames": len(poses), "duration_s": round((len(poses) - 1) / FPS, 3), "loop": loop})
        report["clips"][name] = info
    os.makedirs(os.path.dirname(opt["out"]), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=opt["out"], use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             bake_anim_step=1.0, path_mode="STRIP")
    with open(opt["report"], "w") as f:
        json.dump(report, f, indent=1)
    print("AUTHORED", len(report["clips"]), "clips ->", opt["out"])


main()
