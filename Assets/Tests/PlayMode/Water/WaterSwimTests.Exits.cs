using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>W6 exits (water-swim-spec.md §7.5): low banks are exits; bluff faces are not, but never trap a swimmer.</summary>
    public partial class WaterSwimTests
    {
        [UnityTest, Timeout(1800000)]
        public IEnumerator Exit_LowBankStations_ReachDryGround()
        {
            yield return LoadWorld();
            float w = cfg.WaterLevelY, lipTop = w + cfg.bankLipAboveWater;
            var all = WaterTestKit.Stations();
            var low = WaterTestKit.LowBank(all, rule, 30f);
            var stations = WaterTestKit.Pick(low, RandomStations, Seed);
            var extra = WaterTestKit.BluffNeighbours(all, low);
            extra.Add(WaterTestKit.Nearest(low, WestNeckCorner));
            foreach (var s in extra) if (!stations.Contains(s)) stations.Add(s);
            Debug.Log($"WaterSwimTests[exits]: seed {Seed}, {low.Count} low-bank stations, running {stations.Count}: {string.Join("; ", stations)}");

            var failures = new List<string>();
            var rows = new List<string>();
            try
            {
                foreach (var s in stations)
                {
                    var res = new ExitResult();
                    yield return Exit(s, w, lipTop, res);
                    rows.Add($"{Seed},{s.i},{s.x:F1},{s.z:F1},exit,{res.ok},{res.time:F2},{res.worstStall:F2},\"{res.why}\"");
                    if (!res.ok) failures.Add($"{s}: {res.why}");
                }
            }
            finally { ReleaseAll(); }
            WaterTestKit.AppendCsv("water-w6-exits.csv", "seed,station,x,z,kind,ok,seconds,worstStall,why", rows);
            Assert.IsEmpty(failures, "exit failures: " + string.Join("; ", failures));
            Assert.AreEqual(0, fallThroughs, "fall-through guard fired");
        }

        /// <summary>
        /// §7.5: at each bluff (the first bluff station, and the stations just before and after the bluff run, which are
        /// high banks: there is no low-bank station beside either bluff), swim at the shore for 10 s, then away for
        /// 10 s: the swimmer must get clear. Whether it climbed out is recorded, not graded.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator BluffFaces_NotAnExit_DoNotTrap()
        {
            yield return LoadWorld();
            float w = cfg.WaterLevelY;
            var all = WaterTestKit.Stations();
            var faces = new List<WaterTestKit.Station>();
            bool Bluff(int k) => k >= 0 && k < all.Length && (all[k].excluded ?? "").Contains("bluff");
            for (int k = 0; k < all.Length; k++)
            {
                if (Bluff(k) && !Bluff(k - 1)) { faces.Add(all[k]); if (k > 0) faces.Add(all[k - 1]); }
                if (Bluff(k) && !Bluff(k + 1) && k + 1 < all.Length) faces.Add(all[k + 1]);
            }
            Assert.IsNotEmpty(faces, "no bluff stations");
            var rows = new List<string>();
            var trapped = new List<string>();
            try
            {
                foreach (var s in faces)
                {
                    if (!WaterTestKit.Offshore(s.Position, w, ExitStart, out var dir)) { trapped.Add($"{s}: no offshore bearing"); continue; }
                    var start = s.Position + dir * ExitStart;
                    WaterTestKit.Ground(start, out float ground);
                    WaterTestKit.Place(player, start, Mathf.Max(ground + 0.05f, water.SurfaceY(start.x, start.z) - tuning.floatDepth), WaterTestKit.Yaw(-dir));
                    yield return new WaitForSeconds(0.5f);
                    Press(keyboard.wKey);
                    yield return new WaitForSeconds(10f);
                    var atFace = player.transform.position;
                    bool climbed = player.WaterMode == WaterMode.Dry;
                    player.transform.rotation = Quaternion.Euler(0f, WaterTestKit.Yaw(dir), 0f);
                    yield return new WaitForSeconds(10f);
                    ReleaseAll();
                    float away = WaterTestKit.Flat(player.transform.position - atFace).magnitude;
                    string kind = (s.excluded ?? "").Contains("bluff") ? "bluff" : "bluff-side";
                    rows.Add($"{Seed},{s.i},{s.x:F1},{s.z:F1},{kind},{climbed},{away:F1},0,\"{(climbed ? "climbed out" : "not an exit")}; swam {away:F1} m away\"");
                    if (away < 10f) trapped.Add($"{s}: only {away:F1} m away after 10 s");
                }
            }
            finally { ReleaseAll(); }
            WaterTestKit.AppendCsv("water-w6-exits.csv", "seed,station,x,z,kind,ok,seconds,worstStall,why", rows);
            Assert.IsEmpty(trapped, "swimmer trapped at a bluff: " + string.Join("; ", trapped));
            Assert.AreEqual(0, fallThroughs, "fall-through guard fired");
        }

        /// <summary>
        /// w-designer guard (spec §5: water never climbs what land can't): at the first bluff station of each run and the
        /// high-bank stations either side, swim into the shore for 10 s at 0 and 30 degrees. While swimming the feet never
        /// rise above S + 0.05, and the run never ends grounded on ground above W + stepOffset.
        /// </summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator BluffFaces_SwimmerNeverClimbs()
        {
            yield return LoadWorld();
            float w = cfg.WaterLevelY, step = player.GetComponent<CharacterController>().stepOffset;
            var all = WaterTestKit.Stations();
            bool Bluff(int k) => k >= 0 && k < all.Length && (all[k].excluded ?? "").Contains("bluff");
            var faces = new List<WaterTestKit.Station>();
            for (int k = 0; k < all.Length; k++)
            {
                if (Bluff(k) && !Bluff(k - 1)) { faces.Add(all[k]); if (k > 0) faces.Add(all[k - 1]); }
                if (Bluff(k) && !Bluff(k + 1) && k + 1 < all.Length) faces.Add(all[k + 1]);
            }
            var failures = new List<string>();
            var rows = new List<string>();
            try
            {
                foreach (var s in faces)
                    foreach (float angle in new[] { 0f, 30f })
                    {
                        ReleaseAll();
                        if (!WaterTestKit.Offshore(s.Position, w, ExitStart, out var dir)) { failures.Add($"{s}: no offshore bearing"); continue; }
                        var start = s.Position + dir * ExitStart;
                        WaterTestKit.Ground(start, out float ground);
                        WaterTestKit.Place(player, start, Mathf.Max(ground + 0.05f, water.SurfaceY(start.x, start.z) - tuning.floatDepth), WaterTestKit.Yaw(-dir) + angle);
                        yield return new WaitForSeconds(0.5f);
                        float worstAbove = float.MinValue;
                        Press(keyboard.wKey);
                        for (float t = 0f; t < 10f; t += Time.deltaTime)
                        {
                            yield return null;
                            var p = player.transform.position;
                            if (player.WaterMode == WaterMode.Swim) worstAbove = Mathf.Max(worstAbove, p.y - player.SurfaceY);
                        }
                        ReleaseAll();
                        yield return new WaitForSeconds(0.5f);
                        var end = player.transform.position;
                        bool onHigh = player.IsGrounded && end.y > w + step;
                        rows.Add($"{Seed},{s.i},{s.x:F1},{s.z:F1},climb-guard-{angle:F0},{!(worstAbove > 0.05f || onHigh)},{worstAbove:F3},0,\"end y={end.y:F2} mode={player.WaterMode} grounded={player.IsGrounded}\"");
                        if (worstAbove > 0.05f) failures.Add($"{s} at {angle} deg: swimming feet {worstAbove:F2} m above S");
                        if (onHigh) failures.Add($"{s} at {angle} deg: ended grounded at y={end.y:F2} > W + stepOffset");
                    }
            }
            finally { ReleaseAll(); }
            WaterTestKit.AppendCsv("water-w6-exits.csv", "seed,station,x,z,kind,ok,seconds,worstStall,why", rows);
            Assert.IsEmpty(failures, "water climbed what land can't: " + string.Join("; ", failures));
            Assert.AreEqual(0, fallThroughs, "fall-through guard fired");
        }

        class ExitResult { public bool ok; public float time, worstStall; public string why = ""; }

        /// <summary>Starts a swimmer 20 m off <paramref name="s"/>, holds W toward it, and times the climb out.</summary>
        IEnumerator Exit(WaterTestKit.Station s, float w, float lipTop, ExitResult res)
        {
            ReleaseAll();
            if (!WaterTestKit.Offshore(s.Position, w, ExitStart + 5f, out var dir)) { res.why = "no offshore bearing"; yield break; }
            var start = s.Position + dir * ExitStart;
            WaterTestKit.Ground(start, out float ground);
            WaterTestKit.Place(player, start, Mathf.Max(ground + 0.05f, water.SurfaceY(start.x, start.z) - tuning.floatDepth), WaterTestKit.Yaw(-dir));
            yield return new WaitForSeconds(0.5f);
            if (player.WaterMode != WaterMode.Swim) { res.why = $"not swimming at the start ({player.WaterMode}, depth {player.WaterDepth:F2})"; yield break; }

            float t = 0f, slow = 0f;
            Vector3 last = player.transform.position;
            Press(keyboard.wKey);
            while (t < ExitDeadline)
            {
                yield return null;
                t += Time.deltaTime;
                var p = player.transform.position;
                if (player.IsGrounded && p.y >= lipTop && player.WaterMode == WaterMode.Dry)
                {
                    res.ok = true;
                    res.time = t;
                    break;
                }
                float speed = WaterTestKit.Flat(p - last).magnitude / Time.deltaTime;
                last = p;
                // A stall: input held, horizontal speed under 0.2 m/s (after the first second's acceleration).
                slow = t > 1f && speed < StallSpeed ? slow + Time.deltaTime : 0f;
                res.worstStall = Mathf.Max(res.worstStall, slow);
                if (slow > StallSeconds)
                {
                    res.why = $"stalled at {p:F2} ({player.WaterMode}, depth {player.WaterDepth:F2}, grounded {player.IsGrounded}) after {t:F1} s";
                    break;
                }
            }
            ReleaseAll();
            if (!res.ok && res.why.Length == 0)
                res.why = $"not out after {ExitDeadline} s at {player.transform.position:F2} ({player.WaterMode})";
        }
    }
}
