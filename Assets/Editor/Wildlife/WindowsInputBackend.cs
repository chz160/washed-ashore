using UnityEditor;

namespace WashedAshore.Wildlife.Editor
{
    /// <summary>
    /// WL-BUG-6: the Windows player crashed on quit inside Windows.Gaming.Input.dll (0xc0000005) after a few
    /// minutes of play, after Unity's own shutdown had finished. The player's native input layer picks WGI for
    /// game controllers when the backend hint is Default; forcing XInput keeps it off WGI. Keyboard and mouse
    /// are unaffected and XInput pads still work; DirectInput-only HID pads would no longer be read by the
    /// player's native gamepad layer. Re-runnable; changes only PlayerSettings.windowsGamepadBackendHint.
    /// Run via: unity command eval "return WashedAshore.Wildlife.Editor.WindowsInputBackend.Apply();"
    /// </summary>
    public static class WindowsInputBackend
    {
        public const WindowsGamepadBackendHint Required = WindowsGamepadBackendHint.WindowsGamepadBackendHintXInput;

        [MenuItem("Washed Ashore/Platform/Use XInput Gamepad Backend (WL-BUG-6)")]
        static void ApplyMenu() => UnityEngine.Debug.Log(Apply());

        public static string Apply()
        {
            var before = PlayerSettings.windowsGamepadBackendHint;
            if (before != Required)
            {
                PlayerSettings.windowsGamepadBackendHint = Required;
                AssetDatabase.SaveAssets();
            }
            return $"windowsGamepadBackendHint: {before} -> {PlayerSettings.windowsGamepadBackendHint}";
        }
    }
}
