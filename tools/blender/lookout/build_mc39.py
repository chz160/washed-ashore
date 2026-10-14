"""Builds the Burns (Bell) MC-39 lookout tower from mc39_params.json and exports a Unity-ready FBX.

Run headless from the repo root (Blender 5.2.2), or use build_mc39.ps1, which also checks the result:
    blender -b --factory-startup --python-use-system-env --python tools/blender/lookout/build_mc39.py -- \
        --params tools/blender/lookout/mc39_params.json --out tools/blender/lookout/out/build

Everything is procedural: geometry comes from mc39_layout / mc39_tower / mc39_cab / mc39_collision, textures from
mc39_textures (numpy, no downloaded or photographed input). Nothing is modelled or painted by hand, and no name,
seed or order depends on the clock, so two runs give the same bytes apart from the FBX creation time.

Outputs in --out: <name>.fbx (LOD0 parts, LOD1, LOD2, impostor, moving parts, COL_ proxies), <name>_Albedo.png,
<name>_Normal.png, <name>_Mask.png, <name>.manifest.json, TEXTURES_LICENSE.txt.

The model is built in a design frame (x east, y north, z up), which is Blender's frame as it stands: with the
exporter's -Z forward / Y up and Unity's FBX import it lands with north on +Z, east on +X and 1 unit = 1 m
(MARK_Front imports at (0, 0, +1)). No turn is applied: an earlier half turn about z brought it in facing south.
"""
import json
import os
import sys

import bpy

sys.dont_write_bytecode = True     # no __pycache__ beside the scripts: stale bytecode would be a hidden input
BLENDER_VERSION = (5, 2, 2)
HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import mc39_atlas                                    # noqa: E402
from mc39_cab import Cab                             # noqa: E402
from mc39_collision import Collision                 # noqa: E402
from mc39_layout import Layout                       # noqa: E402
from mc39_mesh import MATERIAL_NAMES, MAT_IMPOSTOR, MeshBuf  # noqa: E402
from mc39_tower import Tower                         # noqa: E402

LOD0_PARTS = ["Frame", "Stairs", "Landings", "Cab", "Roof", "Pads", "Antenna"]


def fail(msg):
    print("BUILD_MC39 FAILED: " + msg)
    sys.stdout.flush()
    os._exit(1)


def parse_args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    opts = {"params": os.path.join(HERE, "mc39_params.json"), "out": os.path.join(HERE, "out", "build"), "no_textures": False, "whole_mm_reach": False}
    i = 0
    while i < len(argv):
        a = argv[i]
        if a in ("--params", "--out") and i + 1 < len(argv):
            opts[a[2:]] = os.path.abspath(argv[i + 1])
            i += 2
        elif a == "--no-textures":
            opts["no_textures"] = True
            i += 1
        elif a == "--whole-millimetre-reach":
            # comparison run only (tower-td-note): reach as whole millimetres, to show what the sub-millimetre reach changed
            opts["whole_mm_reach"] = True
            i += 1
        else:
            fail(f"unknown argument '{a}'")
    return opts


def clear_scene():
    for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights, bpy.data.images):
        for item in list(coll):
            coll.remove(item)


def digit_glyphs():
    """Triangulated outlines of 0-9 from Blender's built-in font, scaled so the digit height is 1."""
    cu = bpy.data.curves.new("mc39_glyph", 'FONT')
    cu.resolution_u = 2
    ob = bpy.data.objects.new("mc39_glyph", cu)
    bpy.context.scene.collection.objects.link(ob)
    raw = {}
    for ch in "0123456789":
        cu.body = ch
        bpy.context.view_layer.update()
        ev = ob.evaluated_get(bpy.context.evaluated_depsgraph_get())
        me = ev.to_mesh()
        me.calc_loop_triangles()
        raw[ch] = [[(me.vertices[i].co.x, me.vertices[i].co.y) for i in t.vertices] for t in me.loop_triangles]
        ev.to_mesh_clear()
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(cu)
    ys = [p[1] for t in raw["0"] for p in t]
    h, y0 = max(ys) - min(ys), min(ys)
    glyphs = {}
    for ch in sorted(raw):
        xs = [p[0] for t in raw[ch] for p in t]
        x0 = min(xs)
        glyphs[ch] = {"width": (max(xs) - x0) / h, "tris": [[((x - x0) / h, (y - y0) / h) for x, y in t] for t in raw[ch]]}
        if not glyphs[ch]["tris"]:
            fail(f"font glyph '{ch}' has no faces")
    return glyphs


def impostor_buf():
    """Two crossed cards for the far distance; normals point up so both cards take the same light."""
    reg = mc39_atlas.region("IMPOSTOR")
    buf = MeshBuf("Impostor")
    hw, ht = reg.length / 2.0, reg.width
    buf.quad_region((-hw, 0, 0), (hw, 0, 0), (hw, 0, ht), (-hw, 0, ht), reg, MAT_IMPOSTOR)
    buf.quad_region((0, -hw, 0), (0, hw, 0), (0, hw, ht), (0, -hw, ht), reg, MAT_IMPOSTOR)
    buf.normals = (0.0, 0.0, 1.0)
    return buf


def merge(name, bufs):
    out = MeshBuf(name)
    for b in bufs:
        out.extend(b)
    return out


def build_buffers(P):
    """Every object of the tower as (name, MeshBuf, pivot in the design frame), plus the helpers that made them."""
    L = Layout(P)
    T = Tower(P, L)
    C = Cab(P, L, T)
    parts = {
        "Frame": T.frame, "Stairs": T.stairs, "Landings": T.landings, "Cab": C.shell, "Roof": C.roof,
        "Pads": T.pads, "Antenna": T.antenna,
    }
    objs = []
    lod0 = {k: parts[k](0) for k in LOD0_PARTS}
    for k in LOD0_PARTS:
        objs.append((f"MC39_LOD0_{k}", lod0[k], (0.0, 0.0, 0.0), "lod0"))
    lod1 = merge("LOD1", [parts[k](1) for k in LOD0_PARTS] + [C.movers_lod1()])
    lod2 = merge("LOD2", [parts[k](2) for k in LOD0_PARTS])
    objs.append(("MC39_LOD1", lod1, (0.0, 0.0, 0.0), "lod1"))
    objs.append(("MC39_LOD2", lod2, (0.0, 0.0, 0.0), "lod2"))
    objs.append(("MC39_Impostor", impostor_buf(), (0.0, 0.0, 0.0), "impostor"))

    objs.append(("MC39_Hatch", C.hatch(), C.hatch_pivot(), "moving"))
    for s in C.sash_frames():
        objs.append(("MC39_" + s["name"], C.sash(s), s["pivot"], "moving"))
    ped, ring = C.firefinder_static(digit_glyphs())
    objs.append(("MC39_Firefinder_Pedestal", ped, (0.0, 0.0, 0.0), "lod0"))
    objs.append(("MC39_Firefinder_Ring", ring, (0.0, 0.0, 0.0), "lod0"))
    objs.append(("MC39_Firefinder_Alidade", C.alidade(), C.firefinder_axis(), "moving"))

    K = Collision(P, L, T, C)
    for name, buf, pivot in K.colliders():
        objs.append((name, buf, pivot, "collision"))
    if len([o for o in objs if o[3] == "collision"]) > 150:
        raise ValueError("more than 150 colliders")
    for name, pos in K.empties():
        objs.append((name, None, pos, "empty"))
    objs.sort(key=lambda o: o[0])
    return {"L": L, "T": T, "C": C, "K": K, "objects": objs, "lod2": lod2}


def get_materials():
    mats = []
    for name in MATERIAL_NAMES:
        m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mats.append(m)
    return mats


def make_object(name, buf, pivot, mats):
    """Design frame = Blender frame; vertices local to the pivot."""
    px, py, pz = pivot[0], pivot[1], pivot[2]
    if buf is None:
        ob = bpy.data.objects.new(name, None)
        ob.empty_display_size = 0.1
        ob.location = (px, py, pz)
        bpy.context.scene.collection.objects.link(ob)
        return ob
    verts = [(v[0] - px, v[1] - py, v[2] - pz) for v in buf.verts]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], buf.faces)
    used = sorted(set(buf.mats))
    slot = {m: i for i, m in enumerate(used)}
    for m in used:
        me.materials.append(mats[m])
    uv = me.uv_layers.new(name="UVMap")
    li = 0
    for pi, poly in enumerate(me.polygons):
        poly.material_index = slot[buf.mats[pi]]
        poly.use_smooth = buf.smooth
        for c in range(poly.loop_total):
            uv.data[poly.loop_start + c].uv = buf.uvs[pi][c]
            li += 1
    me.update()
    if buf.normals is not None:
        me.normals_split_custom_set([buf.normals] * len(me.loops))
    bad = me.validate(verbose=False)
    if bad:
        fail(f"mesh '{name}' failed validation")
    ob = bpy.data.objects.new(name, me)
    ob.location = (px, py, pz)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def export_fbx(path):
    bpy.context.view_layer.update()
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=False,
        object_types={'MESH', 'EMPTY'},
        use_mesh_modifiers=False,
        mesh_smooth_type='FACE',
        use_custom_props=False,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True,
        axis_forward='-Z',
        axis_up='Y',
        bake_anim=False,
        path_mode='STRIP',
        embed_textures=False,
    )


def run_checks(P, man):
    """Every check as its own row; t-qa's known findings are matched by (check, where).

    Returns (rows, the known-findings list as matched, problems). Problems stop a full build: an unlisted failure, a
    listed one that got worse by more than 0.005 m, or a listed entry that matches no failing check."""
    ck = P["checks"]
    cor = man["corridor"]
    rows = []

    def row(name, where, value, bar, ok):
        rows.append({"name": name, "where": where, "value": value, "bar": bar, "status": "PASS" if ok else "FAIL"})
    for e in cor["lane_end_clearance"]:
        row("lane_end_clear", f"flight {e['flight']} {e['end']}", e["clear"], P["stairs"]["lane_end_clear_min"], e["clear"] >= P["stairs"]["lane_end_clear_min"])
    for f in man["flights"]:
        for end in ("foot", "head"):
            v = f[end + "_run_room"]
            row("run_room", f"flight {f['index']} {end}", v, ck["run_room_min"], v >= ck["run_room_min"])
    em = cor["eye_clearance_min"]
    row("eye_clear_lanes", f"flight {em['flight']}, {em['member']}", em["distance"], ck["eye_clear"], em["distance"] >= ck["eye_clear"])
    ts = cor["top_flight_standing"]
    row("eye_clear_top_flight_with_floor_edge_and_leaf", ts["member"], ts["distance"], ck["eye_clear"], ts["distance"] >= ck["eye_clear"])
    for e in cor["landing_eye_clearance"]:
        if e["distance"] is not None:
            row("eye_clear_landing", f"landing {e['landing']}", e["distance"], ck["eye_clear"], e["distance"] >= ck["eye_clear"])
    for e in cor["cab_eye_clearance"]:
        row("eye_clear_cab", f"hatch {e['hatch']}, sashes {e['sashes']}", e["distance"], ck["eye_clear"], e["distance"] >= ck["eye_clear"])
    so = cor["step_off"]
    row("eye_clear_step_off", "cab, flight 8 head to the nearest standing point", so["eye_min"], ck["eye_clear"], so["eye_min"] >= ck["eye_clear"])
    ends = [(f"flight {f['index']} {end}", f.get(end + "_mouth")) for f in man["flights"] for end in ("foot", "head")]
    ends += [(f"lane {x['index']} head", x.get("head_mouth")) for x in man["exit_lanes"]]
    for where, m in ends:
        for side in ("left", "right"):
            v = m[side]["free_end_mouth_distance"] if m else None
            if v is not None:
                row("envelope_mouth_guard", f"{where}, {side}", v, ck["envelope_mouth_guard"], v >= ck["envelope_mouth_guard"])
            v = m[side]["free_end_deck_inset"] if m else None
            if v is not None:
                row("envelope_deck_inset", f"{where}, {side}", v, ck["envelope_deck_inset_min"], v >= ck["envelope_deck_inset_min"])
            if m and "full_body_inside_band" in m[side]:
                row("ground_full_body_joinable", f"{where}, {side}", m[side]["full_body_outside_band_max"], 0.0, m[side]["full_body_inside_band"])
        if m:
            v = m["no_penetration"]["clear_of_solid_radius"]
            row("envelope_no_penetration", where, v, 0.0, v >= 0.0)
    for f in man["flights"]:
        uo = f.get("under_opening")
        if uo:
            row("under_opening_inside_band", f"flight {f['index']}", uo["largest_shortfall"], 0.0, uo["largest_shortfall"] <= 0.0)
    ends_j = [(f"flight {f['index']} {end}", f.get(end + "_junction"), f.get(end + "_run_out")) for f in man["flights"] for end in ("foot", "head")]
    ends_j += [(f"lane {x['index']} foot", x.get("foot_junction"), None) for x in man["exit_lanes"]]
    for where, j, ro in ends_j:
        if j:
            for side in ("left", "right"):
                lp = j["lip"][side]
                row("band_at_lip_is_flight_lip", f"{where}, {side}", lp["band_at_lane_end"], lp["flight_lip"], lp["equal"])
            if "bend" in j:
                ok_ = all(j["bend"][sd]["segment_2_carried_into_segment_1"]["fits"] for sd in ("left", "right"))
                row("bend_carry", where, j["bend"]["bend_deg"], 0.0, ok_)
        if ro is not None:
            row("run_out_keeps_frozen_behaviour", where, ro.get("bend_deg", 0.0), 0.0, "fail" not in ro and not ro.get("fallback", False))
    for h in cor["findings"]:
        row("corridor_frame_member", f"flight {h['flight']}, {h['member']}", h["intrusion_from_corridor_edge"], 0.0, False)
    for h in cor["exceptions"]:
        row("corridor_rail", f"flight {h['flight']}, {h['member']}", h["intrusion_from_corridor_edge"], 0.0, False)
    problems = []
    known = [dict(k) for k in ck.get("known_findings", [])]
    for k in known:
        k["matched"] = False
    for r in rows:
        if r["status"] == "PASS":
            continue
        hit = next((k for k in known if k["check"] == r["name"] and k["where"] == r["where"]), None)
        if hit is None:
            problems.append(f"unlisted failure {r['name']} [{r['where']}] {r['value']} against {r['bar']}")
            continue
        hit["matched"] = True
        r["status"] = "FAIL (known)"
        r["ref"] = hit["ref"]
        worse = (r["value"] - hit["value"]) if r["name"].startswith("corridor") else (hit["value"] - r["value"])
        if worse > 0.005:
            problems.append(f"{hit['ref']} {r['name']} [{r['where']}] got worse: {r['value']} (listed {hit['value']})")
    for k in known:
        if not k["matched"]:
            problems.append(f"{k['ref']} {k['check']} [{k['where']}] is listed but no longer fails: t-qa to remove it")
    return rows, known, problems


def write_json(path, data):
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        json.dump(data, fh, indent=1, sort_keys=True)
        fh.write("\n")


LICENSE_TEXT = """MC-39 lookout tower: texture sources and licences

Every texture in this folder ({name}_Albedo.png, {name}_Normal.png, {name}_Mask.png) is generated by
tools/blender/lookout/mc39_textures.py from seeded noise. No downloaded texture, scan or photograph is used.
The reference photographs of the Burns (Bell) tower were used to judge proportion and condition only; no pixel
of them was sampled, traced or projected.

Licence: the generated textures are original work of this project and ship under the project's own terms.
Third-party texture sources: none.

Numerals on the firefinder ring are meshes built from Blender's bundled font (Bfont, a derivative of
Bitstream Vera Sans; Bitstream Vera licence, which permits use in derived artwork without restriction).

Geometry follows the USDA Forest Service standard drawings T-1001-A to T-1007-A (Fickes 1935), which are
public-domain works of the US Government.
"""


def main():
    opts = parse_args()
    # The FBX writer derives object ids from Python's string hash, so the run has to have hashing pinned
    # (PYTHONHASHSEED=0 with --python-use-system-env, which build_mc39.ps1 sets) and the pinned Blender.
    if sys.flags.hash_randomization:
        fail("hash randomisation is on: run through build_mc39.ps1 (PYTHONHASHSEED=0 and --python-use-system-env)")
    if tuple(bpy.app.version) != BLENDER_VERSION:
        fail(f"Blender {'.'.join(map(str, BLENDER_VERSION))} required, found {bpy.app.version_string}")
    with open(opts["params"], "r", encoding="utf-8") as fh:
        P = json.load(fh)
    if opts["whole_mm_reach"]:
        import mc39_runouts
        mc39_runouts.REACH_BISECT = False
    name = P["name"]
    os.makedirs(opts["out"], exist_ok=True)
    clear_scene()
    scene = bpy.context.scene
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0

    B = build_buffers(P)
    mats = get_materials()
    tri = {}
    objects = []
    for oname, buf, pivot, role in B["objects"]:
        if buf is not None and not buf.faces:
            continue
        make_object(oname, buf, pivot, mats)
        entry = {"name": oname, "role": role, "pivot": [round(pivot[0], 5), round(pivot[2], 5), round(pivot[1], 5)]}
        if buf is not None:
            tri[oname] = buf.tri_count()
            entry["tris"] = buf.tri_count()
            entry["verts"] = len(buf.verts)
            entry["materials"] = [MATERIAL_NAMES[m] for m in sorted(set(buf.mats))] if role != "collision" else []
        objects.append(entry)

    def total(role):
        return sum(o.get("tris", 0) for o in objects if o["role"] == role)
    lod0_tris = total("lod0") + total("moving")
    counts = {"lod0": lod0_tris, "lod1": total("lod1"), "lod2": total("lod2"), "impostor": total("impostor"),
              "collision": total("collision"), "colliders": len([o for o in objects if o["role"] == "collision"])}
    if counts["lod1"] > 0.25 * lod0_tris or counts["lod2"] > 0.05 * lod0_tris:
        fail(f"LOD1 {counts['lod1']} / LOD2 {counts['lod2']} exceed 25% / 5% of LOD0 {lod0_tris}")
    if lod0_tris > P["budget"]["lod0_tris"]:
        fail(f"LOD0 is {lod0_tris} tris, over the budget of {P['budget']['lod0_tris']}")

    if not opts["no_textures"]:
        import mc39_textures
        mc39_textures.write_all(P, B, opts["out"])

    man = B["K"].manifest(counts, objects)
    man["checks"], man["known_findings"], bad = run_checks(P, man)
    man["build_of_record_for_listed_checks"] = not man["known_findings"]
    write_json(os.path.join(opts["out"], name + ".manifest.json"), man)
    with open(os.path.join(opts["out"], "TEXTURES_LICENSE.txt"), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(LICENSE_TEXT.format(name=name))

    fbx = os.path.join(opts["out"], name + ".fbx")
    export_fbx(fbx)
    if not os.path.isfile(fbx):
        fail("FBX was not written")

    L = B["L"]
    print(f"BUILD_MC39 standard={P['height_standard']} cab_floor={L.z_floor:.4f} base={L.base:.4f} flights={L.n_flights} risers={sum(L.risers)}")
    print(f"BUILD_MC39 tris lod0={counts['lod0']} lod1={counts['lod1']} lod2={counts['lod2']} impostor={counts['impostor']} collision={counts['collision']}")
    print(f"BUILD_MC39 colliders={counts['colliders']} panes_missing={man['cab']['panes_missing']}/72")
    print(f"BUILD_MC39 LANE_DATA_DIGEST {man['lane_data_digest']}")
    print(f"BUILD_MC39 INTERACTION_DATA_DIGEST {man['interaction_data_digest']}")
    for c in man["checks"]:
        if c["status"] != "PASS":
            print(f"BUILD_MC39 CHECK {c['name']} [{c['where']}] {c['status']} value={c['value']} bar={c['bar']}" + (f" ref={c['ref']}" if c.get("ref") else ""))
    n_pass = len([c for c in man["checks"] if c["status"] == "PASS"])
    n_known = len([c for c in man["checks"] if c["status"] == "FAIL (known)"])
    print(f"BUILD_MC39 CHECKS pass={n_pass} fail_known={n_known} fail_unlisted={len([c for c in man['checks'] if c['status'] == 'FAIL'])}")
    if bad and not opts["no_textures"]:
        fail("checks: " + "; ".join(bad))
    for m_ in bad:
        print("BUILD_MC39 CHECK PROBLEM (dev build, not stopped): " + m_)
    mods = sorted({os.path.basename(m.__file__) for m in list(sys.modules.values())
                   if getattr(m, "__file__", None) and os.path.dirname(os.path.abspath(m.__file__)) == HERE} | {os.path.basename(__file__)})
    print("BUILD_MC39 MODULES " + ",".join(mods))
    print(f"BUILD_MC39 OK fbx={fbx}")
    sys.stdout.flush()


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception:
        import traceback
        traceback.print_exc()
        fail("exception (see traceback)")
