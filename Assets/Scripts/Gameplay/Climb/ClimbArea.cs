using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// A set of stair lanes with real landings between them (tower-td-note §6 condition 6, §12a). Data only: the
    /// PlayerController decides everything from position each frame. Inside the area, once the player has joined a
    /// lane, the small capsule, the jump lock and the area walk speed apply until they leave through a lane foot
    /// marked <see cref="StairLane.footLeavesArea"/>, respawn, are snapped, or are outside <see cref="LocalBounds"/>.
    /// Two lane ends that coincide are linked: the player passes from one lane to the other there with no hand-over.
    /// Lanes and bounds are written by the build script from the model's lane empties, never by hand.
    /// </summary>
    public class ClimbArea : MonoBehaviour
    {
        /// <summary>Two lane ends closer than this are one junction, m (tower-td-note §12a).</summary>
        public const float LinkTolerance = 0.001f;
        const int NoLink = -1;

        [SerializeField] ClimbTuning tuning;
        [Tooltip("Lanes in this transform's local space, bottom to top.")]
        [SerializeField] StairLane[] lanes = new StairLane[0];
        [Tooltip("Everything a player in this area can reach, local space. Outside it, climb state clears.")]
        [SerializeField] Bounds localBounds;

        static readonly List<ClimbArea> active = new List<ClimbArea>();

        StairLane[] worldLanes = new StairLane[0];
        float[] gates = new float[0];
        bool[] blocked = new bool[0];
        int[] links = new int[0];   // per lane end (lane x 2, + 1 for the head): the linked end's code, or NoLink

        /// <summary>How many areas are enabled: the PlayerController's one early check.</summary>
        public static int ActiveCount => active.Count;
        public ClimbTuning Tuning => tuning;
        public int LaneCount => worldLanes.Length;
        public Bounds LocalBounds => localBounds;
        /// <summary>Null when the lanes' links are sound; otherwise what is wrong (an end shared by three lanes, two inclines linked).</summary>
        public string LinkProblem { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => active.Clear();

        void OnEnable()
        {
            Rebuild();
            if (!active.Contains(this)) active.Add(this);
            if (LinkProblem != null) Debug.LogError($"ClimbArea '{name}': {LinkProblem}", this);
        }

        void OnDisable() => active.Remove(this);

        /// <summary>Sets the data (build script and tests) and refreshes the world-space lanes.</summary>
        public void Configure(ClimbTuning climbTuning, StairLane[] localLanes, Bounds bounds)
        {
            tuning = climbTuning;
            lanes = (StairLane[])localLanes.Clone();
            localBounds = bounds;
            Rebuild();
        }

        /// <summary>Swaps the tuning asset (the build script; tests use a copy for the detector's twin).</summary>
        public void SetTuning(ClimbTuning climbTuning) => tuning = climbTuning;

        /// <summary>World-space lanes and their links from the local lanes. The area is static, so this runs on enable and on Configure only.</summary>
        public void Rebuild()
        {
            if (worldLanes.Length != lanes.Length)
            {
                worldLanes = new StairLane[lanes.Length];
                gates = new float[lanes.Length];
                blocked = new bool[lanes.Length];
                links = new int[lanes.Length * 2];
                for (int i = 0; i < gates.Length; i++) gates[i] = float.PositiveInfinity;
            }
            for (int i = 0; i < lanes.Length; i++) worldLanes[i] = lanes[i].Transformed(transform);
            FindLinks();
        }

        Vector3 EndPoint(int code) => (code & 1) == 1 ? worldLanes[code >> 1].head : worldLanes[code >> 1].foot;

        void FindLinks()
        {
            LinkProblem = null;
            for (int i = 0; i < links.Length; i++) links[i] = NoLink;
            for (int a = 0; a < links.Length; a++)
                for (int b = a + 1; b < links.Length; b++)
                {
                    if (a >> 1 == b >> 1 || Vector3.Distance(EndPoint(a), EndPoint(b)) > LinkTolerance) continue;
                    if (links[a] != NoLink || links[b] != NoLink) { LinkProblem = $"more than two lane ends meet at {EndPoint(a):F3} (lanes {(a >> 1) + 1} and {(b >> 1) + 1})"; continue; }
                    if (!ClimbRules.IsLevel(worldLanes[a >> 1]) && !ClimbRules.IsLevel(worldLanes[b >> 1]))
                    { LinkProblem = $"lanes {(a >> 1) + 1} and {(b >> 1) + 1} are both inclined and share an end: a landing is missing between two flights"; continue; }
                    links[a] = b;
                    links[b] = a;
                }
        }

        /// <summary>Lane <paramref name="index"/> in world space.</summary>
        public ref readonly StairLane Lane(int index) => ref worldLanes[index];

        /// <summary>The lane end linked to an end of lane <paramref name="index"/>; false at a free end.</summary>
        public bool TryLink(int index, ClimbRules.End end, out int other, out ClimbRules.End otherEnd)
        {
            int code = links[index * 2 + (end == ClimbRules.End.Head ? 1 : 0)];
            other = code == NoLink ? NoLink : code >> 1;
            otherEnd = code == NoLink ? ClimbRules.End.None : ((code & 1) == 1 ? ClimbRules.End.Head : ClimbRules.End.Foot);
            return code != NoLink;
        }

        /// <summary>The highest lane parameter reachable on a lane right now (infinity when nothing gates it).</summary>
        public float Gate(int index) => gates[index];

        /// <summary>Stops a lane at <paramref name="maxU"/> (the shut hatch on the top flight). Infinity opens it.</summary>
        public void SetGate(int index, float maxU) => gates[index] = maxU;

        /// <summary>A blocked lane can't be joined or passed onto (the cab's exit lane while the hatch isn't fully open).</summary>
        public bool Blocked(int index) => blocked[index];

        public void SetBlocked(int index, bool isBlocked) => blocked[index] = isBlocked;

        public bool Contains(Vector3 worldPoint) => localBounds.Contains(transform.InverseTransformPoint(worldPoint));

        /// <summary>The enabled area containing a point, or null.</summary>
        public static ClimbArea At(Vector3 worldPoint)
        {
            for (int i = 0; i < active.Count; i++)
                if (active[i] != null && active[i].tuning != null && active[i].Contains(worldPoint)) return active[i];
            return null;
        }
    }
}
