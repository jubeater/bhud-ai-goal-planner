using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace jubeater.aigoalplanner.Providers
{
    /// <summary>
    /// Intercepts chat requests at the module layer to log probe refusal diagnostics to the log
    /// when a model is refused for not supporting tool calling, with any API key redacted.
    /// </summary>
    public sealed class ProbeLoggingChatClient : IChatClient
    {
        private readonly IChatClient    _inner;
        private readonly Action<string> _logWarn;
        private readonly string         _apiKey;

        public ProbeLoggingChatClient(IChatClient inner, Action<string> logWarn, string apiKey = null) {
            _inner   = inner   ?? throw new ArgumentNullException(nameof(inner));
            _logWarn = logWarn ?? throw new ArgumentNullException(nameof(logWarn));
            _apiKey  = apiKey;
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
                                                         ChatOptions               options           = null,
                                                         CancellationToken         cancellationToken = default) {
            try {
                return await _inner.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            } catch (ProviderException exception) when (exception.Failure == ProviderFailure.ToolCallingNotSupported) {
                string rawText = exception.InnerException != null && !string.IsNullOrWhiteSpace(exception.InnerException.Message)
                    ? exception.InnerException.Message
                    : exception.Message;

                string redacted = !string.IsNullOrWhiteSpace(_apiKey)
                    ? KeyRedaction.Redact(rawText, _apiKey)
                    : rawText;

                _logWarn("Provider refused tool-calling probe: " + redacted);

                throw;
            }
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
                                                                              ChatOptions               options           = null,
                                                                              CancellationToken         cancellationToken = default) {
            return _inner.GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        public object GetService(Type serviceType, object serviceKey = null) {
            return _inner.GetService(serviceType, serviceKey);
        }

        public void Dispose() {
            _inner.Dispose();
        }
    }
}

