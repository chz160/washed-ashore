"""First-person view-body stills (pod 49): the exported Survivor_Body.fbx (no head) posed from an authored clip, camera at the stand/crouch eye looking down at the given pitches.
blender -b -noaudio -P fp_stills.py -- --body <Survivor_Body.fbx> --clips <Authored.fbx> --tex <base.png> --outdir <dir> --tag before [--pitches 60,85]
Scene values, read from the game's World scene (Assets/Scenes/World.unity): Sun Light euler (50, -30, 0) (line 50045), colour (1, 0.96, 0.88), intensity 1.4 (lines 49976-77);
camera vertical FOV 70, near 0.1 (lines 83756-58). Camera at (0, -0.24, eye): the clip eye anchor and the stand/crouch eye heights of the clip frame (measure_clips.py).
Back faces are culled and the background is transparent (composited over magenta by the caller), so any gap in the body shows as magenta."""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import reskin_io as R  # noqa: E402
from reskin_checks import depth  # noqa: E402

POSES = (("Stand_Idle", 1, 1.6738), ("Crouch_Idle", 1, 1.2308))
EYE_LOCAL = (0.00012, 0.08417, 0.22662)   # eye-point-Survivor.json eye_in_head_bone_local (Head bone frame: x, up, forward)   # clip, frame, camera height in the clip frame ((1.70 or 1.25) / skeleton scale 1.0156)


def copy_pose(arm, src, frame):
    bpy.context.scene.frame_set(frame)
    skel = {pb.name: {"p": pb.parent.name if pb.parent else None} for pb in arm.pose.bones}
    for n in sorted(skel, key=lambda n: depth(skel, n)):
        if n in src.pose.bones:
            arm.pose.bones[n].matrix = src.pose.bones[n].matrix.copy()
            bpy.context.view_layer.update()


def main():
    o = R.argv()
    os.makedirs(o["outdir"], exist_ok=True)
    pitches = [float(x) for x in o.get("pitches", "60,85").split(",")]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    bpy.ops.import_scene.fbx(filepath=o["body"])
    arm = next(x for x in bpy.data.objects if x.type == "ARMATURE")
    body = next(x for x in bpy.data.objects if x.type == "MESH")
    before = set(bpy.data.objects.keys())
    bpy.ops.import_scene.fbx(filepath=o["clips"])
    src = next(x for k, x in bpy.data.objects.items() if k not in before and x.type == "ARMATURE")
    src.animation_data_create()
    mat = bpy.data.materials.new("fp")
    mat.use_nodes = True
    mat.use_backface_culling = True
    nt = mat.node_tree
    img = nt.nodes.new("ShaderNodeTexImage")
    img.image = bpy.data.images.load(o["tex"])
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.7
    nt.links.new(img.outputs["Color"], bsdf.inputs["Base Color"])
    body.data.materials.clear()
    body.data.materials.append(mat)
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    w = bpy.data.worlds.new("w")   # grey ambient light only; the background is transparent and composited over magenta afterwards (gap = transparent pixel)
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.6, 0.65, 0.75, 1.0)
    bg.inputs["Strength"].default_value = 0.6
    sc.world = w
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    # Unity sun euler (50, -30, 0): light travels along d_U; Unity (x, y, z) -> Blender (-x, -z, y) in this scene (forward = -Y)
    p, yw = math.radians(50), math.radians(-30)
    zx, zy, zz = 0.0, -math.sin(p), math.cos(p)
    dU = (zz * math.sin(yw), zy, zz * math.cos(yw))
    dB = Vector((-dU[0], -dU[2], dU[1]))
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sc.collection.objects.link(sun)
    sun.data.color = (1.0, 0.96, 0.88)
    sun.data.energy = 3.5   # about Unity intensity 1.4 in Blender's sun units (assumed)
    sun.rotation_euler = dB.to_track_quat("-Z", "Y").to_euler()
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.sensor_fit = "VERTICAL"
    cam.data.angle_y = math.radians(70)
    cam.data.clip_start = 0.1
    cam.data.clip_end = 100
    if "cap-double" in o or "cap_double" in o:   # the double-sided cap as the file has it (Blender's importer drops the mirror faces): a second skinned object holding the reversed cap faces, with the cap's (0,0,-1) normals
        import bmesh
        centre = len(body.data.vertices) - 1
        dup = body.copy()
        dup.data = body.data.copy()
        bpy.context.scene.collection.objects.link(dup)
        bm = bmesh.new()
        bm.from_mesh(dup.data)
        drop = [f for f in bm.faces if centre not in [v.index for v in f.verts]]
        bmesh.ops.delete(bm, geom=drop, context="FACES_ONLY")
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
        bm.to_mesh(dup.data)
        bm.free()
        dup.data.normals_split_custom_set([(0.0, 0.0, -1.0)] * len(dup.data.loops))
    head_cam = "poses" in o
    plist = [(x.split(":")[0], int(x.split(":")[1]), None) for x in o["poses"].split(",")] if head_cam else POSES
    for clip, fr, z in plist:
        act = next(a for a in bpy.data.actions if a.name.split("|")[-1].split(".")[0] == clip)
        src.animation_data.action = act
        copy_pose(arm, src, fr)
        if "aim_cap" in o:   # diagnostic: the camera on the eye point, aimed at the skinned cap centre (the neck cap seen from the head)
            hm = arm.matrix_world @ arm.pose.bones["Head"].matrix
            cam.location = hm @ Vector(EYE_LOCAL)
            ev = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
            mm = ev.to_mesh()
            target = body.matrix_world @ mm.vertices[len(mm.vertices) - 1].co
            ev.to_mesh_clear()
            cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
            sc.render.filepath = os.path.join(o["outdir"], "%s_%s_f%d_aimcap.png" % (o["tag"], clip, fr))
            bpy.ops.render.render(write_still=True)
            print("FPSTILL", sc.render.filepath)
            continue
        for pitch in pitches:
            if head_cam:   # the camera sits on the eye point of the posed head and pitches down from the Head bone's own forward
                hm = arm.matrix_world @ arm.pose.bones["Head"].matrix
                cam.location = (hm @ Vector(EYE_LOCAL[:1] + EYE_LOCAL[1:2] + EYE_LOCAL[2:]))
                Rm = hm.to_3x3()
                f, u = Rm @ Vector((0, 0, 1)), Rm @ Vector((0, 1, 0))
                d = f * math.cos(math.radians(pitch)) - u * math.sin(math.radians(pitch))
            else:
                cam.location = (0.0, -0.24, z)
                d = Vector((0.0, -math.cos(math.radians(pitch)), -math.sin(math.radians(pitch))))
            cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
            sc.render.filepath = os.path.join(o["outdir"], "%s_%s_f%d_pitch%d.png" % (o["tag"], clip, fr, int(pitch)))
            bpy.ops.render.render(write_still=True)
            print("FPSTILL", sc.render.filepath)
    print("FPSTILLS OK")


if __name__ == "__main__":
    main()
