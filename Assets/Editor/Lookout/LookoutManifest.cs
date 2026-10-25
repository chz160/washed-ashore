using System;
using System.IO;
using UnityEngine;

namespace WashedAshore.Lookout.Editor
{
    /// <summary>
    /// The parts of the tower's build manifest the interaction setup reads (tools/blender/lookout: MC39_Lookout.manifest.json,
    /// landed beside the FBX; generated, never hand-edited). Unity axes, tower-local metres. Field names are the manifest's keys;
    /// JsonUtility fills them by name and ignores the rest. Only the hatch, sashes, firefinder, cab and the top flight are
    /// read: the walkable tower has no lanes (td ruling adr/lookout-interactions-40 R3).
    /// </summary>
    [Serializable]
    public class LookoutManifest
    {
        public const string AssetPath = "Assets/World/Lookout/MC39_Lookout.manifest.json";
        /// <summary>The manifest layout this reader was written against. Another number fails loudly.</summary>
        public const int Schema = 6;

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable] public class Targets { public float cab_floor_y; }
        [Serializable] public class Flight { public int index; public float[] foot, head; public float width; }
        [Serializable] public class Hatch { public string name, collider, swing_volume; public float[] opening_min, opening_max, hinge_axis; public float open_angle_deg, thickness, leaf_width; }
        [Serializable] public class Sash { public string name, collider; public bool hinged; public float[] hinge_axis; }
        [Serializable] public class Firefinder { public string pedestal, alidade; public string[] colliders; public float[] axis; }
        [Serializable] public class Cab { public float floor_thickness, floor_y; }

        /// <summary>The manifest's text as read.</summary>
        [NonSerialized] public string Json;
        public int schema;
        public Targets targets;
        public Flight[] flights;
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

        /// <summary>Parses manifest text and checks the parts the interaction setup can't do without. Throws with the missing key.</summary>
        public static LookoutManifest Parse(string json)
        {
            var m = JsonUtility.FromJson<LookoutManifest>(json);
            if (m == null) throw new InvalidOperationException("Lookout manifest: not JSON");
            m.Json = json;
            if (m.schema != Schema) throw new InvalidOperationException($"Lookout manifest: schema {m.schema}, this reader is for schema {Schema}");
            if (m.targets == null || m.targets.cab_floor_y <= 0f) throw new InvalidOperationException("Lookout manifest: no targets.cab_floor_y");
            if (m.flights == null || m.flights.Length == 0) throw new InvalidOperationException("Lookout manifest: no flights");
            var top = m.flights[m.flights.Length - 1];
            V(top.foot, "top flight foot"); V(top.head, "top flight head");
            if (top.width <= 0f) throw new InvalidOperationException("Lookout manifest: no top flight width");
            if (m.hatch == null || string.IsNullOrEmpty(m.hatch.name)) throw new InvalidOperationException("Lookout manifest: no hatch");
            if (string.IsNullOrEmpty(m.hatch.collider)) throw new InvalidOperationException("Lookout manifest: no hatch.collider");
            if (string.IsNullOrEmpty(m.hatch.swing_volume)) throw new InvalidOperationException("Lookout manifest: no hatch.swing_volume");
            if (m.hatch.leaf_width <= 0f) throw new InvalidOperationException("Lookout manifest: no hatch.leaf_width");
            V(m.hatch.opening_min, "hatch.opening_min"); V(m.hatch.opening_max, "hatch.opening_max"); V(m.hatch.hinge_axis, "hatch.hinge_axis");
            if (m.sashes == null || m.sashes.Length == 0) throw new InvalidOperationException("Lookout manifest: no sashes");
            if (m.firefinder == null || string.IsNullOrEmpty(m.firefinder.alidade) || string.IsNullOrEmpty(m.firefinder.pedestal)) throw new InvalidOperationException("Lookout manifest: no firefinder");
            if (m.firefinder.colliders == null || m.firefinder.colliders.Length == 0) throw new InvalidOperationException("Lookout manifest: no firefinder.colliders");
            V(m.firefinder.axis, "firefinder.axis");
            if (m.cab == null || m.cab.floor_thickness <= 0f) throw new InvalidOperationException("Lookout manifest: no cab.floor_thickness");
            if (m.cab.floor_y <= 0f) throw new InvalidOperationException("Lookout manifest: no cab.floor_y");
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
