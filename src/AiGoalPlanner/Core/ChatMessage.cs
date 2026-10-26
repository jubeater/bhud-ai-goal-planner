using System;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Who said something in a Chat.
    /// </summary>
    public enum ChatMessageAuthor
    {
        Player,
        Ai
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
