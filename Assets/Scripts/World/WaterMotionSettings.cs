using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>
    /// The one source of the surface motion constants (spec W9). WaterBody evaluates them in C#
    /// and pushes them to the water shader, so both read the same numbers.
    /// </summary>
    [CreateAssetMenu(fileName = "WaterMotion", menuName = "Washed Ashore/Water Motion")]
    public class WaterMotionSettings : ScriptableObject
    {
        [Tooltip("Wind ripples, at most WaterMotion.MaxWaves. Greenlight: near-flat, so keep amplitudes small.")]
        public WaterWave[] waves = new WaterWave[0];
        [Tooltip("Visual-only scrolling normal layers, at most WaterMotion.MaxScrollLayers. No height term.")]
        public WaterScrollLayer[] scrollLayers = new WaterScrollLayer[0];
        [Tooltip("Flow-map cycle period, seconds (two-phase flow). 0 disables.")]
        public float flowCyclePeriod;

        public float Offset(float x, float z, double t) => WaterMotion.Offset(waves, x, z, t);

        void OnValidate()
        {
            if (waves != null && waves.Length > WaterMotion.MaxWaves)
                Debug.LogWarning($"WaterMotionSettings: only the first {WaterMotion.MaxWaves} waves are used.", this);
        }
    }
}
