using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Art.Editor
{
    /// <summary>
    /// Stage 3 of the art pipeline (high-res report section 8): texture import rules for the library outputs
    /// that tools/art-import writes under <see cref="Root"/> (decision I-2). Rules are keyed by the map role in
    /// the file name's last "_" token: _Albedo, _AlbedoAlpha, _Normal, _Mask, _TerrainMask (the art-import
    /// suffix contract). Anything outside Root, or not a .png, is left alone.
    ///
    /// Changing anything in this table means bumping <see cref="Version"/> (it is the postprocessor's
    /// GetVersion(), so Unity re-imports) and updating the golden string in LibraryTextureImportRulesTests;
    /// the fingerprint test fails until both are done. Note: a postprocessor version bump makes Unity
    /// re-import every texture in the project, not only the ones under Root.
    /// </summary>
    public static class LibraryTextureImportRules
    {
        public const uint Version = 1;
        public const string Root = "Assets/World/BellsBend/Art/Library/";
        public const string Extension = ".png";
        public const int MaxSize = 4096;
        public const int Aniso = 4;
        public const FilterMode Filter = FilterMode.Trilinear;
        public const TextureWrapMode Wrap = TextureWrapMode.Repeat;
        public const string StandalonePlatform = "Standalone";   // Windows, Linux and macOS

        /// <summary>
        /// The alpha-test cutoff for _AlbedoAlpha (foliage and cards). Mip coverage is preserved at this value, so
        /// Stage 4 materials must clip at the same value (URP Lit _Cutoff, or the shader's clip threshold).
        /// </summary>
        public const float AlphaClip = 0.5f;

        public enum MapRole { Albedo, AlbedoAlpha, Normal, Mask, TerrainMask }

        public readonly struct Rule
        {
            public readonly MapRole Role;
            public readonly TextureImporterType Type;
            public readonly bool SRgb;
            public readonly bool IgnorePngGamma;
            public readonly TextureImporterFormat StandaloneFormat;
            public readonly int CompressionQuality;
            public readonly TextureImporterMipFilter MipFilter;
            public readonly TextureImporterAlphaSource AlphaSource;
            public readonly bool AlphaIsTransparency;
            public readonly bool Crunch;
            public readonly bool Mipmaps;
            public readonly int MaxSize;
            public readonly int Aniso;
            public readonly bool PreserveCoverage;
            public readonly float AlphaCutoff;

            public Rule(MapRole role, TextureImporterType type, bool sRgb, bool ignorePngGamma, TextureImporterFormat standaloneFormat,
                int compressionQuality, TextureImporterAlphaSource alphaSource, bool alphaIsTransparency, bool preserveCoverage = false)
            {
                Role = role;
                Type = type;
                SRgb = sRgb;
                IgnorePngGamma = ignorePngGamma;
                StandaloneFormat = standaloneFormat;
                CompressionQuality = compressionQuality;
                MipFilter = TextureImporterMipFilter.KaiserFilter;
                AlphaSource = alphaSource;
                AlphaIsTransparency = alphaIsTransparency;
                Crunch = false;   // crunch can't hold BC5 or BC7
                Mipmaps = true;
                MaxSize = LibraryTextureImportRules.MaxSize;
                Aniso = LibraryTextureImportRules.Aniso;
                PreserveCoverage = preserveCoverage;   // keeps alpha-tested foliage from thinning out in the mips
                AlphaCutoff = AlphaClip;
            }
        }

        // The table. BC1 is TextureImporterFormat.DXT1. Normal maps get an explicit BC5 override, never "Automatic".
        static readonly Rule[] Table =
        {
            new Rule(MapRole.Albedo, TextureImporterType.Default, true, false, TextureImporterFormat.DXT1, 100, TextureImporterAlphaSource.None, false),
            new Rule(MapRole.AlbedoAlpha, TextureImporterType.Default, true, false, TextureImporterFormat.BC7, 100, TextureImporterAlphaSource.FromInput, true, preserveCoverage: true),
            new Rule(MapRole.Normal, TextureImporterType.NormalMap, false, false, TextureImporterFormat.BC5, 100, TextureImporterAlphaSource.None, false),
            new Rule(MapRole.Mask, TextureImporterType.Default, false, true, TextureImporterFormat.BC7, 100, TextureImporterAlphaSource.FromInput, false),
            new Rule(MapRole.TerrainMask, TextureImporterType.Default, false, true, TextureImporterFormat.BC7, 100, TextureImporterAlphaSource.FromInput, false),
        };

        static string Normalise(string assetPath) => assetPath == null ? null : assetPath.Replace('\\', '/');

        /// <summary>True for a .png (any case) under <see cref="Root"/>; the folder test is ordinal and case-sensitive.</summary>
        public static bool InScope(string assetPath)
        {
            var p = Normalise(assetPath);
            return p != null && p.StartsWith(Root, StringComparison.Ordinal)
                && p.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The role named by the last "_" token of the file stem (exact case), or null.</summary>
        public static MapRole? RoleFromPath(string assetPath)
        {
            if (!InScope(assetPath)) return null;
            var p = Normalise(assetPath);
            var file = p.Substring(p.LastIndexOf('/') + 1);
            var stem = file.Substring(0, file.Length - Extension.Length);
            int us = stem.LastIndexOf('_');
            if (us <= 0 || us == stem.Length - 1) return null;
            var token = stem.Substring(us + 1);
            foreach (MapRole r in Enum.GetValues(typeof(MapRole)))
                if (string.Equals(token, r.ToString(), StringComparison.Ordinal)) return r;
            return null;
        }

        public static Rule? RuleFor(string assetPath)
        {
            var role = RoleFromPath(assetPath);
            return role.HasValue ? RuleFor(role.Value) : (Rule?)null;
        }

        public static Rule RuleFor(MapRole role)
        {
            foreach (var r in Table)
                if (r.Role == role) return r;
            throw new ArgumentOutOfRangeException(nameof(role), role, "no import rule");
        }

        public static IReadOnlyList<Rule> Rules => Table;

        // Explicit names: some importer enums carry obsolete aliases with the same value, so ToString() isn't stable.
        static string Name(TextureImporterType t) => t == TextureImporterType.NormalMap ? "NormalMap" : t == TextureImporterType.Default ? "Default" : ((int)t).ToString();

        static string Name(TextureImporterFormat f) =>
            f == TextureImporterFormat.DXT1 ? "BC1" : f == TextureImporterFormat.BC7 ? "BC7" : f == TextureImporterFormat.BC5 ? "BC5" : ((int)f).ToString();

        static string Name(TextureImporterMipFilter m) => m == TextureImporterMipFilter.KaiserFilter ? "Kaiser" : m == TextureImporterMipFilter.BoxFilter ? "Box" : ((int)m).ToString();

        static string Name(TextureImporterAlphaSource a) =>
            a == TextureImporterAlphaSource.None ? "None" : a == TextureImporterAlphaSource.FromInput ? "FromInput" : ((int)a).ToString();

        static string B(bool b) => b ? "1" : "0";

        /// <summary>A stable text form of the whole table, checked by a golden test.</summary>
        public static string Fingerprint()
        {
            var sb = new StringBuilder();
            sb.Append("v").Append(Version).Append('|').Append(Root).Append('|').Append(StandalonePlatform)
              .Append("|filter=").Append(Filter == FilterMode.Trilinear ? "Trilinear" : ((int)Filter).ToString())
              .Append("|wrap=").Append(Wrap == TextureWrapMode.Repeat ? "Repeat" : ((int)Wrap).ToString());
            foreach (var r in Table)
            {
                sb.Append('|').Append(r.Role).Append(':').Append(Name(r.Type))
                  .Append(":srgb=").Append(B(r.SRgb)).Append(":ipg=").Append(B(r.IgnorePngGamma))
                  .Append(':').Append(Name(r.StandaloneFormat)).Append(":q=").Append(r.CompressionQuality)
                  .Append(":mip=").Append(B(r.Mipmaps)).Append(Name(r.MipFilter))
                  .Append(":alpha=").Append(Name(r.AlphaSource)).Append(":ait=").Append(B(r.AlphaIsTransparency))
                  .Append(":crunch=").Append(B(r.Crunch)).Append(":max=").Append(r.MaxSize).Append(":aniso=").Append(r.Aniso)
                  .Append(":cov=").Append(B(r.PreserveCoverage)).Append('@').Append(r.AlphaCutoff.ToString("0.###", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
