using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

        private TaskCompletionSource<bool> _hold;
        private Action                   _afterReply;

        public List<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>> Requests { get; } =
            new List<IReadOnlyList<Microsoft.Extensions.AI.ChatMessage>>();

        public List<ChatOptions> RequestOptions { get; } = new List<ChatOptions>();

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

        public void WillFail(ProviderFailure failure, HttpStatusCode statusCode) {
            _script.Enqueue(new ProviderException(failure, statusCode));
        }

        public void WillFail(Exception exception) {
            _script.Enqueue(exception);
        }

        public void WillCallTool(string toolName = "test_tool") {
            _script.Enqueue(new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, new AIContent[] {
                new FunctionCallContent("call_0", toolName)
            })) {
                FinishReason = ChatFinishReason.ToolCalls
            });
        }

        /// <summary>
        /// Makes the model take its time: every Request it is given is recorded and then waits
        /// until <see cref="Release"/>, so a test can send something else while one is in flight.
        /// </summary>
        public void WillHold() {
            _hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Release() {
            _hold?.TrySetResult(true);
        }

        /// <summary>
        /// Runs <paramref name="action"/> once, as soon as the next reply is ready and before the
        /// Chat is given it, so a test can land something in between without depending on timing.
        /// </summary>
        public void WillRunWhenTheReplyIsReady(Action action) {
            _afterReply = action;
        }

        public async Task<ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
                                                         ChatOptions                                  options           = null,
                                                         CancellationToken                            cancellationToken = default) {
            Requests.Add(messages.ToList());
            RequestOptions.Add(options);

            TaskCompletionSource<bool> hold = _hold;

            if (hold != null) await hold.Task.ConfigureAwait(false);

            if (_script.Count == 0) throw new InvalidOperationException("The scripted model has no reply left.");

            object next = _script.Dequeue();

            Action afterReply = _afterReply;
            _afterReply = null;

            afterReply?.Invoke();

            if (next is Exception exception) throw exception;

            return (ChatResponse)next;
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
