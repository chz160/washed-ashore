using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using WashedAshore.World;

// Level-designer: the lookout tower's view test (spec P3, director's note N10), with the definitions fixed in
// tower-qa-plan.md rev 1.1 before any result:
//   eye = cab floor + 1.65 m at the standing position at each cab side; quadrant = +/- 45 deg of N/E/S/W;
//   occluders = terrain (physics raycast, tower ignored) + real tree crowns (instance position, scaled height, crown radius);
//   shoreline visible in a quadrant (Noah's ruling, frozen): >= 50 m of contiguous bank length (10 m samples) with open water in
//     clear sight within N = MapConfig.shoreRampMeters (20 m) outside the bank, >= 3 of 4. The literal reading (the inner-bank
//     waterline itself in clear sight) is reported beside it and fails by geometry;
//   north barrier visible = >= 25 m contiguous fence top rail in clear sight;
//   eye above every crown within 60 m; a side is blocked if > 25% of its azimuths have a crown above the eye line within 150 m.
// The same probe from standing height at the base is the twin (the climb has to earn the view).
//   unity command eval --detach "return BellsBendTowerView.Test();"   -> TestResults/tower-level/p3-view.json
public static class BellsBendTowerView
{
    public const float CabFloorHeight = 26.289f;  // 86'3" above the pad top (tower-height-decision.md)
    public const float EyeHeight = 1.65f;         // CameraPivot above the player's feet
    public const float StandFromCentre = 0.55f;   // 7'0" cab: about 0.5 m inside each wall
    public const float ShoreRun = 50f, FenceRun = 25f, CrownNear = 60f, CrownFar = 150f, BlockedShare = 0.25f;
    const float CanopyCell = 2f, CrownBase = 0.35f; // crowns are solid from 35% of the tree's height up
    const float WaterStep = 2.5f;                   // open water is probed every 2.5 m out from the bank, to N

    class World
    {
        public BellsBendData.Manifest m; public float[,] ground; public float[] canopy; public int cw, ch;
        public BellsBendTowerSite.Site site; public Transform tower; public float water;
        public List<float> treeDist = new List<float>(); // every tree instance's distance from the tower centre

        public float Ground(float x, float z) => BellsBendTiles.Sample(ground, m, x, z);
        public float Canopy(float x, float z)
        {
            int c = Mathf.FloorToInt((x - m.gridOrigin.x) / CanopyCell), r = Mathf.FloorToInt((z - m.gridOrigin.y) / CanopyCell);
            return c < 0 || r < 0 || c >= cw || r >= ch ? float.NegativeInfinity : canopy[r * cw + c];
        }
    }

    static World Load()
    {
        var cfg = BellsBendData.LoadConfig();
        var w = new World { m = BellsBendData.LoadManifest(cfg), water = cfg.WaterLevelY };
        w.ground = BellsBendTiles.ReadBuilt(w.m);
        w.site = BellsBendTowerSite.Get(BellsBendData.LoadVectors(cfg), BellsBendData.LoadRawGrid(w.m), w.m);
        if (w.site == null) return w;
        var go = GameObject.Find(BellsBendTowerSite.RootName);
        w.tower = go ? go.transform : null;
        w.cw = Mathf.CeilToInt(w.m.tilesX * w.m.tileSize / CanopyCell); w.ch = Mathf.CeilToInt(w.m.tilesZ * w.m.tileSize / CanopyCell);
        w.canopy = Enumerable.Repeat(float.NegativeInfinity, w.cw * w.ch).ToArray();
        var root = GameObject.Find(BellsBendTiles.RootName);
        foreach (var t in root.GetComponentsInChildren<Terrain>(true))
        {
            var d = t.terrainData; var o = t.transform.position;
            var size = d.treePrototypes.Select(p => p.prefab ? Bounds(p.prefab) : Vector3.one).ToArray();
            foreach (var i in d.treeInstances)
            {
                float x = o.x + i.position.x * d.size.x, z = o.z + i.position.z * d.size.z;
                w.treeDist.Add(Vector2.Distance(new Vector2(x, z), w.site.centre));
                float top = w.Ground(x, z) + size[i.prototypeIndex].y * i.heightScale;
                float rad = Mathf.Max(size[i.prototypeIndex].x, size[i.prototypeIndex].z) * 0.5f * i.widthScale;
                int c0 = Mathf.Max(0, Mathf.FloorToInt((x - rad - w.m.gridOrigin.x) / CanopyCell)), c1 = Mathf.Min(w.cw - 1, Mathf.FloorToInt((x + rad - w.m.gridOrigin.x) / CanopyCell));
                int r0 = Mathf.Max(0, Mathf.FloorToInt((z - rad - w.m.gridOrigin.y) / CanopyCell)), r1 = Mathf.Min(w.ch - 1, Mathf.FloorToInt((z + rad - w.m.gridOrigin.y) / CanopyCell));
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        float dx = w.m.gridOrigin.x + (c + 0.5f) * CanopyCell - x, dz = w.m.gridOrigin.y + (r + 0.5f) * CanopyCell - z;
                        if (dx * dx + dz * dz <= (rad + CanopyCell * 0.5f) * (rad + CanopyCell * 0.5f)) w.canopy[r * w.cw + c] = Mathf.Max(w.canopy[r * w.cw + c], top);
                    }
            }
        }
        return w;
    }

    static Vector3 Bounds(GameObject prefab)
    {
        var b = new Bounds(); bool any = false;
        foreach (var f in prefab.GetComponentsInChildren<MeshFilter>())
        {
            if (!f.sharedMesh) continue;
            var s = Vector3.Scale(f.sharedMesh.bounds.size, f.transform.lossyScale);
            var bb = new Bounds(Vector3.zero, s);
            if (!any) { b = bb; any = true; } else b.Encapsulate(bb);
        }
        return any ? b.size : Vector3.one;
    }

    static readonly RaycastHit[] Hits = new RaycastHit[32];

    // The cab as the test sees it (a stated proxy of the render geometry, from tower-height-decision.md): a 7'0" square of walls,
    // open only through the window band of the side the eye stands at, between the sill (3'6") and the window head (7'6", the
    // eaves), less a corner post each end. Sash bars are not modelled. Everything outside the wall plane is the real collider set.
    public const float CabHalf = 1.0668f, SillAboveFloor = 1.0668f, HeadAboveFloor = 2.286f, CornerPost = 0.10f, WallClear = 0.15f;

    public const string SashName = "Sash"; // glazed and hinged: a sash's interaction collider (open or shut) doesn't block sight

    static bool IsSash(World w, Transform t)
    {
        for (; t && w.tower && t != w.tower; t = t.parent)
            if (t.name.IndexOf(SashName, System.StringComparison.OrdinalIgnoreCase) >= 0) return t.IsChildOf(w.tower);
        return false;
    }

    struct Eye { public Vector3 pos; public bool cab; public int side; public float floorY; }

    static readonly Vector2[] SideOut = { new Vector2(0, 1), new Vector2(1, 0), new Vector2(0, -1), new Vector2(-1, 0) };

    /// <summary>
    /// Clear line of sight from eye to target. From the cab the ray must leave through its side's window band; past the wall
    /// plane any collider on SightMask (the tower's own included) or any tree crown blocks it.
    /// </summary>
    static bool Sees(World w, Eye eye, Vector3 target, float endClear, bool trees)
    {
        var d = target - eye.pos; float len = d.magnitude;
        if (len <= endClear) return true;
        var dir = d / len; float from = 0f;
        if (eye.cab)
        {
            var o = SideOut[eye.side]; var lat = new Vector2(o.y, -o.x);
            var lp = w.site.Local(new Vector2(eye.pos.x, eye.pos.z));
            var ld = w.site.Local(w.site.centre + new Vector2(dir.x, dir.z));
            float along = Vector2.Dot(ld, o);
            if (along <= 1e-4f) return false;
            from = (CabHalf - Vector2.Dot(lp, o)) / along;
            float h = eye.pos.y + dir.y * from - eye.floorY;
            if (h < SillAboveFloor || h > HeadAboveFloor) return false;
            if (Mathf.Abs(Vector2.Dot(lp + ld * from, lat)) > CabHalf - CornerPost) return false;
            from += WallClear;
        }
        if (len - endClear <= from) return true;
        int n = Physics.RaycastNonAlloc(eye.pos + dir * from, dir, Hits, len - endClear - from, WorldLayers.SightMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (!IsSash(w, Hits[i].transform)) return false;
        if (!trees) return true;
        for (float s = Mathf.Max(1f, from); s < len - endClear; s += 1f)
        {
            var p = eye.pos + dir * s;
            float top = w.Canopy(p.x, p.z);
            if (p.y < top && p.y > w.Ground(p.x, p.z) + (top - w.Ground(p.x, p.z)) * CrownBase) return false;
        }
        return true;
    }

    /// <summary>
    /// Open water in clear sight within n metres outside a bank sample: a wet point (built ground at least WetMargin below
    /// WaterLevelY), probed every WaterStep; the sight line ends on the flat water surface at WaterLevelY, not on the bed.
    /// </summary>
    static bool OpenWater(World w, Eye eye, Vector2 bank, Vector2 outward, float n, bool trees)
    {
        for (float o = WaterStep; o <= n + 0.01f; o += WaterStep)
        {
            var p = bank + outward * o;
            if (w.Ground(p.x, p.y) <= w.water - WetMargin && Sees(w, eye, new Vector3(p.x, w.water, p.y), 1f, trees)) return true;
        }
        return false;
    }
    const float WetMargin = 0.05f;

    static int Quadrant(World w, float x, float z)
    {
        float b = Mathf.Atan2(x - w.site.centre.x, z - w.site.centre.y) * Mathf.Rad2Deg;
        return Mathf.FloorToInt(Mathf.Repeat(b + 45f, 360f) / 90f); // 0 N, 1 E, 2 S, 3 W
    }

    static float LongestRun(IEnumerable<bool> flags, float step)
    {
        int best = 0, cur = 0;
        foreach (var f in flags) { cur = f ? cur + 1 : 0; best = Mathf.Max(best, cur); }
        return Mathf.Max(0, best - 1) * step; // n consecutive samples span (n - 1) steps: 50 m at 10 m needs 6
    }

    public static string Test() => Test(CabFloorHeight);

    public static string Test(float cabFloor)
    {
        var w = Load();
        if (w.site == null) return "no sited LookoutTower landmark";
        Physics.SyncTransforms();
        var cfg = BellsBendData.LoadConfig(); var v = BellsBendData.LoadVectors(cfg);
        var bank = BellsBendRoads.Resample(v.innerBank, 10f);
        // Outward normal per bank sample: the side whose point 50 m out lies outside the playable polygon.
        var outward = new Vector2[bank.Count];
        for (int i = 0; i < bank.Count; i++)
        {
            var t = (bank[Mathf.Min(i + 1, bank.Count - 1)] - bank[Mathf.Max(i - 1, 0)]).normalized;
            var nrm = new Vector2(t.y, -t.x);
            outward[i] = BellsBendData.InPolygon(bank[i] + nrm * 50f, v.polygon) ? -nrm : nrm;
        }
        float nWater = cfg.shoreRampMeters;
        float fenceZ = cfg.northLineZ - BarrierBuilder.LineOffset;
        var fence = new List<Vector3>();
        for (float x = v.westCrossing.x; x <= v.eastCrossing.x; x += 5f) fence.Add(new Vector3(x, w.Ground(x, fenceZ) + BarrierBuilder.FenceHeight, fenceZ));

        string[] side = { "N", "E", "S", "W" };
        var sb = new StringBuilder("{\n");
        sb.Append($" \"cabFloorHeight\": {cabFloor:F3}, \"eyeAboveFloor\": {EyeHeight}, \"padY\": {w.site.padY:F2}, \"towerInScene\": {(w.tower ? "true" : "false")},\n");
        int passed = 0;
        float reach = Mathf.Max(bank.Max(p => Vector2.Distance(p, w.site.centre)) + nWater, fence.Max(p => Vector2.Distance(new Vector2(p.x, p.z), w.site.centre)));
        sb.Append($" \"cabProxy\": {{\"half\": {CabHalf}, \"sillAboveFloor\": {SillAboveFloor}, \"headAboveFloor\": {HeadAboveFloor}, \"cornerPost\": {CornerPost}, \"standFromCentre\": {StandFromCentre}}},\n");
        // Every non-trigger collider under the tower that the sash rule lets sight through (t-qa checks it is the 8 sashes and nothing else).
        var ignored = w.tower ? w.tower.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger && IsSash(w, c.transform)).Select(c => c.name).OrderBy(s => s, System.StringComparer.Ordinal).ToList() : new List<string>();
        sb.Append($" \"ignoredColliderRule\": \"under the tower root, own or ancestor name contains '{SashName}'\", \"ignoredColliderCount\": {ignored.Count}, \"ignoredColliders\": [{string.Join(", ", ignored.Select(s => "\"" + s + "\""))}],\n");
        sb.Append($" \"treeInstances\": {w.treeDist.Count}, \"treeInstancesWithinReach\": {w.treeDist.Count(d => d <= reach)}, \"reachMeters\": {reach:F0}, \"waterSurfaceY\": {w.water:F3}, \"wetMargin\": {WetMargin},\n");
        foreach (var (label, baseY) in new[] { ("cab", w.site.Origin.y + cabFloor + EyeHeight), ("groundTwin", w.site.Origin.y + EyeHeight) })
        {
            int visibleSides = 0, literalSides = 0; var rows = new List<string>();
            bool cab = label == "cab";
            // The base twin stands just outside the footings on each side (inside them the legs and stair would be the test).
            float off = cab ? StandFromCentre : BellsBendTowerSite.BaseSquare * 0.5f + 1.5f;
            Eye At(int q) { var e2 = w.site.World(SideOut[q] * off); return new Eye { pos = new Vector3(e2.x, baseY, e2.y), cab = cab, side = q, floorY = w.site.Origin.y + cabFloor }; }
            for (int q = 0; q < 4; q++)
            {
                var eye = At(q);
                var idx = Enumerable.Range(0, bank.Count).Where(i => Quadrant(w, bank[i].x, bank[i].y) == q).ToList();
                var literal = idx.Select(i => Sees(w, eye, new Vector3(bank[i].x, w.water + 0.05f, bank[i].y), 3f, true)).ToList();
                var open = idx.Select(i => OpenWater(w, eye, bank[i], outward[i], nWater, true)).ToList();
                var openBare = idx.Select(i => OpenWater(w, eye, bank[i], outward[i], nWater, false)).ToList();
                float run = LongestRun(open, 10f);
                if (run >= ShoreRun) visibleSides++;
                if (LongestRun(literal, 10f) >= ShoreRun) literalSides++;
                rows.Add($"   \"{side[q]}\": {{\"samples\": {idx.Count}, \"openWaterVisible\": {open.Count(b => b)}, \"openWaterLongestRun\": {run:F0}, " +
                         $"\"openWaterLongestRunNoTrees\": {LongestRun(openBare, 10f):F0}, \"literalWaterlineLongestRun\": {LongestRun(literal, 10f):F0}}}");
            }
            var eyeN = At(0);
            var fs = fence.Select(p => Sees(w, eyeN, p, 1f, true)).ToList();
            float fenceRun = LongestRun(fs, 5f);
            sb.Append($" \"{label}\": {{\n  \"eyeY\": {baseY:F2},\n  \"shoreline\": {{\n{string.Join(",\n", rows)}\n  }},\n");
            sb.Append($"  \"openWaterWithinMeters\": {nWater}, \"shorelineSidesVisible\": {visibleSides}, \"shorelinePass\": {(visibleSides >= 3 ? "true" : "false")}, \"literalWaterlineSides\": {literalSides},\n");
            sb.Append($"  \"fence\": {{\"samples\": {fs.Count}, \"visible\": {fs.Count(b => b)}, \"longestRun\": {fenceRun:F0}}}, \"fencePass\": {(fenceRun >= FenceRun ? "true" : "false")}\n }},\n");
            if (label == "cab") passed = visibleSides;
        }

        // Canopy against the cab eye.
        float eyeY = w.site.Origin.y + cabFloor + EyeHeight, tallest = float.NegativeInfinity;
        for (float dz = -CrownNear; dz <= CrownNear; dz += CanopyCell)
            for (float dx = -CrownNear; dx <= CrownNear; dx += CanopyCell)
                if (dx * dx + dz * dz <= CrownNear * CrownNear) tallest = Mathf.Max(tallest, w.Canopy(w.site.centre.x + dx, w.site.centre.y + dz));
        var shares = new float[4]; int blocked = 0;
        for (int q = 0; q < 4; q++)
        {
            int hit = 0, tot = 0;
            for (float az = q * 90f - 45f; az < q * 90f + 45f; az += 1f, tot++)
            {
                float sx = Mathf.Sin(az * Mathf.Deg2Rad), cz = Mathf.Cos(az * Mathf.Deg2Rad);
                for (float s = 1f; s < CrownFar; s += 1f)
                    if (w.Canopy(w.site.centre.x + sx * s, w.site.centre.y + cz * s) > eyeY) { hit++; break; }
            }
            shares[q] = hit / (float)tot;
            if (shares[q] > BlockedShare) blocked++;
        }
        bool above = eyeY > tallest;
        sb.Append($" \"canopy\": {{\"tallestCrownTopWithin60m\": {(float.IsNegativeInfinity(tallest) ? 0f : tallest):F2}, \"eyeY\": {eyeY:F2}, \"eyeAboveCanopy\": {(above ? "true" : "false")}, " +
                  $"\"blockedShare\": {{{string.Join(", ", shares.Select((s, i) => $"\"{side[i]}\": {s:F3}"))}}}, \"sidesBlocked\": {blocked}, \"pass\": {(above && blocked <= 1 ? "true" : "false")}}},\n");
        sb.Append($" \"targets\": {{\"shoreRunMeters\": {ShoreRun}, \"shoreSides\": 3, \"fenceRunMeters\": {FenceRun}, \"blockedShare\": {BlockedShare}, \"maxSidesBlocked\": 1}}\n" + "}\n");
        File.WriteAllText(Path.Combine(BellsBendTowerSite.EvidenceDir, "p3-view.json"), sb.ToString());
        return sb.ToString();
    }
}
