using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WashedAshore.Tests.Support
{
    /// <summary>
    /// The render-faithful Bar 1 rule (td-49 "BAR 1 ASSERT TEXT OF RECORD (R3)" B1, Noah via team-lead: "What the player sees"; director-49 studio:decisions gate/pod49-bar1-gap-g-and-standidle-ruling).
    /// A ray is a Bar 1 defect when its nearest hit (chosen exactly as before, under MirrorTie) is a back face AND the nearest FRONT-facing hit behind it is absent (NoFront), more than G behind it (GapOverG),
    /// on an unknown region (UnknownRegion, fail closed) or on another body region (OtherRegion). Units: t is world metres along the ray over the world-space baked triangles, never mesh units.
    /// G lives only here. Test code only; nothing in the game reads it.
    /// </summary>
    public static class BarOneRule
    {
        /// <summary>
        /// The gap G in metres, PER REGION of the winner (back-face) triangle: LEG.L and LEG.R 35 mm; ARM.L, ARM.R, TORSO and HEAD 10 mm. Director-set (studio:decisions gate/pod49-bar1-gap-g-and-standidle-ruling and
        /// gate/pod49-bar1-gap-g-and-standidle-ruling-note1-per-region, "per-region as td"); it may still go to Noah. THIS LOOKUP IS THE ONLY PLACE G LIVES: no test hard-codes a gap. UNKNOWN has no G: Judge fails it closed first.
        /// </summary>
        public static float GapMetres(string region) => region == "LEG.L" || region == "LEG.R" ? 0.035f : 0.010f;

        /// <summary>Test-only override, set by the forced cases and reset in a finally. It replaces G for EVERY region. Effective G = override ?? GapMetres(region).</summary>
        public static float? GapOverrideForTest;

        public static float G(string region) => GapOverrideForTest ?? GapMetres(region);

        /// <summary>The G values in force, for failure texts: "G legs 35 mm, arms/torso/head 10 mm" or "G override N mm (all regions)".</summary>
        public static string GText => GapOverrideForTest.HasValue ? $"G override {GapOverrideForTest.Value * 1000f:F0} mm (all regions)" : $"G legs {GapMetres("LEG.L") * 1000f:F0} mm, arms/torso/head {GapMetres("ARM.L") * 1000f:F0} mm";

        public enum Verdict { Ok, NoFront, GapOverG, OtherRegion, UnknownRegion }

        /// <summary>Region of a game bone by name: HEAD (Head, Neck), TORSO (Root, Hips, Abdomen, Torso, Chest), ARM.L / ARM.R (Shoulder, UpperArm, LowerArm, Wrist and the finger bones of that side), LEG.L / LEG.R (UpperLeg, LowerLeg, Foot, toes of that side); *_end and PT.* take their parent's region. Anything else is "UNKNOWN".</summary>
        public static string Region(string bone)
        {
            if (string.IsNullOrEmpty(bone)) return "UNKNOWN";
            string s = bone;
            if (s.EndsWith("_end")) s = s.Substring(0, s.Length - 4);
            string side = s.EndsWith(".L") ? ".L" : (s.EndsWith(".R") ? ".R" : "");
            string baseName = side.Length > 0 ? s.Substring(0, s.Length - 2) : s;
            switch (baseName)
            {
                case "Head": case "Neck": return "HEAD";
                case "Root": case "Hips": case "Abdomen": case "Torso": case "Chest": return "TORSO";
                case "Shoulder": case "UpperArm": case "LowerArm": case "Wrist": return side.Length > 0 ? "ARM" + side : "UNKNOWN";
                case "UpperLeg": case "LowerLeg": case "Foot": return side.Length > 0 ? "LEG" + side : "UNKNOWN";
            }
            if (side.Length > 0)
            {
                if (baseName.StartsWith("Thumb") || baseName.StartsWith("Index") || baseName.StartsWith("Middle") || baseName.StartsWith("Ring") || baseName.StartsWith("Pinky")) return "ARM" + side;
                if (baseName.StartsWith("Toe")) return "LEG" + side;
                if (baseName.StartsWith("PT")) return "UNKNOWN";   // PT.L / PT.R pole helpers: their parent's region is not recoverable from the name alone; the coverage test lists them
            }
            return "UNKNOWN";
        }

        /// <summary>Region of a bone Transform: the name map, except that PT.* helpers and *_end bones take their parent's region (walking up until a mapped bone is found). Null = UNKNOWN.</summary>
        public static string Region(Transform bone)
        {
            for (var t = bone; t != null; t = t.parent)
            {
                string n = t.name;
                if (n.StartsWith("PT") || n.EndsWith("_end")) continue;
                return Region(n);
            }
            return "UNKNOWN";
        }

        /// <summary>Region of a triangle: the majority, over the 3 corners, of each corner's dominant-bone region (boneIndex0; Unity sorts weights descending). On a 3-way tie, corner 0. UNKNOWN corners count as their own region.</summary>
        public static string TriRegion(string r0, string r1, string r2)
        {
            if (r0 == r1 || r0 == r2) return r0;
            if (r1 == r2) return r1;
            return r0;
        }

        /// <summary>The verdict for one ray, in this order: the winner is a front face -> Ok; no front hit -> NoFront; either region UNKNOWN -> UnknownRegion (fail closed, before any gap test); frontT - winnerT > G(winner region) (strictly) -> GapOverG; regions differ -> OtherRegion; else Ok.</summary>
        public static Verdict Judge(bool winnerBack, float winnerT, string winnerRegion, bool frontFound, float frontT, string frontRegion)
        {
            if (!winnerBack) return Verdict.Ok;
            if (!frontFound) return Verdict.NoFront;
            if (winnerRegion == "UNKNOWN" || frontRegion == "UNKNOWN") return Verdict.UnknownRegion;
            if (frontT - winnerT > G(winnerRegion)) return Verdict.GapOverG;
            if (winnerRegion != frontRegion) return Verdict.OtherRegion;
            return Verdict.Ok;
        }

        /// <summary>Per-ray tracker of the nearest FRONT-facing hit (min t over front hits, with its triangle's region). Call Note for every intersection of the ray.</summary>
        public struct RayState
        {
            public bool Found;
            public float T;
            public string Region;

            public void Note(bool back, float t, string region)
            {
                if (back) return;
                if (!Found || t < T) { Found = true; T = t; Region = region; }
            }
        }

        /// <summary>Counts of the four clauses over many rays, with the largest front gap seen among the back-first rays that had one.</summary>
        public struct Tally
        {
            public int noFront, gapOver, otherRegion, unknown, backFirst;
            public float maxGap;
            Dictionary<string, int> gapByRegion;   // gap>G counts by "REGION G mm" (the G used), for the failure text

            public int Defects => noFront + gapOver + otherRegion + unknown;

            public void Add(Verdict v, bool winnerBack, float gap, string winnerRegion = null)
            {
                if (winnerBack) backFirst++;
                if (!float.IsNaN(gap) && gap > maxGap) maxGap = gap;
                switch (v)
                {
                    case Verdict.NoFront: noFront++; break;
                    case Verdict.GapOverG:
                        gapOver++;
                        gapByRegion ??= new Dictionary<string, int>();
                        string key = $"{winnerRegion ?? "?"} {G(winnerRegion ?? "?") * 1000f:F0} mm";
                        gapByRegion[key] = gapByRegion.TryGetValue(key, out int n) ? n + 1 : 1;
                        break;
                    case Verdict.OtherRegion: otherRegion++; break;
                    case Verdict.UnknownRegion: unknown++; break;
                }
            }

            public void Merge(Tally o)
            {
                noFront += o.noFront; gapOver += o.gapOver; otherRegion += o.otherRegion; unknown += o.unknown; backFirst += o.backFirst;
                if (o.maxGap > maxGap) maxGap = o.maxGap;
                if (o.gapByRegion != null)
                {
                    gapByRegion ??= new Dictionary<string, int>();
                    foreach (var kv in o.gapByRegion) gapByRegion[kv.Key] = gapByRegion.TryGetValue(kv.Key, out int n) ? n + kv.Value : kv.Value;
                }
            }

            public string Clauses => $"no front {noFront}, gap>G {gapOver}{(gapByRegion != null && gapByRegion.Count > 0 ? " [" + string.Join(", ", gapByRegion.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} x{kv.Value}")) + "]" : "")}, other region {otherRegion}, unknown {unknown}";

            public override string ToString() => $"BAR 1 defects {Defects} [{Clauses}; max gap {maxGap * 1000f:F1} mm; {GText}]";
        }

        /// <summary>The CSV columns appended by the asserting tests: defects, noFront, gapOverG, otherRegion, unknown, maxFrontGap_m.</summary>
        public const string CsvColumns = "bar1Defects,noFront,gapOverG,otherRegion,unknown,maxFrontGap_m";

        public static string CsvValues(Tally t) => $"{t.Defects},{t.noFront},{t.gapOver},{t.otherRegion},{t.unknown},{t.maxGap:F4}";
    }
}
