using UnityEditor;

namespace WashedAshore.Art.Editor
{
    /// <summary>
    /// Applies <see cref="LibraryTextureImportRules"/> to the art-import outputs under
    /// Assets/World/BellsBend/Art/Library/. Every setting is written on every import, so a hand edit in the
    /// Inspector doesn't survive a reimport. No Presets: they don't re-apply on reimport.
    /// </summary>
    public class LibraryTextureImportPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => LibraryTextureImportRules.Version;

        void OnPreprocessTexture()
        {
            var rule = LibraryTextureImportRules.RuleFor(assetPath);
            if (rule == null)
            {
                if (LibraryTextureImportRules.InScope(assetPath))
                    context.LogImportWarning($"{assetPath}: no library import rule for this file name; expected a suffix of _Albedo, _AlbedoAlpha, _Normal, _Mask or _TerrainMask.");
                return;
            }
            Apply((TextureImporter)assetImporter, rule.Value);
        }

        public static void Apply(TextureImporter imp, LibraryTextureImportRules.Rule rule)
        {
            imp.textureType = rule.Type;
            imp.sRGBTexture = rule.SRgb;
            imp.ignorePngGamma = rule.IgnorePngGamma;
            imp.alphaSource = rule.AlphaSource;
            imp.alphaIsTransparency = rule.AlphaIsTransparency;
            if (rule.Type == TextureImporterType.NormalMap) imp.convertToNormalmap = false;   // already a normal map
            imp.mipmapEnabled = rule.Mipmaps;
            imp.mipmapFilter = rule.MipFilter;
            imp.mipMapsPreserveCoverage = rule.PreserveCoverage;
            imp.alphaTestReferenceValue = rule.AlphaCutoff;
            imp.crunchedCompression = rule.Crunch;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.maxTextureSize = rule.MaxSize;
            imp.anisoLevel = rule.Aniso;
            imp.filterMode = LibraryTextureImportRules.Filter;
            imp.wrapMode = LibraryTextureImportRules.Wrap;
            imp.isReadable = false;

            var s = imp.GetPlatformTextureSettings(LibraryTextureImportRules.StandalonePlatform);
            s.overridden = true;
            s.format = rule.StandaloneFormat;
            s.maxTextureSize = rule.MaxSize;
            s.compressionQuality = rule.CompressionQuality;
            s.crunchedCompression = rule.Crunch;
            s.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.SetPlatformTextureSettings(s);
        }
    }
}
