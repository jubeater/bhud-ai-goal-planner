using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class PlanValidatorInstallTests
    {
        private readonly FakeWorld _world;

        public PlanValidatorInstallTests() {
            _world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo(
                        "bh.community.pathing",
                        "Pathing",
                        "3.2.1",
                        "Markers and trails for Guild Wars 2.",
                        new[] { "bh.blishhud" },
                        isPreview: false,
                        apiPermissions: new[] { "account", "characters" }),
                    new CatalogModuleInfo(
                        "bh.addon",
                        "Addon Module",
                        "1.0.0",
                        "An addon requiring Pathing.",
                        new[] { "bh.blishhud", "bh.community.pathing" },
                        isPreview: false)
                }
            };
        }

        [Fact]
        public async Task Valid_install_with_Gap_is_accepted_with_dependencies_and_permissions() {
            var plan = new Plan("Install Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Needed for markers", isSideEffect: false, gap: "Shows markers on map")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.True(action.IsAccepted);
            Assert.True(action.CanApply);
            Assert.Equal("Pathing: install and enable it", action.Summary);
            Assert.Equal("not installed", action.CurrentValue);
            Assert.Equal("installed", action.ProposedValue);
            Assert.Equal("Shows markers on map", action.Gap);
            Assert.Equal(new[] { "account", "characters" }, action.ApiPermissions);
            Assert.Equal("Markers and trails for Guild Wars 2.", action.Description);
            Assert.True(action.PermissionsKnown);
            Assert.Empty(action.Dependencies);
        }

        [Fact]
        public async Task Install_is_accepted_with_unknown_permissions_when_details_cannot_be_fetched() {
            var plan = new Plan("Install Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Needed for markers", isSideEffect: false, gap: "Shows markers on map")
            });

            _world.AnswerGetCatalogModuleDetails = (ns, ct) =>
                Task.FromResult(new CatalogModuleDetails(false, ns, problem: "Hash mismatch."));

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.True(action.IsAccepted);
            Assert.True(action.CanApply);
            Assert.False(action.PermissionsKnown);
            Assert.Empty(action.ApiPermissions);
            Assert.Contains("Hash mismatch", action.DetailsProblem);
        }

        [Fact]
        public async Task Install_with_missing_Gap_is_rejected() {
            var plan = new Plan("Install Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Needed for markers", isSideEffect: false, gap: "")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("does not state the Gap it fills", action.Problem);
        }

        [Fact]
        public async Task Install_with_whitespace_only_Gap_is_rejected() {
            var plan = new Plan("Install Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Needed for markers", isSideEffect: false, gap: "   ")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("does not state the Gap it fills", action.Problem);
        }

        [Fact]
        public async Task Install_with_module_not_in_catalog_is_rejected() {
            var plan = new Plan("Install Unknown", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.nonexistent", null, null,
                               "Needed for something", isSideEffect: false, gap: "Missing feature")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("is not in the module catalog", action.Problem);
        }

        [Fact]
        public async Task Install_missing_module_name_is_rejected() {
            var plan = new Plan("Install None", new[] {
                new PlanAction(PlanActionKind.Install, "install", "", null, null,
                               "Reason", isSideEffect: false, gap: "Missing feature")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("does not say which module to install", action.Problem);
        }

        [Fact]
        public async Task Install_for_already_installed_module_is_rejected() {
            _world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "", enabled: true));

            var plan = new Plan("Install Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Already have it", isSideEffect: false, gap: "Shows markers")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("is already installed", action.Problem);
        }

        [Fact]
        public async Task Same_install_proposed_twice_is_rejected_the_second_time() {
            var plan = new Plan("Install twice", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "First", isSideEffect: false, gap: "Gap one"),
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Second", isSideEffect: false, gap: "Gap two")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            Assert.Equal(2, review.Actions.Count);
            Assert.True(review.Actions[0].IsAccepted);
            Assert.False(review.Actions[1].IsAccepted);
            Assert.Contains("is already changed by another action in this plan", review.Actions[1].Problem);
        }

        [Fact]
        public async Task Install_with_unmet_dependency_is_rejected() {
            // bh.addon requires bh.community.pathing, which is neither installed nor in the plan
            var plan = new Plan("Install Addon", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.addon", null, null,
                               "Need addon", isSideEffect: false, gap: "Addon feature")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("requires dependency \"bh.community.pathing\"", action.Problem);
        }

        [Fact]
        public async Task Install_with_dependency_already_installed_is_accepted() {
            _world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "", enabled: true));

            var plan = new Plan("Install Addon", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.addon", null, null,
                               "Need addon", isSideEffect: false, gap: "Addon feature")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.True(action.IsAccepted);
            Assert.True(action.CanApply);
            Assert.Equal(new[] { "bh.community.pathing" }, action.Dependencies);
        }

        [Fact]
        public async Task Install_with_dependency_in_same_plan_is_accepted() {
            var plan = new Plan("Install Pathing and Addon", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Need pathing", isSideEffect: false, gap: "Pathing gap"),
                new PlanAction(PlanActionKind.Install, "install", "bh.addon", null, null,
                               "Need addon", isSideEffect: false, gap: "Addon gap")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            Assert.Equal(2, review.Actions.Count);
            Assert.True(review.Actions[0].IsAccepted);
            Assert.True(review.Actions[1].IsAccepted);
        }

        [Fact]
        public async Task Installing_by_catalog_name_must_reject_installed_namespace() {
            _world.Modules.Add(new InstalledModuleInfo("bh.dependency", "Old name", "0.9.0", "", enabled: true));
            _world.CatalogModules.Add(new CatalogModuleInfo("bh.dependency", "Dependency", "1.0.0", "A dependency", new[] { "bh.blishhud" }));

            var plan = new Plan("Install Dependency", new[] {
                new PlanAction(PlanActionKind.Install, "install", "Dependency", null, null,
                               "Need dependency", isSideEffect: false, gap: "Missing feature")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            ReviewedAction action = Assert.Single(review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("is already installed", action.Problem);
        }

        [Fact]
        public async Task Dependency_later_in_same_plan_is_accepted() {
            var plan = new Plan("Install Addon before Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.addon", null, null,
                               "Need addon", isSideEffect: false, gap: "Addon gap"),
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Need pathing", isSideEffect: false, gap: "Pathing gap")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, _world, CancellationToken.None);

            Assert.Equal(2, review.Actions.Count);
            Assert.True(review.Actions[0].IsAccepted);
            Assert.True(review.Actions[1].IsAccepted);
        }
    }
}
