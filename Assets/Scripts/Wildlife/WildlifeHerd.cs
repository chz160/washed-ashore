using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WashedAshore.Wildlife
{
    /// <summary>
    /// A group of animals (a solitary fox is a group of one). The transform sits on the group's
    /// anchor. The group walks legs together: the first member to finish idling picks the next group
    /// target (a wander leg from the centroid, kept on the leash around the anchor), and the others
    /// follow to points near it. An alarm from any member makes the whole group react.
    /// </summary>
    public class WildlifeHerd : MonoBehaviour
    {
        [SerializeField] WildlifeSpecies species;
        [Tooltip("Route arc (0-based from W0) the anchor was placed in; retune R0.")]
        [SerializeField] int routeArc = -1;

        readonly List<WildlifeAgent> members = new List<WildlifeAgent>();
        Vector3 groupTarget;
        float lastLegTime = float.NegativeInfinity;

        public WildlifeSpecies Species { get => species; set => species = value; }
        public int RouteArc { get => routeArc; set => routeArc = value; }
        public IReadOnlyList<WildlifeAgent> Members => members;
        public Vector3 Anchor => transform.position;

        public Vector3 Centroid
        {
            get
            {
                if (members.Count == 0) return transform.position;
                Vector3 sum = Vector3.zero;
                foreach (var m in members) sum += m.transform.position;
                return sum / members.Count;
            }
        }

        internal void Register(WildlifeAgent a)
        {
            if (!members.Contains(a)) members.Add(a);
        }

        internal void Unregister(WildlifeAgent a) => members.Remove(a);

        internal void RaiseAlarm(WildlifeAgent source)
        {
            foreach (var m in members)
                if (m != source) m.OnHerdAlarm();
        }

        /// <summary>Where <paramref name="asker"/> should walk next; may start a new group leg.</summary>
        internal bool NextWanderTarget(WildlifeAgent asker, SpeciesTuning t, System.Random rng, out Vector3 target)
        {
            if (Time.time - lastLegTime > t.idleSeconds.x)
            {
                if (!PickGroupTarget(t, rng)) { target = default; return false; }
                lastLegTime = Time.time;
                foreach (var m in members)
                    if (m != asker) m.OnHerdMove();
            }

            float spread = members.Count > 1 ? t.cohesionRadius * 0.4f : 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = groupTarget + Disc(rng) * spread;
                if (NavMesh.SamplePosition(WildlifeAgent.OnGround(p), out var hit, 3f, NavMesh.AllAreas)) { target = hit.position; return true; }
            }
            target = groupTarget;
            return true;
        }

        bool PickGroupTarget(SpeciesTuning t, System.Random rng)
        {
            Vector3 c = Centroid;
            for (int i = 0; i < 10; i++)
            {
                Vector3 cand;
                if (Flat(c, Anchor) > t.leashRadius * 0.8f)
                    cand = Anchor + Disc(rng) * (t.leashRadius * 0.4f); // walk back home after a flee
                else
                {
                    float leg = t.wanderLeg.x + (float)rng.NextDouble() * (t.wanderLeg.y - t.wanderLeg.x);
                    Vector3 d = Disc(rng);
                    cand = c + (d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward) * leg;
                }
                if (Flat(cand, Anchor) > t.leashRadius) continue;
                if (!NavMesh.SamplePosition(WildlifeAgent.OnGround(cand), out var hit, 4f, NavMesh.AllAreas)) continue;
                groupTarget = hit.position;
                return true;
            }
            return false;
        }

        static Vector3 Disc(System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt((float)rng.NextDouble());
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
