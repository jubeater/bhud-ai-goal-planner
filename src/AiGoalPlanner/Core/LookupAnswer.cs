namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What one Lookup found: the text the model reads, and the line the debug log may keep.
    /// </summary>
    /// <remarks>
    /// The detail is what the Lookup read in names and counts — a module's namespace, how many
    /// Settings came back — never a Setting's value. Keeping the two apart is what lets every
    /// Lookup be logged without the log becoming a copy of the Player's configuration.
    /// </remarks>
    public sealed class LookupAnswer
    {
        public LookupAnswer(string text, string logDetail = null) {
            Text      = text      ?? string.Empty;
            LogDetail = logDetail ?? string.Empty;
        }

        /// <summary>The answer, as the model reads it.</summary>
        public string Text { get; }

        /// <summary>
        /// What was read, in names and counts, for the log; empty when there is nothing worth
        /// saying about it.
        /// </summary>
        public string LogDetail { get; }
    }
}
