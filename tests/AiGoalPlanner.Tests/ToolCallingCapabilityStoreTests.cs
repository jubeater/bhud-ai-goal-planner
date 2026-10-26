using System;
using System.IO;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class ToolCallingCapabilityStoreTests : IDisposable
    {
        private readonly string _directory =
            Path.Combine(Path.GetTempPath(), "aigoalplanner-capability-tests-" + Guid.NewGuid().ToString("n"));

        public void Dispose() {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Fact]
        public void An_untested_model_returns_null() {
            var store = new ToolCallingCapabilityStore(_directory);

            Assert.Null(store.GetCapability("https://ollama.com/v1", "beam:cloud"));
        }

        [Fact]
        public void A_recorded_pass_is_remembered() {
            var store = new ToolCallingCapabilityStore(_directory);

            store.Record("https://ollama.com/v1", "beam:cloud", canCallTools: true);

            Assert.True(store.GetCapability("https://ollama.com/v1", "beam:cloud"));
        }

        [Fact]
        public void A_recorded_failure_is_remembered() {
            var store = new ToolCallingCapabilityStore(_directory);

            store.Record("https://api.deepseek.com", "deepseek-flash", canCallTools: false);

            Assert.False(store.GetCapability("https://api.deepseek.com", "deepseek-flash"));
        }

        [Fact]
        public void Looking_up_capability_ignores_case_trailing_slash_and_whitespace() {
            var store = new ToolCallingCapabilityStore(_directory);

            store.Record("https://ollama.com/v1/", "beam:cloud", canCallTools: true);

            Assert.True(store.GetCapability("https://OLLAMA.com/v1", " BEAM:CLOUD "));
        }

        [Fact]
        public void Results_are_persisted_across_store_instances() {
            var first = new ToolCallingCapabilityStore(_directory);
            first.Record("https://ollama.com/v1", "beam:cloud", canCallTools: true);
            first.Record("http://localhost:11434/v1", "tiny-model", canCallTools: false);

            var second = new ToolCallingCapabilityStore(_directory);
            Assert.True(second.GetCapability("https://ollama.com/v1", "beam:cloud"));
            Assert.False(second.GetCapability("http://localhost:11434/v1", "tiny-model"));
        }

        [Fact]
        public void Clearing_capability_removes_the_stored_result() {
            var store = new ToolCallingCapabilityStore(_directory);
            store.Record("https://ollama.com/v1", "beam:cloud", canCallTools: false);
            Assert.False(store.GetCapability("https://ollama.com/v1", "beam:cloud"));

            store.Clear("https://ollama.com/v1", "beam:cloud");
            Assert.Null(store.GetCapability("https://ollama.com/v1", "beam:cloud"));
        }

        [Fact]
        public void Clearing_capability_persists_across_store_instances() {
            var first = new ToolCallingCapabilityStore(_directory);
            first.Record("https://ollama.com/v1", "beam:cloud", canCallTools: false);

            var second = new ToolCallingCapabilityStore(_directory);
            second.Clear("https://ollama.com/v1", "beam:cloud");

            var third = new ToolCallingCapabilityStore(_directory);
            Assert.Null(third.GetCapability("https://ollama.com/v1", "beam:cloud"));
        }
    }
}
