using System;
using System.IO;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

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

        [Fact]
        public async Task ForgetFailure_clears_stored_failure_so_the_next_request_probes_again() {
            var store = new ToolCallingCapabilityStore(_directory);
            store.Record("https://ollama.com/v1", "beam:cloud", canCallTools: false);

            store.ForgetFailure("https://ollama.com/v1", "beam:cloud");

            var inner = new ScriptedChatClient();
            inner.WillCallTool("test_tool");
            inner.WillReply("Now it works.");
            var client = new ToolCallingChatClient(inner, "https://ollama.com/v1", "beam:cloud", store);

            ChatResponse response = await client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") });

            Assert.Equal("Now it works.", response.Text);
            Assert.Equal(2, inner.Requests.Count); // Probe request + actual request
        }

        [Fact]
        public async Task ForgetFailure_leaves_a_recorded_true_intact_so_the_next_request_does_not_probe() {
            var store = new ToolCallingCapabilityStore(_directory);
            store.Record("https://ollama.com/v1", "beam:cloud", canCallTools: true);

            store.ForgetFailure("https://ollama.com/v1", "beam:cloud");

            var inner = new ScriptedChatClient();
            inner.WillReply("Direct answer.");
            var client = new ToolCallingChatClient(inner, "https://ollama.com/v1", "beam:cloud", store);

            ChatResponse response = await client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") });

            Assert.Equal("Direct answer.", response.Text);
            Assert.Single(inner.Requests); // Only the actual request, no probe
        }

        [Fact]
        public void Dropping_capability_records_for_an_endpoint_removes_all_its_stored_models() {
            var store = new ToolCallingCapabilityStore(_directory);
            store.Record("https://my-proxy.example/v1", "model-a", canCallTools: true);
            store.Record("https://my-proxy.example/v1", "model-b", canCallTools: false);
            store.Record("https://other-provider.example/v1", "model-c", canCallTools: true);

            store.Clear("https://my-proxy.example/v1/");

            Assert.Null(store.GetCapability("https://my-proxy.example/v1", "model-a"));
            Assert.Null(store.GetCapability("https://my-proxy.example/v1", "model-b"));
            Assert.True(store.GetCapability("https://other-provider.example/v1", "model-c"));

            var second = new ToolCallingCapabilityStore(_directory);
            Assert.Null(second.GetCapability("https://my-proxy.example/v1", "model-a"));
            Assert.Null(second.GetCapability("https://my-proxy.example/v1", "model-b"));
            Assert.True(second.GetCapability("https://other-provider.example/v1", "model-c"));
        }
    }
}
