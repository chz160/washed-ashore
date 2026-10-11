using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace WashedAshore.Fish.Editor
{
    /// <summary>
    /// G3 stand-in mapping (research artist-items-3-5.md §4; f-director ruling/fish-roster-look: gar V2 shadow, buffalo Fish1)
    /// as the bake reads it: per brief variant id, the model, the non-uniform Y/X scale and the back/belly/fin swatches (sRGB,
    /// already capped at HSV saturation 0.40 / value 0.82) with mottle. Variant ids and their order come from f-designer's
    /// fish-targets.json (species, then schools), so no id is typed twice; a variant id with no look here fails the bake.
    /// Sibling variants (longear/redear, black crappie, bigmouth buffalo, spotted/shortnose gar, threadfin) use their
    /// species' look with +-5% value.
    /// </summary>
    public static class FishLooks
    {
        public const string TargetsPath = "_bmad-output/poc/fish-targets.json";

        public struct Look
        {
            public string model; public float y, x; public string back, belly, fins; public float mottle;
            public Look(string model, float y, float x, string back, string belly, string fins, float mottle)
            { this.model = model; this.y = y; this.x = x; this.back = back; this.belly = belly; this.fins = fins; this.mottle = mottle; }
        }

        static readonly Dictionary<string, Look> Species = new Dictionary<string, Look>
        {
            ["Bluegill"] = new Look("Fish2", 0.94f, 1.0f, "4F5A48", "A08F60", "4A4E40", 0.10f),
            ["LargemouthBass"] = new Look("Fish1", 1.20f, 1.1f, "5C6440", "C9C4A0", "6A6A48", 0.20f),
            ["SpottedBass"] = new Look("Fish1", 1.15f, 1.05f, "646040", "C8C0A0", "64604A", 0.25f),
            ["SmallmouthBass"] = new Look("Fish1", 1.15f, 1.05f, "6E6042", "C2B48E", "6A5A40", 0.25f),
            ["WhiteCrappie"] = new Look("Fish2", 0.83f, 0.9f, "5E6656", "B9BCB2", "5A5E52", 0.30f),
            ["ChannelCatfish"] = new Look("Fish1", 0.86f, 1.2f, "5A6052", "B8B49C", "4E5048", 0.10f),
            ["FlatheadCatfish"] = new Look("Fish1", 0.80f, 1.4f, "6B5B40", "B5A783", "5A4C36", 0.35f),
            ["BlueCatfish"] = new Look("Fish1", 0.95f, 1.2f, "5E6670", "C2C4BE", "565C62", 0.00f),
            ["FreshwaterDrum"] = new Look("Fish1", 1.40f, 1.15f, "6E7270", "BEBEB4", "6A6C68", 0.00f),
            ["SmallmouthBuffalo"] = new Look("Fish1", 1.55f, 1.2f, "5E5A48", "B0AA92", "56524A", 0.05f),
            ["CommonCarp"] = new Look("Fish1", 1.40f, 1.2f, "6C6241", "B8A570", "6A5640", 0.10f),
            ["LongnoseGar"] = new Look("Fish1", 0.43f, 0.6f, "5E5A3E", "B3A986", "5A5438", 0.40f),
            ["Sauger"] = new Look("Fish1", 0.80f, 0.9f, "66603E", "CFC8AA", "6A6448", 0.35f),
            ["WhiteBass"] = new Look("Fish1", 1.40f, 1.1f, "6A706A", "C4C6BE", "646862", 0.00f),
            ["Paddlefish"] = new Look("Fish1", 0.90f, 1.0f, "5E6468", "B4B6B2", "585E62", 0.00f), // census body only, never drawn
            ["GizzardShad"] = new Look("Fish2", 0.75f, 0.8f, "646C68", "C6C8C0", "60645E", 0.00f),
            ["SkipjackHerring"] = new Look("Fish1", 1.15f, 0.85f, "566A66", "C8CCC4", "5E6462", 0.00f),
        };

        // Sibling variant -> (species look, value multiplier).
        static readonly Dictionary<string, (string species, float value)> Siblings = new Dictionary<string, (string, float)>
        {
            ["LongearSunfish"] = ("Bluegill", 1.05f), ["RedearSunfish"] = ("Bluegill", 0.95f),
            ["BlackCrappie"] = ("WhiteCrappie", 0.95f), ["BigmouthBuffalo"] = ("SmallmouthBuffalo", 1.05f),
            ["SpottedGar"] = ("LongnoseGar", 1.05f), ["ShortnoseGar"] = ("LongnoseGar", 0.95f),
            ["ThreadfinShad"] = ("GizzardShad", 1.05f),
        };

        public static Look For(string variant, out Color back, out Color belly, out Color fins)
        {
            float k = 1f;
            string key = variant;
            if (Siblings.TryGetValue(variant, out var sib)) { key = sib.species; k = sib.value; }
            if (!Species.TryGetValue(key, out var look)) throw new InvalidDataException($"no G3 look for variant '{variant}'");
            back = Value(look.back, k); belly = Value(look.belly, k); fins = Value(look.fins, k);
            return look;
        }

        static Color Value(string hex, float k)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            Color.RGBToHSV(c, out float h, out float s, out float v);
            // Cap after 8-bit rounding: the stored hex itself must be at or under S 0.40 / V 0.82 (f-qa F2).
            float sc = Mathf.Min(s, 0.40f), vc = Mathf.Min(v * k, 0.82f);
            for (int i = 0; i < 20; i++)
            {
                Color32 q = Color.HSVToRGB(h, sc, vc);
                Color.RGBToHSV(q, out _, out float qs, out float qv);
                if (qs <= 0.40f && qv <= 0.82f) { Color o = q; o.a = 1f; return o; }
                sc -= 0.002f; vc -= qv > 0.82f ? 0.002f : 0f;
            }
            throw new InvalidDataException($"swatch #{hex} can't be capped");
        }

        [Serializable] class Row { public string id; public string tier; public string[] variants; public float lengthMin, lengthMax, lengthTailMax; public bool neverDrawBody; }
        [Serializable] class Targets { public Row[] species; public Row[] schools; }

        /// <summary>Largest adult length (max of lengthMax, lengthTailMax) of any species drawn with this model as a body or a
        /// shadow, in metres (f-qa rev 2.7: neverDrawBody / census-only species such as paddlefish and shad don't count).</summary>
        public static float MaxLength(string model)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            var t = JsonUtility.FromJson<Targets>(File.ReadAllText(Path.Combine(root, TargetsPath)));
            return (t.species ?? new Row[0]).Concat(t.schools ?? new Row[0])
                .Where(r => !r.neverDrawBody && r.variants.Any(v => For(v, out _, out _, out _).model == model))
                .Max(r => Mathf.Max(r.lengthMax, r.lengthTailMax));
        }

        /// <summary>Variant ids in FishBodies order, each with its species' adult length range (G1).</summary>
        public static List<(string variant, string species, Vector2 length)> Variants()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            var t = JsonUtility.FromJson<Targets>(File.ReadAllText(Path.Combine(root, TargetsPath)));
            return (t.species ?? new Row[0]).Concat(t.schools ?? new Row[0])
                .SelectMany(r => r.variants.Select(v => (v, r.id, new Vector2(r.lengthMin, r.lengthMax)))).ToList();
        }
    }
}
