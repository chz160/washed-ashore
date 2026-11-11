using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Character.Editor
{
    /// <summary>
    /// Import standard for the modular character FBX files (spec H1): Generic rig with an avatar from the model, no cameras or
    /// lights, part files import no animation, and clip takes are renamed to their short names ("CharacterArmature|CharacterArmature|Run"
    /// becomes "Run") with the looping ones set to loop. Runs before ModelFacingPostprocessor, which still applies its facing turn.
    /// </summary>
    public class CharacterImportSettings : AssetPostprocessor
    {
        const uint Version = 5;

        public const string PackRoot = "Assets/ThirdParty/Quaternius/ModularCharacters";
        public const string ClipFolder = "Assets/Art/Character/Clips";

        /// <summary>The Survivor (Meshy, retargeted onto the shared Skeleton_Human names): body and head parts, textures and the one material.</summary>
        public const string SurvivorRoot = "Assets/Art/Character/Survivor";

        /// <summary>The shared Skeleton_Human FBX (armature only, rest pose): the source of Skeleton_Human.prefab.</summary>
        public const string SkeletonHumanPath = "Assets/Art/Character/Skeleton_Human.fbx";

        /// <summary>
        /// Blend-shape normals mode of the Survivor body: engineer-49's pick, PROVISIONAL (td-49 ruled only mesh Normals = Import). Import keeps the imported smooth normals under the
        /// knee shapes if the FBX carries no shape normals; Calculate would rebuild them from the smoothing angle and could facet the shin the director rejected. The import test asserts
        /// this value and prints the shapes' position and normal deltas; the director's in-engine knee at 90 degrees decides, with None as the fallback if Unity calculated the
        /// deltas and it facets.
        /// </summary>
        public const ModelImporterNormals SurvivorBlendShapeNormals = ModelImporterNormals.Import;

        public static bool IsSurvivorAsset(string path) => path.StartsWith(SurvivorRoot + "/", System.StringComparison.Ordinal);

        // Clips that loop. Everything else (enter/exit/land/start/interact) plays once.
        public static readonly HashSet<string> LoopClips = new HashSet<string>
        {
            "Idle", "Idle_Neutral", "Stand_Idle", "Walk", "Run", "Run_Back", "Run_Left", "Run_Right",
            "Jog", "JogBack", "JogLeft", "JogRight", "Sprint", "SprintBack", "SprintLeft", "SprintRight",
            "CrouchWalk", "CrouchWalkBack", "CrouchWalkLeft", "CrouchWalkRight", "Crouch_Idle",
            "Squat_Hold", "Sit_Loop", "Sleep_Loop", "Jump_Air",
        };

        public override uint GetVersion() => Version;

        public static bool IsCharacterModel(string path) =>
            path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) &&
            (path.StartsWith(PackRoot + "/") || path.StartsWith(ClipFolder + "/") || IsSurvivorAsset(path) || path == SkeletonHumanPath);

        /// <summary>Animations.fbx and Authored_*.fbx carry clips; the part files carry only a mesh.</summary>
        public static bool CarriesClips(string path)
        {
            string file = System.IO.Path.GetFileName(path);
            return file == "Animations.fbx" || file == "Pack_Retargeted.fbx" || file.StartsWith("Authored_");
        }

        public static string ShortName(string takeName)
        {
            int cut = takeName.LastIndexOf('|');
            string name = cut >= 0 ? takeName.Substring(cut + 1) : takeName;
            // Blender names a re-exported action "Walk.001" when "Walk" already exists in the file; the clip name the game looks up has no such suffix.
            return System.Text.RegularExpressions.Regex.Replace(name, @"\.\d{3}$", "");
        }

        void OnPreprocessModel()
        {
            if (!IsCharacterModel(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = CarriesClips(assetPath);
            bool survivor = IsSurvivorAsset(assetPath);
            mi.importBlendShapes = survivor;   // only the Survivor body carries shapes (Knee_L_90, Knee_R_90)
            if (survivor)
            {
                mi.importNormals = ModelImporterNormals.Import;            // never Calculate: Unity would recalculate and bring the faceting back (td-49 DK2)
                mi.importBlendShapeNormals = SurvivorBlendShapeNormals;
                mi.importTangents = ModelImporterTangents.None;            // no normal map
                mi.weldVertices = false;                                   // keep the authored seams, normals and UV splits
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.isReadable = false;
                mi.skinWeights = ModelImporterSkinWeights.Standard;        // 4 bones per vertex, as the project's quality setting
            }
            mi.importCameras = false;
            mi.importLights = false;
            mi.optimizeGameObjects = false; // the assembler finds bones by name
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.animationCompression = ModelImporterAnimationCompression.Off; // exact keys for the foot-plant measurements
        }

        // Survivor textures (art-49 import standard sections 2 and 7, td-49 F10/F11): base colour sRGB, 4096 BC7 PROVISIONAL (the 4096-vs-2048 A/B decides the final size);
        // MetallicGloss (R metallic, A smoothness) linear, 2048 BC7. Mips on, trilinear, not readable, Standalone override.
        public const int SurvivorBaseColorMaxSize = 4096, SurvivorMetallicGlossMaxSize = 2048;
        public static bool IsSurvivorBaseColor(string path) => IsSurvivorAsset(path) && System.IO.Path.GetFileName(path).Contains("BaseColor");
        public static bool IsSurvivorMetallicGloss(string path) => IsSurvivorAsset(path) && System.IO.Path.GetFileName(path).Contains("MetallicGloss");

        void OnPreprocessTexture()
        {
            bool baseColor = IsSurvivorBaseColor(assetPath), gloss = IsSurvivorMetallicGloss(assetPath);
            if (!baseColor && !gloss) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = baseColor;
            ti.alphaSource = gloss ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;   // the alpha of MetallicGloss is smoothness, not transparency
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = baseColor ? 8 : 4;
            ti.isReadable = false;
            ti.streamingMipmaps = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            int size = baseColor ? SurvivorBaseColorMaxSize : SurvivorMetallicGlossMaxSize;
            ti.maxTextureSize = size;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Standalone", overridden = true, maxTextureSize = size, format = TextureImporterFormat.BC7,
                textureCompression = TextureImporterCompression.CompressedHQ, compressionQuality = 50,
            });
        }

        void OnPreprocessAnimation()
        {
            if (!IsCharacterModel(assetPath) || !CarriesClips(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = ShortName(c.name);
                bool loop = LoopClips.Contains(c.name.Replace("_Low", "").Replace("_High", "").Replace("_Full", ""));
                c.loopTime = loop;
                c.loopPose = loop;
            }
            mi.clipAnimations = clips;
        }
    }
}
