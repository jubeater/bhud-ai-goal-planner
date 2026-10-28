using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// Carries out the Actions the Player approved, one at a time, and remembers what it changed so
    /// Undo can put it back.
    /// </summary>
    /// <remarks>
    /// One Action that fails does not stop the rest: the Player approved a list, and the parts of it
    /// that can still be done are done, with what failed reported afterwards. Every write goes
    /// through <see cref="IBlishHudWorld"/>, which is the decorator that hands it to the game thread,
    /// so nothing here touches Blish HUD from the Chat's thread (ADR-0009).
    /// </remarks>
    public sealed class PlanApplier
    {
        private readonly IBlishHudWorld _world;

        public PlanApplier(IBlishHudWorld world) {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>
        /// Applies the Actions at <paramref name="actionIndexes"/>, in the order the Plan has them.
        /// An index that names no Action, one the validator rejected, or an Advice is skipped: the
        /// Review never offered it, and applying it would be applying something the Player did not
        /// approve.
        /// </summary>
        /// <remarks>
        /// A cancellation part-way does not throw away what was already done: the outcomes and their
        /// Undo steps come back in the answer, marked <see cref="AppliedPlan.Cancelled"/>, so the
        /// caller can record and report them before the Request ends (ADR-0010).
        /// </remarks>
        public async Task<AppliedPlan> ApplyAsync(PlanReview review, IEnumerable<int> actionIndexes,
                                                  CancellationToken cancellationToken) {
            if (review == null) throw new ArgumentNullException(nameof(review));

            var outcomes = new List<ApplyOutcome>();
            var undo     = new List<UndoStep>();

            foreach (ReviewedAction action in Chosen(review, actionIndexes)) {
                try {
                    outcomes.Add(await ApplyOneAsync(action, undo, cancellationToken).ConfigureAwait(false));
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    // What is done is done, and the Player can still undo it.
                    return new AppliedPlan(outcomes, undo, cancelled: true);
                }
            }

            return new AppliedPlan(outcomes, undo);
        }

        /// <summary>
        /// Puts back everything <paramref name="applied"/> changed, newest change first, so a Plan
        /// that touched the same Module twice (it cannot, but Undo does not rely on that) ends where
        /// it started.
        /// </summary>
        public async Task<UndoResult> UndoAsync(AppliedPlan applied, CancellationToken cancellationToken) {
            if (applied == null) return UndoResult.Nothing;

            var outcomes = new List<UndoOutcome>(applied.Undo.Count);

            for (int index = applied.Undo.Count - 1; index >= 0; index--) {
                UndoStep step = applied.Undo[index];

                try {
                    outcomes.Add(await UndoTaking(step, cancellationToken).ConfigureAwait(false));
                } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                    throw;
                } catch (Exception exception) {
                    outcomes.Add(new UndoOutcome(step, false, Describe(exception)));
                }
            }

            return new UndoResult(outcomes);
        }

        private static List<ReviewedAction> Chosen(PlanReview review, IEnumerable<int> actionIndexes) {
            var chosen = new List<ReviewedAction>();

            foreach (int index in actionIndexes ?? new int[0]) {
                if (index < 0 || index >= review.Actions.Count) continue;

                ReviewedAction action = review.Actions[index];

                if (!action.CanApply || chosen.Contains(action)) continue;

                chosen.Add(action);
            }

            return chosen;
        }

        private async Task<ApplyOutcome> ApplyOneAsync(ReviewedAction action, List<UndoStep> undo,
                                                       CancellationToken cancellationToken) {
            try {
                switch (action.Action.Kind) {
                    case PlanActionKind.EnableModule:
                    case PlanActionKind.DisableModule:
                        return await SetModuleAsync(action, undo, cancellationToken).ConfigureAwait(false);

                    case PlanActionKind.SetSetting:
                        return await SetSettingAsync(action, undo, cancellationToken).ConfigureAwait(false);

                    case PlanActionKind.DownloadPack:
                        return await DownloadPackAsync(action, undo, cancellationToken).ConfigureAwait(false);

                    default:
                        return new ApplyOutcome(action, false, "this kind of action cannot be applied");
                }
            } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                throw;
            } catch (Exception exception) {
                // The type, not the message: a message can carry a Setting's value back out.
                return new ApplyOutcome(action, false, Describe(exception));
            }
        }

        private async Task<ApplyOutcome> SetModuleAsync(ReviewedAction action, List<UndoStep> undo,
                                                        CancellationToken cancellationToken) {
            bool enable = action.Action.Kind == PlanActionKind.EnableModule;

            ModuleChange change = await _world.SetModuleEnabledAsync(action.ModuleNamespace, enable, cancellationToken)
                                              .ConfigureAwait(false);

            if (!change.Applied) {
                return new ApplyOutcome(action, false, ProblemOr(change.Problem, "the module did not accept the change"));
            }

            // A Module that already had the state asked for is done, but there is nothing to undo.
            if (!change.Changed) return new ApplyOutcome(action, true, changed: false);

            // Undo is the state the Module had before: whatever that was, not the opposite of what
            // was asked for.
            undo.Add(new UndoStep(change.WasEnabled ? PlanActionKind.EnableModule : PlanActionKind.DisableModule,
                                  action.ModuleNamespace, action.ModuleName, null, null, null));

            return new ApplyOutcome(action, true);
        }

        private async Task<ApplyOutcome> SetSettingAsync(ReviewedAction action, List<UndoStep> undo,
                                                         CancellationToken cancellationToken) {
            ModuleSettingChange change = await _world.SetSettingAsync(action.ModuleNamespace, action.SettingKey,
                                                                      action.Action.Value, cancellationToken)
                                                      .ConfigureAwait(false);

            if (!change.Applied) {
                return new ApplyOutcome(action, false, ProblemOr(change.Problem, "the setting did not accept the value"));
            }

            // The Setting already holds that value: nothing to write back, so no Undo step.
            if (!change.Changed) return new ApplyOutcome(action, true, changed: false);

            undo.Add(new UndoStep(PlanActionKind.SetSetting, action.ModuleNamespace, action.ModuleName,
                                  action.SettingKey, action.TargetName, change.PreviousValue));

            return new ApplyOutcome(action, true);
        }

        private async Task<ApplyOutcome> DownloadPackAsync(ReviewedAction action, List<UndoStep> undo,
                                                           CancellationToken cancellationToken) {
            MarkerPackChange change = await _world.DownloadMarkerPackAsync(action.Action.Pack, cancellationToken)
                                                  .ConfigureAwait(false);

            if (!change.Applied) {
                return new ApplyOutcome(action, false, ProblemOr(change.Problem, "the pack could not be downloaded"));
            }

            if (!change.Changed) return new ApplyOutcome(action, true, changed: false);

            undo.Add(new UndoStep(PlanActionKind.DownloadPack, null, null, null, change.PackName, change.FileName));

            return new ApplyOutcome(action, true);
        }

        private async Task<UndoOutcome> UndoTaking(UndoStep step, CancellationToken cancellationToken) {
            if (step.Kind == PlanActionKind.DownloadPack) {
                MarkerPackChange pack = await _world.DeleteMarkerPackAsync(step.Value, cancellationToken)
                                                    .ConfigureAwait(false);

                return new UndoOutcome(step, pack.Applied,
                                       pack.Applied ? null : ProblemOr(pack.Problem, "the marker pack could not be deleted"));
            }

            if (step.Kind == PlanActionKind.SetSetting) {
                ModuleSettingChange change = await _world.SetSettingAsync(step.ModuleNamespace, step.SettingKey, step.Value,
                                                                          cancellationToken)
                                                          .ConfigureAwait(false);

                return new UndoOutcome(step, change.Applied,
                                       change.Applied ? null : ProblemOr(change.Problem, "the setting did not accept the value"));
            }

            ModuleChange module = await _world.SetModuleEnabledAsync(step.ModuleNamespace,
                                                                     step.Kind == PlanActionKind.EnableModule,
                                                                     cancellationToken)
                                              .ConfigureAwait(false);

            return new UndoOutcome(step, module.Applied,
                                   module.Applied ? null : ProblemOr(module.Problem, "the module did not accept the change"));
        }

        private static string ProblemOr(string problem, string fallback) {
            return string.IsNullOrEmpty(problem) ? fallback : problem;
        }

        private static string Describe(Exception exception) {
            return "the module reported an error (" + exception.GetType().Name + ")";
        }
    }
}

