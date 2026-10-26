namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What the Provider settings may say about the API key, and what a pasted clipboard entry
    /// gives them.
    /// </summary>
    /// <remarks>
    /// Blish HUD 1.3.0 has no masked text box, so a key is never typed into a field where it would
    /// be drawn on screen: the Player copies it and pastes it from the clipboard. This is the only
    /// place that looks at key text before it reaches <see cref="ProviderKeyStore"/>, it never logs
    /// anything, and <see cref="Describe"/> never gives back more than the last four characters.
    /// </remarks>
    public static class ProviderKeySummary
    {
        /// <summary>
        /// What the settings view shows when no key is stored for the endpoint.
        /// </summary>
        public const string NoKey = "No key";

        /// <summary>
        /// How many characters of a stored key the settings view may show, so the Player can tell
        /// which key is saved.
        /// </summary>
        private const int ShownCharacters = 4;

        /// <summary>
        /// The key the clipboard carried, or null when it carried nothing to save. An empty or
        /// whitespace-only clipboard is not a key, so nothing is stored.
        /// </summary>
        public static string FromClipboard(string clipboardText) {
            if (clipboardText == null) return null;

            string key = clipboardText.Trim();

            return key.Length == 0 ? null : key;
        }

        /// <summary>
        /// The one line the settings view may show about the stored key: that there is none, or
        /// that there is one and how it ends. It never returns the key itself.
        /// </summary>
        public static string Describe(string key) {
            if (string.IsNullOrEmpty(key)) return NoKey;

            // A real API key is far longer than four characters, so this is a hint, not the key.
            string lastCharacters = key.Length <= ShownCharacters
                                        ? key
                                        : key.Substring(key.Length - ShownCharacters);

            return "Key saved (..." + lastCharacters + ")";
        }
    }
}
