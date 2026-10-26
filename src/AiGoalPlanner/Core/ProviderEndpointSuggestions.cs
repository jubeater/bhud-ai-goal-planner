using System;
using System.Collections.Generic;
using System.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// One ready-made endpoint suggestion for a common Provider.
    /// </summary>
    public sealed class ProviderEndpointSuggestion
    {
        public ProviderEndpointSuggestion(string name, string endpoint) {
            Name     = name     ?? throw new ArgumentNullException(nameof(name));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public string Name { get; }

        public string Endpoint { get; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Ready-made endpoint suggestions so Players don't have to look up URLs.
    /// </summary>
    public static class ProviderEndpointSuggestions
    {
        public static readonly IReadOnlyList<ProviderEndpointSuggestion> All = new[] {
            new ProviderEndpointSuggestion("Ollama Cloud", "https://ollama.com/v1"),
            new ProviderEndpointSuggestion("Local Ollama", "http://localhost:11434/v1"),
            new ProviderEndpointSuggestion("LM Studio",    "http://localhost:1234/v1"),
            new ProviderEndpointSuggestion("DeepSeek",     "https://api.deepseek.com"),
            new ProviderEndpointSuggestion("OpenAI",       "https://api.openai.com/v1"),
            new ProviderEndpointSuggestion("OpenRouter",   "https://openrouter.ai/api/v1"),
            new ProviderEndpointSuggestion("Claude",       "https://api.anthropic.com/v1")
        };

        /// <summary>
        /// Finds a suggestion matching <paramref name="endpoint"/>, ignoring case, surrounding
        /// whitespace and a trailing slash, or null if none match.
        /// </summary>
        public static ProviderEndpointSuggestion Match(string endpoint) {
            if (string.IsNullOrWhiteSpace(endpoint)) return null;

            string normalized = Normalize(endpoint);

            return All.FirstOrDefault(suggestion => Normalize(suggestion.Endpoint).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Finds a suggestion by its display name, or null if none match.
        /// </summary>
        public static ProviderEndpointSuggestion FindByName(string name) {
            if (string.IsNullOrWhiteSpace(name)) return null;

            return All.FirstOrDefault(suggestion => suggestion.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        internal static string Normalize(string url) {
            return (url ?? string.Empty).Trim().TrimEnd('/');
        }
    }
}
