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
            // are joined: the digits mark a numbered secret, not a different word.
            return SecretNames.Contains(string.Concat(words.Where(word => !char.IsDigit(word[0]))));
        }

        /// <summary>
        /// Splits a Setting name into its words: separators of any kind, the boundaries inside
        /// camelCase names, and the boundaries between letters and digits. "api_key", "ApiKey" and
        /// "api key" all come out as [api, key]; "API_KEY2" comes out as [API, KEY, 2], so a
        /// numbered key is still seen as a key.
        /// </summary>
        private static List<string> SplitWords(string name) {
            string withCamelBoundaries = Regex.Replace(name.Trim(), "(?<=[a-z0-9])(?=[A-Z])", " ");

            var words = new List<string>();

            foreach (Match match in Regex.Matches(withCamelBoundaries, "[A-Za-z]+|[0-9]+")) {
                words.Add(match.Value);
            }

            return words;
        }
    }
}
