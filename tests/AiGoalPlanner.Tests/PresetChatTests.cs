using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = jubeater.aigoalplanner.Core.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests each Preset the way a Player drives it: the button fills the text box with the
    /// preset's starter Request, the Player sends it, and the Chat works with what this version
    /// has — Installed Modules, Marker Pack downloads and Marker Category directions — to a
    /// working setup in one Chat.
    /// </summary>
    public class PresetChatTests
    {
        [Fact]
        public async Task The_hero_points_preset_ends_in_a_working_setup_in_one_Chat() {
            Preset preset = PresetLibrary.Load().Single(candidate => candidate.Name == "Hero points");

            var model = new ScriptedChatClient();
            model.WillCallTool(GetProgressTool.Name, "call_progress",
                               new Dictionary<string, object> { ["kind"] = "hero_points" });
            model.WillCallTool(ListMarkerPacksTool.Name, "call_packs", new Dictionary<string, object>());
            model.WillCallTool(SubmitPlanTool.Name, "call_plan",
                               Plan("This pack has the hero point routes. I suggest downloading it.",
                                    DownloadAction("Hero Point Trails"),
                                    AdviceAction("In Pathing, turn on Hero Point Trails › Routes.")));

            var world = HeroPointWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync(preset.Request);

            // The flow only used what this version has: progress, Marker Packs and directions.
            // (The counts include the Review re-reading the world after the model's own Lookup.)
            Assert.True(world.ProgressCalls >= 1);
            Assert.True(world.MarkerPacksCalls >= 1);

            PlanReview review = Assert.IsType<PlanReview>(chat.Review);
            Assert.Collection(review.Actions,
                              download => {
                                  Assert.True(download.IsAccepted);
                                  Assert.True(download.CanApply);
                              },
                              advice => {
                                  Assert.True(advice.IsAccepted);
                                  Assert.False(advice.CanApply);
                              });

            world.MarkerCategoriesResult = new MarkerCategoriesResult(
                MarkerCategoriesStatus.Ok,
                mapId: 999,
                mapName: "Test Map",
                categories: new[] {
                    new MarkerCategoryMatch("Hero Point Trails", "Hero Point Trails › Routes",
                                            "hero_trails.routes", shown: false,
                                            markerCount: 2, trailCount: 1)
                });

            model.WillCallTool(FindMarkerCategoriesTool.Name, "call_categories",
                               new Dictionary<string, object> { ["search"] = "hero point routes" });
            model.WillReply("In Pathing, turn on Hero Point Trails › Routes. It is off right now.");

            await chat.ApplyAsync(new[] { 0 });

            Assert.Equal(new[] { "Hero Point Trails" }, world.DownloadedPacks.ToArray());
            Assert.Equal(1, world.MarkerCategoriesCalls);
            Assert.Equal("In Pathing, turn on Hero Point Trails › Routes. It is off right now.",
                         chat.Messages.Last(message => message.Author == ChatMessageAuthor.Ai).Text);

            await chat.UndoAsync();

            Assert.Empty(world.DownloadedPacks);
        }

        [Fact]
        public async Task The_mastery_preset_points_at_what_is_left_using_progress_and_owned_content() {
            Preset preset = PresetLibrary.Load().Single(candidate => candidate.Name == "Mastery points");

            var model = new ScriptedChatClient();
            model.WillCallTool(GetProgressTool.Name, "call_progress",
                               new Dictionary<string, object> { ["kind"] = "mastery" });
            model.WillCallTool(GetAccountAccessTool.Name, "call_access", new Dictionary<string, object>());
            model.WillCallTool(SubmitPlanTool.Name, "call_plan",
                               Plan("Here is what is left.",
                                    AdviceAction("Finish the unfinished tracks the progress check listed.")));

            var world = new FakeWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync(preset.Request);

            Assert.Equal(1, world.ProgressCalls);
            Assert.Equal(1, world.AccountAccessCalls);

            ReviewedAction advice = Assert.Single(chat.Review.Actions);

            Assert.True(advice.IsAccepted);
            Assert.False(advice.CanApply);
            Assert.Empty(world.Writes);
        }

        [Fact]
        public async Task The_xp_preset_enables_what_the_Player_already_has() {
            Preset preset = PresetLibrary.Load().Single(candidate => candidate.Name == "XP");

            var model = new ScriptedChatClient();
            model.WillCallTool(ListInstalledModulesTool.Name, "call_modules", new Dictionary<string, object>());
            model.WillCallTool(SubmitPlanTool.Name, "call_plan",
                               Plan("This one can help, but it is disabled.",
                                    EnableAction("test.xphelper"),
                                    AdviceAction("Turn on its event markers once it is enabled.")));

            var world = XpWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync(preset.Request);

            // The model checked Installed Modules; the Review re-reads them after.
            Assert.True(world.ListCalls >= 1);

            ReviewedAction enable = Assert.Single(chat.Review.Actions, action => action.CanApply);

            Assert.True(enable.IsAccepted);
            Assert.Equal("disabled", enable.CurrentValue);
            Assert.Equal("enabled", enable.ProposedValue);

            await chat.ApplyAsync(new[] { 0 });

            Assert.Contains("enable test.xphelper", world.Writes);
        }

        [Fact]
        public async Task The_hero_points_preset_without_Pathing_advises_a_hand_install() {
            Preset preset = PresetLibrary.Load().Single(candidate => candidate.Name == "Hero points");

            var model = new ScriptedChatClient();
            model.WillCallTool(ListInstalledModulesTool.Name, "call_modules", new Dictionary<string, object>());
            model.WillCallTool(ListMarkerPacksTool.Name, "call_packs", new Dictionary<string, object>());
            model.WillCallTool(SubmitPlanTool.Name, "call_plan",
                               Plan("Pathing is not installed, so there are no trails to show yet.",
                                    AdviceAction("Install Pathing from the Blish HUD module manager, then ask again.")));

            // A fresh profile: only this module, no Pathing, no Marker Packs.
            var world = new FakeWorld {
                LocationResult = new PlayerLocationResult(
                    LocationStatus.Ok,
                    new PlayerLocation("Test Character", 999, "Test Map", 99, "Test Region"))
            };
            var chat = NewChat(model, world);

            await chat.SendAsync(preset.Request);

            ReviewedAction advice = Assert.Single(chat.Review.Actions);

            Assert.True(advice.IsAccepted);
            Assert.False(advice.CanApply);
            Assert.Empty(world.Writes);
        }

        [Fact]
        public async Task The_hero_points_preset_without_Pathing_proposes_install_through_catalog_action() {
            Preset preset = PresetLibrary.Load().Single(candidate => candidate.Name == "Hero points");

            var model = new ScriptedChatClient();
            model.WillCallTool(ListInstalledModulesTool.Name, "call_modules", new Dictionary<string, object>());
            model.WillCallTool(SearchCatalogTool.Name, "call_catalog",
                               new Dictionary<string, object> { ["query"] = "pathing" });
            model.WillCallTool(SubmitPlanTool.Name, "call_plan",
                               Plan("Pathing is not installed. I suggest installing it from the Catalog.",
                                    InstallAction("bh.community.pathing", "Markers and trails for hero point routes", "Needed for markers and trails")));

            var world = new FakeWorld {
                LocationResult = new PlayerLocationResult(
                    LocationStatus.Ok,
                    new PlayerLocation("Test Character", 999, "Test Map", 99, "Test Region")),
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" })
                }
            };
            var chat = NewChat(model, world);

            await chat.SendAsync(preset.Request);

            Assert.True(world.ListCalls >= 1);
            Assert.True(world.CatalogCalls >= 1);

            PlanReview review = Assert.IsType<PlanReview>(chat.Review);
            ReviewedAction install = Assert.Single(review.Actions);
            Assert.Equal(PlanActionKind.Install, install.Action.Kind);
            Assert.True(install.IsAccepted);
            Assert.True(install.CanApply);
            Assert.Equal("bh.community.pathing", install.ModuleNamespace);

            model.WillReply("Pathing has been installed.");
            await chat.ApplyAsync(new[] { 0 });

            Assert.Equal(1, world.InstallCalls);
            Assert.True(world.Modules.Any(m => m.Namespace == "bh.community.pathing"));
        }

        [Fact]
        public async Task The_hero_points_preset_enables_an_installed_but_disabled_Pathing_and_continues() {
            Preset preset = PresetLibrary.Load().Single(candidate => candidate.Name == "Hero points");

            var model = new ScriptedChatClient();
            model.WillCallTool(ListInstalledModulesTool.Name, "call_modules", new Dictionary<string, object>());
            model.WillCallTool(SubmitPlanTool.Name, "call_plan",
                               Plan("Pathing is installed but disabled. I suggest enabling it.",
                                    EnableAction("test.pathing")));

            var world = DisabledPathingWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync(preset.Request);

            ReviewedAction enable = Assert.Single(chat.Review.Actions);

            Assert.True(enable.IsAccepted);
            Assert.True(enable.CanApply);
            Assert.Equal("disabled", enable.CurrentValue);
            Assert.Equal("enabled", enable.ProposedValue);

            model.WillCallTool(GetModuleSettingsTool.Name, "call_settings",
                               new Dictionary<string, object> { ["namespace"] = "test.pathing" });
            model.WillReply("Pathing is enabled now. Here is how it is set up.");

            await chat.ApplyAsync(new[] { 0 });

            Assert.True(world.Modules.Single(module => module.Namespace == "test.pathing").Enabled);
            Assert.True(world.SettingsCalls >= 1);
            Assert.Equal("Pathing is enabled now. Here is how it is set up.",
                         chat.Messages.Last(message => message.Author == ChatMessageAuthor.Ai).Text);
        }

        private static FakeWorld DisabledPathingWorld() {
            var world = new FakeWorld {
                LocationResult = new PlayerLocationResult(
                    LocationStatus.Ok,
                    new PlayerLocation("Test Character", 999, "Test Map", 99, "Test Region"))
            };

            world.Modules.Add(new InstalledModuleInfo("test.pathing", "Pathing", "1.0.0", "Markers and trails.", enabled: false));
            world.SettingsByNamespace["test.pathing"] = FakeWorld.Disabled("Pathing");

            return world;
        }

        private static FakeWorld HeroPointWorld() {
            var world = new FakeWorld {
                LocationResult = new PlayerLocationResult(
                    LocationStatus.Ok,
                    new PlayerLocation("Test Character", 999, "Test Map", 99, "Test Region")),
                HeroPointsProgressResult = new ProgressResult(
                    ProgressStatus.Ok, ProgressKind.HeroPoints,
                    heroChallenges: new HeroChallengeProgress(mapTotal: 8, mapCompleted: 5, regionMissing: 12),
                    characterName: "Test Character", mapId: 999, mapName: "Test Map", regionName: "Test Region"),
                MarkerPacks = {
                    new MarkerPackInfo("Hero Point Trails", "hero_point_trails.taco", "Hero point routes.",
                                       "Hero Points", 12.5, "https://packs.test/hero_point_trails.taco", "1", false)
                }
            };

            world.Modules.Add(new InstalledModuleInfo("test.pathing", "Pathing", "1.0.0", "Markers and trails.", enabled: true));

            return world;
        }

        private static FakeWorld XpWorld() {
            var world = new FakeWorld();

            world.Modules.Add(new InstalledModuleInfo("test.xphelper", "XP Helper", "1.0.0", "Helps with XP.", enabled: false));
            world.SettingsByNamespace["test.xphelper"] = FakeWorld.Disabled("XP Helper");

            return world;
        }

        private static Chat NewChat(ScriptedChatClient model, FakeWorld world) {
            var plans = new Plans(world, () => false);
            var tools = Lookups.For(world);
            plans.AddTo(tools);

            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1")
                                                + "\n\n" + PresetLibrary.SystemPromptSectionFor(PresetLibrary.Load()),
                            tools, null, plans);
        }

        private static IDictionary<string, object> Plan(string reply, params IDictionary<string, object>[] actions) {
            return new Dictionary<string, object> {
                ["reply"]   = reply,
                ["actions"] = actions.Cast<object>().ToList()
            };
        }

        private static IDictionary<string, object> DownloadAction(string pack) {
            return new Dictionary<string, object> {
                ["kind"]           = "download_pack",
                ["pack"]           = pack,
                ["reason"]         = "The routes are in this pack.",
                ["is_side_effect"] = false
            };
        }

        private static IDictionary<string, object> AdviceAction(string reason) {
            return new Dictionary<string, object> {
                ["kind"]           = "advice",
                ["reason"]         = reason,
                ["is_side_effect"] = false
            };
        }

        private static IDictionary<string, object> InstallAction(string @namespace, string gap = "Markers and trails", string reason = "Install Pathing for markers", bool sideEffect = false) {
            return new Dictionary<string, object> {
                ["kind"]           = "install",
                ["module"]         = @namespace,
                ["gap"]            = gap,
                ["reason"]         = reason,
                ["is_side_effect"] = sideEffect
            };
        }

        private static IDictionary<string, object> EnableAction(string @namespace) {
            return new Dictionary<string, object> {
                ["kind"]           = "enable_module",
                ["module"]         = @namespace,
                ["reason"]         = "It helps with this goal.",
                ["is_side_effect"] = false
            };
        }
    }
}
