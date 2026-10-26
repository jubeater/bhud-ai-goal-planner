using System.Linq;
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
    /// <summary>
    /// Tests the Provider client against a stand-in HTTP endpoint: what it sends, what it reads
    /// back, and what the Player is told when the Provider says no.
    /// </summary>
    public class OpenAiCompatibleChatClientTests
    {
        private const string ReplyBody =
            "{\"model\":\"deepseek-chat\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}]}";

        [Fact]
        public async Task A_reply_comes_back_as_the_models_text() {
            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal("Try the Pathing module.", response.Text);
            Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
            Assert.Equal("deepseek-chat", response.ModelId);
        }

        [Fact]
        public async Task The_request_goes_to_chat_completions_for_the_model_with_the_key_as_a_bearer_token() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            await client.GetResponseAsync(new[] { Request("Show only world bosses") });

            HttpRequestMessage request = Assert.Single(endpoint.Requests);

            Assert.Equal("https://api.deepseek.com/chat/completions", request.RequestUri.ToString());
            Assert.Equal("Bearer",     request.Headers.Authorization.Scheme);
            Assert.Equal("sk-the-key", request.Headers.Authorization.Parameter);

            string body = Assert.Single(endpoint.RequestBodies);
            Assert.Contains("\"model\":\"deepseek-chat\"", body);
            Assert.Contains("Show only world bosses",     body);
            Assert.Contains("\"role\":\"user\"",           body);
        }

        [Fact]
        public async Task A_local_Provider_is_asked_without_an_Authorization_header() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint, key: string.Empty);

            await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Null(Assert.Single(endpoint.Requests).Headers.Authorization);
        }

        [Fact]
        public async Task An_endpoint_that_already_names_chat_completions_is_not_extended_twice() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint, "https://ollama.com/v1/chat/completions");

            await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal("https://ollama.com/v1/chat/completions",
                         Assert.Single(endpoint.Requests).RequestUri.ToString());
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized,        ProviderFailure.InvalidKey)]
        [InlineData(HttpStatusCode.Forbidden,           ProviderFailure.InvalidKey)]
        [InlineData(HttpStatusCode.NotFound,            ProviderFailure.UnknownModel)]
        [InlineData((HttpStatusCode)429,                ProviderFailure.RateLimited)]
        [InlineData(HttpStatusCode.InternalServerError, ProviderFailure.Unexpected)]
        public async Task A_Provider_that_answers_with_an_error_is_reported_as_the_matching_failure(
            HttpStatusCode  statusCode,
            ProviderFailure expected) {
            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(statusCode, "{\"error\":\"nope\"}"));

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { Request("Hello") }));

            Assert.Equal(expected, exception.Failure);
        }

        [Fact]
        public async Task A_Provider_that_cannot_be_reached_is_reported_as_a_network_failure() {
            var endpoint = new StubHttpMessageHandler(request => throw new HttpRequestException("No such host is known."));
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { Request("Hello") }));

            Assert.Equal(ProviderFailure.Network, exception.Failure);
        }

        [Fact]
        public async Task A_reply_the_client_cannot_read_is_reported_as_unexpected() {
            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, "not json at all"));

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { Request("Hello") }));

            Assert.Equal(ProviderFailure.Unexpected, exception.Failure);
        }

        [Fact]
        public async Task A_reply_that_was_cut_off_keeps_its_finish_reason_so_the_Chat_can_explain_it() {
            const string cutOffBody =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"The reply was cut\"},\"finish_reason\":\"length\"}]}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, cutOffBody));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Tell me everything") });

            Assert.Equal(ChatFinishReason.Length, response.FinishReason);
        }

        [Fact]
        public async Task The_identity_message_reaches_the_Provider_as_a_system_message() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            await client.GetResponseAsync(new[] {
                new ChatMessage(ChatRole.System, "You are AI Goal Planner."),
                Request("Hello")
            });

            string body = Assert.Single(endpoint.RequestBodies);

            Assert.Contains("\"role\":\"system\"",       body);
            Assert.Contains("You are AI Goal Planner.",  body);
        }

        private static OpenAiCompatibleChatClient ClientFor(StubHttpMessageHandler endpoint,
                                                            string               url = "https://api.deepseek.com",
                                                            string               key = "sk-the-key") {
            return new OpenAiCompatibleChatClient(
                ProviderSettings.Parse(url, "deepseek-chat", key),
                new HttpClient(endpoint));
        }

        private static ChatMessage Request(string text) => new ChatMessage(ChatRole.User, text);
    }
}

