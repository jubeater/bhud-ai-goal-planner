using System;
using System.Collections.Generic;
using System.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// One Action of a Plan as the Review shows it: what it would change, from what to what, why,
    /// and — when the validator would not let it through — the reason it cannot be carried out.
    /// </summary>
    public sealed class ReviewedAction
    {
        private static readonly IReadOnlyList<string> NoStrings = new string[0];
        public ReviewedAction(PlanAction action, string moduleNamespace, string moduleName, string settingKey,
                              string targetName, string currentValue, string proposedValue, string problem, IReadOnlyList<string> dependencies = null, IReadOnlyList<string> apiPermissions = null, string description = null, bool permissionsKnown = true, string detailsProblem = null) {
            Action          = action ?? throw new ArgumentNullException(nameof(action));
            ModuleNamespace = moduleNamespace ?? string.Empty;
            ModuleName      = moduleName      ?? string.Empty;
            SettingKey      = settingKey      ?? string.Empty;
            TargetName      = targetName      ?? string.Empty;
            CurrentValue    = currentValue    ?? string.Empty;
            ProposedValue   = proposedValue   ?? string.Empty;
            Problem         = problem         ?? string.Empty;
            Dependencies    = dependencies    ?? NoStrings;
            ApiPermissions   = apiPermissions  ?? NoStrings;
            Description      = description     ?? string.Empty;
            PermissionsKnown = permissionsKnown;
            DetailsProblem   = detailsProblem  ?? string.Empty;
            Summary         = Describe(action, ModuleName, TargetName, ProposedValue);
        }

        public PlanAction Action { get; }

        /// <summary>
        /// The namespace of the Module the Action was understood to mean, or empty when there is no
        /// such Module. Applying uses this, never the spelling the model chose.
        /// </summary>
        public string ModuleNamespace { get; }

        /// <summary>The name the Player knows the Module by, for the Review and the Chat.</summary>
        public string ModuleName { get; }

        /// <summary>
        /// The Setting's key as its own Module spells it, or empty when the Action does not change a
        /// Setting. Applying uses this, so a model that gets the spelling or the case wrong still
        /// changes the Setting it meant — or is told there is no such key.
        /// </summary>
        public string SettingKey { get; }

        /// <summary>The Setting's label, or empty when the Action does not change a Setting.</summary>
        public string TargetName { get; }

        /// <summary>The value now: a Module's state ("enabled"/"disabled") or a Setting's value.</summary>
        public string CurrentValue { get; }

        /// <summary>The value the Action would give it.</summary>
        public string ProposedValue { get; }

        /// <summary>Why the Action cannot be applied, or empty when it can.</summary>
        public string Problem { get; }

        /// <summary>Whether the validator let the Action through.</summary>
        public IReadOnlyList<string> Dependencies   { get; }
        public IReadOnlyList<string> ApiPermissions   { get; }
        public string                Description      { get; }
        public bool                  PermissionsKnown { get; }
        public string                DetailsProblem   { get; }
        public string                Gap            => Action.Gap;

        public bool IsAccepted => Problem.Length == 0;

        /// <summary>
        /// Whether Apply has anything to do for this Action: it was accepted and it changes
        /// something, which an Advice does not.
        /// </summary>
        public bool CanApply => IsAccepted && Action.Kind != PlanActionKind.Advice && Action.Kind != PlanActionKind.Unknown;

        /// <summary>One line naming the change, e.g. "Pathing: set &quot;Show trails&quot; to false".</summary>
        public string Summary { get; }

        private static string Describe(PlanAction action, string moduleName, string targetName, string proposedValue) {
            string subject = moduleName.Length == 0 ? action.Module : moduleName;

            switch (action.Kind) {
                case PlanActionKind.Install:       return subject + ": install and enable it";
                case PlanActionKind.EnableModule:  return subject + ": enable it";
                case PlanActionKind.DisableModule: return subject + ": disable it";
                case PlanActionKind.SetSetting:
                    string setting = targetName.Length == 0 ? action.Key : targetName;

                    return subject + ": set \"" + setting + "\" to " + proposedValue;
                case PlanActionKind.DownloadPack:
                    string packTitle = targetName.Length == 0 ? action.Pack : targetName;
                    string sizeInfo = proposedValue.Length == 0 ? string.Empty : proposedValue + ", ";

                    return "Pathing: download " + packTitle + " (" + sizeInfo + "Pathing will restart)";
                case PlanActionKind.Advice:        return action.Reason;
                default:                           return subject + ": " + action.KindText;
            }
        }
    }

    /// <summary>
    /// A Plan as the Chat offers it for Review: its reply, and every Action with what the validator
    /// made of it. Rejected Actions stay in the list, with the reason, so the Player sees what the
    /// AI proposed and why it cannot be done.
    /// </summary>
    public sealed class PlanReview
    {
        private static readonly IReadOnlyList<ReviewedAction> NoActions = new ReviewedAction[0];

        public PlanReview(Plan plan, IReadOnlyList<ReviewedAction> actions) {
            Plan    = plan ?? throw new ArgumentNullException(nameof(plan));
            Actions = actions ?? NoActions;
        }

        public Plan Plan { get; }

        public IReadOnlyList<ReviewedAction> Actions { get; }

        /// <summary>Whether there is anything left for the Player to approve.</summary>
        public bool HasSomethingToApply => Actions.Any(action => action.CanApply);

        /// <summary>Whether the Review has anything to show at all.</summary>
        public bool IsEmpty => Actions.Count == 0;

        /// <summary>
        /// The same Review without the Actions at <paramref name="indexes"/>, for what is left
        /// after Auto-apply has done its part.
        /// </summary>
        public PlanReview Without(IEnumerable<int> indexes) {
            var removed = new HashSet<int>(indexes ?? new int[0]);

            return new PlanReview(Plan, Actions.Where((action, index) => !removed.Contains(index)).ToList());
        }
    }
}
