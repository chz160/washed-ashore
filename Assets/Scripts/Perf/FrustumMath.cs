using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace WashedAshore.Perf
{
    /// <summary>One saved camera pose for the #9/#10/#11 protocol: a ground point, a heading and the eye height above the ground.</summary>
    public struct SpotPose
    {
        public string name;
        public float x, z, yaw, pitch, heightAboveGround;
        public string source;

        public SpotPose(string name, float x, float z, float yaw, float pitch, float heightAboveGround, string source)
        {
            this.name = name; this.x = x; this.z = z; this.yaw = yaw; this.pitch = pitch; this.heightAboveGround = heightAboveGround; this.source = source;
        }
    }

    /// <summary>Frame-time and counter statistics, as one sorted sample set.</summary>
    public struct SampleSummary
    {
        public int count;
        public double median, p95, p99, max, mean;
    }

    /// <summary>
    /// Pure helpers for the frustum-culling harness (FrustumProbe), kept free of scene state so EditMode tests cover them.
    /// Percentiles use the same nearest-rank rule as BellsBendFpsWalk and FarViewProbe (index = round(q * (n - 1)) of the
    /// sorted samples), so a #9 figure compares directly with the R2, M30 and far-view figures.
    /// </summary>
    public static class FrustumMath
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Nearest-rank percentile of an ascending list; 0 for an empty list.</summary>
        public static double Percentile(IReadOnlyList<double> sorted, double q)
        {
            if (sorted == null || sorted.Count == 0) return 0;
            int i = (int)System.Math.Round(q * (sorted.Count - 1));
            return sorted[Mathf.Clamp(i, 0, sorted.Count - 1)];
        }

        public static SampleSummary Summarize(IEnumerable<double> samples)
        {
            var s = samples.OrderBy(v => v).ToList();
            return new SampleSummary
            {
                count = s.Count,
                median = Percentile(s, 0.5),
                p95 = Percentile(s, 0.95),
                p99 = Percentile(s, 0.99),
                max = s.Count > 0 ? s[s.Count - 1] : 0,
                mean = s.Count > 0 ? s.Average() : 0,
            };
        }

        /// <summary>JSON object (with braces) for a summary: count, median, p95, p99, max, mean.</summary>
        public static string Json(SampleSummary s) =>
            $"{{\"count\":{s.count},\"median\":{F(s.median)},\"p95\":{F(s.p95)},\"p99\":{F(s.p99)},\"max\":{F(s.max)},\"mean\":{F(s.mean)}" + "}";

        public static string F(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("0.###", Inv);
        public static string F(float v) => F((double)v);

        /// <summary>Escapes a string for a JSON value (quotes, backslashes, control characters).</summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var b = new System.Text.StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                else b.Append(c);
            }
            return b.ToString();
        }

        /// <summary>
        /// Parses a pose override "name=x,z,yaw,pitch,heightAboveGround" (world metres and degrees, invariant culture).
        /// Height may be left out (default <paramref name="defaultHeight"/>).
        /// </summary>
        public static bool TryParsePose(string text, float defaultHeight, out SpotPose pose, out string error)
        {
            pose = default; error = null;
            if (string.IsNullOrWhiteSpace(text)) { error = "empty pose"; return false; }
            int eq = text.IndexOf('=');
            if (eq <= 0) { error = $"'{text}': expected name=x,z,yaw,pitch[,height]"; return false; }
            string name = text.Substring(0, eq).Trim();
            var parts = text.Substring(eq + 1).Split(',');
            if (parts.Length < 4 || parts.Length > 5) { error = $"'{text}': expected 4 or 5 numbers after '='"; return false; }
            var v = new float[5];
            v[4] = defaultHeight;
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, Inv, out v[i]) || float.IsNaN(v[i]) || float.IsInfinity(v[i]))
                { error = $"'{text}': '{parts[i]}' is not a number"; return false; }
            if (v[4] <= 0f) { error = $"'{text}': height above ground must be positive"; return false; }
            pose = new SpotPose(name, v[0], v[1], v[2], v[3], v[4], "command line -fcPose");
            return true;
        }

        /// <summary>Parses a per-layer cull distance "LayerName=metres" (metres &gt;= 0; 0 means no layer cull).</summary>
        public static bool TryParseLayerCull(string text, out string layer, out float metres, out string error)
        {
            layer = null; metres = 0f; error = null;
            int eq = text == null ? -1 : text.LastIndexOf('=');
            if (eq <= 0) { error = $"'{text}': expected LayerName=metres"; return false; }
            layer = text.Substring(0, eq).Trim();
            if (!float.TryParse(text.Substring(eq + 1).Trim(), NumberStyles.Float, Inv, out metres) || metres < 0f || float.IsInfinity(metres))
            { error = $"'{text}': distance must be a number >= 0"; return false; }
            return true;
        }

        /// <summary>
        /// Per-frame counts as JSON: frames, share of frames with at least <paramref name="atLeast"/>, max and mean. Used for the
        /// fish sample (share of frames with a fish drawn).
        /// </summary>
        public static string CountJson(IReadOnlyList<int> perFrame, int atLeast)
        {
            int n = perFrame?.Count ?? 0, hit = 0, max = 0;
            double sum = 0;
            for (int i = 0; i < n; i++) { int v = perFrame[i]; if (v >= atLeast) hit++; if (v > max) max = v; sum += v; }
            return $"{{\"frames\":{n},\"share_at_least_{atLeast}\":{F(n > 0 ? (double)hit / n : 0)},\"max\":{max},\"mean\":{F(n > 0 ? sum / n : 0)}" + "}";
        }

        /// <summary>Comma list of names; blanks dropped, order kept, duplicates removed.</summary>
        public static List<string> ParseList(string text) =>
            (text ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();

        /// <summary>Compass yaw (degrees, 0 = +Z north, 90 = +X east) from one ground point toward another, in [0, 360).</summary>
        public static float YawToward(float fromX, float fromZ, float toX, float toZ)
        {
            float y = Mathf.Atan2(toX - fromX, toZ - fromZ) * Mathf.Rad2Deg;
            return y < 0f ? y + 360f : y;
        }

        /// <summary>
        /// Screen-relative height of a LODGroup as Unity computes it for a perspective camera:
        /// size * 0.5 / (distance * tan(fov / 2)), scaled by QualitySettings.lodBias.
        /// </summary>
        public static float RelativeHeight(float worldSize, float distance, float fovDeg, float lodBias)
        {
            float halfTan = Mathf.Tan(Mathf.Deg2Rad * fovDeg * 0.5f);
            if (distance <= 1e-4f || halfTan <= 0f) return float.PositiveInfinity;
            return worldSize * 0.5f / (distance * halfTan) * lodBias;
        }

        /// <summary>
        /// The LOD level drawn at a relative height: the first level whose transition height it still meets, or -1 when
        /// it is below the last transition (the group is culled). Cross-fade bands are ignored.
        /// </summary>
        public static int LodIndex(float relativeHeight, IReadOnlyList<float> transitions)
        {
            for (int i = 0; i < transitions.Count; i++) if (relativeHeight >= transitions[i]) return i;
            return -1;
        }

        /// <summary>
        /// How far into linear fog a view depth is: 0 at or before fog start, 1 at or past fog end (fully fogged).
        /// URP's linear fog runs on view-space depth, not radial distance.
        /// </summary>
        public static float FogFraction(float viewDepth, float fogStart, float fogEnd)
        {
            if (fogEnd <= fogStart) return viewDepth >= fogEnd ? 1f : 0f;
            return Mathf.Clamp01((viewDepth - fogStart) / (fogEnd - fogStart));
        }

        /// <summary>Smallest view depth (distance along the camera's forward axis) of any corner of a box; negative when a corner is behind the eye.</summary>
        public static float MinViewDepth(Bounds b, Vector3 eye, Vector3 forward)
        {
            float min = float.PositiveInfinity;
            for (int i = 0; i < 8; i++) min = Mathf.Min(min, Vector3.Dot(Corner(b, i) - eye, forward));
            return min;
        }

        /// <summary>Corner i (0..7) of a box.</summary>
        public static Vector3 Corner(Bounds b, int i)
        {
            var mn = b.min; var mx = b.max;
            return new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
        }

        /// <summary>
        /// Whether a box is cut by a per-layer cull distance: planar culling (Unity's default) cuts it when its nearest
        /// view depth is past the distance; spherical culling when its nearest point is past it. 0 means no layer cull.
        /// </summary>
        public static bool LayerCulled(Bounds b, Vector3 eye, Vector3 forward, float cullDistance, bool spherical)
        {
            if (cullDistance <= 0f) return false;
            if (spherical) return b.SqrDistance(eye) > cullDistance * cullDistance;
            return MinViewDepth(b, eye, forward) > cullDistance;
        }
    }
}
