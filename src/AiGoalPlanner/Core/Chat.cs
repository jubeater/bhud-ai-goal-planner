using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// One Chat: the conversation between the Player and the AI, from "New chat" until the next one.
    /// </summary>
    /// <remarks>
    /// This is the planning core's public seam. The Chat window and the Provider layer talk to a
    /// Chat only through this class, which keeps it free of Blish HUD types. The model is reached
    /// through <see cref="IChatClient"/>, so tests can drive the Chat with a scripted model.
    /// </remarks>
    public sealed class Chat
    {
        private readonly Func<IChatClient> _chatClient;
        private readonly List<ChatMessage> _messages     = new List<ChatMessage>();
        private readonly object            _messagesLock = new object();
        private readonly SemaphoreSlim     _sendLock     = new SemaphoreSlim(1, 1);

        private int _generation;

        public Chat(Func<IChatClient> chatClient) {
            _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        }

        /// <summary>
        /// Raised whenever what the Player sees in the Chat changes, so the Chat window can redraw.
        /// It may be raised on a background thread; the Chat window marshals it to the game thread.
        /// </summary>
        public event EventHandler<EventArgs> MessagesChanged;

        /// <summary>
        /// Everything said in this Chat so far, oldest first.
        /// </summary>
        public IReadOnlyList<ChatMessage> Messages {
            get {
                lock (_messagesLock) {
                    return _messages.ToArray();
                }
            }
        }

        /// <summary>
        /// Sends a Request in this Chat: the Player's words are added to the Chat and the AI answers.
        /// An empty Request (a Send with nothing typed) is ignored.
        /// </summary>
        /// <remarks>
        /// History is append-only: earlier messages are never edited or replaced. Only what the
        /// Player and the AI said goes back to the model; notices stay in the Chat. Sends are
        /// serialized, and a Request that is still waiting its turn when the Player starts a new
        /// Chat is dropped instead of being sent into the new one.
        /// </remarks>
        public async Task SendAsync(string request, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(request)) return;

            // Read before waiting for the lock: a Request belongs to the Chat the Player sent it
            // in, not to whatever Chat is current by the time the model is free to answer it.
            int generation = Volatile.Read(ref _generation);

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try {
                // The Player started a new Chat while this Request was queued: it is dropped
                // before it is shown and before the model sees it.
                if (generation != Volatile.Read(ref _generation)) return;

                Append(new ChatMessage(ChatMessageAuthor.Player, request.Trim()));

                ChatResponse response;

                try {
                    IChatClient chatClient = _chatClient();

                    response = await chatClient
                                      .GetResponseAsync(BuildHistory(), cancellationToken: cancellationToken)
                                      .ConfigureAwait(false);
                } catch (ProviderException exception) {
                    if (generation == Volatile.Read(ref _generation)) AppendNotice(Describe(exception.Failure));

                    return;
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                } catch (Exception) {
                    if (generation == Volatile.Read(ref _generation)) AppendNotice(Describe(ProviderFailure.Unexpected));

                    return;
                }

                // The Player started a new Chat while the Provider was thinking: the reply belongs
                // to the old Chat, so it is dropped rather than shown out of context.
                if (generation != Volatile.Read(ref _generation)) return;

                ShowResponse(response);
            } finally {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Starts a new Chat: what the Player said before is cleared, and the next Request starts from scratch.
        /// </summary>
        public void StartNewChat() {
            lock (_messagesLock) {
                _messages.Clear();
            }

            Interlocked.Increment(ref _generation);

            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ShowResponse(ChatResponse response) {
            if (response == null) {
                AppendNotice(Describe(ProviderFailure.Unexpected));
                return;
            }

            ChatFinishReason? finishReason = response.FinishReason;
            string            text         = response.Text;

            if (finishReason.HasValue && !string.IsNullOrEmpty(finishReason.Value.Value) && !finishReason.Value.Equals(ChatFinishReason.Stop)) {
                AppendNotice(DescribeFinishReason(finishReason.Value, text));
                return;
            }

            if (string.IsNullOrWhiteSpace(text)) {
                AppendNotice("The Provider returned an empty reply. Try again.");
                return;
            }

            Append(new ChatMessage(ChatMessageAuthor.Ai, text));
        }

        private IReadOnlyList<AiChatMessage> BuildHistory() {
            lock (_messagesLock) {
                return _messages
                      .Where(message => message.Author == ChatMessageAuthor.Player || message.Author == ChatMessageAuthor.Ai)
                      .Select(message => new AiChatMessage(ToRole(message.Author), message.Text))
                      .ToList();
            }
        }

        private static ChatRole ToRole(ChatMessageAuthor author) {
            return author == ChatMessageAuthor.Player ? ChatRole.User : ChatRole.Assistant;
        }

        private static string DescribeFinishReason(ChatFinishReason finishReason, string text) {
            if (finishReason.Equals(ChatFinishReason.ContentFilter)) {
                return string.IsNullOrWhiteSpace(text)
                           ? "The model refused to answer this request."
                           : "The model refused to answer this request. It said: " + text;
            }

            if (finishReason.Equals(ChatFinishReason.Length)) {
                return "The reply was cut off because the model reached its length limit. Try a shorter request.";
            }

            if (finishReason.Equals(ChatFinishReason.ToolCalls)) {
                return "The model tried to use a tool, which this Chat does not support yet.";
            }

            return "The model stopped before finishing (" + finishReason.Value + ").";
        }

        private static string Describe(ProviderFailure failure) {
            switch (failure) {
                case ProviderFailure.NotConfigured:
                    return "Connect a Provider first: click Provider settings, then enter the endpoint URL and model name, and an API key if your Provider needs one.";
                case ProviderFailure.InvalidEndpoint:
                    return "The endpoint URL is not valid. Check it in Provider settings.";
                case ProviderFailure.InvalidKey:
                    return "The Provider rejected the API key. Check it in Provider settings and try again.";
                case ProviderFailure.UnknownModel:
                    return "The Provider does not have that model. Check the model name in Provider settings.";
                case ProviderFailure.RateLimited:
                    return "The Provider is rate limiting requests. Wait a moment, then try again.";
                case ProviderFailure.Network:
                    return "Could not reach the Provider. Check the endpoint URL and your internet connection, then try again.";
                default:
                    return "Something went wrong while asking the Provider. Try again.";
            }
        }

        private void AppendNotice(string text) {
            Append(new ChatMessage(ChatMessageAuthor.Notice, text));
        }

        private void Append(ChatMessage message) {
            lock (_messagesLock) {
                _messages.Add(message);
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
