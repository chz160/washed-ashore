using UnityEngine;

namespace WashedAshore.Level
{
    // Placed by the sky build (BellsBendSky.Build) on the "Sky" root. World.unity has no baked lighting data, so at load
    // this regenerates ambient light and the default reflection (what the water reflects) from the current skybox
    // material, on Windows and WebGL alike. One convolution at load, a few ms once.
    public class BellsBendSkyInit : MonoBehaviour
    {
        void Awake() => DynamicGI.UpdateEnvironment();
    }
}
