using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace jubeater.aigoalplanner.Providers
{
    /// <summary>
    /// Wraps an <see cref="IChatClient"/> to run a quick tool-calling test on first use of a
    /// Provider/model. Models that cannot call tools are refused with a clear message and the result
    /// is remembered, so they are not asked to plan.
    /// </summary>
    public sealed class ToolCallingChatClient : IChatClient
    {
        private static readonly AIFunctionDeclaration TestProbeTool = AIFunctionFactory.CreateDeclaration(
            "test_tool",
            "A test tool to check tool calling support.",
            JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}").RootElement);

        private readonly IChatClient                 _innerClient;
        private readonly string                      _endpoint;
        private readonly string                      _model;
        private readonly ToolCallingCapabilityStore  _capabilityStore;
        private readonly SemaphoreSlim               _probeLock = new SemaphoreSlim(1, 1);

        public ToolCallingChatClient(IChatClient                innerClient,
                                    string                     endpoint,
                                    string                     model,
                                    ToolCallingCapabilityStore capabilityStore) {
            _innerClient      = innerClient      ?? throw new ArgumentNullException(nameof(innerClient));
            _endpoint         = endpoint         ?? throw new ArgumentNullException(nameof(endpoint));
            _model            = model            ?? throw new ArgumentNullException(nameof(model));
            _capabilityStore  = capabilityStore  ?? throw new ArgumentNullException(nameof(capabilityStore));
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
                                                         ChatOptions               options           = null,
                                                         CancellationToken         cancellationToken = default) {
            await EnsureToolCallingCapabilityAsync(cancellationToken).ConfigureAwait(false);

            return await _innerClient.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
                                                                              ChatOptions               options           = null,
                                                                              CancellationToken         cancellationToken = default) {
            return _innerClient.GetStreamingResponseAsync(messages, options, cancellationToken);
        }

        public object GetService(Type serviceType, object serviceKey = null) {
            return _innerClient.GetService(serviceType, serviceKey);
        }

        public void Dispose() {
            _innerClient.Dispose();
            _probeLock.Dispose();
        }

        private async Task EnsureToolCallingCapabilityAsync(CancellationToken cancellationToken) {
            bool? capable = _capabilityStore.GetCapability(_endpoint, _model);

            if (capable == false) {
                throw new ProviderException(ProviderFailure.ToolCallingNotSupported);
            }

            if (capable == true) {
                return;
            }

            await _probeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                // Re-check under the lock in case another request ran the probe while waiting.
                capable = _capabilityStore.GetCapability(_endpoint, _model);

                if (capable == false) {
                    throw new ProviderException(ProviderFailure.ToolCallingNotSupported);
                }

                if (capable == true) {
                    return;
                }

                await RunProbeAsync(cancellationToken).ConfigureAwait(false);
            } finally {
                _probeLock.Release();
            }
        }

        private async Task RunProbeAsync(CancellationToken cancellationToken) {
            var probeMessages = new[] {
                new ChatMessage(ChatRole.User, "Check tool calling support. Call the test_tool tool.")
            };

            var probeOptions = new ChatOptions {
                Tools    = new List<AITool> { TestProbeTool },
                ToolMode = ChatToolMode.RequireSpecific("test_tool")
            };

            ChatResponse probeResponse;

            try {
                probeResponse = await _innerClient
                                      .GetResponseAsync(probeMessages, probeOptions, cancellationToken)
                                      .ConfigureAwait(false);
            } catch (ProviderException exception) when (exception.Failure == ProviderFailure.Network
                                                     || exception.Failure == ProviderFailure.InvalidKey
                                                     || exception.Failure == ProviderFailure.RateLimited
                                                     || exception.Failure == ProviderFailure.UnknownModel
                                                     || exception.Failure == ProviderFailure.NotConfigured) {
                // Network, API key and rate-limit failures do not mean the model cannot call tools.
                throw;
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception exception) {
                _capabilityStore.Record(_endpoint, _model, canCallTools: false);
                throw new ProviderException(ProviderFailure.ToolCallingNotSupported, "The model does not support tool calling.", exception);
            }

            bool calledTool = probeResponse?.Messages != null
                           && probeResponse.Messages
                                           .SelectMany(message => message.Contents)
                                           .OfType<FunctionCallContent>()
                                           .Any();

            if (!calledTool) {
                _capabilityStore.Record(_endpoint, _model, canCallTools: false);
                throw new ProviderException(ProviderFailure.ToolCallingNotSupported);
            }

            _capabilityStore.Record(_endpoint, _model, canCallTools: true);
        }
    }
}
