using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            return GetResponseCoreAsync(messages, cancellationToken);
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

        private async Task<AiChatResponse> GetResponseCoreAsync(IEnumerable<AiChatMessage> messages, CancellationToken cancellationToken) {
            var payload = new JObject {
                ["model"]    = _model,
                ["messages"] = new JArray((messages ?? Enumerable.Empty<AiChatMessage>()).Select(ToJson)),
                ["stream"]   = false
            };

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
                json = JObject.Parse(body);
            } catch (JsonException exception) {
                throw new ProviderException(ProviderFailure.Unexpected, "The Provider returned a response that is not JSON.", exception);
            }

            JToken choice = (json["choices"] as JArray)?.FirstOrDefault();

            if (choice == null) {
                throw new ProviderException(ProviderFailure.Unexpected, "The Provider returned no choices.");
            }

            string text = ReadContent(choice["message"]?["content"]);
            var    response = new AiChatResponse(new AiChatMessage(AiChatRole.Assistant, text)) {
                ModelId = json["model"]?.Value<string>() ?? _model
            };

            string finishReason = choice["finish_reason"]?.Value<string>();

            if (!string.IsNullOrEmpty(finishReason)) {
                response.FinishReason = ToFinishReason(finishReason);
            }

            JToken usage = json["usage"];

            if (usage != null) {
                response.Usage = new AiUsageDetails {
                    InputTokenCount  = usage["prompt_tokens"]?.Value<long?>(),
                    OutputTokenCount = usage["completion_tokens"]?.Value<long?>(),
                    TotalTokenCount  = usage["total_tokens"]?.Value<long?>()
                };
            }

            return response;
        }

        private static JObject ToJson(AiChatMessage message) {
            return new JObject {
                ["role"]    = RoleName(message.Role),
                ["content"] = message.Text ?? string.Empty
            };
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
                    return new ProviderException(ProviderFailure.InvalidKey, "The Provider rejected the API key (" + (int)statusCode + "). " + Shorten(body));
                case 404:
                    return new ProviderException(ProviderFailure.UnknownModel, "The Provider did not find the model or endpoint (404). " + Shorten(body));
                case 429:
                    return new ProviderException(ProviderFailure.RateLimited, "The Provider is rate limiting requests (429). " + Shorten(body));
            }

            if ((int)statusCode == 400 && LooksLikeAnUnknownModel(body)) {
                return new ProviderException(ProviderFailure.UnknownModel, "The Provider does not have that model (400). " + Shorten(body));
            }

            return new ProviderException(ProviderFailure.Unexpected, "The Provider returned HTTP " + (int)statusCode + ". " + Shorten(body));
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
