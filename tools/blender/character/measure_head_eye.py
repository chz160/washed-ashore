"""Measures the eye point of each Head part mesh at rest: the mean of the vertices of the "Eye" material, expressed in the Head bone's
local frame, plus the same point as (up, forward) metres from the Head bone head in the upright rest pose.

blender -b -noaudio -P measure_head_eye.py -- --out <eye.json> <Head.fbx> [<Head.fbx> ...]
Method: Quaternius head meshes carry a separate "Eye" material (two small eye quads); their vertex mean is the point between the eyes.
The authored clips and measure_clips.py use this point instead of a guessed offset.
"""
import json
import os
import statistics
import sys

import bpy


def measure(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    idx = [i for i, m in enumerate(mesh.data.materials) if m and m.name.split(".")[0] == "Eye"]
    method = "material named Eye"
    if not idx:  # women's heads colour the eye quads with a hair-brown material: the small (<= 64 vertices) material furthest forward
        small = []
        for i, m in enumerate(mesh.data.materials):
            vs = [mesh.matrix_world @ mesh.data.vertices[v].co for p in mesh.data.polygons if p.material_index == i for v in p.vertices]
            if 0 < len(vs) <= 64:
                small.append((statistics.mean(v.y for v in vs), i))
        if not small:
            raise RuntimeError(f"{path}: no eye material")
        idx = [min(small)[1]]
        method = "smallest material furthest forward (" + mesh.data.materials[idx[0]].name + ")"
    pts = [mesh.matrix_world @ mesh.data.vertices[v].co for p in mesh.data.polygons if p.material_index in idx for v in p.vertices]
    mean = [statistics.mean(p[k] for p in pts) for k in range(3)]
    head = arm.data.bones["Head"]
    local = head.matrix_local.inverted() @ type(pts[0])(mean)
    head_pos = head.head_local
    return {
        "eye_in_head_bone_local": [round(x, 5) for x in local],
        "up_m": round(mean[2] - head_pos.z, 4),
        "forward_m": round(-(mean[1] - head_pos.y), 4),  # the rigs face -Y in Blender
        "lateral_m": round(mean[0] - head_pos.x, 4),
        "eye_vertices": len(pts),
        "method": method,
    }


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    out = argv[argv.index("--out") + 1]
    files = [a for a in argv if a.lower().endswith(".fbx")]
    result = {}
    for f in files:
        try:
            result[os.path.basename(f)] = measure(f)
        except RuntimeError as e:  # a head with no identifiable eye material is reported, not guessed
            result[os.path.basename(f)] = {"error": str(e)}
    with open(out, "w") as fh:
        json.dump(result, fh, indent=1)
    print("EYEPOINT", json.dumps(result))


main()
