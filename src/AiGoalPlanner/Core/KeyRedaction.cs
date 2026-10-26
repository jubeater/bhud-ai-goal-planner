namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Redacts sensitive API keys from text before logging, ensuring secrets never reach the log.
    /// </summary>
    public static class KeyRedaction
    {
        public const string Redacted = "[redacted]";

        /// <summary>
        /// Replaces every occurrence of <paramref name="apiKey"/> in <paramref name="text"/> with
        /// <c>[redacted]</c>. When <paramref name="apiKey"/> is null, empty or whitespace-only,
        /// returns <paramref name="text"/> unchanged.
        /// </summary>
        public static string Redact(string text, string apiKey) {
            if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(apiKey)) {
                return text;
            }

            return text.Replace(apiKey, Redacted);
        }
    }
}

