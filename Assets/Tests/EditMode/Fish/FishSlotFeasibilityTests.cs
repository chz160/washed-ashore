using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Fish;

namespace WashedAshore.Tests.EditMode.Fish
{
    /// <summary>
    /// f-td slot-E review (c): the slot plan never assigns two groupmates poses the body guard forbids. The exact bound:
    /// for every shipped variant of every grouped species, twice the padded clip box's circumscribed radius (about the
    /// fish's centre, at the species' longest adult) fits inside the slot spacing, so a pair is clear at ANY two yaws. Then
    /// the slot plan at the largest group never needs PickSlot's fallback, and a 0/90 degree sampling smoke-checks it.
    /// </summary>
    public class FishSlotFeasibilityTests
    {
        const float Pad = 0.05f;   // FishSimWorld.BoxClearance

        [Test]
        public void SlotSpacing_ExceedsTwiceTheCircumscribedRadius_ForEveryVariant()
        {
            var t = AssetDatabase.LoadAssetAtPath<FishTuning>(FishTuningImport.AssetPath);
            var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(FishTuningImport.BodiesPath);
            var failures = new List<string>();
            foreach (var sp in t.species)
            {
                if (sp.grouping == FishGrouping.Solitary || sp.groupSize.y < 2) continue;
                float l = FishSimWorld.MaxLength(sp, 0f);
                foreach (int v in sp.variants)
                {
                    var body = bodies.bodies[v];
                    float scale = l / Mathf.Max(1e-3f, body.noseToTail), p = Pad * l;
                    var b = body.clipBounds;
                    float centre = new Vector2(b.center.x, b.center.z).magnitude * scale;
                    float r = centre + new Vector2(b.extents.x * scale + p, b.extents.z * scale + p).magnitude;
                    if (2f * r > FishSimWorld.BodyFeasibleSlotLengths * l)
                        failures.Add($"{sp.name} variant {v}: 2 x circumscribed radius {2f * r:F3} m > {FishSimWorld.BodyFeasibleSlotLengths} x L = {FishSimWorld.BodyFeasibleSlotLengths * l:F3} m");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void SlotPlan_IsBodyFeasible_ForEveryGroupedSpecies()
        {
            var t = AssetDatabase.LoadAssetAtPath<FishTuning>(FishTuningImport.AssetPath);
            var bodies = AssetDatabase.LoadAssetAtPath<FishBodies>(FishTuningImport.BodiesPath);
            Assert.IsNotNull(t, "shipped FishTuning");
            Assert.IsNotNull(bodies, "shipped FishBodies");
            var failures = new List<string>();
            for (int s = 0; s < t.species.Length; s++)
            {
                var sp = t.species[s];
                if (sp.grouping == FishGrouping.Solitary || sp.groupSize.y < 2) continue;
                float length = FishSimWorld.MaxLength(sp, 0f);
                for (int seed = 1; seed <= 5; seed++)
                {
                    var slots = new List<Vector3>();
                    for (int m = 0; m < sp.groupSize.y; m++)
                    {
                        var d = new FishDraws(seed, FishRandom.StreamSteer, s, m);
                        slots.Add(FishSimWorld.PickSlot(sp, ref d, slots, length, out bool fellBack));
                        if (fellBack) failures.Add($"{sp.name} seed {seed}: member {m} needed PickSlot's fallback (n {sp.groupSize.y}, radius {sp.groupRadius})");
                    }
                    foreach (int v in sp.variants)
                        foreach (float yaw in new[] { 0f, 90f })
                            for (int i = 0; i < slots.Count; i++)
                                for (int j = i + 1; j < slots.Count; j++)
                                    if (Box(bodies.bodies[v], length, slots[i], yaw).OverlapsXZ(Box(bodies.bodies[v], length, slots[j], yaw)))
                                    {
                                        failures.Add($"{sp.name} seed {seed} variant {v} yaw {yaw}: slots {i},{j} {(slots[i] - slots[j]).magnitude:F2} m apart (L {length:F2}, radius {sp.groupRadius}, n {sp.groupSize.y})");
                                        goto nextSeed;
                                    }
                    nextSeed:;
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        static FishBox Box(FishBody body, float length, Vector3 centre, float yaw)
        {
            float scale = length / Mathf.Max(1e-3f, body.noseToTail);
            var b = body.clipBounds;
            var fwd = FishSteering.Heading(yaw);
            var c = centre + Quaternion.Euler(0f, yaw, 0f) * (b.center * scale);
            float pad = Pad * length;
            return new FishBox(new Vector2(c.x, c.z), fwd, b.extents.x * scale + pad, b.extents.z * scale + pad, Vector2.zero);
        }
    }
}
