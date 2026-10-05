using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WashedAshore.Wildlife
{
    /// <summary>
    /// One animal: graze or idle, wander with its group, stop and watch the player when alert, and
    /// flee (deer, stag, fox) or keep its distance (wolf). Numbers come from <see cref="WildlifeTuning"/>
    /// (density brief 3.3). The agent drives the transform itself (updatePosition off) so the body
    /// sits on the terrain rather than the coarser NavMesh, and the Animator "Speed" is the agent's
    /// real ground speed so the feet don't slide.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class WildlifeAgent : MonoBehaviour
    {
        public enum State { Graze, Wander, Alert, Flee, KeepDistance, Calm }

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int GrazingId = Animator.StringToHash("Grazing");
        static readonly int AnimSpeedId = Animator.StringToHash("AnimSpeed");
        static readonly float[] EscapeAngles = { 0f, 35f, -35f, 70f, -70f, 105f, -105f, 140f, -140f };
        static readonly List<WildlifeAgent> all = new List<WildlifeAgent>();

        [SerializeField] WildlifeSpecies species;
        [SerializeField] WildlifeTuning tuning;
        [SerializeField] Animator animator;
        [Tooltip("Keep the body on the terrain height instead of the NavMesh approximation.")]
        [SerializeField] bool snapToTerrain = true;

        NavMeshAgent agent;
        WildlifeHerd herd;
        SpeciesTuning t;
        Transform threat;
        Terrain terrain;
        Renderer[] renderers;
        System.Random rng;
        float yaw;
        float stateTimer;
        float threatTimer;
        float escapeTime;
        float stuckTimer;
        bool started;
        bool hasGrazingParam;
        bool hasAnimSpeedParam;

        public static IReadOnlyList<WildlifeAgent> All => all;
        public WildlifeSpecies Species => species;
        public WildlifeTuning Tuning => tuning;
        public SpeciesTuning SpeciesTuning => t;
        public NavMeshAgent Agent => agent;
        public Animator Animator => animator;
        public WildlifeHerd Herd => herd;
        public State Current { get; private set; }
        public bool IsGrazing { get; private set; }
        public bool IsEscaping => Current == State.Flee || Current == State.KeepDistance;
        /// <summary>Test hook (A4 facing positive control): extra model yaw on this one agent, default 0.</summary>
        public float ExtraModelYaw { get; set; }

        public Bounds VisualBounds
        {
            get
            {
                if (renderers == null || renderers.Length == 0) return new Bounds(transform.position, Vector3.one);
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                return b;
            }
        }

        void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            if (!animator) animator = GetComponentInChildren<Animator>();
            renderers = GetComponentsInChildren<Renderer>();
            herd = GetComponentInParent<WildlifeHerd>();
            if (animator)
                foreach (var p in animator.parameters)
                {
                    if (p.nameHash == GrazingId) hasGrazingParam = true;
                    if (p.nameHash == AnimSpeedId) hasAnimSpeedParam = true;
                }
        }

        void OnEnable()
        {
            all.Add(this);
            if (herd) herd.Register(this);
        }

        void OnDisable()
        {
            all.Remove(this);
            if (herd) herd.Unregister(this);
        }

        void Start()
        {
            if (!tuning)
            {
                Debug.LogError($"{name}: WildlifeAgent has no WildlifeTuning; disabling.", this);
                enabled = false;
                return;
            }
            t = tuning.Get(species);
            rng = WildlifeRandom.For(tuning, transform.position);
            terrain = Terrain.activeTerrain;
            yaw = transform.eulerAngles.y;

            // Prefabs ship with the NavMeshAgent disabled: in a player, a scene-load OnEnable can run
            // before NavMeshSurface adds its data ("Failed to create agent"). Start runs after every
            // OnEnable, so the NavMesh is there now.
            agent.updatePosition = false;
            agent.updateRotation = false;
            agent.acceleration = t.acceleration;
            agent.angularSpeed = t.angularSpeed;
            agent.autoBraking = true;
            agent.enabled = true;
            if (!agent.isOnNavMesh && !TryRecoverOntoNavMesh())
                Debug.LogError($"{name}: no NavMesh within 10 m of {transform.position}", this);
            FindThreat();
            started = true;
            StartGraze(Range(0f, t.idleSeconds.y)); // stagger so groups don't move in lockstep
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!agent.isOnNavMesh && !TryRecoverOntoNavMesh()) return;

            threatTimer -= dt;
            if (threatTimer <= 0f)
            {
                EvaluateThreat(tuning.threatCheckInterval - threatTimer);
                threatTimer = tuning.threatCheckInterval;
            }

            stateTimer -= dt;
            switch (Current)
            {
                case State.Graze:
                    if (stateTimer <= 0f) StartWander();
                    break;
                case State.Calm:
                    if (stateTimer <= 0f) StartWander();
                    break;
                case State.Wander:
                    if (Arrived()) StartGraze(Range(t.idleSeconds.x, t.idleSeconds.y));
                    break;
                case State.Flee:
                case State.KeepDistance:
                    // Re-aim on the timer, or early on arrival (but not every frame when cornered).
                    if (stateTimer <= 0f || (Arrived() && stateTimer < 0.75f)) Escape();
                    break;
            }

            TrackStuck(dt);
            SyncTransform(dt);
            Animate(dt);
        }

        // ---- Decisions -------------------------------------------------------------------------

        void FindThreat()
        {
            var player = GameObject.FindWithTag(tuning.playerTag);
            threat = player ? player.transform : null;
        }

        float ThreatDistance() => threat ? Flat(agent.nextPosition, threat.position) : float.PositiveInfinity;

        void EvaluateThreat(float elapsed)
        {
            if (!threat) FindThreat();
            float d = ThreatDistance();

            if (IsEscaping)
            {
                escapeTime += elapsed;
                bool timedOut = t.maxFleeSeconds > 0f && escapeTime >= t.maxFleeSeconds;
                if (d >= t.releaseDistance || timedOut) EnterCalm();
                return;
            }
            if (d < t.triggerDistance)
            {
                EnterEscape();
                if (herd) herd.RaiseAlarm(this);
                return;
            }
            if (t.alertDistance > 0f && d < t.alertDistance)
            {
                if (Current != State.Alert) EnterAlert();
                return;
            }
            if (Current == State.Alert) EnterCalm();
        }

        internal void OnHerdAlarm()
        {
            if (started && enabled && !IsEscaping && agent.isOnNavMesh) EnterEscape();
        }

        internal void OnHerdMove()
        {
            // The group is setting off: cut this member's idle short so it keeps up.
            if (started && Current == State.Graze) stateTimer = Mathf.Min(stateTimer, Range(0.2f, 1.5f));
        }

        void EnterEscape()
        {
            Current = t.response == ThreatResponse.Flee ? State.Flee : State.KeepDistance;
            IsGrazing = false;
            escapeTime = 0f;
            Escape();
        }

        void Escape()
        {
            stateTimer = 1f; // re-aim once a second as the player moves
            if (!threat) { EnterCalm(); return; }

            Vector3 pos = agent.nextPosition;
            float d = Flat(pos, threat.position);
            Vector3 away = pos - threat.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

            // Wolves only back off to the release ring; prey runs a full leg.
            float leg = Current == State.Flee ? t.fleeLegDistance : Mathf.Max(4f, t.releaseDistance - d + 3f);
            agent.speed = t.runSpeed;

            float jitter = Range(-t.fleeJitterDegrees, t.fleeJitterDegrees);
            foreach (float a in EscapeAngles)
            {
                Vector3 dir = Quaternion.Euler(0f, a + jitter, 0f) * away;
                if (!NavMesh.SamplePosition(OnGround(pos + dir * leg), out var hit, 6f, NavMesh.AllAreas)) continue;
                if (Flat(hit.position, threat.position) <= d) continue;
                if (agent.SetDestination(hit.position)) return;
            }
        }

        void EnterAlert()
        {
            Current = State.Alert;
            IsGrazing = false;
            if (agent.isOnNavMesh) agent.ResetPath();
        }

        void EnterCalm()
        {
            Current = State.Calm;
            IsGrazing = false;
            if (agent.isOnNavMesh) agent.ResetPath();
            stateTimer = t.calmDownSeconds;
        }

        void StartGraze(float seconds)
        {
            Current = State.Graze;
            if (agent.isOnNavMesh) agent.ResetPath();
            stateTimer = seconds;
            IsGrazing = rng.NextDouble() < t.grazeChance;
        }

        void StartWander()
        {
            Vector3 target;
            bool ok;
            if (herd)
                ok = herd.NextWanderTarget(this, t, rng, out target);
            else
            {
                Vector3 p = agent.nextPosition + RandomFlat() * Range(t.wanderLeg.x, t.wanderLeg.y);
                ok = NavMesh.SamplePosition(OnGround(p), out var hit, 4f, NavMesh.AllAreas);
                target = hit.position;
            }
            if (!ok)
            {
                StartGraze(1f); // nowhere valid; try again shortly
                return;
            }
            agent.speed = t.walkSpeed;
            if (!agent.SetDestination(target)) { StartGraze(1f); return; }
            Current = State.Wander;
            IsGrazing = false;
        }

        bool Arrived() =>
            !agent.pathPending && (!agent.hasPath || agent.remainingDistance <= agent.stoppingDistance + 0.25f);

        void TrackStuck(float dt)
        {
            bool tryingToMove = agent.hasPath && !agent.pathPending && agent.desiredVelocity.sqrMagnitude > 0.09f;
            bool notMoving = agent.velocity.sqrMagnitude < 0.01f;
            bool invalid = agent.hasPath && agent.pathStatus == NavMeshPathStatus.PathInvalid;
            stuckTimer = (tryingToMove && notMoving) || invalid ? stuckTimer + dt : 0f;
            if (stuckTimer < tuning.repathAfterStuckSeconds) return;
            stuckTimer = 0f;
            if (IsEscaping) Escape();
            else StartGraze(Range(0.5f, 2f));
        }

        bool TryRecoverOntoNavMesh()
        {
            if (!NavMesh.SamplePosition(transform.position, out var hit, 10f, NavMesh.AllAreas)) return false;
            return agent.Warp(hit.position);
        }

        // ---- Presentation ----------------------------------------------------------------------

        void SyncTransform(float dt)
        {
            Vector3 p = agent.nextPosition;
            Vector3 normal = Vector3.up;
            if (snapToTerrain && terrain)
            {
                Vector3 tp = terrain.transform.position;
                p.y = terrain.SampleHeight(p) + tp.y;
                var data = terrain.terrainData;
                normal = data.GetInterpolatedNormal((p.x - tp.x) / data.size.x, (p.z - tp.z) / data.size.z);
            }

            Vector3 v = agent.velocity;
            v.y = 0f;
            float targetYaw = yaw;
            if (v.sqrMagnitude > 0.04f)
                targetYaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            else if (threat && (Current == State.Alert || (Current == State.Calm && ThreatDistance() < t.releaseDistance + 10f)))
            {
                Vector3 look = threat.position - p; // stop and watch the player
                targetYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            }
            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, t.angularSpeed * dt);

            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, normal, tuning.groundAlign));
            // yaw is the facing the agent wants. The models face +Z at import (ModelFacingPostprocessor, WL-BUG-7), so
            // the nose follows transform.forward; only the test hook ExtraModelYaw may turn the model away from it.
            transform.SetPositionAndRotation(p, tilt * Quaternion.Euler(0f, yaw + ExtraModelYaw, 0f));
        }

        void Animate(float dt)
        {
            if (!animator) return;
            Vector3 v = agent.velocity;
            v.y = 0f;
            float speed = v.magnitude;
            animator.SetFloat(SpeedId, speed, tuning.speedDampTime, dt);
            if (hasGrazingParam) animator.SetBool(GrazingId, IsGrazing && speed < 0.1f);
            if (hasAnimSpeedParam)
                animator.SetFloat(AnimSpeedId, speed > tuning.gallopClipSpeed ? speed / tuning.gallopClipSpeed : 1f);
        }

        // ---- Helpers ---------------------------------------------------------------------------

        float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        /// <summary>Puts a candidate point at terrain height before a NavMesh sample. A point 25 m away on a
        /// hill can be many metres above or below the animal, and SamplePosition only searches a few metres.</summary>
        internal static Vector3 OnGround(Vector3 p)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain) p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
            return p;
        }

        Vector3 RandomFlat()
        {
            float a = Range(0f, Mathf.PI * 2f);
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
