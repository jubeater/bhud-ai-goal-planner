using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The Plan pipeline as the Chat sees it, in one place: the <c>submit_plan</c> proposal the model
    /// is offered, the validator that decides what is possible, the Applier that carries the approved
    /// Actions out, and the Undo journal that can put them back.
    /// </summary>
    /// <remarks>
    /// One of these belongs to the module, not to a Chat: Undo is about the Player's configuration,
    /// which outlives a conversation, while the conversation (and its history) is the Chat's own.
    /// Everything that touches Blish HUD goes through <paramref name="world"/>, which is the
    /// decorator that hands reads and writes to the game thread (ADR-0009).
    /// </remarks>
    public sealed class Plans
    {
        private readonly IBlishHudWorld  _world;
        private readonly PlanApplier     _applier;
        private readonly UndoJournal     _journal = new UndoJournal();
        private readonly Func<bool>      _autoApply;
        private readonly Action<string>  _logDebug;

        /// <summary>
        /// Builds the pipeline against <paramref name="world"/>.
        /// <paramref name="autoApply"/> says whether the Player turned Auto-apply on; without it
        /// Auto-apply is off, which is how ADR-0005 wants it. <paramref name="logDebug"/> receives
        /// one line per Apply and per Undo, with counts and no values.
        /// </summary>
        public Plans(IBlishHudWorld world, Func<bool> autoApply = null, Action<string> logDebug = null) {
            _world     = world ?? throw new ArgumentNullException(nameof(world));
            _applier   = new PlanApplier(_world);
            _autoApply = autoApply;
            _logDebug  = logDebug;
        }

        /// <summary>
        /// Whether Setting changes may be applied without Review. Installs and Side Effects are
        /// never auto-applied (ADR-0005), and this module has none of the former yet.
        /// </summary>
        public bool AutoApplies => _autoApply != null && _autoApply();

        /// <summary>
        /// Adds <c>submit_plan</c> to the tools the model is offered, as the one proposal among the
        /// read-only Lookups (ADR-0003).
        /// </summary>
        public void AddTo(ToolRegistry tools) {
            if (tools == null) throw new ArgumentNullException(nameof(tools));

            tools.Register(ToolAccess.Proposal, SubmitPlanTool.Create(), SubmitPlanTool.Progress);
        }

        /// <summary>
        /// Checks a Plan against the Player's real setup, Action by Action, so the Review can show
        /// what would happen and what cannot.
        /// </summary>
        public Task<PlanReview> ReviewAsync(Plan plan, CancellationToken cancellationToken) {
            return PlanValidator.ReviewAsync(plan, _world, cancellationToken);
        }

        /// <summary>
        /// Applies the Actions at <paramref name="actionIndexes"/> and remembers how to undo them.
        /// </summary>
        public async Task<AppliedPlan> ApplyAsync(PlanReview review, IEnumerable<int> actionIndexes,
                                                   CancellationToken cancellationToken) {
            AppliedPlan applied = await _applier.ApplyAsync(review, actionIndexes, cancellationToken).ConfigureAwait(false);

            _journal.Record(applied);
            Log("Plan applied: " + applied.AppliedCount + " of " + applied.Outcomes.Count + " actions, "
                  + applied.FailedCount + " failed");

            return applied;
        }

        /// <summary>Whether an Apply is waiting to be undone.</summary>
        public bool CanUndo => _journal.Count > 0;

        /// <summary>
        /// Puts the most recent Apply back the way it was. The record leaves the journal either way,
        /// so a failed Undo cannot be retried into an ever-growing list of attempts; what failed is
        /// reported instead.
        /// </summary>
        public async Task<UndoResult> UndoAsync(CancellationToken cancellationToken) {
            AppliedPlan last = _journal.Take();

            if (last == null) return UndoResult.Nothing;

            UndoResult result = await _applier.UndoAsync(last, cancellationToken).ConfigureAwait(false);

            Log("Undo: " + result.RestoredCount + " of " + result.Outcomes.Count + " changes restored");

            return result;
        }

        private void Log(string line) {
            _logDebug?.Invoke(line);
        }
    }
}
