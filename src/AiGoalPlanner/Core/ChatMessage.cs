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
        /// The module itself, reporting something it did or could not do — what an Apply changed,
        /// what an Undo restored, that a plan was dismissed. The AI did not do it, so it is not the
        /// AI that says it happened. Like a Notice, the model never sees it; what the model is told
        /// about an Apply is a message of its own.
        /// </summary>
        Report,

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
