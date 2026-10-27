using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Recognizes a Setting that looks like it holds a secret, from its key or its display name.
    /// </summary>
    /// <remarks>
    /// Detection is by name only, and it is deliberately one-sided: a Setting whose name only
    /// resembles a secret word stays visible, while anything that carries a secret word is hidden
    /// even when it turns out to be harmless. A secret that reaches the model cannot be taken back.
    ///
    /// The rule is two-tier, because one tier cannot be both sensitive and honest:
    ///
    /// Strong words hide a name when they appear anywhere in its joined letters, ignoring case — a
    /// name that carries one carries a secret however it is decorated ("refreshtoken",
    /// "userpassword", "myapikey"), and a plural is just the word with something attached
    /// ("tokens"). A name does not have to equal the word to be about one.
    ///
    /// Weak words hide a name only when they are a whole word after splitting, digit suffixes
    /// included ("Key2" hides, "Hotkey" does not). They are weak because harmless names contain
    /// them inside another word: hotkey, keybind, author.
    /// </remarks>
    public static class SecretFilter
    {
        private static readonly string[] StrongWords = {
            "token", "secret", "password", "passwd", "credential",
            "apikey", "privatekey", "passkey", "authkey", "oauth", "bearer"
        };

        private static readonly string[] WeakWords = { "key", "keys", "auth", "pwd" };

        public static bool LooksLikeSecret(string key, string displayName) {
            return LooksLikeSecret(key) || LooksLikeSecret(displayName);
        }

        private static bool LooksLikeSecret(string name) {
            if (string.IsNullOrWhiteSpace(name)) return false;

            List<string> words = SplitWords(name);

            // Only the letter words are joined: the digits mark a numbered secret, not a different
            // word, so API_KEY2 is judged as APIKEY.
            string joinedLetters = string.Concat(words.Where(word => !char.IsDigit(word[0])));

            foreach (string strong in StrongWords) {
                if (joinedLetters.IndexOf(strong, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            foreach (string weak in WeakWords) {
                foreach (string word in words) {
                    if (string.Equals(word, weak, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Splits a Setting name into its words: separators of any kind, the boundaries inside
        /// camelCase names, the boundary where an uppercase run meets the capitalized word that
        /// follows it (APIKey becomes [API, Key]), and the boundaries between letters and digits.
        /// "API_KEY2" comes out as [API, KEY, 2], so a numbered key is still seen as a key.
        /// </summary>
        private static List<string> SplitWords(string name) {
            string withBoundaries = Regex.Replace(name.Trim(),
                                                  "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
                                                  " ");

            var words = new List<string>();

            foreach (Match match in Regex.Matches(withBoundaries, "[A-Za-z]+|[0-9]+")) {
                words.Add(match.Value);
            }

            return words;
        }
    }
}
