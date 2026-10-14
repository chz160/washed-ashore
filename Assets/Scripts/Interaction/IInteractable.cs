namespace WashedAshore.Interaction
{
    /// <summary>
    /// Something the player can aim at and use (spec I1, tower-design-spec.md §3.1). Put it on the object or any
    /// parent of the collider the aim ray hits. There is no highlight of any kind (N12): the prompt line is the
    /// only feedback, and the thing moving.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>The verb for the current state, capitalised ("Open").</summary>
        string Verb { get; }

        /// <summary>The noun, lower case ("hatch").</summary>
        string Noun { get; }

        /// <summary>False means no prompt and the press does nothing (moving, blocked, not usable from here).</summary>
        bool CanInteract(Interactor by);

        /// <summary>Called once per press while aimed at and <see cref="CanInteract"/> is true.</summary>
        void Interact(Interactor by);
    }

    /// <summary>
    /// The one modal seam (the firefinder). The <see cref="Interactor"/> holds at most one focus; while it does,
    /// nothing is aimed at, and the focus gets a tick every frame and one end call.
    /// </summary>
    public interface IInteractionFocus
    {
        /// <summary>Verb shown while in focus; null or empty shows no prompt.</summary>
        string FocusVerb { get; }

        /// <summary>Noun shown while in focus.</summary>
        string FocusNoun { get; }

        /// <summary>Every frame while in focus, after the Interactor has read its input.</summary>
        void Tick(Interactor by, float dt);

        /// <summary>Focus is over, whoever ended it (the focus itself, a respawn or a snap). Restore everything here.</summary>
        void End(Interactor by);
    }
}
