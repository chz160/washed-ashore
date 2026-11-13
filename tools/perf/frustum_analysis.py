#!/usr/bin/env python3
"""Issue #9 analysis (task #27): applies the pre-registered rule of TestResults/9-measurement-plan.md section 5
(revision 2, git hash-object 710f0d11) and technical-director's #25 addenda to a finished frustum_matrix.ps1 run.

Reads only finished files (run after the runner exits; never during a run):
  TestResults/9-runs/<run>/matrix-progress.txt, <slot>/run.json, <slot>/progress.txt,
  <slot>/frustum-spots.json (spot launches) or <slot>/frustum-route.json + bells-bend-fps.json (route launches),
  and optionally the -dev counter pass <dev-run>/<slot>/frustum-spots.json.
Writes TestResults/9-analysis.json and TestResults/9-analysis.md (or --out-dir).

Rule (plan section 5), per spot and per variant V, run r = the spot's launch in run r (its "-repeat" replaces it):
  d_r = mean(p95(A before V), p95(A after V)) - p95(V), the A windows bracketing V in the same launch; + is faster.
  S_min = max(0.20 ms, 2% of the mean A p95 at that spot (all A windows of all its launches)).
  1 Valid: the launch is DONE (progress.txt ends "DONE spots"), no argument errors, one build hash for the whole session,
    V and both A windows valid:true with >= 600 timed samples.
  2 The arm is the arm: enabled renderers within +-2% of both A (not FISHOFF), and the target moved:
    FAR (ground): submitted lower than both A at >= 1 heading, or median triangles below both A; FAR (cab): readback;
    LAYER: median visible skinned meshes below both A; SMALLMESH, GRDOCC: median triangles or batches below both A;
    SHADOW: median shadow casters 0 when exposed, else readback; FISHOFF: Fish root inactive and (addendum 5) fish
    on >= 10% of frames in both bracketing A windows. A valid arm that moved nothing is NO EFFECT.
  3 Bracket under the saving: every run |A before - A after| < d_r.
  4 Worst run: min d_r >= S_min.
  SAVING = 1-4. COST = every run -d_r >= S_min and |bracket| < -d_r. NO SAVING = every |bracket| < S_min and
  max d_r < S_min. Else INCONCLUSIVE. SHADOW and FISHOFF are costs / upper bounds: mean d_r and range, valid under
  1-3, else INCONCLUSIVE; never a recommendation.
Route: run r = the r-th launch of V in A FAR A LAYER A LAYER A FAR A FAR A LAYER A, bracketed by the A launches either
  side; p95 from frustum-route.json; a launch more than 5% below the block's most samples is void.
Median and p99 are reported and do not decide. Counters alone never make a saving.
"""
import argparse
import glob
import json
import os
import re
import statistics
import sys

PLAN_BLOB = "710f0d11b3dea7fa610a4874b186eff334e0050f"
MIN_SAMPLES = 600
S_MIN_FLOOR = 0.20
S_MIN_FRACTION = 0.02
ENABLED_TOLERANCE = 0.02
FISH_SHARE_MIN = 0.10
ROUTE_BAND = 0.95
COST_ARMS = ("SHADOW", "FISHOFF")


def load(path):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def last_line(path):
    if not os.path.exists(path):
        return ""
    lines = [l.strip() for l in open(path, encoding="utf-8") if l.strip()]
    return lines[-1] if lines else ""


def counter_median(window, key):
    c = (window.get("counters") or {}).get(key)
    return None if c is None else c.get("median")


def heading(window, label):
    for h in window.get("headings") or []:
        if str(h.get("heading")) == label:
            return h
    return None


def enabled(window):
    h = heading(window, "000") or (window.get("headings") or [None])[0]
    return None if h is None else h["census"]["total"]["enabled"]


# ----- spot launches ---------------------------------------------------------------------------------------------

def spot_slots(run_dir):
    """{(spot, run): slot dir}, a '-repeat' folder replacing its original."""
    out = {}
    for d in sorted(glob.glob(os.path.join(run_dir, "s[0-9][0-9]-r[0-9]-*"))):
        name = os.path.basename(d)
        m = re.match(r"s\d\d-r(\d)-(.+?)(-repeat)?$", name)
        if not m or "-broken-" in name or "-failed-" in name:
            continue
        key = (m.group(2), int(m.group(1)))
        if m.group(3) or key not in out:
            out[key] = d
    return out


def arm_moved(variant, v, a0, a1, spot):
    """(moved, why) for test 2's 'the target moved'."""
    below = lambda key: (counter_median(v, key) is not None and counter_median(a0, key) is not None and counter_median(a1, key) is not None
                         and counter_median(v, key) < counter_median(a0, key) and counter_median(v, key) < counter_median(a1, key))
    if variant == "FAR":
        if spot == "cab":
            return True, "cab: readback alone"
        for label in ("000", "090", "180", "270", "own"):
            hv, h0, h1 = heading(v, label), heading(a0, label), heading(a1, label)
            if hv and h0 and h1 and hv["census"]["total"]["submitted"] < min(h0["census"]["total"]["submitted"], h1["census"]["total"]["submitted"]):
                return True, f"submitted lower at heading {label}"
        return (True, "median triangles below both A") if below("triangles") else (False, "no heading with fewer submitted and triangles not below both A")
    if variant == "LAYER":
        return (True, "visible skinned meshes below both A") if below("visible_skinned_meshes") else (False, "visible skinned meshes not below both A")
    if variant in ("SMALLMESH", "GRDOCC"):
        if below("triangles"):
            return True, "median triangles below both A"
        if below("batches"):
            return True, "median batches below both A"
        return False, "neither triangles nor batches below both A"
    if variant == "SHADOW":
        sc = counter_median(v, "shadow_casters")
        if sc is None:
            return True, "shadow casters not exposed: readback alone"
        return (sc == 0, f"median shadow casters {sc}")
    if variant == "FISHOFF":
        root_off = (v.get("readback_after_timing") or {}).get("fish_root_active") is False
        shares = [((a.get("fish_sample") or {}).get("share_at_least_1")) for a in (a0, a1)]
        ok = root_off and all(s is not None and s >= FISH_SHARE_MIN for s in shares)
        return ok, f"fish root inactive {root_off}; bracketing A fish share {shares} (>= {FISH_SHARE_MIN} needed)"
    return False, "unknown variant"


def analyse_spots(run_dir, build_ok):
    slots = spot_slots(run_dir)
    per = {}       # (spot, variant) -> list of run records
    a_p95 = {}     # spot -> list of A p95
    census = {}    # spot -> list of A census per heading (Q1)
    launches = []
    for (spot, r), d in sorted(slots.items()):
        prog = last_line(os.path.join(d, "progress.txt"))
        rj = load(os.path.join(d, "run.json")) if os.path.exists(os.path.join(d, "run.json")) else {}
        sj_path = os.path.join(d, "frustum-spots.json")
        done = prog.endswith(" DONE spots")
        launch = {"spot": spot, "run": r, "slot": os.path.basename(d), "done": done, "progress_last": prog,
                  "runner_ok": rj.get("ok"), "build": rj.get("buildSha256"), "seconds": rj.get("seconds")}
        launches.append(launch)
        if not os.path.exists(sj_path):
            launch["error"] = "no frustum-spots.json"
            continue
        sj = load(sj_path)
        launch["argument_errors"] = sj.get("argument_errors") or []
        launch["resolution"] = sj.get("resolution")
        wins = sorted(sj["windows"], key=lambda w: w["window"])
        launch_valid = done and not launch["argument_errors"] and build_ok
        for w in wins:
            if w["variant_id"] == "A" and w["valid"]:
                a_p95.setdefault(spot, []).append(w["frame_ms"]["p95"])
                census.setdefault(spot, []).append({h["heading"]: h["census"]["total"] | {"by_root": {k: v["submitted"] for k, v in h["census"].get("by_root", {}).items()}}
                                                    for h in w.get("headings") or []})
        for i, v in enumerate(wins):
            if v["variant_id"] == "A":
                continue
            a0 = wins[i - 1] if i > 0 else None
            a1 = wins[i + 1] if i + 1 < len(wins) else None
            rec = {"run": r, "slot": os.path.basename(d), "window": v["window"]}
            problems = []
            if not launch_valid:
                problems.append("launch not valid (DONE line, argument errors or build)")
            for name, w in (("V", v), ("A before", a0), ("A after", a1)):
                if w is None or w["variant_id"] != ("A" if name != "V" else v["variant_id"]):
                    problems.append(f"{name} window missing")
                    continue
                if not w["valid"]:
                    problems.append(f"{name} window invalid: {w.get('problems')}")
                if w["frame_ms"]["count"] < MIN_SAMPLES:
                    problems.append(f"{name} window {w['frame_ms']['count']} samples < {MIN_SAMPLES}")
            if a0 and a1 and v:
                pa0, pa1, pv = a0["frame_ms"]["p95"], a1["frame_ms"]["p95"], v["frame_ms"]["p95"]
                rec.update({"p95_A_before": pa0, "p95_V": pv, "p95_A_after": pa1, "d": (pa0 + pa1) / 2 - pv, "bracket": abs(pa0 - pa1),
                            "median_V": v["frame_ms"]["median"], "p99_V": v["frame_ms"]["p99"],
                            "median_A": (a0["frame_ms"]["median"] + a1["frame_ms"]["median"]) / 2,
                            "p99_A": (a0["frame_ms"]["p99"] + a1["frame_ms"]["p99"]) / 2})
                for key in ("triangles", "vertices", "batches", "setpass_calls", "draw_calls", "shadow_casters", "visible_skinned_meshes"):
                    rec[f"{key}_V"] = counter_median(v, key)
                    rec[f"{key}_A"] = [counter_median(a0, key), counter_median(a1, key)]
                if v["variant_id"] != "FISHOFF":
                    e = [enabled(x) for x in (v, a0, a1)]
                    if None not in e and any(abs(e[0] - x) > ENABLED_TOLERANCE * x for x in e[1:]):
                        problems.append(f"enabled renderers {e[0]} not within 2% of A {e[1:]}")
                moved, why = arm_moved(v["variant_id"], v, a0, a1, spot)
                rec["moved"], rec["moved_why"] = moved, why
                if v["variant_id"] == "FISHOFF":
                    rec["fish_share_A"] = [(x.get("fish_sample") or {}).get("share_at_least_1") for x in (a0, a1)]
                h_v, h_a = heading(v, "own"), heading(a0, "own")
                if h_v and h_a:
                    rec["own_submitted_V"], rec["own_submitted_A"] = h_v["census"]["total"]["submitted"], h_a["census"]["total"]["submitted"]
            rec["valid"] = not problems
            rec["problems"] = problems
            per.setdefault((spot, v["variant_id"]), []).append(rec)
    return per, a_p95, census, launches


def verdict(variant, runs, s_min, expected_runs=3):
    valid = [r for r in runs if r["valid"]]
    out = {"runs": runs, "valid_runs": len(valid), "s_min": s_min}
    if len(valid) < expected_runs:
        out["outcome"] = "INCONCLUSIVE"
        out["d"] = [r["d"] for r in valid]
        out["why"] = (f"{len(valid)} valid runs of {expected_runs} (plan section 4: one repeat, then INCONCLUSIVE); invalid: "
                      + "; ".join(f"run {r['run']}: {', '.join(r['problems'])}" for r in runs if not r["valid"]))
        out["needs_repeat_runs"] = sorted({r["run"] for r in runs if not r["valid"]})
        return out
    d = [r["d"] for r in valid]
    out.update({"d": d, "d_mean": statistics.mean(d), "d_min": min(d), "d_max": max(d),
                "bracket_max": max(r["bracket"] for r in valid)})
    if variant in COST_ARMS:
        moved = all(r["moved"] for r in valid)
        brackets_ok = all(r["bracket"] < abs(r["d"]) for r in valid)
        out["outcome"] = "COST/UPPER BOUND" if (moved and brackets_ok) else "INCONCLUSIVE"
        out["why"] = f"mean d {out['d_mean']:.3f} ms (range {min(d):.3f}..{max(d):.3f}); arm moved {moved}; brackets under |d| {brackets_ok}"
        return out
    if not all(r["moved"] for r in valid):
        out["outcome"] = "NO EFFECT"
        out["why"] = "NO EFFECT under the pre-set test 2 (medians): " + "; ".join(sorted({r["moved_why"] for r in valid if not r["moved"]}))
        return out
    if all(r["bracket"] < r["d"] for r in valid) and min(d) >= s_min:
        out["outcome"] = "SAVING"
        out["why"] = f"min d {min(d):.3f} >= S_min {s_min:.3f}; every bracket < its d"
    elif all(-r["d"] >= s_min and r["bracket"] < -r["d"] for r in valid):
        out["outcome"] = "COST"
        out["why"] = f"every -d >= S_min {s_min:.3f} with bracket < -d"
    elif all(r["bracket"] < s_min for r in valid) and max(d) < s_min:
        out["outcome"] = "NO SAVING"
        out["why"] = f"every bracket < S_min {s_min:.3f} and max d {max(d):.3f} < S_min"
    else:
        out["outcome"] = "INCONCLUSIVE"
        out["why"] = f"d {[round(x, 3) for x in d]}, brackets {[round(r['bracket'], 3) for r in valid]}, S_min {s_min:.3f}"
    return out


# ----- route -----------------------------------------------------------------------------------------------------

ROUTE_ORDER = ["A", "FAR", "A", "LAYER", "A", "LAYER", "A", "FAR", "A", "FAR", "A", "LAYER", "A"]


def analyse_route(run_dir, build_ok, test1="band", speed_tol=0.05):
    """test1 "band": plan section 5 as written (5% sample band against the block's most samples; set 1).
    test1 "addendum6": technical-director's addendum 6 (#25 note_technical-director_plan_route_addendum6) for route set 2:
    DONE route, frustum-route.json valid, bells-bend-fps.json mean_speed_mps within +-5% of the set's A median (addendum
    6b, replacing the first draft's absolute 4.75-5.25), duration_s within +-5% of the set's A median, no "did not finish"
    warning in Player.log; no sample band."""
    launches = []
    for i, v in enumerate(ROUTE_ORDER):
        d = os.path.join(run_dir, f"w{i + 1:02d}-{v}")
        rec = {"slot": os.path.basename(d), "variant": v, "index": i}
        prog = last_line(os.path.join(d, "progress.txt"))
        rec["done"] = prog.endswith(" DONE route")
        rp = os.path.join(d, "frustum-route.json")
        if os.path.exists(rp):
            j = load(rp)
            rec.update({"p95": j["frame_ms"]["p95"], "median": j["frame_ms"]["median"], "p99": j["frame_ms"]["p99"], "samples": j["frame_ms"]["count"],
                        "player_valid": j.get("valid"), "triangles": counter_median(j, "triangles"), "visible_skinned_meshes": counter_median(j, "visible_skinned_meshes"),
                        "far_start": (j.get("readback_start") or {}).get("camera_far"), "far_end": (j.get("readback_end") or {}).get("camera_far")})
        fp = os.path.join(d, "bells-bend-fps.json")
        if os.path.exists(fp):
            fj = load(fp)
            rec["mean_speed_mps"], rec["duration_s"] = fj.get("mean_speed_mps"), fj.get("duration_s")
        lp = os.path.join(d, "Player.log")
        rec["did_not_finish_warning"] = os.path.exists(lp) and "did not finish the route in time" in open(lp, encoding="utf-8", errors="replace").read()
        launches.append(rec)
    most = max([l.get("samples", 0) for l in launches] or [0])
    a_dur = [l["duration_s"] for l in launches if l["variant"] == "A" and l.get("duration_s") is not None]
    a_dur_med = statistics.median(a_dur) if a_dur else None
    a_spd = [l["mean_speed_mps"] for l in launches if l["variant"] == "A" and l.get("mean_speed_mps") is not None]
    a_spd_med = statistics.median(a_spd) if a_spd else None
    for l in launches:
        l["void_by_sample_count"] = most > 0 and l.get("samples", 0) < ROUTE_BAND * most
        why = []
        if not l["done"]: why.append("no DONE route")
        if not l.get("player_valid"): why.append("frustum-route.json not valid")
        if not build_ok: why.append("more than one build")
        if test1 == "band":
            if l["void_by_sample_count"]: why.append(f"samples {l.get('samples')} < 95% of the block's most ({most})")
        else:
            sp = l.get("mean_speed_mps")
            if sp is None or a_spd_med is None or abs(sp - a_spd_med) > speed_tol * a_spd_med:
                why.append(f"mean_speed_mps {sp} not within 5% of the A median {a_spd_med}")
            if a_dur_med is None or l.get("duration_s") is None or abs(l["duration_s"] - a_dur_med) > 0.05 * a_dur_med:
                why.append(f"duration_s {l.get('duration_s')} not within 5% of the A median {a_dur_med}")
            if l["did_not_finish_warning"]: why.append("'did not finish' warning in Player.log")
        l["test1_problems"] = why
        l["valid"] = not why
    per = {}
    for i, l in enumerate(launches):
        if l["variant"] == "A":
            continue
        a0, a1 = launches[i - 1], launches[i + 1]
        rec = {"run": sum(1 for x in launches[:i + 1] if x["variant"] == l["variant"]), "slots": [a0["slot"], l["slot"], a1["slot"]]}
        problems = [f"{x['slot']} not valid ({'; '.join(x['test1_problems'])})" for x in (a0, l, a1) if not x["valid"]]
        if not problems:
            rec.update({"p95_A_before": a0["p95"], "p95_V": l["p95"], "p95_A_after": a1["p95"], "d": (a0["p95"] + a1["p95"]) / 2 - l["p95"],
                        "bracket": abs(a0["p95"] - a1["p95"]), "median_V": l["median"], "p99_V": l["p99"]})
            if l["variant"] == "LAYER":
                below = l.get("visible_skinned_meshes") is not None and all(
                    x.get("visible_skinned_meshes") is not None and l["visible_skinned_meshes"] < x["visible_skinned_meshes"] for x in (a0, a1))
                rec["moved"], rec["moved_why"] = below, "visible skinned meshes below both A" if below else "visible skinned meshes not below both A"
            else:
                below = l.get("triangles") is not None and all(x.get("triangles") is not None and l["triangles"] < x["triangles"] for x in (a0, a1))
                rec["moved"], rec["moved_why"] = below, "median triangles below both A" if below else "median triangles not below both A"
                # Addendum 6c (PENDING the Studio Head's written acceptance): the far-plane readback is the proof for route FAR.
                rb = l.get("far_start") == 600 and l.get("far_end") == 600 and all(x.get("far_start") == 1000 and x.get("far_end") == 1000 for x in (a0, a1))
                rec["moved_6c"], rec["moved_6c_why"] = rb, ("camera_far 600 at start and end, 1000 in both A" if rb else
                    f"readback camera_far V {l.get('far_start')}/{l.get('far_end')}, A {a0.get('far_start')}/{a0.get('far_end')} and {a1.get('far_start')}/{a1.get('far_end')}")
        rec["valid"] = not problems
        rec["problems"] = problems
        per.setdefault(l["variant"], []).append(rec)
    a_p95 = [l["p95"] for l in launches if l["variant"] == "A" and l.get("valid")]
    return per, a_p95, launches


# ----- dev counter pass ------------------------------------------------------------------------------------------

def analyse_dev(dev_dir):
    out = {}
    for d in sorted(glob.glob(os.path.join(dev_dir, "d[0-9][0-9]-dev-*"))):
        sp = os.path.join(d, "frustum-spots.json")
        if not os.path.exists(sp):
            continue
        for w in load(sp)["windows"]:
            grd = (w.get("counters") or {}).get("grd") or {}
            out.setdefault(w["spot"], []).append({"window": w["window"], "variant": w["variant_id"], "valid": w["valid"],
                                                  "grd": {k: (v or {}).get("median") for k, v in grd.items()},
                                                  "shadow_casters": counter_median(w, "shadow_casters"),
                                                  "visible_skinned_meshes": counter_median(w, "visible_skinned_meshes")})
    return out


# ----- the INCONCLUSIVE rerun ------------------------------------------------------------------------------------

RERUN_RULED = ("SHADOW", "FISHOFF")


def compute_rerun(a, builds):
    """Plan section 5: an INCONCLUSIVE spot's 3 launches are rerun once, unchanged; if still INCONCLUSIVE, it goes to the
    Studio Head. The Studio Head's item 3 reran SHADOW (park, ridge_top, valley_floor, cab) and FISHOFF (fish_water) under
    -Slots; only those rows are ruled from the rerun, by the same section 5 rule (addendum 7: they replace set 1's rows).
    The other rows are descriptive."""
    rdir = os.path.join(a.root, a.rerun)
    r_builds = sorted({load(p).get("buildSha256") for p in glob.glob(os.path.join(rdir, "*", "run.json"))} - {None})
    per, a_p95, _, launches = analyse_spots(rdir, len(r_builds) == 1 and r_builds == builds)
    s_min = {spot: max(S_MIN_FLOOR, S_MIN_FRACTION * statistics.mean(v)) for spot, v in a_p95.items()}
    ruled, desc = {}, {}
    for (spot, variant), runs in sorted(per.items()):
        v = verdict(variant, runs, s_min.get(spot, S_MIN_FLOOR))
        (ruled if variant in RERUN_RULED else desc).setdefault(spot, {})[variant] = v
    return {"run": a.rerun, "builds": r_builds, "same_build": r_builds == builds, "launches": launches, "ruled": ruled, "descriptive": desc}


def rerun_section(a, rr, out):
    out["rerun"] = rr
    r_builds, launches, ruled, desc, builds = rr["builds"], rr["launches"], rr["ruled"], rr["descriptive"], out["builds"]
    md = ["", f"## INCONCLUSIVE rerun (run {a.rerun}; Studio Head, 9-studio-head-rulings.md item 3)", "",
          f"Same build as the session: {r_builds == builds} ({', '.join(b[:12] for b in r_builds)}). Spot launches DONE: {sum(1 for l in launches if l['done'])}/{len(launches)}.",
          "Ruled from the rerun (plan section 5, unchanged): SHADOW at park, ridge_top, valley_floor and cab; FISHOFF at fish_water. If a row is still INCONCLUSIVE, it goes to the Studio Head.", "",
          "| spot | variant | set 1 outcome (superseded) | rerun outcome (current) | rerun valid runs | rerun d per run | S_min | rerun max bracket | why |", "|---|---|---|---|---|---|---|---|---|"]
    for spot, vs in sorted(ruled.items()):
        for variant, r in sorted(vs.items()):
            first = ((out["spots"].get(spot, {}).get(variant) or {}).get("superseded") or {}).get("outcome", "n/a")
            md.append(f"| {spot} | {variant} | {first} | **{r['outcome']}** | {r['valid_runs']} | {', '.join(fmt(x) for x in r.get('d', []))} | {fmt(r['s_min'])} | {fmt(r.get('bracket_max'))} | {r['why']} |")
    md += ["", "Descriptive only (not ruled from the rerun; the first run's outcomes stand):", "",
           "| spot | variant | rerun d per run | rerun outcome under the rule, for reference |", "|---|---|---|---|"]
    for spot, vs in sorted(desc.items()):
        for variant, r in sorted(vs.items()):
            md.append(f"| {spot} | {variant} | {', '.join(fmt(x) for x in r.get('d', []))} | {r['outcome']} (descriptive) |")
    return md


# ----- supporting tables (report only; they decide nothing) -------------------------------------------------------

def med(xs):
    xs = [x for x in xs if x is not None]
    return statistics.median(xs) if xs else None


def iso(t):
    from datetime import datetime
    return datetime.fromisoformat(t.replace("Z", "+00:00"))


def extra_sections(run_dir, a, launches, route_launches, out):
    import struct
    from datetime import timedelta
    md = []
    slots = spot_slots(run_dir)
    labels = ("000", "090", "180", "270", "own")
    roots = ("NorthBarrier", "LookoutTower", "Wildlife", "Water")

    # Q1: A-window census, median over the A windows of all launches, per heading.
    md += ["", "## Q1 census in the A windows (median over every A window of the spot)", "",
           "Primary fields per plan section 2: enabled, submitted (camera rule), fog_visible (nearest view depth < fog end), bands by nearest view depth. Occlusion is not counted.", "",
           "| spot | heading | enabled | submitted | fog_visible | 0-120 m | 120-520 m | 520 m-far | submitted by root (NorthBarrier/LookoutTower/Wildlife/Water) | terrain trees in frustum within tree distance |",
           "|---|---|---|---|---|---|---|---|---|---|"]
    q1 = {}
    for (spot, r), d in sorted(slots.items()):
        for w in load(os.path.join(d, "frustum-spots.json"))["windows"]:
            if w["variant_id"] != "A":
                continue
            for h in w.get("headings") or []:
                t = h["census"]["total"]
                br = h["census"].get("by_root", {})
                q1.setdefault((spot, str(h["heading"])), []).append(
                    (t["enabled"], t["submitted"], t["fog_visible"], t["band_0_fogstart"], t["band_fogstart_fogend"], t["band_fogend_far"],
                     tuple((br.get(k) or {}).get("submitted", 0) for k in roots), h["census"]["terrain"]["trees_within_tree_distance_in_frustum"]))
    for spot in sorted({k[0] for k in q1}):
        for lab in labels:
            rows = q1.get((spot, lab))
            if not rows:
                continue
            col = lambda i: med([x[i] for x in rows])
            rootcol = "/".join(fmt(med([x[6][j] for x in rows]), 0) for j in range(len(roots)))
            md.append(f"| {spot} | {lab} | {fmt(col(0), 0)} | {fmt(col(1), 0)} | {fmt(col(2), 0)} | {fmt(col(3), 0)} | {fmt(col(4), 0)} | {fmt(col(5), 0)} | {rootcol} | {fmt(col(7), 0)} |")

    # Q2/Q3 stills: trial against its in-frame authored twin, pixels changed by more than 8/255, against the same-frame repeat.
    md += ["", "## Stills: trial against the in-frame authored twin (pixels changed > 8/255; median over runs, with the same-frame repeat as the noise floor)", "",
           "| spot | variant | " + " | ".join(labels) + " |", "|---|---|" + "---|" * len(labels)]
    diffs = {}
    for (spot, r), d in sorted(slots.items()):
        for w in load(os.path.join(d, "frustum-spots.json"))["windows"]:
            if w["variant_id"] == "A":
                continue
            for h in w.get("headings") or []:
                s = h.get("still") or {}
                tva, rep = (s.get("trial_vs_authored") or {}).get("changed_px_over_8"), (s.get("repeat_same_frame") or {}).get("changed_px_over_8")
                diffs.setdefault((spot, w["variant_id"], str(h["heading"])), []).append((tva, rep))
    for spot, variant in sorted({(k[0], k[1]) for k in diffs}):
        cells = []
        for lab in labels:
            v = diffs.get((spot, variant, lab), [])
            cells.append(f"{fmt(med([x[0] for x in v]), 0)} (noise {fmt(med([x[1] for x in v]), 0)})")
        md.append(f"| {spot} | {variant} | " + " | ".join(cells) + " |")

    # Q5: shadow casters in the A windows against SHADOW, and the SHADOW reading.
    md += ["", "## Q5 shadows (A windows: engine Shadow Casters median; census shadow-caster candidates at the 50 m shadow distance)", "",
           "Counters and d come from the run whose outcome is current (the section 5 rerun where it replaced set 1).", "",
           "| spot | shadow casters, A (median) | shadow casters, SHADOW | triangles A / SHADOW (M) | SHADOW outcome | d per run (ms) |", "|---|---|---|---|---|---|"]
    for spot, vs in sorted(out["spots"].items()):
        if "SHADOW" not in vs:
            continue
        runs = vs["SHADOW"]["runs"]
        sa = med([x for r in runs for x in (r.get("shadow_casters_A") or [])])
        sv = med([r.get("shadow_casters_V") for r in runs])
        ta = med([x for r in runs for x in (r.get("triangles_A") or [])])
        tv = med([r.get("triangles_V") for r in runs])
        sup = vs["SHADOW"].get("superseded")
        oc = vs["SHADOW"]["outcome"] + (f" (rerun {vs['SHADOW']['source_run']}; set 1 {sup['outcome']}, superseded)" if sup else "")
        md.append(f"| {spot} | {fmt(sa, 0)} | {fmt(sv, 0)} | {fmt(ta / 1e6 if ta else None, 2)} / {fmt(tv / 1e6 if tv else None, 2)} | {oc} | {', '.join(fmt(r.get('d')) for r in runs)} |")

    # Route: the 5% sample band as written, with the evidence for technical-director.
    md += ["", "## Route set 1: descriptive, not ruled; void under the pre-registered sample band", "",
           "| slot | frames | walk seconds | mean speed | p95 | median | p99 | void by sample count |", "|---|---|---|---|---|---|---|---|"]
    for l in route_launches:
        fps = os.path.join(run_dir, l["slot"], "bells-bend-fps.json")
        dur = load(fps).get("duration_s") if os.path.exists(fps) else None
        md.append(f"| {l['slot']} | {l.get('samples')} | {dur} | {l.get('mean_speed_mps')} | {fmt(l.get('p95'))} | {fmt(l.get('median'))} | {fmt(l.get('p99'))} | {l['void_by_sample_count']} |")

    # Q1 for the route (technical-director's #30 R1): the route census launch (A only, at two route waypoints) and the
    # route walks' engine counters (A walks only; median of each walk's per-frame medians).
    cpath = os.path.join(run_dir, "c01-route-census", "frustum-spots.json")
    md += ["", "## Q1 on the route: census at the route waypoints (launch c01-route-census, one A window each) and the A walks' engine counters", ""]
    if os.path.exists(cpath):
        md += ["| waypoint | heading | enabled | submitted | fog_visible | 0-120 m | 120-520 m | 520 m-far | submitted by root (NorthBarrier/LookoutTower/Wildlife/Water) | terrain trees in frustum within tree distance |",
               "|---|---|---|---|---|---|---|---|---|---|"]
        for w in load(cpath)["windows"]:
            for h in w.get("headings") or []:
                t = h["census"]["total"]
                br = h["census"].get("by_root", {})
                md.append(f"| {w['spot']} | {h['heading']} | {t['enabled']} | {t['submitted']} | {t['fog_visible']} | {t['band_0_fogstart']} | {t['band_fogstart_fogend']} | {t['band_fogend_far']} | "
                          + "/".join(str((br.get(k) or {}).get("submitted", 0)) for k in roots) + f" | {h['census']['terrain']['trees_within_tree_distance_in_frustum']} |")
    else:
        md.append("No c01-route-census launch in this run.")
    walk_sets = [("set 1", run_dir)] + ([("set 2", os.path.join(a.root, a.route2))] if getattr(a, "route2", "") else [])
    md += ["", "| route set | A walks | SetPass calls | triangles (M) | vertices (M) | shadow casters | visible skinned meshes |", "|---|---|---|---|---|---|---|"]
    for name, rd in walk_sets:
        js = [load(p) for p in sorted(glob.glob(os.path.join(rd, "w[0-9][0-9]-A", "frustum-route.json")))]
        cm = lambda k: med([counter_median(j, k) for j in js])
        tri, ver = cm("triangles"), cm("vertices")
        md.append(f"| {name} | {len(js)} | {fmt(cm('setpass_calls'), 0)} | {fmt(tri / 1e6 if tri else None, 2)} | {fmt(ver / 1e6 if ver else None, 2)} | {fmt(cm('shadow_casters'), 0)} | {fmt(cm('visible_skinned_meshes'), 0)} |")

    # Engine counters beside the census (Q1), per spot, median over the A windows.
    md += ["", "## Q1 engine counters in the A windows (median of the per-window medians; ProfilerRecorder, release player)", "",
           "Batches and Draw Calls are not exposed: null in the release player, 0 in the development player. GPU time is null (enableFrameTimingStats 0).", "",
           "| spot | SetPass calls | triangles (M) | vertices (M) | shadow casters | visible skinned meshes |", "|---|---|---|---|---|---|"]
    acount = {}
    for (spot, r), d in sorted(slots.items()):
        for w in load(os.path.join(d, "frustum-spots.json"))["windows"]:
            if w["variant_id"] == "A":
                acount.setdefault(spot, []).append(w)
    for spot, ws in sorted(acount.items()):
        cm = lambda k: med([counter_median(w, k) for w in ws])
        tri, ver = cm("triangles"), cm("vertices")
        md.append(f"| {spot} | {fmt(cm('setpass_calls'), 0)} | {fmt(tri / 1e6 if tri else None, 2)} | {fmt(ver / 1e6 if ver else None, 2)} | {fmt(cm('shadow_casters'), 0)} | {fmt(cm('visible_skinned_meshes'), 0)} |")

    # Q2/Q3 timing and counters per spot and variant (median over runs).
    md += ["", "## Frame time and counters per spot and variant (median over the 3 runs; A = mean of the two bracketing A windows)", "",
           "| spot | variant | A median / p95 / p99 (ms) | V median / p95 / p99 (ms) | triangles A / V (M) | vertices A / V (M) | SetPass A / V |", "|---|---|---|---|---|---|---|"]
    for spot, vs in sorted(out["spots"].items()):
        for variant, rv in sorted(vs.items()):
            rr = rv["runs"]
            a_tri = med([sum(r["triangles_A"]) / 2 for r in rr if r.get("triangles_A") and None not in r["triangles_A"]])
            a_ver = med([sum(r["vertices_A"]) / 2 for r in rr if r.get("vertices_A") and None not in r["vertices_A"]])
            a_sp = med([sum(r["setpass_calls_A"]) / 2 for r in rr if r.get("setpass_calls_A") and None not in r["setpass_calls_A"]])
            md.append(f"| {spot} | {variant} | {fmt(med([r.get('median_A') for r in rr]))} / {fmt(med([(r['p95_A_before'] + r['p95_A_after']) / 2 for r in rr if 'p95_A_before' in r]))} / {fmt(med([r.get('p99_A') for r in rr]))}"
                      f" | {fmt(med([r.get('median_V') for r in rr]))} / {fmt(med([r.get('p95_V') for r in rr]))} / {fmt(med([r.get('p99_V') for r in rr]))}"
                      f" | {fmt(a_tri / 1e6 if a_tri else None, 2)} / {fmt(med([r.get('triangles_V') for r in rr]) / 1e6 if med([r.get('triangles_V') for r in rr]) else None, 2)}"
                      f" | {fmt(a_ver / 1e6 if a_ver else None, 2)} / {fmt(med([r.get('vertices_V') for r in rr]) / 1e6 if med([r.get('vertices_V') for r in rr]) else None, 2)}"
                      f" | {fmt(a_sp, 0)} / {fmt(med([r.get('setpass_calls_V') for r in rr]), 0)} |")

    # LAYER descriptively (technical-director R7): the pre-set test 2 compares medians, which stay at 3 where wildlife
    # and birds are rare, so a cull that removes them on part of the sweep shows in p95 and max, not the median.
    md += ["", "## LAYER: what the cull removed (descriptive, not ruled; visible skinned meshes per window)", "",
           "The pre-set test 2 for LAYER compares median visible skinned meshes, and the median is 3 in both A and LAYER windows wherever wildlife and birds are rare on the sweep. That median blind spot is technical-director's plan error. p95 and max show the cull acting on part of the sweep. d is quoted from the Spots table for reference only.", "",
           "| spot | A windows: p95 (range) / median | LAYER windows: p95 per run / median per run | LAYER d per run (ms) |", "|---|---|---|---|"]
    for spot, vs in sorted(out["spots"].items()):
        if "LAYER" not in vs:
            continue
        a_p95, a_med, l_p95, l_med = [], [], [], []
        for (sp, r), d in sorted(slots.items()):
            if sp != spot:
                continue
            for w in load(os.path.join(d, "frustum-spots.json"))["windows"]:
                c = (w.get("counters") or {}).get("visible_skinned_meshes")
                if not c:
                    continue
                if w["variant_id"] == "A": a_p95.append(c["p95"]); a_med.append(c["median"])
                if w["variant_id"] == "LAYER": l_p95.append(c["p95"]); l_med.append(c["median"])
        md.append(f"| {spot} | {min(a_p95)}-{max(a_p95)} / {fmt(med(a_med), 0)} | {', '.join(str(x) for x in l_p95)} / {', '.join(str(x) for x in l_med)} | {', '.join(fmt(r.get('d')) for r in vs['LAYER']['runs'])} |")

    # Q3 edge stills.
    md += ["", "## Q3 cull-edge stills", "",
           '- LAYER: no edge still exists. At every spot and run, edge_stills reports found:false for Wildlife and Birds ("no renderer on the layer within 25% of the distance"). That search ran once per launch, on the frozen frame after the sweep at the pose heading, so it says only that no wildlife or bird renderer was 300-500 m away at that moment. During the sweep the cull did act (see "LAYER: what the cull removed"). So the 400 m LAYER pop-in check was not done where the cull acted.',
           "- SMALLMESH: one culled/shown pair per launch, aimed at the renderer whose estimated relative height is closest to 1% (smallMesh 1). `changed_px` counts every pixel that differs at all, so compare `changed_px_over_8` with the same-frame repeat noise in the stills table above. For example, park run 1 has changed_px 496,465 at a mean of 0.64/255 but only 3 pixels over 8/255, against its own-heading repeat of 459,522 at 0.62/255 and 5 over 8. That is frame-to-frame sub-visible noise, not a moved object. The ridge_top pairs (LookoutTower/MC39_Firefinder_Ring at 62.5 m) change 1,337-1,554 px over 8/255, above that spot's heading noise of 11-876. An edge pair has no same-frame repeat of its own, so whether that is the ring culled at 1% or noise at that aim is for technical-artist to judge on the files.", "",
           "| launch | target renderer | distance (m) | est. relative height | changed px (any / > 3 / > 8) | culled | shown | mask |", "|---|---|---|---|---|---|---|---|"]
    for (spot, r), d in sorted(slots.items()):
        for w in load(os.path.join(d, "frustum-spots.json"))["windows"]:
            for e in w.get("edge_stills") or []:
                if not e.get("found"):
                    continue
                ch = e.get("changed") or {}
                mask = e["culled"].replace("-culled.png", "-mask.png")
                md.append(f"| {os.path.basename(d)} | {e.get('renderer') or e.get('layer')} | {fmt(e.get('distance_m'), 1)} | {fmt(e.get('relative_height_estimate'))} | "
                          f"{ch.get('changed_px')} / {ch.get('changed_px_over_3')} / {ch.get('changed_px_over_8')} | {os.path.basename(d)}/{e['culled']} | {os.path.basename(d)}/{e['shown']} | {os.path.basename(d)}/{mask} |")

    # Q5 census: casting renderers inside the 50 m shadow distance against everything submitted.
    md += ["", "## Q5 census: shadow-caster candidates inside the shadow distance (A windows, median over headings and windows)", "",
           'Method: `shadow_caster_candidates` counts enabled renderers with shadow casting on whose bounds meet the camera frustum cut at the shadow distance in force (50 m), the same rule URP\'s camera-side caster culling uses. It ignores the light-space extension, so casters just outside the view that throw shadows in are not counted. "Submitted beyond 50 m" is submitted minus candidates, an upper bound that also includes renderers that don\'t cast (for example, the water quads with shadows off). The engine\'s Shadow Casters Count is exposed in the release player (a value, not null); its A-window median is in the counters table. It is larger than the census; assumed, not measured: it also counts casters the census cannot see (terrain trees are not Renderers) and casters in the light frustum outside the view.', "",
           "| spot | candidates within 50 m | submitted | submitted not a candidate (upper bound beyond 50 m) | engine Shadow Casters (A median) |", "|---|---|---|---|---|"]
    for spot in sorted({k[0] for k in q1}):
        cands, subs = [], []
        for (sp, r), d in sorted(slots.items()):
            if sp != spot:
                continue
            for w in load(os.path.join(d, "frustum-spots.json"))["windows"]:
                if w["variant_id"] != "A":
                    continue
                for h in w.get("headings") or []:
                    t = h["census"]["total"]
                    cands.append(t["extra"]["shadow_caster_candidates"]); subs.append(t["submitted"])
        c, sb = med(cands), med(subs)
        md.append(f"| {spot} | {fmt(c, 0)} | {fmt(sb, 0)} | {fmt(sb - c if c is not None and sb is not None else None, 0)} | {fmt(med([counter_median(w, 'shadow_casters') for w in acount.get(spot, [])]), 0)} |")

    # Still pairs: file names for every trial heading of run 1 (the same names exist for runs 2 and 3 with r2/r3).
    md += ["", "## Appendix: still pair files (every run; window numbers follow each run's variant order, plan section 4)", "",
           "Each trial still has an in-frame authored twin and a change mask: `<spot>-r<run>-w<NN>-<VARIANT>-h<heading>.png`, `...-authored.png`, `...-mask.png`.", "",
           "| launch | variant | heading | trial | authored | mask |", "|---|---|---|---|---|---|"]
    for (spot, r), d in sorted(slots.items()):
        for w in load(os.path.join(d, "frustum-spots.json"))["windows"]:
            if w["variant_id"] == "A":
                continue
            for h in w.get("headings") or []:
                st = h.get("still") or {}
                if not st.get("file"):
                    continue
                base = st["file"][:-4]
                md.append(f"| {os.path.basename(d)} | {w['variant_id']} | {h['heading']} | {st['file']} | {st.get('authored', '')} | {base}-mask.png |")

    # Stills, launches, wall time.
    pngs = glob.glob(os.path.join(run_dir, "*", "*.png"))
    sizes = collections_counter(struct.unpack(">II", open(p, "rb").read(24)[16:24]) for p in pngs)
    start, end = iso(out["session_start"]), iso(out["session_end"])
    md += ["", "## Checks", "",
           f"- Stills: {len(pngs)} PNGs; sizes {dict((f'{w}x{h}', n) for (w, h), n in sizes.items())}.",
           f"- Spot launches DONE: {sum(1 for l in launches if l['done'])}/{len(launches)}; repeats: {[l['slot'] for l in launches if l['slot'].endswith('-repeat')]}.",
           f"- Route launches ending 'DONE route': {sum(1 for l in route_launches if l['done'])}/{len(route_launches)}.",
           f"- Wall time: {start.isoformat()} to {end.isoformat()} = {str(end - start).split('.')[0]} (plan estimate about 2 h 45 min for 29 launches, before the 3 fish_water launches were added)."]
    if a.dev:
        dev = out.get("dev_counters") or {}
        grd_values = {v for ws in dev.values() for w in ws for v in (w.get("grd") or {}).values()}
        md.append(f"- Dev counter pass {a.dev}: {sum(len(ws) for ws in dev.values())} windows; GRD counter medians seen: {sorted(x for x in grd_values if x is not None)} "
                  "(all 0 means the counters are registered but never filled in this player, so the pass gives no GRD culling data).")
    if a.flag_window:
        f0, f1 = iso(a.flag_window[0]), iso(a.flag_window[1])
        hits = []
        for (spot, r), d in sorted(slots.items()):
            lines = [l.split(" ", 1) for l in open(os.path.join(d, "progress.txt"), encoding="utf-8") if " WINDOW " in l or "SPOT-DONE" in l]
            for i, (t, txt) in enumerate(lines):
                if not txt.startswith("WINDOW"):
                    continue
                w0 = iso(t) + timedelta(seconds=5)   # the 5 s settle is untimed
                w1 = iso(lines[i + 1][0]) if i + 1 < len(lines) else w0 + timedelta(seconds=24)
                if w0 < f1 and w1 > f0:
                    hits.append(f"{os.path.basename(d)} {txt.strip()} (timed about {w0.time()} to {w1.time()})")
        md.append(f"- Other machine load {a.flag_window[0]} to {a.flag_window[1]} overlaps: {hits or 'no timed window'}. Tests 1 and 3 are applied unchanged to those windows (technical-director, #27).")
        for slot in sorted({h.split(' ')[0] for h in hits}):
            rj = load(os.path.join(run_dir, slot, "run.json"))
            for k in ("loadAtStart", "loadAtEnd"):
                v = rj.get(k) or {}
                md.append(f"  - {slot} run.json {k}: time {v.get('time')}, total CPU {v.get('cpu_total_percent')}%, render or Editor processes {v.get('render_or_editor_processes')}, Unity CLI helpers {v.get('unity_cli_helpers')}.")
    return md


def collections_counter(it):
    import collections
    return collections.Counter(it)


# ----- report ----------------------------------------------------------------------------------------------------

def fmt(x, n=3):
    return "n/a" if x is None else (f"{x:.{n}f}" if isinstance(x, float) else str(x))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--run", required=True, help="matrix run id (folder under TestResults/9-runs)")
    ap.add_argument("--dev", default="", help="-DevCounters run id (optional)")
    ap.add_argument("--root", default="TestResults/9-runs")
    ap.add_argument("--plan", default="TestResults/9-measurement-plan.md")
    ap.add_argument("--out-dir", default="TestResults")
    ap.add_argument("--route2", default="", help="route set 2 run id (technical-director's addendum 6); set 1 is then descriptive only")
    ap.add_argument("--rerun", default="", help="plan section 5's INCONCLUSIVE rerun run id (Studio Head, 9-studio-head-rulings.md item 3): only its SHADOW and FISHOFF rows are ruled")
    ap.add_argument("--accept-6c", action="store_true", help="only after the Studio Head accepts addendum 6c in writing: rank route FAR under the readback test 2")

    ap.add_argument("--flag-window", nargs=2, metavar=("START_UTC", "END_UTC"), default=None,
                    help="a span of other machine load (ISO UTC); timed windows that overlap it are listed")
    a = ap.parse_args()
    run_dir = os.path.join(a.root, a.run)
    if not os.path.isdir(run_dir):
        sys.exit(f"no run folder {run_dir}")

    builds = sorted({load(p).get("buildSha256") for p in glob.glob(os.path.join(run_dir, "*", "run.json")) if "-broken-" not in p and "-failed-" not in p} - {None})
    build_ok = len(builds) == 1
    progress = [l.strip() for l in open(os.path.join(run_dir, "matrix-progress.txt"), encoding="utf-8") if l.strip()]
    ts = [l.split(" ")[0] for l in progress]
    per, a_p95, census, launches = analyse_spots(run_dir, build_ok)
    route_per, route_a, route_launches = analyse_route(run_dir, build_ok, "band")
    route2 = None
    if a.route2:
        r2_dir = os.path.join(a.root, a.route2)
        r2_builds = sorted({load(p).get("buildSha256") for p in glob.glob(os.path.join(r2_dir, "*", "run.json"))} - {None})
        r2_per, r2_a, r2_launches = analyse_route(r2_dir, len(r2_builds) == 1 and r2_builds == builds, "addendum6")
        route2 = {"run": a.route2, "builds": r2_builds, "per": r2_per, "a": r2_a, "launches": r2_launches}

    s_min = {spot: max(S_MIN_FLOOR, S_MIN_FRACTION * statistics.mean(v)) for spot, v in a_p95.items()}
    results = {}
    for (spot, variant), runs in sorted(per.items()):
        results.setdefault(spot, {})[variant] = verdict(variant, runs, s_min.get(spot, S_MIN_FLOOR))
    rerun = compute_rerun(a, builds) if a.rerun else None
    if rerun:
        # Addendum 7: the rerun REPLACES set 1 for SHADOW at park, ridge_top, valley_floor and cab, and FISHOFF at fish_water.
        for spot, vs in rerun["ruled"].items():
            for variant, r in vs.items():
                first = results.get(spot, {}).get(variant)
                if first is None:
                    continue
                r = dict(r)
                r["superseded"] = {"outcome": first["outcome"], "d": first.get("d"), "why": first["why"]}
                r["source_run"] = a.rerun
                results[spot][variant] = r
    # Route set 1: INCONCLUSIVE under test 1's sample band as written; descriptive only (technical-director,
    # #27 note_technical-director_route_band): no d, no S_min comparison, no saving wording.
    route_smin = None
    route_results = {v: {"outcome": "INCONCLUSIVE", "valid_runs": sum(1 for r in runs if r["valid"]), "s_min": None,
                         "why": "test 1: walks void by the pre-registered 5% sample band (a plan error: a faster arm records more frames over the same fixed-time route); descriptive only"}
                     for v, runs in route_per.items()}
    route2_results = {}
    if route2:
        r2_smin = max(S_MIN_FLOOR, S_MIN_FRACTION * statistics.mean(route2["a"])) if route2["a"] else S_MIN_FLOOR
        route2_results = {v: verdict(v, runs, r2_smin) for v, runs in route2["per"].items()}
        # Route FAR under addendum 6c, reported beside the original test 2 and ranked only with --accept-6c.
        if "FAR" in route2["per"]:
            runs6c = [dict(r, moved=r.get("moved_6c"), moved_why=r.get("moved_6c_why")) for r in route2["per"]["FAR"]]
            route2_results["FAR (addendum 6c, accepted by the Studio Head with disclosure)" if a.accept_6c else "FAR (addendum 6c, PENDING Studio Head)"] = verdict("FAR", runs6c, r2_smin)

    # Q6 ranking: SAVING rows only, by mean d over the spots where they are SAVING.
    ranking = {}
    for spot, vs in results.items():
        for variant, r in vs.items():
            if r["outcome"] == "SAVING" and variant not in COST_ARMS:
                ranking.setdefault(variant, {"spots": [], "d": []})
                ranking[variant]["spots"].append(spot)
                ranking[variant]["d"].append(r["d_mean"])
    for variant, r in route2_results.items():
        if "6c" in variant and not a.accept_6c:
            continue   # 6c is shown, never ranked, until the Studio Head accepts it
        if variant == "FAR" and a.accept_6c:
            continue   # with 6c accepted, its row replaces the original FAR row in the ranking
        if r["outcome"] == "SAVING":
            key = "FAR" if "6c" in variant else variant
            ranking.setdefault(key, {"spots": [], "d": []})
            ranking[key]["spots"].append("route (addendum 6c, accepted)" if "6c" in variant else "route")
            ranking[key]["d"].append(r["d_mean"])
    ranked = sorted(({"variant": k, "mean_d": statistics.mean(v["d"]), "saving_at": v["spots"]} for k, v in ranking.items()), key=lambda x: -x["mean_d"])

    plan_blob = os.popen(f'git hash-object "{a.plan}"').read().strip() if os.path.exists(a.plan) else ""
    out = {"issue": 9, "run": a.run, "dev": a.dev or None, "plan": a.plan, "plan_blob": plan_blob, "plan_blob_expected": PLAN_BLOB,
           "builds": builds, "same_build": build_ok, "session_start": ts[0] if ts else None, "session_end": ts[-1] if ts else None,
           "matrix_last_line": progress[-1] if progress else "", "s_min": s_min, "route_s_min": route_smin,
           "spot_launches": launches, "spots": results, "route_launches": route_launches, "route": route_results,
           "route2": ({"run": route2["run"], "builds": route2["builds"], "launches": route2["launches"], "results": route2_results} if route2 else None),
           "q1_census_A": census, "ranking": ranked, "dev_counters": analyse_dev(os.path.join(a.root, a.dev)) if a.dev else None}
    os.makedirs(a.out_dir, exist_ok=True)
    with open(os.path.join(a.out_dir, "9-analysis.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, indent=1, default=str)

    md = [f"# #9 analysis: run {a.run}", "",
          f"Generated by `tools/perf/frustum_analysis.py --run {a.run}" + (f" --dev {a.dev}" if a.dev else "") + (f" --route2 {a.route2}" if a.route2 else "")
          + (" --accept-6c" if a.accept_6c else "") + (f" --rerun {a.rerun}" if a.rerun else "") + "`, which applies plan section 5 unchanged.",
          f"Plan blob {plan_blob or 'n/a'} (expected {PLAN_BLOB}). Build hashes: {', '.join(b[:12] for b in builds)} (same build: {build_ok}).",
          f"Session {out['session_start']} to {out['session_end']}; last line: `{out['matrix_last_line']}`.", "",
          "## Spots (p95, ms; d = mean bracketing A - V, positive = faster)", "",
          "| spot | variant | outcome | valid runs | d per run | S_min | max bracket | why |", "|---|---|---|---|---|---|---|---|"]
    for spot, vs in sorted(results.items()):
        for variant, r in sorted(vs.items()):
            label = r['outcome']
            why = r['why']
            if r.get("superseded"):
                label = f"{r['outcome']} (section 5 rerun {r['source_run']})"
                why = (f"{r['why']}. Set 1 ({a.run}): {r['superseded']['outcome']}, superseded by the section 5 rerun; set 1 d "
                       + ", ".join(fmt(x) for x in (r['superseded'].get('d') or [])))
            md.append(f"| {spot} | {variant} | **{label}** | {r['valid_runs']} | {', '.join(fmt(x) for x in r.get('d', []))} | {fmt(r['s_min'])} | {fmt(r.get('bracket_max'))} | {why} |")
    md += ["", "## Route set 1 (plan section 5 as written)", "", "| variant | outcome | valid runs | why |", "|---|---|---|---|"]
    for variant, r in sorted(route_results.items()):
        md.append(f"| {variant} | **{r['outcome']}** | {r['valid_runs']} | {r['why']} |")
    if route2:
        md += ["", f"## Route set 2 (run {route2['run']}; test 1 per technical-director's addendum 6)", "",
               ("Route FAR is reported two ways. **Under the original test 2** (median triangles below both A) and **under addendum 6c** (the far-plane readback: camera_far 600 at start and end in FAR, 1000 in both A). "
                "6c was set after set-1 data and **accepted by the Studio Head with disclosure** (TestResults/9-studio-head-rulings.md), so the 6c row ranks; the original row is kept for disclosure." if a.accept_6c else
                "Route FAR is reported two ways. **Under the original test 2** (median triangles below both A; the default, which ranks) and "
                "**under addendum 6c** (the far-plane readback; PENDING the Studio Head's written acceptance, #25 note_technical-director_plan_route_addendum6c_status, "
                "shown but not ranked unless the script is run with --accept-6c). The analysis does not choose between them."), "",
               "| variant | outcome | valid runs | d per run | S_min | max bracket | why |", "|---|---|---|---|---|---|---|"]
        for variant, r in sorted(route2_results.items()):
            md.append(f"| {variant} | **{r['outcome']}** | {r['valid_runs']} | {', '.join(fmt(x) for x in r.get('d', []))} | {fmt(r['s_min'])} | {fmt(r.get('bracket_max'))} | {r['why']} |")
        md += ["", "| slot | frames | walk seconds | mean speed | p95 | median | p99 | test 1 problems |", "|---|---|---|---|---|---|---|---|"]
        for l in route2["launches"]:
            md.append(f"| {l['slot']} | {l.get('samples')} | {l.get('duration_s')} | {l.get('mean_speed_mps')} | {fmt(l.get('p95'))} | {fmt(l.get('median'))} | {fmt(l.get('p99'))} | {'; '.join(l['test1_problems']) or 'none'} |")
    md += ["", "## Q6 ranking (SAVING only)", ""] + ([f"{i + 1}. {x['variant']}: mean d {x['mean_d']:.3f} ms, a saving at {', '.join(x['saving_at'])}" for i, x in enumerate(ranked)] or ["None."])
    bad = [l for l in launches if not l.get("done") or l.get("error")]
    md += ["", "## Launch checks", "", f"Spot launches: {len(launches)}, not DONE or missing JSON: {len(bad)}. "
           f"Route launches DONE: {sum(1 for l in route_launches if l['done'])}/{len(route_launches)}; void by sample count: "
           f"{[l['slot'] for l in route_launches if l['void_by_sample_count']]}."]
    if rerun:
        md += rerun_section(a, rerun, out)
    md += extra_sections(run_dir, a, launches, route_launches, out)
    with open(os.path.join(a.out_dir, "9-analysis.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(md) + "\n")
    with open(os.path.join(a.out_dir, "9-analysis.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(out, f, indent=1, default=str)
    print(f"written {a.out_dir}/9-analysis.json and 9-analysis.md: {sum(len(v) for v in results.values())} spot comparisons, {len(route_results)} route variants, {len(ranked)} ranked")


if __name__ == "__main__":
    main()
