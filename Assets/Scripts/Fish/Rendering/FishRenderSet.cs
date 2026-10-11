using System;
using UnityEngine;

namespace WashedAshore.Fish.Rendering
{
    /// <summary>
    /// Everything the fish renderer draws with, written by f-artist's bake (Assets/Editor/Fish/FishVatBake), never typed in:
    /// one VAT mesh + instanced material per stand-in model, and per variant (index = FishState.variantIndex, the same order
    /// as FishBodies) the model, the non-uniform axis scale and the G3 swatches. The water material is the source of the
    /// murk values the fish shader mirrors.
    /// </summary>
    [CreateAssetMenu(fileName = "FishRenderSet", menuName = "Washed Ashore/Fish Render Set")]
    public class FishRenderSet : ScriptableObject
    {
        [Serializable]
        public class Model
        {
            public string name;
            public Mesh mesh;
            [Tooltip("FishUnderwater with _UseVat 1 and this model's VAT; enableInstancing on.")]
            public Material material;
        }

        [Serializable]
        public struct Variant
        {
            public string id;
            public int model;
            [Tooltip("Non-uniform scale (x width, y height, z 1) from G3, applied before the fish's uniform size scale.")]
            public Vector3 axisScale;
            public Color back, belly, fins;
            [Range(0f, 1f)] public float mottle;
        }

        /// <summary>
        /// A surface sign's physical footprint (fish-targets surfaceSigns.footprints[], ruling/fish-sign-footprint-physical),
        /// imported by f-artist's editor tool, never typed in. Ring signs: leading ring radius = coreRadius + ringSpeed x age (s),
        /// capped at maxRingRadius; patch signs: patchDiameter; wakes: armLength. lifeSec is the fallback life when an event
        /// carries no duration (min..max: an event's size picks within it).
        /// </summary>
        [Serializable]
        public struct Footprint
        {
            public FishSurfaceKind kind;
            public float coreRadius, ringSpeed, maxRingRadius, armLength, armSeconds; // wake arm = armLength, or burst speed x armSeconds
            public Vector2 lifeSec, patchDiameter;
        }

        public Model[] models = new Model[0];
        public Footprint[] footprints = new Footprint[0];
        /// <summary>Where the footprints came from: targets file, briefRevision and sha256 (written by the import, never typed in).</summary>
        public string footprintsSource = "";
        /// <summary>sha256 of the canonical surfaceSigns.footprints that were imported (the pins bind this, not the whole file).</summary>
        public string footprintsSha = "";
        /// <summary>The brief revision whose footprints the asset matches (current even when the whole-file sha lags).</summary>
        public string footprintsRev = "";

        /// <summary>The footprint for a kind, or false if none was imported (the sign then uses its legacy look).</summary>
        public bool TryFootprint(FishSurfaceKind kind, out Footprint f)
        {
            for (int i = 0; i < footprints.Length; i++) if (footprints[i].kind == kind) { f = footprints[i]; return true; }
            f = default;
            return false;
        }
        public Variant[] variants = new Variant[0];
        public Material water;
        [Tooltip("FishRing with enableInstancing on.")]
        public Material ring;
        public Mesh ringMesh;
    }
}
