using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class PlanValidatorMarkerPackTests
    {
        private readonly FakeWorld _world;

        public PlanValidatorMarkerPackTests() {
            _world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "https://example.com/tw.taco", "1", false),
                    new MarkerPackInfo("Fast TacO", "fast_TacO_pack.taco", "Farming", "Solo Farming", 1.5, "https://example.com/fast.taco", "1", true)
                }
            };
        }

        [Fact]
        public async Task Official_uninstalled_pack_is_accepted_with_size_and_restart_note() {
            var plan = new Plan("Download Tekkit", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Needed for routes", isSideEffect: false, pack: "Tekkit's All-In-One")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.True(action.IsAccepted);
            Assert.True(action.CanApply);
            Assert.Equal("Pathing: download Tekkit's All-In-One (47.1 MB, Pathing will restart)", action.Summary);
        }

        [Fact]
        public async Task Unofficial_pack_is_rejected() {
            var plan = new Plan("Download Unknown", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Needed for routes", isSideEffect: false, pack: "Shady Hacker Pack")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("not on Pathing's official marker pack list", action.Problem);
        }

        [Fact]
        public async Task Missing_pack_name_is_rejected() {
            var plan = new Plan("Download Missing", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Needed for routes", isSideEffect: false, pack: "")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.Contains("does not say which marker pack", action.Problem);
        }

        [Fact]
        public async Task Already_installed_pack_is_rejected() {
            var plan = new Plan("Download Fast", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Farming", isSideEffect: false, pack: "Fast TacO")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.Contains("already installed", action.Problem);
        }

        [Fact]
        public async Task Duplicate_pack_in_same_plan_is_rejected() {
            var plan = new Plan("Download Duplicate", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "First", isSideEffect: false, pack: "Tekkit's All-In-One"),
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Second", isSideEffect: false, pack: "Tekkit's All-In-One")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            Assert.Equal(2, review.Actions.Count);
            Assert.True(review.Actions[0].IsAccepted);
            Assert.False(review.Actions[1].IsAccepted);
            Assert.Contains("already changed by another action", review.Actions[1].Problem);
        }

        [Fact]
        public async Task PlanApplier_applies_download_and_records_undo_step() {
            var plan = new Plan("Download Tekkit", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Needed", isSideEffect: false, pack: "Tekkit's All-In-One")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);
            var applier = new PlanApplier(_world);

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0 }, CancellationToken.None);

            Assert.True(applied.AnyApplied);
            Assert.Single(_world.DownloadedPacks);
            Assert.Equal("Tekkit's All-In-One", _world.DownloadedPacks[0]);

            UndoStep step = Assert.Single(applied.Undo);
            Assert.Equal(PlanActionKind.DownloadPack, step.Kind);
            Assert.Equal("tw_ALL_IN_ONE.taco", step.Value);
            Assert.Equal("Pathing: delete marker pack Tekkit's All-In-One", step.Summary);

            // Undo
            UndoResult undoResult = await applier.UndoAsync(applied, CancellationToken.None);
            Assert.Equal(1, undoResult.RestoredCount);
            Assert.Empty(_world.DownloadedPacks);
        }
    }
}
