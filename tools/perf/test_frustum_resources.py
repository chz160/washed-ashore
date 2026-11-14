"""Unit tests for frustum_resources.py (synthetic window rows; no player data).

Run from the repo root: python -I tools/perf/test_frustum_resources.py
"""
import json
import os
import shutil
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import frustum_resources as fr  # noqa: E402

MB = 1024 * 1024


def summary(median, p95=None, mx=None):
    return {"count": 100, "median": median, "p95": p95 if p95 is not None else median, "p99": median, "max": mx if mx is not None else median, "mean": median}


def window(variant, frame_p95, gpu=None, reason=None, used_mb=1000.0, tex_mb=500.0, index=0, mip=0):
    return {
        "spot": "park", "window": index, "variant_id": variant, "valid": True,
        "frame_ms": summary(7.0, frame_p95),
        "readback_after_timing": {"texture_mip_limit": mip, "lod_bias": 2, "msaa": 1, "camera_aa": "None", "upscaling_filter": "Auto",
                                  "stp_used": False, "render_post_processing": False, "allow_msaa": True, "depth_priming": ["Disabled"],
                                  "depth_priming_not_blocked_by_msaa": True},
        "resources": {
            "frame_timing": {"enabled": True, "timed_frames": 2000, "frames_accepted": 1996,
                             "cpu_frame_ms": summary(7.1, frame_p95), "cpu_main_thread_ms": summary(5.0), "cpu_render_thread_ms": summary(3.0),
                             "gpu_frame_ms": summary(gpu, gpu + 1) if gpu is not None else None, "gpu_zero_frames": 0 if gpu is not None else 1996,
                             "gpu_null_reason": reason},
            "memory": {"total_used_bytes": summary(used_mb * MB, mx=(used_mb + 10) * MB), "total_reserved_bytes": summary(2000.0 * MB),
                       "gc_used_bytes": summary(50.0 * MB), "gc_reserved_bytes": summary(60.0 * MB), "gfx_used_bytes": summary(800.0 * MB),
                       "gfx_reserved_bytes": None, "system_used_bytes": summary(3000.0 * MB), "texture_bytes": None, "mesh_bytes": None},
            "texture_start": {"current_bytes": tex_mb * MB, "total_bytes": tex_mb * MB, "desired_bytes": None, "target_bytes": None},
            "texture_end": {"current_bytes": tex_mb * MB, "total_bytes": tex_mb * MB, "desired_bytes": None, "target_bytes": None},
        },
    }


class ExtractTests(unittest.TestCase):
    def test_null_gpu_stays_none_never_zero(self):
        r = fr.extract(window("A", 9.0, gpu=None, reason="gpu time 0 on every frame (unsupported on this API or driver)"))
        self.assertIsNone(r["gpu_frame_p95_ms"])
        self.assertIsNone(r["gpu_frame_p50_ms"])
        self.assertTrue(r["gpu"].startswith("unavailable (gpu time 0"))
        self.assertNotEqual(r["gpu_frame_p95_ms"], 0.0)

    def test_gpu_value_and_memory_in_mb(self):
        r = fr.extract(window("A", 9.0, gpu=6.0, used_mb=1234.5))
        self.assertEqual(r["gpu"], "ok")
        self.assertEqual(r["gpu_frame_p50_ms"], 6.0)
        self.assertEqual(r["gpu_frame_p95_ms"], 7.0)
        self.assertAlmostEqual(r["total_used_median_mb"], 1234.5)
        self.assertAlmostEqual(r["total_used_max_mb"], 1244.5)
        self.assertIsNone(r["texture_desired_end_mb"])
        self.assertIsNone(r["texture_memory_dev_median_mb"])
        self.assertEqual(r["readback"]["texture_mip_limit"], 0)

    def test_pre_phase0_window_is_na(self):
        w = window("A", 9.0)
        del w["resources"]
        r = fr.extract(w)
        self.assertEqual(r["harness"], fr.PRE_PHASE0)
        self.assertEqual(r["gpu"], fr.PRE_PHASE0)
        self.assertIsNone(r["total_used_median_mb"])
        self.assertEqual(r["frame_p95_ms"], 9.0)

    def test_route_row_reads_readback_end(self):
        w = window("cli", 9.0, gpu=5.0)
        w["readback_end"] = w.pop("readback_after_timing")
        self.assertEqual(fr.extract(w)["readback"]["msaa"], 1)


class DeltaTests(unittest.TestCase):
    def test_delta_is_v_minus_mean_a(self):
        a0 = fr.extract(window("A", 9.0, gpu=6.0, used_mb=1000.0, tex_mb=500.0))
        a1 = fr.extract(window("A", 9.4, gpu=6.4, used_mb=1010.0, tex_mb=500.0))
        v = fr.extract(window("MIP1", 8.7, gpu=5.0, used_mb=900.0, tex_mb=200.0, mip=1))
        d = fr.bracket_delta(v, a0, a1)
        self.assertAlmostEqual(d["frame_p95_ms"], 8.7 - 9.2)
        self.assertAlmostEqual(d["gpu_frame_p95_ms"], 6.0 - 7.2)
        self.assertAlmostEqual(d["total_used_median_mb"], 900.0 - 1005.0)
        self.assertAlmostEqual(d["texture_current_end_mb"], -300.0)

    def test_delta_missing_is_none_not_zero(self):
        a0 = fr.extract(window("A", 9.0, gpu=6.0))
        a1 = fr.extract(window("A", 9.0, gpu=None, reason="no frames captured"))
        v = fr.extract(window("V", 9.0, gpu=6.0))
        d = fr.bracket_delta(v, a0, a1)
        self.assertIsNone(d["gpu_frame_p95_ms"])
        self.assertIsNone(d["texture_desired_end_mb"])
        self.assertEqual(d["frame_p95_ms"], 0.0)


class FolderTests(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.mkdtemp()

    def tearDown(self):
        shutil.rmtree(self.dir)

    def slot(self, name, rows, done=True):
        d = os.path.join(self.dir, name)
        os.makedirs(d)
        with open(os.path.join(d, "frustum-spots.json"), "w", encoding="utf-8") as f:
            json.dump({"windows": rows}, f)
        with open(os.path.join(d, "progress.txt"), "w", encoding="utf-8") as f:
            f.write("2026-11-13T00:00:00Z START\n" + ("2026-11-13T00:10:00Z DONE spots\n" if done else "2026-11-13T00:05:00Z WINDOW 1 park V\n"))

    def test_brackets_and_skips_unfinished(self):
        self.slot("s01-park", [window("A", 9.0, gpu=6.0, index=0), window("V", 8.0, gpu=5.0, index=1), window("A", 9.2, gpu=6.2, index=2)])
        self.slot("s02-park", [window("A", 9.0, index=0)], done=False)
        rep = fr.analyse(self.dir)
        self.assertEqual(len(rep["launches"]), 1)
        self.assertEqual(rep["skipped"][0]["slot"], "s02-park")
        v = rep["launches"][0]["windows"][1]
        self.assertAlmostEqual(v["delta_vs_A"]["frame_p95_ms"], 8.0 - 9.1)
        self.assertNotIn("delta_vs_A", rep["launches"][0]["windows"][0])
        md = fr.markdown(rep)
        self.assertIn("not part of S1", md)
        self.assertIn("| s01-park | park | 1 | V |", md)

    def test_main_writes_both_files(self):
        self.slot("s01-park", [window("A", 9.0, index=0)])
        out = os.path.join(self.dir, "out")
        self.assertEqual(fr.main(["--run-dir", self.dir, "--out-dir", out]), 0)
        self.assertTrue(os.path.exists(os.path.join(out, "resources.json")))
        self.assertIn("unavailable (no reason recorded)", open(os.path.join(out, "resources.md"), encoding="utf-8").read())

    def test_same_bytes_however_the_path_is_spelled(self):
        self.slot("s01-park", [window("A", 9.0, index=0)])
        a = fr.analyse(self.dir)
        b = fr.analyse(self.dir + os.sep + "." + os.sep)
        try:
            rel = os.path.relpath(self.dir)
        except ValueError:   # temp folder on another drive
            rel = self.dir
        c = fr.analyse(rel)
        self.assertEqual(json.dumps(a, sort_keys=True), json.dumps(b, sort_keys=True))
        self.assertEqual(json.dumps(a, sort_keys=True), json.dumps(c, sort_keys=True))
        self.assertEqual(a["run_dir"], os.path.basename(self.dir), "outside the repo: the folder name only")
        inside = os.path.join(fr.REPO_ROOT, "TestResults", "9-runs", "20261112-1803")
        self.assertEqual(fr.run_label(inside), "TestResults/9-runs/20261112-1803")
        self.assertEqual(fr.run_label(os.path.join(fr.REPO_ROOT, "TestResults", ".", "9-runs", "20261112-1803")), "TestResults/9-runs/20261112-1803")

    def test_default_out_dir_is_outside_the_run_folder(self):
        run = os.path.join(fr.REPO_ROOT, "TestResults", "9-runs", "20261112-1803")
        out = fr.default_out_dir(run)
        self.assertFalse(os.path.abspath(out).startswith(os.path.abspath(run)))
        self.assertEqual(os.path.basename(out), "20261112-1803")


if __name__ == "__main__":
    unittest.main()
