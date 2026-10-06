using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using WashedAshore.World;

// Level-designer (B2): the visible north barricade, bank to bank, positioned only from
// MapConfig.northLineZ so a line shift (B6) moves it. A scavenged post-1987 fence line
// (timber posts, rusted sheet, barbed wire) with rubble, wrecked vehicles, concrete blocks,
// a washed-out cut where the old Cleeces Ferry track meets it, and one closed checkpoint gate
// on Old Hickory Blvd (BoundaryGate reads MapConfig.gateOpen). Colliders stay on Default;
// the WorldBounds backstop belongs to WorldBoundsBuilder. Never saves the scene.
public static class BarrierBuilder
{
    public const string RootName = "NorthBarrier";
    public static float LineOffset = 20f;      // fence line this far south of northLineZ (Layer A: 10-30 m)
    public static float PostSpacing = 2.5f;
    public static float FenceHeight = 2.6f;
    public static float GateGap = 8f;
    public static float ApproachClearance = 3.6f;   // capsule 1.8 m + jump 1.2 m + margin (3.5 required; +0.1 for the 2 m sampling grid)
    public static float LaunchRadius = 12f;         // ground farther than this can't launch a jump that clears the fence
    public static float ColliderHeight = 4.0f;   // invisible, above the higher end: a 1.2 m jump off a 1.5 m wreck still stops at the fence (designer-2, B5)
    public static float WaterRunToStop = 30f;  // a water run this long marks the river bank
    public static float IntoWater = 0f;
    public static float CornerPastLine = 8f;   // corners run this far north of the line along each bank        // no dressing on the lake bed past the neck ends (designer-2); backstop covers it
    public static int Seed = 1987;

    const string MatDir = BellsBendData.WorldRoot + "/Barrier/Materials";
    const string Downtown = "Assets/ThirdParty/Quaternius/DowntownCity/FBX/";
    const string Survival = "Assets/ThirdParty/Quaternius/SurvivalPack/FBX/";
    const string Nature = "Assets/ThirdParty/Quaternius/NatureMegaKit/Prefabs/Rocks/";

    static System.Random rng;
    static Dictionary<string, Material> mats;
    static List<string> missing;
    static int pieces, kitPieces;

    public static string Build() => Build(BellsBendData.LoadConfig());

    public static string Build(MapConfig cfg)
    {
        if (!cfg) throw new System.ArgumentNullException(nameof(cfg));
        rng = new System.Random(Seed); missing = new List<string>(); pieces = kitPieces = 0; LastMaxExtension = 0f;
        mats = Materials();
        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);
        var root = new GameObject(RootName);
        float z = cfg.northLineZ - LineOffset, water = cfg.WaterLevelY;

        var vec = TryVectors(cfg);
        float gateX = vec != null ? CrossingX(Road(vec, "Old Hickory Boulevard"), z) : float.NaN;
        float startX = float.IsNaN(gateX) ? 0f : gateX;
        float x0 = Bank(startX, z, -1f, water), x1 = Bank(startX, z, 1f, water);
        if (float.IsNaN(gateX)) gateX = (x0 + x1) * 0.5f;
        float washX = vec != null ? CrossingX(Road(vec, "Cleeces Ferry Road"), z) : float.NaN;
        if (float.IsNaN(washX) || Mathf.Abs(washX - gateX) < 40f || washX < x0 + 20f || washX > x1 - 20f) washX = LowestX(x0, x1, z, gateX);

        if (AssetDatabase.IsValidFolder(MeshDir)) AssetDatabase.DeleteAsset(MeshDir);
        Directory.CreateDirectory(MeshDir); // no Refresh here: a refresh can re-import MapConfig mid-build (friction BB-L3)
        bakeIndex = bakedMeshes = 0;
        var fence = Group(root, "Fence");
        int bays = 0;
        BeginBake();
        for (float x = x0; x < x1; x += PostSpacing)
        {
            float xe = Mathf.Min(x + PostSpacing, x1);
            if (Mathf.Abs((x + xe) * 0.5f - gateX) < GateGap * 0.5f) continue;
            bool washout = Mathf.Abs(x - washX) < 9f;
            Bay(fence.transform, new Vector2(x, z), new Vector2(xe, z), washout);
            if (++bays % 20 == 0) { EndBake(fence.transform); BeginBake(); }
        }
        EndBake(fence.transform); BeginBake();   // each river corner gets its own chunk, so bounds stay local for culling
        int cornerBays = Corner(fence.transform, x0, z, -1f, cfg);
        EndBake(fence.transform); BeginBake();
        cornerBays += Corner(fence.transform, x1, z, 1f, cfg);
        EndBake(fence.transform);

        var dress = Group(root, "Dressing");
        for (float x = x0 + 6f; x < x1 - 6f; x += 30f)
        {
            if (Mathf.Abs(x - gateX) < 25f || Mathf.Abs(x - washX) < 20f) continue;
            float roll = (float)rng.NextDouble(), cx = x + Rand(-8f, 8f);
            if (roll < 0.18f) Rubble(dress.transform, cx, z - Rand(1.5f, 3f));
            else if (roll < 0.33f) Wreck(dress.transform, cx, z - Rand(2.5f, 4f), Rand(-25f, 25f), rng.NextDouble() < 0.3);
            else if (roll < 0.43f) for (int i = 0; i < 3; i++) Block(dress.transform, cx + i * 3.2f, z - 2.5f, Rand(-6f, 6f));
            else if (roll < 0.55f) Clutter(dress.transform, cx, z - Rand(1f, 3f));
        }
        Washout(Group(root, "Washout").transform, washX, z);
        var gate = Gate(Group(root, "Checkpoint").transform, gateX, z, cfg);

        Physics.SyncTransforms();
        return $"Barrier: lineZ={cfg.northLineZ} fenceZ={z:F1} span=[{x0:F1},{x1:F1}] length={x1 - x0:F0}m bays={bays} cornerBays={cornerBays} " +
               $"gateX={gateX:F1} gateOpen={cfg.gateOpen} leaves={(gate.leftLeaf ? 2 : 0)} washoutX={washX:F1} pieces={pieces} kitPieces={kitPieces} bakedMeshes={bakedMeshes} maxColliderAboveGround={LastMaxExtension:F1}m " +
               $"missingKit=[{string.Join(",", missing.Distinct())}] saved=False";
    }

    // ---------------- layout helpers ----------------

    static BellsBendData.Vectors TryVectors(MapConfig cfg)
    {
        try { return BellsBendData.LoadVectors(cfg); } catch (IOException) { return null; }
    }

    static List<Vector2> Road(BellsBendData.Vectors v, string name) => v.roads.Where(r => r.name == name).SelectMany(r => r.points).ToList();

    /// <summary>X where a road polyline crosses the given Z (nearest to the road's centre), NaN if none.</summary>
    public static float CrossingX(List<Vector2> pts, float z)
    {
        float best = float.NaN;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2 a = pts[i - 1], b = pts[i];
            if ((a.y - z) * (b.y - z) > 0f || Mathf.Approximately(a.y, b.y)) continue;
            float x = a.x + (z - a.y) / (b.y - a.y) * (b.x - a.x);
            if (float.IsNaN(best) || Mathf.Abs(x) < Mathf.Abs(best)) best = x;
        }
        return best;
    }

    static float H(float x, float z) => TerrainQuery.TryGroundHeight(new Vector3(x, 0f, z), out float h) ? h : float.NaN;

    /// <summary>Walks from x along z until a water run of WaterRunToStop m (the river), then IntoWater m past the bank.</summary>
    static float Bank(float x, float z, float dir, float water)
    {
        float lastLand = x, run = 0f;
        for (float d = 0f; d < 6000f; d += 1f)
        {
            float px = x + dir * d, h = H(px, z);
            if (float.IsNaN(h)) break;
            if (h < water) { run += 1f; if (run >= WaterRunToStop) break; }
            else { run = 0f; lastLand = px; }
        }
        return lastLand + dir * IntoWater;
    }

    static float LowestX(float x0, float x1, float z, float gateX)
    {
        float best = (x0 + x1) * 0.5f, low = float.MaxValue;
        for (float x = x0 + 30f; x < x1 - 30f; x += 2f)
        {
            if (Mathf.Abs(x - gateX) < 40f) continue;
            float h = H(x, z);
            if (h < low) { low = h; best = x; }
        }
        return best;
    }

    static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    static GameObject Group(GameObject root, string name)
    {
        var g = new GameObject(name); g.transform.SetParent(root.transform, false); return g;
    }

    static Vector3 Ground(float x, float z, float up = 0f)
    {
        float h = H(x, z);
        return new Vector3(x, (float.IsNaN(h) ? 0f : h) + up, z);
    }

    static GameObject Prim(PrimitiveType t, Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale, string mat, bool collider)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name; go.isStatic = true;
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mats[mat];
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        pieces++;
        return go;
    }

    static GameObject Kit(string path, Transform parent, Vector3 pos, Quaternion rot, float scale, bool collider)
    {
        var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!src) { missing.Add(Path.GetFileNameWithoutExtension(path)); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = Vector3.one * scale;
        foreach (var t in go.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        if (collider)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                var box = go.AddComponent<BoxCollider>();
                box.center = go.transform.InverseTransformPoint(b.center);
                box.size = go.transform.InverseTransformVector(b.size);
                box.size = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z));
            }
        }
        pieces++; kitPieces++;
        return go;
    }

    // ---------------- pieces ----------------

    /// <summary>
    /// River corners: the fence turns north along the bank from the fence line to CornerPastLine m past
    /// northLineZ, so the bank-side out-of-bounds steps by the crossings sit behind barrier.
    /// </summary>
    static int Corner(Transform parent, float xEnd, float z, float dir, MapConfig cfg)
    {
        float water = cfg.WaterLevelY; int n = 0;
        var prev = new Vector2(xEnd, z);
        for (float zz = z + PostSpacing; zz <= cfg.northLineZ + CornerPastLine + 0.01f; zz += PostSpacing)
        {
            float bx = Bank(xEnd - dir * 40f, zz, dir, water);
            if (Mathf.Abs(bx - prev.x) > 25f) bx = prev.x;  // keep the corner on this bank
            var cur = new Vector2(bx, zz);
            Bay(parent, prev, cur, false);
            prev = cur; n++;
        }
        return n;
    }

    /// <summary>
    /// One see-through bay (team-lead: the north view must read past the fence): timber post, three
    /// rails, barbed wire strands between and above them, an occasional low scavenged sheet. Visuals go
    /// into the bake buffer; impassability is one invisible box collider per bay on Default.
    /// </summary>
    static void Bay(Transform parent, Vector2 a2, Vector2 b2, bool washout)
    {
        var vis = bakeBuffer ? bakeBuffer.transform : parent;
        var a = Ground(a2.x, a2.y); var b = Ground(b2.x, b2.y);
        float lean = washout ? Rand(-18f, 18f) : Rand(-4f, 4f);
        Prim(PrimitiveType.Cylinder, vis, "Post", a + Vector3.up * (FenceHeight * 0.5f + 0.2f), Quaternion.Euler(lean, 0f, Rand(-3f, 3f)),
            new Vector3(0.18f, FenceHeight * 0.5f + 0.5f, 0.18f), "Wood", false);
        float len = Vector2.Distance(a2, b2);
        float yaw = Mathf.Atan2(-(b2.y - a2.y), b2.x - a2.x) * Mathf.Rad2Deg;
        float slope = Mathf.Atan2(b.y - a.y, len) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, slope);
        var mid = (a + b) * 0.5f;
        foreach (float y in new[] { 0.5f, 1.2f, 1.9f })
        {
            bool broken = washout || rng.NextDouble() < 0.08;
            var r = broken ? rot * Quaternion.Euler(Rand(-6f, 6f), 0f, Rand(-18f, 18f)) : rot;
            Prim(PrimitiveType.Cube, vis, "Rail", mid + Vector3.up * (broken ? y * Rand(0.3f, 0.8f) : y), r, new Vector3(len + 0.1f, 0.12f, 0.08f), "Wood", false);
        }
        foreach (float y in new[] { 0.85f, 1.55f, 2.25f, 2.55f, 2.85f })
        {
            var p0 = a + Vector3.up * y; var p1 = b + Vector3.up * (y + Rand(-0.1f, 0.06f));
            // Thin box, not a cylinder: 24 vertices instead of 88, and wire this thin reads the same.
            Prim(PrimitiveType.Cube, vis, "Wire", (p0 + p1) * 0.5f, Quaternion.FromToRotation(Vector3.up, p1 - p0),
                new Vector3(0.025f, Vector3.Distance(p0, p1), 0.025f), "Wire", false);
        }
        if (!washout && rng.NextDouble() < 0.22)
        {
            float h = Rand(0.7f, 1.3f);
            Prim(PrimitiveType.Cube, vis, "Sheet", mid + Vector3.up * (h * 0.5f), rot * Quaternion.Euler(Rand(-8f, 8f), 0f, Rand(-6f, 6f)),
                new Vector3(len * Rand(0.6f, 1f), h, 0.05f), rng.NextDouble() < 0.4 ? "RustDark" : "Rust", false);
        }
        var col = new GameObject("BayCollider") { isStatic = true };
        col.transform.SetParent(parent, false);
        // Level box (yaw only) from below the low end to FenceHeight above the high end, so steep bays stay full height (qa-2 scan).
        // Highest and lowest ground along the bay (not just at the posts: humps between posts count).
        float gLo = Mathf.Min(a.y, b.y), gHi = Mathf.Max(a.y, b.y);
        float ext = 0.1f / Mathf.Max(len, 0.1f); // the collider overhangs each post by 0.1 m
        for (int i = 0; i <= 16; i++) { float t = Mathf.Lerp(-ext, 1f + ext, i / 16f); float gy = Ground(Mathf.LerpUnclamped(a2.x, b2.x, t), Mathf.LerpUnclamped(a2.y, b2.y, t)).y; gLo = Mathf.Min(gLo, gy); gHi = Mathf.Max(gHi, gy); }
        // Where the line runs at the foot of a slope, the approach ground sits well above the fence ground: a player
        // jumping (or one big CharacterController.Move) from upslope stays high. Keep the top above the approach too.
        float lo = gLo - 0.5f, hi = Mathf.Max(gHi + ColliderHeight, ApproachMax(a2 - (b2 - a2).normalized * 0.15f, b2 + (b2 - a2).normalized * 0.15f) + ApproachClearance);
        LastMaxExtension = Mathf.Max(LastMaxExtension, hi - gHi);
        col.transform.SetPositionAndRotation(new Vector3(mid.x, (lo + hi) * 0.5f, mid.z), Quaternion.Euler(0f, yaw, 0f));
        col.AddComponent<BoxCollider>().size = new Vector3(len + 0.2f, hi - lo, 0.3f);
        // Extra posts on steep bays so the fence visibly climbs instead of burying its uphill end.
        if (Mathf.Abs(b.y - a.y) > 1.5f)
            Prim(PrimitiveType.Cylinder, vis, "Post", Ground((a2.x + b2.x) * 0.5f, (a2.y + b2.y) * 0.5f) + Vector3.up * (FenceHeight * 0.5f + 0.2f),
                Quaternion.identity, new Vector3(0.16f, FenceHeight * 0.5f + 0.5f, 0.16f), "Wood", false);
    }

    /// <summary>
    /// Highest ground on the playable (south) side within LaunchRadius (plan distance) of segment a-b. Only ground that close can launch a
    /// sprint-jump that still reaches the fence above its launch height (designer-2: 8 m/s, 1.2 m jump).
    /// </summary>
    static float ApproachMax(Vector2 a2, Vector2 b2)
    {
        float m = float.MinValue;
        Vector2 min = Vector2.Min(a2, b2) - Vector2.one * LaunchRadius, max = Vector2.Max(a2, b2) + Vector2.one * LaunchRadius;
        Vector2 ab = b2 - a2; float l2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
        // Playable (south) side only: ground north of the fence is out of bounds and launches nothing.
        for (float z = min.y; z <= Mathf.Min(a2.y, b2.y) + 0.01f; z += 1f)
            for (float x = min.x; x <= max.x + 0.01f; x += 1f)
            {
                var p = new Vector2(x, z);
                if (Vector2.Distance(p, a2 + ab * Mathf.Clamp01(Vector2.Dot(p - a2, ab) / l2)) > LaunchRadius) continue;
                m = Mathf.Max(m, Ground(x, z).y);
            }
        return m;
    }

    public static float LastMaxExtension;   // report: tallest collider top above its own ground, m

    /// <summary>Collider height above a base Y for a gate part spanning a-b: >= ColliderHeight + camber margin, and clear of the approach.</summary>
    static float GateColliderTop(Vector2 a2, Vector2 b2, float baseY) =>
        Mathf.Max(ColliderHeight + 0.3f, ApproachMax(a2, b2) + ApproachClearance - baseY);

    // ---------------- mesh baking (keeps ~10k primitives down to a few dozen renderers) ----------------

    static GameObject bakeBuffer;
    static int bakeIndex, bakedMeshes;
    const string MeshDir = BellsBendData.WorldRoot + "/Barrier/Meshes";

    static void BeginBake() { bakeBuffer = new GameObject("_bake"); }

    /// <summary>Combines everything in the bake buffer per material into mesh assets under parent.</summary>
    static void EndBake(Transform parent)
    {
        if (!bakeBuffer) return;
        var byMat = new Dictionary<Material, List<CombineInstance>>();
        foreach (var mf in bakeBuffer.GetComponentsInChildren<MeshFilter>())
        {
            var mat = mf.GetComponent<MeshRenderer>().sharedMaterial;
            if (!byMat.TryGetValue(mat, out var l)) byMat[mat] = l = new List<CombineInstance>();
            l.Add(new CombineInstance { mesh = mf.sharedMesh, transform = mf.transform.localToWorldMatrix });
        }
        foreach (var kv in byMat)
        {
            var mesh = new Mesh { name = $"Barrier_{bakeIndex:D3}_{kv.Key.name}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(kv.Value.ToArray(), true, true);
            // Flat-colour URP materials need positions and normals only: dropping UVs and tangents roughly halves the
            // force-text .asset size.
            mesh.uv = null; mesh.tangents = null;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, $"{MeshDir}/{mesh.name}.asset");
            var go = new GameObject(mesh.name) { isStatic = true };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
            bakedMeshes++;
        }
        Object.DestroyImmediate(bakeBuffer);
        bakeBuffer = null; bakeIndex++;
    }

    static void Rubble(Transform parent, float x, float z)
    {
        var g = new GameObject("Rubble"); g.transform.SetParent(parent, false);
        int n = 6 + rng.Next(5);
        for (int i = 0; i < n; i++)
        {
            var p = Ground(x + Rand(-4f, 4f), z + Rand(-2f, 1f), Rand(-0.3f, 0.6f));
            var r = Quaternion.Euler(Rand(-35f, 35f), Rand(0f, 360f), Rand(-35f, 35f));
            switch (rng.Next(4))
            {
                case 0: if (!Kit(Downtown + "Brick_Plain_1.fbx", g.transform, p, r, Rand(0.7f, 1.1f), true)) Prim(PrimitiveType.Cube, g.transform, "Brick", p, r, new Vector3(1.2f, 0.8f, 0.5f), "Brick", true); break;
                case 1: if (!Kit(Downtown + "Entrance_Concrete_2x1.fbx", g.transform, p, r, Rand(0.6f, 1f), true)) Prim(PrimitiveType.Cube, g.transform, "Slab", p, r, new Vector3(1.6f, 0.3f, 1f), "Concrete", true); break;
                case 2: if (!Kit(Nature + $"Rock_Medium_{1 + rng.Next(3)}.prefab", g.transform, p, r, Rand(0.6f, 1.1f), true)) Prim(PrimitiveType.Sphere, g.transform, "Rock", p, r, Vector3.one * 1.2f, "Concrete", true); break;
                default: Prim(PrimitiveType.Cube, g.transform, "Debris", p, r, new Vector3(Rand(0.5f, 1.4f), Rand(0.3f, 0.8f), Rand(0.5f, 1.2f)), "Dirt", true); break;
            }
        }
    }

    /// <summary>Blockout wrecked car: rusted body, caved cabin, flat tyres, optionally on its side.</summary>
    static void Wreck(Transform parent, float x, float z, float yaw, bool onSide)
    {
        var g = new GameObject("WreckedVehicle") { isStatic = true };
        g.transform.SetParent(parent, false);
        var basePos = Ground(x, z, onSide ? 0.9f : 0.35f);
        g.transform.SetPositionAndRotation(basePos, Quaternion.Euler(Rand(-3f, 3f), 90f + yaw, onSide ? 88f : Rand(-4f, 4f)));
        var body = rng.NextDouble() < 0.5 ? "WreckRust" : "WreckPaint";
        Child(g, PrimitiveType.Cube, "Body", new Vector3(0f, 0.45f, 0f), Vector3.zero, new Vector3(1.8f, 0.7f, 4.4f), body);
        Child(g, PrimitiveType.Cube, "Cabin", new Vector3(0f, 1.05f, -0.3f), new Vector3(Rand(-6f, 6f), 0f, Rand(-5f, 5f)), new Vector3(1.6f, Rand(0.4f, 0.6f), 2.1f), body);
        Child(g, PrimitiveType.Cube, "Windows", new Vector3(0f, 1.05f, -0.3f), Vector3.zero, new Vector3(1.62f, 0.3f, 1.9f), "Glassless");
        for (int i = 0; i < 4; i++)
            Child(g, PrimitiveType.Cylinder, "Tyre", new Vector3(i < 2 ? -0.85f : 0.85f, 0.1f, i % 2 == 0 ? 1.35f : -1.35f), new Vector3(0f, 0f, 90f), new Vector3(0.6f, 0.12f, 0.6f), "Tyre");
        var box = g.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 0.7f, 0f); box.size = new Vector3(1.9f, 1.5f, 4.5f);
        pieces++;
    }

    static void Child(GameObject g, PrimitiveType t, string name, Vector3 local, Vector3 euler, Vector3 scale, string mat)
    {
        var c = GameObject.CreatePrimitive(t);
        c.name = name; c.isStatic = true;
        Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(g.transform, false);
        c.transform.localPosition = local; c.transform.localEulerAngles = euler; c.transform.localScale = scale;
        c.GetComponent<Renderer>().sharedMaterial = mats[mat];
    }

    static void Block(Transform parent, float x, float z, float yaw)
    {
        var g = new GameObject("ConcreteBlock") { isStatic = true };
        g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(Ground(x, z), Quaternion.Euler(Rand(-2f, 2f), yaw, Rand(-3f, 3f)));
        Child(g, PrimitiveType.Cube, "Base", new Vector3(0f, 0.25f, 0f), Vector3.zero, new Vector3(3f, 0.5f, 0.6f), "Concrete");
        Child(g, PrimitiveType.Cube, "Top", new Vector3(0f, 0.6f, 0f), Vector3.zero, new Vector3(3f, 0.4f, 0.25f), "Concrete");
        var box = g.AddComponent<BoxCollider>(); box.center = new Vector3(0f, 0.4f, 0f); box.size = new Vector3(3f, 0.8f, 0.6f);
        pieces++;
    }

    static void Clutter(Transform parent, float x, float z)
    {
        // Survival Pack FBX import oversized (GasCan 2.5 m tall); scales bring them to real size.
        string[] items = { "GasCan", "PropaneTank", "Trashcan", "WoodLog" };
        float[] scales = { 0.2f, 0.42f, 0.16f, 0.45f };
        int n = 2 + rng.Next(3);
        for (int i = 0; i < n; i++)
        {
            int k = rng.Next(items.Length); var item = items[k];
            var p = Ground(x + Rand(-2f, 2f), z + Rand(-1f, 1f), 0.05f);
            if (!Kit(Survival + item + ".fbx", parent, p, Quaternion.Euler(rng.NextDouble() < 0.4 ? 90f : 0f, Rand(0f, 360f), 0f), scales[k], false))
                Prim(PrimitiveType.Cylinder, parent, item, p + Vector3.up * 0.4f, Quaternion.identity, new Vector3(0.5f, 0.4f, 0.5f), "RustDark", false);
        }
    }

    /// <summary>Washed-out cut: the old lane collapses into a scoured drainage; broken asphalt, exposed culverts, slumped fence.</summary>
    static void Washout(Transform parent, float x, float z)
    {
        for (int i = 0; i < 5; i++)
        {
            var p = Ground(x + Rand(-7f, 7f), z - Rand(2f, 9f), Rand(-0.6f, 0.1f));
            var r = Quaternion.Euler(Rand(18f, 38f) * (rng.NextDouble() < 0.5 ? 1 : -1), Rand(0f, 360f), Rand(-20f, 20f));
            if (!Kit(Downtown + "Street_Asphalt_6x6.fbx", parent, p, r, Rand(0.3f, 0.45f), true))
                Prim(PrimitiveType.Cube, parent, "AsphaltSlab", p, r, new Vector3(2.4f, 0.25f, 2f), "Asphalt", true);
        }
        for (int i = 0; i < 2; i++)
            Prim(PrimitiveType.Cylinder, parent, "Culvert", Ground(x + (i == 0 ? -1.2f : 1.4f), z - 4f, 0.2f),
                Quaternion.Euler(90f + Rand(-10f, 10f), Rand(-15f, 15f), 0f), new Vector3(1.3f, 2.5f, 1.3f), "Concrete", true);
        for (int i = 0; i < 9; i++)
            Prim(PrimitiveType.Sphere, parent, "Scour", Ground(x + Rand(-9f, 9f), z - Rand(0f, 10f), Rand(-0.8f, -0.2f)),
                Quaternion.Euler(0f, Rand(0f, 360f), 0f), new Vector3(Rand(2.5f, 5f), Rand(0.8f, 1.6f), Rand(2f, 4f)), "Dirt", true);
        for (int i = 0; i < 3; i++)
            Prim(PrimitiveType.Cylinder, parent, "BrokenPost", Ground(x + Rand(-6f, 6f), z - Rand(1f, 6f), 0.3f),
                Quaternion.Euler(Rand(50f, 85f), Rand(0f, 360f), 0f), new Vector3(0.18f, 1.2f, 0.18f), "Wood", false);
        Sign(parent, x + 9f, z - 6f, "WASHED OUT");
    }

    static BoundaryGate Gate(Transform parent, float x, float z, MapConfig cfg)
    {
        float half = GateGap * 0.5f;
        foreach (float s in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Cube, parent, "GatePost", Ground(x + s * (half + 0.3f), z, 1.5f), Quaternion.identity, new Vector3(0.6f, 3.6f, 0.6f), "Concrete", true);
            // Invisible extension so the posts stop a jump as high as the fence bays do.
            var ext = new GameObject("GatePostCollider") { isStatic = true };
            ext.transform.SetParent(parent, false);
            var pxz = new Vector2(x + s * (half + 0.3f), z);
            float ptop = GateColliderTop(pxz - Vector2.right * 0.3f, pxz + Vector2.right * 0.3f, Ground(pxz.x, pxz.y).y);
            ext.transform.position = Ground(pxz.x, pxz.y, (ptop - 0.5f) * 0.5f);
            ext.AddComponent<BoxCollider>().size = new Vector3(0.6f, ptop + 0.5f, 0.6f);
        }
        var gate = new GameObject("Gate"); gate.transform.SetParent(parent, false);
        gate.transform.position = Ground(x, z);
        var comp = gate.AddComponent<BoundaryGate>();
        comp.config = cfg;
        comp.leftLeaf = Leaf(gate.transform, x - half, z, 1f);
        comp.rightLeaf = Leaf(gate.transform, x + half, z, -1f);
        comp.Apply();
        Sign(parent, x - half - 2.5f, z - 1.5f, "CHECKPOINT\nROAD CLOSED");
        for (int i = 0; i < 3; i++) Block(parent, x + (i - 1) * 3.4f + (i == 1 ? 1.2f : 0f), z - 6f - (i % 2) * 3f, Rand(-8f, 8f));
        for (int i = 0; i < 6; i++)
            Prim(PrimitiveType.Cube, parent, "Sandbags", Ground(x + half + 2f + (i % 3) * 1.05f, z - 2f, 0.25f + (i / 3) * 0.4f),
                Quaternion.Euler(0f, Rand(-5f, 5f), 0f), new Vector3(1f, 0.4f, 0.55f), "Sandbag", true);
        Wreck(parent, x + Rand(-1f, 1f), z + 6f, Rand(60f, 80f), false); // pushed across the road on the far side
        return comp;
    }

    static Transform Leaf(Transform gate, float hingeX, float z, float dir)
    {
        var hinge = new GameObject(dir > 0 ? "LeafLeft" : "LeafRight").transform;
        hinge.SetParent(gate, false);
        hinge.position = Ground(hingeX, z);
        float w = GateGap * 0.5f - 0.05f;
        var leaf = hinge.gameObject;
        void Part(string n, Vector3 local, Vector3 scale, string m, bool col)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = n; c.transform.SetParent(hinge, false);
            c.transform.localPosition = local; c.transform.localScale = scale;
            c.GetComponent<Renderer>().sharedMaterial = mats[m];
            if (!col) Object.DestroyImmediate(c.GetComponent<Collider>());
            pieces++;
        }
        var infill = new GameObject("LeafCollider"); infill.transform.SetParent(hinge, false);
        // Invisible: from 0.5 m below the hinge ground to ColliderHeight above it (visual leaf is 2.85 m).
        // +0.3 m for road camber across the leaf; taller where the road climbs toward the gate.
        float ltop = GateColliderTop(new Vector2(Mathf.Min(hingeX, hingeX + dir * w), z), new Vector2(Mathf.Max(hingeX, hingeX + dir * w), z), hinge.position.y);
        infill.transform.localPosition = new Vector3(dir * w * 0.5f, (ltop - 0.5f) * 0.5f, 0f);
        infill.AddComponent<BoxCollider>().size = new Vector3(w, ltop + 0.5f, 0.2f);
        for (float bx = 0.3f; bx < w - 0.1f; bx += 0.3f)
            Part("Bar", new Vector3(dir * bx, 1.55f, 0f), new Vector3(0.05f, 2.5f, 0.05f), "Steel", false);
        Part("RailMid", new Vector3(dir * w * 0.5f, 1.55f, 0f), new Vector3(w, 0.08f, 0.08f), "Steel", false);
        Part("Plate", new Vector3(dir * w * 0.5f, 0.75f, 0.03f), new Vector3(w * 0.9f, 0.9f, 0.04f), "Rust", false);
        Part("RailTop", new Vector3(dir * w * 0.5f, 2.85f, 0f), new Vector3(w, 0.1f, 0.1f), "Steel", false);
        Part("RailBottom", new Vector3(dir * w * 0.5f, 0.3f, 0f), new Vector3(w, 0.1f, 0.1f), "Steel", false);
        Part("Stile", new Vector3(dir * 0.05f, 1.55f, 0f), new Vector3(0.1f, 2.6f, 0.1f), "Steel", false);
        var brace = GameObject.CreatePrimitive(PrimitiveType.Cube);
        brace.name = "Brace"; Object.DestroyImmediate(brace.GetComponent<Collider>());
        brace.transform.SetParent(hinge, false);
        brace.transform.localPosition = new Vector3(dir * w * 0.5f, 1.55f, -0.06f);
        brace.transform.localRotation = Quaternion.Euler(0f, 0f, dir * Mathf.Atan2(2.5f, w) * Mathf.Rad2Deg);
        brace.transform.localScale = new Vector3(Mathf.Sqrt(w * w + 6.25f), 0.1f, 0.08f);
        brace.GetComponent<Renderer>().sharedMaterial = mats["Steel"];
        pieces++;
        return hinge;
    }

    static void Sign(Transform parent, float x, float z, string text)
    {
        var p = Ground(x, z);
        Prim(PrimitiveType.Cylinder, parent, "SignPost", p + Vector3.up * 1.1f, Quaternion.identity, new Vector3(0.1f, 1.1f, 0.1f), "Wood", false);
        var board = Prim(PrimitiveType.Cube, parent, "SignBoard", p + Vector3.up * 2f, Quaternion.Euler(0f, 0f, Rand(-6f, 6f)), new Vector3(2f, 0.9f, 0.05f), "SignWhite", false);
        var t = new GameObject("SignText").AddComponent<TextMesh>();
        t.transform.SetParent(board.transform, false);
        t.transform.localPosition = new Vector3(0f, 0f, -0.6f); t.transform.localScale = new Vector3(0.045f, 0.1f, 1f);
        t.text = text; t.anchor = TextAnchor.MiddleCenter; t.alignment = TextAlignment.Center;
        t.fontSize = 48; t.characterSize = 0.5f; t.color = new Color(0.65f, 0.08f, 0.05f);
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.GetComponent<MeshRenderer>().sharedMaterial = t.font.material;
    }

    // ---------------- materials ----------------

    static Dictionary<string, Material> Materials()
    {
        Directory.CreateDirectory(MatDir);
        var specs = new (string n, Color c, float metal, float smooth)[]
        {
            ("Rust", new Color(0.42f, 0.24f, 0.14f), 0.35f, 0.15f), ("RustDark", new Color(0.28f, 0.17f, 0.11f), 0.3f, 0.1f),
            ("Wood", new Color(0.33f, 0.27f, 0.2f), 0f, 0.05f), ("Wire", new Color(0.2f, 0.19f, 0.18f), 0.6f, 0.3f),
            ("Steel", new Color(0.24f, 0.24f, 0.25f), 0.7f, 0.25f), ("Concrete", new Color(0.56f, 0.55f, 0.52f), 0f, 0.08f),
            ("Brick", new Color(0.5f, 0.27f, 0.2f), 0f, 0.05f), ("Dirt", new Color(0.32f, 0.25f, 0.17f), 0f, 0.02f),
            ("Asphalt", new Color(0.2f, 0.2f, 0.2f), 0f, 0.1f), ("WreckRust", new Color(0.36f, 0.2f, 0.12f), 0.4f, 0.15f),
            ("WreckPaint", new Color(0.26f, 0.31f, 0.35f), 0.4f, 0.2f), ("Tyre", new Color(0.06f, 0.06f, 0.06f), 0f, 0.15f),
            ("Glassless", new Color(0.04f, 0.04f, 0.05f), 0f, 0.4f), ("Sandbag", new Color(0.45f, 0.42f, 0.3f), 0f, 0.02f),
            ("SignWhite", new Color(0.8f, 0.78f, 0.72f), 0f, 0.1f),
        };
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var d = new Dictionary<string, Material>();
        foreach (var (n, c, metal, smooth) in specs)
        {
            var path = $"{MatDir}/Barrier_{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            d[n] = m;
        }
        return d;
    }
}
