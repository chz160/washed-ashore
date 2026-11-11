"""Retarget the Quaternius pack clips (Animations.fbx, CC0) onto another skeleton with the same 79 bone names (P4: Skeleton_Human).

blender -b -noaudio -P retarget_pack.py -- --src <tools/blender/character/source/Quaternius_Men_Animations.fbx> --dst <skeleton .fbx, armature only, rest pose>
        --out <retargeted.fbx> --report <json> [--dry-run 1] [--only Idle,Walk] [--break bone|k|reach|moving]

Method (td-49 adr/49-p4-realistic-skeleton section 2, engineer-49 formulation):
  * rotations: every bone takes the WORLD-SPACE rotation delta of the source bone from its rest, applied to the destination rest
    (identical to copying the local rotation when both rests have the same bone axes);
  * Body: moved so that the destination hip centre sits at  M_d_rest + k * (M_src(t) - M_src_rest),  k = hip joint height ratio
    (destination rest / source rest, hip joint height above the floor; the floor is z = 0 in both rests);
  * feet: the sole contact point is  c_d = M_d(t) + k * (c_src(t) - M_src(t))  (hip-relative, so stride follows leg length, in all three axes);
    the new ankle pivot is  A = c_d + Q * (0, 0, a_dst),  Q = the foot's rotation from its rest, a_dst = destination Foot head height at rest
    (the source Foot head, at the floor, is the sole point: its height a_src is taken off first, rotated by Q);
  * legs: re-solved with a two-bone IK (rigkit's, with the knee plane taken from the PT pole instead of forward) onto A and the scaled PT;
  * Shoulder bones keep their (tiny) source location curves; any other moving location curve outside Body/Foot/PT/Shoulder fails the run.
Fails closed: bone set or hierarchy differs, an unexpected moving location curve, the destination shin does not end at the Foot head,
IK reach error or knee position error over the limits, a non-finite value -> nothing is exported.
Deviation from the ADR text, to be ruled by td-49: the ADR scales Foot translation "in the horizontal plane"; here the foot is scaled
hip-relative in all three axes, so the lift of a swing foot scales with k as well (standing on the floor needs the vertical part).
"""
import hashlib
import json
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
if "--rigkit-dir" in sys.argv:   # staged runs: the repo copy of rigkit.py (read-only import)
    sys.path.insert(0, sys.argv[sys.argv.index("--rigkit-dir") + 1])
from rigkit import Rig, FWD  # noqa: E402  (read-only: rigkit belongs to ta-49)

REACH_LIMIT = 0.010      # m, IK reach error (my choice)
FLEX_LIMIT = 10.0        # deg, knee flexion angle of the solved leg vs the source leg (my choice; thigh/shin ratios differ a little)
SOLE_LIMIT = 0.002       # m, lowest sole height vs the source clip (td-49: planted soles z = 0 +- 2 mm)
DROP_LIMIT = 0.020       # m, how far the pelvis may be lowered to keep a foot target in reach (td-49 E3)
DROP_LOCO = 0.030        # m, pelvis drop allowed for the locomotion clips while h is searched (td-49 ruling of record)
FLEX_LOCO = 15.0         # deg, knee flexion difference allowed for the locomotion clips when the stride factor h is searched (td-49)
WORLD_LIMIT_DEG = 0.01   # deg, world orientation of every non-leg bone vs the source clip (td-49 E2)
LOCO = ("Walk", "Run", "Run_Back", "Run_Left", "Run_Right")   # clips that get the horizontal stride factor h (td-49 director stride preference)
ROT_LIMIT_DEG = 0.005    # deg (about 1e-4 rad): absolute local rotation of every non-leg bone vs the template clip (td-49 item 2 rule)
MOVING = 1e-6            # m, a location curve with a range above this moves
FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
ALLOWED_MOVING = ("Body", "Foot.", "PT.", "Shoulder.")
LEGS = ("UpperLeg.L", "UpperLeg.R", "LowerLeg.L", "LowerLeg.R")
SIDES = ("L", "R")


class PoleRig(Rig):
    def leg_ik_pole(self, side, A, foot_rot, pole):
        """rigkit.Rig.leg_ik with the knee plane from a pole point (PT) instead of straight forward. Returns the reach error."""
        up, lo, ft = "UpperLeg." + side, "LowerLeg." + side, "Foot." + side
        self.basis[up] = self.base[up].copy()
        self.basis[lo] = self.base[lo].copy()
        self.basis[lo].translation = Vector((0, 0, 0))
        H = self.world(up).translation
        d = A - H
        dist_true = d.length
        dist = min(max(dist_true, abs(self.leg1 - self.leg2) + 1e-3), (self.leg1 + self.leg2) * 0.9995)
        u = d.normalized()
        a = (self.leg1 ** 2 - self.leg2 ** 2 + dist ** 2) / (2 * dist)
        b = math.sqrt(max(self.leg1 ** 2 - a * a, 0.0))
        n = pole - H
        n = n - u * n.dot(u)
        if n.length < 1e-6:
            n = FWD - u * FWD.dot(u)
        n = n.normalized()
        K = H + u * a + n * b
        self.aim(up, (K - H).normalized())
        self.aim(lo, (H + u * dist - K).normalized())
        M = foot_rot.to_4x4()
        M.translation = A
        self.basis[ft] = self.base[ft].copy()
        self.set_world(ft, M)
        return abs(dist_true - dist)


def parse():
    argv = sys.argv[sys.argv.index("--") + 1:]
    return {argv[i][2:]: argv[i + 1] for i in range(0, len(argv), 2)}


def load(path, tag):
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o.name not in before]
    arm = next(o for o in new if o.type == "ARMATURE")
    for o in new:
        if o is not arm and o.type != "ARMATURE":
            bpy.data.objects.remove(o)
    arm.name = tag
    return arm


def fail(msg):
    print("RETARGET REFUSED:", msg)
    raise SystemExit(2)


def hip_centre(M):
    return (M["UpperLeg.L"].translation + M["UpperLeg.R"].translation) * 0.5


def rest_world(arm):
    return {b.name: b.matrix_local.copy() for b in arm.data.bones}


def structure_check(src, dst):
    sb = {b.name: (b.parent.name if b.parent else None) for b in src.data.bones}
    db = {b.name: (b.parent.name if b.parent else None) for b in dst.data.bones}
    if set(sb) != set(db):
        fail(f"bone sets differ: only in src {sorted(set(sb) - set(db))[:6]}, only in dst {sorted(set(db) - set(sb))[:6]}")
    bad = [n for n in sb if sb[n] != db[n]]
    if bad:
        fail(f"hierarchy differs at {bad[:6]}")
    for arm in (src, dst):
        if not (arm.matrix_world.is_identity or (arm.matrix_world - Matrix.Identity(4)).median_scale < 1e-6):
            fail(f"{arm.name} object transform is not identity (apply it first)")
    for s in SIDES:  # the shin must end at the Foot head (rigkit's IK solves to the Foot head)
        for arm in (src, dst):
            lo, ft = arm.data.bones["LowerLeg." + s], arm.data.bones["Foot." + s]
            gap = (lo.tail_local - ft.head_local).length
            if gap > 0.001:
                fail(f"{arm.name}: LowerLeg.{s} tail is {gap:.4f} m from Foot.{s} head (the ankle pivot must coincide)")


def moving_location_set(src, actions, inject=None):
    """Bones whose basis location moves in any selected action (range above MOVING). Fails on a bone outside the allowed set."""
    moving = {}
    scene = bpy.context.scene
    for act in actions:
        src.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        lo, hi = {}, {}
        for f in range(f0, f1 + 1):
            scene.frame_set(f)
            for pb in src.pose.bones:
                t = pb.matrix_basis.translation
                a, b = lo.setdefault(pb.name, t.copy()), hi.setdefault(pb.name, t.copy())
                for i in range(3):
                    a[i], b[i] = min(a[i], t[i]), max(b[i], t[i])
        for n in lo:
            rng = max((hi[n] - lo[n])[i] for i in range(3))
            if rng > MOVING:
                moving[n] = max(moving.get(n, 0.0), rng)
    if inject:  # forced failure: pretend this bone has a moving location curve
        moving[inject] = 1.0
    bad = [n for n in moving if not n.startswith(ALLOWED_MOVING)]
    if bad:
        fail(f"unexpected moving location curves: {bad}")
    return moving


def plant_runs(zs, band=0.03):
    low = min(zs)
    idx = [i for i, z in enumerate(zs) if z <= low + band]
    runs, cur = [], []
    for i in idx:
        if cur and i != cur[-1] + 1:
            runs.append(cur)
            cur = []
        cur.append(i)
    if cur:
        runs.append(cur)
    return runs


def plant_speed(ys, zs):
    """Ground speed of the left sole over its longest plant run (frames within 3 cm of its lowest height), m per frame x 30."""
    runs = plant_runs(zs)
    best = max(runs, key=len)
    return abs(ys[best[-1]] - ys[best[0]]) / max(len(best) - 1, 1) * 30.0 if len(best) > 1 else 0.0


def plant_z(zs):
    """Largest deviation from the lowest height inside the longest plant run (the planted sole must stay flat on the floor)."""
    best = max(plant_runs(zs), key=len)
    return max(zs[i] for i in best) - min(zs)


def knee_angle(hip, knee, ankle):
    """Interior angle at the knee in degrees (180 = straight)."""
    a, b = hip - knee, ankle - knee
    return math.degrees(a.angle(b)) if a.length > 1e-9 and b.length > 1e-9 else 180.0


REPO = "E:/GitHub/washed-ashore"
GIT = "C:/Program Files/Git/cmd/git.exe"


def sha256(path):
    with open(path, "rb") as fh:
        return hashlib.sha256(fh.read()).hexdigest()


def git_hash(path, raw):
    """git blob hash: raw bytes (--no-filters), or the in-repo form (--path <repo-relative>, cwd = repo) when the file lives in the repo; n/a otherwise."""
    import subprocess
    p = path.replace("\\", "/")
    try:
        if raw:
            return subprocess.run([GIT, "hash-object", "--no-filters", p], capture_output=True, text=True, timeout=60).stdout.strip() or "n/a"
        if p.lower().startswith(REPO.lower() + "/"):
            return subprocess.run([GIT, "hash-object", "--path", p[len(REPO) + 1:], p], cwd=REPO, capture_output=True, text=True, timeout=60).stdout.strip() or "n/a"
        return "n/a (not in the repo)"
    except Exception as e:  # noqa: BLE001
        return f"n/a ({type(e).__name__})"


def finite(v):
    return all(math.isfinite(x) for x in v)


def main():
    opt = parse()
    dry = opt.get("dry-run", "0") == "1"
    brk = opt.get("break", "")
    mode = opt.get("scale-mode", "ankle")   # ankle: scale the ankle (pivot) trajectory by the leg length ratio, knee flexion kept. sole: scale the sole trajectory by the hip height ratio, stride and ground speed kept
    if mode not in ("ankle", "sole"):
        fail("--scale-mode must be ankle or sole")
    fingers = opt.get("fingers", "absolute")   # delta: the source finger pose as a change from its rest, on the destination's rest curl; absolute: copy the orientation
    GAME = "Idle,Idle_Neutral,Walk,Run,Run_Back,Run_Left,Run_Right,Interact"   # the pack clips the game uses (td-49 to confirm); "--only game" selects them
    only = set((GAME if opt.get("only") == "game" else opt["only"]).split(",")) if "only" in opt else None
    bpy.ops.wm.read_factory_settings(use_empty=True)
    src = load(opt["src"], "SRC")
    fps = bpy.context.scene.render.fps   # E4: the output uses the source file's frame rate (30 for the pack), nothing hard-coded
    fps_base = bpy.context.scene.render.fps_base
    if fps != 30 and opt.get("allow-fps") != "1":
        fail(f"source frame rate is {fps}, the pack is 30 (pass --allow-fps 1 to retarget another file)")
    src_actions = list(bpy.data.actions)
    dst = load(opt["dst"], "DST")
    for a in list(bpy.data.actions):
        if a not in src_actions:
            bpy.data.actions.remove(a)
    for arm in (src, dst):
        for pb in arm.pose.bones:
            pb.rotation_mode = "QUATERNION"
    if brk == "bone":  # forced failure: the destination loses a bone name
        dst.data.bones["Wrist.L"].name = "Wrist.Lx"
    structure_check(src, dst)
    actions = [a for a in src_actions if (only is None or a.name.split("|")[-1] in only)]
    if src.animation_data is None:
        src.animation_data_create()
    moving = moving_location_set(src, actions, "Torso" if brk == "moving" else None)
    Sr, Dr = rest_world(src), rest_world(dst)
    leg = lambda arm: arm.data.bones["UpperLeg.L"].length + arm.data.bones["LowerLeg.L"].length   # hip joint to the shin end (= the Foot head)
    k = leg(dst) / leg(src)   # leg length ratio: scales the pelvis motion and the foot and pole trajectories about the hip
    a_src = Sr["Foot.L"].translation.z   # source Foot head above the sole (the floor is z = 0 in the source rest)
    a_dst = Dr["Foot.L"].translation.z   # destination ankle pivot above the sole
    Ms0 = hip_centre(Sr)
    Md0 = hip_centre(Dr)
    print("== retarget", opt["src"], "->", opt["dst"])
    for label, path in (("script", os.path.abspath(__file__)), ("src", opt["src"]), ("dst", opt["dst"])):
        print(f"{label:<6} sha256 {sha256(path)[:16]}  git raw (--no-filters) {git_hash(path, True)}  in-repo (--path) {git_hash(path, False)}  {path}")
    print(f"scale mode {mode}, fingers {fingers}")
    kh = Dr["UpperLeg.L"].translation.z / Sr["UpperLeg.L"].translation.z   # hip joint height ratio (rest)
    if mode == "sole":
        k = kh
    if brk == "k":   # forced failure: the scale is wrong by half
        k *= 0.5
    print(f"leg length (thigh+shin) src {leg(src):.4f} dst {leg(dst):.4f}  k {k:.4f}   hip joint height at rest src {Ms0.z:.4f} dst {Md0.z:.4f}   foot pivot height src {a_src:.4f} dst {a_dst:.4f}")
    print("moving location bones in the selected clips:", {n: round(v, 4) for n, v in sorted(moving.items())})
    rest_diff = {n: math.degrees(Sr[n].to_3x3().to_quaternion().rotation_difference(Dr[n].to_3x3().to_quaternion()).angle) for n in Sr}
    worst = sorted(rest_diff.items(), key=lambda kv: -kv[1])[:6]
    print("rest orientation difference src vs dst (deg), largest six:", [(n, round(a, 2)) for n, a in worst])
    body = sorted(((n, a) for n, a in rest_diff.items() if not n.startswith(("Thumb", "Index", "Middle", "Ring", "Pinky"))), key=lambda kv: -kv[1])[:6]
    print("same, non-finger bones:", [(n, round(a, 2)) for n, a in body])
    rel = lambda R, b: (R[b.parent.name].to_3x3().inverted() @ R[b.name].to_3x3()).to_quaternion() if b.parent else R[b.name].to_3x3().to_quaternion()
    reldiff = {b.name: math.degrees(rel(Sr, b).rotation_difference(rel(Dr, dst.data.bones[b.name])).angle) for b in src.data.bones}
    print("rest LOCAL rotation difference (bone relative to its parent), largest non-finger:",
          [(n, round(a, 1)) for n, a in sorted(((n, a) for n, a in reldiff.items() if not n.startswith(FINGERS)), key=lambda kv: -kv[1])[:8]])

    # destination rig (own FK, no depsgraph): base pose = rest
    rest_act = bpy.data.actions.new("REST")
    if dst.animation_data is None:
        dst.animation_data_create()
    rig = PoleRig(dst, rest_act, base_frame=1)
    scene = bpy.context.scene
    report = {"src": opt["src"], "dst": opt["dst"], "scale_mode": mode, "k": k, "a_src": a_src, "a_dst": a_dst, "moving": moving, "clips": {},
              "licence": "Quaternius Ultimate Modular Men pack clips (tools/blender/character/source/Quaternius_Men_Animations.fbx), CC0 1.0 (Assets/ThirdParty/Quaternius/LICENSE_CC0.txt)",
              "limits": {"reach_m": REACH_LIMIT, "flex_deg": FLEX_LIMIT, "drop_m": DROP_LIMIT, "rot_deg": ROT_LIMIT_DEG}}
    out_actions = []

    def solve(act, h, key):
        """One clip at horizontal stride factor h (vertical stays k). Returns the stats; keys the frames into the destination action when key is True."""
        hx, kv = (h, k) if mode == "ankle" else (k, k)
        src.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        travel_src, travel_dst, sole_src, sole_dst = [], [], [], []
        stats = dict(world_bone="", frames=f1 - f0 + 1, h=round(h, 4), loc_off=0.0, reach=0.0, flex=0.0, drop=0.0, rot=0.0, world=0.0, sole_min=1e9, sole_src_min=1e9, pelvis_z=[1e9, -1e9])
        for f in range(f0, f1 + 1):
            scene.frame_set(f)
            S = {pb.name: pb.matrix.copy() for pb in src.pose.bones}
            Sb = {pb.name: pb.matrix_basis.translation.copy() for pb in src.pose.bones}
            Ms = hip_centre(S)
            rig.reset()
            for n in rig.order:   # body bones: the source's world orientation (same pose); fingers: absolute by default (the delta form is optional)
                if n.startswith(FINGERS) and fingers == "delta":
                    Wd = (S[n].to_3x3().to_quaternion().to_matrix() @ Sr[n].to_3x3().inverted()) @ Dr[n].to_3x3()
                else:
                    Wd = S[n].to_3x3().to_quaternion().to_matrix()
                if brk == "world" and n == "Head":   # forced failure: one bone is off by 5 degrees
                    Wd = Matrix.Rotation(math.radians(5), 3, "X") @ Wd
                M = rig.world(n)
                Mn = Wd.to_4x4()
                Mn.translation = M.translation
                rig.set_world(n, Mn)
            sc = lambda v: Vector((v.x * hx, v.y * hx, v.z * kv))   # horizontal by hx, vertical by kv
            if mode == "ankle":   # the pelvis stands a_dst + k * (height above the source pivot standing level)
                d = Ms - Ms0
                target = Vector((Md0.x + hx * d.x, Md0.y + hx * d.y, a_dst + kv * (Ms.z - a_src)))
            else:                 # sole mode: the pelvis moves k times its source motion from the destination's rest hip
                target = Md0 + (Ms - Ms0) * k
            now = hip_centre({n: rig.world(n) for n in LEGS})
            rig.move("Body", target - now)
            Mh = hip_centre({n: rig.world(n) for n in LEGS})
            feet = {}
            for sd in SIDES:
                ft = "Foot." + sd
                Q = S[ft].to_3x3().to_quaternion().to_matrix() @ Sr[ft].to_3x3().to_quaternion().to_matrix().inverted()
                c_src = S[ft].translation - Q @ Vector((0, 0, a_src))     # the sole point under the source pivot
                if mode == "ankle":
                    A = Mh + sc(S[ft].translation - Ms)                     # the ankle pivot follows the source pivot, scaled about the hip
                    c_dst = A - Q @ Vector((0, 0, a_dst))                   # the sole point under the new ankle (floor contact check)
                else:
                    c_dst = Mh + (c_src - Ms) * k                           # the sole point follows the source sole, scaled about the hip
                    A = c_dst + Q @ Vector((0, 0, a_dst))                   # the ankle sits a_dst above it in the foot's own frame
                if sd == "L":
                    travel_src.append(c_src.y)
                    travel_dst.append(c_dst.y)
                    sole_src.append(c_src.z)
                    sole_dst.append(c_dst.z)
                if not finite(A):
                    fail(f"{act.name} frame {f}: non-finite foot target")
                pole = Mh + (sc(S["PT." + sd].translation - Ms) if mode == "ankle" else (S["PT." + sd].translation - Ms) * k)
                feet[sd] = (A, Q @ Dr[ft].to_3x3().to_quaternion().to_matrix(), pole)
                stats["sole_min"] = min(stats["sole_min"], c_dst.z)
                stats["sole_src_min"] = min(stats["sole_src_min"], c_src.z)
            lmax = (rig.leg1 + rig.leg2) * 0.9995     # a foot beyond reach: lower the pelvis (feet stay planted) instead of lifting the foot
            drop = 0.0
            for _ in range(8):
                over = max((feet[sd][0] - rig.world("UpperLeg." + sd).translation).length - lmax for sd in SIDES)
                if over <= 1e-5:
                    break
                rig.move("Body", Vector((0, 0, -over * 1.25)))
                drop += over * 1.25
            stats["drop"] = max(stats["drop"], drop)
            for sd in SIDES:
                A, foot_rot, pole = feet[sd]
                stats["reach"] = max(stats["reach"], rig.leg_ik_pole(sd, A, foot_rot, pole))
                pt = rig.world("PT." + sd)
                pt.translation = pole
                rig.set_world("PT." + sd, pt)
                stats["flex"] = max(stats["flex"], abs(knee_angle(rig.world("UpperLeg." + sd).translation, rig.world("LowerLeg." + sd).translation, rig.world("Foot." + sd).translation)
                                                       - knee_angle(S["UpperLeg." + sd].translation, S["LowerLeg." + sd].translation, S["Foot." + sd].translation)))
            for n in moving:     # tiny shoulder curves are copied as they are
                if n.startswith("Shoulder."):
                    rig.basis[n].translation = Sb[n]
            for n in rig.order:  # td-49: absolute local rotation (item 2 rule) and WORLD orientation (E2) of every non-IK bone equal the template clip's
                if n.startswith(("UpperLeg", "LowerLeg", "Foot.", "PT.")):   # the IK-solved bones and their end bones
                    continue
                par = rig.parent[n]
                wn, ws = rig.world(n).to_3x3(), S[n].to_3x3().to_quaternion().to_matrix()
                dl = (rig.world(par).to_3x3().inverted() if par else Matrix.Identity(3)) @ wn
                sl = (S[par].to_3x3().to_quaternion().to_matrix().inverted() if par else Matrix.Identity(3)) @ ws
                stats["rot"] = max(stats["rot"], math.degrees(dl.to_quaternion().rotation_difference(sl.to_quaternion()).angle))
                wd = math.degrees(wn.to_quaternion().rotation_difference(ws.to_quaternion()).angle)
                if wd > stats["world"]:
                    stats["world"], stats["world_bone"] = wd, n
            for n in rig.order:  # td N3: every bone's location is keyed; a non-moving bone must key the destination's rest offset (basis 0)
                if n == "Body" or n.startswith(ALLOWED_MOVING) or n in moving:
                    continue
                stats["loc_off"] = max(stats["loc_off"], rig.basis[n].translation.length)
            z = rig.world("Body").translation.z
            stats["pelvis_z"] = [min(stats["pelvis_z"][0], z), max(stats["pelvis_z"][1], z)]
            if key:
                rig.key_frame(f)
        stats["travel_src"], stats["travel_dst"] = max(travel_src) - min(travel_src), max(travel_dst) - min(travel_dst)   # fore-aft sole travel, left foot
        stats["plant_speed_src"], stats["plant_speed_dst"] = plant_speed(travel_src, sole_src), plant_speed(travel_dst, sole_dst)
        stats["plant_z_src"], stats["plant_z_dst"] = plant_z(sole_src), plant_z(sole_dst)
        for key_ in ("loc_off", "reach", "flex", "drop", "rot", "world", "sole_min", "sole_src_min", "travel_src", "travel_dst", "plant_speed_src", "plant_speed_dst", "plant_z_src", "plant_z_dst"):
            stats[key_] = round(stats[key_], 5)
        stats["pelvis_z"] = [round(v, 4) for v in stats["pelvis_z"]]
        return stats

    rot_lim = ROT_LIMIT_DEG if fingers == "absolute" else 1e9   # the residual is 0 by construction only when fingers are copied as absolute rotations

    flex_loco = float(opt.get("flex-loco", FLEX_LOCO))   # test hook: the locomotion knee gate (forced-failure cases use 5 and 0.1)

    def why(st, name):
        loco = name.split("|")[-1] in LOCO
        flex_lim = (flex_loco if loco else FLEX_LIMIT) if mode == "ankle" else 1e9   # in sole mode the knee angle differs by design: information only
        bad = []
        if st["loc_off"] > 1e-6: bad.append("loc_off")
        if st["reach"] > REACH_LIMIT: bad.append("reach")
        if st["flex"] > flex_lim: bad.append("flex")
        if st["drop"] > (DROP_LOCO if loco and mode == "ankle" else DROP_LIMIT): bad.append("drop")
        if st["rot"] > rot_lim: bad.append("rot")
        if st["world"] > WORLD_LIMIT_DEG and fingers == "absolute": bad.append("world orientation")
        if abs(st["sole_min"] - st["sole_src_min"]) > SOLE_LIMIT: bad.append("sole z vs source")   # td-49: planted soles stay on the floor within 2 mm
        if mode == "sole" and st["plant_speed_src"] > 0.3 and abs(st["plant_speed_dst"] / (st["plant_speed_src"] * k) - 1) > 0.03:
            bad.append("plant speed != source x k")
        if mode == "ankle" and loco and st["plant_speed_src"] > 0.3 and st["plant_speed_dst"] < st["plant_speed_src"] * st["h"] * 0.97:
            bad.append("plant speed < source x h")   # the stride must be at least what h claims (it is typically 3-5 percent longer: the ankle offset rotates with the foot)
        return bad

    for act in actions:
        short = act.name.split("|")[-1]
        new = bpy.data.actions.new(act.name)
        dst.animation_data.action = new
        h = k
        limit_by = []
        if mode == "ankle" and short in LOCO:   # largest horizontal stride factor in [k, 1] that meets the gates (td-49 director stride preference)
            for hs in (k, 0.90, 0.95, 1.0):   # td-49: the sweep, printed per clip
                sw = solve(act, hs, False)
                print(f"  sweep {short:<10} h {hs:.3f}: reach {sw['reach']:.4f} flex {sw['flex']:5.1f} drop {sw['drop']:.3f} sole dz {abs(sw['sole_min'] - sw['sole_src_min']) * 1000:4.1f} mm  "
                      f"plant speed {sw['plant_speed_dst']:.3f} / src {sw['plant_speed_src']:.3f} = {sw['plant_speed_dst'] / max(sw['plant_speed_src'], 1e-9):.3f} (h {hs:.3f})  -> "
                      f"{','.join(why(sw, act.name)) or 'passes'}")
            h = None
            cand = 1.0
            while cand >= k - 1e-9:
                st = solve(act, cand, False)
                bad = [w for w in why(st, act.name) if w in ("reach", "flex", "drop", "sole z vs source", "plant speed < source x h")]
                if not bad:
                    h = cand
                    break
                limit_by = bad   # the gate(s) that stopped the next larger h
                cand -= 0.01
            if h is None:
                h = k   # nothing larger passes: the plain k (the gates below then decide)
        dst.animation_data.action = new
        stats = solve(act, h, True)
        new.use_fake_user = True
        out_actions.append(new)
        stats["h_limited_by"] = limit_by if h < 1.0 - 1e-9 else []
        report["clips"][act.name] = stats
        print(f"{short:<18} frames {stats['frames']:>3}  h {stats['h']:.3f}  locoff {stats['loc_off']:.1e}  reach {stats['reach']:.4f}  flex {stats['flex']:.1f}  drop {stats['drop']:.3f}  rot {stats['rot']:.3f} / world {stats['world']:.4f} deg ({stats['world_bone']})  "
              f"sole z min dst {stats['sole_min']:+.4f} / src {stats['sole_src_min']:+.4f}  pelvis z {stats['pelvis_z']}  travel {stats['travel_src']:.3f}/{stats['travel_dst']:.3f}  plant speed {stats['plant_speed_src']:.3f}/{stats['plant_speed_dst']:.3f} m/s  plant z span {stats['plant_z_src']:.4f}/{stats['plant_z_dst']:.4f}")
    problems = [f"{n}: " + ", ".join(why(st, n)) for n, st in report["clips"].items() if why(st, n)]
    # playback-rate table (producer-49/td-49): stride scale rho = planted-sole ground speed new / pack; at the unchanged controller nodes the rate becomes (band edge) / rho
    print("\nPLAYBACK RATE TABLE (planted-sole ground speed, left foot; controller nodes unchanged; limit 1.4)")
    print(f"{'clip':<12}{'h':>6}{'speed src':>11}{'speed dst':>11}{'rho':>7}{'stride dst':>12}   rate at the band edges            verdict")
    rates = {}
    for n, st in report["clips"].items():
        short = n.split("|")[-1]
        if short not in LOCO or st["plant_speed_src"] <= 0.3:
            continue
        rho = st["plant_speed_dst"] / st["plant_speed_src"]
        edges = (0.7, 1.4) if short == "Walk" else ((0.7, 1.0) if short in ("Run_Back", "Run_Left", "Run_Right") else (1.0, 1.0))
        lo, hi = edges[0] / rho, edges[1] / rho
        verdict = "OVER 1.4" if hi > 1.4 + 1e-9 else "ok"
        rates[short] = dict(h=st["h"], limited_by=st.get("h_limited_by", []), rho=round(rho, 4), rate_lo=round(lo, 3), rate_hi=round(hi, 3), stride=st["travel_dst"], verdict=verdict)
        print(f"{short:<12}{st['h']:>6.3f}{st['plant_speed_src']:>11.3f}{st['plant_speed_dst']:>11.3f}{rho:>7.3f}{st['travel_dst']:>12.3f}   {lo:.3f} .. {hi:.3f}   {verdict}   h limited by: {','.join(st.get('h_limited_by', [])) or '-'}")
    report["rates"] = rates
    report["problems"] = problems
    if problems:
        with open(opt["report"], "w") as fh:
            json.dump(report, fh, indent=1)
        fail("; ".join(problems))
    if dry:
        with open(opt["report"], "w") as fh:
            json.dump(report, fh, indent=1)
        print("DRY RUN OK: nothing exported;", len(report["clips"]), "clips checked")
        return
    try:
        dst.animation_data.action = None
        for pb in dst.pose.bones:   # the exporter writes the current pose as the default transform: leave the armature at its rest pose
            pb.matrix_basis = Matrix.Identity(4)
        for a in list(bpy.data.actions):
            if a not in out_actions:
                bpy.data.actions.remove(a)
        bpy.context.scene.render.fps = fps
        bpy.context.scene.render.fps_base = fps_base
        os.makedirs(os.path.dirname(opt["out"]), exist_ok=True)
        bpy.ops.object.select_all(action="DESELECT")
        dst.select_set(True)
        bpy.context.view_layer.objects.active = dst
        bpy.ops.export_scene.fbx(filepath=opt["out"], use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                                 bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                                 bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, bake_anim_step=1.0, path_mode="STRIP")
        if not os.path.isfile(opt["out"]) or os.path.getsize(opt["out"]) < 1000:
            raise RuntimeError("the export wrote no usable file")
        report["out_sha256"] = sha256(opt["out"])
        report["out_git_raw"] = git_hash(opt["out"], True)
        with open(opt["report"], "w") as fh:
            json.dump(report, fh, indent=1)
    except BaseException as e:  # noqa: BLE001  (td-49 E5: no half-written FBX is left behind)
        if os.path.isfile(opt["out"]):
            os.remove(opt["out"])
        print("EXPORT FAILED, FBX removed:", type(e).__name__, e)
        sys.exit(3)
    print("RETARGETED", len(out_actions), "clips ->", opt["out"], "sha256", report["out_sha256"][:16], "git raw", report["out_git_raw"])


main()
