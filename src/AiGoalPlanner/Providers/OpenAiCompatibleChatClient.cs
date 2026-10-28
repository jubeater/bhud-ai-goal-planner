using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using JsonException = Newtonsoft.Json.JsonException;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;
using AiChatResponse = Microsoft.Extensions.AI.ChatResponse;
using AiChatResponseUpdate = Microsoft.Extensions.AI.ChatResponseUpdate;
using AiChatClient = Microsoft.Extensions.AI.IChatClient;
using AiChatClientMetadata = Microsoft.Extensions.AI.ChatClientMetadata;
using AiChatFinishReason = Microsoft.Extensions.AI.ChatFinishReason;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;
using AiUsageDetails = Microsoft.Extensions.AI.UsageDetails;

namespace jubeater.aigoalplanner.Providers
{
    /// <summary>
    /// One client for every OpenAI-compatible Provider, using the Chat Completions API through
    /// <see cref="AiChatClient"/>.
    /// </summary>
    /// <remarks>
    /// The module ships its own small HTTP client instead of the OpenAI SDK. The SDK drags another
    /// JSON stack (System.ClientModel and a newer System.Text.Json) into Blish HUD's .NET Framework
    /// process, where Gw2Sharp already loads System.Text.Json and no binding redirects exist. A raw
    /// request keeps the number of extra assemblies in the .bhm small; see the README.
    /// Requests are not streamed: a Chat reply arrives complete, which is all the Chat needs.
    /// Tools offered in <see cref="AiChatOptions.Tools"/> are sent as functions, and the tools the
    /// model asks to call come back as <see cref="FunctionCallContent"/>. Running them is the
    /// caller's job; this client only speaks the wire format.
    /// </remarks>
    public sealed class OpenAiCompatibleChatClient : AiChatClient
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        private readonly HttpClient _httpClient;
        private readonly bool       _ownsHttpClient;
        private readonly string     _model;
        private readonly string     _apiKey;
        private readonly Uri        _completionsUri;
        private readonly AiChatClientMetadata _metadata;

        /// <summary>
        /// Talks to the Provider the Player connected. <paramref name="settings"/> has already been
        /// checked by <see cref="ProviderSettings.Parse"/>, so anything the Player could get wrong
        /// was reported before this client was built.
        /// </summary>
        public OpenAiCompatibleChatClient(ProviderSettings settings, HttpClient httpClient = null) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _model    = settings.Model;
            _apiKey   = settings.ApiKey;
            _metadata = new AiChatClientMetadata("OpenAI-compatible", settings.EndpointUri, _model);
            _completionsUri = CompletionsUriFor(settings.EndpointUri);

            if (httpClient == null) {
                _httpClient = new HttpClient { Timeout = DefaultTimeout };
                _ownsHttpClient = true;
            } else {
                _httpClient = httpClient;
            }
        }

        public Task<AiChatResponse> GetResponseAsync(IEnumerable<AiChatMessage> messages,
                                                     AiChatOptions               options           = null,
                                                     CancellationToken           cancellationToken = default) {
            return GetResponseCoreAsync(messages, options, cancellationToken);
        }

        /// <summary>
        /// Streaming is not used by the Chat in this version. The interface method is required, so
        /// it says so rather than pretending to stream.
        /// </summary>
        public IAsyncEnumerable<AiChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AiChatMessage> messages,
                                                                                AiChatOptions               options           = null,
                                                                                CancellationToken           cancellationToken = default) {
            throw new NotSupportedException("This Provider client does not stream responses.");
        }

        public object GetService(Type serviceType, object serviceKey = null) {
            if (serviceKey != null) return null;
            if (serviceType == typeof(AiChatClient)) return this;
            if (serviceType == typeof(AiChatClientMetadata)) return _metadata;

            return null;
        }

        public void Dispose() {
            if (_ownsHttpClient) _httpClient.Dispose();
        }

        private async Task<AiChatResponse> GetResponseCoreAsync(IEnumerable<AiChatMessage> messages, AiChatOptions options, CancellationToken cancellationToken) {
            var payload = new JObject {
                ["model"]    = _model,
                ["messages"] = new JArray((messages ?? Enumerable.Empty<AiChatMessage>()).SelectMany(ToJson)),
                ["stream"]   = false
            };

            AddTools(payload, options);

            using (var request = new HttpRequestMessage(HttpMethod.Post, _completionsUri)) {
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");

                if (_apiKey.Length > 0) {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                }

                string body;

                try {
                    using (HttpResponseMessage response = await _httpClient
                                                                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                                                                .ConfigureAwait(false)) {
                        body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        if (!response.IsSuccessStatusCode) {
                            if (_apiKey.Length > 0) {
                                body = KeyRedaction.Redact(body, _apiKey);
                            }

                            throw FailureFor(response.StatusCode, body);
                        }
                    }
                } catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested) {
                    throw new ProviderException(ProviderFailure.Network, "The Provider took too long to answer.", exception);
                } catch (HttpRequestException exception) {
                    throw new ProviderException(ProviderFailure.Network, "The Provider could not be reached.", exception);
                } catch (WebException exception) {
                    throw new ProviderException(ProviderFailure.Network, "The Provider could not be reached.", exception);
                }

                return ReadResponse(body);
            }
        }

        private AiChatResponse ReadResponse(string body) {
            JObject json;

            try {
                json = ParseJson(body) as JObject ?? throw new JsonReaderException("The response is not a JSON object.");
            } catch (JsonException exception) {
                throw new ProviderException(ProviderFailure.Unexpected, "The Provider returned a response that is not JSON.", exception);
            }

            JToken choice = (json["choices"] as JArray)?.FirstOrDefault();

            if (choice == null) {
                throw new ProviderException(ProviderFailure.Unexpected, "The Provider returned no choices.");
            }

            JToken message = choice["message"];
            string text    = ReadContent(message?["content"]);

            var contents = new List<AIContent>();

            if (text.Length > 0) contents.Add(new TextContent(text));

            contents.AddRange(ReadToolCalls(message?["tool_calls"] as JArray));

            var response = new AiChatResponse(new AiChatMessage(AiChatRole.Assistant, contents)) {
                ModelId = json["model"]?.Value<string>() ?? _model
            };

            string finishReason = choice["finish_reason"]?.Value<string>();

            if (!string.IsNullOrEmpty(finishReason)) {
                response.FinishReason = ToFinishReason(finishReason);
            }

            JToken usage = json["usage"];

            if (usage != null) {
                response.Usage = ReadUsage(usage);
            }

            return response;
        }

        /// <summary>
        /// The Provider's usage block, or null when it is missing or reads as nothing the Chat
        /// could show. Usage is best-effort display data: a block no Provider shape explains must
        /// never replace a usable reply with a failure, so a block that cannot be read is no usage
        /// rather than an error.
        /// </summary>
        private static AiUsageDetails ReadUsage(JToken usage) {
            try {
                if (!(usage is JObject usageObject)) return null;

                return new AiUsageDetails {
                    InputTokenCount       = usageObject["prompt_tokens"]?.Value<long?>(),
                    OutputTokenCount      = usageObject["completion_tokens"]?.Value<long?>(),
                    TotalTokenCount       = usageObject["total_tokens"]?.Value<long?>(),
                    CachedInputTokenCount = ReadCachedInputTokens(usageObject)
                };
            } catch (Exception) {
                return null;
            }
        }

        /// <summary>
        /// The cached input tokens in a usage block, however the Provider spells them: OpenAI nests
        /// them as <c>prompt_tokens_details.cached_tokens</c>, DeepSeek also reports them flat as
        /// <c>prompt_cache_hit_tokens</c> (the same number twice), and a Provider that reports no
        /// caching leaves neither. A details block that is null or no object is absent, and the
        /// flat field is tried instead.
        /// </summary>
        private static long? ReadCachedInputTokens(JObject usage) {
            if (usage["prompt_tokens_details"] is JObject details) {
                long? cached = details["cached_tokens"]?.Value<long?>();

                if (cached.HasValue) return cached;
            }

            return usage["prompt_cache_hit_tokens"]?.Value<long?>();
        }

        /// <summary>
        /// Offers the model the tools in <paramref name="options"/>. Only function tools exist in
        /// Chat Completions; any other kind of tool is a programming error, not something to drop
        /// quietly, because the model would then plan without a tool the caller counted on.
        /// </summary>
        private static void AddTools(JObject payload, AiChatOptions options) {
            if (options?.Tools == null || options.Tools.Count == 0) return;

            var tools = new JArray();

            foreach (AITool tool in options.Tools) {
                if (!(tool is AIFunctionDeclaration function)) {
                    throw new NotSupportedException("Only function tools can be offered to an OpenAI-compatible Provider, not " + tool?.GetType().Name + ".");
                }

                tools.Add(new JObject {
                    ["type"]     = "function",
                    ["function"] = new JObject {
                        ["name"]        = function.Name,
                        ["description"] = function.Description ?? string.Empty,
                        ["parameters"]  = ParseJson(function.JsonSchema.GetRawText())
                    }
                });
            }

            payload["tools"] = tools;

            JToken toolChoice = ToolChoiceFor(options.ToolMode);

            if (toolChoice != null) payload["tool_choice"] = toolChoice;
        }

        private static JToken ToolChoiceFor(ChatToolMode toolMode) {
            switch (toolMode) {
                case null:                return null;
                case NoneChatToolMode _:  return "none";
                case AutoChatToolMode _:  return "auto";
                case RequiredChatToolMode required when !string.IsNullOrEmpty(required.RequiredFunctionName):
                    return new JObject {
                        ["type"]     = "function",
                        ["function"] = new JObject { ["name"] = required.RequiredFunctionName }
                    };
                case RequiredChatToolMode _: return "required";
                default:                     return null;
            }
        }

        /// <summary>
        /// One message in, one or more out: Chat Completions wants every tool result as a message of
        /// its own, while one <see cref="AiChatMessage"/> can carry the results of several calls.
        /// </summary>
        private static IEnumerable<JObject> ToJson(AiChatMessage message) {
            List<FunctionResultContent> results = message.Contents.OfType<FunctionResultContent>().ToList();

            if (results.Count > 0) {
                return results.Select(result => new JObject {
                    ["role"]         = "tool",
                    ["tool_call_id"] = result.CallId,
                    ["content"]      = ResultText(result)
                });
            }

            List<FunctionCallContent> calls = message.Contents.OfType<FunctionCallContent>().ToList();

            var json = new JObject { ["role"] = RoleName(message.Role) };

            if (calls.Count == 0) {
                json["content"] = message.Text ?? string.Empty;

                return new[] { json };
            }

            // An assistant turn that only called tools has no text, which the API spells as null.
            json["content"]    = string.IsNullOrEmpty(message.Text) ? JValue.CreateNull() : (JToken)message.Text;
            json["tool_calls"] = new JArray(calls.Select(call => new JObject {
                ["id"]       = call.CallId,
                ["type"]     = "function",
                ["function"] = new JObject {
                    ["name"]      = call.Name,
                    ["arguments"] = ArgumentsText(call.Arguments)
                }
            }));

            return new[] { json };
        }

        /// <summary>
        /// The model reads a tool result as text. A failed call says so rather than sending nothing,
        /// so the model can tell a failure from an empty answer.
        /// </summary>
        private static string ResultText(FunctionResultContent result) {
            if (result.Exception != null && result.Result == null) {
                return "Error: " + result.Exception.Message;
            }

            switch (result.Result) {
                case null:          return string.Empty;
                case string text:   return text;
                default:            return ToJToken(result.Result).ToString(Formatting.None);
            }
        }

        private static string ArgumentsText(IDictionary<string, object> arguments) {
            var json = new JObject();

            if (arguments != null) {
                foreach (KeyValuePair<string, object> argument in arguments) {
                    json[argument.Key] = ToJToken(argument.Value);
                }
            }

            return json.ToString(Formatting.None);
        }

        /// <summary>
        /// Converts a value to JSON with the Newtonsoft.Json Blish HUD ships. A
        /// <see cref="JsonElement"/> (what Microsoft.Extensions.AI uses for JSON) is copied by its
        /// text wherever it sits in the value, because Newtonsoft.Json would otherwise serialize the
        /// struct's properties.
        /// </summary>
        private static JToken ToJToken(object value) {
            switch (value) {
                case null:                return JValue.CreateNull();
                case JToken token:        return token;
                case JsonElement element: return ParseJson(element.GetRawText());
                default:                  return JToken.FromObject(value, ValueSerializer);
            }
        }

        private static readonly JsonSerializer ValueSerializer = JsonSerializer.Create(new JsonSerializerSettings {
            Converters = { new JsonElementConverter() }
        });

        private sealed class JsonElementConverter : JsonConverter
        {
            public override bool CanConvert(Type objectType) => objectType == typeof(JsonElement);

            public override bool CanRead => false;

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                ParseJson(((JsonElement)value).GetRawText()).WriteTo(writer);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Parses JSON with strings left as strings. Newtonsoft.Json turns anything that looks like a
        /// date into a <see cref="DateTime"/> by default, which would hand a tool a different type
        /// than the model sent, shift its time zone when it goes back, and empty a reply whose whole
        /// text happens to be a date.
        /// </summary>
        private static JToken ParseJson(string json) {
            using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None }) {
                JToken token = JToken.ReadFrom(reader);

                if (reader.Read()) throw new JsonReaderException("Unexpected content after the JSON value.");

                return token;
            }
        }

        /// <summary>
        /// Reads the tools the model asked to call. Arguments that are not a JSON object are not
        /// guessed at: the call keeps its name and id, carries the parse error, and the caller can
        /// tell the model its call was malformed.
        /// </summary>
        private static IEnumerable<FunctionCallContent> ReadToolCalls(JArray toolCalls) {
            if (toolCalls == null) yield break;

            int index = 0;

            foreach (JToken toolCall in toolCalls) {
                JToken function = toolCall["function"];
                string name     = function?["name"]?.Value<string>() ?? string.Empty;

                // A call without an id cannot be answered, so one is made up from its position.
                string callId = toolCall["id"]?.Value<string>();
                if (string.IsNullOrEmpty(callId)) callId = "call_" + index;

                index++;

                Exception                   parseError = null;
                Dictionary<string, object> arguments  = null;

                try {
                    arguments = ReadArguments(function?["arguments"]);
                } catch (JsonException exception) {
                    parseError = exception;
                } catch (InvalidOperationException exception) {
                    parseError = exception;
                }

                yield return new FunctionCallContent(callId, name, arguments) { Exception = parseError };
            }
        }

        /// <summary>
        /// Arguments arrive as a JSON string in the OpenAI API, but some compatible Providers (Ollama
        /// among them) send the object itself, so both are read. Values become plain .NET values —
        /// strings, numbers, booleans, null, lists and dictionaries — so tools do not have to know
        /// which JSON library read them.
        /// </summary>
        private static Dictionary<string, object> ReadArguments(JToken arguments) {
            if (arguments == null || arguments.Type == JTokenType.Null) return new Dictionary<string, object>();

            if (arguments.Type == JTokenType.String) {
                string text = arguments.Value<string>();

                if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, object>();

                arguments = ParseJson(text);
            }

            if (!(arguments is JObject json)) {
                throw new InvalidOperationException("The tool call's arguments are not a JSON object.");
            }

            return json.Properties().ToDictionary(property => property.Name, property => ToPlainValue(property.Value));
        }

        private static object ToPlainValue(JToken token) {
            switch (token.Type) {
                case JTokenType.Object:
                    return ((JObject)token).Properties().ToDictionary(property => property.Name, property => ToPlainValue(property.Value));
                case JTokenType.Array:
                    return token.Select(ToPlainValue).ToList();
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                default:
                    return ((JValue)token).Value;
            }
        }

        private static string RoleName(AiChatRole role) {
            if (role.Equals(AiChatRole.System)) return "system";
            if (role.Equals(AiChatRole.Assistant)) return "assistant";
            if (role.Equals(AiChatRole.Tool)) return "tool";

            return "user";
        }

        private static string ReadContent(JToken content) {
            if (content == null) return string.Empty;

            if (content.Type == JTokenType.String) return content.Value<string>() ?? string.Empty;

            if (content is JArray parts) {
                return string.Concat(parts.Select(part => part["text"]?.Value<string>() ?? string.Empty));
            }

            return string.Empty;
        }

        private static AiChatFinishReason ToFinishReason(string finishReason) {
            switch (finishReason) {
                case "stop":           return AiChatFinishReason.Stop;
                case "length":         return AiChatFinishReason.Length;
                case "content_filter": return AiChatFinishReason.ContentFilter;
                case "tool_calls":
                case "function_call":  return AiChatFinishReason.ToolCalls;
                default:               return new AiChatFinishReason(finishReason);
            }
        }

        private static ProviderException FailureFor(HttpStatusCode statusCode, string body) {
            switch ((int)statusCode) {
                case 401:
                case 403:
                    return new ProviderException(ProviderFailure.InvalidKey, "The Provider rejected the API key (" + (int)statusCode + "). " + Shorten(body), statusCode);
                case 404:
                    return new ProviderException(ProviderFailure.UnknownModel, "The Provider did not find the model or endpoint (404). " + Shorten(body), statusCode);
                case 429:
                    return new ProviderException(ProviderFailure.RateLimited, "The Provider is rate limiting requests (429). " + Shorten(body), statusCode);
            }

            if ((int)statusCode == 400 && LooksLikeAnUnknownModel(body)) {
                return new ProviderException(ProviderFailure.UnknownModel, "The Provider does not have that model (400). " + Shorten(body), statusCode);
            }

            return new ProviderException(ProviderFailure.Unexpected, "The Provider returned HTTP " + (int)statusCode + ". " + Shorten(body), statusCode);
        }

        private static bool LooksLikeAnUnknownModel(string body) {
            if (string.IsNullOrEmpty(body)) return false;

            string lower = body.ToLowerInvariant();

            return lower.Contains("model")
                && (lower.Contains("not found") || lower.Contains("does not exist") || lower.Contains("unknown") || lower.Contains("invalid"));
        }

        private static string Shorten(string body) {
            if (string.IsNullOrWhiteSpace(body)) return string.Empty;

            const int limit = 500;

            return body.Length <= limit ? body : body.Substring(0, limit);
        }

        private static Uri CompletionsUriFor(Uri endpointUri) {
            string endpoint = endpointUri.ToString().TrimEnd('/');

            if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) {
                return new Uri(endpoint);
            }

            return new Uri(endpoint + "/chat/completions");
        }
    }
}
