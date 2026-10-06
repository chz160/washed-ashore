using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WashedAshore.World;

// Level-designer: loads tools/terrain outputs (manifest.json, map_vectors.json) for the Bells Bend build.
public static class BellsBendData
{
    public const string MapConfigPath = "Assets/World/MapConfig.asset";
    public const string WorldRoot = "Assets/World/BellsBend";
    public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
    public static string PocDir => Path.Combine(ProjectRoot, "_bmad-output", "poc");

    public class Tile { public string file, demFile; public int ix, iz; public Vector3 position; }
    public class Road { public string name, highway; public bool required; public List<Vector2> points; }
    public class Landmark { public string id, name; public Vector2 xz; public bool inside; }

    public class Manifest
    {
        public string dir;
        public float tileSize, sampleSpacing, terrainBaseY, terrainHeight, waterLevelY, northLineZData;
        public int res, tilesX, tilesZ;
        public Vector2 gridOrigin;
        public List<Tile> tiles = new List<Tile>();
    }

    public class Vectors
    {
        public List<Vector2> polygon, innerBank;
        public Vector2 westCrossing, eastCrossing;
        public double originE, originN, hs;
        public Vector2 ToGame(double lat, double lon) => BellsBendTiles.LatLonToGame(lat, lon, originE, originN, hs);
        public List<Road> roads = new List<Road>();
        public List<Landmark> landmarks = new List<Landmark>();
    }

    public static MapConfig LoadConfig()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<MapConfig>(MapConfigPath);
        if (!cfg) throw new FileNotFoundException("MapConfig missing", MapConfigPath);
        return cfg;
    }

    public static Manifest LoadManifest(MapConfig cfg)
    {
        var path = Path.Combine(ProjectRoot, cfg.manifestPath);
        if (!File.Exists(path)) throw new FileNotFoundException("Run py tools/terrain/build_terrain.py first", path);
        var j = (Dictionary<string, object>)Json.Parse(File.ReadAllText(path));
        var m = new Manifest
        {
            dir = Path.GetDirectoryName(path),
            tileSize = F(j["tileSize"]), sampleSpacing = F(j["sampleSpacing"]), res = (int)F(j["heightmapResolution"]),
            tilesX = (int)F(j["tilesX"]), tilesZ = (int)F(j["tilesZ"]), gridOrigin = V2(j["gridOrigin"]),
            terrainBaseY = F(j["terrainBaseY"]), terrainHeight = F(j["terrainHeight"]),
            waterLevelY = F(j["WaterLevelY"]), northLineZData = F(j["northLineZData"]),
        };
        foreach (Dictionary<string, object> t in (List<object>)j["tiles"])
        {
            var p = (List<object>)t["position"];
            m.tiles.Add(new Tile { file = (string)t["file"], demFile = t.ContainsKey("demFile") ? (string)t["demFile"] : null, ix = (int)F(t["ix"]), iz = (int)F(t["iz"]), position = new Vector3(F(p[0]), F(p[1]), F(p[2])) });
        }
        return m;
    }

    public static Vectors LoadVectors(MapConfig cfg)
    {
        var path = Path.Combine(ProjectRoot, Path.GetDirectoryName(cfg.manifestPath), "map_vectors.json");
        var j = (Dictionary<string, object>)Json.Parse(File.ReadAllText(path));
        var nl = (Dictionary<string, object>)j["northLine"];
        var fr = (Dictionary<string, object>)j["frame"];
        var v = new Vectors
        {
            polygon = Pts(j["playablePolygon"]), innerBank = Pts(j["innerBank"]),
            westCrossing = V2(nl["westCrossing"]), eastCrossing = V2(nl["eastCrossing"]),
            originE = (double)fr["originE"], originN = (double)fr["originN"], hs = (double)fr["horizontalScale"],
        };
        foreach (Dictionary<string, object> r in (List<object>)j["roads"])
            v.roads.Add(new Road { name = r["name"] as string, highway = r["highway"] as string, required = r["required"] is bool b && b, points = Pts(r["points"]) });
        foreach (Dictionary<string, object> l in (List<object>)j["landmarks"])
            v.landmarks.Add(new Landmark { id = (string)l["id"], name = (string)l["name"], xz = new Vector2(F(l["x"]), F(l["z"])), inside = l["insidePolygon"] is bool b && b });
        return v;
    }

    /// <summary>All tiles' heights stitched into one world grid [row = z, col = x] in Unity Y.</summary>
    public static float[,] LoadRawGrid(Manifest m) => LoadGrid(m, false);

    /// <summary>Unshaped real DEM (metres, before clamp/bank/lake) from tools' dem_x*_z*.raw (u16 centimetres).</summary>
    public static float[,] LoadDemGrid(Manifest m) => LoadGrid(m, true);

    static float[,] LoadGrid(Manifest m, bool dem)
    {
        int n = m.res - 1, nx = m.tilesX * n + 1, nz = m.tilesZ * n + 1;
        var g = new float[nz, nx];
        var buf = new byte[m.res * m.res * 2];
        foreach (var t in m.tiles)
        {
            using (var fs = File.OpenRead(Path.Combine(m.dir, dem ? t.demFile : t.file))) fs.Read(buf, 0, buf.Length);
            for (int r = 0; r < m.res; r++)
                for (int c = 0; c < m.res; c++)
                {
                    int i = (r * m.res + c) * 2;
                    int u = buf[i] | buf[i + 1] << 8;
                    g[t.iz * n + r, t.ix * n + c] = dem ? u / 100f : u / 65535f * m.terrainHeight + m.terrainBaseY;
                }
        }
        return g;
    }

    public static float F(object o) => Convert.ToSingle(o, CultureInfo.InvariantCulture);
    static Vector2 V2(object o) { var l = (List<object>)o; return new Vector2(F(l[0]), F(l[1])); }
    static List<Vector2> Pts(object o) => ((List<object>)o).Select(V2).ToList();

    // --- geometry helpers shared by the level scripts ---

    public static bool InPolygon(Vector2 p, List<Vector2> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }

    public static float DistToPolyline(Vector2 p, List<Vector2> line, out int seg, out float t)
    {
        float best = float.MaxValue; seg = 0; t = 0f;
        for (int i = 1; i < line.Count; i++)
        {
            Vector2 a = line[i - 1], ab = line[i] - a;
            float u = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            float d = (a + ab * u - p).sqrMagnitude;
            if (d < best) { best = d; seg = i - 1; t = u; }
        }
        return Mathf.Sqrt(best);
    }

    public static string WriteText(string fileName, string content)
    {
        Directory.CreateDirectory(PocDir);
        var path = Path.Combine(PocDir, fileName);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>Minimal JSON reader: objects → Dictionary, arrays → List, numbers → double.</summary>
    public static class Json
    {
        public static object Parse(string s) { int i = 0; return Value(s, ref i); }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                for (Ws(s, ref i); s[i] != '}'; Ws(s, ref i))
                {
                    var k = Str(s, ref i); Ws(s, ref i); i++; // ':'
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') i++;
                }
                i++; return d;
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                for (Ws(s, ref i); s[i] != ']'; Ws(s, ref i))
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') i++;
                }
                i++; return l;
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    switch (s[i])
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                        default: sb.Append(s[i]); break;
                    }
                }
                else sb.Append(s[i]);
                i++;
            }
            i++; return sb.ToString();
        }
    }
}
