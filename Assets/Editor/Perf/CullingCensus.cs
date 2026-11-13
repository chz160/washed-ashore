using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WashedAshore.Perf.Editor
{
    /// <summary>
    /// Issue #9 census, read-only (task #24): Q4 oversized bounds and the static culling inventory of the built scene.
    /// Opens World.unity, reads it and writes TestResults/9-culling-census.json. Never saves the scene or any asset.
    /// Run headless (no Editor may have the project open; hold .studio/unity.lock):
    ///   Unity.exe -batchmode -nographics -projectPath . -executeMethod WashedAshore.Perf.Editor.CullingCensus.RunBatch
    ///             [-fcCabHeight m] -logFile TestResults/9-census.log
    /// Exit code 0 written, 1 failed.
    ///
    /// Oversized: a renderer whose world bounds exceed 200 m on any axis. "Defeats culling" is judged from the views
    /// the protocol uses: the five FrustumSpots poses and the route's three waypoints (PlayerSpawn, LM_OutdoorCenter,
    /// TowerBaseSpawn), each turned through 12 headings 30 degrees apart at the scene camera's field of view, 16:9 and
    /// the authored far plane. A renderer whose box meets the frustum in at least half of those 96 views is submitted
    /// from most places, so its culling is defeated; the "why" names the size, the views hit and the share of the box
    /// past fog end. Edit-time bounds only: a renderer that moves or is spawned at run time is listed by its root.
    /// </summary>
    public static class CullingCensus
    {
        const float Oversize = 200f, DefeatShare = 0.5f;
        const int Headings = 12;
        const string Out = "TestResults/9-culling-census.json";

        public static void RunBatch()
        {
            try
            {
                string summary = Run();
                Debug.Log("[Census] " + summary);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Census] FAILED " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v) => FrustumMath.F(v);
        static string S(string v) => "\"" + FrustumMath.Esc(v) + "\"";
        static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";

        static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string LayerName(int l) { var n = LayerMask.LayerToName(l); return string.IsNullOrEmpty(n) ? "layer" + l : n; }

        public static string Run()
        {
            var scenePath = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).FirstOrDefault();
            if (scenePath == null) throw new System.InvalidOperationException("no enabled scene in the build settings");
            var root = Path.GetDirectoryName(Application.dataPath);
            var sceneFile = Path.Combine(root, scenePath);
            var before = File.GetLastWriteTimeUtc(sceneFile);
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var args = System.Environment.GetCommandLineArgs();
            float cabHeight = FarViewProbe.CabHeight;
            int ci = System.Array.IndexOf(args, "-fcCabHeight");
            if (ci >= 0 && ci + 1 < args.Length && float.TryParse(args[ci + 1], NumberStyles.Float, Inv, out float ch) && ch > 0f) cabHeight = ch;

            var roots = scene.GetRootGameObjects();
            var renderers = roots.SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToList();
            var cams = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).ToList();
            var mainCam = cams.FirstOrDefault(c => c.CompareTag("MainCamera")) ?? cams.FirstOrDefault();
            float fov = mainCam ? mainCam.fieldOfView : 60f, near = mainCam ? mainCam.nearClipPlane : 0.1f, far = mainCam ? mainCam.farClipPlane : 1000f;
            float fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;
            var terrains = roots.SelectMany(r => r.GetComponentsInChildren<Terrain>(true)).ToList();

            // Views: the static spots and the route's waypoints, each with 12 headings.
            var eyes = new List<(string name, Vector3 eye)>();
            foreach (var p in FrustumSpots.Defaults(cabHeight)) eyes.Add((p.name, new Vector3(p.x, Ground(terrains, p.x, p.z) + p.heightAboveGround, p.z)));
            foreach (var n in new[] { "PlayerSpawn", "LM_OutdoorCenter", "TowerBaseSpawn" })
            {
                var go = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t => t.name == n);
                if (go) eyes.Add(("route:" + n, new Vector3(go.position.x, Ground(terrains, go.position.x, go.position.z) + FrustumSpots.Eye, go.position.z)));
            }
            var views = new List<(string name, Vector3 eye, Vector3 fwd, Plane[] planes)>();
            var proj = Matrix4x4.Perspective(fov, 16f / 9f, near, far);
            foreach (var (name, eye) in eyes)
                for (int h = 0; h < Headings; h++)
                {
                    var rot = Quaternion.Euler(0f, h * 360f / Headings, 0f);
                    var w2c = Matrix4x4.TRS(eye, rot, Vector3.one).inverse;
                    w2c = Matrix4x4.Scale(new Vector3(1, 1, -1)) * w2c;   // camera space looks down -Z
                    views.Add((name, eye, rot * Vector3.forward, GeometryUtility.CalculateFrustumPlanes(proj * w2c)));
                }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"issue\":9,\"task\":24,\"generated_utc\":{S(System.DateTime.UtcNow.ToString("o"))},\"unity\":{S(Application.unityVersion)},\"scene\":{S(scenePath)},");
            sb.Append($"\"method\":{S("Edit-time bounds of every Renderer in the scene (inactive included, flagged). Oversized: any axis > 200 m. Views: 5 FrustumSpots poses + 3 route waypoints x 12 headings (30 deg), scene camera fov, 16:9, authored far plane. defeats_culling: box meets the frustum in >= 50% of the views. Ground heights from the terrain tiles. Read-only; the scene is not saved.")},");

            // Camera, fog, URP, quality.
            sb.Append("\"cameras\":[" + string.Join(",", cams.Select(c =>
                $"{{\"path\":{S(HierarchyPath(c.transform))},\"tag\":{S(c.tag)},\"enabled\":{B(c.enabled && c.gameObject.activeInHierarchy)},\"far\":{F(c.farClipPlane)},\"near\":{F(c.nearClipPlane)},\"fov\":{F(c.fieldOfView)}," +
                $"\"use_occlusion_culling\":{B(c.useOcclusionCulling)},\"layer_cull_nonzero\":{{{string.Join(",", c.layerCullDistances.Select((d, i) => (d, i)).Where(x => x.d > 0f).Select(x => S(LayerName(x.i)) + ":" + F(x.d)))}}}," +
                $"\"layer_cull_spherical\":{B(c.layerCullSpherical)},\"culling_mask\":{c.cullingMask}" + "}")) + "],");
            sb.Append($"\"fog\":{{\"enabled\":{B(RenderSettings.fog)},\"mode\":{S(RenderSettings.fogMode.ToString())},\"start\":{F(fogStart)},\"end\":{F(fogEnd)}}},");
            sb.Append(Urp() + ",");
            sb.Append($"\"occlusion_culling\":{{\"umbra_data_size\":{StaticOcclusionCulling.umbraDataSize},\"baked\":{B(StaticOcclusionCulling.umbraDataSize > 0)}}},");

            // Terrain tiles.
            sb.Append("\"terrain\":{\"tiles\":[" + string.Join(",", terrains.OrderBy(t => t.name).Select(t =>
            {
                var d = t.terrainData;
                return $"{{\"name\":{S(t.name)},\"size\":{V(d ? d.size : Vector3.zero)},\"basemap\":{F(t.basemapDistance)},\"trees\":{F(t.treeDistance)},\"billboards\":{F(t.treeBillboardDistance)}," +
                       $"\"details\":{F(t.detailObjectDistance)},\"detail_density\":{F(t.detailObjectDensity)},\"pixel_error\":{F(t.heightmapPixelError)},\"tree_max_full_lod\":{t.treeMaximumFullLODCount}," +
                       $"\"instanced\":{B(t.drawInstanced)},\"shadows\":{S(t.shadowCastingMode.ToString())},\"tree_instances\":{(d ? d.treeInstanceCount : 0)},\"layer\":{S(LayerName(t.gameObject.layer))}" + "}";
            })) + "],");
            sb.Append($"\"expected_issue9\":{{\"basemap\":250,\"trees\":450,\"billboards\":80,\"details\":70,\"pixel_error\":5,\"tree_max_full_lod\":50,\"instanced\":true}},");
            bool match = terrains.Count > 0 && terrains.All(t => Mathf.Approximately(t.basemapDistance, 250f) && Mathf.Approximately(t.treeDistance, 450f) && Mathf.Approximately(t.treeBillboardDistance, 80f)
                && Mathf.Approximately(t.detailObjectDistance, 70f) && Mathf.Approximately(t.heightmapPixelError, 5f) && t.treeMaximumFullLODCount == 50 && t.drawInstanced);
            sb.Append($"\"all_tiles_match_issue9\":{B(match)}}},");

            // Inventory.
            var activeR = renderers.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            sb.Append($"\"renderers\":{{\"total\":{renderers.Count},\"active\":{activeR.Count},");
            sb.Append("\"by_layer\":{" + string.Join(",", renderers.GroupBy(r => r.gameObject.layer).OrderBy(g => g.Key).Select(g =>
                $"{S(LayerName(g.Key))}:{{\"total\":{g.Count()},\"active\":{g.Count(r => r.enabled && r.gameObject.activeInHierarchy)},\"shadow_casting\":{g.Count(r => r.shadowCastingMode != ShadowCastingMode.Off)}," +
                $"\"types\":{{{string.Join(",", g.GroupBy(r => r.GetType().Name).Select(t => S(t.Key) + ":" + t.Count()))}}}," +
                $"\"top_roots\":{{{string.Join(",", g.GroupBy(r => r.transform.root.name).OrderByDescending(x => x.Count()).Take(8).Select(x => S(x.Key) + ":" + x.Count()))}}}" + "}")) + "},");
            sb.Append("\"by_root\":{" + string.Join(",", roots.Select(go => (go, rs: go.GetComponentsInChildren<Renderer>(true))).Where(x => x.rs.Length > 0).OrderByDescending(x => x.rs.Length).Select(x =>
                $"{S(x.go.name)}:{{\"renderers\":{x.rs.Length},\"active_root\":{B(x.go.activeSelf)},\"layers\":{{{string.Join(",", x.rs.GroupBy(r => r.gameObject.layer).Select(g => S(LayerName(g.Key)) + ":" + g.Count()))}}}" + "}")) + "},");
            sb.Append("\"static_flags\":{" +
                $"\"batching_static\":{renderers.Count(r => GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic))}," +
                $"\"occluder_static\":{renderers.Count(r => GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccluderStatic))}," +
                $"\"occludee_static\":{renderers.Count(r => GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccludeeStatic))}," +
                $"\"any_static\":{renderers.Count(r => r.gameObject.isStatic)}" + "}},");

            // Animal roots: what exists at edit time (populations spawn at run time; FrustumProbe counts them live).
            sb.Append("\"population_roots\":{" + string.Join(",", new[] { "Wildlife", "Birds", "Fish" }.Select(n =>
            {
                var go = roots.FirstOrDefault(r => r.name == n);
                if (!go) return $"{S(n)}:null";
                var rs = go.GetComponentsInChildren<Renderer>(true);
                var comps = go.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m).Select(m => m.GetType().Name).Distinct().Take(12);
                return $"{S(n)}:{{\"renderers_at_edit_time\":{rs.Length},\"layers\":{{{string.Join(",", rs.GroupBy(r => r.gameObject.layer).Select(g => S(LayerName(g.Key)) + ":" + g.Count()))}}},\"root_layer\":{S(LayerName(go.layer))},\"components\":[{string.Join(",", comps.Select(S))}]}}";
            })) + "},");

            // LOD coverage.
            var groups = roots.SelectMany(r => r.GetComponentsInChildren<LODGroup>(true)).ToList();
            var inLod = new HashSet<Renderer>(groups.SelectMany(g => g.GetLODs().SelectMany(l => l.renderers)).Where(r => r));
            var meshActive = activeR.Where(r => r is MeshRenderer).ToList();
            sb.Append($"\"lod\":{{\"groups\":{groups.Count},\"groups_enabled\":{groups.Count(g => g.enabled && g.gameObject.activeInHierarchy)},\"renderers_in_groups\":{inLod.Count}," +
                      $"\"active_mesh_renderers\":{meshActive.Count},\"active_mesh_renderers_without_lod\":{meshActive.Count(r => !inLod.Contains(r))}," +
                      $"\"without_lod_by_layer\":{{{string.Join(",", meshActive.Where(r => !inLod.Contains(r)).GroupBy(r => r.gameObject.layer).Select(g => S(LayerName(g.Key)) + ":" + g.Count()))}}}," +
                      $"\"last_transition_heights\":{{{string.Join(",", groups.Select(g => g.GetLODs()).Where(l => l.Length > 0).GroupBy(l => F(l[l.Length - 1].screenRelativeTransitionHeight)).Select(g => S(g.Key) + ":" + g.Count()))}}}" + "},");

            // Q4: oversized bounds.
            var big = renderers.Where(r => { var s = r.bounds.size; return s.x > Oversize || s.y > Oversize || s.z > Oversize; }).OrderByDescending(r => r.bounds.size.magnitude).ToList();
            sb.Append("\"oversized\":{\"threshold_m\":" + F(Oversize) + ",\"views\":" + views.Count + ",\"count\":" + big.Count + ",\"renderers\":[");
            var rows = new List<string>();
            int defeated = 0;
            foreach (var r in big)
            {
                var b = r.bounds;
                var hitBy = views.Where(v => GeometryUtility.TestPlanesAABB(v.planes, b)).ToList();
                float share = views.Count > 0 ? hitBy.Count / (float)views.Count : 0f;
                var spotsHit = hitBy.Select(v => v.name).Distinct().ToList();
                // How much of the box lies past fog end from the nearest spot (by area on the ground plane, sampled on a 16 x 16 grid).
                float nearest = eyes.Count > 0 ? eyes.Min(e => Mathf.Sqrt(b.SqrDistance(e.eye))) : -1f;
                int past = 0, cells = 0;
                for (int ix = 0; ix < 16; ix++)
                    for (int iz = 0; iz < 16; iz++)
                    {
                        var p = new Vector3(Mathf.Lerp(b.min.x, b.max.x, (ix + 0.5f) / 16f), b.center.y, Mathf.Lerp(b.min.z, b.max.z, (iz + 0.5f) / 16f));
                        float dmin = eyes.Min(e => Vector3.Distance(e.eye, p));
                        cells++;
                        if (dmin >= fogEnd) past++;
                    }
                bool defeats = share >= DefeatShare && r.enabled && r.gameObject.activeInHierarchy;
                if (defeats) defeated++;
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf ? mf.sharedMesh : (r is SkinnedMeshRenderer smr ? smr.sharedMesh : null);
                bool lod = inLod.Contains(r);
                string why = !r.enabled || !r.gameObject.activeInHierarchy ? "inactive or disabled at edit time: never drawn unless enabled at run time"
                    : defeats ? $"box {F(b.size.x)} x {F(b.size.z)} m meets the frustum in {hitBy.Count}/{views.Count} protocol views ({spotsHit.Count} of {eyes.Count} places), so it is submitted from almost anywhere; {F(100.0 * past / cells)}% of its ground footprint is past fog end ({F(fogEnd)} m) from every place" +
                                (mesh ? $"; its {mesh.vertexCount} vertices are drawn whole each time" : "")
                    : $"meets the frustum in {hitBy.Count}/{views.Count} views: culled from most places";
                rows.Add($"{{\"name\":{S(r.name)},\"path\":{S(HierarchyPath(r.transform))},\"type\":{S(r.GetType().Name)},\"active\":{B(r.enabled && r.gameObject.activeInHierarchy)}," +
                         $"\"bounds_size\":{V(b.size)},\"bounds_center\":{V(b.center)},\"layer\":{S(LayerName(r.gameObject.layer))},\"shadow_mode\":{S(r.shadowCastingMode.ToString())}," +
                         $"\"static\":{B(r.gameObject.isStatic)},\"in_lod_group\":{B(lod)},\"mesh\":{S(mesh ? mesh.name : "")},\"vertices\":{(mesh ? mesh.vertexCount : 0)}," +
                         $"\"triangles\":{(mesh && mesh.isReadable ? mesh.triangles.Length / 3 : -1)},\"submeshes\":{(mesh ? mesh.subMeshCount : 0)}," +
                         $"\"materials\":[{string.Join(",", r.sharedMaterials.Where(m => m).Select(m => S(m.name + " (" + (m.shader ? m.shader.name : "no shader") + ")")))}]," +
                         $"\"views_hit\":{hitBy.Count},\"view_share\":{F(share)},\"places_hit\":[{string.Join(",", spotsHit.Select(S))}],\"nearest_place_m\":{F(nearest)}," +
                         $"\"footprint_past_fog_end_share\":{F(past / (float)cells)},\"defeats_culling\":{B(defeats)},\"why\":{S(why)}" + "}");
            }
            sb.Append(string.Join(",", rows) + "],\"defeating_culling\":" + defeated + "},");
            sb.Append("\"places\":[" + string.Join(",", eyes.Select(e => $"{{\"name\":{S(e.name)},\"eye\":{V(e.eye)}}}")) + "],");
            sb.Append($"\"cab_height\":{F(cabHeight)}");
            sb.Append("}");

            var outPath = Path.Combine(root, Out);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString().Replace("\r", "") + "\n");
            var after = File.GetLastWriteTimeUtc(sceneFile);
            if (after != before) throw new System.InvalidOperationException("World.unity changed on disk during the census");
            return $"written {Out}: renderers={renderers.Count} active={activeR.Count} oversized={big.Count} defeating={defeated} tiles={terrains.Count} tiles_match_issue9={match} lod_groups={groups.Count}";
        }

        static string B(bool b) => b ? "true" : "false";

        static float Ground(List<Terrain> tiles, float x, float z)
        {
            foreach (var t in tiles)
            {
                var d = t.terrainData;
                if (!d) continue;
                var o = t.transform.position;
                if (x >= o.x && x <= o.x + d.size.x && z >= o.z && z <= o.z + d.size.z) return t.SampleHeight(new Vector3(x, 0f, z)) + o.y;
            }
            return 0f;
        }

        static string Urp()
        {
            var def = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            var levels = new List<string>();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i) as UniversalRenderPipelineAsset;
                var applied = a ? a : def;
                levels.Add($"{{\"level\":{i},\"name\":{S(QualitySettings.names[i])},\"quality_asset\":{S(a ? a.name : "")},\"applied_asset\":{S(applied ? applied.name : "")}," +
                           $"\"applied_asset_path\":{S(applied ? AssetDatabase.GetAssetPath(applied) : "")}," +
                           $"\"shadow_distance\":{F(applied ? applied.shadowDistance : -1f)},\"shadow_cascades\":{(applied ? applied.shadowCascadeCount : -1)}," +
                           $"\"shadowmap_resolution\":{(applied ? applied.mainLightShadowmapResolution : -1)},\"main_light_shadows\":{B(applied && applied.supportsMainLightShadows)}," +
                           $"\"gpu_resident_drawer\":{S(applied ? applied.gpuResidentDrawerMode.ToString() : "")}" + "}");
            }
            return $"\"urp\":{{\"default_asset\":{S(def ? def.name : "")},\"current_quality\":{QualitySettings.GetQualityLevel()},\"quality_settings_shadow_distance_ignored_by_urp\":{F(QualitySettings.shadowDistance)}," +
                   $"\"lod_bias\":{F(QualitySettings.lodBias)},\"maximum_lod_level\":{QualitySettings.maximumLODLevel},\"levels\":[{string.Join(",", levels)}]}}";
        }
    }
}
