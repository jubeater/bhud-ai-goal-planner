using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// One Chat: the conversation between the Player and the AI, from "New chat" until the next one.
    /// </summary>
    /// <remarks>
    /// This is the planning core's public seam. The Chat window and, from ticket #3 on, the Provider
    /// layer talk to a Chat only through this class, which keeps it free of Blish HUD types.
    /// </remarks>
    public sealed class Chat
    {
        private readonly List<ChatMessage> _messages     = new List<ChatMessage>();
        private readonly object            _messagesLock = new object();

        /// <summary>
        /// Raised whenever what the Player sees in the Chat changes, so the Chat window can redraw.
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
        /// Until the Provider layer lands, the AI's answer is the Request echoed back.
        /// </remarks>
        public Task SendAsync(string request, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(request)) return Task.CompletedTask;

            string trimmedRequest = request.Trim();

            Append(new ChatMessage(ChatMessageAuthor.Player, trimmedRequest));
            Append(new ChatMessage(ChatMessageAuthor.Ai,     trimmedRequest));

            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts a new Chat: what the Player said before is cleared, and the next Request starts from scratch.
        /// </summary>
        public void StartNewChat() {
            lock (_messagesLock) {
                _messages.Clear();
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Append(ChatMessage message) {
            lock (_messagesLock) {
                _messages.Add(message);
            }

            MessagesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
