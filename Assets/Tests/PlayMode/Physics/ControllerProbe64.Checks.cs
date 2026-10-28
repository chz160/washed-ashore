using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WashedAshore.Lookout;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// spec-64 r9 5d row checks on the tower flights: POP, CLIMB, OFF RAMP (THROUGH is in Analyse). Flags only in Probe64_Tower; in Probe64_TowerStep they fail the row (Judge).
    /// h = feet y - surface y (the nosing line inside [0, L], the head deck beyond L going up, the foot beyond 0 going down); h0 = h at the section start.
    /// </summary>
    public partial class ControllerProbe64
    {
        TowerWalkZones zones;

        float SurfaceAt(ProbeRun s, float a) => a <= 0f ? s.surfRef : a >= s.spanL ? s.surfFar : s.surfRef + (s.surfFar - s.surfRef) * a / s.spanL;

        string TowerChecks(ProbeRun s, ProbeResult res, List<Vector3> f, List<bool> onRamp, int kS, int kE, int kLast)
        {
            const float PopRise = 0.05f, PopFrame = 0.06f, ClimbRise = 0.10f, OffDrop = -0.10f; const int Frames = 6;
            float h0 = f[kS].y - SurfaceAt(s, Along(s, f[kS]));
            float maxRise = float.NegativeInfinity, maxFrame = float.NegativeInfinity;
            int climbRun = 0, climbFirst = -1, offRun = 0; bool pop = false, off = false;
            for (int k = 1; k <= kLast; k++)
            {
                float a = Along(s, f[k]), ap = Along(s, f[k - 1]);
                float h = f[k].y - SurfaceAt(s, a), rel = h - h0;
                maxRise = Mathf.Max(maxRise, rel);
                float frameRise = (f[k].y - f[k - 1].y) - (SurfaceAt(s, a) - SurfaceAt(s, ap));
                maxFrame = Mathf.Max(maxFrame, frameRise);
                if (rel > PopRise || frameRise > PopFrame) pop = true;
                if (rel > ClimbRise) { climbRun++; if (climbRun >= Frames && climbFirst < 0) climbFirst = k - Frames + 1; } else climbRun = 0;
                if (k >= kS && k <= kE)
                {
                    if (!onRamp[k]) { offRun++; if (offRun >= Frames) off = true; } else offRun = 0;
                    if (rel < OffDrop) off = true;
                }
            }
            string collider = "-";
            if (climbFirst >= 0)
            {
                Vector3 from = f[climbFirst] + Vector3.up * cc.center.y;
                float best = float.MaxValue;
                foreach (var hit in Physics.RaycastAll(from, Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore))
                    if (!hit.collider.transform.IsChildOf(player.transform) && hit.distance < best) { best = hit.distance; collider = hit.collider.name; }
            }
            if (pop && !res.flags.Contains("POP")) res.flags.Add("POP");
            if (climbFirst >= 0) res.flags.Add("CLIMB");
            if (off) res.flags.Add("OFF RAMP");
            return $"    checks: max h-h0 {F(maxRise, 3)} max single-frame rise over the surface {F(maxFrame, 3)}; climb {(climbFirst < 0 ? "none" : $"from frame {climbFirst}, first collider below {collider}")}; off ramp {(off ? "yes" : "no")}";
        }
    }
}
