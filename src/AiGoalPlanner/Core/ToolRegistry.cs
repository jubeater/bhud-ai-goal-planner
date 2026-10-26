using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.AI;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What a tool is allowed to do, per ADR-0003: a read-only Lookup, or a proposal. Only a
    /// proposal tool delivers a Plan, and a Plan changes nothing until the Player applies it.
    /// </summary>
    public enum ToolAccess
    {
        /// <summary>The tool only reads. The agent loop runs it whenever the model asks.</summary>
        ReadOnly,

        /// <summary>
        /// The tool proposes. The agent loop does not run it; it ends the Request instead, and the
        /// proposal goes where the Review flow takes it.
        /// </summary>
        Proposal
    }

    /// <summary>
    /// One tool in the registry: what the model may call, what it is allowed to do, and what the
    /// Chat shows the Player while it runs.
    /// </summary>
    public sealed class RegisteredTool
    {
        private readonly Func<IDictionary<string, object>, string> _progressText;

        public RegisteredTool(ToolAccess access, AIFunction function,
                              Func<IDictionary<string, object>, string> progressText) {
            Access        = access;
            Function      = function ?? throw new ArgumentNullException(nameof(function));
            _progressText = progressText ?? throw new ArgumentNullException(nameof(progressText));
        }

        public ToolAccess Access { get; }

        public AIFunction Function { get; }

        /// <summary>
        /// The progress line for one call, e.g. "Checking installed modules…". May name what the
        /// call asked for; it must make sense even with no arguments at all.
        /// </summary>
        public string ProgressText(IDictionary<string, object> arguments) {
            return _progressText(arguments ?? new Dictionary<string, object>());
        }
    }

    /// <summary>
    /// Every tool the AI can call, each labelled read-only or proposal (ADR-0003). The agent loop
    /// offers <see cref="Declarations"/> to the model and runs only what the labels allow.
    /// </summary>
    public sealed class ToolRegistry
    {
        private readonly List<RegisteredTool> _tools = new List<RegisteredTool>();

        /// <summary>
        /// Adds a tool. Two tools under one name is a programming error, not a quiet replacement:
        /// the model would call one and the registry would run the other.
        /// </summary>
        public void Register(ToolAccess access, AIFunction function,
                             Func<IDictionary<string, object>, string> progressText) {
            if (function == null) throw new ArgumentNullException(nameof(function));

            if (_tools.Any(tool => string.Equals(tool.Function.Name, function.Name, StringComparison.OrdinalIgnoreCase))) {
                throw new ArgumentException("A tool named " + function.Name + " is already registered.", nameof(function));
            }

            _tools.Add(new RegisteredTool(access, function, progressText));
        }

        /// <summary>
        /// The tools as the model is offered them.
        /// </summary>
        public IReadOnlyList<AIFunctionDeclaration> Declarations {
            get { return _tools.Select(tool => (AIFunctionDeclaration)tool.Function).ToList(); }
        }

        /// <summary>
        /// Finds the tool a call names, however the model spelled it.
        /// </summary>
        public bool TryGet(string name, out RegisteredTool tool) {
            tool = _tools.FirstOrDefault(candidate =>
                                             string.Equals(candidate.Function.Name, name, StringComparison.OrdinalIgnoreCase));

            return tool != null;
        }

        /// <summary>
        /// Whether the tool a call names is a proposal: one the loop must deliver rather than run.
        /// A name no tool has is not a proposal; it gets an error result instead.
        /// </summary>
        public bool IsProposal(string name) {
            return TryGet(name, out RegisteredTool tool) && tool.Access == ToolAccess.Proposal;
        }
    }
}
