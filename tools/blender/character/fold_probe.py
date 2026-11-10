"""Read-only independent cross-check of td-49's inversion measure (i) in Unity's PER-CORNER form (primary class) with the face-mean form printed beside it: faces of the Survivor Body whose posed normal is INVERTED against the rest normal carried by the face's blended skin matrix.
blender -b -noaudio -P fold_probe.py -- <Body.fbx> <Authored.fbx> <clip:frame,...|-> [<out.json>] [--rows rows.csv] [--unity-pts "idx:x,y,z;..."]
blender -b -noaudio -P fold_probe.py -- --synthetic-control        (F3s: a scripted 2-bone strip; exits 2 if the control fails)
Rows mode: lines id,clip,frame,cx,cy,cz (hit triangle REST centre, Unity mesh space when --unity-pts is given, else the file's frame). Each row is matched to the nearest rest face (a distance above 1e-4 m = UNCLASSIFIED).
It prints the inversion dot and FOLD (dot < 0) / OK, plus the plain posed-vs-rest dot as information; a sign that disagrees with Unity's at |dot| < 0.05 is UNCLASSIFIED (judged by the reader of the table).
--unity-pts (F2): Unity mesh-space positions of Body vertices (include the cap centre, the last vertex); tries the 48 signed axis permutations x scale 1/100/0.01, needs exactly one within 1e-5 m, converts the rows.
F5: before every evaluation each Knee_L/R_90 key is set to the driver weight s1 = smoothstep(20, 90, knee flex), flex = the driver's angle between (knee - hip) and (ankle - knee) from the posed heads of UpperLeg.X, LowerLeg.X and LowerLeg.X_end (KneeCorrectiveDriver.cs; exit 2 if _end is missing; the Foot-head, LowerLeg-tail and matrix_basis angles are printed too) and a Knee_*_135 key (if the Body has one) to smoothstep(90, 135, flex); the weights are printed.
The cap-control (--cap-control) of the earlier revision 43011c71 is WITHDRAWN (td-49: the neck cap is rigid and turns edge-on, it does not invert)."""
import itertools
import json
import sys

import bpy
import numpy as np


def smooth(x, lo, hi):
    t = min(max((x - lo) / (hi - lo), 0.0), 1.0)
    return t * t * (3 - 2 * t)


def face_normals(P, F):
    n = np.cross(P[F[:, 1]] - P[F[:, 0]], P[F[:, 2]] - P[F[:, 0]])
    return n / (np.linalg.norm(n, axis=1, keepdims=True) + 1e-12)


class Ctx:
    """Mesh + armature + weights; the one place the measure is computed (real Body and the synthetic strip share it)."""

    def __init__(self, arm, ob):
        self.arm, self.ob, self.me = arm, ob, ob.data
        me = self.me
        nv = len(me.vertices)
        gn = {g.index: g.name for g in ob.vertex_groups}
        self.bones = sorted(set(gn.values()))
        bidx = {b: i for i, b in enumerate(self.bones)}
        self.Wt = np.zeros((nv, len(self.bones)))
        for v in me.vertices:
            for g in v.groups:
                self.Wt[v.index, bidx[gn[g.group]]] = g.weight
        self.F = np.array([tuple(p.vertices) for p in me.polygons if len(p.vertices) == 3])
        self.rest = np.array([v.co[:] for v in me.vertices])
        self.Nrest = face_normals(self.rest, self.F)
        self.missing = [b for b in self.bones if b not in arm.pose.bones]
        self.nonfinite = 0
        print("FOLDPROBE weighted bones %d, falling back to identity (not in the armature): %d %s" % (len(self.bones), len(self.missing), self.missing))
        for pb in arm.pose.bones:
            pb.matrix_basis.identity()
        bpy.context.view_layer.update()

    def evaluate(self):
        ev = self.ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
        m = ev.to_mesh()
        P = np.array([v.co[:] for v in m.vertices])
        ev.to_mesh_clear()
        return P

    def dots(self, P):
        arm, F = self.arm, self.F
        M = np.array([np.array(arm.pose.bones[b].matrix @ arm.data.bones[b].matrix_local.inverted())[:3, :3] if b in arm.pose.bones else np.eye(3) for b in self.bones])
        Mv = np.einsum("vb,bij->vij", self.Wt, M)
        Mf = Mv[F].mean(1)
        Nexp = np.einsum("fij,fj->fi", np.linalg.inv(Mf).transpose(0, 2, 1), self.Nrest)
        Nexp /= np.linalg.norm(Nexp, axis=1, keepdims=True) + 1e-12
        d = (face_normals(P, F) * Nexp).sum(1)
        self.nonfinite += int((~np.isfinite(d)).sum())
        return d, Mf

    def dots_corner(self, P):
        """Unity's measure (BarOneHitStillsClassifier.InversionDot): the rest normal carried by EACH corner's blended skin matrix (M^-T n, unit), the three added and normalised, against the posed unit normal."""
        arm, F = self.arm, self.F
        M = np.array([np.array(arm.pose.bones[b].matrix @ arm.data.bones[b].matrix_local.inverted())[:3, :3] if b in arm.pose.bones else np.eye(3) for b in self.bones])
        Mv = np.einsum("vb,bij->vij", self.Wt, M)
        Minv_T = np.linalg.inv(Mv).transpose(0, 2, 1)
        acc = np.zeros((len(F), 3))
        for j in range(3):
            c = np.einsum("fij,fj->fi", Minv_T[F[:, j]], self.Nrest)
            acc += c / (np.linalg.norm(c, axis=1, keepdims=True) + 1e-300)
        acc /= np.linalg.norm(acc, axis=1, keepdims=True) + 1e-300
        d = (face_normals(P, F) * acc).sum(1)
        self.nonfinite += int((~np.isfinite(d)).sum())
        return d

    def knee_keys(self, tag):
        """F5: set the knee shape keys to the driver weights for the pose now on the armature; print them."""
        sk = self.me.shape_keys
        out = {}
        if not sk:
            return out
        for side in ("L", "R"):
            pb = self.arm.pose.bones.get("LowerLeg." + side)
            if pb is None:
                continue
            hip = np.array(self.arm.pose.bones["UpperLeg." + side].head)
            knee = np.array(pb.head)
            end = self.arm.pose.bones.get("LowerLeg." + side + "_end")
            if end is None:
                print("FOLDPROBE FAIL: no bone LowerLeg.%s_end (the driver's ankle, KneeCorrectiveDriver.cs); import the leaf bones. Not falling back." % side)
                sys.exit(2)
            ankle = np.array(end.head)
            a1, a2 = knee - hip, ankle - knee
            flex = float(np.degrees(np.arccos(np.clip(a1 @ a2 / (np.linalg.norm(a1) * np.linalg.norm(a2) + 1e-12), -1, 1))))   # the driver's Vector3.Angle(knee - hip, ankle - knee), ankle = LowerLeg.X_end
            foot = self.arm.pose.bones.get("Foot." + side)
            foot_flex = float(np.degrees(np.arccos(np.clip(a1 @ (np.array(foot.head) - knee) / (np.linalg.norm(a1) * np.linalg.norm(np.array(foot.head) - knee) + 1e-12), -1, 1)))) if foot is not None else float("nan")
            tail_flex = float(np.degrees(np.arccos(np.clip(a1 @ (np.array(pb.tail) - knee) / (np.linalg.norm(a1) * np.linalg.norm(np.array(pb.tail) - knee) + 1e-12), -1, 1))))
            basis_flex = float(np.degrees(pb.matrix_basis.to_quaternion().angle))
            for key, w in (("Knee_%s_90" % side, smooth(flex, 20, 90)), ("Knee_%s_135" % side, smooth(flex, 90, 135))):
                if key in sk.key_blocks:
                    kb = sk.key_blocks[key]
                    kb.value = w
                    out[key] = round(w, 3)
                    out[key + "_range"] = (kb.slider_min, kb.slider_max, kb.relative_key.name)
            out["flex_driver_" + side] = round(flex, 1)
            out["ankle_bone_" + side] = end.name
            out["flex_LowerLeg_tail_" + side] = round(tail_flex, 1)
            out["flex_Foot_head_" + side] = round(foot_flex, 1)
            out["flex_matrix_basis_" + side] = round(basis_flex, 1)
        print("FOLDPROBE knee keys %s: %s" % (tag, out))
        return out


def synthetic_control():
    """F3s: a strip of 40 quads along X (x -0.5..0.5, width 0.1), bones A (x<0) and B (x>0) meeting at x=0, linear weights across a 0.2 m joint band, preserve volume off, B bent in the XY plane (about its local Z) to 0 / 90 / 170.
    Geometric truth, matrix-free: the strip stays in the XY plane, so a triangle is inverted when its posed signed area (cross z) has the opposite sign to its rest signed area.
    Pass: 0 FOLD at 0; at 170 FOLD exactly on the truth set (no misses, no extras); the count at 90 is printed."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    n = 40
    xs = np.linspace(-0.5, 0.5, n + 1)
    verts = [(x, y, 0.0) for x in xs for y in (0.0, 0.1)]
    tris = []
    for i in range(n):
        bl, tl, br, tr = 2 * i, 2 * i + 1, 2 * i + 2, 2 * i + 3
        tris += [(bl, br, tr), (bl, tr, tl)]
    me = bpy.data.meshes.new("strip")
    me.from_pydata(verts, [], tris)
    me.update()
    ob = bpy.data.objects.new("strip", me)
    bpy.context.scene.collection.objects.link(ob)
    ad = bpy.data.armatures.new("rig")
    arm = bpy.data.objects.new("rig", ad)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="EDIT")
    a = ad.edit_bones.new("A")
    a.head, a.tail = (-0.5, 0, 0), (0, 0, 0)
    b = ad.edit_bones.new("B")
    b.head, b.tail = (0, 0, 0), (0.5, 0, 0)
    b.parent = a
    bpy.ops.object.mode_set(mode="OBJECT")
    ga, gb = ob.vertex_groups.new(name="A"), ob.vertex_groups.new(name="B")
    for i, (x, _, _) in enumerate(verts):
        wb = min(max((x + 0.1) / 0.2, 0.0), 1.0)
        ga.add([i], 1.0 - wb, "REPLACE")
        gb.add([i], wb, "REPLACE")
    ob.parent = arm
    md = ob.modifiers.new("Armature", "ARMATURE")
    md.object = arm
    md.use_deform_preserve_volume = False
    bpy.context.view_layer.update()
    ctx = Ctx(arm, ob)
    ok = True
    for deg in (0, 90, 170):
        arm.pose.bones["B"].rotation_mode = "XYZ"
        arm.pose.bones["B"].rotation_euler = (0, 0, np.radians(deg))
        bpy.context.view_layer.update()
        P = ctx.evaluate()
        d_mean, Mf = ctx.dots(P)
        d = ctx.dots_corner(P)
        fold = set(np.nonzero(d < 0)[0].tolist())
        fold_mean = set(np.nonzero(d_mean < 0)[0].tolist())
        truth = set()
        for k, t in enumerate(ctx.F):
            def area(Q):
                return float((Q[t[1]][0] - Q[t[0]][0]) * (Q[t[2]][1] - Q[t[0]][1]) - (Q[t[1]][1] - Q[t[0]][1]) * (Q[t[2]][0] - Q[t[0]][0]))
            if area(P) * area(ctx.rest) < 0:
                truth.add(k)
        print("SYNTHETIC bend %3d: FOLD by measure (i) per-corner %d of %d triangles, geometric truth %d, misses %d, extras %d | face-mean %d, misses %d, extras %d" % (
            deg, len(fold), len(ctx.F), len(truth), len(truth - fold), len(fold - truth), len(fold_mean), len(truth - fold_mean), len(fold_mean - truth)))
        if deg == 0 and fold:
            ok = False
        if deg == 170 and (fold != truth or not truth):
            ok = False
    print("FOLDPROBE non-finite dots this run (per-corner and face-mean): %d (expected 0)" % ctx.nonfinite)
    ok = ok and ctx.nonfinite == 0
    print("SYNTHETIC CONTROL", "PASS" if ok else "FAIL")
    return ok


def unity_conversion(ctx, spec):
    pts = []
    for part in spec.split(";"):
        i, xyz = part.split(":")
        pts.append((int(i), np.array([float(t) for t in xyz.split(",")])))
    hits = []
    for perm in itertools.permutations(range(3)):
        for sg in itertools.product((1, -1), repeat=3):
            for sc in (1.0, 100.0, 0.01):
                Mx = np.zeros((3, 3))
                for r in range(3):
                    Mx[r, perm[r]] = sg[r] * sc
                err = max(float(np.linalg.norm(Mx @ ctx.rest[i] - u)) for i, u in pts)
                if err <= 1e-5 * max(1.0, sc):
                    hits.append((err, Mx))
    nv = len(ctx.rest)
    print("FRAME PROOF: cap centre (last vertex %d) in the file frame = %s" % (nv - 1, ctx.rest[nv - 1].round(6).tolist()))
    for i, u in pts:
        print("FRAME PROOF point %d: file %s unity %s" % (i, ctx.rest[i].round(6).tolist(), u.round(6).tolist()))
    if len(hits) != 1:
        print("FRAME PROOF %s: %d transforms agree within 1e-5 m (need exactly 1)" % ("AMBIGUOUS" if hits else "FAIL", len(hits)))
        sys.exit(2)
    print("FRAME PROOF OK: unity = M @ file, M = %s (max point error %.2e m)" % (hits[0][1].tolist(), hits[0][0]))
    return np.linalg.inv(hits[0][1])


def main():
    a = sys.argv[sys.argv.index("--") + 1:]
    if "--synthetic-control" in a:
        sys.exit(0 if synthetic_control() else 2)

    def take(name):
        nonlocal a
        if name not in a:
            return None
        v = a[a.index(name) + 1]
        a = [x for i, x in enumerate(a) if x != name and (i == 0 or a[i - 1] != name)]
        return v

    rows_path, unity_pts = take("--rows"), take("--unity-pts")
    body, clips, items = a[0], a[1], a[2].split(",") if a[2] != "-" else []
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=body)
    arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
    ob = [o for o in bpy.data.objects if o.type == "MESH"][0]
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=clips)
    src = next(x for k, x in bpy.data.objects.items() if k not in before and x.type == "ARMATURE")
    src.animation_data_create()
    ctx = Ctx(arm, ob)
    names = [pb.name for pb in arm.pose.bones if pb.name in src.pose.bones]
    F = ctx.F
    dom = np.array([ctx.bones[i] for i in ctx.Wt[F].mean(1).argmax(1)])
    cent = ctx.rest[F].mean(1)
    conv = unity_conversion(ctx, unity_pts) if unity_pts else None

    def pose(cname, fr):
        act = next(x for x in bpy.data.actions if x.name.split("|")[-1].split(".")[0] == cname)
        src.animation_data.action = act
        bpy.context.scene.frame_set(int(fr))
        for n in names:
            arm.pose.bones[n].matrix_basis = src.pose.bones[n].matrix_basis.copy()
        bpy.context.view_layer.update()
        ctx.knee_keys("%s f%s" % (cname, fr))
        bpy.context.view_layer.update()
        return ctx.evaluate()

    out = {}
    for it in items:
        cname, fr = it.split(":")
        d = ctx.dots_corner(pose(cname, fr))
        bad = d < 0
        by = {}
        for b in sorted(set(dom[bad])):
            sel = bad & (dom == b)
            by[b] = {"faces": int(sel.sum()), "rest_centre": cent[sel].mean(0).round(3).tolist(), "rest_z": [round(float(cent[sel][:, 2].min()), 3), round(float(cent[sel][:, 2].max()), 3)], "min_dot": round(float(d[sel].min()), 2)}
        out[it] = {"inverted_faces": int(bad.sum()), "of": int(len(F)), "by_dominant_bone": by}
        print("FOLD %-22s inverted %d of %d  %s" % (it, int(bad.sum()), len(F), {k: (v["faces"], v["rest_z"]) for k, v in by.items()}))
    if len(a) > 3:
        json.dump(out, open(a[3], "w"), indent=1)
    for line in (open(rows_path) if rows_path else []):
        if not line.strip() or line.startswith("#"):
            continue
        rid, cname, fr, cx, cy, cz = line.strip().split(",")[:6]
        c = np.array([float(cx), float(cy), float(cz)])
        if conv is not None:
            c = conv @ c
        i = int(np.linalg.norm(cent - c, axis=1).argmin())
        dist = float(np.linalg.norm(cent[i] - c))
        P = pose(cname, fr)
        d = ctx.dots_corner(P)
        d_mean = float(ctx.dots(P)[0][i])
        plain = float(face_normals(P, F)[i] @ ctx.Nrest[i])
        if dist > 1e-4:
            print("ROW %s %s f%s: UNCLASSIFIED nearest rest centre %.1e m away (> 1e-4)" % (rid, cname, fr, dist))
            continue
        near0 = " (|dot| < 0.05: near zero, a sign that disagrees with Unity here is UNCLASSIFIED)" if abs(d[i]) < 0.05 else ""
        print("ROW %s %s f%s: matched face %d (%s) at %.1e m, per-corner inversion dot %.3f -> %s%s; face-mean dot %.3f; plain posed-vs-rest dot %.3f" % (rid, cname, fr, i, dom[i], dist, d[i], "FOLD" if d[i] < 0 else "OK", near0, d_mean, plain))
    print("FOLDPROBE non-finite dots this run (per-corner and face-mean): %d (expected 0)" % ctx.nonfinite)


main()
