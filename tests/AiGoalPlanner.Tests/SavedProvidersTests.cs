using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class SavedProvidersTests
    {
        [Fact]
        public void Remembering_a_model_for_an_endpoint_stores_and_retrieves_it() {
            var saved = new SavedProviders();
            saved.SetModel("https://ollama.com/v1", "beam:cloud");
            saved.SetModel("https://api.deepseek.com", "deepseek-flash");

            Assert.Equal("beam:cloud", saved.GetModel("https://ollama.com/v1"));
            Assert.Equal("deepseek-flash", saved.GetModel("https://api.deepseek.com"));
            Assert.Null(saved.GetModel("http://localhost:11434/v1"));
        }

        [Fact]
        public void Endpoint_lookup_ignores_trailing_slash_and_case() {
            var saved = new SavedProviders();
            saved.SetModel("https://api.deepseek.com/", "deepseek-flash");

            Assert.Equal("deepseek-flash", saved.GetModel("https://API.DeepSeek.com"));
        }

        [Fact]
        public void Round_trip_to_json_preserves_all_entries() {
            var saved = new SavedProviders();
            saved.SetModel("https://ollama.com/v1", "beam:cloud");
            saved.SetModel("http://localhost:11434/v1", "qwen3.5:9b");

            string json = saved.ToJson();
            var restored = SavedProviders.FromJson(json);

            Assert.Equal("beam:cloud", restored.GetModel("https://ollama.com/v1"));
            Assert.Equal("qwen3.5:9b", restored.GetModel("http://localhost:11434/v1"));
        }

        [Fact]
        public void FromJson_handles_null_empty_or_malformed_json_gracefully() {
            Assert.NotNull(SavedProviders.FromJson(null));
            Assert.NotNull(SavedProviders.FromJson(string.Empty));
            Assert.NotNull(SavedProviders.FromJson("not json"));
        }
    }
}
