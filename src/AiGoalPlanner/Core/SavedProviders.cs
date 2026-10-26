using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Remembers the model configured for each endpoint so the Player can switch between several
    /// Providers without losing the model name they typed.
    /// </summary>
    public sealed class SavedProviders
    {
        private readonly Dictionary<string, string> _modelsByEndpoint =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public void SetModel(string endpoint, string model) {
            if (string.IsNullOrWhiteSpace(endpoint)) return;

            string key = ProviderEndpointSuggestions.Normalize(endpoint);

            if (string.IsNullOrWhiteSpace(model)) {
                _modelsByEndpoint.Remove(key);
            } else {
                _modelsByEndpoint[key] = model.Trim();
            }
        }

        public string GetModel(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint)) return null;

            return _modelsByEndpoint.TryGetValue(ProviderEndpointSuggestions.Normalize(endpoint), out string model) ? model : null;
        }

        public string ToJson() {
            return JsonConvert.SerializeObject(_modelsByEndpoint);
        }

        public static SavedProviders FromJson(string json) {
            var saved = new SavedProviders();

            if (string.IsNullOrWhiteSpace(json)) return saved;

            try {
                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);

                if (dictionary != null) {
                    foreach (KeyValuePair<string, string> entry in dictionary) {
                        saved.SetModel(entry.Key, entry.Value);
                    }
                }
            } catch (Exception) {
                // Malformed settings are treated as empty rather than crashing module load.
            }

            return saved;
        }
    }
}
