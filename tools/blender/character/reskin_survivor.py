"""P4 re-skin: his Meshy mesh onto Skeleton_Human, split head and body, export two FBXs (pod 49, td-49 adr/49-p4-realistic-skeleton item 4).

blender -b -noaudio -P reskin_survivor.py -- --src <source/Meshy_Survivor_char.fbx> --game <tools/blender/character/source/Farmer_Body_template.fbx> --skel <source/skeleton_human.json> --tex <Assets/Art/Character/Survivor/Survivor_BaseColor.png>
    [--metal <source/Meshy_Survivor_metallic.png> --rough <source/Meshy_Survivor_roughness.png>] --outdir <dir> [--dry] [--force-fail swap_sides]

--game is only the TEMPLATE (source/Farmer_Body_template.fbx, byte-identical to the Farmer's Assets copy; kept as source data) (bone names, hierarchy, finger directions); every position comes from --skel. Rotation-only repose, no scaling; the Survivor
appearance carries skeletonScale = 1.70 / his eye (printed). Hands and fingers are reposed (F7), knees and elbows get a wider blend band (DK1), the
watch has one identical weight set. Prints the F12 list. Fails closed: if any check fails nothing is left behind (exit 2 for a refused check, 3 for a crash).
"""
import hashlib
import json
import os
import sys

import bpy
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_skeleton_human as K  # noqa: E402
import reskin_checks as C  # noqa: E402
import reskin_io as R  # noqa: E402
import reskin_map as M  # noqa: E402
from reskin_body import fit_body, load_skeleton  # noqa: E402
from reskin_split import Fail, build_object, export_fbx, git_hashes, backing_patches, boundary_caps, cap_double_sided, cap_corner_normals, exported_open_edges, open_edges, open_loops, pos_key, split_polys, verify_export  # noqa: E402
from reskin_tex import pack_metallic_gloss, skin_texel_uv  # noqa: E402

SELFINT_LIMIT = 10   # td-49: front/side pairs per knee with the shape, a disclosed limit pending qa's ray grid and the Squat_Hold re-check
OUTPUTS = ("Survivor_Head.fbx", "Survivor_Body.fbx", "Survivor_MetallicGloss.png", "reskin_report.json", "_template_roundtrip.fbx")


def reimport_hash(path):
    """R4: deterministic content hash of one exported piece, taken from the RE-IMPORTED FBX (positions, normals, weights, UVs, polygons)."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    ob = next(o for o in bpy.data.objects if o.type == "MESH")
    me = ob.data
    h = hashlib.sha1()
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    h.update(np.round(co, 5).tobytes())
    uv = np.empty(len(me.loops) * 2)
    me.uv_layers[0].data.foreach_get("uv", uv)
    h.update(np.round(uv, 5).tobytes())
    h.update(np.array([len(p.vertices) for p in me.polygons], dtype=np.int32).tobytes())
    h.update(np.array([v for p in me.polygons for v in p.vertices], dtype=np.int32).tobytes())
    try:
        cn = np.empty(len(me.loops) * 3)
        me.corner_normals.foreach_get("vector", cn)
        h.update(np.round(cn, 4).tobytes())
    except Exception:  # noqa: BLE001
        h.update(b"no-corner-normals")
    if me.shape_keys:
        for kb in me.shape_keys.key_blocks:
            kc = np.empty(len(me.vertices) * 3)
            kb.data.foreach_get("co", kc)
            h.update(kb.name.encode() + np.round(kc, 5).tobytes())
    names = [g.name for g in ob.vertex_groups]
    for v in me.vertices:
        h.update(repr(sorted((names[g.group], round(g.weight, 4)) for g in v.groups)).encode())
    return h.hexdigest()


ENV = {}
ARGS_REC = {}
SOURCE_PROP = "Meshy AI (meshy.ai), meshy-quick-retarget export, Pro plan"


def main():
    o = R.argv()
    ENV.update({"RESKIN_FINGERS": os.environ.get("RESKIN_FINGERS", "mitten"), "RESKIN_BAND_SHIFT": os.environ.get("RESKIN_BAND_SHIFT", "0.0"),
                "RESKIN_SMAX": os.environ.get("RESKIN_SMAX", "2.0"), "RESKIN_CREASE": os.environ.get("RESKIN_CREASE", "0.0"), "RESKIN_KEEP": os.environ.get("RESKIN_KEEP", "unset")})
    print("ENV (defaults: mitten, 0.0, 2.0, 0.0, unset):", ENV, "ARGS:", dict(sorted(o.items())))
    ARGS_REC.update({k: v for k, v in sorted(o.items())})   # as typed (repo-relative when run from the repo root); the paths are made absolute below for file access
    for k in ("src", "game", "skel", "tex", "metal", "rough", "outdir", "check_export"):
        if o.get(k):
            o[k] = os.path.abspath(o[k])
    if ENV["RESKIN_KEEP"] != "unset":   # RESKIN_KEEP keeps a refused export on disk: a diagnostic aid, never for a release run
        raise Fail("RESKIN_KEEP is set (%s): it is for diagnosis only; unset it for any run that produces files of record" % ENV["RESKIN_KEEP"])
    if ENV["RESKIN_FINGERS"] != "mitten":
        raise Fail("RESKIN_FINGERS=%s: only the mitten hands ship in 49 (F7' is rejected, F7'' waits for #35)" % ENV["RESKIN_FINGERS"])
    for k in ("skel", "outdir", "src", "game", "tex"):
        if not o.get(k):
            raise Fail("missing --%s" % k)
    garm, game = R.load_game(o["game"])
    skel = load_skeleton(o["skel"])
    if set(skel) != set(game):
        raise Fail("skeleton and template bone names differ")
    b = fit_body(o, game, skel)
    V3, N3, G, gnames, polys = b["V"], b["N"], b["G"], b["gnames"], b["polys"]
    me = b["mesh"].data
    print("SOLE height (game frame, m): %.4f" % float(V3[:, 2].min()))
    eye = R.measure_eye(V3, b["W"], b["names"], b["lum"], None)  # the FINAL mesh (legs straightened to the T bind make him about 1 cm taller than at rest)
    print("EYE %.4f m above the floor (%s); body-frame target %.2f; skeletonScale %.4f here is SUPERSEDED (the shipped scale is 1.70 / his eye in Stand_Idle, 1.0156, from speeds-Survivor.json); the skeleton stays true size" % (
        eye[0], eye[1], M.EYE_STAND, M.EYE_STAND / eye[0]))
    height = float(V3[:, 2].max())
    scaled = height * M.EYE_STAND / eye[0]
    print("HEIGHT: mesh %.3f m, scaled by skeletonScale %.4f = %.3f m (Noah accepted 1.85 m; the band is %.2f-%.2f m)" % (height, M.EYE_STAND / eye[0], scaled, M.HEIGHT_BAND[0], M.HEIGHT_BAND[1]))
    if not (M.HEIGHT_BAND[0] <= scaled <= M.HEIGHT_BAND[1]):
        print("HEIGHT ROUTE: %.3f m is outside %.2f-%.2f m: route to team-lead for Noah (the run carries on)" % (scaled, M.HEIGHT_BAND[0], M.HEIGHT_BAND[1]))
    if not (M.HEIGHT_STOP[0] <= scaled <= M.HEIGHT_STOP[1]):
        raise Fail("scaled height %.3f m is outside the hard stop %.2f-%.2f m" % (scaled, M.HEIGHT_STOP[0], M.HEIGHT_STOP[1]))
    print("JOINT OFFSETS, skeleton vs his own joints (m; limits elbow 0.02, wrist 0.02, knee 0.03):", {k: round(v, 4) for k, v in b["joint_offsets"].items()})
    cross = {}
    for side in ("L", "R"):
        cross["knee." + side] = C.cross_ratio(V3, G, gnames, skel, "UpperLeg", "LowerLeg", side, 90, (1, 0, 0))
        cross["elbow." + side] = C.cross_ratio(V3, G, gnames, skel, "UpperArm", "LowerArm", side, -90 if side == "L" else 90, (0, 0, 1))
    print("CROSS-SECTION area at 90 degrees over rest (above 5 cm, joint plane, below 5 cm; floor %.2f):" % C.CROSS_FLOOR)
    for k, v in cross.items():
        print("  %-8s %s" % (k, v))
    # td ruling 1: the joint plane is reported only; the planes at +-5 cm each >= 0.80, the +-6 cm mean >= 0.80, no plane in the band below 0.65
    low = {}
    for k, dd in cross.items():
        if k.startswith("knee."):
            continue   # the knees are judged WITH the corrective shape (td Update 2: every band plane at least 0.90 at weight 1)
        why = []
        for pl in ("above5cm", "below5cm"):
            if dd[pl] is not None and dd[pl] < 0.80:
                why.append("%s %.3f < 0.80" % (pl, dd[pl]))
        if dd["planes_mean"] is not None and dd["planes_mean"] < 0.80:
            why.append("band mean %.3f < 0.80" % dd["planes_mean"])
        if dd["planes_min"] is not None and dd["planes_min"] < 0.65:
            why.append("plane %.2f m is %.3f < 0.65" % (dd["planes_min_at_m"], dd["planes_min"]))
        if why:
            low[k] = why
    if low and not o.get("allow_dk1"):
        raise Fail("DK1 cross-section floors not met: %s (a corrective blend shape would be a PENDING tech item)" % low)
    if low:
        print("DK1 NOT MET (--allow-dk1 given, so this run is a diagnostic only): %s" % low)
    # td-knee-corrective.md: Corrective_Knee.L/R, knees only (the elbows passed), authored here at the provisional 90 degrees (item 3 prints the shipped largest knee flex)
    shapes, shape_report = {}, {}
    which = o.get("correctives", "knees").split(",")
    shin = {}
    for side in ("L", "R"):
        shin[side] = C.shin_checks(V3, G, gnames, skel, side, 90, b["collars"][side])
    print("UPDATE 2 SHIN CHECKS at 90 degrees, IK foot on the shin end (limits: centreline 0.010 m, boot RMS 0.002 m):", shin)
    bad = {k: v for k, v in shin.items() if v["shin_centreline_max_dev_m"] > 0.010 or v["boot_rms_m"] > 0.002}
    if bad and not o.get("allow_shin"):
        raise Fail("shin straightness or boot rigidity out of limits at 90 degrees: %s" % bad)
    for side in ("L", "R"):
        kt = float(o.get("knee_target", 90))   # the shape is authored at this flexion (td: the shipped largest knee flex; 90 until item 3 prints it)
        key = "Knee_%s_%d" % (side, int(kt))
        pl = cross["knee." + side]["planes"] if kt == 90 else C.cross_ratio(V3, G, gnames, skel, "UpperLeg", "LowerLeg", side, kt, (1, 0, 0))["planes"]
        target = dict(pl)
        for _ in range(4):   # refine: the radial push about the axis point is not exactly area-preserving, so re-aim at the planes still short
            dl = C.corrective(V3, G, gnames, skel, "UpperLeg", "LowerLeg", side, kt, (1, 0, 0), target)
            after = C.cross_ratio(V3 + dl, G, gnames, skel, "UpperLeg", "LowerLeg", side, kt, (1, 0, 0), rest=V3)
            if after["planes_min"] >= 0.95:
                break
            target = {k: target[k] * min(after["planes"].get(k, 1.0), 1.0) for k in target}
        shapes[key] = dl
        # self-intersections in the knee band: at rest, bent without the shape, bent with it (the shape must not add any)
        sgn = 1.0 if side == "L" else -1.0
        jn = skel["LowerLeg." + side]["h"]
        band = (np.abs(V3[:, 2] - jn[2]) < 0.22) & (V3[:, 0] * sgn > -0.02)
        w0 = C.self_intersections(V3, polys, b["weld"], band)
        pw0, _ = C.pose(V3, G, gnames, skel, {"LowerLeg." + side: ((1, 0, 0), kt)})
        pw1, _ = C.pose(V3 + dl, G, gnames, skel, {"LowerLeg." + side: ((1, 0, 0), kt)})
        kax = (jn - skel["UpperLeg." + side]["h"]) / np.linalg.norm(jn - skel["UpperLeg." + side]["h"])
        w1 = C.self_intersections_classified(pw0, V3, polys, b["weld"], band, jn, kax)
        w2 = C.self_intersections_classified(pw1, V3, polys, b["weld"], band, jn, kax)
        print("  outside-crease pairs with the shape: along-axis (min, median, max) m %s, fraction on the back of the knee %s, pairs not BOTH on the back (visible on front or sides): %s" % (C.classify_report["last"]["outside_along_m"], C.classify_report["last"]["outside_on_back_fraction"], C.classify_report["last"]["outside_not_both_on_back"]))
        shape_report[key] = {"vertices": int((np.abs(dl).max(1) > 0).sum()), "max_delta_mm": round(1000 * float(np.linalg.norm(dl, axis=1).max()), 2), "planes_before": pl,
                             "planes_after": after["planes"], "self_intersections": {"rest": w0, "bent_no_shape_total_crease_outside": list(w1), "bent_with_shape_total_crease_outside": list(w2)}}
        print("CORRECTIVE %-18s vertices %d, max delta %.1f mm; band planes before %s -> after %s; self-intersecting pairs (total, back-of-knee crease, FRONT/SIDE): rest %d, bent without %s, bent with %s" % (
            key, shape_report[key]["vertices"], shape_report[key]["max_delta_mm"], pl, after["planes"], w0, w1, w2))
        if max(after["planes"].values()) > 1.10:
            raise Fail("%s: the shape over-inflates (a band plane above 1.10: %s)" % (key, after["planes"]))
        if after["planes_min"] is None or after["planes_min"] < 0.90:
            raise Fail("%s: the shape does not restore the band (worst plane %s after, limit 0.90)" % (key, after["planes_min"]))
        print("  front/side overlaps (not both on the back of the knee), without the shape %d, with the shape %d (disclosed limit: at most %d per knee with the shape; all hidden inside the trouser fold, td-49)" % (w1[2], w2[2], SELFINT_LIMIT))
        if w2[2] > SELFINT_LIMIT:
            raise Fail("%s: %d front/side self-intersections with the shape at 90 degrees (limit %d)" % (key, w2[2], SELFINT_LIMIT))
    # SECOND KNEE SHAPE (td-49 SHELL FIX TECH RULES K1-K5, director gate/pod49-shell-fix-preference): Knee_L/R_135 authored independently from rest at --knee2-target degrees, stored as the INCREMENT over the 90 shape
    # (delta135 - delta90), so the 90 shape is untouched and at flexion >= 135 the evaluated delta is the full 135 correction. Limits (disclosed, td-49's choice): every band plane 0.65..1.25, joint plane >= 0.80,
    # front/side self-intersections with the shape <= without it, K11 0.010 m / K12 0.002 m at that flexion. A plane above 1.15 also needs the director's look OK on the stills. Never loosened here.
    knee2 = {}
    if o.get("knee2_target"):
        k2 = float(o["knee2_target"])
        for side in ("L", "R"):
            kn = "Knee_%s_%d" % (side, int(k2))
            pl2 = C.cross_ratio(V3, G, gnames, skel, "UpperLeg", "LowerLeg", side, k2, (1, 0, 0))["planes"]
            target = dict(pl2)
            tune = {}   # --knee2-tune "L:0.02:0.95,R:..." sets that plane's target to measured ratio x factor on that side (td-49 option 1a, left-knee tune); echoed in the report
            for t in (o.get("knee2_tune") or "").split(","):
                if t.strip():
                    sd, pk, fk = t.split(":")
                    if sd == side:
                        tune[pk] = float(fk)
            for pk in tune:
                if pk not in target:
                    raise Fail("--knee2-tune: no plane %s on side %s (planes %s)" % (pk, side, sorted(target)))
            for _ in range(4):
                for pk, fk in tune.items():
                    target[pk] = min(1.0, pl2[pk] * fk)   # tuned planes keep base ratio x factor on every pass (no re-aim)
                dl2 = C.corrective(V3, G, gnames, skel, "UpperLeg", "LowerLeg", side, k2, (1, 0, 0), target)
                after2 = C.cross_ratio(V3 + dl2, G, gnames, skel, "UpperLeg", "LowerLeg", side, k2, (1, 0, 0), rest=V3)
                if after2["planes_min"] >= 0.95:
                    break
                target = {k: target[k] * min(after2["planes"].get(k, 1.0), 1.0) for k in target}
            inc = dl2 - shapes["Knee_%s_90" % side]
            sgn = 1.0 if side == "L" else -1.0
            jn = skel["LowerLeg." + side]["h"]
            band = (np.abs(V3[:, 2] - jn[2]) < 0.22) & (V3[:, 0] * sgn > -0.02)
            kax = (jn - skel["UpperLeg." + side]["h"]) / np.linalg.norm(jn - skel["UpperLeg." + side]["h"])
            pw0, _ = C.pose(V3, G, gnames, skel, {"LowerLeg." + side: ((1, 0, 0), k2)})
            pw1, _ = C.pose(V3 + dl2, G, gnames, skel, {"LowerLeg." + side: ((1, 0, 0), k2)})
            wa = C.self_intersections_classified(pw0, V3, polys, b["weld"], band, jn, kax)
            wb = C.self_intersections_classified(pw1, V3, polys, b["weld"], band, jn, kax)
            print("KNEE2 %s overlaps with the shape: along-axis (min, median, max) m %s, fraction on the back of the knee %s, pairs not BOTH on the back %s" % (kn, C.classify_report["last"]["outside_along_m"], C.classify_report["last"]["outside_on_back_fraction"], C.classify_report["last"]["outside_not_both_on_back"]))
            sh2 = C.shin_checks(V3 + dl2, G, gnames, skel, side, k2, b["collars"][side])
            part = {}
            for label, flex, dd in (("112.5 (w135 0.5)", 0.5 * (90 + k2), shapes["Knee_%s_90" % side] + 0.5 * inc), ("%d" % int(k2), k2, dl2), ("169 (w135 1)", 169.0, dl2)):
                part[label] = C.cross_ratio(V3 + dd, G, gnames, skel, "UpperLeg", "LowerLeg", side, flex, (1, 0, 0), rest=V3)
            allp = [v for r in part.values() for v in r["planes"].values()]
            joint = after2["planes"].get("0.0")
            print("KNEE2 %s: planes before %s -> after (full delta at %d) %s; max delta %.1f mm, vertices %d" % (kn, pl2, int(k2), after2["planes"], 1000 * float(np.linalg.norm(dl2, axis=1).max()), int((np.abs(dl2).max(1) > 0).sum())))
            print("KNEE2 %s: K5 planes (min, max) at 112.5: %s, at %d: %s, at 169: %s; joint plane at %d: %s; self-intersections front/side without %d with %d; shin %s" % (
                kn, (min(part["112.5 (w135 0.5)"]["planes"].values()), max(part["112.5 (w135 0.5)"]["planes"].values())), int(k2), (min(part[str(int(k2))]["planes"].values()), max(part[str(int(k2))]["planes"].values())),
                (min(part["169 (w135 1)"]["planes"].values()), max(part["169 (w135 1)"]["planes"].values())), int(k2), joint, wa[2], wb[2], sh2))
            bad2 = []
            if min(allp) < 0.65 or max(allp) > 1.25:
                bad2.append("a plane outside 0.65..1.25 (min %.3f, max %.3f)" % (min(allp), max(allp)))
            if joint is not None and joint < 0.80:
                bad2.append("joint plane %.3f < 0.80" % joint)
            if wb[2] > wa[2]:
                bad2.append("front/side overlaps with the shape %d > without %d" % (wb[2], wa[2]))
            if sh2["shin_centreline_max_dev_m"] > 0.010 or sh2["boot_rms_m"] > 0.002:
                bad2.append("K11/K12 %s" % sh2)
            if max(allp) > 1.15:
                print("KNEE2 %s: a plane above 1.15 (%.3f): needs the director's look OK on the stills" % (kn, max(allp)))
            knee2[kn] = {"planes_before": pl2, "planes_after": after2["planes"], "k5": {k: v["planes"] for k, v in part.items()}, "self_intersections_front_side": {"without": wa[2], "with": wb[2]},
                         "shin": sh2, "max_delta_mm": round(1000 * float(np.linalg.norm(dl2, axis=1).max()), 2), "violations": bad2}
            if bad2 and not o.get("allow_k4"):
                raise Fail("%s: %s (PENDING to td-49 with these figures; the script does not loosen the limits)" % (kn, "; ".join(bad2)))
            shapes[kn] = inc
            shape_report[kn] = {"increment_over_90": True, "vertices": int((np.abs(inc).max(1) > 0).sum()), "max_delta_mm": round(1000 * float(np.linalg.norm(inc, axis=1).max()), 2)}
    edges = np.array([[e.vertices[0], e.vertices[1]] for e in me.edges])
    sw = C.watch_flex(V3, G, gnames, skel, b["watch"], edges, 50, b["watch_band"])
    print("S3 WATCH at the wrist-flex extremes (size change limit 0.1 mm; hand-side band edge ratio p99 limit 1.5):", sw)
    if any(v["size_change_mm"] > 0.1 for v in sw.values()) or (any(v.get("band_edge_p99", 0) > 1.5 for v in sw.values()) and not o.get("allow_band")):
        raise Fail("S3: the watch size or its hand-side band is out of limits at the wrist-flex extreme: %s" % sw)
    # DK3: the web stretch is judged at the shipped clips' largest thumb bend (item 3 measures it; until then PROVISIONAL, --thumb-bend a,b,c degrees per bone)
    bend = tuple(float(x) for x in o.get("thumb_bend", "20,25,30").split(","))
    webm = {}
    for label, degs in (("provisional shipped bend %s" % (bend,), bend), ("severe (40, 45, 50)", (40, 45, 50))):
        webm[label] = {}
        for side in ("L", "R"):
            if not b["fingers"][side].get("fallback"):
                webm[label][side] = C.thumb_web(V3, G, gnames, skel, edges, side, degs)
        print("DK3 THUMB WEB edge-length ratio at %s (limits p99 1.5, max 3.0):" % label, webm[label])
    bad = {k: v for k, v in list(webm.values())[0].items() if v["p99"] > 1.5 or v["max"] > 3.0}
    if bad:
        raise Fail("DK3: the thumb web stretches beyond the limits at the provisional shipped bend: %s" % bad)
    s1 = {"mesh_before": round(C.mirror_p95(b["V0"]), 4), "mesh_after": round(C.mirror_p95(V3), 4)}
    print("S1 SHOULDER MIRROR p95 (m): before the repose %.4f, after %.4f" % (s1["mesh_before"], s1["mesh_after"]))
    loop_uv = [[tuple(u) for u in b["loop_uv"][p.loop_start:p.loop_start + p.loop_total]] for p in me.polygons]
    hw = sum(b["W"][:, b["names"].index(n)] for n in M.HEAD_BONES if n in b["names"])
    hv = hw >= 0.5
    head_ids, body_ids = split_polys(polys, hv)
    src_open = open_edges(polys, b["weld"])
    caps, cap_report = boundary_caps(polys, body_ids, V3, G, b["uv_v"], b["weld"], src_open, lum=b["lum"])
    print("SPLIT head polys %d, body polys %d; boundary caps %d components, %d edges: %s" % (len(head_ids), len(body_ids), len(caps), sum(c["edges"] for c in cap_report), cap_report))
    if not caps:
        raise Fail("no neck boundary found to cap")
    # BACKING PATCHES (td-49 SHELL FIX TECH RULES H1-H5, director-49 patch colour): --patch-dark / --patch-skin "zlo,zhi,vmin,vmax,expect" select open loops by a z band and a vertex-count range and must match exactly `expect` loops
    patches, patch_report = [], []
    for kind in ("dark", "skin"):
        if o.get("patch_" + kind):
            zlo, zhi, vmin, vmax, expect = (float(x) for x in o["patch_" + kind].split(","))
            loops = open_loops(polys, body_ids, V3, b["weld"], src_open)
            def uv_for(centre, kd):
                if kd == "dark":
                    return np.array(cap_report[0]["collar_texel"]["uv"])
                uv, col, n = skin_texel_uv(o["tex"], b["uv_v"], V3, centre)
                print("PATCH skin texel at %s: uv %s, colour %s, %d skin candidates" % (np.round(centre, 3).tolist(), np.round(uv, 4).tolist(), np.round(col, 3).tolist(), n))
                return uv
            got = backing_patches(loops, (zlo, zhi, vmin, vmax, int(expect), kind), V3, N3, G, b["uv_v"], uv_for, float(o.get("patch_offset", 0.008)), float(o.get("patch_scale", 1.4)))
            patches += got
            patch_report += [g["report"] for g in got]
    if patches:
        print("PATCH backing patches: %d: %s" % (len(patches), patch_report))
    # DK4 (before export): every boundary edge of the Body piece that the source did not already have is capped; none may sit on a hand
    body_polys = [polys[i] for i in body_ids]
    new_open = open_edges(body_polys, b["weld"]) - src_open
    hand_w = {}
    for side, hn in (("L", "mixamorig:LeftHand"), ("R", "mixamorig:RightHand")):
        hmask = b["W"][:, b["names"].index(hn)] >= 0.5
        hw_ids = {int(w) for w in b["weld"][hmask]}
        hand_w[side] = sum(1 for e in new_open if e[0] in hw_ids or e[1] in hw_ids)
    print("DK4 (pre-export) boundary edges of the Body piece not open in the source, all capped: total %d, left hand %d, right hand %d (source open edges: %d)" % (len(new_open), hand_w["L"], hand_w["R"], len(src_open)))
    # qa-49: the source's own open edges by body region on the shipped mesh (head piece = hidden in first person but in the shadow; body piece = visible in first person)
    rep = {}
    for i, w in enumerate(b["weld"]):
        rep.setdefault(int(w), i)

    def region(v):
        n = gnames[int(np.argmax(G[v]))]
        base = n.split(".")[0]
        sd = n[-1] if n[-2:] in (".L", ".R") else ""
        if base in ("Head", "Neck"):
            return "head/neck"
        if base in ("Wrist",) or base in M.FINGER_BONES:
            return "hand." + sd
        if base in ("Shoulder", "UpperArm", "LowerArm"):
            return "arm." + sd
        if base in ("Chest", "Torso", "Abdomen", "Hips", "Body"):
            return "torso/hips"
        if base in ("UpperLeg", "LowerLeg"):
            return "leg." + sd
        return "foot." + sd if base == "Foot" else base
    open_by = {}
    for e in src_open:
        v = rep[e[0]]
        key = (region(v), "head piece" if hv[v] else "body piece")
        pts = open_by.setdefault(key, [])
        pts.append(V3[v])
    print("SOURCE OPEN EDGES by region on the shipped mesh (%d in all; head piece = hidden in first person, in the shadow; body piece = in the first-person body):" % len(src_open))
    open_report = {}
    for (rg, pc), pts in sorted(open_by.items()):
        p = np.array(pts)
        print("  %-12s %-11s %3d edges, x %.2f..%.2f, y %.2f..%.2f, z %.2f..%.2f" % (rg, pc, len(p), p[:, 0].min(), p[:, 0].max(), p[:, 1].min(), p[:, 1].max(), p[:, 2].min(), p[:, 2].max()))
        open_report["%s|%s" % (rg, pc)] = {"edges": int(len(p)), "centre": p.mean(0).round(3).tolist()}
    adj = {}
    for e0, e1 in src_open:
        adj.setdefault(e0, set()).add(e1)
        adj.setdefault(e1, set()).add(e0)
    seen, loops = set(), []
    for s0 in adj:
        if s0 in seen:
            continue
        st, comp = [s0], []
        seen.add(s0)
        while st:
            cc = st.pop()
            comp.append(cc)
            for nb in adj[cc]:
                if nb not in seen:
                    seen.add(nb)
                    st.append(nb)
        kind = "closed ring" if all(len(adj[v]) == 2 for v in comp) else "open strip"
        v0 = rep[comp[0]]
        loops.append({"kind": kind, "nodes": len(comp), "region": region(v0), "piece": "head piece" if hv[v0] else "body piece", "z": round(float(np.mean([V3[rep[v]][2] for v in comp])), 2)})
    print("SOURCE OPEN-EDGE LOOPS (closed ring or open strip, size in vertices):")
    for lp in sorted(loops, key=lambda q: (q["piece"], q["region"], -q["nodes"])):
        print("  %-11s %-12s %-11s %3d vertices at z %.2f" % (lp["piece"], lp["region"], lp["kind"], lp["nodes"], lp["z"]))
    open_report["loops"] = loops
    if hand_w["L"] or hand_w["R"]:
        raise Fail("DK4: the cut would open the hands (left hand %d, right hand %d)" % (hand_w["L"], hand_w["R"]))
    body_src_open = sum(v["edges"] for k, v in open_report.items() if isinstance(v, dict) and k.endswith("|body piece"))
    rep_v = {}
    for i, w in enumerate(b["weld"]):
        rep_v.setdefault(int(w), i)
    expected_open = {tuple(sorted((pos_key(V3[rep_v[x]]), pos_key(V3[rep_v[y]])))) for x, y in (open_edges(body_polys, b["weld"]) & src_open)}
    for pt in patches:   # a patch is a free triangle: its own three edges are boundary edges of the export (the shell's open edges stay exactly the source's)
        pk = [pos_key(x) for x in pt["points"]]
        for i0, i1 in ((0, 1), (1, 2), (2, 0)):
            expected_open.add(tuple(sorted((pk[i0], pk[i1]))))
    if o.get("check_export"):   # X2: judge a given Body FBX with the same set-based rule, export nothing
        eo = exported_open_edges(o["check_export"])
        extra, missing = eo["positions"] - expected_open, expected_open - eo["positions"]
        if os.environ.get("RESKIN_DEBUG_SET"):
            print("DEBUG expected", sorted(expected_open)[:3], "found", sorted(eo["positions"])[:3])
        print("CHECK-EXPORT %s: open edges %d, expected (source's body-piece set) %d, extra %d, missing %d; loops %s" % (o["check_export"], eo["edges"], len(expected_open), len(extra), len(missing), eo["loops"]))
        if extra or missing:
            raise Fail("CHECK-EXPORT: %d open edges not in the source's set (first %s), %d expected ones missing" % (len(extra), sorted(extra)[:3], len(missing)))
        print("CHECK-EXPORT PASS")
        return
    report = {"skeleton_scale_superseded": round(M.EYE_STAND / eye[0], 4), "skeleton_scale_note": "SUPERSEDED: the shipped scale is 1.0156 (1.70 / the Stand_Idle eye, speeds-Survivor.json)", "args": ARGS_REC, "env": ENV, "fingers": b["fingers"], "hand_edge_stats": b["edge_stats"], "band": b["band"], "cross": cross,
              "watch": {"vertices": int(len(b["watch"])), "weights": b["watch_weights"], "max_weight_diff": b["watch_diff"], "flex": {str(k): v for k, v in sw.items()}},
              "s1": s1, "counts": b["counts"], "source_open_edges": open_report, "correctives": shape_report, "shin_checks": shin, "collars": {k: round(float(v), 3) for k, v in b["collars"].items()}, "split": {"head": len(head_ids), "body": len(body_ids), "neck_cap": cap_report, "patches": patch_report}, "joint_offsets": b["joint_offsets"]}
    if o["dry"]:
        report["dry"] = True
        write(o, report)
        return
    os.makedirs(o["outdir"], exist_ok=True)
    if o.get("metal") and o.get("rough"):
        tp = os.path.join(o["outdir"], "Survivor_MetallicGloss.png")
        sha, chk = pack_metallic_gloss(o["metal"], o["rough"], tp)
        print("PACK MetallicGloss sha1", sha, chk, "git hashes", git_hashes(tp, "Assets/Art/Character/Survivor/Survivor_MetallicGloss.png"))
        if not chk["inverted_ok"]:
            raise Fail("roughness inversion check failed: %s" % chk)
        report["metallic_gloss"] = {"path": tp, "sha1": sha, "check": chk, "hashes": git_hashes(tp, "Assets/Art/Character/Survivor/Survivor_MetallicGloss.png")}
    material = me.materials[0]
    bpy.data.objects.remove(garm, do_unlink=True)   # the template armature holds the name CharacterArmature
    order = sorted(skel, key=lambda n: K.R_depth(skel, n))
    arm = K.make_armature(skel, order)
    paths = {}
    for nm, ids, cp in (("Survivor_Head", head_ids, []), ("Survivor_Body", body_ids, caps)):
        obj, nv, npoly = build_object(nm, V3, polys, loop_uv, N3, G, gnames, ids, material, cp, arm, (b["LN"], [p.loop_start for p in me.polygons]), shapes if nm == "Survivor_Body" else None, patches if nm == "Survivor_Body" else ())
        obj["source"] = SOURCE_PROP   # Meshy's terms: keep the AI-identifying source note (Blender's re-export replaces the exporter header)
        p = os.path.join(o["outdir"], nm + ".fbx")
        export_fbx(arm, obj, p, bool(o.get("fbx_scale_all")))
        bpy.data.objects.remove(obj, do_unlink=True)
        paths[nm] = {"path": p, "verts": nv, "polys": npoly}
    floor = K.roundtrip_floor(o["game"], o["outdir"])
    tol = floor + 0.01
    print("ROUND-TRIP FLOOR of the untouched template: %.4f degrees; tolerance floor + 0.01 = %.4f" % (floor, tol))
    for nm in paths:
        paths[nm]["verify"] = verify_export(paths[nm]["path"], skel, nm, tol, tuple(shapes) if nm == "Survivor_Body" else ())
        paths[nm]["hashes"] = git_hashes(paths[nm]["path"], "Assets/Art/Character/Survivor/%s.fbx" % nm)
        paths[nm]["content_hash"] = reimport_hash(paths[nm]["path"])
        eo = exported_open_edges(paths[nm]["path"])
        paths[nm]["exported_open"] = {k: v for k, v in eo.items() if k != "positions"}
        print("EXPORT OPEN EDGES %s (re-imported, welded by position 1e-5): %d, source open edges on this piece: %s; loops %s; source property: %r" % (
            nm, eo["edges"], body_src_open if nm == "Survivor_Body" else "n/a (head rim open by design)", eo["loops"], eo["source_property"]))
        if nm == "Survivor_Body":
            ncap, nmiss = cap_double_sided(paths[nm]["path"])
            print("EXPORT CAP (Body, FBX-direct): %d cap triangles (both windings), %d without their reverse" % (ncap, nmiss))
            ncorner, nok, nchanged, nnon = cap_corner_normals(paths[nm]["path"], o.get("normals_ref"))
            print("EXPORT CAP corner normals (FBX-direct): %d/%d = (0,0,-1); non-cap loop normals changed vs the reference (%s): %s of %d" % (nok, ncorner, o.get("normals_ref") or "none given", nchanged, nnon))
            if nok != ncorner or (o.get("normals_ref") and nchanged):
                raise Fail("cap corner normals %d/%d are (0,0,-1); non-cap loop normals changed vs the reference: %s" % (nok, ncorner, nchanged))
            if nmiss or ncap != 2 * sum(c["edges"] for c in cap_report):
                raise Fail("the cap is not double-sided: %d triangles, %d without a reverse" % (ncap, nmiss))
        if eo["source_property"] != SOURCE_PROP:
            raise Fail("%s: the Meshy source property did not survive the FBX round trip: %r" % (nm, eo["source_property"]))
        if nm == "Survivor_Body":
            extra, missing = eo["positions"] - expected_open, expected_open - eo["positions"]
            print("EXPORT OPEN-EDGE SET (Body): extra %d, missing %d against the source's body-piece set of %d" % (len(extra), len(missing), len(expected_open)))
            if extra or missing:
                raise Fail("DK4 (exported): the re-imported Body's open edges differ from the source's set: extra %d (first %s), missing %d" % (len(extra), sorted(extra)[:3], len(missing)))
        print("EXPORT %s raw/LFS-pointer %s, content hash (re-imported) %s, %s" % (nm, paths[nm]["hashes"], paths[nm]["content_hash"], paths[nm]["verify"]))
    report["exports"] = paths
    report["roundtrip_floor_deg"] = round(floor, 5)
    write(o, report)


def write(o, report):
    os.makedirs(o["outdir"], exist_ok=True)
    with open(os.path.join(o["outdir"], "reskin_report.json"), "w") as f:
        json.dump(report, f, indent=1, default=lambda x: x.tolist() if hasattr(x, "tolist") else str(x))
    print("RESKIN OK" + (" (dry, nothing exported)" if report.get("dry") else ""))


if __name__ == "__main__":
    try:
        main()
    except Exception as e:  # noqa: BLE001  (a Fail is a refused check; anything else is a crash: both leave nothing behind, exit nonzero)
        print("RESKIN FAIL:" if isinstance(e, Fail) else "RESKIN CRASH:", repr(e))
        if not isinstance(e, Fail):
            import traceback
            traceback.print_exc(file=sys.stdout)
        od = R.argv().get("outdir")
        for f in ([] if os.environ.get("RESKIN_KEEP") else OUTPUTS):   # RESKIN_KEEP keeps a refused export for diagnosis; a run with it set refuses to start (see main), so this only applies to the diagnostic variant of the code path
            if od and os.path.exists(os.path.join(od, f)):
                os.remove(os.path.join(od, f))
        sys.exit(2 if isinstance(e, Fail) else 3)
