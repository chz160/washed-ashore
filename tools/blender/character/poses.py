"""Pose parameters, pose building, eye fitting and the gait generator (see rigkit.py for axes)."""
import math
from mathutils import Vector
from rigkit import Rig, rx, ry, smooth, ANKLE_Z, FWD


class Pose:
    def __init__(self, rig):
        b = rig.base_body
        fx = abs(rig.world("Foot.L").translation.x)
        self.pelvis = Vector((b.x, b.y, b.z))
        self.body_rx = 0.0
        self.lean = 0.0
        self.head_extra = 0.0
        self.breath = 0.0
        self.feet = {"L": [Vector((fx, b.y, ANKLE_Z)), 0.0], "R": [Vector((-fx, b.y, ANKLE_Z)), 0.0]}
        self.arm = {"L": [0.0, 0.0, 0.0], "R": [0.0, 0.0, 0.0]}  # swing, elbow, abduct (deg)
        self.arm_frame_torso = True  # arm_abs shoulder angles are measured in the torso frame (False: the world, vertical frame)
        self.arm_abs = {"L": None, "R": None}  # optional (shoulder sagittal angle, elbow flexion) targets in degrees; override swing and elbow
        self.foot_x = fx

    def copy(self):
        o = Pose.__new__(Pose)
        o.pelvis = self.pelvis.copy()
        o.body_rx, o.lean, o.head_extra, o.breath, o.foot_x = self.body_rx, self.lean, self.head_extra, self.breath, self.foot_x
        o.feet = {s: [v[0].copy(), v[1]] for s, v in self.feet.items()}
        o.arm = {s: list(v) for s, v in self.arm.items()}
        o.arm_abs = dict(self.arm_abs)
        o.arm_frame_torso = self.arm_frame_torso
        return o


def lerp_pose(a, b, t):
    o = a.copy()
    o.pelvis = a.pelvis.lerp(b.pelvis, t)
    for k in ("body_rx", "lean", "head_extra", "breath"):
        setattr(o, k, getattr(a, k) * (1 - t) + getattr(b, k) * t)
    for s in "LR":
        o.feet[s] = [a.feet[s][0].lerp(b.feet[s][0], t), a.feet[s][1] * (1 - t) + b.feet[s][1] * t]
        o.arm[s] = [a.arm[s][i] * (1 - t) + b.arm[s][i] * t for i in range(3)]
    return o


def shoulder_angle(d, up=None, fwd=None):
    """Sagittal angle of an arm direction: 0 hanging, positive forward (the rigs face -Y). With up and fwd the torso's own axes, the angle is
    measured in the TORSO frame, so the forward lean does not tip the swing."""
    if up is None:
        return math.degrees(math.atan2(-d.y, -d.z))
    return math.degrees(math.atan2(d.dot(fwd), -d.dot(up)))


def torso_axes(rig):
    """Up (hips to neck) and forward (the sagittal forward axis made perpendicular to it) of the torso in the current pose."""
    up = (rig.world("Neck").translation - rig.world("Hips").translation).normalized()
    fwd = Vector((0.0, -1.0, 0.0))
    fwd = (fwd - up * fwd.dot(up)).normalized()
    return up, fwd


def pick(err_of):
    """The rotation angle (degrees, 0.25 steps) with the smallest error; among near-equal solutions the smallest rotation, so the choice never jumps branches."""
    errs = [(err_of(i * 0.25), i * 0.25) for i in range(-720, 721)]
    lo = min(e for e, _ in errs)
    return min((abs(a), a) for e, a in errs if e <= lo + 0.3)[1]


def solve_arm(rig, side, shoulder_deg, elbow_deg, torso_frame=True):
    """Rotate the upper arm about the lateral axis until its sagittal angle is shoulder_deg, then the forearm until the elbow flexion is
    elbow_deg (0 = straight). Both are measured, not assumed, so left and right match whatever the rest pose is."""
    up, lo, wr = "UpperArm." + side, "LowerArm." + side, "Wrist." + side
    d = rig.world(lo).translation - rig.world(up).translation
    t_up, t_fwd = torso_axes(rig) if torso_frame else (None, None)
    rig.rot(up, rx(pick(lambda a: abs(shoulder_angle(rx(a).to_3x3() @ d, t_up, t_fwd) - shoulder_deg))))
    el = rig.world(lo).translation
    u = rig.world(up).translation
    w = rig.world(wr).translation
    rig.rot(lo, rx(pick(lambda a: abs(180.0 - math.degrees((u - el).angle(el + rx(a).to_3x3() @ (w - el) - el)) - elbow_deg))))


def build(rig, P):
    """Pose the rig from P. Returns max IK reach error (m)."""
    rig.reset()
    M0 = rig.world("Body")
    new = rx(P.body_rx) @ M0.to_3x3().to_4x4()
    new.translation = P.pelvis
    rig.set_world("Body", new)
    L = P.lean
    rig.rot("Abdomen", rx(0.25 * L))
    rig.rot("Torso", rx(0.30 * L))
    rig.rot("Chest", rx(0.25 * L + P.breath))
    rig.rot("Neck", rx(-0.20 * L))
    rig.rot("Head", rx(-0.35 * L + P.head_extra))
    for s, sign in (("L", -1.0), ("R", 1.0)):
        swing, elbow, abd = P.arm[s]
        target = P.arm_abs.get(s)
        if target is None:
            rig.rot("UpperArm." + s, rx(swing) @ ry(sign * abd))
            rig.rot("LowerArm." + s, rx(elbow))
        else:
            solve_arm(rig, s, target[0], target[1], P.arm_frame_torso)
    err = 0.0
    lying = rx(P.body_rx).to_3x3()
    for s in "LR":
        A, pitch = P.feet[s]
        base = lying @ rig.base_foot_rot[s]
        err = max(err, rig.leg_ik(s, A, rig.foot_rot(s, pitch, base)))
    return err


def fit_eye(rig, P, target_z, anchor_y, vary="z", lean_range=(0.0, 50.0)):
    """Shift the pelvis (y always; z or lean as chosen) until the eye is at target_z / anchor_y."""
    if vary == "z":
        for _ in range(6):
            build(rig, P)
            e = rig.eye()
            P.pelvis.z += target_z - e.z
            P.pelvis.y += anchor_y - e.y
    else:
        lo, hi = lean_range
        for _ in range(40):
            P.lean = 0.5 * (lo + hi)
            build(rig, P)
            if rig.eye().z > target_z:
                lo = P.lean
            else:
                hi = P.lean
        build(rig, P)
        if rig.eye().z < target_z - 0.002:  # upright is still too low (shorter rig): lift the pelvis
            P.pelvis.z += target_z - rig.eye().z
        P.pelvis.y += anchor_y - rig.eye().y
    build(rig, P)
    return rig.eye()


def stand_pose(rig, k=0.975):
    P = Pose(rig)
    lat = abs(rig.hip_off.x - P.foot_x)
    hz = ANKLE_Z + math.sqrt(max((k * (rig.leg1 + rig.leg2)) ** 2 - lat * lat, 0))
    P.pelvis.z = hz - rig.hip_off.z
    return P


def gait(rig, N, sf, S, direction, lean, swing, elbow, lift, k, bob, anchor_y,
         eye_target=None, lat_sep=0.12, name="", fps=30, arm_fwd=1.0, abduct=0.0, arm_abs=None):
    """Returns (poses[N+1], info). N = frames per cycle at `fps`. The stance foot moves back at S/(sf*N/fps) m/s exactly, and the first
    and last swing samples keep moving with the ground while the foot is already above the plant band, so the foot does not slide
    in the 3 cm band around each plant. direction: unit (dx, dy) movement direction in armature space; forward is (0, -1)."""
    D = Vector((direction[0], direction[1], 0.0))
    T = N / float(fps)
    base = Pose(rig)
    fx = base.foot_x
    lat = abs(rig.hip_off.x - fx)
    hz = ANKLE_Z + math.sqrt(max((k * (rig.leg1 + rig.leg2)) ** 2 - lat * lat, 0))
    zc = hz - rig.hip_off.z
    fwdness = max(-D.y, 0.0)
    lean_eff = lean * (fwdness + 0.3 * (1 - fwdness))
    step = S / (sf * N)  # metres the stance foot travels per frame

    def foot_f(ph):  # continuous model, used to size the pelvis
        if ph < sf:
            return S / 2 - S * (ph / sf), 0.0
        u = (ph - sf) / (1 - sf)
        return -S / 2 + S * smooth(u), lift * math.sin(math.pi * u) ** 0.6

    def foot_samples(off):
        ph = [((i / N) + off) % 1.0 for i in range(N)]
        F = [foot_f(x)[0] for x in ph]
        Z = [foot_f(x)[1] for x in ph]
        swing_idx = [i for i in range(N) if ph[i] >= sf]
        ks = next(i for i in swing_idx if (i - 1) % N not in swing_idx)
        km = next(i for i in swing_idx if (i + 1) % N not in swing_idx)
        F[ks] = F[(ks - 1) % N] - step   # lifts off still moving back with the ground
        F[km] = F[(km + 1) % N] + step   # lands moving back with the ground
        n = (km - ks) % N
        f_a, f_b = F[ks], F[km]
        for j in range(1, n):
            i = (ks + j) % N
            F[i] = f_a + (f_b - f_a) * smooth(j / n)
        for i in (ks, km):
            Z[i] = max(Z[i], 0.07)  # clear of the 3 cm plant band by the first and last swing sample
        return F + [F[0]], Z + [Z[0]]

    arrays = {"L": foot_samples(0.0), "R": foot_samples(0.5)}

    def make(ph, Y, ZC, i=None):
        P = base.copy()
        P.lean = lean_eff
        P.pelvis = Vector((base.pelvis.x, Y, ZC + bob * (1 - math.cos(4 * math.pi * (ph - sf / 2))) / 2))
        gs = {}
        for s, off, x0, sepsign in (("L", 0.0, fx, 1.0), ("R", 0.5, -fx, -1.0)):
            f, z = foot_f((ph + off) % 1.0) if i is None else (arrays[s][0][i], arrays[s][1][i])
            gs[s] = f
            P.feet[s] = [Vector((x0 + D.x * f, Y + D.y * f + sepsign * lat_sep * abs(D.x), ANKLE_Z + z)), 0.0]
        P.arm["L"] = [0.0, elbow, abduct]
        P.arm["R"] = [0.0, elbow, abduct]
        return P, gs

    # left arm swings forward (negative about X) when the RIGHT foot is ahead (positive f)
    Y = base.pelvis.y
    mid = sf / 2
    for _ in range(3):
        P, _g = make(mid, Y, zc)
        build(rig, P)
        e = rig.eye()
        Y += anchor_y - e.y
        if eye_target is not None:
            zc += eye_target - bob / 2 - e.z
    poses = []
    a_amp = swing * (abs(D.y) + 0.4 * abs(D.x))
    # arm_abs = dict(centre, amplitude, elbow, elbow_swing): shoulder angle = centre + amplitude * (opposite foot's travel / half stance), elbow
    # flexion = elbow + elbow_swing * (the arm's own forwardness, -1 back to +1 forward). Both measured angles, symmetrical left and right.
    for i in range(N + 1):
        P, gs = make((i % N) / N, Y, zc, i)
        if arm_abs:
            P.arm_frame_torso = arm_abs.get("torso_frame", True)
        for side, other in (("L", "R"), ("R", "L")):
            v = -a_amp * (gs[other] / (S / 2))
            P.arm[side][0] = v * arm_fwd if v < 0 else v  # negative = forward swing: scaled down so the hands stay out of the first-person view
            if arm_abs:
                g = gs[other] / (S / 2)  # the opposite foot's forwardness: -1 .. +1
                P.arm_abs[side] = (arm_abs["centre"] + arm_abs["amplitude"] * g, arm_abs["elbow"] + arm_abs["elbow_swing"] * g)
        poses.append(P)
    info = {"cycle_s": T, "stance_s": sf * T, "stance_travel_m": S, "clip_speed_mps": S / (sf * T),
            "eye_mean_z": None, "pelvis_y": Y, "zc": zc}
    return poses, info
