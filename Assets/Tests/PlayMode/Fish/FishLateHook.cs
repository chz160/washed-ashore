using System;
using UnityEngine;

namespace WashedAshore.Tests.PlayMode.Fish
{
    /// <summary>
    /// Calls <see cref="Frame"/> in LateUpdate after the fish renderer (1000/1001) has written DrawModes and Visibility,
    /// so a test reads this frame's States next to this frame's draw decisions (a coroutine resumed by
    /// <c>yield return null</c> runs before LateUpdate and would pair them with last frame's; f-artist's triage).
    /// </summary>
    [DefaultExecutionOrder(20000)]
    sealed class FishLateHook : MonoBehaviour
    {
        public Action Frame;

        void LateUpdate() => Frame?.Invoke();

        public static FishLateHook Attach(Action frame)
        {
            var hook = new GameObject("FishLateHook").AddComponent<FishLateHook>();
            hook.Frame = frame;
            return hook;
        }
    }
}
