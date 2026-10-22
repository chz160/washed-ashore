#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using WashedAshore.Character.Stance;
using WashedAshore.Gameplay;

namespace WashedAshore.Character.DebugCamera
{
    /// <summary>
    /// H7: a third-person camera for QA and Noah to check animations. Editor and development builds only: the whole file is behind
    /// the define, so a release build has no such type. F9 toggles it; the player's own camera and look keep working underneath.
    /// It shows the head (the player never does) and writes nothing on the player.
    /// </summary>
    public class PlayerDebugCamera : MonoBehaviour
    {
        public const string ToggleBinding = "<Keyboard>/f9";

        public Vector3 offset = new Vector3(0.7f, 1.6f, -2.6f);
        public float lookHeight = 1.0f;
        public float follow = 12f;

        PlayerController player;
        FirstPersonBody body;
        Camera mainCamera, debugCamera;
        InputAction toggle;
        bool active;

        public bool Active => active;
        public Camera Camera => debugCamera;

        void Awake()
        {
            player = GetComponent<PlayerController>();
            body = GetComponentInChildren<FirstPersonBody>(true);
            toggle = new InputAction("DebugCamera", InputActionType.Button, ToggleBinding);
            var go = new GameObject("PlayerDebugCamera");
            go.transform.SetParent(transform, false);
            debugCamera = go.AddComponent<Camera>();
            debugCamera.enabled = false;
            debugCamera.nearClipPlane = 0.1f;
            debugCamera.farClipPlane = 1000f;
        }

        void OnEnable() => toggle.Enable();
        void OnDisable() => toggle.Disable();
        void OnDestroy() => toggle.Dispose();

        public void SetActive(bool on)
        {
            if (on == active) return;
            if (body == null) body = GetComponentInChildren<FirstPersonBody>(true);
            mainCamera = player != null ? player.ViewCamera : null;
            active = on;
            if (on)
            {
                debugCamera.CopyFrom(mainCamera != null ? mainCamera : Camera.main);
                debugCamera.depth = (mainCamera != null ? mainCamera.depth : 0f) + 10f;
                debugCamera.transform.SetPositionAndRotation(Target(out Quaternion rot), rot);
            }
            debugCamera.enabled = on;
            if (body != null) body.SetHeadVisible(on);
        }

        Vector3 Target(out Quaternion rotation)
        {
            Transform t = player.transform;
            Vector3 pos = t.TransformPoint(offset);
            Vector3 look = t.position + Vector3.up * lookHeight;
            rotation = Quaternion.LookRotation(look - pos, Vector3.up);
            return pos;
        }

        void LateUpdate()
        {
            if (toggle.WasPressedThisFrame()) SetActive(!active);
            if (!active || player == null) return;
            Vector3 pos = Target(out Quaternion rot);
            float k = 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime);
            debugCamera.transform.SetPositionAndRotation(Vector3.Lerp(debugCamera.transform.position, pos, k), Quaternion.Slerp(debugCamera.transform.rotation, rot, k));
        }
    }

    static class PlayerDebugCameraBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void First() => Attach();

        static void OnLoaded(Scene s, LoadSceneMode m) => Attach();

        static void Attach()
        {
            foreach (var p in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include))
                if (p.GetComponent<PlayerDebugCamera>() == null) p.gameObject.AddComponent<PlayerDebugCamera>();
        }
    }
}
#endif
