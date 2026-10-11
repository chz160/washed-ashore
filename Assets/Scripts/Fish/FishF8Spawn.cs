using System.Linq;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Fish
{
    /// <summary>
    /// Fish F8 (Noah's feel check): -fishF8Spawn shore|bluff starts the player at the start of the graded shore walk or on
    /// the McCord bluff crest, facing the water, so the check begins where F7 is measured. Inert without the flag; logs
    /// one line when it acts. Temporary: removed after F8 and the build re-verified (f-producer).
    /// </summary>
    public class FishF8Spawn : MonoBehaviour
    {
        public const string Arg = "-fishF8Spawn";

        // The F7 shore route's teleport start and its first leg (fish-targets.json sightings.routeShore; the same points as
        // BellsBendFpsWalk's shore route), and the McCord bluff pose E with its bank normal (fish-sightings.json pose).
        static readonly Vector3 ShoreStart = new Vector3(1077.9f, 0f, -49.6f), ShoreNext = new Vector3(1062.1f, 0f, -28.8f);
        static readonly Vector3 BluffCrest = new Vector3(401.8f, 0f, 717.6f), BluffNormal = new Vector3(0.81f, 0f, 0.59f);

        static string where;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, Arg);
            if (i < 0) return;
            where = i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : "";
            if (where != "shore" && where != "bluff") { Debug.LogError($"[Fish] {Arg} needs shore or bluff, got '{where}'"); return; }
            new GameObject("FishF8Spawn").AddComponent<FishF8Spawn>();
        }

        System.Collections.IEnumerator Start()
        {
            yield return null;   // after the player's own spawn
            yield return Spawn(where, null);
            Destroy(gameObject);
        }

        /// <summary>
        /// Moves the player to the F8 start for <paramref name="where"/> (shore or bluff), logs it, and after 1 s logs the
        /// camera's yaw against the wanted one (f-qa: the facing must hold); <paramref name="report"/> gets (wanted, actual).
        /// Public for the PlayMode check that runs it in the Editor (no player launch needed).
        /// </summary>
        public static System.Collections.IEnumerator Spawn(string where, System.Action<float, float> report)
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (!player) { Debug.LogError("[Fish] F8 spawn: no PlayerController"); yield break; }
            Vector3 at = where == "shore" ? ShoreStart : BluffCrest;
            Vector3 face = where == "shore" ? ShoreNext - ShoreStart : BluffNormal;
            if (TerrainQuery.TryGroundHeight(at, out float g)) at.y = g + 0.05f;
            var cc = player.GetComponent<CharacterController>();
            if (cc) cc.enabled = false;
            player.transform.SetPositionAndRotation(at, Quaternion.LookRotation(new Vector3(face.x, 0f, face.z).normalized));
            if (cc) cc.enabled = true;
            float wanted = Quaternion.LookRotation(face).eulerAngles.y;
            Debug.Log($"[Fish] F8 spawn {where} at ({at.x:0.0}, {at.y:0.0}, {at.z:0.0}) facing {wanted:0} deg");
            yield return new WaitForSecondsRealtime(1f);
            var cam = Camera.main;
            float yaw = cam ? Quaternion.LookRotation(Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up)).eulerAngles.y : float.NaN;
            Debug.Log($"[Fish] F8 spawn check after 1 s: camera yaw {yaw:0} deg (wanted {wanted:0}), player at ({player.transform.position.x:0.0}, {player.transform.position.z:0.0})");
            report?.Invoke(wanted, yaw);
        }
    }
}
