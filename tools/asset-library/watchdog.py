"""Watchdog for a long download.py run. Task Scheduler runs it every 10 minutes (see README).

    pythonw tools/asset-library/watchdog.py           # one check, then exit
    python  tools/asset-library/watchdog.py --status  # print the current state

Reads the heartbeat that download.py writes to local staging every 30 s:
- state finished or stopped: do nothing (the run ended on purpose).
- process gone while running: restart download.py (it resumes).
- process alive but no bytes received for STALL_MIN minutes: kill it and restart.
- more than MAX_RESTARTS restarts within WINDOW_H hours: stop restarting, write an ALERT and leave it
  for a person.
The log is local (E:\\AssetLibrary-staging\\watchdog.log), so a hung NAS can't hang the watchdog too.
"""
from __future__ import annotations

import datetime as _dt
import json
import subprocess
import sys
import time
from pathlib import Path

from common import LIB, STAGING

HERE = Path(__file__).resolve().parent
PYTHON = Path(sys.executable).with_name("python.exe")
HEARTBEAT = STAGING / "heartbeat.json"
STATE = STAGING / "watchdog_state.json"
WLOG = STAGING / "watchdog.log"
STALL_MIN = 20
HEARTBEAT_STALE_MIN = 5
MAX_RESTARTS = 6
WINDOW_H = 6


def wlog(msg):
    STAGING.mkdir(parents=True, exist_ok=True)
    with open(WLOG, "a", encoding="utf-8") as f:
        f.write(f"{_dt.datetime.now():%Y-%m-%d %H:%M:%S} {msg}\n")


def load(p, default):
    try:
        return json.loads(p.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return default


def alive(pid) -> bool:
    out = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, creationflags=subprocess.CREATE_NO_WINDOW).stdout
    return f'"{pid}"' in out and "python" in out.lower()


def kill(pid):
    subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True,
                   creationflags=subprocess.CREATE_NO_WINDOW)


def start():
    logdir = LIB / "_logs"
    stamp = _dt.date.today().isoformat()
    out = open(logdir / f"download-{stamp}.log", "a", encoding="utf-8")
    err = open(logdir / f"download-{stamp}.err", "a", encoding="utf-8")
    p = subprocess.Popen([str(PYTHON), "-u", "download.py"], cwd=HERE, stdout=out, stderr=err,
                         creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
                         | subprocess.CREATE_NO_WINDOW)
    return p.pid


def main() -> int:
    hb = load(HEARTBEAT, None)
    st = load(STATE, {"restarts": [], "alert": None})
    if "--status" in sys.argv:
        print(json.dumps({"heartbeat": hb, "watchdog": st}, indent=2))
        return 0
    if st.get("alert"):
        return 0                                   # a person has to look first
    if not hb:
        wlog("no heartbeat yet; nothing to watch")
        return 0
    if hb["state"] in ("finished", "stopped"):
        if not st.get("noted_end"):
            wlog(f"run ended: {hb['state']} ({hb.get('detail')}); not restarting")
            st["noted_end"] = True
            STATE.write_text(json.dumps(st), encoding="utf-8")
        return 0

    now = time.time()
    pid = hb["pid"]
    is_alive = alive(pid)
    stalled_min = (now - hb.get("last_progress", hb["time"])) / 60
    hb_age_min = (now - hb["time"]) / 60
    reason = None
    if not is_alive:
        reason = f"process {pid} is gone (state {hb['state']})"
    elif stalled_min > STALL_MIN:
        reason = f"no bytes for {stalled_min:.0f} min"
    elif hb_age_min > HEARTBEAT_STALE_MIN + STALL_MIN:
        reason = f"heartbeat {hb_age_min:.0f} min old"
    if not reason:
        wlog(f"ok: pid {pid}, {hb['bytes'] / 1e9:.1f} GB this run, last progress {stalled_min:.1f} min ago")
        return 0

    recent = [t for t in st["restarts"] if now - t < WINDOW_H * 3600]
    if len(recent) >= MAX_RESTARTS:
        st["alert"] = f"{_dt.datetime.now():%Y-%m-%d %H:%M} gave up: {len(recent)} restarts in {WINDOW_H} h; last: {reason}"
        wlog("ALERT " + st["alert"])
        STATE.write_text(json.dumps(st), encoding="utf-8")
        return 1
    if is_alive:
        kill(pid)
        time.sleep(5)
    new = start()
    recent.append(now)
    st["restarts"] = recent
    STATE.write_text(json.dumps(st), encoding="utf-8")
    # A fresh heartbeat so the next check doesn't see the old pid before the new one writes.
    HEARTBEAT.write_text(json.dumps({**hb, "pid": new, "time": now, "last_progress": now, "bytes": 0,
                                     "state": "running", "detail": "restarted by watchdog"}), encoding="utf-8")
    wlog(f"restarted: {reason}; new pid {new}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
