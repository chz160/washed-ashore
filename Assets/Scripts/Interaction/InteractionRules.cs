namespace WashedAshore.Interaction
{
    /// <summary>Pure rules of the interaction system (spec I1, tower-design-spec.md §3), covered by EditMode tests.</summary>
    public static class InteractionRules
    {
        /// <summary>The one prompt line, "{key}{separator}{Verb noun}". Null when there is nothing to show.</summary>
        public static string PromptLine(string key, string separator, string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            return string.IsNullOrEmpty(key) ? text : key + separator + text;
        }

        /// <summary>"Verb noun" with one space; just the verb when there is no noun.</summary>
        public static string VerbNoun(string verb, string noun)
        {
            if (string.IsNullOrEmpty(verb)) return null;
            return string.IsNullOrEmpty(noun) ? verb : verb + " " + noun;
        }

        /// <summary>The aim ray's length: the tuned range, never above the cap.</summary>
        public static float Range(float tunedRange, float cap) => tunedRange < cap ? tunedRange : cap;

        /// <summary>
        /// The key text for a binding's display string (§3.2: E, A, Cross, and South for any other pad). A pad the
        /// Input System can't name shows as <paramref name="unnamedSouthDisplay"/>, which becomes <paramref name="genericPadKey"/>.
        /// </summary>
        public static string KeyLabel(string bindingDisplay, string unnamedSouthDisplay, string genericPadKey)
        {
            if (string.IsNullOrEmpty(bindingDisplay)) return genericPadKey;
            return bindingDisplay == unnamedSouthDisplay ? genericPadKey : bindingDisplay;
        }

        /// <summary>
        /// The shared button (§3.3, td-note §3.3): a gamepad South press while a prompt shows interacts and does not
        /// jump. Space is a different control and always jumps.
        /// </summary>
        public static bool JumpBlocked(bool jumpPressFromGamepad, bool interactTookPressThisFrame)
        {
            return jumpPressFromGamepad && interactTookPressThisFrame;
        }

        /// <summary>Font size in pixels that gives a cap height of <paramref name="capFraction"/> of the screen height.</summary>
        public static int FontSize(int screenHeight, float capFraction, float capToEm)
        {
            if (capToEm <= 0f) return 0;
            float size = screenHeight * capFraction / capToEm;
            return size < 1f ? 1 : (int)(size + 0.5f);
        }
    }
}
