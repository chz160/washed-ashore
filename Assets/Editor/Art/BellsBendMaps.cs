using System.Linq;
using UnityEditor;
using UnityEngine;
using WashedAshore.Level;

// Technical-artist adapter over level-2's BellsBendLevelMaps (zones on a 5 m grid; roads and shore
// distance on a 2 m grid) plus the built tiles' slope. The only file that names level-2's API.
public class BellsBendMaps
{
    // Road ids from BellsBendLevelMaps.Road().
    public const int OldHickory = BellsBendLevelMaps.RoadOldHickory, PecanValley = BellsBendLevelMaps.RoadPecanValley,
        TidwellHollow = BellsBendLevelMaps.RoadTidwellHollow, CleecesFerry = BellsBendLevelMaps.RoadCleecesFerry, CleecesTrack = BellsBendLevelMaps.RoadCleecesTrack;
    public static bool IsGravel(int road) => road == TidwellHollow || road == CleecesTrack;

    // The lookout tower's access track (road id 6) is paint only: Road() and RoadWeight() hide it, so trees, details and
    // the road splat never see it. Weights() paints it from TowerTrackRut().
    public const int TowerTrack = BellsBendLevelMaps.RoadTowerTrack;
    const float RutOffset = 0.9f, RutHalf = 0.3f, RutSoft = 0.4f, BaseKeepClear = 1.5f;

    readonly BellsBendLevelMaps src;
    readonly Terrain[] tiles;
    float[] roadDist; // metres from the nearest carriageway cell (RoadWeight >= 0.99), on the level's road grid
    int gw, gh;
    float cell;
    Vector2 origin;

    BellsBendMaps(BellsBendLevelMaps src, Terrain[] tiles) { this.src = src; this.tiles = tiles; }

    public static BellsBendMaps Load()
    {
        var m = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
        if (!m) throw new System.IO.FileNotFoundException("Run BellsBendLevel.BuildAll() first: level maps missing", BellsBendLevelMaps.AssetPath);
        var maps = new BellsBendMaps(m, BellsBendGround.Tiles());
        maps.BuildRoadDistance();
        return maps;
    }

    public byte Zone(Vector3 p) => src.Zone(p);
    public bool IsCliff(Vector3 p) => src.IsCliff(p);
    public int Road(Vector3 p) { int id = src.Road(p); return id == TowerTrack ? 0 : id; }
    public float RoadWeight(Vector3 p) => src.Road(p) == TowerTrack ? 0f : src.RoadWeight(p);
    public float ShoreDistance(Vector3 p) => src.ShoreDistance(p);

    /// <summary>Road id at p, or of the nearest road cell within 6 m (0 if none).</summary>
    public int NearestRoad(Vector3 p)
    {
        int id = Road(p);
        if (id != 0) return id;
        for (float r = 2f; r <= 6f; r += 2f)
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                id = Road(p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                if (id != 0) return id;
            }
        return 0;
    }

    /// <summary>Two-rut mask (0..1) of the tower's access track at p, and the grass crown between the ruts; both 0 off the track.</summary>
    public float TowerTrackRut(Vector3 p, out float crown)
    {
        crown = 0f;
        var track = src.towerTrack;
        if (track == null || track.Length < 2) return 0f;
        bool near = src.Road(p) == TowerTrack;
        for (int k = 0; k < 4 && !near; k++) near = src.Road(p + new Vector3(k < 2 ? (k == 0 ? cell : -cell) : 0f, 0f, k >= 2 ? (k == 2 ? cell : -cell) : 0f)) == TowerTrack;
        if (!near || src.RoadWeight(p) >= 0.5f) return 0f; // a real road wins where the track joins it
        var q = new Vector2(p.x, p.z);
        float best = float.MaxValue;
        for (int i = 1; i < track.Length; i++)
        {
            Vector2 a = track[i - 1], ab = track[i] - a;
            float u = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            best = Mathf.Min(best, (a + ab * u - q).sqrMagnitude);
        }
        float d = Mathf.Sqrt(best);
        crown = 1f - Mathf.Clamp01((d - 0.3f) / 0.4f);
        return 1f - Mathf.Clamp01((Mathf.Abs(d - RutOffset) - RutHalf) / RutSoft);
    }

    /// <summary>Inside the tower's tree-free ring, clear of the base (the footings and the foot of the stairs stay bare).</summary>
    public bool InTowerRing(Vector3 p, out bool atBase)
    {
        atBase = false;
        if (src.towerClearRadius <= 0f) return false;
        float d = Vector2.Distance(new Vector2(p.x, p.z), src.towerCentre);
        atBase = d < src.towerBaseRadius + BaseKeepClear;
        return d <= src.towerClearRadius;
    }

    /// <summary>Approximate distance (m) from the road centreline: carriageway edge distance + half a carriageway.</summary>
    public float RoadCentreDistance(Vector3 p)
    {
        int x = Mathf.FloorToInt((p.x - origin.x) / cell), z = Mathf.FloorToInt((p.z - origin.y) / cell);
        if (x < 0 || z < 0 || x >= gw || z >= gh) return float.MaxValue;
        return roadDist[z * gw + x] + 3.5f;
    }

    /// <summary>Built terrain steepness (degrees) at a world point, from whichever tile holds it.</summary>
    public float GameSlope(Vector3 p)
    {
        foreach (var t in tiles)
        {
            var o = t.transform.position;
            var s = t.terrainData.size;
            float u = (p.x - o.x) / s.x, v = (p.z - o.z) / s.z;
            if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) return t.terrainData.GetSteepness(u, v);
        }
        return 0f;
    }

    // Two-pass chamfer distance transform (1, sqrt 2) over the road grid: O(cells), run once per build.
    void BuildRoadDistance()
    {
        origin = src.originXZ;
        cell = src.roadCell;
        gw = src.roadW; gh = src.roadH;
        roadDist = new float[gw * gh];
        const float Big = 1e6f;
        float d1 = cell, d2 = cell * 1.41421356f;
        for (int z = 0; z < gh; z++)
            for (int x = 0; x < gw; x++)
            {
                var p = new Vector3(origin.x + (x + 0.5f) * cell, 0f, origin.y + (z + 0.5f) * cell);
                roadDist[z * gw + x] = RoadWeight(p) >= 0.99f ? 0f : Big; // the adapter's own: the hidden track can never be a carriageway
            }
        for (int z = 0; z < gh; z++)
            for (int x = 0; x < gw; x++)
            {
                float v = roadDist[z * gw + x];
                if (x > 0) v = Mathf.Min(v, roadDist[z * gw + x - 1] + d1);
                if (z > 0)
                {
                    v = Mathf.Min(v, roadDist[(z - 1) * gw + x] + d1);
                    if (x > 0) v = Mathf.Min(v, roadDist[(z - 1) * gw + x - 1] + d2);
                    if (x < gw - 1) v = Mathf.Min(v, roadDist[(z - 1) * gw + x + 1] + d2);
                }
                roadDist[z * gw + x] = v;
            }
        for (int z = gh - 1; z >= 0; z--)
            for (int x = gw - 1; x >= 0; x--)
            {
                float v = roadDist[z * gw + x];
                if (x < gw - 1) v = Mathf.Min(v, roadDist[z * gw + x + 1] + d1);
                if (z < gh - 1)
                {
                    v = Mathf.Min(v, roadDist[(z + 1) * gw + x] + d1);
                    if (x < gw - 1) v = Mathf.Min(v, roadDist[(z + 1) * gw + x + 1] + d2);
                    if (x > 0) v = Mathf.Min(v, roadDist[(z + 1) * gw + x - 1] + d2);
                }
                roadDist[z * gw + x] = v;
            }
    }
}
