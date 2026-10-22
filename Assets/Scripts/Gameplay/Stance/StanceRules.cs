namespace WashedAshore.Gameplay
{
    public enum Stance { Stand, Crouch, Squat, Sit, Sleep }

    public enum StanceKey { None, Crouch, Squat, Sit, Sleep }

    /// <summary>
    /// The stance matrix of record (_bmad-output/design/player-stances.md §5.2) as a pure function: no scene, no time.
    /// Row = current stance, column = the input. Whether the move is allowed (headroom, water, tower, surface) is the
    /// controller's job; this only says where the input wants to go.
    /// </summary>
    public static class StanceRules
    {
        public struct Decision
        {
            public bool Change;
            public Stance Target;
        }

        static Decision Go(Stance s) => new Decision { Change = true, Target = s };

        /// <param name="previous">The stance a squat was entered from (Stand or Crouch).</param>
        /// <param name="moving">Move input is held.</param>
        /// <param name="jumpPressed">A jump press this frame that counts (not taken by the Interactor).</param>
        /// <param name="sprintHeld">Sprint is held together with move input.</param>
        /// <param name="still">Horizontal speed is at or under the still limit.</param>
        public static Decision Resolve(Stance current, Stance previous, StanceKey key, bool moving, bool jumpPressed, bool sprintHeld, bool still)
        {
            switch (current)
            {
                case Stance.Stand:
                    switch (key)
                    {
                        case StanceKey.Crouch: return Go(Stance.Crouch);
                        case StanceKey.Squat: return Go(Stance.Squat);
                        case StanceKey.Sit: return still ? Go(Stance.Sit) : default;
                        case StanceKey.Sleep: return still ? Go(Stance.Sleep) : default;
                    }
                    return default;
                case Stance.Crouch:
                    switch (key)
                    {
                        case StanceKey.Crouch: return Go(Stance.Stand);
                        case StanceKey.Squat: return Go(Stance.Squat);
                        case StanceKey.Sit: return still ? Go(Stance.Sit) : default;
                        case StanceKey.Sleep: return still ? Go(Stance.Sleep) : default;
                    }
                    return jumpPressed || sprintHeld ? Go(Stance.Stand) : default;
                case Stance.Squat:
                    switch (key)
                    {
                        case StanceKey.Crouch: return Go(Stance.Crouch);
                        case StanceKey.Squat: return Go(previous);
                        case StanceKey.Sit: return Go(Stance.Sit);
                        case StanceKey.Sleep: return Go(Stance.Sleep);
                    }
                    if (jumpPressed || sprintHeld) return Go(Stance.Stand);
                    return moving ? Go(previous) : default;
                case Stance.Sit:
                    switch (key)
                    {
                        case StanceKey.Crouch: return Go(Stance.Crouch);
                        case StanceKey.Squat: return Go(Stance.Squat);
                        case StanceKey.Sit: return Go(Stance.Stand);
                        case StanceKey.Sleep: return Go(Stance.Sleep);
                    }
                    return moving || jumpPressed ? Go(Stance.Stand) : default;   // sprint alone is ignored
                case Stance.Sleep:
                    return key != StanceKey.None || moving || jumpPressed ? Go(Stance.Stand) : default;
            }
            return default;
        }
    }
}
