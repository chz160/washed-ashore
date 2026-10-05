using NUnit.Framework;
using UnityEngine;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// W3.3: the mouse lock is gated by platform. WebGL waits for a click; standalone and
    /// the Editor lock on Start exactly as before the web build.
    /// </summary>
    public class MouseLockGateTests
    {
        [TestCase(RuntimePlatform.WindowsPlayer, ExpectedResult = true)]
        [TestCase(RuntimePlatform.WindowsEditor, ExpectedResult = true)]
        [TestCase(RuntimePlatform.LinuxPlayer, ExpectedResult = true)]
        [TestCase(RuntimePlatform.OSXPlayer, ExpectedResult = true)]
        [TestCase(RuntimePlatform.WebGLPlayer, ExpectedResult = false)]
        public bool ShouldLockOnStart(RuntimePlatform platform) =>
            PlayerController.ShouldLockOnStart(platform);

        [TestCase(RuntimePlatform.WebGLPlayer, ExpectedResult = true)]
        [TestCase(RuntimePlatform.WindowsPlayer, ExpectedResult = false)]
        [TestCase(RuntimePlatform.WindowsEditor, ExpectedResult = false)]
        public bool ShouldRelockOnClick(RuntimePlatform platform) =>
            PlayerController.ShouldRelockOnClick(platform);

        [TestCase(RuntimePlatform.WebGLPlayer, CursorLockMode.None, ExpectedResult = false)]
        [TestCase(RuntimePlatform.WebGLPlayer, CursorLockMode.Locked, ExpectedResult = true)]
        [TestCase(RuntimePlatform.WindowsPlayer, CursorLockMode.None, ExpectedResult = true)]
        public bool ShouldApplyMouseLook(RuntimePlatform platform, CursorLockMode lockState) =>
            PlayerController.ShouldApplyMouseLook(platform, lockState);
    }
}
