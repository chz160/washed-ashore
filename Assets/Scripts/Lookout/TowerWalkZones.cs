using System;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Lookout
{
    /// <summary>
    /// Lets the ordinary PlayerController walk the lookout tower (no guided lane). While the player is inside the
    /// tower footprint the capsule is slimmed to <see cref="towerRadius"/> (the stairs are 0.61 m wide between rails) and shortened to <see cref="towerHeight"/> (the top flight rises under the cab floor);
    /// while on or near a flight the controller's slope limit is raised to <see cref="flightSlopeLimit"/> (the flights
    /// are 52 to 56 degrees, the controller's limit is 45). Both are restored on leaving. Every number is a field,
    /// written by the WalkableTowerBuilder from the manifest; nothing else about the controller is touched.
    /// </summary>
    public class TowerWalkZones : MonoBehaviour, IPlayerTickHook
    {
        [Serializable]
        public struct Flight { public Vector3 foot, head; }   // tower-local nosing line ends

        public Flight[] flights = new Flight[0];
        public Vector3 footprintMin, footprintMax;            // tower-local box for the slim capsule
        public float cabStepHeight = 25.5f;   // tower-local y: from the top of flight 8 up, getting off the ramp onto the cab floor takes a 0.12 m step
        public float upperStepOffset = 0.25f;   // on ramps and upper decks (#41). 0.05 stalled the climb at the rail ends and the F04 and F07 heads at every fixed step from 1/60 to 1/240 (diagnostics, #46); the #64 TowerStep probe at 0.25 measured POP 0, CLIMB 0, OFF RAMP 0 (it was 0.05 against "a larger step lets the controller pop up when it rubs a rail end")
        public float towerStepOffset = 0.25f;   // the ramps are smooth; the pad edge is the biggest step (0.2). The default 0.4 popped the body up a step on landings
        public float towerRadius = 0.25f, towerHeight = 1.6f;   // height: the hatch at the top of flight 8 clears a 1.8 m capsule by -0.2 m
        public float flightSlopeLimit = 80f;
        [Tooltip("Walk speed cap, m/s, only while on a flight ramp (horizontal speed; the climb along the slope is about 0.6 of it).")]
        public float stairWalkSpeed = 1.15f;
        [Tooltip("Walk speed cap, m/s, only while on an upper landing deck or in the cab (feet above deckMinHeight). Ground level has no cap.")]
        public float landingWalkSpeed = 1.5f;
        [Tooltip("Metres the player is pulled back onto the ramp after a step that leaves it airborne, on flights.")]
        public float stairGroundSnap = 0.35f;
        public float deckMinHeight = 3.5f;   // tower-local y: landing 1 is at 4.17, the ground and pad are below this
        public float halfWidth = 0.9f, padFoot = 1.0f, padHead = 0.5f, below = 0.7f, above = 2.2f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Test-only mutation (#46 KF-T4a): the zone pass acts only on the first registered player, which is single-player behaviour. Reset in TearDown.</summary>
        public static bool TestFirstPlayerOnly;
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Probe/tool readout of the first registered player (td-46 adr/46-a-departures-1). Game code reads p.TowerZone; no runtime code
        // may call these (today the only callers are ControllerProbe64.cs:222,233).
        public bool InFlightZone => PlayerRegistry.Count > 0 && PlayerRegistry.All[0].TowerZone.InFlightZone;
        public bool OnRamp => PlayerRegistry.Count > 0 && PlayerRegistry.All[0].TowerZone.OnRamp;
        public bool InFootprint => PlayerRegistry.Count > 0 && PlayerRegistry.All[0].TowerZone.InFootprint;
#endif

        void OnEnable() => PlayerTickHooks.Register(this);

        /// <summary>Runs first in the player's tick (the old execution order -100).</summary>
        public int Order => -100;

        /// <summary>The zone logic for the player that is about to move, inside its tick.</summary>
        public void BeforeMove(PlayerController p)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TestFirstPlayerOnly && PlayerRegistry.Count > 1 && PlayerRegistry.All[0] != p) return;
#endif
            if (PlayerRegistry.IsLive(p)) Step(p);
        }

        public void AfterMove(PlayerController p) { }

        /// <summary>One zone pass over every live player (the tick runs it per player; public so a test can time it). No allocation.</summary>
        public void ZonePass()
        {
            var players = PlayerRegistry.All;
            int count = players.Count;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (TestFirstPlayerOnly && count > 1) count = 1;
#endif
            for (int i = 0; i < count; i++)
                if (PlayerRegistry.IsLive(players[i])) Step(players[i]);
        }

        /// <summary>The zone logic for one player: slim capsule, step offset, speed cap, ground snap and slope limit, all kept in <see cref="PlayerController.TowerZone"/>.</summary>
        void Step(PlayerController player)
        {
            var body = player.Capsule;
            if (!body) return;
            ref TowerZoneState z = ref player.TowerZone;
            Vector3 p = transform.InverseTransformPoint(player.transform.position);
            z.InFootprint = p.x >= footprintMin.x && p.x <= footprintMax.x && p.y >= footprintMin.y && p.y <= footprintMax.y
                          && p.z >= footprintMin.z && p.z <= footprintMax.z;
            z.InFlightZone = z.InFootprint && InAnyFlight(p, padFoot, padHead);
            z.OnRamp = z.InFootprint && InAnyFlight(p, 0.05f, 0.05f);

            if (z.InFootprint && !z.slimmed) { z.savedRadius = body.radius; z.savedHeight = player.StandingHeight; z.savedStep = body.stepOffset; body.stepOffset = towerStepOffset; player.SetWalkCapsule(towerRadius, towerHeight); z.slimmed = true; }
            else if (!z.InFootprint && z.slimmed) { player.SetWalkCapsule(z.savedRadius, z.savedHeight); body.stepOffset = z.savedStep; z.slimmed = false; }

            // The cap follows what the player stands on, never the footprint: ground level and the pad stay at the game's walk speed.
            if (z.slimmed) body.stepOffset = (z.OnRamp || p.y > deckMinHeight) && p.y < cabStepHeight ? upperStepOffset : towerStepOffset;
            player.WalkSpeedCap = z.OnRamp ? stairWalkSpeed : z.InFootprint && p.y > deckMinHeight ? landingWalkSpeed : 0f;
            player.GroundSnapDistance = z.InFlightZone ? stairGroundSnap : 0f;

            if (z.InFlightZone && !z.slopeRaised) { z.savedSlope = body.slopeLimit; body.slopeLimit = flightSlopeLimit; z.slopeRaised = true; }
            else if (!z.InFlightZone && z.slopeRaised) { body.slopeLimit = z.savedSlope; z.slopeRaised = false; }
        }

        void OnDisable()
        {
            PlayerTickHooks.Unregister(this);
            var players = PlayerRegistry.All;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null) continue;
                var body = player.Capsule;
                ref TowerZoneState z = ref player.TowerZone;
                if (!body) continue;
                if (z.slopeRaised) body.slopeLimit = z.savedSlope;
                if (z.slimmed) { player.SetWalkCapsule(z.savedRadius, z.savedHeight); body.stepOffset = z.savedStep; }
                player.WalkSpeedCap = 0f; player.GroundSnapDistance = 0f;
                z.slopeRaised = z.slimmed = false;
            }
        }

        bool InAnyFlight(Vector3 p, float padFootM, float padHeadM)
        {
            foreach (var f in flights)
            {
                Vector3 run = f.head - f.foot;
                var flat = new Vector2(run.x, run.z);
                float length = flat.magnitude;
                if (length < 1e-3f) continue;
                Vector2 dir = flat / length, rel = new Vector2(p.x - f.foot.x, p.z - f.foot.z);
                float along = Vector2.Dot(rel, dir);
                float lateral = rel.x * -dir.y + rel.y * dir.x;
                if (along < -padFootM || along > length + padHeadM || Mathf.Abs(lateral) > halfWidth) continue;
                float lineY = f.foot.y + along * (run.y / length);
                float h = p.y - lineY;
                if (h >= -below && h <= above) return true;
            }
            return false;
        }
    }
}
