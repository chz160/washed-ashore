using NUnit.Framework;
using UnityEditor;
using WashedAshore.Art.Editor;
using Role = WashedAshore.Art.Editor.LibraryTextureImportRules.MapRole;

namespace WashedAshore.Art.Editor.Tests
{
    /// <summary>Pure checks of the Stage 3 rule table; nothing is imported.</summary>
    public class LibraryTextureImportRulesTests
    {
        const string Dir = "Assets/World/BellsBend/Art/Library/ambientcg/Ground037/";

        // Update together with LibraryTextureImportRules.Version whenever the table changes.
        const string GoldenV1 =
            "v1|Assets/World/BellsBend/Art/Library/|Standalone|filter=Trilinear|wrap=Repeat" +
            "|Albedo:Default:srgb=1:ipg=0:BC1:q=100:mip=1Kaiser:alpha=None:ait=0:crunch=0:max=4096:aniso=4:cov=0@0.5" +
            "|AlbedoAlpha:Default:srgb=1:ipg=0:BC7:q=100:mip=1Kaiser:alpha=FromInput:ait=1:crunch=0:max=4096:aniso=4:cov=1@0.5" +
            "|Normal:NormalMap:srgb=0:ipg=0:BC5:q=100:mip=1Kaiser:alpha=None:ait=0:crunch=0:max=4096:aniso=4:cov=0@0.5" +
            "|Mask:Default:srgb=0:ipg=1:BC7:q=100:mip=1Kaiser:alpha=FromInput:ait=0:crunch=0:max=4096:aniso=4:cov=0@0.5" +
            "|TerrainMask:Default:srgb=0:ipg=1:BC7:q=100:mip=1Kaiser:alpha=FromInput:ait=0:crunch=0:max=4096:aniso=4:cov=0@0.5";

        [TestCase(Dir + "Ground037_Albedo.png", true)]
        [TestCase(Dir + "Ground037_Albedo.PNG", true)]
        [TestCase("Assets\\World\\BellsBend\\Art\\Library\\ambientcg\\Ground037\\Ground037_Albedo.png", true)]
        [TestCase(Dir + "Ground037_Albedo.jpg", false)]
        [TestCase("Assets/World/BellsBend/Art/LibraryOld/ambientcg/Ground037_Albedo.png", false)]
        [TestCase("Assets/World/BellsBend/Art/Layers/Ground037_Albedo.png", false)]
        [TestCase("Assets/ThirdParty/ambientCG/Ground037/Ground037_Albedo.png", false)]
        [TestCase("assets/world/bellsbend/art/library/ambientcg/Ground037_Albedo.png", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void InScope(string path, bool expected) =>
            Assert.AreEqual(expected, LibraryTextureImportRules.InScope(path));

        [TestCase("Ground037_Albedo.png", Role.Albedo)]
        [TestCase("Leaf001_AlbedoAlpha.png", Role.AlbedoAlpha)]
        [TestCase("Ground037_Normal.png", Role.Normal)]
        [TestCase("Bark002_Mask.png", Role.Mask)]
        [TestCase("Ground037_TerrainMask.png", Role.TerrainMask)]
        [TestCase("Some_Long_Name_Normal.png", Role.Normal)]
        public void RoleFromSuffix(string file, Role expected) =>
            Assert.AreEqual(expected, LibraryTextureImportRules.RoleFromPath(Dir + file));

        [TestCase("Ground037_albedo.png")]
        [TestCase("Ground037_Color.png")]
        [TestCase("Ground037.png")]
        [TestCase("_Albedo.png")]
        [TestCase("Ground037_.png")]
        [TestCase("Ground037_TerrainMaskX.png")]
        public void UnknownSuffixHasNoRule(string file)
        {
            Assert.IsNull(LibraryTextureImportRules.RoleFromPath(Dir + file));
            Assert.IsNull(LibraryTextureImportRules.RuleFor(Dir + file));
        }

        [Test]
        public void OutOfScopeHasNoRuleEvenWithAKnownSuffix() =>
            Assert.IsNull(LibraryTextureImportRules.RuleFor("Assets/World/BellsBend/Art/Layers/Ground037_Albedo.png"));

        [Test]
        public void TerrainMaskIsNotReadAsMask() =>
            Assert.AreEqual(Role.TerrainMask, LibraryTextureImportRules.RuleFor(Dir + "Ground037_TerrainMask.png").Value.Role);

        [Test]
        public void Albedo_IsSrgbBc1()
        {
            var r = LibraryTextureImportRules.RuleFor(Role.Albedo);
            Assert.AreEqual(TextureImporterType.Default, r.Type);
            Assert.IsTrue(r.SRgb);
            Assert.IsFalse(r.IgnorePngGamma);
            Assert.AreEqual(TextureImporterFormat.DXT1, r.StandaloneFormat);
            Assert.AreEqual(TextureImporterAlphaSource.None, r.AlphaSource);
        }

        [Test]
        public void AlbedoAlpha_IsSrgbBc7WithAlpha()
        {
            var r = LibraryTextureImportRules.RuleFor(Role.AlbedoAlpha);
            Assert.IsTrue(r.SRgb);
            Assert.AreEqual(TextureImporterFormat.BC7, r.StandaloneFormat);
            Assert.AreEqual(TextureImporterAlphaSource.FromInput, r.AlphaSource);
            Assert.IsTrue(r.AlphaIsTransparency);
            Assert.IsTrue(r.PreserveCoverage);
            Assert.AreEqual(LibraryTextureImportRules.AlphaClip, r.AlphaCutoff);
        }

        [TestCase(Role.Albedo)]
        [TestCase(Role.Normal)]
        [TestCase(Role.Mask)]
        [TestCase(Role.TerrainMask)]
        public void OnlyAlbedoAlpha_PreservesCoverage(Role role) =>
            Assert.IsFalse(LibraryTextureImportRules.RuleFor(role).PreserveCoverage, role.ToString());

        [Test]
        public void Normal_IsNormalMapWithExplicitBc5()
        {
            var r = LibraryTextureImportRules.RuleFor(Role.Normal);
            Assert.AreEqual(TextureImporterType.NormalMap, r.Type);
            Assert.IsFalse(r.SRgb);
            Assert.AreEqual(TextureImporterFormat.BC5, r.StandaloneFormat);
        }

        [TestCase(Role.Mask)]
        [TestCase(Role.TerrainMask)]
        public void Masks_AreLinearIgnorePngGammaBc7(Role role)
        {
            var r = LibraryTextureImportRules.RuleFor(role);
            Assert.AreEqual(TextureImporterType.Default, r.Type);
            Assert.IsFalse(r.SRgb);
            Assert.IsTrue(r.IgnorePngGamma);
            Assert.AreEqual(TextureImporterFormat.BC7, r.StandaloneFormat);
            Assert.AreEqual(100, r.CompressionQuality);
            Assert.AreEqual(TextureImporterAlphaSource.FromInput, r.AlphaSource);   // A is smoothness, not transparency
            Assert.IsFalse(r.AlphaIsTransparency);
        }

        [Test]
        public void EveryRole_HasOneRule_KaiserMips_NoCrunch_PowerOfTwoMax()
        {
            var roles = System.Enum.GetValues(typeof(Role));
            Assert.AreEqual(roles.Length, LibraryTextureImportRules.Rules.Count);
            foreach (Role role in roles)
            {
                var r = LibraryTextureImportRules.RuleFor(role);
                Assert.AreEqual(role, r.Role);
                Assert.IsTrue(r.Mipmaps, role.ToString());
                Assert.AreEqual(TextureImporterMipFilter.KaiserFilter, r.MipFilter, role.ToString());
                Assert.IsFalse(r.Crunch, role.ToString());
                Assert.IsTrue(r.MaxSize > 0 && (r.MaxSize & (r.MaxSize - 1)) == 0, role.ToString());
            }
        }

        [Test]
        public void Fingerprint_MatchesGoldenForThisVersion()
        {
            Assert.GreaterOrEqual(LibraryTextureImportRules.Version, 1u);
            Assert.AreEqual(GoldenV1, LibraryTextureImportRules.Fingerprint(),
                "The rule table changed: bump LibraryTextureImportRules.Version and update the golden string.");
        }
    }
}
