using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Remembers which Provider/model combinations pass or fail the tool-calling capability test,
    /// so the test runs only on first use and refused models are rejected immediately on subsequent
    /// requests.
    /// </summary>
    public sealed class ToolCallingCapabilityStore
    {
        private const string FileName = "tool-capabilities.json";

        private readonly string                     _filePath;
        private readonly object                     _lock = new object();
        private readonly Dictionary<string, bool>   _capabilities =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public ToolCallingCapabilityStore(string directory = null) {
            if (!string.IsNullOrWhiteSpace(directory)) {
                _filePath = Path.Combine(directory, FileName);
                Load();
            }
        }

        public bool? GetCapability(string endpoint, string model) {
            string key = KeyFor(endpoint, model);
            if (key == null) return null;

            lock (_lock) {
                return _capabilities.TryGetValue(key, out bool capable) ? (bool?)capable : null;
            }
        }

        public void Record(string endpoint, string model, bool canCallTools) {
            string key = KeyFor(endpoint, model);
            if (key == null) return;

            lock (_lock) {
                _capabilities[key] = canCallTools;
                Save();
            }
        }

        public void Clear(string endpoint, string model) {
            string key = KeyFor(endpoint, model);
            if (key == null) return;

            lock (_lock) {
                if (_capabilities.Remove(key)) {
                    Save();
                }
            }
        }

        private static string KeyFor(string endpoint, string model) {
            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model)) return null;

            return ProviderEndpointSuggestions.Normalize(endpoint).ToLowerInvariant() + "|" + model.Trim().ToLowerInvariant();
        }

        private void Load() {
            if (_filePath == null || !File.Exists(_filePath)) return;

            try {
                string json = File.ReadAllText(_filePath);
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);

                if (loaded != null) {
                    foreach (KeyValuePair<string, bool> entry in loaded) {
                        _capabilities[entry.Key] = entry.Value;
                    }
                }
            } catch (Exception) {
                // Ignore corrupted file and start with clean state
            }
        }

        private void Save() {
            if (_filePath == null) return;

            try {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory)) {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonConvert.SerializeObject(_capabilities, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            } catch (Exception) {
                // If writing fails, in-memory state is still retained
            }
        }
    }
}
