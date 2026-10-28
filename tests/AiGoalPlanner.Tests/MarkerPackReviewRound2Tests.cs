using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class MarkerPackReviewRound2Tests
    {
        private static async Task<PlanReview> CreateReviewAsync(FakeWorld world, string packName) {
            var plan = new Plan("Download", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Needed for markers", isSideEffect: false, pack: packName)
            });
            return await PlanValidator.ReviewAsync(plan, world, CancellationToken.None);
        }

        [Fact]
        public async Task Cancel_after_file_commit_keeps_the_pack_in_Undo() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            var cts = new CancellationTokenSource();
            world.AnswerDownloadMarkerPack = (pack, ct) => {
                world.DownloadedPacks.Add(pack);
                cts.Cancel();
                return Task.FromResult(new MarkerPackChange(true, true, pack, "tw_ALL_IN_ONE.taco", 47.1));
            };

            var target = new GameThreadWorld(world, new ImmediateGameThread());
            var applier = new PlanApplier(target);
            PlanReview review = await CreateReviewAsync(world, "Tekkit's All-In-One");

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0 }, cts.Token);

            Assert.Single(world.DownloadedPacks);
            Assert.True(applied.Cancelled);
            Assert.Single(applied.Undo);
            Assert.Equal("tw_ALL_IN_ONE.taco", applied.Undo[0].Value);
        }

        [Fact]
        public async Task Cancel_between_restart_writes_restores_enabled_Pathing() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            var cts = new CancellationTokenSource();
            world.AnswerSetModuleEnabled = (@namespace, enabled) => {
                InstalledModuleInfo mod = world.Modules.First(m => m.Namespace == @namespace);
                world.Modules.Remove(mod);
                world.Modules.Add(new InstalledModuleInfo(mod.Namespace, mod.Name, mod.Version, mod.Description, enabled));
                if (!enabled) {
                    cts.Cancel();
                }
                return new ModuleChange(true, true);
            };

            var target = new GameThreadWorld(world, new ImmediateGameThread());
            var applier = new PlanApplier(target);
            PlanReview review = await CreateReviewAsync(world, "Tekkit's All-In-One");

            await applier.ApplyAsync(review, new[] { 0 }, cts.Token);

            Assert.True(world.Modules.First(m => m.Name == "Pathing").Enabled);
        }

        [Fact]
        public async Task Failed_Pathing_restart_is_reported_in_the_apply_outcome() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            world.AnswerSetModuleEnabled = (@namespace, enabled) => {
                InstalledModuleInfo mod = world.Modules.First(m => m.Namespace == @namespace);
                if (!enabled) {
                    world.Modules.Remove(mod);
                    world.Modules.Add(new InstalledModuleInfo(mod.Namespace, mod.Name, mod.Version, mod.Description, false));
                    return new ModuleChange(true, true);
                }
                return new ModuleChange(false, false, "Pathing crashed on reload");
            };

            var target = new GameThreadWorld(world, new ImmediateGameThread());
            var applier = new PlanApplier(target);
            PlanReview review = await CreateReviewAsync(world, "Tekkit's All-In-One");

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0 }, CancellationToken.None);

            Assert.Single(world.DownloadedPacks);
            Assert.False(world.Modules.First(m => m.Name == "Pathing").Enabled);
            Assert.Single(applied.Undo);
            ApplyOutcome outcome = Assert.Single(applied.Outcomes);
            Assert.False(string.IsNullOrEmpty(outcome.Problem));
            Assert.Contains("Pathing crashed on reload", outcome.Problem);

            string report = applied.Describe();
            Assert.Contains("Pathing crashed on reload", report);
        }

        [Fact]
        public async Task Undo_pack_delete_restores_enabled_Pathing_even_when_cancelled_between_writes() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            var target = new GameThreadWorld(world, new ImmediateGameThread());
            var applier = new PlanApplier(target);
            PlanReview review = await CreateReviewAsync(world, "Tekkit's All-In-One");
            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0 }, CancellationToken.None);

            var cts = new CancellationTokenSource();
            world.AnswerSetModuleEnabled = (@namespace, enabled) => {
                InstalledModuleInfo mod = world.Modules.First(m => m.Namespace == @namespace);
                world.Modules.Remove(mod);
                world.Modules.Add(new InstalledModuleInfo(mod.Namespace, mod.Name, mod.Version, mod.Description, enabled));
                if (!enabled) {
                    cts.Cancel();
                }
                return new ModuleChange(true, true);
            };

            await applier.UndoAsync(applied, cts.Token);

            Assert.True(world.Modules.First(m => m.Name == "Pathing").Enabled);
        }

        [Fact]
        public async Task Failed_Pathing_restart_during_undo_pack_delete_is_reported() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            var target = new GameThreadWorld(world, new ImmediateGameThread());
            var applier = new PlanApplier(target);
            PlanReview review = await CreateReviewAsync(world, "Tekkit's All-In-One");
            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0 }, CancellationToken.None);

            world.AnswerSetModuleEnabled = (@namespace, enabled) => {
                InstalledModuleInfo mod = world.Modules.First(m => m.Namespace == @namespace);
                if (!enabled) {
                    world.Modules.Remove(mod);
                    world.Modules.Add(new InstalledModuleInfo(mod.Namespace, mod.Name, mod.Version, mod.Description, false));
                    return new ModuleChange(true, true);
                }
                return new ModuleChange(false, false, "Pathing crashed on reload during undo");
            };

            UndoResult result = await applier.UndoAsync(applied, CancellationToken.None);

            Assert.False(world.Modules.First(m => m.Name == "Pathing").Enabled);
            UndoOutcome outcome = Assert.Single(result.Outcomes);
            Assert.False(outcome.Succeeded);
            Assert.False(string.IsNullOrEmpty(outcome.Problem));
            Assert.Contains("Pathing crashed on reload during undo", outcome.Problem);

            string report = result.Describe();
            Assert.Contains("Pathing crashed on reload during undo", report);
        }
    }
}
