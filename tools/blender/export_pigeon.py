"""Export the Peripheral Arbor Pigeon (public domain) to a Unity-ready FBX.

Run from the repo root (Blender 5.2.2, headless):
    "C:\\Program Files\\Blender Foundation\\Blender 5.2\\blender.exe" -b \
        "vendor/Pigeon - Peripheral Arbor (Public Domain)/bird.blend" \
        --python tools/blender/export_pigeon.py -- [--out Assets/ThirdParty/PeripheralArbor/Pigeon]
or use tools/blender/export_pigeon.ps1, which finds Blender and does the same.

The source rig is a Rigify rig: 348 bones, of which 76 deform (DEF-*), plus a metarig and WGT-* widget
meshes. Exporting it directly would carry the control and mechanism bones into Unity. Instead this script
builds a new armature holding only the 76 deform bones (each parented to its nearest deform ancestor),
bakes the five clips onto it frame by frame from the evaluated Rigify pose, re-binds the Bird mesh to it,
and exports just that armature and mesh. One NLA strip per clip becomes one FBX take.

The vendor .blend is never saved: everything happens in memory and Blender quits without writing it.
The packed source texture is written out byte for byte (Pigeon_Texture.jpg); recolouring is done in
Unity with a material variant, never on the texture.
"""
import os
import sys

import bpy
from mathutils import Matrix

CLIPS = ["Flapping", "Gliding", "Takeoff", "Landing", "Standing Idle"]
SOURCE_RIG = "rig"
SOURCE_MESH = "Bird"
OUT_ARMATURE = "PigeonRig"
OUT_MESH = "Pigeon"
FBX_NAME = "Pigeon.fbx"
TEXTURE_NAME = "Pigeon_Texture.jpg"
EXPECTED_BONES = 76


def parse_out_dir():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    repo = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    out = os.path.join(repo, "Assets", "ThirdParty", "PeripheralArbor", "Pigeon")
    if "--out" in argv:
        out = argv[argv.index("--out") + 1]
        if not os.path.isabs(out):
            out = os.path.join(repo, out)
    return os.path.abspath(out)


def fail(msg):
    print("EXPORT_PIGEON FAILED: " + msg)
    sys.stdout.flush()
    os._exit(1)


def deform_parent(bone):
    p = bone.parent
    while p and not p.use_deform:
        p = p.parent
    return p


def ordered_deform_bones(rig):
    """Deform bones, parents before children (depth order)."""
    def depth(b):
        d = 0
        while b.parent:
            d, b = d + 1, b.parent
        return d
    return sorted((b for b in rig.data.bones if b.use_deform), key=lambda b: (depth(b), b.name))


def build_deform_armature(rig, bones):
    data = bpy.data.armatures.new(OUT_ARMATURE)
    arm = bpy.data.objects.new(OUT_ARMATURE, data)
    bpy.context.scene.collection.objects.link(arm)
    arm.matrix_world = rig.matrix_world.copy()

    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        o.select_set(False)
    view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    edit = {}
    for b in bones:
        eb = data.edit_bones.new(b.name)
        eb.head, eb.tail = b.head_local.copy(), b.tail_local.copy()
        eb.matrix = b.matrix_local.copy()
        eb.use_deform = True
        edit[b.name] = eb
    for b in bones:
        p = deform_parent(b)
        if p:
            edit[b.name].parent = edit[p.name]
            edit[b.name].use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
    return arm


def mute_source_nla(rig):
    ad = rig.animation_data
    for t in ad.nla_tracks:
        t.mute = True


def bake_clip(rig, arm, bones, clip_name):
    src = bpy.data.actions.get(clip_name)
    if not src:
        fail(f"source action '{clip_name}' not found")
    rig.animation_data.action = src
    if hasattr(rig.animation_data, "action_slot") and rig.animation_data.action_slot is None and src.slots:
        rig.animation_data.action_slot = src.slots[0]

    baked = bpy.data.actions.new(clip_name)
    if not arm.animation_data:
        arm.animation_data_create()
    arm.animation_data.action = baked

    rest = {b.name: b.matrix_local for b in bones}
    parent = {b.name: (deform_parent(b).name if deform_parent(b) else None) for b in bones}
    start, end = (int(round(v)) for v in src.frame_range)
    scene = bpy.context.scene
    prev_q = {}
    for f in range(start, end + 1):
        scene.frame_set(f)
        pose = {b.name: rig.pose.bones[b.name].matrix.copy() for b in bones}
        for b in bones:
            n, p = b.name, parent[b.name]
            if p:
                local_rest = rest[p].inverted() @ rest[n]
                basis = local_rest.inverted() @ (pose[p].inverted() @ pose[n])
            else:
                basis = rest[n].inverted() @ pose[n]
            loc, rot, scale = basis.decompose()
            if n in prev_q:
                rot.make_compatible(prev_q[n])
            prev_q[n] = rot
            pb = arm.pose.bones[n]
            pb.location, pb.rotation_quaternion, pb.scale = loc, rot, scale
            for path in ("location", "rotation_quaternion", "scale"):
                pb.keyframe_insert(path, frame=f, group=n)
    for fc in fcurves_of(baked):
        for kp in fc.keyframe_points:
            kp.interpolation = 'LINEAR'

    arm.animation_data.action = None
    track = arm.animation_data.nla_tracks.new()
    track.name = clip_name
    strip = track.strips.new(clip_name, start, baked)
    strip.name = clip_name
    return start, end


def fcurves_of(action):
    if hasattr(action, "fcurves") and len(getattr(action, "fcurves", [])) > 0:
        return list(action.fcurves)
    out = []
    for layer in getattr(action, "layers", []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                out.extend(bag.fcurves)
    return out


def rebind_mesh(mesh_obj, arm):
    mw = mesh_obj.matrix_world.copy()
    mesh_obj.parent = arm
    mesh_obj.matrix_world = mw
    mods = [m for m in mesh_obj.modifiers if m.type == 'ARMATURE']
    if len(mods) != 1:
        fail(f"expected one Armature modifier on '{mesh_obj.name}', found {len(mods)}")
    mods[0].object = arm
    mesh_obj.name = OUT_MESH


def write_texture(mesh_obj, out_dir):
    for slot in mesh_obj.material_slots:
        mat = slot.material
        if not mat or not mat.node_tree:
            continue
        for node in mat.node_tree.nodes:
            if node.type == 'TEX_IMAGE' and node.image and node.image.packed_file:
                path = os.path.join(out_dir, TEXTURE_NAME)
                with open(path, "wb") as fh:
                    fh.write(node.image.packed_file.data)
                return path
    fail("no packed base texture found on the Bird mesh")


def main():
    out_dir = parse_out_dir()
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene

    rig = bpy.data.objects.get(SOURCE_RIG)
    mesh_obj = bpy.data.objects.get(SOURCE_MESH)
    if not rig or rig.type != 'ARMATURE' or not mesh_obj:
        fail("source objects 'rig' / 'Bird' not found")

    bones = ordered_deform_bones(rig)
    if len(bones) != EXPECTED_BONES:
        fail(f"expected {EXPECTED_BONES} deform bones, found {len(bones)}")

    arm = build_deform_armature(rig, bones)
    mute_source_nla(rig)
    ranges = {c: bake_clip(rig, arm, bones, c) for c in CLIPS}
    rig.animation_data.action = None
    rebind_mesh(mesh_obj, arm)
    texture = write_texture(mesh_obj, out_dir)

    view_layer = bpy.context.view_layer
    for o in view_layer.objects:
        o.select_set(False)
    arm.select_set(True)
    mesh_obj.select_set(True)
    view_layer.objects.active = arm
    scene.frame_set(1)

    fbx_path = os.path.join(out_dir, FBX_NAME)
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=True,
        object_types={'ARMATURE', 'MESH'},
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        add_leaf_bones=False,
        use_armature_deform_only=True,
        armature_nodetype='NULL',
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_nla_strips=True,
        bake_anim_use_all_actions=False,
        bake_anim_force_startend_keying=True,
        bake_anim_step=1.0,
        bake_anim_simplify_factor=0.0,
        path_mode='STRIP',
        embed_textures=False,
    )

    print(f"EXPORT_PIGEON OK fbx={fbx_path}")
    print(f"EXPORT_PIGEON texture={texture}")
    print(f"EXPORT_PIGEON bones={len(arm.data.bones)} fps={scene.render.fps}")
    for c in CLIPS:
        print(f"EXPORT_PIGEON clip '{c}' frames {ranges[c][0]}-{ranges[c][1]}")
    sys.stdout.flush()


main()
