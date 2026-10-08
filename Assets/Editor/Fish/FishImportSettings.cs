using UnityEditor;

namespace WashedAshore.Fish.Editor
{
    /// <summary>
    /// Technical-art import standard for the two fish stand-ins (spec F1), so a clean checkout imports them the same
    /// way: Generic rig (avatar from the model), embedded vendor materials kept only for their names (the fish are drawn by the fish shader), no
    /// cameras or lights, and the single take renamed to "Swim" and looped (Fish2's take is "Swim.001").
    /// Runs before ModelFacingPostprocessor's facing fix, which still applies. Files come from tools/fish/extract_fish.ps1.
    /// </summary>
    public class FishImportSettings : AssetPostprocessor
    {
        const uint Version = 2;

        public const string Folder = "Assets/ThirdParty/Quaternius/AnimatedFishPack";
        public const string Fish1Fbx = Folder + "/FBX/Fish1.fbx";
        public const string Fish2Fbx = Folder + "/FBX/Fish2.fbx";
        public static readonly string[] Models = { Fish1Fbx, Fish2Fbx };
        public const string ClipName = "Swim";

        public override uint GetVersion() => Version;

        static bool IsFish(string path) => path == Fish1Fbx || path == Fish2Fbx;

        void OnPreprocessModel()
        {
            if (!IsFish(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.importBlendShapes = false;
            // Materials are imported embedded only so the submeshes keep their vendor names (Top/Bottom/Fins, Body/Front/Fins),
            // which map them to back/belly/fin regions; nothing renders with them (the fish shader is used instead).
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = true; // the VAT bake reads the mesh
            mi.animationCompression = ModelImporterAnimationCompression.Off; // the seam check and bake sample exact keys
        }

        void OnPreprocessAnimation()
        {
            if (!IsFish(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = ClipName;
                c.loopTime = true;
                // Fish1/Fish2 Swim: the Tail curve is one key out of step with the rest (its last key != its first, every
                // other bone's last key == first), a jump at the wrap. Loop Pose fixes it for any Animator playback; the VAT bake applies
                // the same correction itself (SampleAnimation ignores Loop Pose), see FishImportCheck.Seam.
                c.loopPose = true;
            }
            mi.clipAnimations = clips;
        }
    }
}
