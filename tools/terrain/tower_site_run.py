"""Lookout tower site runbook, headless. Run only while holding the Unity baton.

    /c/Python313/python.exe tools/terrain/tower_site_run.py site    # pad, marker, zones, road map, repaint, before/after compare
    /c/Python313/python.exe tools/terrain/tower_site_run.py place   # tower prefab + TowerBaseSpawn, P1 report
    /c/Python313/python.exe tools/terrain/tower_site_run.py view    # P3 probe

Each mode runs `Unity -batchmode -executeMethod BellsBendTowerCli.<Mode>` through the unity CLI and prints
TestResults/tower-level/cli-<mode>.txt. After `site` it restores the art assets that a headless repaint re-saves
without changing what they draw (plant materials' legacy _MainTex/_Color, line ends of terrain layers and plant
prefabs; see art-assets-rewritten-by-repaint.diff) and reports exactly which paths it restored. After any mode it
restores files that come back with line-ending-only changes. Exit code: 0 if the run reported RESULT: OK.
"""
import re
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
EVID = ROOT / "TestResults/tower-level"
ART = "Assets/World/BellsBend/Art"          # re-saved by BellsBendGround.Build in batchmode
EOL_ONLY = ["Assets/World/BellsBend/Water/WaterTile.asset", "ProjectSettings/ProjectSettings.asset"]
MODES = {"site": "Site", "place": "Place", "view": "View"}


def git(*args):
    return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True).stdout


def modified(path):
    return [l[3:] for l in git("status", "--porcelain", "--", path).splitlines() if l[:2].strip() == "M"]


def harmless(path):
    """True if the file's diff is line endings only, or (plant materials) only the legacy _MainTex / _Color mirror lines
    that Tinted() resets in batchmode. Anything else is a real change and must not be reverted here."""
    body = git("diff", "--ignore-cr-at-eol", "-U3", "--", path)
    if not body.strip():
        return True
    if not re.search(r"/Plants/M_[^/]+\.mat$", path):
        return False
    prop = None
    for line in body.splitlines():
        if line.startswith(("+++", "---", "diff ", "index ", "@@")):
            prop = None if line.startswith("@@") else prop
            continue
        text = line[1:].strip()
        m = re.match(r"- (_\w+):", text)
        if m:
            prop = m.group(1)
        if line[:1] in "+-":
            if text.startswith("- _Color:"):
                continue
            if text.startswith("m_Texture:") and prop == "_MainTex":
                continue
            return False
    return True


def restore(paths, why, keep_diff=None):
    paths = [p for p in paths if p]
    if keep_diff:  # always written, so the evidence folder reflects this run (empty = nothing was re-saved)
        (EVID / keep_diff).write_text(git("diff", "--", *paths) if paths else "", encoding="utf-8")
    if not paths:
        print(f"restore ({why}): nothing to restore")
        return
    subprocess.run(["git", "checkout", "--", *paths], cwd=ROOT, check=True)
    print(f"restored ({why}):")
    for p in paths:
        print("  " + p)


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    if mode not in MODES:
        sys.exit(__doc__)
    EVID.mkdir(parents=True, exist_ok=True)
    out = EVID / f"cli-{mode}.txt"
    if out.exists():
        out.unlink()
    dirty_art = modified(ART)
    if dirty_art:
        sys.exit(f"{ART} already has modified files; commit or restore them first:\n  " + "\n  ".join(dirty_art))
    p = subprocess.run(["unity", "run", ".", "--no-tail", "--timeout", "2400", "--log-file", str(EVID / f"unity-{mode}.log"),
                        "--", "-executeMethod", f"BellsBendTowerCli.{MODES[mode]}"], cwd=ROOT, capture_output=True, text=True, shell=sys.platform == "win32")
    text = out.read_text(encoding="utf-8") if out.exists() else f"no {out.name}: Unity exit {p.returncode}\n{p.stdout[-2000:]}{p.stderr[-2000:]}"
    print(text)
    stop = []
    if mode == "site":
        art = modified(ART)
        stop = [f for f in art if not harmless(f)]
        stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
        restore([f for f in art if f not in stop], "re-saved by the headless repaint: line endings or legacy _MainTex/_Color only",
                f"art-assets-rewritten-by-repaint-{stamp}.diff")
    other = [l for l in git("status", "--porcelain", "--", ART).splitlines() if l[:2] != " M"]
    if other:
        print(f"NOTE: {ART} has new, deleted or staged entries (not restored):")
        for l in other:
            print("  " + l)
    changed = [f for f in EOL_ONLY if modified(f)]
    real = stop + [f for f in changed if git("diff", "--ignore-cr-at-eol", "--", f).strip()]
    restore([f for f in changed if f not in real], "line endings only")
    if real:  # never restore a content change: stop and say so
        print("STOP: changed in content, not only line endings; left as they are for review:")
        for f in real:
            print("  " + f)
        sys.exit(2)
    sys.exit(0 if "RESULT: OK" in text else 1)  # a FAILED batchmode entry is a non-zero exit here too


if __name__ == "__main__":
    main()
