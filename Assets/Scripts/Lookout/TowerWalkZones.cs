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
    [DefaultExecutionOrder(-100)]
    public class TowerWalkZones : MonoBehaviour
    {
        [Serializable]
        public struct Flight { public Vector3 foot, head; }   // tower-local nosing line ends

        public Flight[] flights = new Flight[0];
        public Vector3 footprintMin, footprintMax;            // tower-local box for the slim capsule
        public float cabStepHeight = 25.5f;   // tower-local y: from the top of flight 8 up, getting off the ramp onto the cab floor takes a 0.12 m step
        public float upperStepOffset = 0.05f;   // on ramps and upper decks no join is over 5 cm; a larger step lets the controller pop up when it rubs a rail end
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

        PlayerController player;
        CharacterController body;
        float savedSlope, savedRadius, savedHeight, savedStep;
        bool slopeRaised, slimmed;

        public bool InFlightZone { get; private set; }
        public bool OnRamp { get; private set; }
        public bool InFootprint { get; private set; }

        void Update()
        {
            if (!player)
            {
                var go = GameObject.FindGameObjectWithTag("Player");
                if (!go) return;
                player = go.GetComponent<PlayerController>();
                body = go.GetComponent<CharacterController>();
                if (!player || !body) { player = null; return; }
            }
            Vector3 p = transform.InverseTransformPoint(player.transform.position);
            InFootprint = p.x >= footprintMin.x && p.x <= footprintMax.x && p.y >= footprintMin.y && p.y <= footprintMax.y
                          && p.z >= footprintMin.z && p.z <= footprintMax.z;
            InFlightZone = InFootprint && InAnyFlight(p, padFoot, padHead);
            OnRamp = InFootprint && InAnyFlight(p, 0.05f, 0.05f);

            if (InFootprint && !slimmed) { savedRadius = body.radius; savedHeight = body.height; savedStep = body.stepOffset; body.stepOffset = towerStepOffset; player.SetWalkCapsule(towerRadius, towerHeight); slimmed = true; }
            else if (!InFootprint && slimmed) { player.SetWalkCapsule(savedRadius, savedHeight); body.stepOffset = savedStep; slimmed = false; }

            // The cap follows what the player stands on, never the footprint: ground level and the pad stay at the game's walk speed.
            if (slimmed) body.stepOffset = (OnRamp || p.y > deckMinHeight) && p.y < cabStepHeight ? upperStepOffset : towerStepOffset;
            player.WalkSpeedCap = OnRamp ? stairWalkSpeed : InFootprint && p.y > deckMinHeight ? landingWalkSpeed : 0f;
            player.GroundSnapDistance = InFlightZone ? stairGroundSnap : 0f;

            if (InFlightZone && !slopeRaised) { savedSlope = body.slopeLimit; body.slopeLimit = flightSlopeLimit; slopeRaised = true; }
            else if (!InFlightZone && slopeRaised) { body.slopeLimit = savedSlope; slopeRaised = false; }
        }

        void OnDisable()
        {
            if (!body) return;
            if (slopeRaised) body.slopeLimit = savedSlope;
            if (slimmed && player) { player.SetWalkCapsule(savedRadius, savedHeight); body.stepOffset = savedStep; }
            if (player) { player.WalkSpeedCap = 0f; player.GroundSnapDistance = 0f; }
            slopeRaised = slimmed = false;
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
