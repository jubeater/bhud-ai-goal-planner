using System;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Who said something in a Chat.
    /// </summary>
    public enum ChatMessageAuthor
    {
        Player,
        Ai,

        /// <summary>
        /// The module itself, explaining something the Player needs to know such as a cut-off
        /// reply or a Provider error. Notices are never sent to the model.
        /// </summary>
        Notice
    }

    /// <summary>
    /// One line of a Chat, as the Player sees it in the Chat window.
    /// </summary>
    public sealed class ChatMessage
    {
        public ChatMessage(ChatMessageAuthor author, string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            Author = author;
            Text   = text;
        }

        public ChatMessageAuthor Author { get; }

        public string Text { get; }
    }
}
