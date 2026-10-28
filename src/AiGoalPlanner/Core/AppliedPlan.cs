using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What came of one Action when it was applied: it happened, or why it did not.
    /// </summary>
    public sealed class ApplyOutcome
    {
        public ApplyOutcome(ReviewedAction action, bool succeeded, string problem = null, bool changed = true) {
            Action    = action ?? throw new ArgumentNullException(nameof(action));
            Succeeded = succeeded;
            Problem   = problem ?? string.Empty;
            Changed   = changed && succeeded;
        }

        public ReviewedAction Action { get; }

        public bool Succeeded { get; }

        /// <summary>
        /// Whether the Action left the Player's configuration different. An Action that asked for a
        /// value already in place succeeded without changing anything, which is worth saying but not
        /// worth an Undo step.
        /// </summary>
        public bool Changed { get; }

        /// <summary>Why the Action did not happen, or empty.</summary>
        public string Problem { get; }
    }

    /// <summary>
    /// One thing Undo has to do to put a Module back the way it was: the state it had, or the value
    /// a Setting had, before an Action in an Applied Plan.
    /// </summary>
    public sealed class UndoStep
    {
        public UndoStep(PlanActionKind kind, string moduleNamespace, string moduleName, string settingKey,
                        string targetName, string value) {
            Kind            = kind;
            ModuleNamespace = moduleNamespace ?? string.Empty;
            ModuleName      = moduleName      ?? string.Empty;
            SettingKey      = settingKey      ?? string.Empty;
            TargetName      = targetName      ?? string.Empty;
            Value           = value           ?? string.Empty;
        }

        /// <summary>
        /// The Action that undoes the change: <see cref="PlanActionKind.EnableModule"/> puts a Module
        /// back to enabled, <see cref="PlanActionKind.DisableModule"/> to disabled, and
        /// <see cref="PlanActionKind.SetSetting"/> writes <see cref="Value"/> back.
        /// </summary>
        public PlanActionKind Kind { get; }

        public string ModuleNamespace { get; }
        public string ModuleName      { get; }
        public string SettingKey      { get; }
        public string TargetName      { get; }

        /// <summary>The value to write back, for a Setting. Empty otherwise.</summary>
        public string Value { get; }

        /// <summary>One line naming the restore, e.g. "Pathing: enable it".</summary>
        public string Summary {
            get {
                switch (Kind) {
                    case PlanActionKind.EnableModule:  return ModuleName + ": enable it";
                    case PlanActionKind.DisableModule: return ModuleName + ": disable it";
                    case PlanActionKind.DownloadPack:
                        string pack = TargetName.Length == 0 ? Value : TargetName;
                        return "Pathing: delete marker pack " + pack;
                    default:
                        string setting = TargetName.Length == 0 ? SettingKey : TargetName;

                        return ModuleName + ": set \"" + setting + "\" back to " + Value;
                }
            }
        }
    }

    /// <summary>
    /// Everything one Apply did: what happened to each Action, and how to put it all back. The Undo
    /// steps are in the order the changes were made, so Undo takes them in reverse.
    /// </summary>
    public sealed class AppliedPlan
    {
        private static readonly IReadOnlyList<ApplyOutcome> NoOutcomes = new ApplyOutcome[0];
        private static readonly IReadOnlyList<UndoStep>     NoSteps    = new UndoStep[0];

        public AppliedPlan(IReadOnlyList<ApplyOutcome> outcomes, IReadOnlyList<UndoStep> undo, bool cancelled = false) {
            Outcomes  = outcomes ?? NoOutcomes;
            Undo      = undo     ?? NoSteps;
            Cancelled = cancelled;
        }

        public IReadOnlyList<ApplyOutcome> Outcomes { get; }

        public IReadOnlyList<UndoStep> Undo { get; }

        /// <summary>
        /// Whether the Player cancelled the Apply part-way. What was applied before that stays
        /// applied, stays recorded and is reported; the Request ends there (ADR-0010).
        /// </summary>
        public bool Cancelled { get; }

        public bool AnyApplied => Outcomes.Any(outcome => outcome.Succeeded);

        /// <summary>
        /// Whether this Apply left the Player's configuration different. An Apply whose every Action
        /// asked for a value already in place changed nothing, and is not worth an Undo record.
        /// </summary>
        public bool AnyChanged => Outcomes.Any(outcome => outcome.Changed);

        public int AppliedCount => Outcomes.Count(outcome => outcome.Succeeded);

        public int FailedCount => Outcomes.Count(outcome => !outcome.Succeeded);

        /// <summary>
        /// What the Chat tells the Player: what was applied, what already was so, and what was not
        /// and why. It names Modules and Settings, never a value the Player did not already see in
        /// the Review.
        /// </summary>
        public string Describe() {
            List<string> changed = Outcomes.Where(outcome => outcome.Changed)
                                           .Select(outcome => outcome.Action.Summary)
                                           .ToList();

            List<string> unchanged = Outcomes.Where(outcome => outcome.Succeeded && !outcome.Changed)
                                             .Select(outcome => outcome.Action.Summary)
                                             .ToList();

            List<ApplyOutcome> failed = Outcomes.Where(outcome => !outcome.Succeeded).ToList();

            var text = new StringBuilder();

            if (changed.Count > 0) {
                text.Append("Applied ").Append(Count(changed.Count, "change")).Append(": ")
                    .Append(string.Join("; ", changed)).Append(".");
            }

            if (unchanged.Count > 0) {
                text.Append(changed.Count == 0 ? "Nothing needed changing: " : " Already so: ")
                    .Append(string.Join("; ", unchanged)).Append(".");
            }

            if (changed.Count == 0 && unchanged.Count == 0) {
                text.Append("Nothing was applied.");
            }

            if (failed.Count > 0) {
                text.Append(changed.Count == 0 && unchanged.Count == 0 ? " " : " Not applied: ")
                    .Append(string.Join("; ", failed.Select(outcome => outcome.Action.Summary + " — " + outcome.Problem)))
                    .Append(".");
            }

            return text.ToString();
        }

        internal static string Count(int number, string noun) {
            return number == 1 ? "1 " + noun : number + " " + noun + "s";
        }
    }
}
