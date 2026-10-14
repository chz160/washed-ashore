using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.Gameplay;
using WashedAshore.Level;

namespace WashedAshore.Perf
{
    /// <summary>
    /// The 1 km ridge stills of FarViewProbe (tower-td-note.md section 13): where to stand, and how strongly the summit
    /// ridge shows against the sky. Viewpoints are either given outright (-fvRidgeEye x z, game-director's spots) or
    /// chosen from open ground by map zone. The measure compares a still with its twin taken with the summit's terrain
    /// tile hidden: what changes between them is the ridge.
    /// </summary>
    static class FarViewRidge
    {
        public const float MinBearingGap = 60f, DryMargin = 1f, TreeFreeRange = 30f, TreeFreeLateral = 6f;
        public const float RingMin = 900f, RingMax = 1100f, RingStep = 50f, BearingStep = 5f;   // candidate viewpoints round the summit
        public const int ChangedOver = 3;   // a pixel counts as changed above this many 1/255 steps (the capture noise floor is below it)
        public const int StrongOver = 8;    // the second, stricter count
        static readonly string TreeNote = $"tree within {TreeFreeRange:0} m of the sight line";

        public struct View
        {
            public Vector3 eye;
            public float bearing, distance, shore;
            public string zone, chosenBy, notes;
        }

        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        static string ZoneName(byte zone) => zone < BellsBendLevelMaps.ZoneNames.Length ? BellsBendLevelMaps.ZoneNames[zone] : "unknown";

        /// <summary>
        /// The viewpoints to use. Explicit ones are taken as given (with what stands in the way noted, never dropped);
        /// with none given, two are chosen 900-1,100 m from the summit: open ground nearest due east, then the candidate
        /// nearest the shore at least MinBearingGap away. <paramref name="diagnostics"/> is JSON members, no braces.
        /// </summary>
        public static List<View> Choose(Vector3 summit, Vector3 aim, float eyeHeight, float water, int solidMask, IList<Vector2> given, out string diagnostics)
        {
            var maps = FishPopulation.Active ? FishPopulation.Active.LevelMaps : null;
            // The zone map is only trusted if it knows the summit; otherwise the tree-free rule and the Linecast decide alone.
            bool zonesKnown = maps && maps.Zone(summit) != BellsBendLevelMaps.ZoneOutside;
            var views = new List<View>();

            View At(Vector3 p, float y, string chosenBy)
            {
                var flat = new Vector2(p.x - summit.x, p.z - summit.z);
                return new View
                {
                    eye = new Vector3(p.x, y + eyeHeight, p.z), distance = flat.magnitude,
                    bearing = Mathf.Repeat(Mathf.Atan2(flat.x, flat.y) * Mathf.Rad2Deg, 360f),
                    zone = maps ? ZoneName(maps.Zone(p)) : "unknown", shore = maps ? maps.ShoreDistance(p) : 0f, chosenBy = chosenBy, notes = "",
                };
            }

            if (given != null && given.Count > 0)
            {
                foreach (var g in given)
                {
                    var p = new Vector3(g.x, 0f, g.y);
                    var tile = TerrainQuery.TileAtOrNearest(p);
                    float y = tile ? tile.SampleHeight(p) + tile.transform.position.y : 0f;
                    var v = At(p, y, "given");
                    var notes = new List<string>();
                    if (y < water + DryMargin) notes.Add("under or at the water level");
                    if (TreeNearSightLine(v.eye, aim)) notes.Add(TreeNote);
                    if (Physics.Linecast(v.eye, aim, out var hit, solidMask, QueryTriggerInteraction.Ignore)) notes.Add("sight line blocked by " + hit.collider.name);
                    v.notes = string.Join("; ", notes);
                    views.Add(v);
                }
                diagnostics = $"\"viewpoints\":\"given\",\"zone_map_used\":{(zonesKnown ? "true" : "false")}";
                return views;
            }

            var candidates = new List<View>(); var bank = new List<bool>();
            var reasons = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            void Reject(string why) { reasons.TryGetValue(why, out int n); reasons[why] = n + 1; }
            for (float ring = RingMin; ring <= RingMax; ring += RingStep)
                for (float a = 0f; a < 360f; a += BearingStep)
                {
                    var p = summit + Quaternion.Euler(0f, a, 0f) * Vector3.forward * ring;
                    var tile = TerrainQuery.TileAt(p);
                    if (!tile) { Reject("no terrain"); continue; }
                    float y = tile.SampleHeight(p) + tile.transform.position.y;
                    if (y < water + DryMargin) { Reject("water"); continue; }
                    byte zone = maps ? maps.Zone(p) : (byte)255;
                    bool open = zone == BellsBendLevelMaps.ZoneRoad || zone == BellsBendLevelMaps.ZoneFormerFields || zone == BellsBendLevelMaps.ZoneClearing;
                    if (zonesKnown && !open && zone != BellsBendLevelMaps.ZoneBank) { Reject("zone " + ZoneName(zone)); continue; }
                    var v = At(p, y, "zone rule");
                    if (TreeNearSightLine(v.eye, aim)) { Reject(TreeNote); continue; }
                    if (Physics.Linecast(v.eye, aim, out var hit, solidMask, QueryTriggerInteraction.Ignore)) { Reject("blocked by " + hit.collider.name); continue; }
                    candidates.Add(v); bank.Add(zone == BellsBendLevelMaps.ZoneBank);
                }
            // View 1: open ground (not bank) nearest to due east of the summit, looking back west.
            int east = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (bank[i]) continue;
                if (east < 0 || Mathf.Abs(Mathf.DeltaAngle(candidates[i].bearing, 90f)) < Mathf.Abs(Mathf.DeltaAngle(candidates[east].bearing, 90f))) east = i;
            }
            if (east >= 0) views.Add(candidates[east]);
            // View 2: the candidate closest to the shore, at least MinBearingGap away from view 1.
            int shoreSide = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (east >= 0 && Mathf.Abs(Mathf.DeltaAngle(candidates[i].bearing, candidates[east].bearing)) < MinBearingGap) continue;
                if (shoreSide < 0 || Mathf.Abs(candidates[i].shore) < Mathf.Abs(candidates[shoreSide].shore)) shoreSide = i;
            }
            if (shoreSide >= 0) views.Add(candidates[shoreSide]);
            diagnostics = $"\"viewpoints\":\"zone rule\",\"zone_map_used\":{(zonesKnown ? "true" : "false")},\"candidates_clear\":{candidates.Count}," +
                          $"\"rejected\":[{string.Join(",", reasons.Select(kv => $"[\"{kv.Key}\",{kv.Value}]"))}]";
            return views;
        }

        /// <summary>True when a terrain tree stands within TreeFreeLateral of the first TreeFreeRange metres of the eye-to-aim line.</summary>
        static bool TreeNearSightLine(Vector3 eye, Vector3 aim)
        {
            var dir = new Vector2(aim.x - eye.x, aim.z - eye.z).normalized;
            foreach (var terrain in Terrain.activeTerrains)
            {
                var data = terrain.terrainData; var origin = terrain.transform.position; var size = data.size;
                if (eye.x < origin.x - TreeFreeRange || eye.x > origin.x + size.x + TreeFreeRange
                    || eye.z < origin.z - TreeFreeRange || eye.z > origin.z + size.z + TreeFreeRange) continue;
                foreach (var tree in data.treeInstances)
                {
                    var rel = new Vector2(origin.x + tree.position.x * size.x - eye.x, origin.z + tree.position.z * size.z - eye.z);
                    float along = Vector2.Dot(rel, dir);
                    if (along < -TreeFreeLateral || along > TreeFreeRange) continue;
                    if ((rel - dir * along).magnitude < TreeFreeLateral) return true;
                }
            }
            return false;
        }

        static int Diff(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        static float Luminance(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        static string Stats(List<float> values)
        {
            if (values.Count == 0) return "null";
            values.Sort();
            return $"{{\"median\":{F(values[values.Count / 2])},\"min\":{F(values[0])},\"max\":{F(values[values.Count - 1])}" + "}";
        }

        /// <summary>
        /// How the ridge shows round the tower's footprint. Window: columns within 3 H of the footprint's column, rows from
        /// H/2 below the footprint to H above it, H being the tower's projected height in pixels. A pixel is "changed" when
        /// still and twin differ by more than ChangedOver/255. Pixel rows count from the bottom of the frame.
        /// Returns a JSON object: the window, changed counts and column coverage, the edge step across the silhouette (the
        /// topmost changed pixel of each column against the pixel above it, in the still), and the footprint and sky colours.
        /// </summary>
        public static string RidgeLine(Texture2D still, Texture2D twin, Vector2 footprint, float towerPixels)
        {
            int w = still.width, h = still.height;
            if (twin.width != w || twin.height != h) return "{\"error\":\"size differs\"}";
            if (towerPixels < 1f) return "{\"error\":\"tower is under one pixel tall\"}";
            var a = still.GetPixels32(); var b = twin.GetPixels32();
            int fx = Mathf.RoundToInt(footprint.x), fy = Mathf.RoundToInt(footprint.y);
            int x0 = Mathf.Max(0, Mathf.RoundToInt(fx - 3f * towerPixels)), x1 = Mathf.Min(w - 1, Mathf.RoundToInt(fx + 3f * towerPixels));
            int y0 = Mathf.Max(0, Mathf.RoundToInt(fy - towerPixels * 0.5f)), y1 = Mathf.Min(h - 1, Mathf.RoundToInt(fy + towerPixels));
            if (x0 > x1 || y0 > y1) return "{\"error\":\"footprint is outside the frame\"}";

            int over3 = 0, over8 = 0, columnsChanged = 0, clipped = 0;
            var lumStep = new List<float>(); var channelStep = new List<float>();
            for (int x = x0; x <= x1; x++)
            {
                int top = -1;
                for (int y = y0; y <= y1; y++)
                {
                    int d = Diff(a[y * w + x], b[y * w + x]);
                    if (d > ChangedOver) { over3++; top = y; }
                    if (d > StrongOver) over8++;
                }
                if (top < 0) continue;
                columnsChanged++;
                // A change reaching the window's top row is the window's edge, not the ridge's: counted, not measured.
                if (top >= y1 || top + 1 >= h) { clipped++; continue; }
                // Ridge (the topmost changed pixel) against the sky just above it, in the still.
                Color32 below = a[top * w + x], above = a[(top + 1) * w + x];
                lumStep.Add(Luminance(below) - Luminance(above));
                channelStep.Add(Diff(below, above));
            }
            int columns = x1 - x0 + 1;
            string colours = "null";
            if (fx >= 0 && fx < w && fy >= 0 && fy < h)
            {
                int skyY = Mathf.Min(h - 1, Mathf.RoundToInt(fy + towerPixels));
                Color32 ridge = a[fy * w + fx], sky = a[skyY * w + fx];
                colours = $"{{\"ridge_rgb\":[{ridge.r},{ridge.g},{ridge.b}],\"sky_rgb\":[{sky.r},{sky.g},{sky.b}],\"sky_row\":{skyY}," +
                          $"\"difference_rgb\":[{ridge.r - sky.r},{ridge.g - sky.g},{ridge.b - sky.b}],\"difference_luminance\":{F(Luminance(ridge) - Luminance(sky))}" + "}";
            }
            return $"{{\"tower_height_px\":{F(towerPixels)},\"window_xyxy\":[{x0},{y0},{x1},{y1}],\"changed_threshold\":{ChangedOver},\"strong_threshold\":{StrongOver}," +
                   $"\"changed_px_over_3\":{over3},\"changed_px_over_8\":{over8},\"window_px\":{columns * (y1 - y0 + 1)}," +
                   $"\"columns\":{columns},\"columns_with_change\":{columnsChanged},\"column_fraction\":{F(columnsChanged / (double)columns)},\"clipped_columns\":{clipped}," +
                   $"\"edge_luminance_step_0_255\":{Stats(lumStep)},\"edge_largest_channel_step_0_255\":{Stats(channelStep)},\"footprint\":{colours}" + "}";
        }
    }
}
