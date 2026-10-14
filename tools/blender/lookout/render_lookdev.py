"""Look-dev stills of the MC-39 tower for the director's gates G1 (grey model) and G2 (textured).

Run headless from the repo root (Blender 5.2.2):
    blender -b --factory-startup --python tools/blender/lookout/render_lookdev.py -- \
        --mode grey|textured [--params <json>] [--out TestResults/tower-lookdev] [--textures <build dir>] [--shots main,cab,...]
        [--set key=value,...] [--tag name]

--set overrides table values for a look option (the shipped model always comes from the table as it stands);
--tag is added to the output names so options sit side by side.

For each shot it renders the tower, renders a twin of the same shot with the tower hidden (and counts the pixels
that changed, so a still that would look the same without the tower is caught), and, for the two shots that
match a reference photo, writes a side-by-side pair. Pairs contain the reference photo, so they stay under
TestResults/ and never go under Assets/.

The shots and their cameras are data (SHOTS below); nothing is framed by hand in the UI.
"""
import json
import math
import os
import sys

import bpy
import numpy as np

sys.dont_write_bytecode = True
from mathutils import Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
PHOTOS = os.path.join(REPO, "_bmad-output", "research", "domain-burns-lookout-tower-2026-10-10", "images")

import build_mc39                                     # noqa: E402
from mc39_mesh import MATERIAL_NAMES                  # noqa: E402

# Cameras in the design frame (x east, y north, z up): 'pos', or 'az' (bearing of the camera from the tower axis,
# degrees clockwise from north) with 'dist' and 'eye'; 'look' is the point aimed at, 'roll' in degrees, 'vfov' the
# vertical field of view, 'size' the output in pixels.
SHOTS = {
    # The two reference photos. Camera solved by fit_reference_cameras.py from points read off each photo
    # (main: rms 11.7 px of 1500 x 2000, and the solved 67 degree field of view is a phone's main lens).
    # Both were taken from off the corner that carries the first landing: the model's north-east corner.
    "main": {"pos": (17.89, 8.96, 1.31), "look": (-0.75, -0.96, 12.22), "roll": 1.92, "vfov": 67.2, "size": (1512, 2016),
             "photo": "burns-nhlr-lutes-main.jpg"},
    "upper": {"pos": (28.08, 18.23, 2.65), "look": (-0.39, -0.53, 24.43), "roll": 1.17, "vfov": 18.7, "size": (1512, 2016),
              "photo": "burns-nhlr-lutes-feb2023.jpg"},
    # G2 close-ups
    "cab": {"az": 62.0, "dist": 7.5, "eye": 29.4, "look": (0.0, 0.0, 27.5), "vfov": 34.0, "size": (1600, 1200)},
    "cab_inside": {"pos": (-0.72, -0.74, 27.94), "look": (0.45, 0.35, 27.2), "vfov": 80.0, "size": (1600, 1200)},
    "landing": {"pos": (-0.45, -0.95, 10.35), "look": (-1.55, -1.75, 8.2), "vfov": 66.0, "size": (1600, 1200)},
    "base": {"az": 62.0, "dist": 9.5, "eye": 1.6, "look": (0.0, 0.0, 3.4), "vfov": 58.0, "size": (1600, 1200)},
    # eye level on landing 7, looking at the two short rail pieces and the foot of the top flight
    "landing7": {"pos": (0.25, 0.95, 25.88), "look": (0.72, 0.55, 24.6), "vfov": 75.0, "size": (1600, 1200)},
    # cab floor, lit side, for the leaf litter
    "litter": {"pos": (0.2, 0.3, 27.9), "look": (-0.72, -0.78, 26.3), "vfov": 60.0, "size": (1600, 1200)},
    # the game's view from 1 km (70 degree camera, 1080p), and the same line of sight through a long lens
    "far": {"az": 62.0, "dist": 1000.0, "eye": 1.65, "look": (0.0, 0.0, 15.0), "vfov": 70.0, "size": (1920, 1080)},
    "far_zoom": {"az": 62.0, "dist": 1000.0, "eye": 1.65, "look": (0.0, 0.0, 16.5), "vfov": 3.2, "size": (900, 1200)},
}
# Burns as photographed in 2023: a low, dark, wide-brimmed roof (director note G1-1). An option for Noah's T2 pick;
# the table's default stays the 1935 drawing's roof.
# No roof variants: the drawing roof (6:12, 8'-0" at the eaves) is final (Noah via team-lead, 2026-10-11). What read as a
# low wide brim on the Burns photos is the top-hinged shutters propped open over the window band.
DEFAULT_SHOTS = {"grey": ["main", "upper"], "textured": ["main", "upper", "cab", "cab_inside", "landing", "base"]}


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    o = {"mode": "grey", "params": os.path.join(HERE, "mc39_params.json"), "out": os.path.join(REPO, "TestResults", "tower-lookdev"),
         "textures": os.path.join(HERE, "out", "build"), "shots": None, "samples": 32, "set": "", "tag": ""}
    i = 0
    while i < len(argv):
        k = argv[i][2:]
        if argv[i].startswith("--") and k in o and i + 1 < len(argv):
            o[k] = argv[i + 1]
            i += 2
        else:
            build_mc39.fail(f"unknown argument '{argv[i]}'")
    o["shots"] = o["shots"].split(",") if o["shots"] else DEFAULT_SHOTS[o["mode"]]
    for k in ("params", "out", "textures"):
        o[k] = os.path.abspath(o[k])
    return o


def to_blender(p):
    """Design frame = Blender frame (build_mc39.make_object applies no turn)."""
    return Vector((p[0], p[1], p[2]))


def clay(name, colour, rough=0.85, alpha=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*colour, 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Alpha"].default_value = alpha
    return m


def textured_materials(tex_dir, name):
    """The four game materials, wired to the generated atlas the way the Unity materials use it."""
    paths = {k: os.path.join(tex_dir, f"{name}_{k}.png") for k in ("Albedo", "Normal", "Mask")}
    for p in paths.values():
        if not os.path.isfile(p):
            build_mc39.fail(f"texture not found: {p} (run build_mc39.ps1 first)")
    imgs = {k: bpy.data.images.load(p) for k, p in paths.items()}
    imgs["Normal"].colorspace_settings.name = 'Non-Color'
    imgs["Mask"].colorspace_settings.name = 'Non-Color'
    out = []
    for mname in MATERIAL_NAMES:
        m = bpy.data.materials.get(mname) or bpy.data.materials.new(mname)
        m.use_nodes = True
        nt = m.node_tree
        b = nt.nodes["Principled BSDF"]
        ta = nt.nodes.new("ShaderNodeTexImage"); ta.image = imgs["Albedo"]
        tm = nt.nodes.new("ShaderNodeTexImage"); tm.image = imgs["Mask"]
        tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = imgs["Normal"]
        nm = nt.nodes.new("ShaderNodeNormalMap")
        sep = nt.nodes.new("ShaderNodeSeparateColor")
        inv = nt.nodes.new("ShaderNodeMath"); inv.operation = 'SUBTRACT'; inv.inputs[0].default_value = 1.0
        nt.links.new(ta.outputs["Color"], b.inputs["Base Color"])
        nt.links.new(tm.outputs["Color"], sep.inputs["Color"])
        nt.links.new(sep.outputs["Red"], b.inputs["Metallic"])
        nt.links.new(tm.outputs["Alpha"], inv.inputs[1])
        nt.links.new(inv.outputs["Value"], b.inputs["Roughness"])
        nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
        nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
        if mname != "MC39_Opaque":
            nt.links.new(ta.outputs["Alpha"], b.inputs["Alpha"])
        if mname == "MC39_Glass":
            b.inputs["Roughness"].default_value = 0.08
            for l in list(b.inputs["Roughness"].links):
                nt.links.remove(l)
        out.append(m)
    return out


def setup_world(mode):
    scene = bpy.context.scene
    w = bpy.data.worlds.new("lookdev")
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes["Background"]
    # a plain clear-sky blue: the stills are judged on shape and surface, not on the sky
    bg.inputs["Color"].default_value = (0.10, 0.22, 0.55, 1.0)
    bg.inputs["Strength"].default_value = 1.0
    scene.world = w
    sun = bpy.data.lights.new("sun", 'SUN')
    sun.energy = 4.0
    sun.angle = math.radians(1.0)
    so = bpy.data.objects.new("sun", sun)
    scene.collection.objects.link(so)
    # light travels down from the south-east, so the east face is lit and the north face shaded, as in the photos
    so.rotation_euler = to_blender((-0.6, 0.45, -0.66)).to_track_quat('-Z', 'Y').to_euler()
    # ground
    me = bpy.data.meshes.new("ground")
    s = 400.0
    me.from_pydata([(-s, -s, -0.02), (s, -s, -0.02), (s, s, -0.02), (-s, s, -0.02)], [], [(0, 1, 2, 3)])
    g = bpy.data.objects.new("ground", me)
    me.materials.append(clay("ground", (0.23, 0.22, 0.17) if mode == "textured" else (0.32, 0.32, 0.32), 0.95))
    scene.collection.objects.link(g)


def setup_render(samples):
    scene = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
    try:
        import addon_utils
        addon_utils.enable("cycles")
    except Exception:
        pass
    try:
        scene.render.engine = 'CYCLES'
        scene.cycles.samples = int(samples)
        scene.cycles.use_denoising = True
        scene.cycles.device = 'CPU'
        scene.cycles.seed = 0
    except Exception:
        scene.render.engine = engines[0]
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'Standard'
    return scene.render.engine


def place_camera(shot):
    scene = bpy.context.scene
    cam = bpy.data.cameras.new("cam")
    ob = bpy.data.objects.new("cam", cam)
    scene.collection.objects.link(ob)
    if "pos" in shot:
        pos = shot["pos"]
    else:
        a = math.radians(shot["az"])
        pos = (math.sin(a) * shot["dist"], math.cos(a) * shot["dist"], shot["eye"])
    p, t = to_blender(pos), to_blender(shot["look"])
    ob.location = p
    q = (t - p).to_track_quat('-Z', 'Y')
    ob.rotation_euler = (q @ Quaternion((0.0, 0.0, 1.0), -math.radians(shot.get("roll", 0.0)))).to_euler()
    w, h = shot["size"]
    scene.render.resolution_x, scene.render.resolution_y = w, h
    scene.render.resolution_percentage = 100
    cam.sensor_fit = 'VERTICAL'
    cam.sensor_height = 24.0
    cam.lens = 12.0 / math.tan(math.radians(shot["vfov"]) / 2.0)
    cam.clip_start, cam.clip_end = (10.0, 3000.0) if shot.get("dist", 0.0) > 100.0 else (0.05, 2000.0)
    scene.camera = ob
    return ob


def load_pixels(path):
    img = bpy.data.images.load(path)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    return px.reshape(h, w, 4)[::-1, :, :3]            # top row first


def save_pixels(path, rgb):
    h, w, _ = rgb.shape
    img = bpy.data.images.new("out", w, h, alpha=False)
    buf = np.ones((h, w, 4), dtype=np.float32)
    buf[:, :, :3] = rgb[::-1]
    img.pixels.foreach_set(buf.ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def resize(rgb, h_out):
    h, w, _ = rgb.shape
    w_out = int(round(w * h_out / h))
    ys = np.clip(((np.arange(h_out) + 0.5) * h / h_out).astype(int), 0, h - 1)
    xs = np.clip(((np.arange(w_out) + 0.5) * w / w_out).astype(int), 0, w - 1)
    return rgb[ys][:, xs]


def main():
    o = parse_args()
    with open(o["params"], "r", encoding="utf-8") as fh:
        P = json.load(fh)
    overrides = o["set"]
    for item in [x for x in overrides.split(",") if x]:
        key, val = item.split("=")
        node = P
        parts = key.split(".")
        for k in parts[:-1]:
            node = node[k]
        if parts[-1] not in node:
            build_mc39.fail(f"--set: no table key '{key}'")
        node[parts[-1]] = val if isinstance(node[parts[-1]], str) else type(node[parts[-1]])(val)
    tag = ("-" + o["tag"]) if o["tag"] else ""
    os.makedirs(o["out"], exist_ok=True)
    build_mc39.clear_scene()
    B = build_mc39.build_buffers(P)
    if o["mode"] == "textured":
        mats = textured_materials(o["textures"], P["name"])
    else:
        grey = clay("grey", (0.70, 0.70, 0.70))
        mats = [grey, grey, clay("grey_glass", (0.12, 0.13, 0.14), 0.25), grey]
    tower = []
    for name, buf, pivot, role in B["objects"]:
        if role in ("lod0", "moving") and buf.faces:
            tower.append(build_mc39.make_object(name, buf, pivot, mats))
    setup_world(o["mode"])
    engine = setup_render(o["samples"])
    report = {"mode": o["mode"], "engine": engine, "params": o["params"], "shots": {}}
    for sname in o["shots"]:
        shot = SHOTS[sname]
        cam = place_camera(shot)
        scene = bpy.context.scene
        base = os.path.join(o["out"], f"{o['mode']}{tag}-{sname}")
        scene.render.filepath = base + ".png"
        bpy.ops.render.render(write_still=True)
        for ob in tower:
            ob.hide_render = True
        scene.render.filepath = base + "-twin.png"
        bpy.ops.render.render(write_still=True)
        for ob in tower:
            ob.hide_render = False
        a, b = load_pixels(base + ".png"), load_pixels(base + "-twin.png")
        changed = int((np.abs(a - b).max(axis=2) > 8.0 / 255.0).sum())
        entry = {"image": base + ".png", "twin": base + "-twin.png", "changed_px": changed, "total_px": int(a.shape[0] * a.shape[1])}
        if shot.get("photo"):
            photo = load_pixels(os.path.join(PHOTOS, shot["photo"]))
            pair = np.concatenate([resize(photo, a.shape[0]), np.ones((a.shape[0], 16, 3), np.float32), a], axis=1)
            save_pixels(base + "-pair.png", pair)
            entry["pair"] = base + "-pair.png"
            entry["photo"] = shot["photo"]
        report["shots"][sname] = entry
        print(f"RENDER_LOOKDEV {sname} changed_px={changed} of {entry['total_px']} -> {entry.get('pair', entry['image'])}")
        bpy.data.objects.remove(cam)
    report["overrides"] = overrides
    with open(os.path.join(o["out"], f"{o['mode']}{tag}-report.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(report, fh, indent=1, sort_keys=True)
    print("RENDER_LOOKDEV OK")
    sys.stdout.flush()


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        import traceback
        traceback.print_exc()
        build_mc39.fail("exception (see traceback)")
