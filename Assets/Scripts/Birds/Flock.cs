using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Birds
{
    /// <summary>
    /// Drives one crow flock (B5; brief 3.3). A lead point orbits the primary POI (radius 20-35 m, 9-12 m/s,
    /// random direction), dwells 50-90 s, then commutes at 12 m/s to the secondary POI and orbits there, and so on.
    /// Each bird holds a fixed horizontal slot in the lead's frame (drawn with the spacing target plus wobble, all
    /// within the cohesion radius), so spacing never closes. Each bird's height follows, with smoothing, its own
    /// target: the terrain under it + the flock's mean AGL + a +/-5 m wave (level while commuting), never below the
    /// highest crown within 30 m + 10 m, inside the configured AGL band.
    /// </summary>
    public class Flock : MonoBehaviour
    {
        public enum Mode { Orbit, Commute }

        FlockTuning t;
        BirdTerrain ground;
        System.Random rng;
        readonly List<FlockBird> birds = new List<FlockBird>();
        readonly List<Vector2> slots = new List<Vector2>();
        readonly List<float> lift = new List<float>(), wobblePhase = new List<float>();
        float[] y, crownTop;
        Vector3[] prev;
        Vector3 lead, poi, otherPoi;
        float heading, speed, orbitSpeed, orbitRadius, sign, meanAgl, commuteAgl, wavePeriod, wavePhase;
        float dwell, time, crownTimer, turnRate;

        public FlockPlan Plan { get; private set; }
        public Mode Current { get; private set; }
        public int Commutes { get; private set; }
        public IReadOnlyList<FlockBird> Birds => birds;
        public Vector3 Lead => lead;
        public float OrbitRadius => orbitRadius;

        // ---- Test hooks (positive controls) ----
        /// <summary>Added to every bird's height after all limits (altitude/canopy control).</summary>
        public float AltitudeOffset { get; set; }
        /// <summary>Collapse every slot onto the lead (spacing control).</summary>
        public bool CollapseSlots { get; set; }

        public void Init(BirdTuning tuning, FlockPlan plan, GameObject prefab, int runSeed, int index, int layer)
        {
            t = tuning.flock;
            Plan = plan;
            ground = BirdTerrain.Active;
            rng = BirdRandom.For(runSeed, 1000 + index);
            sign = rng.NextDouble() < 0.5 ? -1f : 1f;
            orbitRadius = rng.Range(t.orbitRadius);
            orbitSpeed = speed = rng.Range(t.orbitSpeed);
            meanAgl = rng.Range(t.orbitMeanAltitude);
            commuteAgl = rng.Range(t.commuteAltitude);
            wavePeriod = rng.Range(t.wavePeriod);
            wavePhase = rng.Range(0f, wavePeriod);
            poi = plan.primary;
            otherPoi = plan.secondary;
            Current = Mode.Orbit;
            dwell = rng.Range(0f, t.dwellSeconds.y); // random first-dwell phase so flocks don't move together
            float a = rng.Range(0f, Mathf.PI * 2f);
            lead = poi + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * orbitRadius;
            Vector3 tangent = Tangent(lead);
            heading = Mathf.Atan2(tangent.x, tangent.z);

            float span = 0f;
            for (int i = 0; i < plan.size; i++)
            {
                var go = Instantiate(prefab, lead, Quaternion.identity, transform);
                go.name = $"{plan.name}_Crow_{i + 1}";
                go.transform.localScale = prefab.transform.localScale * t.scale;
                if (layer >= 0) SetLayer(go.transform, layer);
                var bird = go.GetComponent<FlockBird>();
                if (!bird) bird = go.AddComponent<FlockBird>(); // no '??': GetComponent can return a fake null in the Editor
                birds.Add(bird);
                var b = bird.VisualBounds;
                span = Mathf.Max(span, Mathf.Max(b.size.x, b.size.z));
            }
            DrawSlots(plan.size, Mathf.Max(t.targetSpacing, span + 0.3f));

            y = new float[birds.Count];
            crownTop = new float[birds.Count];
            prev = new Vector3[birds.Count];
            for (int i = 0; i < birds.Count; i++)
            {
                Vector3 xz = SlotXZ(i);
                crownTop[i] = ground.CanopyTop(xz, t.crownSearchRadius);
                y[i] = TargetY(i, xz);
                prev[i] = new Vector3(xz.x, y[i], xz.z);
                birds[i].transform.position = prev[i];
                birds[i].Init(this, t, rng);
            }
        }

        static void SetLayer(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            foreach (Transform c in root) SetLayer(c, layer);
        }

        /// <summary>Rejection-samples horizontal slots (along, lateral) with spacing >= separation + 2 x wobble,
        /// every slot within the cohesion radius of the lead (less the wobble).</summary>
        void DrawSlots(int n, float separation)
        {
            float min = separation + 2f * t.wobbleAmplitude;
            float maxR = t.cohesionRadius - t.wobbleAmplitude - 1f;
            Vector2 half = t.spreadPerBird * Mathf.Sqrt(n);
            for (int grow = 0; grow < 30; grow++, half *= 1.1f)
            {
                slots.Clear();
                for (int tries = 0; tries < 400 * n && slots.Count < n; tries++)
                {
                    var c = new Vector2(rng.Range(-half.x, half.x), rng.Range(-half.y, half.y));
                    if (c.magnitude > maxR) continue;
                    bool ok = true;
                    foreach (var s in slots) if ((s - c).sqrMagnitude < min * min) { ok = false; break; }
                    if (ok) slots.Add(c);
                }
                if (slots.Count == n) break;
            }
            if (slots.Count < n) Debug.LogError($"{name}: can't fit {n} birds {min:F1} m apart within {maxR:F1} m", this);
            while (slots.Count < n) slots.Add(new Vector2(slots.Count * min, 0f));
            for (int i = 0; i < n; i++)
            {
                lift.Add(rng.Range(-t.verticalJitter, t.verticalJitter));
                wobblePhase.Add(rng.Range(0f, Mathf.PI * 2f));
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || birds.Count == 0) return;
            time += dt;
            Steer(dt);

            bool lookup = (crownTimer -= dt) <= 0f;
            if (lookup) crownTimer = t.crownLookupInterval;
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(t.altitudeSmoothing, 1e-3f));
            float bank = Mathf.Clamp(Mathf.Atan2(speed * turnRate, 9.81f) * Mathf.Rad2Deg, -t.maxBankDegrees, t.maxBankDegrees);
            for (int i = 0; i < birds.Count; i++)
            {
                if (!birds[i]) continue;
                Vector3 xz = SlotXZ(i);
                if (lookup) crownTop[i] = ground.CanopyTop(xz, t.crownSearchRadius);
                y[i] += (TargetY(i, xz) - y[i]) * k;
                // Hard floors under the smoothing: half the crown clearance (the gate is crown + 5 m) and the band floor.
                float g = ground.Height(xz);
                float floor = Mathf.Max(crownTop[i] + t.crownClearance * 0.5f, g + t.altitudeBand.x - 4f);
                y[i] = Mathf.Max(y[i], floor);
                Vector3 p = new Vector3(xz.x, y[i] + AltitudeOffset, xz.z);
                Vector3 v = (p - prev[i]) / dt;
                prev[i] = p;
                birds[i].SetPose(p, v, bank, dt);
            }
        }

        /// <summary>Orbit the current POI, or fly straight at the next one; the heading turns at a bounded rate.</summary>
        void Steer(float dt)
        {
            Vector3 desired;
            float targetSpeed;
            if (Current == Mode.Orbit)
            {
                Vector3 to = lead - poi;
                to.y = 0f;
                float r = Mathf.Max(to.magnitude, 0.01f);
                Vector3 radial = to / r;
                desired = (Tangent(lead) - radial * Mathf.Clamp((r - orbitRadius) / 10f, -1f, 1f)).normalized;
                targetSpeed = orbitSpeed;
                if ((dwell -= dt) <= 0f)
                {
                    Current = Mode.Commute;
                    Commutes++;
                    (poi, otherPoi) = (otherPoi, poi);
                }
            }
            else
            {
                desired = poi - lead;
                desired.y = 0f;
                targetSpeed = t.commuteSpeed;
                if (desired.magnitude <= orbitRadius)
                {
                    Current = Mode.Orbit;
                    dwell = rng.Range(t.dwellSeconds);
                }
                desired.Normalize();
            }
            float want = Mathf.Atan2(desired.x, desired.z);
            float maxTurn = speed / (t.orbitRadius.x * 0.8f);
            float delta = Mathf.Clamp(Mathf.DeltaAngle(heading * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad, -maxTurn * dt, maxTurn * dt);
            heading += delta;
            turnRate = delta / dt;
            speed = Mathf.MoveTowards(speed, targetSpeed, 1f * dt);
            lead += new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)) * (speed * dt);
        }

        Vector3 Tangent(Vector3 at)
        {
            Vector3 to = at - poi;
            to.y = 0f;
            to.Normalize();
            return new Vector3(-to.z, 0f, to.x) * sign;
        }

        Vector3 SlotXZ(int i)
        {
            Vector2 s = CollapseSlots ? Vector2.zero : slots[i];
            // Two axes at amplitude / sqrt(2): the combined wobble never exceeds wobbleAmplitude.
            float amp = CollapseSlots ? 0f : t.wobbleAmplitude * 0.7071f;
            float w1 = Mathf.Sin(2f * Mathf.PI * time / t.wobblePeriod + wobblePhase[i]) * amp;
            float w2 = Mathf.Cos(2f * Mathf.PI * time / (t.wobblePeriod * 1.3f) + wobblePhase[i]) * amp;
            Vector3 fwd = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            return lead + fwd * (s.x + w1) + right * (s.y + w2);
        }

        float TargetY(int i, Vector3 xz)
        {
            float g = ground.Height(xz);
            float agl = Current == Mode.Commute
                ? commuteAgl
                : meanAgl + t.waveAmplitude * Mathf.Sin(2f * Mathf.PI * (time + wavePhase) / wavePeriod);
            float target = Mathf.Clamp(g + agl + lift[i], g + t.altitudeBand.x, g + t.altitudeBand.y);
            return Mathf.Max(target, crownTop[i] + t.crownClearance);
        }
    }
}
