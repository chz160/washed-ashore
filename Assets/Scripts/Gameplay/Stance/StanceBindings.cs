using UnityEngine.InputSystem;

namespace WashedAshore.Gameplay
{
    /// <summary>The stance bindings, all toggles (player-stances.md §5.1). One table so the binding audit has one place to read.</summary>
    public static class StanceBindings
    {
        public struct Entry
        {
            public StanceKey Key;
            public string Name, Keyboard, Gamepad;
        }

        public static readonly Entry[] All =
        {
            new Entry { Key = StanceKey.Crouch, Name = "Crouch", Keyboard = "<Keyboard>/leftCtrl", Gamepad = "<Gamepad>/buttonEast" },
            new Entry { Key = StanceKey.Squat, Name = "Squat", Keyboard = "<Keyboard>/c", Gamepad = "<Gamepad>/dpad/down" },
            new Entry { Key = StanceKey.Sit, Name = "Sit", Keyboard = "<Keyboard>/x", Gamepad = "<Gamepad>/dpad/left" },
            new Entry { Key = StanceKey.Sleep, Name = "Sleep", Keyboard = "<Keyboard>/z", Gamepad = "<Gamepad>/dpad/right" },
        };

        public static InputAction Create(Entry e)
        {
            var a = new InputAction(e.Name, InputActionType.Button, e.Keyboard);
            a.AddBinding(e.Gamepad);
            return a;
        }
    }
}
