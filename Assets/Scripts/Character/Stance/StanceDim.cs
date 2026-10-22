using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WashedAshore.Gameplay;

namespace WashedAshore.Character.Stance
{
    /// <summary>
    /// The sleep dim (player-stances.md §6): a global URP Volume whose post-exposure follows <see cref="PlayerController.SleepDim"/>.
    /// Never a Canvas, text or icon (greenlight C1, no HUD). Post-processing is switched on for the main camera only while the dim
    /// is above zero, so awake frame cost is unchanged.
    /// </summary>
    public class StanceDim : MonoBehaviour
    {
        PlayerController player;
        Volume volume;
        ColorAdjustments adjust;
        UniversalAdditionalCameraData cameraData;
        bool postOn, postWasOn;

        public Volume Volume => volume;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            var go = new GameObject("SleepDimVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            adjust = profile.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(0f);
            volume.sharedProfile = profile;
        }

        void LateUpdate()
        {
            if (player == null) return;
            float dim = player.SleepDim;
            if (dim > 0f && !postOn)
            {
                var cam = player.ViewCamera != null ? player.ViewCamera : Camera.main;
                if (cam != null)
                {
                    cameraData = cam.GetUniversalAdditionalCameraData();
                    postWasOn = cameraData.renderPostProcessing;
                    cameraData.renderPostProcessing = true;
                    postOn = true;
                }
            }
            adjust.postExposure.value = player.StanceTuning.dimExposure * dim;
            volume.weight = dim > 0f ? 1f : 0f;
            if (dim <= 0f && postOn)
            {
                if (cameraData != null) cameraData.renderPostProcessing = postWasOn;
                postOn = false;
            }
        }

        void OnDestroy()
        {
            if (postOn && cameraData != null) cameraData.renderPostProcessing = postWasOn;
            if (volume != null && volume.sharedProfile != null) Destroy(volume.sharedProfile);
        }
    }
}
