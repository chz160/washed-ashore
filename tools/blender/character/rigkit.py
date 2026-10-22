"""Pose engine for the Quaternius modular-character rig (Blender 5.x, headless).

Own forward kinematics on top of the imported armature (no depsgraph updates), two-bone leg IK,
an 'eye point' on the head bone and a keyframe writer. Axes (armature space, verified on the
pack's Run clip): up +Z, character forward -Y, character left +X. Forward lean = +rotation about X,
a hanging limb swings forward with a NEGATIVE rotation about X.
"""
import math
import bpy
from mathutils import Matrix, Vector

FWD = Vector((0.0, -1.0, 0.0))
UP = Vector((0.0, 0.0, 1.0))
ANKLE_Z = 0.023  # Foot bone head height standing on the floor (pack Idle, inspected)
EYE_UP, EYE_FWD = 0.103, 0.08  # eye point offset from the Head bone head, standing frame [my choice]


def rx(deg):
    return Matrix.Rotation(math.radians(deg), 4, "X")


def ry(deg):
    return Matrix.Rotation(math.radians(deg), 4, "Y")


def smooth(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


class Rig:
    def __init__(self, arm, base_action, base_frame=1, eye_local=None):
        self.arm = arm
        bones = list(arm.data.bones)
        assert arm.matrix_world.is_identity or (arm.matrix_world - Matrix.Identity(4)).median_scale < 1e-6
        self.R = {b.name: b.matrix_local.copy() for b in bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in bones}
        self.length = {b.name: b.length for b in bones}

        def depth(n):
            d = 0
            while self.parent[n]:
                n, d = self.parent[n], d + 1
            return d

        self.order = sorted(self.R, key=depth)
        self.rel = {n: (self.R[self.parent[n]].inverted() @ self.R[n]) if self.parent[n] else self.R[n].copy()
                    for n in self.R}
        if arm.animation_data is None:
            arm.animation_data_create()
        arm.animation_data.action = base_action
        bpy.context.scene.frame_set(base_frame)
        self.base = {n: arm.pose.bones[n].matrix_basis.copy() for n in self.R}
        self.basis = {n: m.copy() for n, m in self.base.items()}
        # fixed standing references, measured from the base pose
        self.base_foot_rot = {}
        for s in ("L", "R"):
            m = self.world("Foot." + s).to_3x3()
            y = m @ Vector((0, 1, 0))
            y.z = 0
            q = y.rotation_difference(FWD)
            self.base_foot_rot[s] = q.to_matrix() @ m
        self.stand_head = self.world("Head")
        eye0 = self.stand_head.translation + Vector((0, -EYE_FWD, EYE_UP))
        # eye point: measured from the Head part mesh (measure_head_eye.py) when given, else the old guess above
        self.eye_local = Vector(eye_local) if eye_local is not None else self.stand_head.inverted() @ eye0
        self.hip_off = (self.world("UpperLeg.L").translation - self.world("Body").translation)
        self.leg1 = self.length["UpperLeg.L"]
        self.leg2 = self.length["LowerLeg.L"]
        self.base_body = self.world("Body").translation.copy()

    # ---- kinematics ----
    def reset(self):
        self.basis = {n: m.copy() for n, m in self.base.items()}

    def world(self, n):
        chain = []
        while n:
            chain.append(n)
            n = self.parent[n]
        M = Matrix.Identity(4)
        for b in reversed(chain):
            M = M @ self.rel[b] @ self.basis[b]
        return M

    def set_world(self, n, M):
        p = self.parent[n]
        Mp = self.world(p) if p else Matrix.Identity(4)
        self.basis[n] = (Mp @ self.rel[n]).inverted() @ M

    def rot(self, n, R):
        """Apply an armature-space rotation to bone n about its head."""
        M = self.world(n)
        self.set_world(n, Matrix.Translation(M.translation) @ R @ M.to_3x3().to_4x4())

    def move(self, n, delta):
        M = self.world(n)
        M.translation = M.translation + delta
        self.set_world(n, M)

    def aim(self, n, direction):
        M = self.world(n)
        y = M.to_3x3() @ Vector((0, 1, 0))
        q = y.rotation_difference(direction)
        self.rot(n, q.to_matrix().to_4x4())

    # ---- pose building ----
    def leg_ik(self, side, A, foot_rot):
        """Two-bone IK: knee points forward. A = ankle target (Foot bone head). Returns reach error."""
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
        n = FWD - u * FWD.dot(u)
        n = n.normalized() if n.length > 1e-6 else FWD
        K = H + u * a + n * b
        self.aim(up, (K - H).normalized())
        self.aim(lo, (H + u * dist - K).normalized())
        M = foot_rot.to_4x4()
        M.translation = A
        self.basis[ft] = self.base[ft].copy()
        self.set_world(ft, M)
        return abs(dist_true - dist)

    def eye(self):
        return self.world("Head") @ self.eye_local

    def foot_rot(self, side, pitch_deg=0.0, base_override=None):
        R = (base_override if base_override is not None else self.base_foot_rot[side]).to_4x4()
        return (rx(pitch_deg) @ R).to_3x3()

    # ---- keyframes ----
    def key_frame(self, frame):
        pbs = self.arm.pose.bones
        for n in self.order:
            pb = pbs[n]
            pb.matrix_basis = self.basis[n]
            pb.keyframe_insert("location", frame=frame, group=n)
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=n)
            pb.keyframe_insert("scale", frame=frame, group=n)
