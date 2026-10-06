using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WashedAshore.Birds
{
    public enum PatchType { Trail, Meadow }

    [Serializable]
    public class PatchPlan
    {
        public string name;
        public PatchType type;
        public int size, arc;
        public Vector3 centre;
        public float routeDistance, nearestTree, slope, grass, sightlineBack = -1f;
        public List<Vector3> starts = new List<Vector3>();
    }

    [Serializable]
    public class FlockPlan
    {
        public string name;
        public int size, arc, secondaryArc;
        public Vector3 primary, secondary;
        public string primaryKind, secondaryKind;
        public float primaryRouteDistance, secondaryRouteDistance;
    }

    [Serializable]
    public class BirdPlan
    {
        public int seed, patchDraws, flockDraws;
        public List<PatchPlan> patches = new List<PatchPlan>();
        public List<FlockPlan> flocks = new List<FlockPlan>();
        public List<string> redraws = new List<string>();
        public float tallestCrown, tallestCrownTopY;
        public int RobinCount => patches.Sum(p => p.size);
        public int FlockBirdCount => flocks.Sum(f => f.size);
    }
}
