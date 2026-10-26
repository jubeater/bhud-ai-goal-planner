using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    public class ToolCallingChatClientTests
    {
        private const string Endpoint = "https://ollama.com/v1";
        private const string Model    = "beam:cloud";

        [Fact]
        public async Task On_first_use_a_tool_calling_probe_runs_and_passes_when_the_model_calls_the_tool() {
            var inner = new ScriptedChatClient();
            inner.WillCallTool("test_tool");
            inner.WillReply("I can help with hero points.");

            var store = new ToolCallingCapabilityStore();
            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);

            ChatResponse response = await client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") });

            Assert.Equal("I can help with hero points.", response.Text);
            Assert.Equal(2, inner.Requests.Count);

            // The probe offered exactly one tool and required it
            ChatOptions probeOptions = inner.RequestOptions[0];
            Assert.NotNull(probeOptions);
            Assert.NotNull(probeOptions.Tools);
            Assert.Single(probeOptions.Tools);
            Assert.IsType<RequiredChatToolMode>(probeOptions.ToolMode);

            // Capability is remembered as true
            Assert.True(store.GetCapability(Endpoint, Model));
        }

        [Fact]
        public async Task Subsequent_requests_do_not_run_the_probe_again_when_already_known_to_pass() {
            var inner = new ScriptedChatClient();
            inner.WillReply("Direct answer.");

            var store = new ToolCallingCapabilityStore();
            store.Record(Endpoint, Model, canCallTools: true);

            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);

            ChatResponse response = await client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") });

            Assert.Equal("Direct answer.", response.Text);
            Assert.Single(inner.Requests);
        }

        [Fact]
        public async Task A_model_that_fails_the_tool_calling_test_is_refused_and_recorded_as_failed() {
            var inner = new ScriptedChatClient();
            inner.WillReply("I cannot call tools.");

            var store = new ToolCallingCapabilityStore();
            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") }));

            Assert.Equal(ProviderFailure.ToolCallingNotSupported, exception.Failure);
            Assert.False(store.GetCapability(Endpoint, Model));
        }

        [Fact]
        public async Task A_model_already_known_to_fail_is_refused_immediately_without_calling_the_model() {
            var inner = new ScriptedChatClient();
            var store = new ToolCallingCapabilityStore();
            store.Record(Endpoint, Model, canCallTools: false);

            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") }));

            Assert.Equal(ProviderFailure.ToolCallingNotSupported, exception.Failure);
            Assert.Empty(inner.Requests);
        }

        [Fact]
        public async Task A_network_failure_during_probe_is_rethrown_and_does_not_poison_the_capability_store() {
            var inner = new ScriptedChatClient();
            inner.WillFail(ProviderFailure.Network);

            var store = new ToolCallingCapabilityStore();
            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") }));

            Assert.Equal(ProviderFailure.Network, exception.Failure);
            Assert.Null(store.GetCapability(Endpoint, Model));
        }

        [Fact]
        public async Task A_503_during_probe_leaves_capability_unknown_and_a_later_request_probes_again() {
            var inner = new ScriptedChatClient();
            inner.WillFail(ProviderFailure.Unexpected, HttpStatusCode.ServiceUnavailable);

            var store = new ToolCallingCapabilityStore();
            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") }));

            Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
            Assert.Null(store.GetCapability(Endpoint, Model));

            // Next request runs probe again
            inner.WillCallTool("test_tool");
            inner.WillReply("Second reply");

            ChatResponse response = await client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello again") });

            Assert.Equal("Second reply", response.Text);
            Assert.True(store.GetCapability(Endpoint, Model));
            Assert.Equal(3, inner.Requests.Count);
        }

        [Fact]
        public async Task A_400_during_probe_records_cannot_call_tools() {
            var inner = new ScriptedChatClient();
            inner.WillFail(ProviderFailure.Unexpected, HttpStatusCode.BadRequest);

            var store = new ToolCallingCapabilityStore();
            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") }));

            Assert.Equal(ProviderFailure.ToolCallingNotSupported, exception.Failure);
            Assert.False(store.GetCapability(Endpoint, Model));

            // Immediate rejection on next request without calling inner client
            ProviderException secondException = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") }));

            Assert.Equal(ProviderFailure.ToolCallingNotSupported, secondException.Failure);
            Assert.Single(inner.Requests);
        }

        [Fact]
        public async Task Clearing_stored_failure_on_save_allows_the_player_to_retry() {
            var inner = new ScriptedChatClient();
            var store = new ToolCallingCapabilityStore();
            store.Record(Endpoint, Model, canCallTools: false);

            // Saving settings clears failure
            if (store.GetCapability(Endpoint, Model) == false) {
                store.Clear(Endpoint, Model);
            }

            Assert.Null(store.GetCapability(Endpoint, Model));

            inner.WillCallTool("test_tool");
            inner.WillReply("Now it works.");

            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);
            ChatResponse response = await client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") });

            Assert.Equal("Now it works.", response.Text);
            Assert.True(store.GetCapability(Endpoint, Model));
        }

        [Fact]
        public async Task A_recorded_true_stays_intact_on_save_and_does_not_probe_again() {
            var inner = new ScriptedChatClient();
            inner.WillReply("Direct answer.");

            var store = new ToolCallingCapabilityStore();
            store.Record(Endpoint, Model, canCallTools: true);

            // Saving unchanged or tested settings does not clear a recorded true
            if (store.GetCapability(Endpoint, Model) == false) {
                store.Clear(Endpoint, Model);
            }

            Assert.True(store.GetCapability(Endpoint, Model));

            var client = new ToolCallingChatClient(inner, Endpoint, Model, store);
            ChatResponse response = await client.GetResponseAsync(new[] { new AiChatMessage(ChatRole.User, "Hello") });

            Assert.Equal("Direct answer.", response.Text);
            Assert.Single(inner.Requests); // Only the actual message, no probe
        }
    }
}
