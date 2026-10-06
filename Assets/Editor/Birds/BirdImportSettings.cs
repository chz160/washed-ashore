using System;
using System.Linq;
using UnityEditor;

namespace WashedAshore.Birds.Editor
{
    /// <summary>
    /// Technical-art import standard for the two bird models, so a clean checkout imports them the same way:
    /// Generic rig (avatar from the model), blend shapes on (the Robin's "Wings_Folded" is keyed per clip),
    /// no material import (BirdSetup builds URP Lit materials), clip names without the "Armature|" take
    /// prefix, and loop flags per clip. Runs before ModelFacingPostprocessor's facing fix, which still applies.
    /// </summary>
    public class BirdImportSettings : AssetPostprocessor
    {
        const uint Version = 2;

        public const string RobinFbx = "Assets/ThirdParty/SoltorchGames/AmericanRobin/American_Robin_Mesh.fbx";
        public const string PigeonFbx = "Assets/ThirdParty/PeripheralArbor/Pigeon/Pigeon.fbx";

        // Loop flags follow bd-engineer's runtime interface: every Robin clip loops except A_Flutter, the
        // one-shot burst before A_Fly_01; the Pigeon's Takeoff and Landing play once.
        static readonly string[] RobinOnce = { "A_Flutter" };
        static readonly string[] PigeonLoops = { "Flapping", "Gliding", "Standing Idle" };

        public override uint GetVersion() => Version;

        static bool IsBird(string path) => path == RobinFbx || path == PigeonFbx;

        void OnPreprocessModel()
        {
            if (!IsBird(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.importBlendShapes = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importCameras = false;
            mi.importLights = false;
            mi.animationCompression = ModelImporterAnimationCompression.Optimal;
        }

        void OnPreprocessAnimation()
        {
            if (!IsBird(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            bool robin = assetPath == RobinFbx;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = ClipName(c.takeName);
                c.loopTime = robin ? !RobinOnce.Contains(c.name)
                                   : PigeonLoops.Any(l => c.name.StartsWith(l, StringComparison.Ordinal));
                c.loopPose = false;
            }
            mi.clipAnimations = clips;
        }

        public static string ClipName(string takeName)
        {
            int cut = takeName.LastIndexOf('|');
            return cut >= 0 ? takeName.Substring(cut + 1) : takeName;
        }
    }
}
