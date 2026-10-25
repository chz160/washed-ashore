using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WashedAshore.Lookout.Editor
{
    /// <summary>
    /// One rerunnable script from the Blender export to a tower the ordinary PlayerController can walk, placed in World.unity.
    ///   Copy       the export (FBX, manifest, textures) into Assets/World/Lookout; files already identical are left alone
    ///   Import     model and texture import settings, set here (no AssetPostprocessor, so nothing else reimports)
    ///   Prefab     LookoutTower.prefab: materials, LODGroup, colliders from the FBX proxies, a smooth ramp collider per
    ///              flight along the manifest's nosing line, and TowerWalkZones (slim capsule, raised slope limit)
    ///   Place      BellsBendTowerSite.Place() puts it on the Central Ridge Summit pad; the player's spawn is pointed at the base
    /// The guided-climb scheme is not used: no ClimbArea is added. Proxies that stop a walking body are dropped by name
    /// (see <see cref="Dropped"/>) and the hatch is left open. Run:
    ///   Unity -batchmode -projectPath E:\GitHub\washed-ashore -executeMethod WashedAshore.Lookout.Editor.WalkableTowerBuilder.BuildBatch -quit
    /// or the menu Washed Ashore/Lookout/Build walkable tower. LOOKOUT_EXPORT_DIR overrides the export folder.
    /// </summary>
    public static class WalkableTowerBuilder
    {
        /// <summary>
        /// The convex tower colliders' cooking options, written out (#45 M3). It is the value the default has always given
        /// (30 in the saved prefab), so the cooked hulls and the walked shape do not change.
        /// </summary>
        public const MeshColliderCookingOptions TowerCooking = MeshColliderCookingOptions.CookForFasterSimulation
            | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;

        public const string Folder = "Assets/World/Lookout";
        public const string ModelPath = Folder + "/MC39_Lookout.fbx";
        public const string PrefabPath = Folder + "/LookoutTower.prefab";
        public const string WorldScene = "Assets/Scenes/World.unity";
        public const string DefaultExportDir = @"C:\Users\noah\AppData\Local\Temp\claude\C--Tools-ruflo-studio\d30412c9-b209-4b7a-8cb4-af7df6b6cb3b\scratchpad\t-artist\h\shut3\out_SHUT";
        public const string LogPath = "Logs/walkable-tower.txt";
        const string Stamp = "walkable-v1";
        const float BoxTolerance = 1e-4f;
        // Ramp tunables (metres): width = clear width between the flight side walls; thickness only has to stop tunnelling.
        const float NudgeMax = 0.3f, WallWiden = 0.05f, FloorOverhangTrim = 0.25f;
        const float RampWidth = 0.70f, RampThickness = 0.30f, ZoneFootprintMargin = 0.6f;

        static readonly string[] Files =
        {
            "MC39_Lookout.fbx", "MC39_Lookout.manifest.json", "MC39_Lookout_Albedo.png", "MC39_Lookout_Mask.png",
            "MC39_Lookout_Normal.png", "TEXTURES_LICENSE.txt",
        };

        /// <summary>Proxies removed because they stop a walking body (the guided climb needed them): the thin gate across each
        /// stair mouth, the guided-climb stair solids (replaced by the ramps), the closed hatch leaf and its swing volume, and the two lower diagonal braces on
        /// the west face (the X leaves a door under 0.6 m wide at head height, so the base of flight 1 could not be reached).</summary>
        public static bool Dropped(string name) =>
            name.StartsWith("CLIP_BOX_Gate_", StringComparison.Ordinal) || name.StartsWith("COL_BOX_Stair_", StringComparison.Ordinal)
            || name == "COL_BOX_Hatch_01" || name == "CLIP_BOX_HatchSwing_01"
            || name == "COL_BOX_Brace_13" || name == "COL_BOX_Brace_14";

        [Serializable] class Obj { public string name, role; public string[] materials; }
        [Serializable] class Objs { public Obj[] objects; }
        [Serializable] class Fl { public int index; public float[] foot, head; }
        [Serializable] class Fls { public Fl[] flights; }
        [Serializable] class HatchData { public float[] hinge_axis; public float open_angle_deg; public string name; }
        [Serializable] class Extra { public HatchData hatch; public float[] area_min, area_max; }

        [MenuItem("Washed Ashore/Lookout/Build walkable tower")]
        static void BuildMenu() => Debug.Log(BuildAndPlace());

        public static void BuildBatch()
        {
            try
            {
                string log = BuildAndPlace();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(LogPath)));
                File.WriteAllText(LogPath, log + "\ndone\n");
                Debug.Log(log);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(LogPath)));
                File.WriteAllText(LogPath, "FAILED: " + e);
                Debug.LogError(e);
                EditorApplication.Exit(1);
            }
        }

        public static string BuildAndPlace()
        {
            var log = new StringBuilder();
            log.AppendLine(Build());
            EditorSceneManager.OpenScene(WorldScene, OpenSceneMode.Single);
            var site = Type.GetType("BellsBendTowerSite, WashedAshore.Level.Editor");
            if (site == null) throw new InvalidOperationException("BellsBendTowerSite not found (assembly WashedAshore.Level.Editor)");
            log.AppendLine((string)site.GetMethod("Place").Invoke(null, null));
            log.AppendLine(SpawnAtTower(true));
            return log.ToString();
        }

        /// <summary>
        /// TowerBaseSpawn, always recreated here so a rerun gives the same start: 4.6 m out from the tower's west face on the line
        /// to the foot of flight 1, facing the foot. BellsBendTowerSite.PlaceSpawn searches for a clear spot but finds none now the
        /// proxies are solid; the west face is the one whose diagonal braces are dropped, so the walk in is clear.
        /// </summary>
        static void PlaceBaseSpawn(UnityEngine.SceneManagement.Scene scene)
        {
            var towerRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "LookoutTower");
            if (!towerRoot) return;
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == "TowerBaseSpawn").ToList()) UnityEngine.Object.DestroyImmediate(old);
            var foot = towerRoot.GetComponentsInChildren<Transform>(true).First(t => t.name == "LANE_F01_Foot").position;
            Vector3 p = towerRoot.transform.TransformPoint(new Vector3(-3.98f, 0f, -2.17f));
            p.y = WashedAshore.Gameplay.TerrainQuery.TryGroundHeight(p, out float ground) ? ground : towerRoot.transform.position.y;
            Vector3 look = foot - p; look.y = 0f;
            var go = new GameObject("TowerBaseSpawn");
            go.transform.SetPositionAndRotation(p, Quaternion.LookRotation(look, Vector3.up));
        }

        /// <summary>Points the Player's spawn at TowerBaseSpawn (true) or back at PlayerSpawn (false), in the open World scene, and saves it.</summary>
        public static string SpawnAtTower(bool tower)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (tower) PlaceBaseSpawn(scene);
            var all = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
            var want = all.FirstOrDefault(t => t.name == (tower ? "TowerBaseSpawn" : "PlayerSpawn"));
            var player = GameObject.FindGameObjectWithTag("Player");
            if (!want || !player) return $"spawn: {(tower ? "TowerBaseSpawn" : "PlayerSpawn")} or Player not found; unchanged";
            var so = new SerializedObject(player.GetComponent("PlayerController"));
            so.FindProperty("spawnPoint").objectReferenceValue = want;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"spawn: Player.spawnPoint -> {want.name} at {want.position:F2}";
        }

        [MenuItem("Washed Ashore/Lookout/Spawn at tower base")] static void SpawnTowerMenu() { EditorSceneManager.OpenScene(WorldScene); Debug.Log(SpawnAtTower(true)); }
        [MenuItem("Washed Ashore/Lookout/Spawn at normal start")] static void SpawnNormalMenu() { EditorSceneManager.OpenScene(WorldScene); Debug.Log(SpawnAtTower(false)); }

        /// <summary>Copy, import settings, prefab. Returns the log.</summary>
        public static string Build()
        {
            var log = new StringBuilder($"WalkableTowerBuilder {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            string src = Environment.GetEnvironmentVariable("LOOKOUT_EXPORT_DIR");
            if (string.IsNullOrEmpty(src)) src = DefaultExportDir;
            Directory.CreateDirectory(Folder);
            foreach (var f in Files)
            {
                string from = Path.Combine(src, f), to = Path.Combine(Folder, f);
                if (!File.Exists(from)) { if (File.Exists(to)) continue; throw new FileNotFoundException("export file missing: " + from); }
                if (File.Exists(to) && File.ReadAllBytes(to).SequenceEqual(File.ReadAllBytes(from))) continue;
                File.Copy(from, to, true);
                log.AppendLine("copied " + f);
            }
            AssetDatabase.Refresh();
            ConfigureImports(log);

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) throw new InvalidOperationException(ModelPath + " did not import as a model");
            string json = File.ReadAllText(Folder + "/MC39_Lookout.manifest.json");
            var roles = JsonUtility.FromJson<Objs>(json);
            var flights = JsonUtility.FromJson<Fls>(json).flights;
            var extra = JsonUtility.FromJson<Extra>(json);

            var root = UnityEngine.Object.Instantiate(model);
            try
            {
                root.name = "LookoutTower";
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var byName = new Dictionary<string, Transform>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t != root.transform) byName[t.name] = t;

                var foot = byName["LANE_F01_Foot"].position;
                var wantFoot = new Vector3(flights[0].foot[0], flights[0].foot[1], flights[0].foot[2]);
                if ((foot - wantFoot).magnitude > 0.01f) throw new InvalidOperationException($"LANE_F01_Foot imported at {foot}, manifest says {wantFoot}: axis/scale import is wrong");

                var materials = LookoutTowerArt.EnsureMaterials();
                foreach (var o in roles.objects)
                {
                    if (o.materials == null || o.materials.Length == 0 || !byName.TryGetValue(o.name, out var node)) continue;
                    var r = node.GetComponent<MeshRenderer>();
                    if (!r) continue;
                    r.sharedMaterials = o.materials.Select(n => materials[n]).ToArray();
                }
                LookoutTowerArt.ApplyLods(root);

                int boxes = 0, convex = 0; var dropped = new List<string>();
                foreach (var t in byName.Values.OrderBy(x => x.name, StringComparer.Ordinal).ToList())
                {
                    bool box = t.name.StartsWith("COL_BOX_") || t.name.StartsWith("CLIP_BOX_");
                    bool cvx = t.name.StartsWith("COL_CVX_") || t.name.StartsWith("CLIP_CVX_");
                    if (!box && !cvx) continue;
                    if (Dropped(t.name)) { dropped.Add(t.name); UnityEngine.Object.DestroyImmediate(t.gameObject); continue; }
                    var filter = t.GetComponent<MeshFilter>(); var mesh = filter.sharedMesh;
                    if (box)
                    {
                        var corners = new List<Vector3>();
                        foreach (var v in mesh.vertices) if (!corners.Any(o => (o - v).sqrMagnitude < BoxTolerance * BoxTolerance)) corners.Add(v);
                        if (!BoxFrame(corners, out var centre, out var rot, out var size)) throw new InvalidOperationException(t.name + " is not a rectangular box");
                        t.localPosition += t.localRotation * centre;
                        t.localRotation *= rot;
                        var bc = t.gameObject.AddComponent<BoxCollider>(); bc.center = Vector3.zero; bc.size = size;   // the Editor auto-fits a new collider to the renderer: reset
                        UnityEngine.Object.DestroyImmediate(t.GetComponent<MeshRenderer>());
                        UnityEngine.Object.DestroyImmediate(filter);
                        boxes++;
                    }
                    else
                    {
                        var mc = t.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.convex = true; mc.cookingOptions = TowerCooking;
                        UnityEngine.Object.DestroyImmediate(t.GetComponent<MeshRenderer>());
                        convex++;
                    }
                    t.gameObject.layer = 0;
                }
                log.AppendLine(DropTipPosts(byName));
                log.AppendLine(NudgeLandingWalls(byName));
                log.AppendLine(WidenFlightWalls(byName, flights));
                log.AppendLine(TrimFloorOverStair(byName));
                log.AppendLine($"colliders: {boxes} box + {convex} convex kept; dropped {dropped.Count}: {string.Join(", ", dropped)}");

                // One smooth ramp per flight along the nosing line (foot -> head): the stair-volume solution.
                var rampRoot = new GameObject("WALK_Ramps").transform; rampRoot.SetParent(root.transform, false);
                var zones = new List<TowerWalkZones.Flight>();
                foreach (var f in flights)
                {
                    Vector3 a = new Vector3(f.foot[0], f.foot[1], f.foot[2]), b = new Vector3(f.head[0], f.head[1], f.head[2]);
                    var ramp = new GameObject($"WALK_Ramp_F{f.index:00}").transform; ramp.SetParent(rampRoot, false);
                    Quaternion rot = Quaternion.LookRotation((b - a).normalized, Vector3.up);
                    ramp.localRotation = rot;
                    ramp.localPosition = (a + b) * 0.5f - rot * Vector3.up * (RampThickness * 0.5f);
                    var bc = ramp.gameObject.AddComponent<BoxCollider>();
                    bc.size = new Vector3(RampWidth, RampThickness, (b - a).magnitude);
                    zones.Add(new TowerWalkZones.Flight { foot = a, head = b });
                }
                log.AppendLine($"ramps: {flights.Length} (width {RampWidth}, thickness {RampThickness})");

                var walk = root.AddComponent<TowerWalkZones>();
                walk.flights = zones.ToArray();
                walk.footprintMin = new Vector3(extra.area_min[0] - ZoneFootprintMargin, -1f, extra.area_min[2] - ZoneFootprintMargin);
                walk.footprintMax = new Vector3(extra.area_max[0] + ZoneFootprintMargin, 30f, extra.area_max[2] + ZoneFootprintMargin);

                // Hatch left open (visual): the leaf swings up about its hinge; its collider and swing volume were dropped above.
                if (byName.TryGetValue(extra.hatch.name, out var leaf) && leaf)
                {
                    var axis = new Vector3(extra.hatch.hinge_axis[0], extra.hatch.hinge_axis[1], extra.hatch.hinge_axis[2]);
                    leaf.localRotation = Quaternion.AngleAxis(extra.hatch.open_angle_deg, axis);
                    log.AppendLine($"hatch: {leaf.name} left open ({extra.hatch.open_angle_deg:F1} deg)");
                }

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (!saved) throw new InvalidOperationException("could not save " + PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            log.AppendLine("prefab: " + PrefabPath);
            return log.ToString();
        }

        /// <summary>
        /// The small post at the tip of each landing's divider (CLIP_BOX_LandingNN_xx, a 0.15 m wide rail piece on the x = z diagonal) catches a
        /// capsule going round the hairpin: the controller rubs its corner and pops up a step. The divider's own two wall hulls end
        /// beside it with a slit under 0.15 m, so nothing can pass through where the post was. Removed (colliders only).
        /// </summary>
        static string DropTipPosts(Dictionary<string, Transform> byName)
        {
            var dropped = new List<string>();
            foreach (var kv in byName.Where(k => k.Key.StartsWith("CLIP_BOX_Landing", StringComparison.Ordinal)).ToList())
            {
                if (!kv.Value) continue;
                var box = kv.Value.GetComponent<BoxCollider>();
                Vector3 c = kv.Value.position;
                if (!box || box.size.x > 0.2f || box.size.z > 0.2f || Mathf.Abs(c.x - c.z) > 0.02f) continue;
                dropped.Add(kv.Key);
                UnityEngine.Object.DestroyImmediate(kv.Value.gameObject);
            }
            return $"landing tip posts dropped: {string.Join(", ", dropped.OrderBy(n => n, StringComparer.Ordinal))}";
        }

        /// <summary>
        /// The landing wall proxies sit on the rail line, 0.17 to 0.19 m inside the deck edge. On the small hairpin landings that
        /// leaves a gap of about 0.45 m round the end of the divider, narrower than the capsule (0.5 m), so a walking body cannot
        /// turn. Each axis-aligned thin wall within <see cref="NudgeMax"/> of its deck edge is moved out to the edge (it still stops
        /// a fall). Diagonal walls and posts are left alone.
        /// </summary>
        static string NudgeLandingWalls(Dictionary<string, Transform> byName)
        {
            var moved = new List<string>();
            foreach (var kv in byName)
            {
                var m = System.Text.RegularExpressions.Regex.Match(kv.Key, @"^CLIP_BOX_Landing(\d\d)_\d\d$");
                if (!m.Success || !kv.Value) continue;
                var box = kv.Value.GetComponent<BoxCollider>();
                if (!box || !byName.TryGetValue($"COL_CVX_Landing_{m.Groups[1].Value}", out var deckNode)) continue;
                var deck = deckNode.GetComponent<MeshCollider>().sharedMesh.bounds;
                if (Quaternion.Angle(kv.Value.rotation, Quaternion.identity) > 0.1f) continue;
                Vector3 c = kv.Value.position, size = box.size;
                for (int axis = 0; axis < 3; axis += 2)   // x and z
                {
                    if (size[axis] > 0.05f || size[2 - axis] < 0.2f) continue;   // a wall thin on this axis and long on the other
                    float lo = deck.min[axis], hi = deck.max[axis];
                    float target = float.NaN;
                    if (c[axis] > hi - NudgeMax && c[axis] < hi - 0.01f) target = hi - size[axis] * 0.5f;
                    else if (c[axis] < lo + NudgeMax && c[axis] > lo + 0.01f) target = lo + size[axis] * 0.5f;
                    if (float.IsNaN(target)) continue;
                    moved.Add($"{kv.Key} {c[axis]:F3}->{target:F3} ({(axis == 0 ? "x" : "z")})");
                    c[axis] = target;
                }
                kv.Value.position = c;
            }
            return $"landing walls moved out to the deck edge: {moved.Count}: {string.Join(", ", moved)}";
        }

        /// <summary>
        /// The two side-wall hulls of a flight stand 0.72 m apart (0.62 on flight 8). A character controller needs its diameter
        /// plus two skin widths (0.5 + 0.16 with the player's 0.08 skin), so flight 8 cannot be entered and the others have 3 cm
        /// to spare. Each hull is moved <see cref="WallWiden"/> straight out from the flight's centre line. Colliders only.
        /// </summary>
        static string WidenFlightWalls(Dictionary<string, Transform> byName, Fl[] flights)
        {
            var moved = new List<string>();
            foreach (var f in flights)
            {
                Vector3 foot = new Vector3(f.foot[0], f.foot[1], f.foot[2]), head = new Vector3(f.head[0], f.head[1], f.head[2]);
                Vector3 run = head - foot; run.y = 0f; run.Normalize();
                Vector3 perp = new Vector3(run.z, 0f, -run.x), mid = (foot + head) * 0.5f;
                foreach (int k in new[] { 1, 2 })
                {
                    string name = $"CLIP_CVX_Flight{f.index:00}_{k:00}";
                    if (!byName.TryGetValue(name, out var node) || !node) continue;
                    var c = node.GetComponent<MeshCollider>().sharedMesh.bounds.center;
                    float side = Mathf.Sign(Vector3.Dot(c - mid, perp));
                    node.position += perp * (side * WallWiden);
                    moved.Add(name);
                }
            }
            return $"flight side walls moved {WallWiden} m out from the centre line: {moved.Count}";
        }

        /// <summary>
        /// The cab floor strip north of the hatch (COL_BOX_CabFloor_03) overhangs the foot of flight 8, so a capsule climbing or
        /// descending that 56 degree ramp touches its underside: a round body rides about 0.2 m above the ramp line and the strip
        /// edge is 0.2 m above a 1.6 m head. Its south edge is pulled back <see cref="FloorOverhangTrim"/> (the capsule radius),
        /// which widens the hatch opening northward by that much. Collider only; the boards are unchanged.
        /// </summary>
        static string TrimFloorOverStair(Dictionary<string, Transform> byName)
        {
            if (!byName.TryGetValue("COL_BOX_CabFloor_03", out var t) || !t) return "cab floor strip: COL_BOX_CabFloor_03 not found";
            var box = t.GetComponent<BoxCollider>();
            float south = t.position.z - box.size.z * 0.5f;
            box.size = new Vector3(box.size.x, box.size.y, box.size.z - FloorOverhangTrim);
            t.position += new Vector3(0f, 0f, FloorOverhangTrim * 0.5f);
            return $"cab floor strip COL_BOX_CabFloor_03: south edge z {south:F3} -> {south + FloorOverhangTrim:F3}";
        }

        /// <summary>Every kept proxy's collider bounds against the FBX mesh bounds it came from (5 mm). Returns the mismatches, empty when all agree.</summary>
        public static string Verify()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var prefab = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var sb = new StringBuilder();
            try
            {
                Physics.SyncTransforms();
                var src = model.GetComponentsInChildren<MeshFilter>(true).ToDictionary(f => f.gameObject.name, f => f.sharedMesh.bounds);
                int checkedCount = 0;
                foreach (var c in prefab.GetComponentsInChildren<Collider>(true))
                {
                    if (!src.TryGetValue(c.name, out var b)) continue;
                    checkedCount++;
                    if ((c.bounds.min - b.min).magnitude > 0.005f || (c.bounds.max - b.max).magnitude > 0.005f)
                        sb.AppendLine($"MISMATCH {c.name}: collider {c.bounds.min:F3}..{c.bounds.max:F3} mesh {b.min:F3}..{b.max:F3}");
                }
                sb.Insert(0, $"verify: {checkedCount} proxy colliders compared\n");
            }
            finally { UnityEngine.Object.DestroyImmediate(prefab); }
            return sb.ToString();
        }

        public static void VerifyBatch()
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/walkable-verify.txt", Verify());
            EditorApplication.Exit(0);
        }

        static void ConfigureImports(StringBuilder log)
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            if (mi.userData != Stamp)
            {
                mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true; mi.preserveHierarchy = true;
                mi.sortHierarchyByName = true; mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false;
                mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.importVisibility = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.addCollider = false;
                mi.isReadable = true; mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.optimizeMeshPolygons = false; mi.optimizeMeshVertices = false; mi.weldVertices = true; mi.generateSecondaryUV = false;
                mi.userData = Stamp; mi.SaveAndReimport();
                log.AppendLine("model import settings applied");
            }
            foreach (var tex in new[] { "Albedo", "Mask", "Normal" })
            {
                string path = $"{Folder}/MC39_Lookout_{tex}.png";
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                if (ti.userData == Stamp) continue;
                ti.mipmapEnabled = true; ti.maxTextureSize = 2048; ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4;
                if (tex == "Normal") ti.textureType = TextureImporterType.NormalMap;
                else
                {
                    ti.textureType = TextureImporterType.Default; ti.sRGBTexture = tex != "Mask";
                    ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = tex == "Albedo";
                }
                ti.userData = Stamp; ti.SaveAndReimport();
                log.AppendLine("texture import settings applied: " + tex);
            }
        }

        /// <summary>
        /// The frame of a rectangular box from its 8 corners (any order): three mutually perpendicular edges at corner 0
        /// that add up to the opposite corner, with every corner on the lattice they span. The rotation is the one
        /// closest to identity, so an axis-aligned box keeps an unrotated node.
        /// </summary>
        public static bool BoxFrame(IList<Vector3> c, out Vector3 centre, out Quaternion rotation, out Vector3 size)
        {
            centre = Vector3.zero; rotation = Quaternion.identity; size = Vector3.zero;
            if (c.Count != 8) return false;
            int far = 1;
            for (int i = 2; i < 8; i++) if ((c[i] - c[0]).sqrMagnitude > (c[far] - c[0]).sqrMagnitude) far = i;
            var diagonal = c[far] - c[0];
            var others = Enumerable.Range(1, 7).Where(i => i != far).ToArray();
            for (int i = 0; i < others.Length; i++)
                for (int j = i + 1; j < others.Length; j++)
                    for (int k = j + 1; k < others.Length; k++)
                    {
                        Vector3 a = c[others[i]] - c[0], b = c[others[j]] - c[0], d = c[others[k]] - c[0];
                        if ((a + b + d - diagonal).magnitude > BoxTolerance) continue;
                        if (Mathf.Abs(Vector3.Dot(a, b)) > BoxTolerance * a.magnitude * b.magnitude + BoxTolerance * BoxTolerance) continue;
                        if (Mathf.Abs(Vector3.Dot(a, d)) > BoxTolerance * a.magnitude * d.magnitude + BoxTolerance * BoxTolerance) continue;
                        if (Mathf.Abs(Vector3.Dot(b, d)) > BoxTolerance * b.magnitude * d.magnitude + BoxTolerance * BoxTolerance) continue;
                        // Every corner must be c0 plus a subset of the three edges.
                        bool lattice = true;
                        for (int m = 0; m < 8 && lattice; m++)
                        {
                            bool found = false;
                            for (int bits = 0; bits < 8 && !found; bits++)
                            {
                                var p = c[0] + ((bits & 1) != 0 ? a : Vector3.zero) + ((bits & 2) != 0 ? b : Vector3.zero) + ((bits & 4) != 0 ? d : Vector3.zero);
                                found = (p - c[m]).magnitude <= BoxTolerance;
                            }
                            lattice = found;
                        }
                        if (!lattice) continue;

                        centre = c[0] + diagonal * 0.5f;
                        // Assign the edges to X, Y, Z by best alignment, signs positive, so the rotation is minimal.
                        var edges = new[] { a, b, d };
                        var axis = new Vector3[3]; var length = new float[3];
                        var used = new bool[3];
                        for (int world = 0; world < 3; world++)
                        {
                            var unit = world == 0 ? Vector3.right : world == 1 ? Vector3.up : Vector3.forward;
                            int best = -1; float bestDot = -1f;
                            for (int e = 0; e < 3; e++)
                            {
                                if (used[e]) continue;
                                float dot = Mathf.Abs(Vector3.Dot(edges[e].normalized, unit));
                                if (dot > bestDot) { bestDot = dot; best = e; }
                            }
                            used[best] = true;
                            length[world] = edges[best].magnitude;
                            axis[world] = edges[best].normalized * (Vector3.Dot(edges[best], unit) < 0f ? -1f : 1f);
                        }
                        // A box is symmetric, so Z can be rebuilt from X and Y to keep the frame right-handed.
                        var z = Vector3.Cross(axis[0], axis[1]).normalized;
                        rotation = Quaternion.LookRotation(z, axis[1]);
                        size = new Vector3(length[0], length[1], length[2]);
                        return true;
                    }
            return false;
        }
    }
}
