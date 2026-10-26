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
    /// resembles a secret word stays visible, while anything that reads as a key, token, password,
    /// credential or auth value is hidden even when it turns out to be harmless. A secret that
    /// reaches the model cannot be taken back.
    /// </remarks>
    public static class SecretFilter
    {
        private static readonly HashSet<string> SecretWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "key", "keys", "token", "tokens", "secret", "secrets",
            "password", "passwords", "passwd", "pwd",
            "credential", "credentials", "auth"
        };

        private static readonly HashSet<string> SecretNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "apikey", "apitoken", "accesstoken", "secretkey", "clientsecret",
            "authtoken", "authkey", "privatekey", "passkey", "oauth"
        };

        public static bool LooksLikeSecret(string key, string displayName) {
            return LooksLikeSecret(key) || LooksLikeSecret(displayName);
        }

        private static bool LooksLikeSecret(string name) {
            if (string.IsNullOrWhiteSpace(name)) return false;

            List<string> words = SplitWords(name);

            foreach (string word in words) {
                if (SecretWords.Contains(word)) return true;
            }

            // api_key and ApiKey split into words that match, but APIKey or apikey2 come out as one
            // word each, so the name joined back together is checked as well. Only the letter words
            // are joined: the digits mark a numbered secret, not a different word. The joined name
            // only has to contain a secret name, not be one: myapikey and userauthtoken carry their
            // secret in the middle.
            string joinedLetters = string.Concat(words.Where(word => !char.IsDigit(word[0])));

            foreach (string secretName in SecretNames) {
                if (joinedLetters.IndexOf(secretName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
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
