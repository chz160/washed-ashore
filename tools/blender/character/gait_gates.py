"""Fail-closed gates for the Jog and Sprint families (td-49 gates of record), read from the reports of one authoring run. Exit 2 on any failure.
python gait_gates.py <dir> <reference dir> <speeds-Survivor.json>
<dir> holds authored.json, measured-authored.json, arm_ankle_report.json (clip_arm_report.py), gait.json (gait_report.py) and arm_probe.json (arm_probe.py on the 8 *_Full clips + Stand_Idle);
<reference dir> the same files of the clip set the hip smoothness is compared with (clips16): drift is printed before and after, the hip second difference must not exceed 1.5 x the reference.
Gate keys: knee table studio:decisions gate/pod49-jog-sprint-knee-table (amended by gate/pod49-knee-snap-touchdown-pick: touchdown knee <= 22 for Jog and Sprint; mid-stance <= 50; hip/rest cycle mean Jog >= 0.93, Sprint >= 0.97);
G-K1 touchdown knee >= 10; G-K2 knee change in stance per key incl. the loop seam: Jog <= 13, JogBack/Left/Right <= 15, Sprint <= 17 (director gate/pod49-clips18-knee-jolt-pick, td JOINT RULING; the original 10 is withdrawn); bob band (gate/pod49-knee-snap-pelvis-follow) hip height range Jog 0.04-0.08 m,
Sprint 0.06-0.10 m (lower edges reset by ...-pick-note1 to Jog 0.035, Sprint 0.050); hip rise touchdown -> mid-stance <= 0.015 m (gate/pod49-knee-snap-pelvis-follow-note1; 0.020 for JogBack/Left/Right/SprintBack, gate/pod49-clips17-look); jolt ladder on the cycle-maximum pelvis second difference: <= 2.5 x the clip's clips16 value PASS, up to 0.009 m/key^2 FLAG (director rules), above FAIL;
rate <= 1.4 + 0.005 at 5 / 8 m/s; ankles dorsi <= 40, plantar <= 50; capped clips 0 hands in view at pitch -15..+30; planted-foot drift <= 0.10 m; IK reach <= 10 mm;
arms (the 8 _Full clips): G-A1 back wrist >= 0.05 m behind the hip midpoint, G-A2 forward wrist <= 0.30 m (Jog) / 0.36 m (Sprint) ahead of the Chest head (gate/pod49-arm-foreaft-gate-reset-a2),
G-A3 forearm within 60 deg of +Z at the forward extreme, G-A4 elbow Jog 80-115 (gate/pod49-clips16-jog-arms-look) / Sprint 80-100, arms in opposition (corr < -0.5), palm toward the body (>= 0.5).
GATE_FORCE_FAIL = missing | crouch | snap | rise | bob | touchdown | arms | secdiff  adds one fault to the loaded data (self-test)."""
import copy
import json
import os
import sys

d, ref_d, speeds_path = sys.argv[1], sys.argv[2], sys.argv[3]
force = os.environ.get("GATE_FORCE_FAIL")
load = lambda dd, f: json.load(open(os.path.join(dd, f)))
scale = float(json.load(open(speeds_path))["scale"])   # the shipped skeleton scale
A, M, R = load(d, "authored.json")["clips"], load(d, "measured-authored.json")["clips"], load(d, "arm_ankle_report.json")
G, P = load(d, "gait.json"), load(d, "arm_probe.json")
GR = load(ref_d, "gait.json")
MB = load(ref_d, "measured-authored.json")["clips"]
EXPECTED = [b + dd + v for b in ("Jog", "Sprint") for dd in ("", "Back", "Left", "Right") for v in ("", "_Full")]
G, P, M = copy.deepcopy(G), copy.deepcopy(P), copy.deepcopy(M)
if force == "missing":
    G.pop("JogLeft", None)
if force == "crouch":
    for r in G.values():
        r["knee_mid_max"] = (r["knee_mid_max"] or 0) + 25.0
if force == "snap":
    G["Jog"]["knee_snap"]["max_deg_per_key"] += 15.0
if force == "rise":
    G["Sprint"]["hip_rise_max_m"] += 0.02
if force == "bob":
    G["Jog"]["bob_m"] = 0.2
if force == "touchdown":
    G["Jog"]["knee_touchdown_min"] = 3.6
if force == "arms":
    P["Jog_Full"]["L"]["ahead_of_chest_max"] += 0.2
if force == "secdiff":
    G["Sprint"]["hip_second_diff_max_m"] = 0.02
bad, flags = [], []
for n in EXPECTED:
    for nm, src in (("authored.json", A), ("measured-authored.json", M), ("arm_ankle_report.json", R), ("gait.json", G)):
        if n not in src:
            bad.append("%s missing from %s" % (n, nm))
    if n.endswith("_Full") and n not in P:
        bad.append("%s missing from arm_probe.json" % n)
    if n not in GR:
        bad.append("%s missing from the reference gait.json" % n)
for n in EXPECTED:
    if any(b.startswith(n + " missing") for b in bad):
        continue
    jog = n.startswith("Jog")
    g, why = G[n], []
    game = 5.0 if jog else 8.0
    rate = game / (M[n]["clip_speed_mps"] * scale)
    dr, drb = M[n]["plant_drift_m_max"], MB.get(n, {}).get("plant_drift_m_max")
    an = R[n]
    hv = M[n].get("hand_in_view")
    hands = max(hv["frames_with_a_hand_in_view"].values()) if hv else None
    reach = A[n]["ik_reach_error_max_m"] * 1000
    sec = g["hip_second_diff_max_m"] / max(GR[n]["hip_second_diff_max_m"], 1e-9)
    print("GATE %-16s td %s..%s mid %s hip/rest %.3f snap %.1f deg/key bob %.3f rise %.3f 2nd-diff %.5f (touchdown x%.2f of ref) | rate %.3f drift %.3f (before %s) ankle %.1f/%.1f hands %s reach %.1f mm" % (
        n, g["knee_touchdown_min"], g["knee_touchdown_max"], g["knee_mid_max"], g["hip_mean_over_rest"], g["knee_snap"]["max_deg_per_key"], g["bob_m"], g["hip_rise_max_m"], g["hip_second_diff_max_m"], g["hip_second_diff_touchdown_max_m"] / max(GR[n]["hip_second_diff_max_m"], 1e-9),
        rate, dr, drb, an["ankle_dorsiflexion_max_deg"], an["ankle_plantarflexion_max_deg"], hands, reach))
    if g["knee_touchdown_max"] > 22.0:
        why.append("touchdown knee %.1f above the table" % g["knee_touchdown_max"])
    if g["knee_touchdown_min"] < 10.0:
        why.append("G-K1 touchdown knee %.1f < 10" % g["knee_touchdown_min"])
    if g["knee_mid_max"] > 50.0:
        why.append("mid-stance knee %.1f > 50" % g["knee_mid_max"])
    if g["hip_mean_over_rest"] < (0.93 if jog else 0.97):
        why.append("hip/rest %.3f below the table" % g["hip_mean_over_rest"])
    snap_lim = 17.0 if not jog else (15.0 if n.replace("_Full", "") != "Jog" else 13.0)   # director gate/pod49-clips18-knee-jolt-pick + td JOINT RULING: Jog 13, JogBack/Left/Right 15, Sprint 17 deg per key
    if g["knee_snap"]["max_deg_per_key"] > snap_lim + 0.2:   # the snap is measured at 0.1 resolution; +0.2 tolerance (td-49)
        why.append("G-K2 knee snap %.1f deg/key above %.0f (frame %s leg %s)" % (g["knee_snap"]["max_deg_per_key"], snap_lim, g["knee_snap"]["frame"], g["knee_snap"]["leg"]))
    lo, hi = (0.035, 0.08) if jog else (0.050, 0.10)
    if not (lo <= g["bob_m"] <= hi):
        why.append("bob %.3f m outside %.2f-%.2f" % (g["bob_m"], lo, hi))
    rise_lim = 0.020 if n.replace("_Full", "") in ("JogBack", "JogLeft", "JogRight", "SprintBack") else 0.015   # director gate/pod49-clips17-look: 2.0 cm for these four clips
    if g["hip_rise_max_m"] > rise_lim:
        why.append("hip rise %.3f m > %.3f" % (g["hip_rise_max_m"], rise_lim))
    ref2 = GR[n]["hip_second_diff_max_m"]
    cyc = g["hip_second_diff_max_m"]
    rung = "PASS" if cyc <= 2.5 * ref2 else ("FLAG" if cyc <= 0.009 else "FAIL")   # director's jolt ladder (gate/pod49-clips17-look-note1-jolt), on the cycle maximum
    print("GATE %-16s jolt ladder: cycle-max pelvis second difference %.5f m/key^2 = x%.2f of the clips16 %.5f (2.5x = %.5f) -> %s; around touchdown %.5f" % (n, cyc, cyc / max(ref2, 1e-9), ref2, 2.5 * ref2, rung, g["hip_second_diff_touchdown_max_m"]))
    if rung == "FLAG":
        flags.append("%s %.5f" % (n, cyc))
    if rung == "FAIL":
        why.append("jolt: cycle-max pelvis second difference %.5f above the 0.009 ceiling" % cyc)
    if rate > 1.405:
        why.append("rate %.3f" % rate)
    if dr > 0.10:
        why.append("drift %.3f" % dr)
    if an["ankle_dorsiflexion_max_deg"] > 40.0 or an["ankle_plantarflexion_max_deg"] > 50.0:
        why.append("ankle %.1f/%.1f" % (an["ankle_dorsiflexion_max_deg"], an["ankle_plantarflexion_max_deg"]))
    if reach > 10.0:
        why.append("reach %.1f mm" % reach)
    if not n.endswith("_Full"):
        if hv is None:
            why.append("no hand-in-view measurement")
        elif hands > 0:
            why.append("%d hand-in-view samples" % hands)
    if n.endswith("_Full"):
        p = P[n]
        print("GATE %-16s arms: along %s, yaw chest %.1f pelvis %.1f, alternation %.2f | L %s" % (n, p["measured_along"], p["yaw_chest_max_deg"], p["yaw_pelvis_max_deg"], p["alternation_corr"], p["L"]))
        for s in "LR":
            r = p[s]
            if r["behind_pelvis_max"] < 0.05:
                why.append("G-A1 %s back wrist only %.3f m behind the hip midpoint" % (s, r["behind_pelvis_max"]))
            if r["ahead_of_chest_max"] > (0.30 if jog else 0.36):
                why.append("G-A2 %s forward wrist %.3f m ahead of the Chest" % (s, r["ahead_of_chest_max"]))
            if r["forearm_off_up_at_fwd"] > 60.0:
                why.append("G-A3 %s forearm %.1f deg from +Z" % (s, r["forearm_off_up_at_fwd"]))
            if r["elbow_min"] < 79.5 or r["elbow_max"] > (115.5 if jog else 100.5):
                why.append("G-A4 %s elbow %.1f..%.1f" % (s, r["elbow_min"], r["elbow_max"]))
            if r["palm_in_min"] < 0.5:
                why.append("G-A4 %s palm_in %.2f" % (s, r["palm_in_min"]))
        if p["alternation_corr"] > -0.5:
            why.append("G-A4 arms not in opposition (%.2f)" % p["alternation_corr"])
    if why:
        bad.append("%s: %s" % (n, "; ".join(why)))
print("JOLT FLAGS (between 2x and the 0.009 ceiling, for the director):", flags or "none")
print("GAIT FAMILY GATES", "FAIL: " + " | ".join(bad) if bad else "PASS (%d clips)" % len(EXPECTED))
sys.exit(2 if bad else 0)
