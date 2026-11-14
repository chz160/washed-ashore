using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine.Rendering.Universal;

namespace WashedAshore.Perf
{
    /// <summary>
    /// One trial setting set for a FrustumProbe window. Unset fields leave the authored value in place, so an empty
    /// variant is the A (live settings) window. Text form, for -fcVariant: "id:key=value,key=value", keys:
    ///   far=m  farCab=m  cull.LayerName=m  spherical=on  detail=m  shadows=off  fishOff=on (Fish root inactive for the window)
    ///   shadowDist=m  grdOcc=on|off  smallMesh=percent      (these three write the URP asset: player only)
    ///   mipLimit=0-3 (QualitySettings.globalTextureMipmapLimit)  lodBias=x (QualitySettings.lodBias, &gt; 0 and &lt;= 10)
    ///   aa=off|fxaa|smaa|taa|stp|msaa2|msaa4|msaa8 (camera AA mode, URP asset MSAA and upscaling filter)
    ///   depthPrime=off|auto|forced (every UniversalRendererData of the URP asset)
    ///                          (these four write the URP asset, its renderer data or QualitySettings: player only)
    ///   post=on|off (camera renderPostProcessing). fxaa, smaa, taa and stp need post-processing; with post unset they turn
    ///   it on and the variant records post_implied:true, so the arm also carries the volume's post cost (compare it with a
    ///   post=on variant). Rejected combinations: depthPrime=auto|forced with aa=msaaN (URP turns priming off when MSAA &gt; 1,
    ///   UniversalRendererRenderGraph.IsDepthPrimingEnabledRenderGraph), and post=off with an aa mode that needs post.
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
        /// <summary>-1 leaves QualitySettings.globalTextureMipmapLimit as authored; 0-3 sets it.</summary>
        public int mipLimit = -1;
        /// <summary>-1 leaves QualitySettings.lodBias as authored.</summary>
        public float lodBias = -1f;
        /// <summary>null leaves anti-aliasing as authored; otherwise one of <see cref="AaModes"/>.</summary>
        public string aa;
        /// <summary>null leaves depth priming as authored; otherwise one of <see cref="DepthPrimeModes"/>.</summary>
        public string depthPrime;
        /// <summary>-1 leaves camera post-processing as authored (unless <see cref="aa"/> needs it), 0 off, 1 on.</summary>
        public int post = -1;

        public static readonly string[] AaModes = { "off", "fxaa", "smaa", "taa", "stp", "msaa2", "msaa4", "msaa8" };
        public static readonly string[] DepthPrimeModes = { "off", "auto", "forced" };

        public static TrialVariant Authored() => new TrialVariant();

        public bool IsAuthored => far < 0f && farCab < 0f && detail < 0f && shadowDist < 0f && smallMesh < 0f && !spherical && !shadowsOff && !fishOff && grdOcclusion < 0 && layerCull.Count == 0 &&
                                  mipLimit < 0 && lodBias < 0f && aa == null && depthPrime == null && post < 0;

        /// <summary>
        /// True when applying it writes the URP asset, its renderer data or QualitySettings (refused in the Editor, where the
        /// write would persist). Player only.
        /// </summary>
        public bool WritesAsset => shadowDist >= 0f || smallMesh >= 0f || grdOcclusion >= 0 || aa != null || depthPrime != null || mipLimit >= 0 || lodBias >= 0f;

        /// <summary>True when an aa mode needs camera post-processing and the variant leaves post unset, so applying it turns post on.</summary>
        public bool PostImplied => post < 0 && AaNeedsPost(aa);

        /// <summary>
        /// True when the in-frame authored twin still would need a texture re-upload (mipLimit), a renderer rebuild
        /// (depthPrime) or new render targets and temporal history (aa). The probe skips the twin for these variants.
        /// </summary>
        public bool HeavyTwin => mipLimit >= 0 || depthPrime != null || aa != null;

        /// <summary>True when the frozen stills must wait for temporal history (TAA, STP).</summary>
        public bool Temporal => aa == "taa" || aa == "stp";

        /// <summary>The URP asset MSAA sample count an aa mode asks for: 2, 4 or 8 for msaaN, otherwise 1.</summary>
        public static int MsaaFor(string aa) => aa == "msaa2" ? 2 : aa == "msaa4" ? 4 : aa == "msaa8" ? 8 : 1;

        /// <summary>The camera's post-process AA for an aa mode; stp, msaaN and off map to None (STP is the asset's upscaling filter).</summary>
        public static AntialiasingMode CameraAaFor(string aa) =>
            aa == "fxaa" ? AntialiasingMode.FastApproximateAntialiasing :
            aa == "smaa" ? AntialiasingMode.SubpixelMorphologicalAntiAliasing :
            aa == "taa" ? AntialiasingMode.TemporalAntiAliasing : AntialiasingMode.None;

        /// <summary>True for the aa modes that run in the post-processing pass (fxaa, smaa, taa, stp).</summary>
        public static bool AaNeedsPost(string aa) => aa == "fxaa" || aa == "smaa" || aa == "taa" || aa == "stp";

        /// <summary>The renderer's depth priming mode for a depthPrime value.</summary>
        public static DepthPrimingMode DepthPrimingFor(string mode) =>
            mode == "forced" ? DepthPrimingMode.Forced : mode == "auto" ? DepthPrimingMode.Auto : DepthPrimingMode.Disabled;

        /// <summary>A combination of keys that parses but cannot move its arm, or null. Used by TryParse and the probe's global flags.</summary>
        public static string CombinationError(TrialVariant v)
        {
            if ((v.depthPrime == "auto" || v.depthPrime == "forced") && MsaaFor(v.aa) > 1)
                return $"depthPrime={v.depthPrime} with aa={v.aa} cannot move: URP turns depth priming off when MSAA > 1";
            if (v.post == 0 && AaNeedsPost(v.aa))
                return $"aa={v.aa} needs post-processing, so post=off leaves it with no effect";
            return null;
        }

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
                    case "mipLimit":
                        if (!int.TryParse(val, NumberStyles.Integer, Inv, out int m) || m < 0 || m > 3) { error = $"'{part}': mipLimit is an integer 0-3"; return false; }
                        r.mipLimit = m;
                        break;
                    case "lodBias": if (!Num(out n) || n <= 0f || n > 10f) { error = $"'{part}': lodBias must be > 0 and <= 10"; return false; } r.lodBias = n; break;
                    case "aa":
                        if (!AaModes.Contains(val)) { error = $"'{part}': aa is one of {string.Join("|", AaModes)}"; return false; }
                        r.aa = val;
                        break;
                    case "depthPrime":
                        if (!DepthPrimeModes.Contains(val)) { error = $"'{part}': depthPrime=off|auto|forced"; return false; }
                        r.depthPrime = val;
                        break;
                    case "post":
                        if (val == "on") r.post = 1; else if (val == "off") r.post = 0;
                        else { error = $"'{part}': post=on|off"; return false; }
                        break;
                    default: error = $"'{part}': unknown key '{key}'"; return false;
                }
            }
            string combo = CombinationError(r);
            if (combo != null) { error = $"'{text}': {combo}"; return false; }
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
        static string Q(string s) => s == null ? "null" : "\"" + FrustumMath.Esc(s) + "\"";

        public string Json() =>
            $"{{\"id\":\"{FrustumMath.Esc(id)}\",\"far\":{F(far)},\"far_cab\":{F(farCab)},\"layer_cull\":{{{string.Join(",", layerCull.Select(kv => $"\"{FrustumMath.Esc(kv.Key)}\":{F(kv.Value)}"))}}}," +
            $"\"spherical\":{(spherical ? "true" : "false")},\"detail\":{F(detail)},\"shadows_off\":{(shadowsOff ? "true" : "false")},\"shadow_distance\":{F(shadowDist)}," +
            $"\"grd_occlusion\":{grdOcclusion},\"small_mesh_percent\":{F(smallMesh)},\"fish_off\":{(fishOff ? "true" : "false")}," +
            $"\"texture_mip_limit\":{mipLimit},\"lod_bias\":{F(lodBias)},\"aa\":{Q(aa)},\"depth_priming\":{Q(depthPrime)},\"post\":{post},\"post_implied\":{(PostImplied ? "true" : "false")}" + "}";
    }
}
