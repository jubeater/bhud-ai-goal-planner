using System;
using System.Collections.Generic;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What one Action of a Plan does. Only these kinds exist, so a model that invents one — or
    /// spells a known one differently — is kept as <see cref="Unknown"/> and the Review can say
    /// why nothing came of it.
    /// </summary>
    public enum PlanActionKind
    {
        Unknown,
        EnableModule,
        DisableModule,
        SetSetting,
        DownloadPack,
        Advice
    }

    /// <summary>
    /// One Action of a Plan: what to change, why, and whether it is a Side Effect — something the
    /// Player did not ask for but the Request needs (ADR-0005).
    /// </summary>
    /// <remarks>
    /// Each kind uses only some of the fields: <c>enable_module</c> and <c>disable_module</c> name a
    /// Module, <c>set_setting</c> names a Module, a key and a value, and <c>advice</c> names
    /// nothing. The fields a kind does not use are never read, so a model that adds a stray
    /// <c>"key": "enabled"</c> to an <c>enable_module</c> cannot have it applied as a Setting.
    /// </remarks>
    public sealed class PlanAction
    {
        public PlanAction(PlanActionKind kind, string kindText, string module, string key, string value,
                          string reason, bool isSideEffect, string pack = null) {
            Kind         = kind;
            KindText     = kindText ?? string.Empty;
            Module       = module   ?? string.Empty;
            Key          = key      ?? string.Empty;
            Value        = value    ?? string.Empty;
            Reason       = reason   ?? string.Empty;
            IsSideEffect = isSideEffect;
            Pack         = pack     ?? string.Empty;
        }

        public PlanActionKind Kind { get; }

        /// <summary>The kind as the model named it, kept for the Review's explanation.</summary>
        public string KindText { get; }

        /// <summary>
        /// The Module the Action names, as the model wrote it: a namespace, or the name the Player
        /// knows the Module by. Empty for an Advice.
        /// </summary>
        public string Module { get; }

        /// <summary>The Setting's key, for <see cref="PlanActionKind.SetSetting"/>. Empty otherwise.</summary>
        public string Key { get; }

        /// <summary>The value the Setting should have, as text. Empty unless it is a Setting change.</summary>
        public string Value { get; }

        /// <summary>Why the AI proposes this. The Player reads it in the Review.</summary>
        public string Reason { get; }

        /// <summary>Whether the Player did not ask for this change but the Request needs it.</summary>
        public bool IsSideEffect { get; }

        /// <summary>The Marker Pack name or file name, for <see cref="PlanActionKind.DownloadPack"/>. Empty otherwise.</summary>
        public string Pack { get; }
    }

    /// <summary>
    /// The AI's answer to a Request: a reply to the Player plus zero or more Actions (ADR-0003).
    /// Nothing in it has happened yet; a Plan only takes effect through Review and Apply.
    /// </summary>
    public sealed class Plan
    {
        private static readonly IReadOnlyList<PlanAction> NoActions = new PlanAction[0];

        public Plan(string reply, IReadOnlyList<PlanAction> actions) {
            Reply   = reply ?? string.Empty;
            Actions = actions ?? NoActions;
        }

        /// <summary>What the AI says to the Player about the Plan.</summary>
        public string Reply { get; }

        public IReadOnlyList<PlanAction> Actions { get; }
    }
}
