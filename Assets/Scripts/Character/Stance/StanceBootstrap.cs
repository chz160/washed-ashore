using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Gameplay;

namespace WashedAshore.Character.Stance
{
    /// <summary>
    /// Adds the stance view parts to every Player that has a PlayerController when a scene loads, so no scene or prefab needs a
    /// hand edit and a rebuilt level gets them again. Idempotent: a part already on the Player is left alone.
    /// </summary>
    static class StanceBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void First() => Attach();

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

        public static void Attach()
        {
            foreach (var player in Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include))
            {
                if (player.GetComponent<StanceDim>() == null) player.gameObject.AddComponent<StanceDim>();
            }
        }
    }
}
