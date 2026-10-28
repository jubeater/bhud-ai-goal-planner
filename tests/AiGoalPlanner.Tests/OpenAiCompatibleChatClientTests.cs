using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
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
            "{\"model\":\"deepseek-flash\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}]}";

        [Fact]
        public async Task A_reply_comes_back_as_the_models_text() {
            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal("Try the Pathing module.", response.Text);
            Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
            Assert.Equal("deepseek-flash", response.ModelId);
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
            Assert.Contains("\"model\":\"deepseek-flash\"", body);
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
            Assert.Equal(statusCode, exception.StatusCode);
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

        [Fact]
        public async Task Offered_tools_reach_the_Provider_as_functions_with_their_schema() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            await client.GetResponseAsync(new[] { Request("What modules do I have?") },
                                          new ChatOptions { Tools = new List<AITool> { ListModulesTool() } });

            JObject body = JObject.Parse(Assert.Single(endpoint.RequestBodies));
            JToken  tool = Assert.Single((JArray)body["tools"]);

            Assert.Equal("function",                  (string)tool["type"]);
            Assert.Equal("list_installed_modules",    (string)tool["function"]["name"]);
            Assert.Equal("Lists the Installed Modules.", (string)tool["function"]["description"]);
            Assert.Equal("object",                    (string)tool["function"]["parameters"]["type"]);
            Assert.Null(body["tool_choice"]);
        }

        [Fact]
        public async Task A_request_without_tools_sends_no_tools_field() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            await client.GetResponseAsync(new[] { Request("Hello") }, new ChatOptions());

            JObject body = JObject.Parse(Assert.Single(endpoint.RequestBodies));

            Assert.Null(body["tools"]);
            Assert.Null(body["tool_choice"]);
        }

        [Fact]
        public async Task Requiring_a_specific_tool_names_it_in_tool_choice() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            await client.GetResponseAsync(new[] { Request("Hello") }, new ChatOptions {
                Tools    = new List<AITool> { ListModulesTool() },
                ToolMode = ChatToolMode.RequireSpecific("list_installed_modules")
            });

            JToken toolChoice = JObject.Parse(Assert.Single(endpoint.RequestBodies))["tool_choice"];

            Assert.Equal("function",               (string)toolChoice["type"]);
            Assert.Equal("list_installed_modules", (string)toolChoice["function"]["name"]);
        }

        [Fact]
        public async Task Requiring_any_tool_sends_required() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            await client.GetResponseAsync(new[] { Request("Hello") }, new ChatOptions {
                Tools    = new List<AITool> { ListModulesTool() },
                ToolMode = ChatToolMode.RequireAny
            });

            Assert.Equal("required", (string)JObject.Parse(Assert.Single(endpoint.RequestBodies))["tool_choice"]);
        }

        [Fact]
        public async Task Tool_calls_in_the_reply_come_back_as_function_calls_with_their_arguments() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":["
              + "{\"id\":\"call_1\",\"type\":\"function\",\"function\":{\"name\":\"get_module_settings\",\"arguments\":\"{\\\"module\\\":\\\"Pathing\\\",\\\"limit\\\":3,\\\"tags\\\":[\\\"a\\\"]}\"}},"
              + "{\"id\":\"call_2\",\"type\":\"function\",\"function\":{\"name\":\"list_installed_modules\",\"arguments\":\"{}\"}}"
              + "]},\"finish_reason\":\"tool_calls\"}]}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal(ChatFinishReason.ToolCalls, response.FinishReason);

            List<FunctionCallContent> calls = response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToList();

            Assert.Equal(2, calls.Count);
            Assert.Equal("call_1",              calls[0].CallId);
            Assert.Equal("get_module_settings", calls[0].Name);
            Assert.Equal("Pathing",             calls[0].Arguments["module"]);
            Assert.Equal(3L,                    calls[0].Arguments["limit"]);
            Assert.Equal(new List<object> { "a" }, calls[0].Arguments["tags"]);
            Assert.Null(calls[0].Exception);
            Assert.Equal("list_installed_modules", calls[1].Name);
            Assert.Empty(calls[1].Arguments);
        }

        [Theory]
        [InlineData("\"{\\\"at\\\":\\\"2026-10-26T18:00:00+02:00\\\"}\"")]
        [InlineData("{\"at\":\"2026-10-26T18:00:00+02:00\"}")]
        public async Task An_argument_that_looks_like_a_date_stays_the_string_the_model_sent(string arguments) {
            string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"tool_calls\":["
              + "{\"id\":\"call_1\",\"function\":{\"name\":\"remind_me\",\"arguments\":" + arguments + "}}"
              + "]},\"finish_reason\":\"tool_calls\"}]}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            FunctionCallContent call = Assert.Single(response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());

            Assert.Equal("2026-10-26T18:00:00+02:00", call.Arguments["at"]);
        }

        [Fact]
        public async Task A_reply_that_looks_like_a_date_comes_back_unchanged() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"2026-10-26T18:00:00+02:00\"},\"finish_reason\":\"stop\"}]}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal("2026-10-26T18:00:00+02:00", response.Text);
        }

        [Fact]
        public async Task An_argument_that_looks_like_a_date_is_sent_back_unchanged() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            var arguments = new Dictionary<string, object> { ["at"] = "2026-10-26T18:00:00+02:00" };

            await client.GetResponseAsync(new[] {
                new ChatMessage(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("call_1", "remind_me", arguments) })
            });

            var messages = (JArray)JObject.Parse(Assert.Single(endpoint.RequestBodies))["messages"];

            Assert.Equal("{\"at\":\"2026-10-26T18:00:00+02:00\"}", (string)messages[0]["tool_calls"][0]["function"]["arguments"]);
        }

        [Fact]
        public async Task A_JsonElement_inside_a_tool_result_is_sent_as_its_JSON() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            var result = new Dictionary<string, object> {
                ["settings"] = JsonDocument.Parse("{\"enabled\":true}").RootElement,
                ["modules"]  = new List<object> { JsonDocument.Parse("\"Pathing\"").RootElement }
            };

            await client.GetResponseAsync(new[] {
                new ChatMessage(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("call_1", "get_module_settings") }),
                new ChatMessage(ChatRole.Tool,      new List<AIContent> { new FunctionResultContent("call_1", result) })
            });

            var messages = (JArray)JObject.Parse(Assert.Single(endpoint.RequestBodies))["messages"];

            Assert.Equal("{\"settings\":{\"enabled\":true},\"modules\":[\"Pathing\"]}", (string)messages[1]["content"]);
        }

        [Fact]
        public async Task Arguments_sent_as_an_object_instead_of_a_string_are_read_too() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"tool_calls\":["
              + "{\"id\":\"call_1\",\"function\":{\"name\":\"get_module_settings\",\"arguments\":{\"module\":\"Pathing\"}}}"
              + "]},\"finish_reason\":\"tool_calls\"}]}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            FunctionCallContent call = Assert.Single(response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());

            Assert.Equal("Pathing", call.Arguments["module"]);
        }

        [Fact]
        public async Task A_tool_call_with_arguments_that_are_not_JSON_keeps_its_id_and_carries_the_error() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"tool_calls\":["
              + "{\"function\":{\"name\":\"get_module_settings\",\"arguments\":\"{module: Pathing\"}}"
              + "]},\"finish_reason\":\"tool_calls\"}]}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            FunctionCallContent call = Assert.Single(response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>());

            Assert.Equal("call_0",              call.CallId);
            Assert.Equal("get_module_settings", call.Name);
            Assert.NotNull(call.Exception);
        }

        [Fact]
        public async Task Earlier_tool_calls_and_their_results_are_sent_back_in_the_Chat_Completions_shape() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            var arguments = new Dictionary<string, object> { ["module"] = "Pathing" };

            await client.GetResponseAsync(new[] {
                Request("What does Pathing have?"),
                new ChatMessage(ChatRole.Assistant, new List<AIContent> {
                    new FunctionCallContent("call_1", "get_module_settings", arguments),
                    new FunctionCallContent("call_2", "list_installed_modules")
                }),
                new ChatMessage(ChatRole.Tool, new List<AIContent> {
                    new FunctionResultContent("call_1", "{\"settings\":[]}"),
                    new FunctionResultContent("call_2", new { modules = new[] { "Pathing" } })
                })
            });

            var messages = (JArray)JObject.Parse(Assert.Single(endpoint.RequestBodies))["messages"];

            Assert.Equal(4, messages.Count);

            JToken assistant = messages[1];
            Assert.Equal("assistant", (string)assistant["role"]);
            Assert.Equal(JTokenType.Null, assistant["content"].Type);
            Assert.Equal("call_1",              (string)assistant["tool_calls"][0]["id"]);
            Assert.Equal("function",            (string)assistant["tool_calls"][0]["type"]);
            Assert.Equal("get_module_settings", (string)assistant["tool_calls"][0]["function"]["name"]);
            Assert.Equal("{\"module\":\"Pathing\"}", (string)assistant["tool_calls"][0]["function"]["arguments"]);
            Assert.Equal("{}",                  (string)assistant["tool_calls"][1]["function"]["arguments"]);

            Assert.Equal("tool",              (string)messages[2]["role"]);
            Assert.Equal("call_1",            (string)messages[2]["tool_call_id"]);
            Assert.Equal("{\"settings\":[]}", (string)messages[2]["content"]);

            Assert.Equal("tool",                       (string)messages[3]["role"]);
            Assert.Equal("call_2",                     (string)messages[3]["tool_call_id"]);
            Assert.Equal("{\"modules\":[\"Pathing\"]}", (string)messages[3]["content"]);
        }

        [Fact]
        public async Task A_tool_result_that_is_a_JsonElement_is_sent_as_its_JSON_text() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            JsonElement result = JsonDocument.Parse("{\"enabled\":true}").RootElement;

            await client.GetResponseAsync(new[] {
                new ChatMessage(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("call_1", "get_module_settings") }),
                new ChatMessage(ChatRole.Tool,      new List<AIContent> { new FunctionResultContent("call_1", result) })
            });

            var messages = (JArray)JObject.Parse(Assert.Single(endpoint.RequestBodies))["messages"];

            Assert.Equal("{\"enabled\":true}", (string)messages[1]["content"]);
        }

        [Fact]
        public async Task A_tool_that_failed_tells_the_model_so() {
            var endpoint = new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint);

            await client.GetResponseAsync(new[] {
                new ChatMessage(ChatRole.Assistant, new List<AIContent> { new FunctionCallContent("call_1", "get_module_settings") }),
                new ChatMessage(ChatRole.Tool, new List<AIContent> {
                    new FunctionResultContent("call_1", null) { Exception = new InvalidOperationException("Enable the module first.") }
                })
            });

            var messages = (JArray)JObject.Parse(Assert.Single(endpoint.RequestBodies))["messages"];

            Assert.Equal("Error: Enable the module first.", (string)messages[1]["content"]);
        }

        [Fact]
        public async Task Offering_a_tool_that_is_not_a_function_is_refused() {
            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody));

            await Assert.ThrowsAsync<NotSupportedException>(() => client.GetResponseAsync(
                new[] { Request("Hello") },
                new ChatOptions { Tools = new List<AITool> { new NotAFunctionTool() } }));
        }

        [Fact]
        public async Task An_error_body_where_the_key_crosses_the_shorten_cut_is_redacted_before_shortening() {
            const string apiKey = "sk-live-secret-key-1234567890abcdef";
            // Padding of 495 characters so the key starts at index 495 and crosses the 500-char cut.
            string padding = new string('x', 495);
            string errorBody = padding + apiKey + " trailing text that exceeds the 500 character limit.";

            var endpoint = new StubHttpMessageHandler(HttpStatusCode.BadRequest, errorBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint, key: apiKey);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { Request("Hello") }));

            // The exception's message contains no prefix of the key longer than 4 characters
            for (int length = 5; length <= apiKey.Length; length++) {
                string prefix = apiKey.Substring(0, length);
                Assert.DoesNotContain(prefix, exception.Message);
            }

            Assert.Contains("[reda", exception.Message);
        }

        [Fact]
        public async Task An_error_body_containing_the_key_multiple_times_has_all_occurrences_redacted() {
            const string apiKey = "sk-live-secret-key-99999";
            string errorBody = "Error 1: " + apiKey + " is invalid. Details: " + apiKey + " expired. Retry without " + apiKey + ".";

            var endpoint = new StubHttpMessageHandler(HttpStatusCode.BadRequest, errorBody);
            OpenAiCompatibleChatClient client = ClientFor(endpoint, key: apiKey);

            ProviderException exception = await Assert.ThrowsAsync<ProviderException>(
                () => client.GetResponseAsync(new[] { Request("Hello") }));

            Assert.DoesNotContain(apiKey, exception.Message);
            Assert.Contains("[redacted]", exception.Message);
        }

        [Fact]
        public async Task Usage_counts_come_back_with_the_reply() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}],"
              + "\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":80,\"total_tokens\":1280}}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal((long?)1200, response.Usage.InputTokenCount);
            Assert.Equal((long?)80,   response.Usage.OutputTokenCount);
            Assert.Equal((long?)1280, response.Usage.TotalTokenCount);
            Assert.Null(response.Usage.CachedInputTokenCount);
        }

        [Fact]
        public async Task Cached_tokens_come_back_from_OpenAIs_details_block() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}],"
              + "\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":80,\"total_tokens\":1280,"
              + "\"prompt_tokens_details\":{\"cached_tokens\":900}}}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal((long?)1200, response.Usage.InputTokenCount);
            Assert.Equal((long?)900,  response.Usage.CachedInputTokenCount);
        }

        [Fact]
        public async Task Cached_tokens_come_back_from_DeepSeeks_flat_field() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}],"
              + "\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":80,\"total_tokens\":1280,"
              + "\"prompt_cache_hit_tokens\":700,\"prompt_cache_miss_tokens\":500}}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal((long?)1200, response.Usage.InputTokenCount);
            Assert.Equal((long?)700,  response.Usage.CachedInputTokenCount);
        }

        [Fact]
        public async Task A_reply_without_a_usage_block_has_no_usage() {
            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, ReplyBody));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Null(response.Usage);
        }

        [Fact]
        public async Task Null_cache_details_are_read_as_no_cached_split() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}],"
              + "\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":80,\"total_tokens\":1280,\"prompt_tokens_details\":null}}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal("Try the Pathing module.", response.Text);
            Assert.Equal((long?)1200, response.Usage.InputTokenCount);
            Assert.Equal((long?)80,   response.Usage.OutputTokenCount);
            Assert.Null(response.Usage.CachedInputTokenCount);
        }

        [Fact]
        public async Task Null_cache_details_still_fall_back_to_the_flat_cached_field() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}],"
              + "\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":80,\"total_tokens\":1280,"
              + "\"prompt_tokens_details\":null,\"prompt_cache_hit_tokens\":700}}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal((long?)700, response.Usage.CachedInputTokenCount);
        }

        [Fact]
        public async Task Non_object_cache_details_are_read_as_no_cached_split() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}],"
              + "\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":80,\"total_tokens\":1280,\"prompt_tokens_details\":\"oops\"}}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal((long?)1200, response.Usage.InputTokenCount);
            Assert.Null(response.Usage.CachedInputTokenCount);
        }

        [Fact]
        public async Task Odd_usage_values_are_read_as_no_usage_rather_than_replacing_the_reply() {
            const string body =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Try the Pathing module.\"},\"finish_reason\":\"stop\"}],"
              + "\"usage\":{\"prompt_tokens\":\"lots\",\"completion_tokens\":80,\"total_tokens\":1280}}";

            OpenAiCompatibleChatClient client = ClientFor(new StubHttpMessageHandler(HttpStatusCode.OK, body));

            ChatResponse response = await client.GetResponseAsync(new[] { Request("Hello") });

            Assert.Equal("Try the Pathing module.", response.Text);
            Assert.Null(response.Usage);
        }

        private static AIFunctionDeclaration ListModulesTool() {
            return AIFunctionFactory.CreateDeclaration(
                "list_installed_modules",
                "Lists the Installed Modules.",
                JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement);
        }

        private sealed class NotAFunctionTool : AITool { }

        private static OpenAiCompatibleChatClient ClientFor(StubHttpMessageHandler endpoint,
                                                            string               url = "https://api.deepseek.com",
                                                            string               key = "sk-the-key") {
            return new OpenAiCompatibleChatClient(
                ProviderSettings.Parse(url, "deepseek-flash", key),
                new HttpClient(endpoint));
        }

        private static ChatMessage Request(string text) => new ChatMessage(ChatRole.User, text);
    }
}

