using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>
    /// The scene's water (spec W6/W9). Surface Y = MapConfig.WaterLevelY + WaterMotion offset, never a
    /// typed-in height. Each frame it records the water time and pushes the level, each wave's (A, k, d)
    /// and its wrapped phase phi_i(t) to shader globals, so gameplay and the water shader sample the same
    /// f(x, z, t). _WaterWaveSpeedPhase{i} = (c, phi_i(t), 0, 0). The visual-only layers get offsets wrapped
    /// in double: _WaterScroll{j} = (offset x, offset z, tileSize, 0), _WaterFlowPhase in [0, 1). No raw
    /// time reaches the shader (w-td, water/tech-pick).
    /// Runs early so every Update that frame sees the new time.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class WaterBody : MonoBehaviour
    {
        // Shader global names: agreed with the technical artist's water shader.
        public static readonly int FlowPhaseId = Shader.PropertyToID("_WaterFlowPhase");
        public static readonly int LevelId = Shader.PropertyToID("_WaterLevelY");
        static readonly int[] WaveIds = new int[WaterMotion.MaxWaves];
        static readonly int[] WaveSpeedIds = new int[WaterMotion.MaxWaves];
        static readonly int[] ScrollIds = new int[WaterMotion.MaxScrollLayers];

        static WaterBody()
        {
            for (int i = 0; i < WaterMotion.MaxWaves; i++)
            {
                WaveIds[i] = Shader.PropertyToID($"_WaterWave{i}");
                WaveSpeedIds[i] = Shader.PropertyToID($"_WaterWaveSpeedPhase{i}");
            }
            for (int j = 0; j < WaterMotion.MaxScrollLayers; j++)
                ScrollIds[j] = Shader.PropertyToID($"_WaterScroll{j}");
        }

        [SerializeField] MapConfig config;
        [SerializeField] WaterMotionSettings motion;

        double time;

        /// <summary>The enabled water in the loaded scene, or null when there is none.</summary>
        public static WaterBody Active { get; private set; }

        public MapConfig Config => config;
        public WaterMotionSettings Motion => motion;
        /// <summary>Water time this frame, seconds (WaterClock).</summary>
        public double WaterTime => time;
        public float Level => config ? config.WaterLevelY : float.NegativeInfinity;

        public void Configure(MapConfig cfg, WaterMotionSettings settings)
        {
            config = cfg;
            motion = settings;
            time = WaterClock.Now; // edit mode has no Update: push at the clock's time, not a stale one
            Push();
        }

        /// <summary>Surface height at (x, z) now.</summary>
        public float SurfaceY(float x, float z) => SurfaceY(x, z, time);

        /// <summary>Surface height at (x, z) and water time t.</summary>
        public float SurfaceY(float x, float z, double t) => Level + (motion ? motion.Offset(x, z, t) : 0f);

        void OnEnable()
        {
            if (Active != null && Active != this)
                Debug.LogWarning($"WaterBody: '{Active.name}' was already active; '{name}' replaces it.", this);
            Active = this;
            time = WaterClock.Now;
            Push();
        }

        void OnDisable()
        {
            if (Active == this) Active = null;
        }

        void Update()
        {
            time = WaterClock.Now;
            Push();
        }

        void Push()
        {
            Shader.SetGlobalFloat(LevelId, Level);
            var waves = motion ? motion.waves : null;
            for (int i = 0; i < WaterMotion.MaxWaves; i++)
            {
                bool on = waves != null && i < waves.Length && waves[i].wavelength > 0f;
                var w = on ? waves[i] : default;
                var d = on ? w.Direction : Vector2.zero;
                Shader.SetGlobalVector(WaveIds[i], on ? new Vector4(w.amplitude, w.K, d.x, d.y) : Vector4.zero);
                Shader.SetGlobalVector(WaveSpeedIds[i], on ? new Vector4(w.speed, WaterMotion.Phase(w, time), 0f, 0f) : Vector4.zero);
            }
            var layers = motion ? motion.scrollLayers : null;
            for (int j = 0; j < WaterMotion.MaxScrollLayers; j++)
            {
                bool on = layers != null && j < layers.Length && layers[j].tileSize > 0f;
                var o = on ? WaterMotion.ScrollOffset(layers[j], time) : Vector2.zero;
                Shader.SetGlobalVector(ScrollIds[j], on ? new Vector4(o.x, o.y, layers[j].tileSize, 0f) : Vector4.zero);
            }
            Shader.SetGlobalFloat(FlowPhaseId, motion ? WaterMotion.FlowPhase(motion.flowCyclePeriod, time) : 0f);
        }
    }
}
