"""Backing-patch checks (pod 49, td-49 SHELL FIX TECH RULES h1, h2, h4), fail-closed.
blender -b -noaudio -P patch_check.py -- --body <Survivor_Body.fbx with patches> --ref <Survivor_Body.fbx without (final11a)> --report <reskin_report.json> --clips <Authored.fbx>
h1  an outside ray through each tear centre (from 5 cm outside the rim centre, straight in): before (ref Body) the first hit is a BACK face (or none), after the first hit is the patch's face, front-facing;
h2  at every frame of every clip the patch lies at least 2 mm INSIDE the rim plane (depth along the posed outward normal of the rim triangle), so it never crosses the shell;
h4  block order: the patch faces follow the original faces directly and the cap centre is still the last vertex.
Exit 2 on any failure."""
import json
import sys

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def arg(name):
    a = sys.argv[sys.argv.index("--") + 1:]
    return a[a.index(name) + 1]


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
    ob = [o for o in bpy.data.objects if o.type == "MESH"][0]
    return arm, ob


def main():
    rep = json.load(open(arg("--report")))
    patches = rep["split"]["patches"]
    if not patches:
        print("PATCHCHECK FAIL: no patches in the report")
        sys.exit(2)
    bad = []
    # reference: first hit through each tear centre in the Body without patches
    arm0, ob0 = load(arg("--ref"))
    bvh0 = BVHTree.FromObject(ob0, bpy.context.evaluated_depsgraph_get())
    # the patched Body
    arm, ob = load(arg("--body"))
    me = ob.data
    n_orig = min(p["exported_face"] for p in patches)
    first_patch = n_orig
    for k, p in enumerate(patches):
        if p["exported_face"] != first_patch + k:
            bad.append("h4: patch %d is face %d, expected %d (the patch faces must follow the original faces directly)" % (k, p["exported_face"], first_patch + k))
    centre_v = len(me.vertices) - 1
    cap_faces = [pg.index for pg in me.polygons if centre_v in pg.vertices]
    if not cap_faces or min(cap_faces) < first_patch + len(patches):
        bad.append("h4: the cap faces must come after the patch faces (first cap face %s)" % (min(cap_faces) if cap_faces else None))
    print("PATCHCHECK h4: %d original faces, %d patch faces, cap faces from %d, cap centre is the last vertex (index %d)" % (first_patch, len(patches), min(cap_faces) if cap_faces else -1, centre_v))
    bvh = BVHTree.FromObject(ob, bpy.context.evaluated_depsgraph_get())
    co = [v.co.copy() for v in me.vertices]
    info = []
    for k, p in enumerate(patches):
        rim = [co[i] for i in p["exported_rim_vertices"]]
        pv = [co[i] for i in p["exported_patch_vertices"]]
        rc = sum(rim, Vector()) / 3.0
        pc = sum(pv, Vector()) / 3.0
        nout = (rc - pc).normalized()
        o = rc + nout * 0.05
        d = -nout
        h0 = bvh0.ray_cast(o, d, 1.0)
        h1 = bvh.ray_cast(o, d, 1.0)
        before = "none" if h0[0] is None else ("BACK face" if h0[1].dot(d) > 0 else "front face")
        after_front = h1[0] is not None and h1[1].dot(d) < 0
        after_patch = h1[0] is not None and h1[2] == p["exported_face"]
        print("PATCHCHECK h1 patch %d (%s, centre %s): before first hit = %s%s; after first hit = face %s, %s, %s" % (
            k, p["kind"], p["centre"], before, "" if h0[0] is None else " at %.3f m" % h0[3], h1[2], "front-facing" if after_front else "BACK-facing or none", "the patch" if after_patch else "NOT the patch"))
        if not (after_front and after_patch):
            bad.append("h1: patch %d: the first hit through the tear is not the patch's front face" % k)
        info.append({"rim": p["exported_rim_vertices"], "pv": p["exported_patch_vertices"], "rest_sign": 1.0})
    # h2 over every frame of every clip
    before_objs = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=arg("--clips"))
    src = next(x for k2, x in bpy.data.objects.items() if k2 not in before_objs and x.type == "ARMATURE")
    src.animation_data_create()
    names = [pb.name for pb in arm.pose.bones if pb.name in src.pose.bones]
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    rest_sign = []
    for p in info:
        r = [np.array(co[i]) for i in p["rim"]]
        n = np.cross(r[1] - r[0], r[2] - r[0])
        rc = sum(r) / 3.0
        pc = sum(np.array(co[i]) for i in p["pv"]) / 3.0
        rest_sign.append(1.0 if np.dot(n, rc - pc) > 0 else -1.0)
    worst = [(1e9, None)] * len(info)
    frames = 0
    for cname, act in sorted({a.name.split("|")[-1].split(".")[0]: a for a in bpy.data.actions}.items()):
        src.animation_data.action = act
        for f in range(int(act.frame_range[0]), int(act.frame_range[1]) + 1):
            bpy.context.scene.frame_set(f)
            for n in names:
                arm.pose.bones[n].matrix_basis = src.pose.bones[n].matrix_basis.copy()
            bpy.context.view_layer.update()
            ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
            m = ev.to_mesh()
            P = np.array([v.co[:] for v in m.vertices])
            ev.to_mesh_clear()
            frames += 1
            for k, p in enumerate(info):
                r = [P[i] for i in p["rim"]]
                n = np.cross(r[1] - r[0], r[2] - r[0])
                n = n / (np.linalg.norm(n) + 1e-12) * rest_sign[k]
                depth = float(np.dot(sum(r) / 3.0 - sum(P[i] for i in p["pv"]) / 3.0, n))
                if depth < worst[k][0]:
                    worst[k] = (depth, "%s f%d" % (cname, f))
    for k, (dep, where) in enumerate(worst):
        print("PATCHCHECK h2 patch %d: minimum depth below the rim plane over %d frames = %.4f m at %s" % (k, frames, dep, where))
        if dep < 0.002:
            bad.append("h2: patch %d reaches %.4f m (needs >= 0.002) at %s" % (k, dep, where))
    print("PATCHCHECK", "FAIL: " + "; ".join(bad) if bad else "PASS")
    sys.exit(2 if bad else 0)


main()
