using System;
using System.Collections.Generic;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Who said something in a Chat.
    /// </summary>
    /// <summary>
    /// Information about a module that can be explicitly removed after its installation was undone.
    /// </summary>
    public sealed class RemovableModuleInfo
    {
        public RemovableModuleInfo(string @namespace, string name) {
            Namespace = @namespace ?? string.Empty;
            Name      = name ?? string.Empty;
        }

        public string Namespace { get; }
        public string Name      { get; }
    }

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
        private static readonly IReadOnlyList<RemovableModuleInfo> NoRemovable = new RemovableModuleInfo[0];

        public ChatMessage(ChatMessageAuthor author, string text, TokenUsage usage = null, decimal? estimatedCostUsd = null,
                           IReadOnlyList<RemovableModuleInfo> removableModules = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            Author            = author;
            Text              = text;
            Usage             = usage;
            EstimatedCostUsd  = estimatedCostUsd;
            RemovableModules  = removableModules ?? NoRemovable;
        }

        public ChatMessageAuthor Author { get; }

        public string Text { get; }

        public IReadOnlyList<RemovableModuleInfo> RemovableModules { get; }

        public bool HasRemoveButton => RemovableModules != null && RemovableModules.Count > 0;

        /// <summary>
        /// The tokens the reply behind this message used — the whole Request's, across its tool
        /// rounds — or null when the Provider reported no usage. Only AI replies carry one.
        /// </summary>
        public TokenUsage Usage { get; }

        /// <summary>
        /// What <see cref="Usage"/> cost at the prices the Player entered, or null when no prices
        /// were entered for it.
        /// </summary>
        public decimal? EstimatedCostUsd { get; }
    }
}
