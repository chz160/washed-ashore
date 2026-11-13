using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WashedAshore.Perf
{
    /// <summary>
    /// One trial setting set for a FrustumProbe window. Unset fields leave the authored value in place, so an empty
    /// variant is the A (live settings) window. Text form, for -fcVariant: "id:key=value,key=value", keys:
    ///   far=m  farCab=m  cull.LayerName=m  spherical=on  detail=m  shadows=off  fishOff=on (Fish root inactive for the window)
    ///   shadowDist=m  grdOcc=on|off  smallMesh=percent      (these three write the URP asset: player only)
    /// </summary>
    public sealed class TrialVariant
    {
        public const string AuthoredId = "A";
        public string id = AuthoredId;
        public float far = -1f, farCab = -1f, detail = -1f, shadowDist = -1f, smallMesh = -1f;
        public bool spherical, shadowsOff, fishOff;
        /// <summary>-1 leaves GRD occlusion culling as authored, 0 off, 1 on.</summary>
        public int grdOcclusion = -1;
        public readonly SortedDictionary<string, float> layerCull = new SortedDictionary<string, float>();

        public static TrialVariant Authored() => new TrialVariant();

        public bool IsAuthored => far < 0f && farCab < 0f && detail < 0f && shadowDist < 0f && smallMesh < 0f && !spherical && !shadowsOff && !fishOff && grdOcclusion < 0 && layerCull.Count == 0;

        /// <summary>True when applying it writes the render pipeline asset (refused in the Editor, where the write would persist).</summary>
        public bool WritesAsset => shadowDist >= 0f || smallMesh >= 0f || grdOcclusion >= 0;

        /// <summary>The far plane this variant asks for at a spot (cab or not); -1 for none.</summary>
        public float FarFor(bool cab) => cab ? (farCab > 0f ? farCab : far) : far;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool TryParse(string text, out TrialVariant v, out string error)
        {
            v = null; error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "empty variant"; return false; }
            int colon = text.IndexOf(':');
            string id = (colon < 0 ? text : text.Substring(0, colon)).Trim();
            if (id.Length == 0) { error = $"'{text}': variant needs an id before ':'"; return false; }
            if (id == AuthoredId) { error = $"'{text}': id '{AuthoredId}' is reserved for the authored window"; return false; }
            var r = new TrialVariant { id = id };
            string body = colon < 0 ? "" : text.Substring(colon + 1);
            foreach (var raw in body.Split(','))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                if (eq <= 0) { error = $"'{part}': expected key=value"; return false; }
                string key = part.Substring(0, eq).Trim(), val = part.Substring(eq + 1).Trim();
                bool Num(out float f) => float.TryParse(val, NumberStyles.Float, Inv, out f) && f >= 0f && !float.IsInfinity(f) && !float.IsNaN(f);
                float n;
                if (key.StartsWith("cull."))
                {
                    string layer = key.Substring(5).Trim();
                    if (layer.Length == 0 || !Num(out n)) { error = $"'{part}': cull.Layer needs a distance >= 0"; return false; }
                    r.layerCull[layer] = n;
                    continue;
                }
                switch (key)
                {
                    case "far": if (!Num(out n) || n <= 0f) { error = $"'{part}': far must be > 0"; return false; } r.far = n; break;
                    case "farCab": if (!Num(out n) || n <= 0f) { error = $"'{part}': farCab must be > 0"; return false; } r.farCab = n; break;
                    case "detail": if (!Num(out n)) { error = $"'{part}': detail must be >= 0"; return false; } r.detail = n; break;
                    case "shadowDist": if (!Num(out n)) { error = $"'{part}': shadowDist must be >= 0"; return false; } r.shadowDist = n; break;
                    case "smallMesh": if (!Num(out n) || n > 20f) { error = $"'{part}': smallMesh is a screen percentage 0-20"; return false; } r.smallMesh = n; break;
                    case "spherical": if (val != "on") { error = $"'{part}': spherical=on"; return false; } r.spherical = true; break;
                    case "shadows": if (val != "off") { error = $"'{part}': shadows=off"; return false; } r.shadowsOff = true; break;
                    case "fishOff": if (val != "on") { error = $"'{part}': fishOff=on"; return false; } r.fishOff = true; break;
                    case "grdOcc":
                        if (val == "on") r.grdOcclusion = 1; else if (val == "off") r.grdOcclusion = 0;
                        else { error = $"'{part}': grdOcc=on|off"; return false; }
                        break;
                    default: error = $"'{part}': unknown key '{key}'"; return false;
                }
            }
            v = r;
            return true;
        }

        /// <summary>The window order for one spot run: A V1 A V2 ... Vk A (a fresh A between every variant).</summary>
        public static List<TrialVariant> Windows(IReadOnlyList<TrialVariant> variants)
        {
            var w = new List<TrialVariant> { Authored() };
            foreach (var v in variants) { w.Add(v); w.Add(Authored()); }
            return w;
        }

        static string F(float f) => FrustumMath.F(f);

        public string Json() =>
            $"{{\"id\":\"{FrustumMath.Esc(id)}\",\"far\":{F(far)},\"far_cab\":{F(farCab)},\"layer_cull\":{{{string.Join(",", layerCull.Select(kv => $"\"{FrustumMath.Esc(kv.Key)}\":{F(kv.Value)}"))}}}," +
            $"\"spherical\":{(spherical ? "true" : "false")},\"detail\":{F(detail)},\"shadows_off\":{(shadowsOff ? "true" : "false")},\"shadow_distance\":{F(shadowDist)}," +
            $"\"grd_occlusion\":{grdOcclusion},\"small_mesh_percent\":{F(smallMesh)},\"fish_off\":{(fishOff ? "true" : "false")}" + "}";
    }
}
