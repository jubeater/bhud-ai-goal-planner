using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    public class ProbeLoggingChatClientTests
    {
        private const string Endpoint = "https://api.deepseek.com/v1";
        private const string Model    = "deepseek-flash";

        [Fact]
        public async Task A_stub_Provider_whose_400_body_echoes_the_key_does_not_leak_the_key_into_the_log() {
            const string apiKey = "sk-super-secret-key-xyz123";
            string errorBody = "{\"error\":{\"message\":\"Invalid key: " + apiKey + " is expired or malformed\"}}";

            var httpHandler = new StubHttpMessageHandler(HttpStatusCode.BadRequest, errorBody);
            var httpClient = new HttpClient(httpHandler);

            var settings = ProviderSettings.Parse(Endpoint, Model, apiKey);
            var openAiClient = new OpenAiCompatibleChatClient(settings, httpClient);

            var store = new ToolCallingCapabilityStore();
            var toolCallingClient = new ToolCallingChatClient(openAiClient, Endpoint, Model, store);

            var loggedMessages = new List<string>();
            var loggingClient = new ProbeLoggingChatClient(toolCallingClient, msg => loggedMessages.Add(msg), apiKey);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => loggingClient.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hello") }));

            Assert.Equal(ProviderFailure.ToolCallingNotSupported, exception.Failure);

            string logged = Assert.Single(loggedMessages);
            Assert.DoesNotContain(apiKey, logged);
            Assert.Contains("[redacted]", logged);
            Assert.Contains("Provider refused tool-calling probe", logged);
        }

        [Fact]
        public async Task When_no_key_is_configured_refusal_is_still_logged() {
            var inner = new ScriptedChatClient();
            inner.WillReply("I cannot call tools.");

            var store = new ToolCallingCapabilityStore();
            var toolCallingClient = new ToolCallingChatClient(inner, Endpoint, Model, store);

            var loggedMessages = new List<string>();
            var loggingClient = new ProbeLoggingChatClient(toolCallingClient, msg => loggedMessages.Add(msg), apiKey: string.Empty);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => loggingClient.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hello") }));

            Assert.Equal(ProviderFailure.ToolCallingNotSupported, exception.Failure);

            string logged = Assert.Single(loggedMessages);
            Assert.Contains("I cannot call tools.", logged);
        }

        [Fact]
        public async Task Successful_probe_does_not_log_warnings() {
            var inner = new ScriptedChatClient();
            inner.WillCallTool("test_tool");
            inner.WillReply("Real response.");

            var store = new ToolCallingCapabilityStore();
            var toolCallingClient = new ToolCallingChatClient(inner, Endpoint, Model, store);

            var loggedMessages = new List<string>();
            var loggingClient = new ProbeLoggingChatClient(toolCallingClient, msg => loggedMessages.Add(msg), "sk-some-key");

            ChatResponse response = await loggingClient.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "Hello") });

            Assert.Equal("Real response.", response.Text);
            Assert.Empty(loggedMessages);
        }
    }
}

