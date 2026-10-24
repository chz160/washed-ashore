using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Authoritative north-boundary clamp (spec B4). Every LateUpdate it snaps any player or
    /// Rigidbody that is north of the line back south of it and logs the event. Players and Rigidbodies
    /// are not searched for: a PlayerController registers its CharacterController, and a Rigidbody that
    /// can leave the map carries a <see cref="WorldBoundsBody"/>, which registers it in OnEnable and drops
    /// it in OnDisable (#44: no scene scan and no allocation per frame). A body that spawns or is moved
    /// north is caught the same frame. A physics OverlapBox north of the
    /// line was tried and dropped: in Play every terrain-tree collider shape comes back as a
    /// TerrainCollider hit, and the ~37k vista trees made the query stall the Editor (2026-10-06).
    /// The decision lives in <see cref="WorldBoundsRule"/>; this component only walks the registered
    /// bodies and moves them.
    /// The line is written by WorldBoundsBuilder from MapConfig, so rebuilding moves the clamp.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class WorldBoundsClamp : MonoBehaviour
    {
        [Tooltip("North line as Unity XZ points, sorted by X. Written by WorldBoundsBuilder.")]
        [SerializeField] Vector2[] northLine = { new Vector2(0f, 0f) };
        [Tooltip("How far south of the line a snapped body is put, in metres. Must clear the backstop.")]
        [SerializeField] float snapInset = 3f;
        [Tooltip("Height above the ground a snapped player lands at, in metres.")]
        [SerializeField] float groundOffset = 0.5f;

        /// <summary>Raised after every snap: body, position before, position after.</summary>
        public static event Action<GameObject, Vector3, Vector3> Snapped;

        // Registered by PlayerController and WorldBoundsBody (OnEnable / OnDisable). Static, so a body that enables
        // before the clamp does is still caught, and the lists never allocate after warm-up.
        static readonly List<CharacterController> players = new List<CharacterController>();
        static readonly List<Rigidbody> bodies = new List<Rigidbody>();
        WorldBoundsRule rule;

        public static int RegisteredPlayers => players.Count;
        public static int RegisteredBodies => bodies.Count;

        internal static void Register(CharacterController cc)
        {
            if (cc != null && !players.Contains(cc)) players.Add(cc);
        }

        internal static void Unregister(CharacterController cc) => players.Remove(cc);

        internal static void Register(Rigidbody rb)
        {
            if (rb != null && !bodies.Contains(rb)) bodies.Add(rb);
        }

        internal static void Unregister(Rigidbody rb) => bodies.Remove(rb);

        public WorldBoundsRule Rule => rule ??= new WorldBoundsRule(northLine, snapInset);
        public int SnapCount { get; private set; }

        public void Configure(Vector2[] lineXZ, float inset)
        {
            northLine = (Vector2[])lineXZ.Clone();
            snapInset = inset;
            rule = null;
        }

        void OnEnable() => rule = null;

        void OnValidate() => rule = null;

        /// <summary>
        /// Once, at Start: names any loaded Rigidbody that has no <see cref="WorldBoundsBody"/>, because the clamp would never move it
        /// (#44). Not a per-frame scan.
        /// </summary>
        void Start()
        {
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsInactive.Include))
                if (!rb.TryGetComponent<WorldBoundsBody>(out _))
                    Debug.LogError($"WorldBoundsClamp: Rigidbody '{rb.name}' has no WorldBoundsBody, so the north boundary will not clamp it.", rb);
        }

        void LateUpdate() => Tick();

        /// <summary>One clamp pass over every registered player and body (what LateUpdate runs; public so a test can time it).</summary>
        public void Tick()
        {
            var r = Rule;
            for (int i = 0; i < players.Count; i++)
            {
                var cc = players[i];
                if (cc == null) { players.RemoveAt(i--); continue; }
                if (r.TryConstrain(cc.transform.position, out var p)) SnapController(cc, p);
            }
            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                if (rb == null) { bodies.RemoveAt(i--); continue; }
                if (r.TryConstrain(rb.position, out var p)) SnapBody(rb, p);
            }
        }

        void SnapController(CharacterController cc, Vector3 target)
        {
            var from = cc.transform.position;
            if (TerrainQuery.TryGroundHeight(target, out float ground))
                target.y = Mathf.Max(target.y, ground + groundOffset);
            // CharacterController overrides transform writes while enabled.
            cc.enabled = false;
            cc.transform.position = target;
            cc.enabled = true;
            Report(cc.gameObject, from, target);
        }

        void SnapBody(Rigidbody rb, Vector3 target)
        {
            var from = rb.position;
            if (TerrainQuery.TryGroundHeight(target, out float ground))
                target.y = Mathf.Max(target.y, ground + groundOffset);
            rb.position = target;
            rb.transform.position = target;
            if (!rb.isKinematic)
            {
                var v = rb.linearVelocity;
                if (v.z > 0f) rb.linearVelocity = new Vector3(v.x, v.y, 0f);
            }
            Report(rb.gameObject, from, target);
        }

        void Report(GameObject go, Vector3 from, Vector3 to)
        {
            SnapCount++;
            Debug.LogWarning($"WorldBoundsClamp: {go.name} was {Rule.NorthOfLine(from):F2} m north of the line at {from:F2}; snapped to {to:F2}.");
            Snapped?.Invoke(go, from, to);
        }
    }
}
