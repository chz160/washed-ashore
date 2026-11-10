"""Neck-cap fold check under skinning (pod 49, td-49 CAP FOLD CONFIRMED): every cap triangle's normal, posed, against its rest normal, over EVERY frame of EVERY clip.
blender -b -noaudio -P cap_flip_check.py -- --body <Survivor_Body.fbx> --clips <Authored_Survivor.fbx> [--min-dot 0.2] [--first-only 1]
The cap triangles are the faces that use the cap centre (the last vertex of the Body mesh). A triangle whose posed normal has a negative dot with its rest normal is flipped (back-face culled: a hole);
the gate is 0 flipped and a minimum dot >= --min-dot (default 0.2, td-49's margin). Fail-closed: exit 2 on any flipped triangle, on a minimum dot below the margin, or when no cap triangle is found.
Pose transfer: the clip armature's local pose (matrix_basis) is copied bone by bone (both armatures share the Skeleton_Human rest)."""
import sys

import bpy
import numpy as np


def arg(name, default=None):
    a = sys.argv[sys.argv.index("--") + 1:]
    return a[a.index(name) + 1] if name in a else default


def tri_normals(mesh, idx):
    out = []
    for i in idx:
        v = [np.array(mesh.vertices[j].co) for j in mesh.polygons[i].vertices]
        n = np.cross(v[1] - v[0], v[2] - v[0])
        out.append(n / (np.linalg.norm(n) + 1e-12))
    return np.array(out)


def main():
    margin = float(arg("--min-dot", "0.2"))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=arg("--body"))
    arm = [o for o in bpy.data.objects if o.type == "ARMATURE"][0]
    ob = [o for o in bpy.data.objects if o.type == "MESH"][0]
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=arg("--clips"))
    src = next(x for k, x in bpy.data.objects.items() if k not in before and x.type == "ARMATURE")
    src.animation_data_create()
    centre = len(ob.data.vertices) - 1
    cap = [p.index for p in ob.data.polygons if centre in p.vertices]
    if not cap:
        print("CAPFLIP FAIL: no cap triangle found")
        sys.exit(2)
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    m = ev.to_mesh()
    rest = tri_normals(m, cap)
    ev.to_mesh_clear()
    names = [pb.name for pb in arm.pose.bones if pb.name in src.pose.bones]
    worst, flipped_total, frames_total, bad = 1.0, 0, 0, []
    acts = sorted({a.name.split("|")[-1].split(".")[0]: a for a in bpy.data.actions}.items())
    for cname, act in acts:
        src.animation_data.action = act
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        cmin, cflip, cat = 1.0, 0, None
        for f in range(f0, f1 + 1):
            bpy.context.scene.frame_set(f)
            for n in names:
                arm.pose.bones[n].matrix_basis = src.pose.bones[n].matrix_basis.copy()
            bpy.context.view_layer.update()
            dg = bpy.context.evaluated_depsgraph_get()
            ev = ob.evaluated_get(dg)
            m = ev.to_mesh()
            d = (tri_normals(m, cap) * rest).sum(1)
            ev.to_mesh_clear()
            frames_total += 1
            cflip += int((d < 0).sum())
            if d.min() < cmin:
                cmin, cat = float(d.min()), f
        flipped_total += cflip
        worst = min(worst, cmin)
        print("CAPFLIP %-22s frames %d..%d flipped (summed over frames) %d, min dot %.2f at frame %s" % (cname, f0, f1, cflip, cmin, cat))
        if cflip or cmin < margin:
            bad.append("%s (flipped %d, min dot %.2f at frame %s)" % (cname, cflip, cmin, cat))
    print("CAPFLIP SUMMARY: %d cap triangles, %d clips, %d frames, flipped %d, worst min dot %.2f (margin %.2f)" % (len(cap), len(acts), frames_total, flipped_total, worst, margin))
    print("CAPFLIP", "FAIL: " + "; ".join(bad) if bad else "PASS")
    sys.exit(2 if bad else 0)


main()
