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
    public int Road(Vector3 p) => src.Road(p);
    public float RoadWeight(Vector3 p) => src.RoadWeight(p);
    public float ShoreDistance(Vector3 p) => src.ShoreDistance(p);

    /// <summary>Road id at p, or of the nearest road cell within 6 m (0 if none).</summary>
    public int NearestRoad(Vector3 p)
    {
        int id = src.Road(p);
        if (id != 0) return id;
        for (float r = 2f; r <= 6f; r += 2f)
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI / 4f;
                id = src.Road(p + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                if (id != 0) return id;
            }
        return 0;
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
                roadDist[z * gw + x] = src.RoadWeight(p) >= 0.99f ? 0f : Big;
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
