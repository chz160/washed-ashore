using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WashedAshore.Fish.Rendering;
using WashedAshore.Gameplay;
using WashedAshore.Level;

namespace WashedAshore.Fish.Editor
{
    // Phase 1 look-dev: f-director's conditions L1 (gar as a V2 shadow), L3 (largemouth fins keep their olive/brown swatch)
    // and L4 (soft, irregular, expanding, fading rings) shot through the production path: the baked VAT mesh and material
    // from FishRenderSet, drawn with Graphics.RenderMeshInstanced and the same per-instance properties FishRenderer sets.
    // Output TestResults/fish-lookdev/p1-*.png + p1-log.txt. Same isolated scene as G4 (never World.unity, never in the build).
    public static partial class FishLookDev
    {
        struct Fish { public Matrix4x4 m; public FishRenderSet.Variant v; public float silhouette, flash, phase, amp; }
        struct Sign { public Matrix4x4 m; public float radius, strength, seed, mode, rings; }

        static readonly List<Fish> fishDraws = new List<Fish>();
        static readonly List<Sign> signDraws = new List<Sign>();

        public static void RunPhase1Batch()
        {
            Directory.CreateDirectory(OutDir);
            var path = Path.Combine(OutDir, "p1-log.txt");
            int code = 0;
            try { File.WriteAllText(path, RunPhase1() + "\ndone\n"); }
            catch (System.Exception e) { File.WriteAllText(path, "FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        // #13: the far-water Rise sign (F16) for f-director, through the production ring material: from the shore eye at
        // 40 / 80 m and from the Buzzard Bluff top. Writes p1-rise-*.png and p1-rise-log.txt.
        public static void RunRiseBatch()
        {
            int code = 0;
            var log = new StringBuilder($"Rise stills {System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            try
            {
                var set = AssetDatabase.LoadAssetAtPath<FishRenderSet>(FishVatBake.RenderSetPath);
                log.AppendLine(CheckShaders(Shader.Find(FishShader), Shader.Find(RingShader)));
                var cfg = BellsBendData.LoadConfig();
                float W = cfg.WaterLevelY;
                var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
                var lm = BellsBendData.LoadVectors(cfg).landmarks.ToDictionary(l => l.id, l => new Vector3(l.xz.x, 0f, l.xz.y));
                var scene = BuildScene(new[] { lm["CleecesFerryLanding"], lm["BuzzardBluff"] }, log);
                PushWaterGlobals(scene, W, log);
                var edge = NearestShore(maps, lm["CleecesFerryLanding"], -1f, 0f);
                var outw = Outward(maps, edge);
                var eye = WithY(edge - outw, TerrainQuery.Height(edge - outw) + ShoreEye);
                foreach (var d in new[] { 5f, 15f, 30f, 40f, 60f, 80f })
                {
                    var at = WithY(edge + outw * d, W);
                    foreach (var age in new[] { 0.25f, 0.6f })
                    {
                        AddSign(at, outw, 3f * 0.5f, 0f, 3, age, 17f); // Rise look = Roll: 3 rings, 3 body lengths of a 0.5 m fish
                        log.AppendLine(MeasuredShot(set, $"p1-rise-shore-{d:F0}m-age{age:F2}-fov30.png", "Rise", "shore", d, eye, at, 30f));
                        log.AppendLine(MeasuredShot(set, $"p1-rise-shore-{d:F0}m-age{age:F2}-fov70.png", "Rise", "shore", d, eye, at, 70f));
                        signDraws.Clear();
                    }
                    // A dimple (2 rings, 1.5 body lengths of a 0.2 m fish) at the same spot, mid-life.
                    AddSign(at + outw * 0f, outw, 0.5f, 0f, 2, 0.45f, 11f);
                    log.AppendLine(MeasuredShot(set, $"p1-dimple-shore-{d:F0}m-fov70.png", "Dimple", "shore", d, eye, at, 70f));
                    signDraws.Clear();
                }
                var bluff = lm["BuzzardBluff"];
                var bEdge = NearestShore(maps, bluff, -1f, 0f);
                var bAt = WithY(bEdge + Outward(maps, bEdge) * 60f, W);
                AddSign(bAt, Outward(maps, bEdge), 1.5f, 0f, 3, 0.4f, 29f);
                log.AppendLine(MeasuredShot(set, "p1-rise-bluff-fov30.png", "Rise", "BuzzardBluff", Vector3.Distance(WithY(bluff, TerrainQuery.Height(bluff) + ShoreEye), bAt), WithY(bluff, TerrainQuery.Height(bluff) + ShoreEye), bAt, 30f));
                log.AppendLine(MeasuredShot(set, "p1-rise-bluff-fov70.png", "Rise", "BuzzardBluff", Vector3.Distance(WithY(bluff, TerrainQuery.Height(bluff) + ShoreEye), bAt), WithY(bluff, TerrainQuery.Height(bluff) + ShoreEye), bAt, 70f));
                signDraws.Clear();
                ClearWaterGlobals();
                File.WriteAllText(Path.Combine(OutDir, "p1-rise-log.txt"), log + "done\n");
            }
            catch (System.Exception e) { File.WriteAllText(Path.Combine(OutDir, "p1-rise-log.txt"), "FAILED: " + e); code = 1; }
            EditorApplication.Exit(code);
        }

        // A sign still plus the same frame without signs: peak on-screen height (px rows of changed pixels) and peak contrast
        // (max |luma(sign) - luma(empty)| / mean luma(empty) over the changed pixels). Appends a JSON line to legibility.
        static readonly StringBuilder legibility = new StringBuilder();
        static string MeasuredShot(FishRenderSet set, string file, string kind, string eyeName, float dist, Vector3 eye, Vector3 at, float fov)
        {
            var with = ShotPixels(file, eye, at, fov, cam => Submit(set, cam, at));
            var without = ShotPixels("_empty.png", eye, at, fov, null);
            int W = 1920, H = 1080, minY = H, maxY = -1, n = 0;
            float peak = 0f, sumL = 0f;
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    float l1 = 0.2126f * with[i].r + 0.7152f * with[i].g + 0.0722f * with[i].b;
                    float l0 = 0.2126f * without[i].r + 0.7152f * without[i].g + 0.0722f * without[i].b;
                    if (Mathf.Abs(l1 - l0) < 2f) continue;
                    n++; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    peak = Mathf.Max(peak, Mathf.Abs(l1 - l0)); sumL += l0;
                }
            float meanL = n > 0 ? sumL / n : 1f;
            int height = maxY >= minY ? maxY - minY + 1 : 0;
            File.Delete(Path.Combine(OutDir, "_empty.png"));
            return $"  {file}: {kind} {eyeName} {dist:F0} m fov {fov:F0}: height {height} px, changed {n} px, peak contrast {(n > 0 ? peak / Mathf.Max(meanL, 1f) : 0f):F3}";
        }

        public static string RunPhase1()
        {
            var log = new StringBuilder($"FishLookDev phase 1 {System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            var set = AssetDatabase.LoadAssetAtPath<FishRenderSet>(FishVatBake.RenderSetPath);
            if (!set) throw new FileNotFoundException("run FishVatBake first", FishVatBake.RenderSetPath);
            log.AppendLine(CheckShaders(Shader.Find(FishShader), Shader.Find(RingShader)));
            var cfg = BellsBendData.LoadConfig();
            float W = cfg.WaterLevelY;
            var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
            var lm = BellsBendData.LoadVectors(cfg).landmarks.ToDictionary(l => l.id, l => new Vector3(l.xz.x, 0f, l.xz.y));
            var ferry = lm["CleecesFerryLanding"];
            var scene = BuildScene(new[] { ferry }, log);
            var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(FishVatBake.BodiesPath);
            try
            {
                PushWaterGlobals(scene, W, log);
                var edge = NearestShore(maps, ferry, -1f, 0f);
                var outw = Outward(maps, edge);
                var along = new Vector3(outw.z, 0f, -outw.x);
                var eye = WithY(edge - outw * 1f, TerrainQuery.Height(edge - outw * 1f) + ShoreEye);
                int Var(string id) => System.Array.FindIndex(set.variants, v => v.id == id);

                // f-td 5.4(f) Phase 1 follow-up: per-instance matrices AND per-instance tint through the production VAT material.
                log.AppendLine(TintCheck(set, bodies, Var("LargemouthBass"), WithY(edge + outw * 3f, W + 0.5f), outw, along));

                // L3: largemouth, dry close (side-on, 0.5 m above the water) and at 0.3 m from the shore eye; fin hue measured.
                int lmb = Var("LargemouthBass");
                var dryAt = WithY(edge + outw * 3f, W + 0.5f);
                AddFish(set, bodies, lmb, dryAt, along, 0.375f, 0f, 0f);
                log.AppendLine(ShotWith(set, "p1-L3-largemouth-dry-close.png", dryAt + outw * 1.2f + Vector3.up * 0.05f, dryAt, 30f));
                fishDraws.Clear();
                var wetAt = PlaceAtDepth(bodies, set, lmb, edge, outw, along, 0.375f, 0.3f, W, log);
                log.AppendLine(ShotWith(set, "p1-L3-largemouth-d0.3-shore-fov20.png", eye, wetAt, 20f));
                fishDraws.Clear();

                // f-td 2b: 5-bit oct normals vs the mesh path above water (T = 1, where errors show most): rest pose both ways.
                AddFish(set, bodies, lmb, dryAt, along, 0.375f, 0f, 0f, amp: 0f);
                log.AppendLine(ShotWith(set, "p1-normals-vat-rest.png", dryAt + outw * 1.2f + Vector3.up * 0.05f, dryAt, 30f));
                fishDraws.Clear();
                log.AppendLine(MeshPathShot(set, bodies, lmb, dryAt, along, 0.375f, dryAt + outw * 1.2f + Vector3.up * 0.05f));

                log.AppendLine(MeshPathDiag(set, dryAt, along, outw));

                // L1: gar as a V2 shadow just under the surface (top 5 cm down) and at 0.3 m; shore eye.
                int gar = Var("LongnoseGar");
                foreach (var d in new[] { 0.05f, 0.3f })
                {
                    var at = PlaceAtDepth(bodies, set, gar, edge + along * 1.5f, outw, along, 0.9f, d, W, log, silhouette: 1f);
                    log.AppendLine(ShotWith(set, $"p1-L1-gar-shadow-d{d:F2}-shore-fov20.png", eye, at, 20f));
                    log.AppendLine(ShotWith(set, $"p1-L1-gar-shadow-d{d:F2}-shore-fov60.png", eye, at, 60f));
                    fishDraws.Clear();
                }

                // L4: dimple through its life (ages 0.15 / 0.45 / 0.8), then a rise, a wake and nervous water, from the shore eye.
                var dimpleAt = WithY(edge + outw * 3f, W);
                foreach (var t in new[] { 0.15f, 0.45f, 0.8f })
                {
                    AddSign(dimpleAt, along, 0.5f, 0f, 2, t, 11f);
                    log.AppendLine(ShotWith(set, $"p1-L4-dimple-age{t:F2}-shore-fov20.png", eye, dimpleAt, 20f));
                    signDraws.Clear();
                }
                AddSign(dimpleAt, along, 0.5f, 0f, 2, 0.45f, 11f);
                AddSign(WithY(edge + outw * 5f + along * 1.5f, W), along, 1.2f, 0f, 3, 0.4f, 23f);
                AddSign(WithY(edge + outw * 6f - along * 2.5f, W), along, 1.6f, 1f, 1, 0.3f, 37f);
                AddSign(WithY(edge + outw * 8f + along * 4f, W), along, 3.0f, 2f, 1, 0.5f, 41f);
                log.AppendLine(ShotWith(set, "p1-L4-signs-shore-fov60.png", eye, WithY(edge + outw * 5f, W), 60f));
                log.AppendLine(ShotWith(set, "p1-L4-signs-shore-fov30.png", eye, WithY(edge + outw * 5f, W), 30f));
                signDraws.Clear();
                // Every sign mode side by side from 4 m above (dimple, rise, wake, nervous water), mid-life.
                var top = WithY(edge + outw * 6f, W);
                AddSign(top - along * 2.4f, outw, 0.5f, 0f, 2, 0.45f, 11f);
                AddSign(top - along * 0.8f, outw, 1.2f, 0f, 3, 0.45f, 23f);
                AddSign(top + along * 0.8f, outw, 1.2f, 1f, 1, 0.45f, 37f);
                AddSign(top + along * 2.4f, outw, 1.4f, 2f, 1, 0.45f, 41f);
                log.AppendLine(ShotWith(set, "p1-L4-modes-top.png", top + Vector3.up * 4f + outw * 0.01f, top, 60f));
                signDraws.Clear();
            }
            finally
            {
                fishDraws.Clear(); signDraws.Clear();
                ClearWaterGlobals();
            }
            return log.ToString();
        }

        // Four largemouth instances 1.2 m apart above the water, each with a different G3 back/belly/fin set; pass if every
        // screen band changed (> 400 px) and the four bands' mean colours all differ (> 8 per channel sum).
        static string TintCheck(FishRenderSet set, FishBodies bodies, int variant, Vector3 basePt, Vector3 outw, Vector3 along)
        {
            var swatches = new[] { "LargemouthBass", "BlueCatfish", "CommonCarp", "WhiteCrappie" }
                .Select(id => set.variants.First(v => v.id == id)).ToArray();
            var centres = Enumerable.Range(0, 4).Select(k => basePt + along * ((k - 1.5f) * 1.2f)).ToArray();
            var eye = basePt + outw * 5f + Vector3.up * 0.2f;
            fishDraws.Clear();
            var empty = ShotPixels("p1-tint-check-empty.png", eye, basePt, 45f, null);
            for (int k = 0; k < 4; k++)
            {
                AddFish(set, bodies, variant, centres[k], along, 0.375f, 0f, 0f);
                var f = fishDraws[k]; var v = f.v; var sw = swatches[k];
                v.back = sw.back; v.belly = sw.belly; v.fins = sw.fins; v.mottle = 0f; f.v = v; fishDraws[k] = f;
            }
            var sx = new float[4]; int w = 0, h = 0;
            var drawn = ShotPixels("p1-tint-check.png", eye, basePt, 45f, cam =>
            {
                for (int k = 0; k < 4; k++) sx[k] = cam.WorldToScreenPoint(centres[k]).x;
                w = cam.pixelWidth; h = cam.pixelHeight;
                Submit(set, cam, basePt);
            });
            fishDraws.Clear();
            var order = Enumerable.Range(0, 4).OrderBy(k => sx[k]).ToArray();
            var n = new int[4]; var sum = new Vector3[4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (Mathf.Abs(empty[i].r - drawn[i].r) + Mathf.Abs(empty[i].g - drawn[i].g) + Mathf.Abs(empty[i].b - drawn[i].b) <= 6) continue;
                    int r = 0;
                    while (r < 3 && x > 0.5f * (sx[order[r]] + sx[order[r + 1]])) r++;
                    n[r]++; sum[r] += new Vector3(drawn[i].r, drawn[i].g, drawn[i].b);
                }
            var mean = Enumerable.Range(0, 4).Select(r => n[r] > 0 ? sum[r] / n[r] : Vector3.zero).ToArray();
            bool distinct = true;
            for (int a = 0; a < 4; a++) for (int b = a + 1; b < 4; b++)
                    distinct &= Mathf.Abs(mean[a].x - mean[b].x) + Mathf.Abs(mean[a].y - mean[b].y) + Mathf.Abs(mean[a].z - mean[b].z) > 8f;
            bool ok = n.All(c => c > 400) && distinct;
            return $"tint check (f-td 5.4 f, Phase 1): 4 VAT instances via RenderMeshInstanced, per-region px {string.Join("/", n)} (> 400 each), " +
                   $"mean RGB {string.Join(" | ", mean.Select(m => $"{m.x:F0},{m.y:F0},{m.z:F0}"))}; distinct {distinct} -> {(ok ? "PASS" : "FAIL")} (p1-tint-check.png)";
        }

        // f-td 2b: the census material (mesh path, _UseVat 0, full-precision normals) on the bind-pose mesh, same pose and framing.
        static string MeshPathShot(FishRenderSet set, FishBodies bodies, int variant, Vector3 root, Vector3 heading, float length, Vector3 eye)
        {
            // The shipped census prefab (mesh path, full-precision skinned normals) at the same root, heading and length.
            var id = set.variants[variant].id;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FishPrefabBuilder.PrefabDir}/{id}.prefab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.hideFlags = HideFlags.DontSave;
            try
            {
                inst.transform.SetPositionAndRotation(root, Quaternion.LookRotation(heading, Vector3.up));
                ShotPixels("p1-normals-mesh-rest.png", eye, root, 30f, null);
                return $"  p1-normals-mesh-rest.png: the {id} census prefab (mesh path, skinned normals, bind pose) at the same root/heading as p1-normals-vat-rest.png; " +
                       $"prefab length {inst.transform.localScale.z * bodies.bodies[variant].noseToTail:F3} m vs VAT {length:F3} m";
            }
            finally { Object.DestroyImmediate(inst); }
        }

        // Census (mesh-path) material debug: the same baked Fish1 mesh drawn with (a) the census body material, (b) a copy of
        // the VAT material switched to _UseVat 0, (c) URP Lit, side by side; logs each material's state.
        static string MeshPathDiag(FishRenderSet set, Vector3 at, Vector3 along, Vector3 outw)
        {
            var mesh = set.models[0].mesh;
            var census = AssetDatabase.LoadAssetAtPath<Material>($"{FishPrefabBuilder.MaterialDir}/LargemouthBass_Body.mat");
            var vatCopy = new Material(set.models[0].material) { hideFlags = HideFlags.DontSave };
            vatCopy.SetFloat("_UseVat", 0f);
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
            var mats = new[] { census, vatCopy, lit };
            float size = 0.375f / 8.016f;
            var sb = new StringBuilder("  mesh-path diag (p1-meshpath-diag.png, left to right: census body mat, VAT mat with _UseVat 0, URP Lit):");
            foreach (var m in mats)
                sb.Append($"\n    {(m ? m.name : "NULL")}: shader {(m ? m.shader.name : "-")} supported {(m ? m.shader.isSupported : false)} queue {(m ? m.renderQueue : 0)} " +
                          $"instancing {(m ? m.enableInstancing : false)} keywords [{(m ? string.Join(",", m.shaderKeywords) : "")}] _UseVat {(m && m.HasProperty("_UseVat") ? m.GetFloat("_UseVat") : -1f)} " +
                          $"_BackColor {(m && m.HasProperty("_BackColor") ? m.GetColor("_BackColor").ToString() : "-")} passes {(m ? m.passCount : 0)}");
            ShotPixels("p1-meshpath-diag.png", at + outw * 2.5f + Vector3.up * 0.1f, at, 40f, cam =>
            {
                for (int k = 0; k < 3; k++)
                {
                    if (!mats[k]) continue;
                    var m4 = Matrix4x4.TRS(at + along * ((k - 1) * 0.6f), Quaternion.LookRotation(along, Vector3.up), Vector3.one * size);
                    Graphics.RenderMesh(new RenderParams(mats[k]) { camera = cam, worldBounds = new Bounds(at, Vector3.one * 10f) }, mesh, 0, m4);
                }
            });
            Object.DestroyImmediate(vatCopy); Object.DestroyImmediate(lit);
            // Census prefab as shipped: renderer state and bounds (culling is a common cause of "draws nothing").
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FishPrefabBuilder.PrefabDir + "/LargemouthBass.prefab");
            if (prefab)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                inst.hideFlags = HideFlags.DontSave;
                inst.transform.SetPositionAndRotation(at, Quaternion.LookRotation(along, Vector3.up));
                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var cam = new GameObject("_diag") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(at + outw * 2.5f + Vector3.up * 0.1f, Quaternion.LookRotation(-outw));
                cam.fieldOfView = 40f;
                var planes = GeometryUtility.CalculateFrustumPlanes(cam);
                sb.Append($"\n    census prefab SMR: enabled {smr.enabled} active {smr.gameObject.activeInHierarchy} mesh {(smr.sharedMesh ? smr.sharedMesh.name : "NULL")} " +
                          $"materials [{string.Join(",", smr.sharedMaterials.Select(m => m ? m.name : "NULL"))}] rootBone {(smr.rootBone ? smr.rootBone.name : "NULL")} " +
                          $"localBounds {smr.localBounds} world bounds {smr.bounds} in frustum {GeometryUtility.TestPlanesAABB(planes, smr.bounds)} lossyScale {inst.transform.lossyScale}");
                // Same prefab, offscreen: as shipped, then with its SMR materials swapped to URP Lit. If neither shows, the
                // skinned renderer isn't skinned yet in an edit-mode offscreen render (no player loop tick), not the material.
                var litMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
                var shipped = ShotPixels("p1-census-prefab-asshipped.png", cam.transform.position, at, 40f, null);
                var smrMats = smr.sharedMaterials;
                smr.sharedMaterials = smrMats.Select(_ => litMat).ToArray();
                var withLit = ShotPixels("p1-census-prefab-lit.png", cam.transform.position, at, 40f, null);
                smr.sharedMaterials = smrMats;
                inst.SetActive(false);
                var empty = ShotPixels("p1-census-prefab-empty.png", cam.transform.position, at, 40f, null);
                int Diff(Color32[] a) { int d = 0; for (int i = 0; i < a.Length; i++) if (Mathf.Abs(a[i].r - empty[i].r) + Mathf.Abs(a[i].g - empty[i].g) + Mathf.Abs(a[i].b - empty[i].b) > 6) d++; return d; }
                sb.Append($"\n    census prefab offscreen: as shipped {Diff(shipped)} px changed vs empty; with URP Lit {Diff(withLit)} px");
                Object.DestroyImmediate(litMat);
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(inst);
            }
            return sb.ToString();
        }

        // The fish's model root placed so the top of its clip box is d below W, at the first point out from the bank with room.
        static Vector3 PlaceAtDepth(FishBodies bodies, FishRenderSet set, int variant, Vector3 edge, Vector3 outw, Vector3 along, float length, float d, float W,
                                    StringBuilder log, float silhouette = 0f)
        {
            var b = bodies.bodies[variant];
            float size = length / b.noseToTail;
            float top = (b.clipBounds.center.y + b.clipBounds.extents.y) * size, bottom = (b.clipBounds.center.y - b.clipBounds.extents.y) * size;
            Vector3 p = edge;
            for (float s = 0f; s <= 15f; s += 0.1f)
            {
                p = edge + outw * s;
                if (W - TerrainQuery.Height(p) >= d + (top - bottom) + 0.05f) break;
            }
            var root = new Vector3(p.x, W - d - top, p.z);
            AddFish(set, bodies, variant, root, along, length, silhouette, 0f);
            log.AppendLine($"  {set.variants[variant].id} length {length:F2} m, top {d:F2} m down, {Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(edge.x, 0f, edge.z)):F1} m out, bed {W - TerrainQuery.Height(p):F2} m");
            return root + Vector3.up * (0.5f * (top + bottom));
        }

        static void AddFish(FishRenderSet set, FishBodies bodies, int variant, Vector3 root, Vector3 heading, float length, float silhouette, float flash, float amp = 1f)
        {
            var v = set.variants[variant];
            float size = length / bodies.bodies[variant].noseToTail;
            fishDraws.Add(new Fish { m = Matrix4x4.TRS(root, Quaternion.LookRotation(heading, Vector3.up), v.axisScale * size), v = v, silhouette = silhouette, flash = flash, phase = 0.25f, amp = amp });
        }

        static void AddSign(Vector3 at, Vector3 heading, float size, float mode, int rings, float age, float seed)
        {
            var rot = Quaternion.LookRotation(heading, Vector3.up) * Quaternion.Euler(90f, 0f, 0f);
            signDraws.Add(new Sign
            {
                m = Matrix4x4.TRS(at + Vector3.up * 0.004f, rot, new Vector3(size, size * (mode == 1f ? 1.6f : 1f), 1f)),
                radius = mode == 1f ? 1f : Mathf.Lerp(0.15f, 0.9f, Mathf.Sqrt(age)), strength = (1f - age) * (1f - age), seed = seed, mode = mode, rings = rings,
            });
        }

        // A shot with the pending fish and signs submitted exactly as FishRenderer / FishSurfaceFx submit them.
        static string ShotWith(FishRenderSet set, string file, Vector3 pos, Vector3 target, float fov)
        {
            ShotPixels(file, pos, target, fov, cam => Submit(set, cam, target));
            return $"  {file}: eye ({pos.x:F1},{pos.y:F2},{pos.z:F1}) -> ({target.x:F1},{target.y:F2},{target.z:F1}) fov {fov:F0}, {fishDraws.Count} fish, {signDraws.Count} signs";
        }

        static void Submit(FishRenderSet set, Camera cam, Vector3 target)
        {
            {
                for (int m = 0; m < set.models.Length; m++)
                {
                    var list = fishDraws.Where(f => f.v.model == m).ToList();
                    if (list.Count == 0) continue;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetVectorArray("_BackColor", list.Select(f => (Vector4)f.v.back.linear).ToList());
                    mpb.SetVectorArray("_BellyColor", list.Select(f => (Vector4)f.v.belly.linear).ToList());
                    mpb.SetVectorArray("_FinColor", list.Select(f => (Vector4)f.v.fins.linear).ToList());
                    mpb.SetFloatArray("_Mottle", list.Select(f => f.v.mottle).ToList());
                    mpb.SetFloatArray("_Silhouette", list.Select(f => f.silhouette).ToList());
                    mpb.SetFloatArray("_Flash", list.Select(f => f.flash).ToList());
                    mpb.SetFloatArray("_Phase", list.Select(f => f.phase).ToList());
                    mpb.SetFloatArray("_Amplitude", list.Select(f => f.amp).ToList());
                    mpb.SetFloatArray("_Fade", list.Select(f => 1f).ToList()); // instanced arrays default to 0: not capped here
                    var rp = new RenderParams(set.models[m].material) { camera = cam, matProps = mpb, shadowCastingMode = ShadowCastingMode.Off, worldBounds = new Bounds(target, Vector3.one * 50f) };
                    Graphics.RenderMeshInstanced(rp, set.models[m].mesh, 0, list.Select(f => f.m).ToArray());
                }
                if (signDraws.Count > 0)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetFloatArray("_Radius", signDraws.Select(s => s.radius).ToList());
                    mpb.SetFloatArray("_Strength", signDraws.Select(s => s.strength).ToList());
                    mpb.SetFloatArray("_Seed", signDraws.Select(s => s.seed).ToList());
                    mpb.SetFloatArray("_Mode", signDraws.Select(s => s.mode).ToList());
                    mpb.SetFloatArray("_RingCount", signDraws.Select(s => s.rings).ToList());
                    var rp = new RenderParams(set.ring) { camera = cam, matProps = mpb, shadowCastingMode = ShadowCastingMode.Off, worldBounds = new Bounds(target, Vector3.one * 50f) };
                    Graphics.RenderMeshInstanced(rp, set.ringMesh, 0, signDraws.Select(s => s.m).ToArray());
                }
            }
        }
    }
}
