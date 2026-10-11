using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using WashedAshore.Fish.Rendering;
using WashedAshore.Gameplay;
using WashedAshore.Level;
using Object = UnityEngine.Object;

namespace WashedAshore.Fish.Editor
{
    // Sign legibility table of record (f-qa plan rev 4.2; ruling/fish-sign-footprint-physical). Every countable sign kind, drawn
    // exactly as FishSurfaceFx draws it (Look / FootprintQuad / ShapeFootprint over the imported F17 footprints, FishRing
    // material after the legibility pass), from the shore eye and both F16 bluff poses (McCord, Buzzard), at 5/10/15/30/60/100/150 m (f-qa rev 4.8),
    // at normalised ages 0.1/0.2/0.35/0.5/0.7/0.9, 1920x1080, vFOV 70, shipped fog and water. Per shot: a sign frame and its sign-free
    // twin (cropped to the projected annulus, origin recorded), the crest/trough pixel coordinates, the undisturbed annulus
    // (1.25 and 1.75 quad radii), minor axis (px) and Weber contrast on linear Rec.709 luminance (crest / trough mean vs the
    // annulus mean, same frame). Written with JsonUtility (invariant culture, always valid JSON). Gates (slot #15 void; f-qa
    // amended): (a) every in-frame shot must have SUBMITTED its draw (submitted >= 1); (b) per kind x eye, the control shot
    // (nearest valid distance, youngest age) must change more than LegControlMinPx px vs its twin inside the projected sign
    // bounds; either failing fails the run. Other shots may change 0 px (illegible). Ring crest/trough points count only
    // where the pixel changed vs the twin.
    public static partial class FishLookDev
    {
        static readonly FishSurfaceKind[] LegKinds =
        {
            FishSurfaceKind.Dimple, FishSurfaceKind.Rise, FishSurfaceKind.Swirl, FishSurfaceKind.RollOrTail, FishSurfaceKind.GarGulpOrBask,
            FishSurfaceKind.Jump, FishSurfaceKind.Wake, FishSurfaceKind.NervousWaterOrFlip, FishSurfaceKind.Busting,
        };
        static readonly float[] LegDistances = { 5f, 10f, 15f, 30f, 60f, 100f, 150f };
        static readonly float[] LegAges = { 0.1f, 0.2f, 0.35f, 0.5f, 0.7f, 0.9f };
        public const int LegControlMinPx = 20; // f-qa gate (b): a kind x eye control shot must change more than this
        // f-qa ruling (i), slot #18: the gar body from either bluff has its control at 30 m (the nearest valid point), where a
        // 0.9 m body is legitimately tiny. Those two controls may pass as "rendered-below-N" (submitted, > 0 px in bounds); the
        // series are still graded by the floor. Any other control at or below N fails the run.
        public static bool LegBelowNAllowed(string kind, string eye) => kind == "GarBask" && (eye == "McCord" || eye == "Buzzard");
        // f-qa (b), slot #19: Buzzard 60 m looks through a tree at the F16 pose; legibility is about distance, so that point
        // moves along the 60 m ring to the nearest clear water (F7's occlusion helper handles trees in real runs).
        static bool LegShiftIfOccluded(string eye, float d) => eye == "Buzzard" && Mathf.Approximately(d, 60f);
        const float LegChangedLuma = 0.0015f; // linear luma step that counts as a changed pixel (above 8-bit noise in the dark olive)
        const float LegFov = 70f, LegEventSize = 0.5f, LegBurstSpeed = 1.5f; // burst: a typical bass/carp burst (m/s), recorded per row // event size (body length / school radius) only matters for patch signs

        [Serializable] public class LegPoint { public int x, y; }
        [Serializable]
        public class LegShotRecord
        {
            public string kind, eye, signFile, noSignFile;
            public float distM, camDistM, age, ageSec, lifeSec, quadM, leadingRingRadiusM, peakDrawnAlpha, minorAxisPx, weberCrest, weberTrough;
            public bool inFrame, meetsFloor;
            public bool signRendered; public int changedPx, submitted; public bool control; public string controlStatus = "";
            public float projectedHeightPx, lateralOffsetM; public string occludedBy = ""; // slot #19 facts (f-qa (a), (b)) // twin gate: pixels that differ from the sign-free twin (|dL| >= LegChangedLuma)
            public float fpCoreRadius, fpRingSpeed, fpMaxRingRadius, fpArmLength, fpLifeMin, fpLifeMax, fpPatchMin, fpPatchMax, fpArmSeconds, burstSpeedUsed; // footprint values read
            public int cropX, cropY, cropW, cropH;
            public List<LegPoint> crest = new List<LegPoint>(), trough = new List<LegPoint>(), annulus = new List<LegPoint>();
        }
        [Serializable]
        public class LegTable
        {
            public string protocol = "f-qa plan rev 4.2", footprintsFrom, lookAfter = "FishRing legibility pass (a), slot #14; F17 physical footprints",
                pixelOrigin = "bottom-left, full-frame coordinates (Unity screen space); crop PNG row = cropH - 1 - (y - cropY)",
                luminance = "linear Rec.709 from sRGB-decoded RGB";
            public bool valid; public string invalidReason = ""; public List<string> exceptions = new List<string>();
            public string occlusionMethod = "occludedBy is for choosing a clear measurement point and labelling rows only: terrain heightfield, "
                + "terrain trees as crown cylinders (conservative) and colliders. Not F7's occlusion of record (f-td ruling: trunks and visible colliders, not canopy).";
            public int width = 1920, height = 1080;
            public float vFov = LegFov, waterY, ringWidth;
            public List<LegShotRecord> shots = new List<LegShotRecord>();
        }

        public static void RunLegibilityBatch()
        {
            int code = 0;
            var dir = Path.Combine(OutDir, "legibility");
            var log = new StringBuilder($"legibility {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            try
            {
                Directory.CreateDirectory(dir);
                log.AppendLine(CheckShaders(Shader.Find(FishShader), Shader.Find(RingShader)));
                var cfg = BellsBendData.LoadConfig();
                float W = cfg.WaterLevelY;
                var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
                var lm = BellsBendData.LoadVectors(cfg).landmarks.ToDictionary(l => l.id, l => new Vector3(l.xz.x, 0f, l.xz.y));
                var mcCord = new Vector3(401.8f, 27.6f, 717.6f);
                var buzzard = new Vector3(415.1f, 25.68f, 1792.4f);
                var scene = BuildScene(new[] { lm["CleecesFerryLanding"], new Vector3(mcCord.x, 0f, mcCord.z), new Vector3(buzzard.x, 0f, buzzard.z) }, log);
                PushWaterGlobals(scene, W, log);
                log.AppendLine(FishVatBake.UpdateFootprints()); // F17 footprints into FishRenderSet (no VAT, mesh or material writes)
                var set = AssetDatabase.LoadAssetAtPath<FishRenderSet>(FishVatBake.RenderSetPath);
                if (set.footprints.Length == 0) throw new InvalidOperationException("no footprints in FishRenderSet: run FishVatBake.UpdateFootprints first");
                var edge = NearestShore(maps, lm["CleecesFerryLanding"], -1f, 0f);
                var outw = Outward(maps, edge);
                var shoreEye = WithY(edge - outw, TerrainQuery.Height(edge - outw) + ShoreEye);
                var eyes = new (string name, Vector3 pos, Vector3 facing, float pitch)[]
                {
                    ("shore", shoreEye, outw, float.NaN),                                           // aimed at the sign
                    ("McCord", mcCord, new Vector3(0.81f, 0f, 0.59f).normalized, 23.5f),             // F16 routeBluff poses
                    ("Buzzard", buzzard, new Vector3(0.87f, 0f, -0.5f).normalized, 20.2f),
                };
                var table = new LegTable
                {
                    waterY = W, ringWidth = set.ring.GetFloat("_RingWidth"), footprintsFrom = "FishRenderSet.footprints: " + set.footprintsSource,
                    lookAfter = "FishRing legibility pass (a), slot #14; F17 physical footprints from " + set.footprintsSource,
                };
                foreach (var e in eyes)
                    foreach (float d in LegDistances)
                    {
                        var at = WithY(new Vector3(e.pos.x, 0f, e.pos.z) + e.facing * d, W);
                        if (W - TerrainQuery.Height(at) <= 0.05f) { log.AppendLine($"  {e.name} {d} m: land at ({at.x:F1},{at.z:F1}), skipped"); continue; }
                        Quaternion rot = float.IsNaN(e.pitch) ? Quaternion.LookRotation(at - e.pos)
                                       : Quaternion.LookRotation(Quaternion.AngleAxis(e.pitch, Vector3.Cross(Vector3.up, e.facing)) * e.facing);
                        // f-qa (b): an occluded grid point in LegShiftIfOccluded moves along its ring (same eye, pose and distance)
                        // to the nearest clear water point in frame; the offset and the clear check are recorded per row.
                        string occluder = LegOccluder(e.pos, at);
                        float lateral = 0f;
                        if (occluder.Length > 0 && LegShiftIfOccluded(e.name, d))
                        {
                            var probeCam = new GameObject("_LegSearch") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Camera>();
                            probeCam.transform.SetPositionAndRotation(e.pos, rot);
                            probeCam.fieldOfView = LegFov; probeCam.aspect = 1920f / 1080f; probeCam.nearClipPlane = 0.1f; probeCam.farClipPlane = 3000f;
                            if (LegNearestClear(e.pos, e.facing, d, W, probeCam, 30f, out var clear, out lateral))
                            {
                                log.AppendLine($"  {e.name} {d} m: ({at.x:F1},{at.z:F1}) occluded by {occluder}; moved {lateral:F0} m along the ring to ({clear.x:F1},{clear.z:F1}), line of sight clear");
                                at = clear; occluder = "";
                            }
                            else log.AppendLine($"  {e.name} {d} m: occluded by {occluder}; no clear water point within 30 m of arc");
                            Object.DestroyImmediate(probeCam.gameObject);
                        }
                        else if (occluder.Length > 0) log.AppendLine($"  {e.name} {d} m: line of sight blocked by {occluder} (recorded, not moved)");
                        signDraws.Clear(); fishDraws.Clear();
                        var empty = LegRender(set, e.pos, rot, out var cam0);
                        Object.DestroyImmediate(cam0.gameObject);
                        foreach (var kind in LegKinds)
                        {
                            if (!set.TryFootprint(kind, out var foot)) { log.AppendLine($"  {kind}: no footprint, skipped"); continue; }
                            var look = FishSurfaceFx.Look(kind);
                            float quad = FishSurfaceFx.FootprintQuad(foot, look, LegEventSize, LegBurstSpeed);
                            float life = Mathf.Max(0.5f, FishSurfaceFx.FootprintLife(foot, LegEventSize));
                            foreach (float t in LegAges)
                            {
                                FishSurfaceFx.ShapeFootprint(foot, look.mode, t * life, t, quad, out float radius, out float strength);
                                signDraws.Clear();
                                var m = Matrix4x4.TRS(at + Vector3.up * 0.004f, Quaternion.LookRotation(e.facing, Vector3.up) * Quaternion.Euler(90f, 0f, 0f),
                                                      new Vector3(quad, quad * (look.mode == 1f ? 1.6f : 1f), 1f));
                                signDraws.Add(new Sign { m = m, radius = radius, strength = strength, seed = 17f, mode = look.mode, rings = look.rings });
                                var px = LegRender(set, e.pos, rot, out var cam);
                                var rec = LegMeasure(px, empty, cam, at, quad, radius, look.mode, table.ringWidth);
                                rec.submitted = lastSubmitted;
                                rec.occludedBy = occluder; rec.lateralOffsetM = lateral;
                                rec.projectedHeightPx = LegProjectedHeightPx(cam, at, e.facing, quad, quad * (look.mode == 1f ? 1.6f : 1f));
                                Object.DestroyImmediate(cam.gameObject);
                                string stem = $"{kind}-{e.name}-{d:F0}m-age{t:F2}";
                                rec.kind = kind == FishSurfaceKind.GarGulpOrBask ? "GarGulp" : kind.ToString(); rec.eye = e.name; rec.distM = d; rec.camDistM = Vector3.Distance(e.pos, at);
                                rec.age = t; rec.ageSec = t * life; rec.lifeSec = life; rec.quadM = quad;
                                rec.leadingRingRadiusM = look.mode == 0f ? radius * quad * 0.5f : 0f;
                                rec.fpCoreRadius = foot.coreRadius; rec.fpRingSpeed = foot.ringSpeed; rec.fpMaxRingRadius = foot.maxRingRadius; rec.fpArmLength = foot.armLength;
                                rec.fpArmSeconds = foot.armSeconds; rec.burstSpeedUsed = LegBurstSpeed;
                                rec.fpLifeMin = foot.lifeSec.x; rec.fpLifeMax = foot.lifeSec.y; rec.fpPatchMin = foot.patchDiameter.x; rec.fpPatchMax = foot.patchDiameter.y;
                                float far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(set.ring.GetVector("_FarRange").x, set.ring.GetVector("_FarRange").y, rec.camDistM));
                                rec.peakDrawnAlpha = Mathf.Lerp(set.ring.GetFloat("_MaxAlpha"), set.ring.GetFloat("_FarMaxAlpha"), far) * strength;
                                rec.meetsFloor = rec.inFrame && rec.signRendered && rec.minorAxisPx >= 6f && Mathf.Max(Mathf.Abs(rec.weberCrest), Mathf.Abs(rec.weberTrough)) >= 0.05f;
                                rec.signFile = $"legibility/{stem}.png"; rec.noSignFile = $"legibility/{stem}-nosign.png";
                                SaveCrop(px, rec, Path.Combine(OutDir, rec.signFile));
                                SaveCrop(empty, rec, Path.Combine(OutDir, rec.noSignFile));
                                table.shots.Add(rec);
                            }
                        }
                        // GarBask (f-designer split): no ring; the gar's V2 shadow body with its top at the surface (dorsal/snout line).
                        // Minor axis = vertical extent of the changed mask; ages don't apply (a steady bask), recorded as age 0.5.
                        {
                            var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(FishVatBake.BodiesPath);
                            int gar = System.Array.FindIndex(set.variants, v => v.id == "LongnoseGar");
                            signDraws.Clear(); fishDraws.Clear();
                            var b = bodies.bodies[gar];
                            float length = 0.9f, size = length / b.noseToTail;
                            float top = (b.clipBounds.center.y + b.clipBounds.extents.y) * size;
                            AddFish(set, bodies, gar, new Vector3(at.x, W - top, at.z), e.facing, length, 1f, 0f);
                            var pxb = LegRender(set, e.pos, rot, out var camb);
                            var rb = LegMeasure(pxb, empty, camb, at, length, 0f, 2f, table.ringWidth);
                            rb.submitted = lastSubmitted;
                            rb.occludedBy = occluder; rb.lateralOffsetM = lateral;
                            rb.projectedHeightPx = LegProjectedHeightPx(camb, at, e.facing, 0.15f * length, length); // dorsal/snout line
                            Object.DestroyImmediate(camb.gameObject);
                            fishDraws.Clear();
                            string stemb = $"GarBask-{e.name}-{d:F0}m-age0.50";
                            rb.kind = "GarBask"; rb.eye = e.name; rb.distM = d; rb.camDistM = Vector3.Distance(e.pos, at); rb.age = 0.5f; rb.quadM = length;
                            rb.peakDrawnAlpha = 1f;
                            rb.meetsFloor = rb.inFrame && rb.signRendered && rb.minorAxisPx >= 6f && Mathf.Max(Mathf.Abs(rb.weberCrest), Mathf.Abs(rb.weberTrough)) >= 0.05f;
                            rb.signFile = $"legibility/{stemb}.png"; rb.noSignFile = $"legibility/{stemb}-nosign.png";
                            SaveCrop(pxb, rb, Path.Combine(OutDir, rb.signFile));
                            SaveCrop(empty, rb, Path.Combine(OutDir, rb.noSignFile));
                            table.shots.Add(rb);
                        }
                        log.AppendLine($"  {e.name} {d} m: done");
                    }
                // Gate (a): every in-frame shot submitted its draw. Gate (b): per kind x eye, the control shot (nearest valid
                // distance, youngest age) changed more than LegControlMinPx px vs its twin inside the projected bounds.
                var unsubmitted = table.shots.Where(s => s.inFrame && s.submitted < 1).Select(s => s.signFile).ToList();
                foreach (var g in table.shots.Where(s => s.inFrame).GroupBy(s => (s.kind, s.eye)))
                    g.OrderBy(s => s.distM).ThenBy(s => s.age).First().control = true;
                foreach (var s in table.shots.Where(s => s.control))
                    s.controlStatus = s.changedPx > LegControlMinPx ? "passed"
                        : LegBelowNAllowed(s.kind, s.eye) && s.submitted >= 1 && s.changedPx > 0 ? "rendered-below-N" : "failed";
                table.exceptions = table.shots.Where(s => s.controlStatus == "rendered-below-N")
                    .Select(s => $"{s.kind}/{s.eye} control {s.signFile}: rendered-below-N ({s.changedPx} px in bounds, submitted {s.submitted}; f-qa ruling (i), slot #18)").ToList();
                var dark = table.shots.Where(s => s.controlStatus == "failed").Select(s => $"{s.signFile} ({s.changedPx} px)").ToList();
                table.valid = unsubmitted.Count == 0 && dark.Count == 0;
                if (!table.valid) table.invalidReason = $"{unsubmitted.Count} in-frame shots submitted no draw; {dark.Count} controls changed <= {LegControlMinPx} px"
                    + (dark.Count > 0 ? $", e.g. {dark[0]}" : "") + (unsubmitted.Count > 0 ? $", e.g. {unsubmitted[0]}" : "");
                File.WriteAllText(Path.Combine(OutDir, "sign-legibility.json"), JsonUtility.ToJson(table, true));
                ClearWaterGlobals();
                log.AppendLine($"{table.shots.Count} shots; drawn (changed px > 0): {table.shots.Count(s => s.signRendered)}; meets floor: {table.shots.Count(s => s.meetsFloor)}");
                if (!table.valid) throw new InvalidOperationException("twin gate: " + table.invalidReason);
                File.WriteAllText(Path.Combine(dir, "log.txt"), log + "done\n");
            }
            catch (Exception ex) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "log.txt"), log + "FAILED: " + ex); code = 1; }
            EditorApplication.Exit(code);
        }

        // Slot #17 probe (why slot #15 drew no signs): one Rise at the shore eye, 15 m, age 0.35, rendered five ways; logs the
        // changed pixels of each vs its twin and the ring material/mesh state. Writes TestResults/fish-lookdev/legibility-probe.txt.
        public static void RunLegibilityProbe()
        {
            int code = 0;
            var log = new StringBuilder($"legibility probe {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}\n");
            try
            {
                var set = AssetDatabase.LoadAssetAtPath<FishRenderSet>(FishVatBake.RenderSetPath);
                var cfg = BellsBendData.LoadConfig();
                float W = cfg.WaterLevelY;
                var maps = AssetDatabase.LoadAssetAtPath<BellsBendLevelMaps>(BellsBendLevelMaps.AssetPath);
                var lm = BellsBendData.LoadVectors(cfg).landmarks.ToDictionary(l => l.id, l => new Vector3(l.xz.x, 0f, l.xz.y));
                var scene = BuildScene(new[] { lm["CleecesFerryLanding"] }, log);
                PushWaterGlobals(scene, W, log);
                string State() => $"ring {(set.ring ? $"{set.ring.name} / {set.ring.shader.name}, instancing {set.ring.enableInstancing}, queue {set.ring.renderQueue}" : "NULL")}; " +
                                  $"mesh {(set.ringMesh ? $"{set.ringMesh.name}, {set.ringMesh.vertexCount} verts, asset '{AssetDatabase.GetAssetPath(set.ringMesh)}'" : "NULL")}; footprints {set.footprints.Length}";
                log.AppendLine("before: " + State());
                var edge = NearestShore(maps, lm["CleecesFerryLanding"], -1f, 0f);
                var outw = Outward(maps, edge);
                var eye = WithY(edge - outw, TerrainQuery.Height(edge - outw) + ShoreEye);
                var at = WithY(edge + outw * 15f, W);
                var rot = Quaternion.LookRotation(at - eye);
                int Diff(Color32[] a, Color32[] b) { int n = 0; for (int i = 0; i < a.Length; i++) if (Mathf.Abs(LinearLuma(a[i]) - LinearLuma(b[i])) >= LegChangedLuma) n++; return n; }
                void Stage(bool footprint)
                {
                    signDraws.Clear(); fishDraws.Clear();
                    if (!footprint) { AddSign(at, outw, 1.5f, 0f, 3, 0.35f, 17f); return; } // the slot #14 legacy look
                    set.TryFootprint(FishSurfaceKind.Rise, out var foot);
                    var look = FishSurfaceFx.Look(FishSurfaceKind.Rise);
                    float quad = FishSurfaceFx.FootprintQuad(foot, look, LegEventSize, LegBurstSpeed), life = FishSurfaceFx.FootprintLife(foot, LegEventSize);
                    FishSurfaceFx.ShapeFootprint(foot, look.mode, 0.35f * life, 0.35f, quad, out float radius, out float strength);
                    signDraws.Add(new Sign { m = Matrix4x4.TRS(at + Vector3.up * 0.004f, Quaternion.LookRotation(outw, Vector3.up) * Quaternion.Euler(90f, 0f, 0f), new Vector3(quad, quad, 1f)),
                                             radius = radius, strength = strength, seed = 17f, mode = look.mode, rings = look.rings });
                    log.AppendLine($"  footprint sign: quad {quad:F3} m, radius {radius:F3}, strength {strength:F3}");
                }
                Color32[] Leg() { var px = LegRender(set, eye, rot, out var c); Object.DestroyImmediate(c.gameObject); return px; }
                // A: the slot #15 order (twin first, then the sign frame).
                signDraws.Clear(); var twinA = Leg(); Stage(true); var signA = Leg();
                log.AppendLine($"A legibility path, twin then sign, footprint: {Diff(signA, twinA)} px changed");
                // B: sign frame first, then the twin.
                Stage(true); var signB = Leg(); signDraws.Clear(); var twinB = Leg();
                log.AppendLine($"B legibility path, sign then twin, footprint: {Diff(signB, twinB)} px changed");
                // C: the slot #14 path (ShotPixels + Submit), footprint sign; D: same path, legacy sign.
                Stage(true); var signC = ShotPixels("probe-C-sign.png", eye, at, LegFov, c => Submit(set, c, at)); signDraws.Clear();
                var twinC = ShotPixels("probe-C-twin.png", eye, at, LegFov, null);
                log.AppendLine($"C slot #14 path, footprint: {Diff(signC, twinC)} px changed");
                Stage(false); var signD = ShotPixels("probe-D-sign.png", eye, at, LegFov, c => Submit(set, c, at)); signDraws.Clear();
                log.AppendLine($"D slot #14 path, legacy sign: {Diff(signD, twinC)} px changed");
                // E: slot #15 also ran UpdateFootprints (SetDirty + SaveAssets on FishRenderSet) before rendering: repeat A after it.
                log.Append(FishVatBake.UpdateFootprints());
                set = AssetDatabase.LoadAssetAtPath<FishRenderSet>(FishVatBake.RenderSetPath);
                signDraws.Clear(); var twinE = Leg(); Stage(true); var signE = Leg();
                log.AppendLine($"E legibility path after UpdateFootprints: {Diff(signE, twinE)} px changed; " + State());
                // F: the batch also compiles the shaders' variants (CheckShaders) before building its scene.
                log.AppendLine(CheckShaders(Shader.Find(FishShader), Shader.Find(RingShader)));
                signDraws.Clear(); var twinF = Leg(); Stage(true); var signF = Leg();
                log.AppendLine($"F after CheckShaders: {Diff(signF, twinF)} px changed");
                // G: and builds the scene around all three sites (shore landing, McCord, Buzzard).
                scene = BuildScene(new[] { lm["CleecesFerryLanding"], new Vector3(401.8f, 0f, 717.6f), new Vector3(415.1f, 0f, 1792.4f) }, log);
                PushWaterGlobals(scene, W, log);
                signDraws.Clear(); var twinG = Leg(); Stage(true); var signG = Leg();
                log.AppendLine($"G after the 3-site scene: {Diff(signG, twinG)} px changed");
                log.AppendLine("after: " + State());
                signDraws.Clear(); fishDraws.Clear();
                ClearWaterGlobals();
            }
            catch (Exception ex) { log.AppendLine("FAILED: " + ex); code = 1; }
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "legibility-probe.txt"), log + "done\n");
            EditorApplication.Exit(code);
        }

        static int lastSubmitted; // draws handed to Submit for the last LegRender frame (gate a)

        // One frame with the pending signs (none = the twin). The caller destroys cam.
        static Color32[] LegRender(FishRenderSet set, Vector3 pos, Quaternion rot, out Camera cam)
        {
            const int Wd = 1920, H = 1080;
            var go = new GameObject("_LegCamera") { hideFlags = HideFlags.HideAndDontSave };
            cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = LegFov; cam.nearClipPlane = 0.1f; cam.farClipPlane = 3000f;
            cam.aspect = (float)Wd / H; // kept after targetTexture is cleared, so LegPx projects into this frame
            var rt = RenderTexture.GetTemporary(Wd, H, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            lastSubmitted = signDraws.Count + fishDraws.Count;
            if (lastSubmitted > 0) // GarBask draws a body and no sign
                Submit(set, cam, signDraws.Count > 0 ? (Vector3)signDraws[0].m.GetColumn(3) : (Vector3)fishDraws[0].m.GetColumn(3));
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(Wd, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Wd, H), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            var px = tex.GetPixels32();
            Object.DestroyImmediate(tex);
            return px;
        }

        public static float LinearLuma(Color32 c)
        {
            float Lin(byte b) { float v = b / 255f; return v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f); }
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        // Rings: the projected leading crest circle and its trough (72 points each); wakes and patches: the changed-pixel mask
        // inside the projected quad, split brighter (crest) / darker (trough). Annulus: circles at 1.25 and 1.75 quad radii.
        // Pixel position in the 1920x1080 frame LegRender drew. Slot #18 root cause of the void #15 table and the first #18 run:
        // LegRender clears targetTexture before LegMeasure, so WorldToScreenPoint projected into the batchmode default screen
        // (640x480): every crest/annulus/gate box and crop landed beside the sign (e.g. (320,240) for a centred sign), and the
        // crops of sign and twin were the same untouched water. Viewport coordinates x the frame size don't depend on the target.
        static Vector3 LegPx(Camera cam, Vector3 world)
        {
            var v = cam.WorldToViewportPoint(world);
            return new Vector3(v.x * 1920f, v.y * 1080f, v.z);
        }

        static LegShotRecord LegMeasure(Color32[] px, Color32[] empty, Camera cam, Vector3 at, float quad, float radius, float mode, float ringWidth)
        {
            const int Wd = 1920, H = 1080;
            var r = new LegShotRecord();
            float half = quad * 0.5f;
            var c0 = LegPx(cam, at);
            r.inFrame = c0.z > 0f && c0.x >= 0 && c0.x < Wd && c0.y >= 0 && c0.y < H;
            void Circle(float rad, List<LegPoint> into)
            {
                var seen = new HashSet<int>(into.Select(q => q.y * Wd + q.x));
                for (int k = 0; k < 72; k++)
                {
                    float a = k * 2f * Mathf.PI / 72;
                    var s = LegPx(cam, at + new Vector3(Mathf.Cos(a) * rad, 0.004f, Mathf.Sin(a) * rad));
                    int x = Mathf.RoundToInt(s.x), y = Mathf.RoundToInt(s.y);
                    if (s.z > 0f && x >= 0 && x < Wd && y >= 0 && y < H && seen.Add(y * Wd + x)) into.Add(new LegPoint { x = x, y = y });
                }
            }
            Circle(half * 1.25f, r.annulus); Circle(half * 1.75f, r.annulus);
            bool Changed(LegPoint q) => Mathf.Abs(LinearLuma(px[q.y * Wd + q.x]) - LinearLuma(empty[q.y * Wd + q.x])) >= LegChangedLuma;
            // Twin diff over the drawn footprint (any heading: a box of the quad's bounding radius, wakes 1.6x long).
            float along = mode == 1f ? 1.6f : 1f, reach = half * Mathf.Sqrt(1f + along * along);
            var corners = new[] { new Vector3(-reach, 0, -reach), new Vector3(reach, 0, -reach), new Vector3(-reach, 0, reach), new Vector3(reach, 0, reach) }
                .Select(o => LegPx(cam, at + o)).ToArray();
            int x0 = Mathf.Clamp((int)corners.Min(q => q.x), 0, Wd - 1), x1 = Mathf.Clamp((int)corners.Max(q => q.x), 0, Wd - 1);
            int y0 = Mathf.Clamp((int)corners.Min(q => q.y), 0, H - 1), y1 = Mathf.Clamp((int)corners.Max(q => q.y), 0, H - 1);
            int minY = int.MaxValue, maxY = int.MinValue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var q = new LegPoint { x = x, y = y };
                    if (!Changed(q)) continue;
                    r.changedPx++;
                    if (mode != 0f) (LinearLuma(px[y * Wd + x]) > LinearLuma(empty[y * Wd + x]) ? r.crest : r.trough).Add(q);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            r.signRendered = r.changedPx > 0;
            if (mode == 0f)
            {
                // Rings: the projected crest / trough circles, kept only where the sign changed the pixel vs its twin.
                float w = ringWidth * (0.8f + 0.6f * radius);
                Circle(radius * half, r.crest);
                Circle(Mathf.Max(0.01f, radius - 1.3f * w) * half, r.trough);
                r.crest.RemoveAll(q => !Changed(q)); r.trough.RemoveAll(q => !Changed(q));
                r.minorAxisPx = r.crest.Count > 1 ? r.crest.Max(p => p.y) - r.crest.Min(p => p.y) : 0;
            }
            else r.minorAxisPx = maxY >= minY ? maxY - minY : 0;
            float Mean(List<LegPoint> pts) => pts.Count == 0 ? 0f : pts.Average(p => LinearLuma(px[p.y * Wd + p.x]));
            float la = Mean(r.annulus);
            if (la > 1e-5f)
            {
                r.weberCrest = r.crest.Count > 0 ? (Mean(r.crest) - la) / la : 0f;
                r.weberTrough = r.trough.Count > 0 ? (Mean(r.trough) - la) / la : 0f;
            }
            // Crop: the projected annulus and the twin-gate bounds plus 10%, at least 256 x 144, inside the frame.
            var all = r.annulus.Concat(r.crest).Concat(new[] { new LegPoint { x = x0, y = y0 }, new LegPoint { x = x1, y = y1 } }).ToList(); // + the gated bounds
            int cx0 = all.Count > 0 ? all.Min(p => p.x) : (int)c0.x, cx1 = all.Count > 0 ? all.Max(p => p.x) : (int)c0.x;
            int cy0 = all.Count > 0 ? all.Min(p => p.y) : (int)c0.y, cy1 = all.Count > 0 ? all.Max(p => p.y) : (int)c0.y;
            int padX = Mathf.Max(128 - (cx1 - cx0) / 2, (cx1 - cx0) / 10), padY = Mathf.Max(72 - (cy1 - cy0) / 2, (cy1 - cy0) / 10);
            r.cropX = Mathf.Clamp(cx0 - padX, 0, Wd - 1); r.cropY = Mathf.Clamp(cy0 - padY, 0, H - 1);
            r.cropW = Mathf.Clamp(cx1 + padX, 0, Wd - 1) - r.cropX + 1; r.cropH = Mathf.Clamp(cy1 + padY, 0, H - 1) - r.cropY + 1;
            return r;
        }

        static void SaveCrop(Color32[] px, LegShotRecord r, string path)
        {
            const int Wd = 1920;
            var crop = new Color32[r.cropW * r.cropH];
            for (int y = 0; y < r.cropH; y++) Array.Copy(px, (r.cropY + y) * Wd + r.cropX, crop, y * r.cropW, r.cropW);
            var tex = new Texture2D(r.cropW, r.cropH, TextureFormat.RGB24, false);
            tex.SetPixels32(crop); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
