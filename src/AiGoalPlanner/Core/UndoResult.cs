using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// What came of one Undo step: the Module or Setting is back, or why it is not.
    /// </summary>
    public sealed class UndoOutcome
    {
        public UndoOutcome(UndoStep step, bool succeeded, string problem = null) {
            Step      = step ?? throw new ArgumentNullException(nameof(step));
            Succeeded = succeeded;
            Problem   = problem ?? string.Empty;
        }

        public UndoStep Step { get; }

        public bool Succeeded { get; }

        public string Problem { get; }
    }

    /// <summary>
    /// Everything one Undo did. An Undo that found nothing to undo — a fresh Chat, or a journal that
    /// has been worked through — says so rather than pretending something happened.
    /// </summary>
    public sealed class UndoResult
    {
        private static readonly IReadOnlyList<UndoOutcome> NoOutcomes = new UndoOutcome[0];

        /// <summary>The answer when there was nothing left in the journal.</summary>
        public static readonly UndoResult Nothing = new UndoResult(null, hadRecord: false);

        public UndoResult(IReadOnlyList<UndoOutcome> outcomes) : this(outcomes, hadRecord: true) { }

        private UndoResult(IReadOnlyList<UndoOutcome> outcomes, bool hadRecord) {
            Outcomes  = outcomes ?? NoOutcomes;
            HadRecord = hadRecord;
        }

        public IReadOnlyList<UndoOutcome> Outcomes { get; }

        /// <summary>Whether there was an Apply to undo at all.</summary>
        public bool HadRecord { get; }

        public int RestoredCount => Outcomes.Count(outcome => outcome.Succeeded);

        /// <summary>What the Chat tells the Player about the Undo.</summary>
        public string Describe() {
            if (!HadRecord) return "There is nothing to undo.";

            List<string> restored = Outcomes.Where(outcome => outcome.Succeeded)
                                            .Select(outcome => outcome.Step.Summary)
                                            .ToList();

            List<UndoOutcome> failed = Outcomes.Where(outcome => !outcome.Succeeded).ToList();

            var text = new StringBuilder();

            if (restored.Count == 0) {
                text.Append("Nothing was restored.");
            } else {
                text.Append("Undid ").Append(AppliedPlan.Count(restored.Count, "change")).Append(": ")
                    .Append(string.Join("; ", restored)).Append(".");
            }

            if (failed.Count > 0) {
                text.Append(restored.Count == 0 ? " " : " Could not undo: ")
                    .Append(string.Join("; ", failed.Select(outcome => outcome.Step.Summary + " — " + outcome.Problem)))
                    .Append(".");
            }

            return text.ToString();
        }
    }
}
