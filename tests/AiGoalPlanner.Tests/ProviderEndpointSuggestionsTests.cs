using System.Linq;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests ready-made endpoint suggestions for common Providers so Players don't have to look
    /// up URLs, while still being able to enter any custom URL.
    /// </summary>
    public class ProviderEndpointSuggestionsTests
    {
        [Fact]
        public void All_common_Providers_from_the_spec_are_suggested() {
            var names = ProviderEndpointSuggestions.All.Select(s => s.Name).ToList();

            Assert.Contains("Ollama Cloud", names);
            Assert.Contains("Local Ollama", names);
            Assert.Contains("LM Studio", names);
            Assert.Contains("DeepSeek", names);
            Assert.Contains("OpenAI", names);
            Assert.Contains("OpenRouter", names);
            Assert.Contains("Claude", names);
        }

        [Theory]
        [InlineData("Ollama Cloud", "https://ollama.com/v1")]
        [InlineData("Local Ollama", "http://localhost:11434/v1")]
        [InlineData("LM Studio",    "http://localhost:1234/v1")]
        [InlineData("DeepSeek",     "https://api.deepseek.com")]
        [InlineData("OpenAI",       "https://api.openai.com/v1")]
        [InlineData("OpenRouter",   "https://openrouter.ai/api/v1")]
        [InlineData("Claude",       "https://api.anthropic.com/v1")]
        public void Each_suggestion_has_the_expected_endpoint_url(string name, string expectedEndpoint) {
            ProviderEndpointSuggestion suggestion = ProviderEndpointSuggestions.All.Single(s => s.Name == name);

            Assert.Equal(expectedEndpoint, suggestion.Endpoint);
        }

        [Theory]
        [InlineData("https://ollama.com/v1",       "Ollama Cloud")]
        [InlineData("https://ollama.com/v1/",      "Ollama Cloud")]
        [InlineData("http://localhost:11434/v1",   "Local Ollama")]
        [InlineData("http://localhost:1234/v1/",   "LM Studio")]
        [InlineData("https://api.deepseek.com",    "DeepSeek")]
        [InlineData("https://api.openai.com/v1",   "OpenAI")]
        [InlineData("https://openrouter.ai/api/v1","OpenRouter")]
        [InlineData("https://api.anthropic.com/v1","Claude")]
        public void Matching_an_endpoint_ignores_trailing_slash_and_case(string endpoint, string expectedName) {
            ProviderEndpointSuggestion match = ProviderEndpointSuggestions.Match(endpoint);

            Assert.NotNull(match);
            Assert.Equal(expectedName, match.Name);
        }

        [Fact]
        public void Finding_a_suggestion_by_name_is_case_insensitive() {
            ProviderEndpointSuggestion match = ProviderEndpointSuggestions.FindByName("ollama cloud");

            Assert.NotNull(match);
            Assert.Equal("https://ollama.com/v1", match.Endpoint);
        }

        [Fact]
        public void An_unknown_endpoint_returns_null_match() {
            Assert.Null(ProviderEndpointSuggestions.Match("https://my-custom-llm.example/v1"));
            Assert.Null(ProviderEndpointSuggestions.Match(string.Empty));
            Assert.Null(ProviderEndpointSuggestions.Match(null));
        }

        [Fact]
        public void An_unknown_name_returns_null() {
            Assert.Null(ProviderEndpointSuggestions.FindByName("Custom"));
            Assert.Null(ProviderEndpointSuggestions.FindByName(null));
        }
    }
}
