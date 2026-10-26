using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using Microsoft.Extensions.AI;

namespace jubeater.aigoalplanner.Tests.Fakes
{
    /// <summary>
    /// A model with a script: each Request takes the next reply, finish reason or failure off the
    /// script. It records what the Chat sent, so a test can check the model saw the whole Chat.
    /// </summary>
    internal sealed class ScriptedChatClient : IChatClient
    {
        private readonly Queue<object> _script = new Queue<object>();

        public List<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>> Requests { get; } =
            new List<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>>();

        public void WillReply(string text) {
            _script.Enqueue(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, text)) {
                FinishReason = ChatFinishReason.Stop
            });
        }

        public void WillStop(ChatFinishReason finishReason, string text = "") {
            _script.Enqueue(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, text)) {
                FinishReason = finishReason
            });
        }

        public void WillFail(ProviderFailure failure) {
            _script.Enqueue(new ProviderException(failure));
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
                                                   ChatOptions                                  options           = null,
                                                   CancellationToken                            cancellationToken = default) {
            Requests.Add(messages.ToList());

            if (_script.Count == 0) throw new InvalidOperationException("The scripted model has no reply left.");

            object next = _script.Dequeue();

            if (next is ProviderException failure) return Task.FromException<ChatResponse>(failure);

            return Task.FromResult((ChatResponse)next);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
                                                                              ChatOptions                                  options           = null,
                                                                              CancellationToken                            cancellationToken = default) {
            throw new NotSupportedException("The scripted model does not stream.");
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose() { /* NOOP */ }
    }
}
