using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// spec-64 s5c (td part14): F5t up at theta 0/10/20/30 under (a) GroundSnapDistance 0, (b) stepOffset 0.25, (c) both. Probe-side settings only; everything else is exactly F5t.
    /// Supplementary: fails only when PROBE VALID is no.
    /// </summary>
    public partial class ControllerProbe64
    {
        [UnityTest, Explicit("#64 controller probe: fails on main by design"), Category("ControllerProbe64"), Timeout(3600000)]
        public IEnumerator Probe64_F5tDiag()
        {
            yield return Begin("Probe64_F5tDiag", 1f / 60f);
            ProbeTowerStateSetter setter = null;
            float sR = 0, sH = 0, sStep = 0, sSlope = 0;
            bool flightSetup = false;
            try
            {
                yield return null;
                yield return null;
                Vector3 c = new Vector3(508f, 0f, -230f);
                float maxG = float.MinValue;
                foreach (var p in new[] { c, c + new Vector3(20, 0, 20), c + new Vector3(-20, 0, 20), c + new Vector3(20, 0, -20), c + new Vector3(-20, 0, -20) })
                    if (TerrainQuery.TryGroundHeight(p, out float g)) maxG = Mathf.Max(maxG, g);
                origin = new Vector3(c.x, maxG + 30f, c.z);
                var slab = new GameObject("PROBE64_Slab") { layer = 0 };
                slab.transform.position = origin - Vector3.up * 0.5f;
                slab.AddComponent<BoxCollider>().size = new Vector3(40, 1, 40);
                slabGo = slab;
                yield return new WaitForFixedUpdate();
                yield return TestSpawn.At(origin + new Vector3(0f, 0.05f, -8f), 0f, false);
                for (int i = 0; i < 5; i++) yield return null;
                WriteHeader("Probe64_F5tDiag");
                Emit($"fixture O {origin:F2} (y_O = max terrain {maxG:F2} + 30); slab 40 x 1 x 40, top face at y_O");
                FixtureChecks();

                sR = cc.radius; sH = player.StandingHeight; sStep = cc.stepOffset; sSlope = cc.slopeLimit;
                flightSetup = true;
                Emit($"F5t saved tower state: r {sR:F3} h {sH:F3} step {sStep:F3} slope {sSlope:F0} WalkSpeedCap {player.WalkSpeedCap:F3} snap {player.GroundSnapDistance:F2}");
                TestSpawn.Reposition(player, origin + new Vector3(0, 1f, -8f), 0f);
                player.SetWalkCapsule(0.25f, 1.6f); cc.slopeLimit = 80f;
                setter = player.gameObject.AddComponent<ProbeTowerStateSetter>(); setter.player = player;
                var fixtureRows = ReadCsv("Probe64_Fixture");
                foreach (string variant in new[] { "a", "b", "c" })
                {
                    setter.snap = variant == "b" ? 0.35f : 0f;                       // (a) and (c): GroundSnapDistance 0
                    cc.stepOffset = variant == "a" ? 0.05f : 0.25f;                   // (b) and (c): stepOffset 0.25
                    yield return null;
                    Emit($"variant {variant}: r {cc.radius:F3} h {cc.height:F3} step {cc.stepOffset:F3} slope {cc.slopeLimit:F0} WalkSpeedCap {player.WalkSpeedCap:F3} snap {player.GroundSnapDistance:F2}");
                    var refs = new Dictionary<string, ProbeResult>();
                    foreach (var s in CasesFlight(true, true))
                    {
                        s.variant = variant;
                        yield return Go(s, refs);
                        var hit = fixtureRows.FirstOrDefault(d => d["case"] == "F5t" && d["dir"] == "up" && d["yaw_off"] == F(s.yawOff, 0));
                        Emit("    Probe64_Fixture F5t up row (snap 0.35, step 0.05): " + (hit == null ? "-" : $"stops {hit["stops"]} kept_path {hit["kept_path"]}"));
                    }
                }
                ClearBuilt();
                yield return new WaitForFixedUpdate();
                EmitOverlap("after");
                Summary("Probe64_F5tDiag", false);
            }
            finally
            {
                ClearBuilt();
                if (slabGo) Object.DestroyImmediate(slabGo);
                if (setter) Object.Destroy(setter);
                if (flightSetup && player)
                {
                    player.SetWalkCapsule(sR, sH); cc.stepOffset = sStep; cc.slopeLimit = sSlope;
                    player.WalkSpeedCap = 0f; player.GroundSnapDistance = 0f;
                    Emit($"F5t tower state as restored: r {cc.radius:F3} h {cc.height:F3} step {cc.stepOffset:F3} slope {cc.slopeLimit:F0} WalkSpeedCap {player.WalkSpeedCap:F3} snap {player.GroundSnapDistance:F2}");
                }
                End();
            }
            Verdict("Probe64_F5tDiag");
        }
    }
}
