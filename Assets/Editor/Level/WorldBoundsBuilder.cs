using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WashedAshore.Gameplay;
using WashedAshore.World;

// Gameplay-engineer tool (spec B3/B4): builds the hidden backstop wall and the WorldBoundsClamp
// from the north line. Re-runnable; replaces its own "WorldBounds" root in the open scene.
// Run via: unity command eval "return WorldBoundsBuilder.Build();"
public static class WorldBoundsBuilder
{
    public const string RootName = "WorldBounds";
    public const string LayerName = "WorldBounds";

    // Tunables (not designer-facing; the line itself comes from MapConfig).
    public static float Thickness = 3f;            // B3: >= 2 m
    public static float HeightAboveGround = 35f;   // B3: >= 30 m above the highest ground under each segment
    public static float DepthBelowGround = 20f;    // runs below the lowest ground so nothing slips under
    public static float SegmentLength = 50f;
    public static float EndOverhang = 50f;         // past the outermost terrain edge on both ends
    public static float SnapClearance = 1.5f;      // clamp puts bodies this far south of the wall's south face

    public const string MapConfigPath = "Assets/World/MapConfig.asset";

    /// <summary>Builds from the project's MapConfig.</summary>
    public static string Build() => Build(AssetDatabase.LoadAssetAtPath<MapConfig>(MapConfigPath));

    public static string Build(MapConfig cfg, bool save = true)
    {
        if (!cfg) throw new System.ArgumentNullException(nameof(cfg), $"No MapConfig (expected {MapConfigPath})");
        return Build(NorthLine(cfg), save);
    }

    /// <summary>MapConfig's north line is the 36.2055 N parallel: a constant Unity Z.</summary>
    public static Vector2[] NorthLine(MapConfig cfg) =>
        new[] { new Vector2(-1f, cfg.northLineZ), new Vector2(1f, cfg.northLineZ) };

    /// <summary>
    /// The wall's north face sits on the line, so a player's centre can never reach it. It
    /// spans every terrain tile edge to edge plus <see cref="EndOverhang"/>, which carries it
    /// through the water margin on both ends.
    /// </summary>
    public static string Build(Vector2[] lineXZ, bool save)
    {
        int layer = EnsureLayer(LayerName);
        var old = GameObject.Find(RootName);
        if (old) Object.DestroyImmediate(old);

        var root = new GameObject(RootName);
        var wall = new GameObject("Backstop");
        wall.transform.SetParent(root.transform, false);

        var path = WallPath(lineXZ);
        int segments = 0;
        float lowest = float.MaxValue, highestTop = float.MinValue, minClear = float.MaxValue;
        for (int i = 1; i < path.Count; i++)
        {
            Vector2 a = path[i - 1], b = path[i];
            float len = Vector2.Distance(a, b);
            int pieces = Mathf.Max(1, Mathf.CeilToInt(len / SegmentLength));
            for (int p = 0; p < pieces; p++)
            {
                Vector2 s = Vector2.Lerp(a, b, p / (float)pieces), e = Vector2.Lerp(a, b, (p + 1) / (float)pieces);
                var (lo, hi) = GroundRange(s, e);
                float bottom = lo - DepthBelowGround, top = hi + HeightAboveGround;
                AddSegment(wall.transform, layer, s, e, bottom, top, segments++);
                lowest = Mathf.Min(lowest, bottom);
                highestTop = Mathf.Max(highestTop, top);
                minClear = Mathf.Min(minClear, top - hi);
            }
        }

        var clamp = root.AddComponent<WorldBoundsClamp>();
        clamp.Configure(lineXZ, Thickness + SnapClearance);
        root.isStatic = false;

        Physics.SyncTransforms();
        if (save)
        {
            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
        }
        return $"WorldBounds: layer={LayerName}({layer}) segments={segments} span=[{path[0].x:F1},{path[path.Count - 1].x:F1}] " +
               $"lineZ=[{lineXZ.Min(v => v.y):F1},{lineXZ.Max(v => v.y):F1}] thickness={Thickness} bottom={lowest:F1} top={highestTop:F1} " +
               $"minHeightAboveGround={minClear:F1} clampInset={Thickness + SnapClearance:F1} saved={save}";
    }

    /// <summary>The north line sorted by X, extended flat past every terrain tile on both sides.</summary>
    public static List<Vector2> WallPath(Vector2[] lineXZ)
    {
        var line = lineXZ.OrderBy(v => v.x).ToList();
        float minX = line[0].x, maxX = line[line.Count - 1].x;
        foreach (var t in Terrain.activeTerrains)
        {
            minX = Mathf.Min(minX, t.transform.position.x);
            maxX = Mathf.Max(maxX, t.transform.position.x + t.terrainData.size.x);
        }
        minX -= EndOverhang;
        maxX += EndOverhang;
        if (minX < line[0].x) line.Insert(0, new Vector2(minX, line[0].y));
        if (maxX > line[line.Count - 1].x) line.Add(new Vector2(maxX, line[line.Count - 1].y));
        return line;
    }

    static (float lo, float hi) GroundRange(Vector2 a, Vector2 b)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b)));
        for (int i = 0; i <= n; i++)
        {
            var p = Vector2.Lerp(a, b, i / (float)n);
            // Sample across the wall's thickness too, south face to north face.
            for (float d = -Thickness; d <= 0.01f; d += Thickness)
                if (TerrainQuery.TryGroundHeight(new Vector3(p.x, 0f, p.y + d), out float h))
                {
                    lo = Mathf.Min(lo, h);
                    hi = Mathf.Max(hi, h);
                }
        }
        return lo > hi ? (0f, 0f) : (lo, hi);
    }

    static void AddSegment(Transform parent, int layer, Vector2 a, Vector2 b, float bottom, float top, int index)
    {
        var dir = (b - a).normalized;
        var south = new Vector2(dir.y, -dir.x); // right-hand normal of a west->east line points south
        if (south.y > 0f) south = -south;
        var mid = (a + b) * 0.5f + south * (Thickness * 0.5f);
        var go = new GameObject($"Backstop_{index:D3}") { layer = layer, isStatic = true };
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(new Vector3(mid.x, (bottom + top) * 0.5f, mid.y),
            Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y), Vector3.up));
        var box = go.AddComponent<BoxCollider>();
        // Overlap neighbours by a thickness so joints on a bent line leave no gap.
        box.size = new Vector3(Thickness, top - bottom, Vector2.Distance(a, b) + Thickness * 2f);
    }

    public static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name);
        if (existing >= 0) return existing;
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var slot = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(slot.stringValue)) continue;
            slot.stringValue = name;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return i;
        }
        throw new System.InvalidOperationException($"No free user layer for {name}");
    }
}
