using System;
using System.IO;
using UnityEngine;

namespace WashedAshore.Lookout.Editor
{
    /// <summary>
    /// The parts of the tower's build manifest the gameplay setup and its tests read
    /// (tools/blender/lookout: MC39_Lookout.manifest.json, landed beside the FBX). Unity axes, tower-local metres.
    /// Field names are the manifest's keys; JsonUtility fills them by name.
    /// </summary>
    [Serializable]
    public class LookoutManifest
    {
        /// <summary>Where the build lands it (t-tools: TowerBuilder.ManifestPath).</summary>
        public const string AssetPath = "Assets/World/Lookout/MC39_Lookout.manifest.json";
        /// <summary>The manifest layout this reader was written against. Another number fails loudly.</summary>
        public const int Schema = 4;

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] public class Targets { public float base_square, top_square, cab_floor_y, eave_y, stair_width; public int[] risers_per_flight; }
        [Serializable] public class Flight { public int index, risers; public float[] foot, head; public float width, pitch_deg, slope_length, going, riser; public string foot_on, head_on, foot_empty, head_empty;
            /// <summary>Level floor beyond each end, along the lane, that the capsule's centre can be carried over, m. -1 = not in the manifest.</summary>
            public float foot_run_room = -1f, head_run_room = -1f;
            /// <summary>A linked level run-out at that end (lane-end ruling R3); no empties = the built-in straight run.</summary>
            public RunOut foot_run_out, head_run_out;
            /// <summary>The join mouth at each free end, per side. Absent (half-widths not set) at a linked end.</summary>
            public Mouth foot_mouth, head_mouth;
            /// <summary>The build's own figures for the hand-over point of a built-in straight run (absent where the end has a linked run-out).</summary>
            public HandOver foot_hand_over, head_hand_over; }

        /// <summary>A number the manifest did not give.</summary>
        public const float NotGiven = -999f;

        /// <summary>
        /// One side of a free end's final level stretch. <c>reach</c>: how far to that side a body at wall_reach can
        /// stand on the deck, m, sampled every <c>reach_spacing</c> from the free end to the lip or junction.
        /// <c>reachable_span</c> is the same at the free end; the rest are the build's notes for the reports.
        /// </summary>
        [Serializable] public class MouthSide { public float[] reach; public float reachable_span = -1f, coverage = -1f; public string bound_by, reachable_span_ended_by; }
        /// <summary>A free end's join mouth. Left and right are as seen standing at the free end looking along the lane toward the stair; <c>left_dir</c> is that left as a unit vector.</summary>
        [Serializable] public class Mouth { public string stretch; public float stretch_length = -1f, wall_reach = -1f, reach_spacing = -1f; public float[] left_dir; public MouthSide left, right;
            public bool Present => left != null && right != null && left.reach != null && right.reach != null && left.reach.Length > 0 && right.reach.Length > 0; }
        /// <summary>The build's claims at a built-in run's hand-over point: margins over the 0.30 m bar from walls and from the nearest drop.</summary>
        [Serializable] public class HandOver { public string kind; public float[] point; public float run, point_wall_margin = NotGiven, point_mouth_margin = NotGiven; }

        /// <summary>
        /// A run-out of one or more level segments from a flight end to its hand-over point. One empty per point, the
        /// first being the first point after the lane end and the last the hand-over point; positions are read from the
        /// empties (tower-td-note §6 condition 8), the numbers below are the build's own claims for the checks.
        /// </summary>
        [Serializable] public class RunOut
        {
            public string[] empties;
            public float margin = -1f, min_wall, turn_deg, crossing_length, landing_margin, point_wall_margin = NotGiven, point_mouth_margin = NotGiven;
            /// <summary>The turn at the start of each segment, degrees: the first from the flight's heading, the rest from the segment before.</summary>
            public float[] segment_turns_deg;
            /// <summary>The run-out points as x,y,z triples, one per empty, in the empties' order (JsonUtility can't read the nested "points").</summary>
            public float[] points_flat;
            public string turn, arrival, shape, status;
            public int landing;
            public bool Present => empties != null && empties.Length > 0;
        }
        /// <summary>
        /// A level lane linked to a flight's end (tower-td-note §12a): the step off the top flight onto the cab floor.
        /// Its foot empty coincides with the flight's end; its head empty is the hand-over point on the floor.
        /// </summary>
        [Serializable] public class FreeEndClear { public float margin_over_reach = -1f, south_wall_proxy, ring, open_hatch, nearest_wall_proxy; }
        [Serializable] public class ExitLane { public int index; public string kind, foot_empty, head_empty, wall_proxy; public float[] foot, head; public float width, length, wall_overlap_max = -1f; public FreeEndClear free_end_clear; public Mouth head_mouth; }
        [Serializable] public class OpenStretch { public int edge; public float[] from, to; public string why; }
        [Serializable] public class Landing { public int index; public float y, platform_depth, platform_width, inner_edge_from_centre_line; public OpenStretch[] open_stretches; }
        [Serializable] public class Hatch { public string name, collider, swing_volume; public string[] corner_empties; public float[] pivot, opening_min, opening_max, hinge_axis; public float open_angle_deg, thickness, leaf_width; }
        [Serializable] public class Sash { public string name, collider; public bool hinged; public float[] pivot, hinge_axis, outward; }
        [Serializable] public class Numeral { public float bearing_deg; public float[] position; }
        [Serializable] public class Firefinder { public string pedestal, ring, alidade, pointer_empty, numeral_empty_prefix; public string[] colliders; public float[] axis; public float ring_radius; }
        [Serializable] public class Cab { public float floor_thickness, floor_y; public float[] walk_rect_min, walk_rect_max; }
        [Serializable] public class LaneEndClearance { public int flight, landing; public string end, nearest; public float clear; }
        [Serializable] public class CorridorFinding { public int flight; public string member, kind; public float intrusion_from_corridor_edge, closest_to_eye_path, lowest_above_nosing; }
        [Serializable] public class CorridorException { public string member, kind, sheet; public int flight; public float along_run_from, along_run_to, intrusion_from_corridor_edge, lateral_at_deepest, lowest_above_nosing, closest_to_eye_path; }
        [Serializable] public class Corridor { public float width, height, eye_height; public CorridorException[] exceptions; public CorridorFinding[] findings; public LaneEndClearance[] lane_end_clearance; }

        /// <summary>The manifest's text as read, for callers that need keys this class doesn't carry (the builder's objects and LODs).</summary>
        [NonSerialized] public string Json;
        public int schema;
        public float[] area_min, area_max;
        public Corridor corridor;
        public Targets targets;
        public Flight[] flights;
        /// <summary>Empty or absent when the model has no exit lane.</summary>
        public ExitLane[] exit_lanes;
        public Landing[] landings;
        public Hatch hatch;
        public Sash[] sashes;
        public Firefinder firefinder;
        public Cab cab;
#pragma warning restore 0649

        public static Vector3 V(float[] xyz, string what)
        {
            if (xyz == null || xyz.Length != 3) throw new InvalidOperationException($"Lookout manifest: '{what}' is not a 3-vector");
            return new Vector3(xyz[0], xyz[1], xyz[2]);
        }

        /// <summary>Parses manifest text and checks the parts the gameplay setup can't do without. Throws with the missing key.</summary>
        public static LookoutManifest Parse(string json)
        {
            var m = JsonUtility.FromJson<LookoutManifest>(json);
            if (m == null) throw new InvalidOperationException("Lookout manifest: not JSON");
            m.Json = json;
            if (m.schema != Schema) throw new InvalidOperationException($"Lookout manifest: schema {m.schema}, this reader is for schema {Schema}");
            if (m.targets == null || m.targets.cab_floor_y <= 0f) throw new InvalidOperationException("Lookout manifest: no targets.cab_floor_y");
            if (m.targets.stair_width <= 0f) throw new InvalidOperationException("Lookout manifest: no targets.stair_width");
            if (m.flights == null || m.flights.Length == 0) throw new InvalidOperationException("Lookout manifest: no flights");
            if (m.hatch == null || string.IsNullOrEmpty(m.hatch.name)) throw new InvalidOperationException("Lookout manifest: no hatch");
            if (m.sashes == null || m.sashes.Length == 0) throw new InvalidOperationException("Lookout manifest: no sashes");
            if (m.firefinder == null || string.IsNullOrEmpty(m.firefinder.alidade) || string.IsNullOrEmpty(m.firefinder.pedestal)) throw new InvalidOperationException("Lookout manifest: no firefinder");
            if (m.firefinder.colliders == null || m.firefinder.colliders.Length == 0) throw new InvalidOperationException("Lookout manifest: no firefinder.colliders");
            if (string.IsNullOrEmpty(m.hatch.swing_volume)) throw new InvalidOperationException("Lookout manifest: no hatch.swing_volume");
            if (m.hatch.leaf_width <= 0f) throw new InvalidOperationException("Lookout manifest: no hatch.leaf_width");
            if (m.area_min == null || m.area_max == null) throw new InvalidOperationException("Lookout manifest: no area_min / area_max");
            if (m.cab == null || m.cab.floor_thickness <= 0f) throw new InvalidOperationException("Lookout manifest: no cab.floor_thickness");
            return m;
        }

        public static LookoutManifest Load()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", AssetPath));
            if (!File.Exists(path)) throw new InvalidOperationException($"Lookout manifest not found at {AssetPath}");
            return Parse(File.ReadAllText(path));
        }
    }
}
