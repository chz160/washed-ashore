using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using WashedAshore.Wildlife;

namespace WashedAshore.Tests.Wildlife
{
    /// <summary>
    /// A4 measurements for one animal (spec A4 plus wl-qa's 2026-10-05 ruling).
    /// Every 0.2 s: speed against the dominant animation clip (sliding).
    /// Every 1 s: footprint clip/float, stuck over a 5 s window, terrain/NavMesh height agreement.
    /// Footprint method: the SkinnedMeshRenderer's localBounds (rootBone space) give an oriented box
    /// that tilts with the model; its 4 lowest world corners are the footprint. clipDepth is how far the
    /// deepest corner sits below the terrain, floatGap how far the lowest corner sits above it.
    /// </summary>
    class WildlifeA4Probe
    {
        public const float MovingSpeed = 0.8f;   // above this the clip must be Walk/Gallop
        public const float StillSpeed = 0.1f;    // below this the clip must be Idle/Eating
        public const float MaxClip = 0.35f, MaxFloat = 0.15f, StuckMove = 0.25f, StuckWindow = 5f;

        readonly WildlifeAgent a;
        readonly SkinnedMeshRenderer[] skins;
        readonly Queue<(Vector3 pos, bool wants)> lastSeconds = new Queue<(Vector3, bool)>();
        Vector3 last;
        float mismatch;
        int clipRun, floatRun;

        public string Name => a.name;
        public WildlifeSpecies Species => a.Species;
        public int Samples, MovingSamples, StillSamples, OffNavMeshSamples, StuckEvents;
        public float MaxMismatch, MaxClipDepth = float.NegativeInfinity, MaxFloatGap = float.NegativeInfinity;
        public int MaxClipRunSeconds, MaxFloatRunSeconds;
        public float MaxNavMeshDelta, MaxRootGroundError;

        public WildlifeA4Probe(WildlifeAgent agent)
        {
            a = agent;
            last = agent.transform.position;
            skins = agent.GetComponentsInChildren<SkinnedMeshRenderer>();
            head = agent.GetComponentsInChildren<Transform>().FirstOrDefault(x => x.name == "Head");
            state = agent.Current;
        }

        // ---- Facing (WL-BUG-7) ------------------------------------------------------------------
        // The model's forward is measured from the root to the "Head" bone, not transform.forward (which the
        // agent sets along the motion by construction, so it passes even when the mesh runs tail-first).
        public const float MinFacingDot = 0.7f, MinFacingShare = 0.95f, FacingSpeed = 0.5f;
        public const int MinFacingSamples = 5;
        readonly Transform head;
        WildlifeAgent.State state;
        float inState;
        public int FacingSamples, FacingOk, WatchSamples, WatchOk;
        public float MinMovingDot = 1f, MinWatchDot = 1f;
        /// <summary>designer-2: every moving sample under MinFacingDot, with what can tell a slow turn from an avoidance push.</summary>
        public readonly List<string> BadFacing = new List<string>();
        public bool HasHead => head;
        public float FacingShare => FacingSamples == 0 ? 1f : (float)FacingOk / FacingSamples;
        public float WatchShare => WatchSamples == 0 ? 1f : (float)WatchOk / WatchSamples;

        /// <summary>The head direction in the model's own frame (the agent root carries the mesh). WL-BUG-7: the
        /// meshes are turned to +Z at import (ModelFacingPostprocessor), so this should read z = +1; z = -1 means
        /// the mesh faces -Z and runs tail-first.</summary>
        public Vector3 HeadDirInModelFrame() => Quaternion.Inverse(Quaternion.Euler(0f, a.transform.eulerAngles.y, 0f)) * ModelForward();

        /// <summary>Flat direction from the agent root to its head bone (the model's visual forward).</summary>
        public Vector3 ModelForward()
        {
            Vector3 f = head.position - a.transform.position;
            f.y = 0f;
            return f.normalized;
        }

        string DescribeBadFacing(float dot, float v, Vector3 displacement)
        {
            Vector3 p = a.transform.position;
            var ag = a.Agent;
            Vector3 desired = ag.desiredVelocity; desired.y = 0f;
            // Turn lag: the model trails a desired direction that the motion already follows. Push: the motion itself departs
            // from the desired direction (avoidance or a NavMesh corner).
            float motionVsDesired = desired.sqrMagnitude > 0.01f ? Vector3.Angle(displacement, desired) : -1f;
            float modelVsDesired = desired.sqrMagnitude > 0.01f ? Vector3.Angle(ModelForward(), desired) : -1f;
            float mate = float.MaxValue;
            foreach (var o in WildlifeAgent.All)
                if (o != a && o.Species == a.Species) mate = Mathf.Min(mate, WildlifeTestKit.Flat(o.transform.position, p));
            float obstacle = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(p, 5f, ~0, QueryTriggerInteraction.Ignore))
                if (!(c is TerrainCollider) && !c.transform.IsChildOf(a.transform)) obstacle = Mathf.Min(obstacle, Vector3.Distance(c.ClosestPoint(p), p));
            return $"t+{Samples * 0.2f:F1}s pos=({p.x:F1},{p.z:F1}) v={v:F2} dot={dot:F2} slope={WashedAshore.Gameplay.TerrainQuery.Steepness(p):F0}deg " +
                   $"motionVsDesired={motionVsDesired:F0}deg modelVsDesired={modelVsDesired:F0}deg angularSpeed={ag.angularSpeed:F0} " +
                   $"state={a.Current} nearestMate={(mate < float.MaxValue ? mate.ToString("F1") : "-")}m nearestObstacle={(obstacle < float.MaxValue ? obstacle.ToString("F1") : ">5")}m";
        }

        void SampleFacing(Vector3 displacement, float v, float window, Vector3? player)
        {
            if (a.Current != state) { state = a.Current; inState = 0f; }
            else inState += window;
            if (!head) return;
            if (v > FacingSpeed)
            {
                displacement.y = 0f;
                float dot = Vector3.Dot(ModelForward(), displacement.normalized);
                FacingSamples++;
                if (dot >= MinFacingDot) FacingOk++;
                else BadFacing.Add(DescribeBadFacing(dot, v, displacement));
                MinMovingDot = Mathf.Min(MinMovingDot, dot);
            }
            // Alert (and the wolves' post-retreat watch) must face the player once they've had 1 s to turn.
            bool watching = a.Current == WildlifeAgent.State.Alert
                            || (a.Current == WildlifeAgent.State.Calm && a.SpeciesTuning.response == ThreatResponse.KeepDistance);
            if (player.HasValue && watching && v < StillSpeed && inState >= 1f
                && WildlifeTestKit.Flat(a.transform.position, player.Value) < a.SpeciesTuning.releaseDistance)
            {
                Vector3 to = player.Value - a.transform.position;
                to.y = 0f;
                float dot = Vector3.Dot(ModelForward(), to.normalized);
                WatchSamples++;
                if (dot >= MinFacingDot) WatchOk++;
                MinWatchDot = Mathf.Min(MinWatchDot, dot);
            }
        }

        /// <summary>Sliding and facing check: called every 0.2 s.</summary>
        public void SampleMotion(float window, Vector3? player = null)
        {
            Vector3 p = a.transform.position;
            float v = WildlifeTestKit.Flat(p, last) / window;
            SampleFacing(p - last, v, window, player);
            last = p;
            Samples++;
            if (!a.Agent.isOnNavMesh) OffNavMeshSamples++;

            bool animMoving = DominantClipMoves(a.Animator);
            bool moving = v > MovingSpeed, still = v < StillSpeed;
            if (moving) MovingSamples++;
            if (still) StillSamples++;
            mismatch = (moving && !animMoving) || (still && animMoving) ? mismatch + window : 0f;
            MaxMismatch = Mathf.Max(MaxMismatch, mismatch);
        }

        /// <summary>Clip/float, stuck and height agreement: called every 1 s.</summary>
        public void SampleSecond(Terrain terrain)
        {
            var (clip, gap) = Footprint(terrain);
            MaxClipDepth = Mathf.Max(MaxClipDepth, clip);
            MaxFloatGap = Mathf.Max(MaxFloatGap, gap);
            clipRun = clip > MaxClip ? clipRun + 1 : 0;
            floatRun = gap > MaxFloat ? floatRun + 1 : 0;
            MaxClipRunSeconds = Math.Max(MaxClipRunSeconds, clipRun);
            MaxFloatRunSeconds = Math.Max(MaxFloatRunSeconds, floatRun);

            Vector3 p = a.transform.position;
            MaxRootGroundError = Mathf.Max(MaxRootGroundError, Mathf.Abs(p.y - TerrainHeight(terrain, p)));
            // agent.nextPosition reports the transform height once the transform is written (run 1 read
            // 0.000 everywhere), so measure the NavMesh surface directly under the animal instead.
            float navDelta = NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)
                ? Mathf.Abs(p.y - hit.position.y) : float.PositiveInfinity;
            MaxNavMeshDelta = Mathf.Max(MaxNavMeshDelta, navDelta);

            var s = a.Current;
            bool wants = s == WildlifeAgent.State.Wander || s == WildlifeAgent.State.Flee || s == WildlifeAgent.State.KeepDistance
                         || (a.Agent.hasPath && a.Agent.remainingDistance > 0.5f);
            lastSeconds.Enqueue((p, wants));
            if (lastSeconds.Count > StuckWindow + 1) lastSeconds.Dequeue();
            if (lastSeconds.Count == StuckWindow + 1 && lastSeconds.All(x => x.wants)
                && WildlifeTestKit.Flat(lastSeconds.Peek().pos, p) < StuckMove)
            {
                StuckEvents++;
                lastSeconds.Clear(); // count each stuck stretch once per window
            }
        }

        (float clip, float gap) Footprint(Terrain terrain)
        {
            var corners = new List<Vector3>(8 * skins.Length);
            foreach (var smr in skins)
            {
                Transform space = smr.rootBone ? smr.rootBone : smr.transform;
                Bounds lb = smr.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1);
                    corners.Add(space.TransformPoint(lb.min + Vector3.Scale(lb.size, c)));
                }
            }
            if (corners.Count == 0) return (0f, 0f);
            var foot = corners.OrderBy(c => c.y).Take(4).ToList();
            foot.Add(foot.Aggregate(Vector3.zero, (s, c) => s + c) / 4f); // footprint centre
            float clip = foot.Max(c => TerrainHeight(terrain, c) - c.y);
            float gap = foot.Min(c => c.y - TerrainHeight(terrain, c));
            return (clip, gap);
        }

        // Tiled terrain: the tile under the point, not the one passed in.
        static float TerrainHeight(Terrain t, Vector3 p) => WashedAshore.Gameplay.TerrainQuery.Height(p);

        public const string NavMeshMethod =
            "|transform.y - NavMesh.SamplePosition(transform.position, r=2 m).position.y|; transform.y is set to terrain height each frame";

        /// <summary>wl-qa positive control for (c): the same query must report ~1.0 m for a point lifted 1 m,
        /// and a non-zero horizontal offset for a point 0.5 m outside the nearest NavMesh edge. Also returns the
        /// raw |agent.nextPosition.y - terrain| for comparison.</summary>
        public (float lifted, float offEdge, float rawNextPosDelta) NavMeshControl(Terrain terrain)
        {
            Vector3 p = a.transform.position;
            float lifted = NavMesh.SamplePosition(p + Vector3.up, out var h1, 2f, NavMesh.AllAreas)
                ? Mathf.Abs(p.y + 1f - h1.position.y) : float.NaN;
            float offEdge = float.NaN;
            if (NavMesh.FindClosestEdge(p, out var edge, NavMesh.AllAreas))
            {
                Vector3 outside = edge.position - edge.normal * 0.5f; // edge normals point into the mesh
                if (NavMesh.SamplePosition(outside, out var h2, 2f, NavMesh.AllAreas))
                    offEdge = WildlifeTestKit.Flat(outside, h2.position);
            }
            float raw = Mathf.Abs(a.Agent.nextPosition.y - TerrainHeight(terrain, p));
            return (lifted, offEdge, raw);
        }

        static bool DominantClipMoves(Animator animator)
        {
            var clips = animator.IsInTransition(0) && animator.GetAnimatorTransitionInfo(0).normalizedTime > 0.5f
                ? animator.GetNextAnimatorClipInfo(0)
                : animator.GetCurrentAnimatorClipInfo(0);
            if (clips.Length == 0) return false;
            string name = clips.OrderByDescending(c => c.weight).First().clip.name;
            return name.StartsWith("Walk") || name.StartsWith("Gallop");
        }

        public override string ToString() =>
            $"{Name}: samples={Samples} moving={MovingSamples} still={StillSamples} maxMismatch={MaxMismatch:F2}s " +
            $"maxClip={MaxClipDepth:F3}m (run {MaxClipRunSeconds}s) maxFloat={MaxFloatGap:F3}m (run {MaxFloatRunSeconds}s) " +
            $"stuck={StuckEvents} navMeshDeltaY={MaxNavMeshDelta:F3}m rootGroundErr={MaxRootGroundError:F3}m offNavMesh={OffNavMeshSamples} " +
            $"facing={FacingOk}/{FacingSamples} ({FacingShare:P0}, minDot {MinMovingDot:F2}) watch={WatchOk}/{WatchSamples} (minDot {MinWatchDot:F2})" +
            (BadFacing.Count > 0 ? $" badFacing=[{string.Join(" ; ", BadFacing)}]" : "");

        /// <summary>Fails the agent if its model doesn't face its motion (or the player while watching) often enough.</summary>
        public void AssertFacing(string context)
        {
            NUnit.Framework.Assert.IsTrue(HasHead, $"{Name}: no Head bone to measure the model's facing");
            if (FacingSamples >= MinFacingSamples)
                NUnit.Framework.Assert.GreaterOrEqual(FacingShare, MinFacingShare,
                    $"{context} {Name} ({Species}) runs facing away from its motion: {this} | bad samples: {string.Join(" ; ", BadFacing)}");
            if (WatchSamples >= MinFacingSamples)
                NUnit.Framework.Assert.GreaterOrEqual(WatchShare, MinFacingShare,
                    $"{context} {Name} ({Species}) turns its back while watching the player: {this}");
        }
    }
}
