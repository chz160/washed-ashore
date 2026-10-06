using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Birds
{
    /// <summary>
    /// One ground robin (spec B3, B4; bird density brief 3.3). On the ground it plays weighted bouts (PeckGround, Hop,
    /// Idle, Preen, ScratchGround), each a random clip variant at its own playback speed and start time, never the
    /// same clip twice in a row and never the clip a neighbour is playing. Within 14 m of the player it stops and
    /// watches (an Idle variant, facing the player); within the FID (8 m) it flushes: Flutter, then Fly along a path
    /// from <see cref="RobinFlightPlanner"/>, and its patch-mates within 8 m follow after 0.2-0.8 s. It lands 25-40 m
    /// away and calms for 20 s (Hop and Peck only, no alert or flush), or, with no clear heading, despawns out of view
    /// and later reappears at its start point out of view. At most 2 flushes per run; hops stay within 4 m of the
    /// start point, on grass, and 3 m or more from the route (A4). The robin moves its own transform, has no collider.
    /// </summary>
    public class RobinAgent : MonoBehaviour
    {
        public enum State { Ground, Alert, Flutter, Fly, Land, Away }
        const int PeckBout = 0, HopBout = 1, IdleBout = 2;
        const float GuardInterval = 0.25f, GuardPhase = 0.1f;

        static readonly List<RobinAgent> all = new List<RobinAgent>();

        BirdTuning tuning;
        RobinTuning t;
        BirdPlacementRules.Site site;
        BirdTerrain ground;
        System.Random rng;
        Transform threat;
        Camera view;
        Animator animator;
        Renderer[] renderers;
        int mask;
        readonly List<Vector3> path = new List<Vector3>();
        int pathIndex, bout, hopsLeft;
        bool landing;
        Vector3 awayDir, start, hopFrom, hopTo;
        float yaw, targetYaw, boutTimer, hopTimer, socialTimer = -1f, guardTimer, awayTimer, calmTimer, flutterTimer;

        public static IReadOnlyList<RobinAgent> All => all;
        /// <summary>Flushes since the domain loaded; the sighting test counts the difference over a walk.</summary>
        public static int FlushCount { get; private set; }
        public static event System.Action<RobinAgent> Flushed;

        public BirdTuning Tuning => tuning;
        public Animator Animator => animator;
        public PatchPlan Patch { get; private set; }
        public Vector3 StartPoint => start;
        public State Current { get; private set; }
        public string CurrentClip { get; private set; }
        public bool IsFlying => Current == State.Flutter || Current == State.Fly || Current == State.Land;
        public bool Despawned => Current == State.Away;
        public bool Calm => calmTimer > 0f;
        public Vector3 Velocity { get; private set; }
        public Vector3 FlushOrigin { get; private set; }
        /// <summary>Horizontal player distance when the last flush fired (player-triggered or social).</summary>
        public float FlushDistance { get; private set; }
        /// <summary>The last flush came from a patch-mate (social), not the player's distance.</summary>
        public bool FlushedSocially { get; private set; }
        public int Flushes { get; private set; }
        public int Landings { get; private set; }
        public int Despawns { get; private set; }
        public Vector3 BodyCenter => transform.position + Vector3.up * t.bodyCenterHeight;
        public IReadOnlyList<Vector3> Path => path;

        // ---- Test hooks (positive controls); all off by default ----
        public float ExtraModelYaw { get; set; }
        /// <summary>Copy this robin's clip and normalised time every frame (lockstep control).</summary>
        public RobinAgent MirrorOf { get; set; }
        /// <summary>Play only this state (variety control).</summary>
        public string LockedClip { get; set; }
        /// <summary>Ignore the player (flush control).</summary>
        public bool FlushDisabled { get; set; }

        public Bounds VisualBounds
        {
            get
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                return b;
            }
        }

        /// <summary>Called by <see cref="BirdPopulation"/> right after Instantiate.</summary>
        public void Init(BirdTuning birdTuning, BirdPlacementRules.Site habitat, PatchPlan patch, int runSeed, int index)
        {
            tuning = birdTuning;
            t = tuning.robin;
            site = habitat;
            Patch = patch;
            ground = BirdTerrain.Active;
            mask = RobinFlightPlanner.ObstacleMask(ground.Terrain);
            rng = BirdRandom.For(runSeed, index);
            animator = GetComponentInChildren<Animator>();
            renderers = GetComponentsInChildren<Renderer>();
            animator.applyRootMotion = false;
            start = ground.OnGround(transform.position);
            yaw = targetYaw = transform.eulerAngles.y;
            Settle(start, false);
            // Random start clip and normalised time per robin (brief 3.3 desync).
            boutTimer = rng.Range(0f, 2f);
        }

        void OnEnable() => all.Add(this);
        void OnDisable() => all.Remove(this);

        void Update()
        {
            if (rng == null) return;
            float dt = Time.deltaTime;
            if (!threat) FindThreat();
            switch (Current)
            {
                case State.Ground:
                case State.Alert: UpdateGround(dt); break;
                case State.Away: UpdateAway(dt); break;
                default: UpdateFlight(dt); break;
            }
        }

        void FindThreat()
        {
            var player = GameObject.FindWithTag(tuning.playerTag);
            threat = player ? player.transform : null;
            view = player ? player.GetComponentInChildren<Camera>() : null;
            if (!view) view = Camera.main;
        }

        float ThreatDistance() => threat ? RobinFlightPlanner.Flat(transform.position, threat.position) : float.PositiveInfinity;

        // ---- Ground (B3) and alert -------------------------------------------------------------

        void UpdateGround(float dt)
        {
            if (MirrorOf) { Mirror(); return; }
            calmTimer -= dt;
            bool canFlush = !FlushDisabled && !Calm && threat && Flushes < t.maxFlushes;
            float d = ThreatDistance();
            if (canFlush && d < t.flightInitiationDistance) { Flush(false, d); return; }
            if (canFlush && socialTimer >= 0f && (socialTimer -= dt) < 0f) { Flush(true, d); return; }

            bool alert = !FlushDisabled && !Calm && LockedClip == null && d < t.alertDistance;
            if (alert && Current != State.Alert) { Current = State.Alert; hopsLeft = 0; PlayVariant(IdleBout); }
            else if (!alert && Current == State.Alert) { Current = State.Ground; NextBout(); }

            Vector3 p = transform.position;
            if (Current == State.Alert)
            {
                Vector3 look = threat.position - p;
                targetYaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
            }
            else
            {
                boutTimer -= dt;
                guardTimer -= dt;
                if (bout == HopBout && LockedClip == null) p = StepHop(dt, p);
                else if (boutTimer <= 0f) NextBout();
                if (guardTimer <= 0f)
                {
                    guardTimer = GuardInterval;
                    if (LockedClip == null && InStepWithNeighbour()) NextBout();
                }
            }
            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, t.turnSpeed * dt);
            p.y = ground.Height(p);
            Velocity = (p - transform.position) / Mathf.Max(dt, 1e-4f);
            transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw + ExtraModelYaw, 0f));
        }

        /// <summary>A hop bout is 1-3 hops of about 0.3 m, one per Hop clip cycle.</summary>
        Vector3 StepHop(float dt, Vector3 p)
        {
            if (hopTimer > 0f)
            {
                hopTimer -= dt;
                float u = 1f - Mathf.Clamp01(hopTimer / (t.hopSeconds / animator.speed));
                return Vector3.Lerp(hopFrom, hopTo, u);
            }
            if (hopsLeft <= 0 || !PickHop(p)) { NextBout(); return p; }
            hopsLeft--;
            hopTimer = t.hopSeconds / animator.speed;
            return p;
        }

        bool PickHop(Vector3 p)
        {
            for (int i = 0; i < 6; i++)
            {
                float a = (yaw + rng.Range(-60f, 60f) + (i > 2 ? 180f : 0f)) * Mathf.Deg2Rad;
                Vector3 to = ground.OnGround(p + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * t.hopLength);
                if (RobinFlightPlanner.Flat(to, start) > t.hopLeash) continue;
                // A4: the hop may not end inside, or cross, the 3 m keep-out round the route.
                if (site.RouteDistance(Vector3.Lerp(p, to, 0.5f)) < t.offTrail) continue;
                if (!RobinFlightPlanner.GroundOk(site, ground, t, to, mask, t.offTrail)) continue;
                hopFrom = p;
                hopTo = to;
                targetYaw = a * Mathf.Rad2Deg;
                return true;
            }
            return false;
        }

        void NextBout()
        {
            if (LockedClip != null)
            {
                if (CurrentClip != LockedClip) Play(LockedClip, true);
                boutTimer = 2f;
                return;
            }
            var taken = NeighbourClips();
            int first = PickWeighted();
            for (int k = 0; k < t.bouts.Length; k++)
            {
                int b = (first + k) % t.bouts.Length;
                if (Calm && b != PeckBout && b != HopBout) continue; // brief 3.3: Hop and Peck only while calm
                if (!PlayVariant(b, taken)) continue;
                return;
            }
            boutTimer = 0.5f; // every variant is taken nearby: keep the current clip a little longer
        }

        bool PlayVariant(int b, HashSet<string> taken = null)
        {
            var free = new List<string>();
            foreach (var s in t.bouts[b].states)
                if (s != CurrentClip && (taken == null || !taken.Contains(s))) free.Add(s);
            if (free.Count == 0) return false;
            bout = b;
            var dur = t.bouts[b].duration;
            if (b == HopBout) { hopsLeft = Mathf.RoundToInt(rng.Range(dur)); hopTimer = 0f; }
            else
            {
                boutTimer = rng.Range(dur);
                if (Current != State.Alert) targetYaw = yaw + rng.Range(-t.idleTurnDegrees, t.idleTurnDegrees);
            }
            Play(free[rng.Next(free.Count)], true);
            return true;
        }

        int PickWeighted()
        {
            float sum = 0f;
            foreach (var b in t.bouts) sum += b.weight;
            float r = rng.Range(0f, sum);
            for (int i = 0; i < t.bouts.Length; i++)
                if ((r -= t.bouts[i].weight) <= 0f) return i;
            return 0;
        }

        HashSet<string> NeighbourClips()
        {
            var set = new HashSet<string>();
            foreach (var r in all)
                if (r != this && r.OnGround && r.CurrentClip != null && IsNeighbour(r)) set.Add(r.CurrentClip);
            return set;
        }

        bool OnGround => Current == State.Ground || Current == State.Alert;

        bool IsNeighbour(RobinAgent r) => (r.transform.position - transform.position).sqrMagnitude <= t.neighbourRadius * t.neighbourRadius;

        /// <summary>A neighbour that hopped into range may be playing this clip in phase: change bout.</summary>
        bool InStepWithNeighbour()
        {
            var mine = animator.GetCurrentAnimatorStateInfo(0);
            foreach (var r in all)
            {
                if (r == this || !r.OnGround || r.CurrentClip != CurrentClip || !IsNeighbour(r)) continue;
                var theirs = r.animator.GetCurrentAnimatorStateInfo(0);
                float d = Mathf.Abs(Mathf.Repeat(mine.normalizedTime, 1f) - Mathf.Repeat(theirs.normalizedTime, 1f));
                if (Mathf.Min(d, 1f - d) < GuardPhase) return true;
            }
            return false;
        }

        void Mirror()
        {
            var info = MirrorOf.animator.GetCurrentAnimatorStateInfo(0);
            animator.speed = MirrorOf.animator.speed;
            animator.Play(info.fullPathHash, 0, info.normalizedTime);
            CurrentClip = MirrorOf.CurrentClip;
        }

        void Play(string state, bool randomTime)
        {
            CurrentClip = state;
            if (randomTime) animator.CrossFade(state, t.crossFadeSeconds, 0, (float)rng.NextDouble());
            else animator.CrossFadeInFixedTime(state, t.crossFadeSeconds, 0);
        }

        void Settle(Vector3 p, bool afterFlight)
        {
            transform.position = ground.OnGround(p);
            Current = State.Ground;
            calmTimer = afterFlight ? t.calmSeconds : 0f;
            animator.speed = rng.Range(t.playbackSpeed);
            NextBout();
        }

        // ---- Flush (B4) --------------------------------------------------------------------------

        void Flush(bool social, float playerDistance)
        {
            Current = State.Flutter;
            socialTimer = -1f;
            FlushedSocially = social;
            FlushDistance = playerDistance;
            FlushOrigin = transform.position;
            Flushes++;
            FlushCount++;
            animator.speed = 1f;
            Play(t.flutterState, false);
            flutterTimer = rng.Range(t.flutterSeconds);
            landing = RobinFlightPlanner.Plan(site, ground, t, rng, transform.position, threat.position, threat, mask, path);
            pathIndex = 1;
            awayDir = path[path.Count - 1] - path[0];
            awayDir.y = 0f;
            awayDir = awayDir.sqrMagnitude > 0.01f ? awayDir.normalized : Vector3.forward;
            Flushed?.Invoke(this);
            foreach (var r in all)
                if (r != this && r.Patch == Patch && r.OnGround && r.socialTimer < 0f && !r.MirrorOf
                    && (r.transform.position - transform.position).sqrMagnitude <= t.socialRadius * t.socialRadius)
                    r.socialTimer = r.rng.Range(t.socialDelay);
        }

        void UpdateFlight(float dt)
        {
            Vector3 from = transform.position;
            Vector3 p = from;
            flutterTimer -= dt;
            // Flutter 0.3-0.6 s rising off the ground, then Fly; Flutter again for the last metre and a half.
            bool slow = Current == State.Flutter || Current == State.Land;
            float step = (slow ? t.landFlutterDistance / 0.6f : t.flySpeed) * dt;
            while (step > 0f && pathIndex < path.Count)
            {
                Vector3 to = path[pathIndex] - p;
                float d = to.magnitude;
                if (d <= step) { p = path[pathIndex++]; step -= d; }
                else { p += to / d * step; step = 0f; }
                if (Current == State.Flutter && pathIndex >= 2) break; // hold at the lift point until Flutter ends
            }
            Velocity = (p - from) / Mathf.Max(dt, 1e-4f);

            if (Current == State.Flutter && pathIndex >= 2 && flutterTimer <= 0f) { Current = State.Fly; Play(t.flyState, false); }
            if (Current == State.Fly && landing && pathIndex >= path.Count - 1) { Current = State.Land; Play(t.flutterState, false); }

            Vector3 v = Velocity;
            float horiz = new Vector2(v.x, v.z).magnitude;
            if (horiz > 0.05f) yaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            float pitch = horiz > 0.05f ? Mathf.Clamp(-Mathf.Atan2(v.y, horiz) * Mathf.Rad2Deg, -35f, 35f) : 0f;
            transform.SetPositionAndRotation(p, Quaternion.Euler(pitch, yaw + ExtraModelYaw, 0f));

            if (pathIndex < path.Count) return;
            if (landing)
            {
                Landings++;
                targetYaw = yaw;
                Settle(p, true);
                return;
            }
            if (ThreatDistance() >= t.despawnDistance && !InView(VisualBounds)) Despawn();
            else path.Add(RobinFlightPlanner.Extend(ground, t, p, awayDir, 30f));
        }

        // ---- Despawn and return ------------------------------------------------------------------

        bool InView(Bounds b) => view && GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(view), b);

        void Despawn()
        {
            Current = State.Away;
            Despawns++;
            awayTimer = t.respawnSeconds;
            Velocity = Vector3.zero;
            foreach (var r in renderers) r.enabled = false;
        }

        void UpdateAway(float dt)
        {
            if ((awayTimer -= dt) > 0f) return;
            if (threat && RobinFlightPlanner.Flat(start, threat.position) < t.minRespawnDistance) return;
            if (InView(new Bounds(start + Vector3.up * 0.15f, Vector3.one * 0.5f))) return;
            foreach (var r in renderers) r.enabled = true;
            Settle(start, true);
        }
    }
}
