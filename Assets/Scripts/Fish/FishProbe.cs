using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using WashedAshore.World;

namespace WashedAshore.Fish
{
    /// <summary>
    /// f-qa's re-gradable fish record (F4, F7, F9; fish QA plan 3): JSON lines with every live fish every
    /// <see cref="Interval"/> seconds (id, species, tier, position, body min/max Y, speed, the clip rate fed to the
    /// renderer, yaw, the renderer's draw mode and FishMurk value, surface-event flag), every surface sign, and every group
    /// spawn/despawn with an in-frustum flag (N7 pop check). With FishRenderer in the scene (found by type name: the
    /// rendering assembly references this one, not the reverse) each fish also logs the alpha drawn and whether the visible
    /// cap holds it, and every cap transition is a line (N7 cap fades). Inert unless a test adds it or the player starts with
    /// -fishProbe [path]. Runs after the renderer (it writes DrawModes in LateUpdate). A probe allocates; it is never on
    /// in a normal game.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public class FishProbe : MonoBehaviour
    {
        public const string Arg = "-fishProbe";
        public float Interval = 0.25f;
        public string Path;

        StreamWriter writer;
        FishPopulation pop;
        float next;
        readonly StringBuilder sb = new StringBuilder(4096);
        readonly Plane[] frustum = new Plane[6];
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const string RendererType = "WashedAshore.Fish.Rendering.FishRenderer";
        Component renderer;
        System.Reflection.PropertyInfo alphaProp, heldProp;
        System.Reflection.EventInfo capEvent;
        System.Action<string, long, Vector3, bool, float, float> onCap;
        System.Func<FishSurfaceEvent, float> quadSize;
        int badLines;
        readonly System.Collections.Generic.List<string> pendingSigns = new System.Collections.Generic.List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, Arg);
            if (i < 0) return;
            string path = i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1]
                : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.consoleLogPath) ?? Application.persistentDataPath, "fish-probe.jsonl");
            new GameObject("FishProbe").AddComponent<FishProbe>().Path = path;
        }

        void Start()
        {
            pop = FishPopulation.Active;
            if (!pop || string.IsNullOrEmpty(Path)) { enabled = false; return; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)));
            writer = new StreamWriter(Path, false, new UTF8Encoding(false));
            Line($"{{\"probe\":\"fish\",\"seed\":{pop.ActiveSeed},\"brief\":\"{pop.Tuning.briefRevision}\",\"briefSha16\":\"{pop.Tuning.briefSha16}\",\"interval\":{F(Interval)}}}");
            FishEvents.Surface += OnSurface;
            pop.GroupChanged += OnGroup;
            FindRenderer();
            FindQuadSize();
        }

        void FindRenderer()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>())
            {
                if (mb.GetType().FullName != RendererType) continue;
                renderer = mb;
                var type = mb.GetType();
                alphaProp = type.GetProperty("DrawnAlpha");
                heldProp = type.GetProperty("CapHeld");
                capEvent = type.GetEvent("CapTransition");
                if (capEvent != null)
                {
                    onCap = OnCap;
                    capEvent.AddEventHandler(renderer, onCap);
                }
                return;
            }
        }

        /// <summary>
        /// The quad FishSurfaceFx draws for a sign, in metres (f-qa's 6 px floor): its public static QuadSizeMetres(event)
        /// if present, else max(minSize, sizeBodies x size) from its Look(kind) table; null if neither is found.
        /// </summary>
        void FindQuadSize()
        {
            const string FxType = "WashedAshore.Fish.Rendering.FishSurfaceFx";
            // FishSurfaceFx lives in the renderer's assembly.
            var type = renderer ? renderer.GetType().Assembly.GetType(FxType, false) : null;
            if (type == null) return;
            var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var direct = type.GetMethod("QuadSizeMetres", flags, null, new[] { typeof(FishSurfaceEvent) }, null);
            if (direct != null) { quadSize = e => (float)direct.Invoke(null, new object[] { e }); return; }
            var look = type.GetMethod("Look", flags, null, new[] { typeof(FishSurfaceKind) }, null);
            if (look == null) return;
            quadSize = e =>
            {
                object tuple = look.Invoke(null, new object[] { e.kind });
                var tt = tuple.GetType();
                float sizeBodies = (float)tt.GetField("Item3").GetValue(tuple), minSize = (float)tt.GetField("Item4").GetValue(tuple);
                return Mathf.Max(minSize, sizeBodies * Mathf.Max(0.05f, e.size));
            };
        }

        /// <summary>Every probe line passes the FishJson gate; a malformed one is replaced by an error line and logged.</summary>
        void Line(string json)
        {
            if (writer == null) return;
            if (FishJson.TryValidate(json, out string error)) { writer.WriteLine(json); return; }
            if (badLines++ < 5) Debug.LogError($"FishProbe: malformed line not written: {error}");
            writer.WriteLine("{\"probeError\":" + FishJson.Str(error) + "}");
        }

        void OnDestroy()
        {
            FishEvents.Surface -= OnSurface;
            if (pop) pop.GroupChanged -= OnGroup;
            if (renderer && capEvent != null) capEvent.RemoveEventHandler(renderer, onCap);
            writer?.Dispose();
            writer = null;
        }

        void LateUpdate()
        {
            // Each sign raised this frame: was it drawn (FishSurfaceFx ran before this, order 1001 < 10000)?
            if (writer != null && pendingSigns.Count > 0)
            {
                bool active = FishSignDraws.Active(out int drawnNow);
                foreach (var id in pendingSigns)
                    Line($"{{\"signDrawn\":{(active && drawnNow >= 1 ? "true" : "false")},\"event\":{id},\"fxActive\":{(active ? "true" : "false")},\"fxDrawnLastFrame\":{drawnNow},\"t\":{F(Time.time)}}}");
                pendingSigns.Clear();
            }
            if (writer == null || Time.time < next) return;
            next = Time.time + Interval;
            var t = pop.Tuning;
            sb.Clear();
            sb.Append("{\"t\":").Append(F(Time.time)).Append(",\"water\":").Append(F((float)WaterClock.Now));
            var cam = Camera.main;
            if (cam) sb.Append(",\"cam\":").Append(V(cam.transform.position)).Append(",\"camFwd\":").Append(V(cam.transform.forward)).Append(",\"fov\":").Append(F(cam.fieldOfView));
            if (pop.PlayerOverride.HasValue) sb.Append(",\"player\":").Append(V(pop.PlayerOverride.Value.position)).Append(",\"playerMode\":\"").Append(pop.PlayerOverride.Value.mode).Append('"');
            if (!renderer) { FindRenderer(); FindQuadSize(); }
            var alpha = renderer && alphaProp != null ? alphaProp.GetValue(renderer) as float[] : null;
            var held = renderer && heldProp != null ? heldProp.GetValue(renderer) as bool[] : null;
            sb.Append(",\"fish\":[");
            for (int i = 0; i < pop.Count; i++)
            {
                var s = pop.States[i];
                var sp = t.species[s.speciesIndex];
                if (i > 0) sb.Append(',');
                float length = s.sizeScale * pop.Bodies.bodies[s.variantIndex].noseToTail;
                sb.Append("{\"id\":").Append(s.fishId).Append(",\"group\":").Append(s.group).Append(",\"len\":").Append(F(length))
                  .Append(",\"fwd\":").Append(V(s.forward)).Append(",\"sp\":\"").Append(sp.name).Append("\",\"tier\":\"").Append(sp.tier)
                  .Append("\",\"x\":").Append(F(s.position.x)).Append(",\"y\":").Append(F(s.position.y)).Append(",\"z\":").Append(F(s.position.z))
                  .Append(",\"minY\":").Append(F(s.bodyMinY)).Append(",\"maxY\":").Append(F(s.bodyMaxY))
                  .Append(",\"speed\":").Append(F(s.speed)).Append(",\"rate\":").Append(F(s.animRate))
                  .Append(",\"yaw\":").Append(F(FishSteering.Yaw(s.forward))).Append(",\"mode\":\"").Append(s.mode)
                  .Append("\",\"draw\":\"").Append(i < pop.DrawModes.Length ? pop.DrawModes[i] : FishDrawMode.None)
                  .Append("\",\"vis\":").Append(F(i < pop.Visibility.Length ? pop.Visibility[i] : 0f))
                  .Append(",\"event\":").Append(s.inSurfaceEvent ? "true" : "false");
                if (alpha != null && i < alpha.Length) sb.Append(",\"alpha\":").Append(F(alpha[i]));
                if (held != null && i < held.Length) sb.Append(",\"capHeld\":").Append(held[i] ? "true" : "false");
                sb.Append('}');
            }
            sb.Append("]}");
            Line(sb.ToString());
        }

        void OnSurface(FishSurfaceEvent e)
        {
            string quad = quadSize != null ? F(quadSize(e)) : "null";
            pendingSigns.Add(FishJson.Str($"{e.kind}:{e.fishId}:{F((float)e.startTime)}"));
            Line($"{{\"event\":\"{e.kind}\",\"id\":{e.fishId},\"sp\":{e.species},\"x\":{F(e.position.x)},\"y\":{F(e.position.y)},\"z\":{F(e.position.z)}," +
                 $"\"start\":{F((float)e.startTime)},\"dur\":{F(e.duration)},\"spontaneous\":{(e.spontaneous ? "true" : "false")},\"far\":{(e.farWater ? "true" : "false")},\"bask\":{(e.bask ? "true" : "false")}," +
                 $"\"bodySize\":{F(e.size)},\"quadM\":{quad},\"t\":{F(Time.time)}}}");
        }

        void OnCap(string kind, long id, Vector3 p, bool inView, float distance, float planned)
        {
            Line($"{{\"cap\":\"{kind}\",\"id\":{id},\"x\":{F(p.x)},\"y\":{F(p.y)},\"z\":{F(p.z)},\"inView\":{(inView ? "true" : "false")}," +
                              $"\"dist\":{F(distance)},\"planned\":{F(planned)},\"t\":{F(Time.time)}}}");
        }

        void OnGroup(int group, Vector3 home, bool added)
        {
            var cam = Camera.main;
            bool inFrustum = false;
            if (cam)
            {
                GeometryUtility.CalculateFrustumPlanes(cam, frustum);
                inFrustum = GeometryUtility.TestPlanesAABB(frustum, new Bounds(home, Vector3.one * 2f * pop.Tuning.species.Max(s => s.groupRadius)));
            }
            Line($"{{\"{(added ? "spawn" : "despawn")}\":{group},\"x\":{F(home.x)},\"z\":{F(home.z)},\"inFrustum\":{(inFrustum ? "true" : "false")},\"t\":{F(Time.time)}}}");
        }

        static string F(float v) => v.ToString("0.###", Inv);

        static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    }
}
