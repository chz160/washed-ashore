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
        const uint Version = 3;

        public const string PackRoot = "Assets/ThirdParty/Quaternius/ModularCharacters";
        public const string ClipFolder = "Assets/Art/Character/Clips";

        // Clips that loop. Everything else (enter/exit/land/start/interact) plays once.
        public static readonly HashSet<string> LoopClips = new HashSet<string>
        {
            "Idle", "Idle_Neutral", "Walk", "Run", "Run_Back", "Run_Left", "Run_Right",
            "Jog", "JogBack", "JogLeft", "JogRight", "Sprint", "SprintBack", "SprintLeft", "SprintRight",
            "CrouchWalk", "CrouchWalkBack", "CrouchWalkLeft", "CrouchWalkRight", "Crouch_Idle",
            "Squat_Hold", "Sit_Loop", "Sleep_Loop", "Jump_Air",
        };

        public override uint GetVersion() => Version;

        public static bool IsCharacterModel(string path) =>
            path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) &&
            (path.StartsWith(PackRoot + "/") || path.StartsWith(ClipFolder + "/"));

        /// <summary>Animations.fbx and Authored_*.fbx carry clips; the part files carry only a mesh.</summary>
        public static bool CarriesClips(string path)
        {
            string file = System.IO.Path.GetFileName(path);
            return file == "Animations.fbx" || file.StartsWith("Authored_");
        }

        public static string ShortName(string takeName)
        {
            int cut = takeName.LastIndexOf('|');
            return cut >= 0 ? takeName.Substring(cut + 1) : takeName;
        }

        void OnPreprocessModel()
        {
            if (!IsCharacterModel(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = CarriesClips(assetPath);
            mi.importBlendShapes = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.optimizeGameObjects = false; // the assembler finds bones by name
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.animationCompression = ModelImporterAnimationCompression.Off; // exact keys for the foot-plant measurements
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
