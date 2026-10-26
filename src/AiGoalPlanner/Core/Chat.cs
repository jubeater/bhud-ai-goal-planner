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
        private readonly Func<string>       _systemPrompt;
        private readonly List<ChatMessage> _messages     = new List<ChatMessage>();
        private readonly object            _messagesLock = new object();
        private readonly SemaphoreSlim     _sendLock     = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Which Chat this is, so work started in one Chat can tell that the Player has moved on. It
        /// moves on under <c>_messagesLock</c>, together with emptying the Chat, which is what makes
        /// "still the same Chat?" and "add to it" one step. A read outside that lock only decides
        /// whether it is worth starting; whether a message lands is decided under the lock.
        /// </summary>
        private int _generation;

        /// <summary>
        /// A Chat talks to the model <paramref name="chatClient"/> builds and is told what it is by
        /// <paramref name="systemPrompt"/>. Both are asked again for every Request, so a change of
        /// Settings takes effect without losing the conversation.
        /// </summary>
        public Chat(Func<IChatClient> chatClient, Func<string> systemPrompt) {
            _chatClient   = chatClient   ?? throw new ArgumentNullException(nameof(chatClient));
            _systemPrompt = systemPrompt ?? throw new ArgumentNullException(nameof(systemPrompt));
        }

        /// <summary>
        /// Raised whenever what the Player sees in the Chat changes, so the Chat window can redraw.
        /// It may be raised on a background thread; the Chat window marshals it to the game thread.
        /// </summary>
        public event EventHandler<EventArgs> MessagesChanged;

        /// <summary>
        /// Everything said in this Chat so far, oldest first. The identity message the Chat puts
        /// ahead of the Player's words is not one of them: it is instruction, not conversation.
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
        /// Player and the AI said goes back to the model, behind the identity message that says which
        /// product the model is part of; notices stay in the Chat. Sends are serialized, and a Request
        /// that is still waiting its turn when the Player starts a new Chat is dropped instead of
        /// being sent into the new one. So is whatever else the old Chat still had coming: a reply or a
        /// Notice joins the Chat only while it is still the current one, so it can neither appear in
        /// the new Chat nor be sent to the model with it.
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
                if (!TryAppend(generation, new ChatMessage(ChatMessageAuthor.Player, request.Trim()))) return;

                ChatResponse response;

                try {
                    IChatClient chatClient = _chatClient();

                    response = await chatClient
                                      .GetResponseAsync(BuildHistory(), cancellationToken: cancellationToken)
                                      .ConfigureAwait(false);
                } catch (ProviderException exception) {
                    TryAppendNotice(generation, Describe(exception.Failure));

                    return;
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                } catch (Exception) {
                    TryAppendNotice(generation, Describe(ProviderFailure.Unexpected));

                    return;
                }

                // The Player may have started a new Chat while the Provider was thinking. The reply
                // then belongs to the old Chat, so it is dropped rather than shown out of context:
                // ShowResponse appends only while the Chat it answers is still the current one.
                ShowResponse(generation, response);
            } finally {
                _sendLock.Release();
            }
        }

        /// <summary>
        /// Starts a new Chat: what the Player said before is cleared, and the next Request starts from scratch.
        /// </summary>
        /// <remarks>
        /// Emptying the Chat and moving on to the next generation happen together, under the lock
        /// that guards the messages, so nothing that is happening in the old Chat at that moment can
        /// land in the new one: a reply, or a Notice about a Request that failed, either gets there
        /// first and is cleared with the old Chat, or does not get there at all.
        /// </remarks>
        public void StartNewChat() {
            lock (_messagesLock) {
                _messages.Clear();
                Interlocked.Increment(ref _generation);
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ShowResponse(int generation, ChatResponse response) {
            if (response == null) {
                TryAppendNotice(generation, Describe(ProviderFailure.Unexpected));
                return;
            }

            ChatFinishReason? finishReason = response.FinishReason;
            string            text         = response.Text;

            if (finishReason.HasValue && !string.IsNullOrEmpty(finishReason.Value.Value) && !finishReason.Value.Equals(ChatFinishReason.Stop)) {
                TryAppendNotice(generation, DescribeFinishReason(finishReason.Value, text));
                return;
            }

            if (string.IsNullOrWhiteSpace(text)) {
                TryAppendNotice(generation, "The Provider returned an empty reply. Try again.");
                return;
            }

            TryAppend(generation, new ChatMessage(ChatMessageAuthor.Ai, text));
        }

        /// <summary>
        /// What the model is sent: the identity message first, then everything the Player and the AI
        /// said. The identity message is not one of the Chat's messages, so the Player never reads it
        /// and New chat leaves it in place.
        /// </summary>
        private IReadOnlyList<AiChatMessage> BuildHistory() {
            var history = new List<AiChatMessage> {
                new AiChatMessage(ChatRole.System, _systemPrompt())
            };

            lock (_messagesLock) {
                history.AddRange(_messages
                                .Where(message => message.Author == ChatMessageAuthor.Player || message.Author == ChatMessageAuthor.Ai)
                                .Select(message => new AiChatMessage(ToRole(message.Author), message.Text)));
            }

            return history;
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
                case ProviderFailure.ToolCallingNotSupported:
                    return "That model cannot call tools, which AI Goal Planner needs. Choose a different model in Provider settings.";
                default:
                    return "Something went wrong while asking the Provider. Try again.";
            }
        }

        /// <summary>
        /// Adds <paramref name="message"/> to the Chat only while <paramref name="generation"/> is
        /// still the Chat the Player is looking at, and says whether it landed.
        /// </summary>
        /// <remarks>
        /// Asking whether it is still the same Chat and adding to it happen under one lock, the same
        /// lock New chat empties the Chat under. That is what keeps a reply from slipping into a Chat
        /// it does not belong to: it either lands first and goes with the old Chat, or it does not
        /// land at all. The Chat window is told outside the lock, so it can read the Chat back.
        /// </remarks>
        private bool TryAppend(int generation, ChatMessage message) {
            lock (_messagesLock) {
                if (generation != Volatile.Read(ref _generation)) return false;

                _messages.Add(message);
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);

            return true;
        }

        private bool TryAppendNotice(int generation, string text) {
            return TryAppend(generation, new ChatMessage(ChatMessageAuthor.Notice, text));
        }
    }
}
