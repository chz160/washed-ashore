using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>A wall the THROUGH test watches: horizontal unit direction toward it, face distance from the run's reference line, and the along range it applies to.</summary>
    public class ProbeWall
    {
        public Vector3 dir;
        public float face;
        public float aMin = -1e9f, aMax = 1e9f;
    }

    /// <summary>One run of the #64 probe (spec-64 s3). All positions are world metres; feet = player.transform.position.</summary>
    public class ProbeRun
    {
        public string caseName, flight = "-", dir = "-", side = "-", refKey = "";
        public float yawOff;                 // degrees, magnitude
        public float cap, r;                 // speed cap (m/s), capsule radius for THROUGH
        public float expectedWalkCap;        // value player.WalkSpeedCap must have after settling (0 on the plain slab)
        public Vector3 start; public float yaw;
        public Vector3 d, refPt, toWall;     // clean path, reference point, horizontal unit toward the target wall
        public float aStart, aEnd, runEnd;   // along bounds, m
        public float endTime = -1f;          // >= 0: the run and its section end at this time (FX)
        public float afterEnd = -1f;         // >= 0: keep walking this long after aEnd, end zone not scored (F2i)
        public float idealPath = float.NaN;  // NaN prints "-"
        public bool useAt, control, tower, logContacts, isRef;
        public List<ProbeWall> walls = new List<ProbeWall>();   // lateral THROUGH test (T1, F5t, F1m)
        public List<Collider> wallCols = new List<Collider>();  // ComputePenetration THROUGH test
        public float surfRef, surfFar, spanL;   // tower rows: surface y at the reference point, at the far end, and the flight length (spec r9 5d checks)
        public bool lateralThrough, noYaw;
        public string extraFlag = "", variant = "";
        public System.Func<Vector3, bool> seated;   // F2i: seat check on the final feet
        public string note = "";
    }

    public class ProbeResult
    {
        public ProbeRun run;
        public int id;
        public string invalid;               // null when the run measured
        public List<string> flags = new List<string>();
        public float miss1 = float.NaN, miss2 = float.NaN, startLat = float.NaN, contactAlong = float.NaN;
        public float keptH, keptPath, minV, longest, firstStopAlong = float.NaN, riseMax, refKept = float.NaN;
        public int stops, endStops, air, endStopK = -1;
        public bool stuck, through, seatFail;
        public string seatGap = "-", touched = "-", verdict = "", detail = "", mode = "", dtText = "";
        public Vector3 sectionEndFeet;
        public float dt, stepAtStart;
    }

    /// <summary>F5t only: re-applies the tower's speed cap and ground snap every frame after TowerWalkZones (order -100) zeroed them, before PlayerController (order 0).</summary>
    [DefaultExecutionOrder(-50)]
    public class ProbeTowerStateSetter : MonoBehaviour
    {
        public WashedAshore.Gameplay.PlayerController player;
        public float cap = 1.15f, snap = 0.35f;
        void Update() { if (player) { player.WalkSpeedCap = cap; player.GroundSnapDistance = snap; } }
    }

    /// <summary>TowerStep only (spec 5d): while the capsule is slimmed, forces the ramp stepOffset 0.05 to 0.25 every frame, after TowerWalkZones (order -100) and before PlayerController.</summary>
    [DefaultExecutionOrder(-50)]
    public class ProbeStepForcer : MonoBehaviour
    {
        public CharacterController body;
        public int framesChanged;
        void Update()
        {
            if (!body || body.radius > 0.3f) return;   // slimmed = inside the tower footprint
            if (Mathf.Abs(body.stepOffset - 0.05f) < 1e-4f) { body.stepOffset = 0.25f; framesChanged++; }
        }
    }

    /// <summary>Read-only: records every OnControllerColliderHit on the Player root. Does not touch movement.</summary>
    public class ProbeContactRecorder : MonoBehaviour
    {
        public struct Hit { public int frame; public string name; public int layer; public Vector3 normal, point; public float feetY; }
        public readonly List<Hit> hits = new List<Hit>();

        void OnControllerColliderHit(ControllerColliderHit h)
        {
            hits.Add(new Hit { frame = Time.frameCount, name = h.collider.name, layer = h.collider.gameObject.layer, normal = h.normal, point = h.point, feetY = transform.position.y });
        }
    }
}
