using System.Globalization;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The per-Chat soft limit on tokens used (GLOSSARY): past it the Chat asks before continuing,
    /// and the Player either keeps going with another message or starts a New chat. Asking costs
    /// no tokens itself: the Chat shows the question, the model is not asked to ask it.
    /// </summary>
    public static class TokenBudget
    {
        /// <summary>
        /// What a Chat may use before the Chat asks: about 300k tokens.
        /// </summary>
        public const long Default = 300000;

        /// <summary>
        /// Whether <paramref name="used"/> has reached <paramref name="budget"/>. A budget of zero
        /// or less means no limit, so the Chat never asks.
        /// </summary>
        public static bool IsReached(long used, long budget) {
            return budget > 0 && used >= budget;
        }

        /// <summary>
        /// Reads what the Player typed as a Token Budget: a whole number of tokens, or 0 for no
        /// limit. Only the invariant culture is read, the same one the settings view writes, so a
        /// decimal separator is never taken for a thousands separator whichever culture the
        /// Player's machine runs.
        /// </summary>
        public static bool TryParse(string text, out long budget) {
            string trimmed = (text ?? string.Empty).Trim();

            return long.TryParse(trimmed, NumberStyles.Integer | NumberStyles.AllowThousands,
                                 CultureInfo.InvariantCulture, out budget)
                && budget >= 0;
        }

        /// <summary>
        /// The question the Chat shows once per Chat when the budget is reached. It names exact
        /// counts, so the Player sees how far past the budget this Chat has gone.
        /// </summary>
        public static string PromptFor(long used, long budget) {
            return "Token budget reached: this Chat has used " + Format(used) + " of " + Format(budget)
                 + " tokens. Send another message to keep going, or start a New chat.";
        }

        private static string Format(long count) {
            return count.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
