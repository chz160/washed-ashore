using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Authoritative north-boundary clamp (spec B4). Every LateUpdate it snaps any player or
    /// Rigidbody that is north of the line back south of it and logs the event. Rigidbodies are found
    /// by a per-frame scan of the scene's Rigidbodies (cost grows with the body count, 0 today), so a
    /// body spawned or teleported north is caught the same frame. A physics OverlapBox north of the
    /// line was tried and dropped: in Play every terrain-tree collider shape comes back as a
    /// TerrainCollider hit, and the ~37k vista trees made the query stall the Editor (2026-10-06).
    /// The decision lives in <see cref="WorldBoundsRule"/>; this component only finds bodies and
    /// moves them.
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
        [Tooltip("Seconds between rescans for Player-tagged CharacterControllers.")]
        [SerializeField] float playerRescanInterval = 1f;

        /// <summary>Raised after every snap: body, position before, position after.</summary>
        public static event Action<GameObject, Vector3, Vector3> Snapped;

        readonly List<CharacterController> players = new List<CharacterController>();
        WorldBoundsRule rule;
        float nextScan;

        public WorldBoundsRule Rule => rule ??= new WorldBoundsRule(northLine, snapInset);
        public int SnapCount { get; private set; }

        public void Configure(Vector2[] lineXZ, float inset)
        {
            northLine = (Vector2[])lineXZ.Clone();
            snapInset = inset;
            rule = null;
        }

        void OnEnable()
        {
            rule = null;
            nextScan = 0f;
        }

        void OnValidate() => rule = null;

        /// <summary>Refreshes the Player-tagged CharacterControllers; call after spawning a player.</summary>
        public void Rescan()
        {
            players.Clear();
            foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
                if (go.TryGetComponent(out CharacterController cc)) players.Add(cc);
            nextScan = Time.unscaledTime + playerRescanInterval;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= nextScan) Rescan();
            var r = Rule;
            foreach (var cc in players)
                if (cc != null && r.TryConstrain(cc.transform.position, out var p)) SnapController(cc, p);
            ClampBodies(r);
        }

        void ClampBodies(WorldBoundsRule r)
        {
            foreach (var rb in FindObjectsByType<Rigidbody>())
                if (r.TryConstrain(rb.position, out var p)) SnapBody(rb, p);
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
