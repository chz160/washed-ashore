using UnityEngine;

namespace WashedAshore.Gameplay
{
    [System.Flags]
    public enum PlayerButtons : byte { None = 0, Sprint = 1, Jump = 2 }

    /// <summary>
    /// One tick of player intent (#46 P1). Gathered in Update (PlayerController.Input.cs); the tick reads only this, never an InputAction.
    /// It records raw intent: every game rule (suspended control, stance locks, the jump conditions) is applied again in the tick from the
    /// sim state, so a server fed the same commands gets the same result.
    /// </summary>
    public struct PlayerCommand
    {
        /// <summary>The sim tick this command drives (the player's previous tick plus one).</summary>
        public int Tick;
        /// <summary>x right, y forward; magnitude clamped to 1 at gather.</summary>
        public Vector2 Move;
        /// <summary>Absolute body yaw, degrees, [0, 360).</summary>
        public float Yaw;
        /// <summary>Absolute view pitch, degrees, positive looks down, clamped to the player's pitch limits.</summary>
        public float Pitch;
        /// <summary>Sprint = held; Jump = a press that counts, latched until a tick takes it.</summary>
        public PlayerButtons Buttons;
        /// <summary>A stance key press, latched until a tick takes it; <see cref="StanceKey.None"/> when none.</summary>
        public StanceKey Stance;
    }
}
