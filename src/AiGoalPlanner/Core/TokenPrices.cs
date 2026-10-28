using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What one Provider charges per million tokens: input and output separately, because they
    /// differ. Either may be missing, when the Player left that field empty — and with either
    /// missing there is no estimate to show.
    /// </summary>
    public sealed class TokenPrice
    {
        public TokenPrice(decimal? inputPerMillion, decimal? outputPerMillion) {
            if (inputPerMillion.HasValue && inputPerMillion.Value < 0) {
                throw new ArgumentOutOfRangeException(nameof(inputPerMillion));
            }

            if (outputPerMillion.HasValue && outputPerMillion.Value < 0) {
                throw new ArgumentOutOfRangeException(nameof(outputPerMillion));
            }

            InputPerMillion  = inputPerMillion;
            OutputPerMillion = outputPerMillion;
        }

        public decimal? InputPerMillion { get; }

        public decimal? OutputPerMillion { get; }

        /// <summary>
        /// Whether the Player entered nothing for this Provider: with nothing entered the Chat
        /// shows no cost.
        /// </summary>
        public bool IsEmpty => !InputPerMillion.HasValue && !OutputPerMillion.HasValue;

        /// <summary>
        /// Reads what the Player typed as the prices per million tokens for one Provider: both
        /// numbers, or either field left empty. Only the invariant culture is read, the same one
        /// the settings view writes, so a decimal separator is never taken for a thousands
        /// separator whichever culture the Player's machine runs.
        /// </summary>
        public static bool TryParse(string inputText, string outputText, out TokenPrice price) {
            if (!TryParseSingle(inputText, out decimal? input) || !TryParseSingle(outputText, out decimal? output)) {
                price = null;

                return false;
            }

            price = new TokenPrice(input, output);

            return true;
        }

        private static bool TryParseSingle(string text, out decimal? price) {
            string trimmed = (text ?? string.Empty).Trim();

            if (trimmed.Length == 0) {
                price = null;

                return true;
            }

            if (decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal parsed)
             && parsed >= 0) {
                price = parsed;

                return true;
            }

            price = null;

            return false;
        }
    }

    /// <summary>
    /// Remembers the price per million tokens for each endpoint, so the Chat can show an
    /// estimated cost for the Provider in use. Prices differ by Provider, which is why tokens
    /// stay the main unit (ADR-0002) and the cost is only ever an estimate.
    /// </summary>
    public sealed class TokenPrices
    {
        private readonly object _lock = new object();

        private readonly Dictionary<string, TokenPrice> _pricesByEndpoint =
            new Dictionary<string, TokenPrice>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Keeps <paramref name="price"/> for <paramref name="endpoint"/>. A price with nothing in
        /// it, or no price at all, forgets the endpoint instead, so an emptied field stops the
        /// estimate rather than freezing a stale one. Safe to call from any thread: the Chat reads
        /// prices off the game thread while the settings view writes them on it.
        /// </summary>
        public void SetPrice(string endpoint, TokenPrice price) {
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            string key = ProviderEndpointSuggestions.Normalize(endpoint);

            lock (_lock) {
                if (price == null || price.IsEmpty) {
                    _pricesByEndpoint.Remove(key);
                } else {
                    _pricesByEndpoint[key] = price;
                }
            }
        }

        /// <summary>
        /// The prices remembered for <paramref name="endpoint"/>, or null when the Player entered
        /// none for it.
        /// </summary>
        public TokenPrice GetPrice(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint)) return null;

            lock (_lock) {
                return _pricesByEndpoint.TryGetValue(ProviderEndpointSuggestions.Normalize(endpoint), out TokenPrice price)
                           ? price
                           : null;
            }
        }

        public bool Remove(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint)) return false;

            lock (_lock) {
                return _pricesByEndpoint.Remove(ProviderEndpointSuggestions.Normalize(endpoint));
            }
        }

        public string ToJson() {
            var json = new JObject();

            lock (_lock) {
                foreach (KeyValuePair<string, TokenPrice> entry in _pricesByEndpoint) {
                    var price = new JObject();

                    if (entry.Value.InputPerMillion.HasValue) price["input"] = entry.Value.InputPerMillion.Value;
                    if (entry.Value.OutputPerMillion.HasValue) price["output"] = entry.Value.OutputPerMillion.Value;

                    json[entry.Key] = price;
                }
            }

            return json.ToString(Formatting.None);
        }

        public static TokenPrices FromJson(string json) {
            var prices = new TokenPrices();

            if (string.IsNullOrWhiteSpace(json)) return prices;

            try {
                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, JObject>>(json);

                if (dictionary != null) {
                    foreach (KeyValuePair<string, JObject> entry in dictionary) {
                        prices.SetPrice(entry.Key, ReadPrice(entry.Value));
                    }
                }
            } catch (Exception) {
                // Malformed settings are treated as empty rather than crashing module load.
            }

            return prices;
        }

        private static TokenPrice ReadPrice(JObject json) {
            if (json == null) return null;

            return new TokenPrice(ReadDecimal(json, "input"), ReadDecimal(json, "output"));
        }

        private static decimal? ReadDecimal(JObject json, string name) {
            JToken token = json[name];

            if (token == null || token.Type == JTokenType.Null) return null;

            try {
                decimal value = token.Value<decimal>();

                return value < 0 ? (decimal?)null : value;
            } catch (Exception) {
                return null;
            }
        }
    }
}
