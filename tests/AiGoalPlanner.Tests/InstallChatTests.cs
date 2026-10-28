using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = jubeater.aigoalplanner.Core.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    public class InstallChatTests
    {
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world, bool autoApply = false) {
            IBlishHudWorld target = new GameThreadWorld(world, new ImmediateGameThread());
            var plans = new Plans(target, () => autoApply);
            var tools = Lookups.For(target);
            plans.AddTo(tools);

            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            tools, null, plans);
        }

        private static IDictionary<string, object> Plan(string reply, params IDictionary<string, object>[] actions) {
            return new Dictionary<string, object> {
                ["reply"]   = reply,
                ["actions"] = actions.Cast<object>().ToList()
            };
        }

        private static IDictionary<string, object> InstallAction(string module, string gap, string reason = "Needed for markers", bool sideEffect = false) {
            return new Dictionary<string, object> {
                ["kind"]           = "install",
                ["module"]         = module,
                ["gap"]            = gap,
                ["reason"]         = reason,
                ["is_side_effect"] = sideEffect
            };
        }

        private static IDictionary<string, object> EnableAction(string module, string reason = "Enable module", bool sideEffect = false) {
            return new Dictionary<string, object> {
                ["kind"]           = "enable_module",
                ["module"]         = module,
                ["reason"]         = reason,
                ["is_side_effect"] = sideEffect
            };
        }

        private static IDictionary<string, object> SettingAction(string module, string key, string value, string reason = "Configure setting", bool sideEffect = false) {
            return new Dictionary<string, object> {
                ["kind"]           = "set_setting",
                ["module"]         = module,
                ["key"]            = key,
                ["value"]          = value,
                ["reason"]         = reason,
                ["is_side_effect"] = sideEffect
            };
        }

        [Fact]
        public async Task Auto_apply_never_installs_module_and_review_shows_gap_and_dependencies() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo(
                        "bh.community.pathing",
                        "Pathing",
                        "3.2.1",
                        "Markers and trails for Guild Wars 2.",
                        new[] { "bh.blishhud" },
                        isPreview: false,
                        apiPermissions: new[] { "account", "characters" })
                }
            };

            model.WillCallTool("submit_plan", "call_1",
                               Plan("I suggest installing Pathing.",
                                    InstallAction("bh.community.pathing", "Markers and trails on map",
                                                  reason: "Pathing provides trails", sideEffect: false)));

            // Auto-apply is ON
            var chat = NewChat(model, world, autoApply: true);
            await chat.SendAsync("Show me marker routes");

            // Module was NOT installed automatically by Auto-apply
            Assert.Equal(0, world.InstallCalls);

            // Review is open and waiting for Player approval
            Assert.NotNull(chat.Review);
            ReviewedAction action = Assert.Single(chat.Review.Actions);
            Assert.True(action.IsAccepted);
            Assert.True(action.CanApply);
            Assert.Equal("Pathing: install and enable it", action.Summary);
            Assert.Equal("Markers and trails on map", action.Gap);
            Assert.Equal(new[] { "account", "characters" }, action.ApiPermissions);
        }

        [Fact]
        public async Task Missing_gap_is_rejected_in_chat() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" })
                }
            };

            model.WillCallTool("submit_plan", "call_1",
                               Plan("I propose installing Pathing.",
                                    InstallAction("bh.community.pathing", gap: "")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Install pathing");

            Assert.NotNull(chat.Review);
            ReviewedAction action = Assert.Single(chat.Review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("does not state the Gap it fills", action.Problem);
            Assert.False(chat.Review.HasSomethingToApply);
        }

        [Fact]
        public async Task No_install_when_installed_module_covers_the_request() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", enabled: false));

            // AI calls list_installed_modules, sees Pathing is installed (disabled), and proposes enable_module
            model.WillCallTool("list_installed_modules", "call_list", new Dictionary<string, object>());
            model.WillCallTool("submit_plan", "call_plan",
                               Plan("Pathing is already installed but disabled. I propose enabling it.",
                                    EnableAction("bh.community.pathing", reason: "Enable Pathing for routes")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Show me marker routes");

            Assert.NotNull(chat.Review);
            ReviewedAction action = Assert.Single(chat.Review.Actions);
            Assert.Equal(PlanActionKind.EnableModule, action.Action.Kind);
            Assert.Equal("Pathing: enable it", action.Summary);
            Assert.True(action.CanApply);
            Assert.Equal(0, world.InstallCalls);
        }

        [Fact]
        public async Task Apply_installs_and_enables_module_then_continuation_configures_settings() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" })
                }
            };
            world.SettingsByNamespace["bh.community.pathing"] = FakeWorld.Ok("Pathing",
                new ModuleSettingInfo("ShowTrails", "Show trails", "", "Boolean", "false", null, null));

            // Turn 1: AI calls search_catalog, proposes install with Gap
            model.WillCallTool("search_catalog", "call_cat", new Dictionary<string, object> { ["query"] = "marker" });
            model.WillCallTool("submit_plan", "call_plan",
                               Plan("I propose installing Pathing to display markers and trails.",
                                    InstallAction("bh.community.pathing", "Markers and trails on map", reason: "Provides routes")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Show me marker routes on this map");

            Assert.NotNull(chat.Review);
            Assert.Equal(0, world.InstallCalls);

            // Turn 2 setup (Continuation): after apply, model calls get_module_settings and proposes set_setting
            model.WillCallTool("get_module_settings", "call_settings",
                               new Dictionary<string, object> { ["module"] = "bh.community.pathing" });
            model.WillCallTool("submit_plan", "call_cfg",
                               Plan("Pathing is installed and enabled. I suggest turning on trails.",
                                    SettingAction("bh.community.pathing", "ShowTrails", "true", reason: "Show the trails")));

            // Player applies the install
            await chat.ApplyAsync(new[] { 0 });

            // Module was installed and enabled
            Assert.Equal(1, world.InstallCalls);
            Assert.Contains("install bh.community.pathing", world.Writes);
            Assert.Contains("enable bh.community.pathing", world.Writes);

            // Report in Chat
            Assert.Contains(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("Applied 1 change: Pathing: install and enable it"));

            // Continuation ran and proposed configuring settings
            Assert.Equal(2, world.SettingsCalls);
            Assert.NotNull(chat.Review);
            ReviewedAction cfgAction = Assert.Single(chat.Review.Actions);
            Assert.Equal(PlanActionKind.SetSetting, cfgAction.Action.Kind);
            Assert.Equal("ShowTrails", cfgAction.SettingKey);
            Assert.Equal("true", cfgAction.ProposedValue);
        }

        [Fact]
        public async Task Undo_disables_installed_module_without_deleting() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" })
                }
            };
            world.SettingsByNamespace["bh.community.pathing"] = FakeWorld.Ok("Pathing");

            model.WillCallTool("submit_plan", "call_plan",
                               Plan("I propose installing Pathing.",
                                    InstallAction("bh.community.pathing", "Markers on map")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Install pathing");

            // Configure continuation reply
            model.WillReply("Pathing is installed.");

            await chat.ApplyAsync(new[] { 0 });

            // Module is installed and enabled
            InstalledModuleInfo installed = world.Modules.FirstOrDefault(m => m.Namespace == "bh.community.pathing");
            Assert.NotNull(installed);
            Assert.True(installed.Enabled);

            // Undo the install
            world.Writes.Clear();
            await chat.UndoAsync();

            // Module is disabled, NOT deleted
            Assert.Contains("disable bh.community.pathing", world.Writes);
            installed = world.Modules.FirstOrDefault(m => m.Namespace == "bh.community.pathing");
            Assert.NotNull(installed);
            Assert.False(installed.Enabled);

            Assert.Contains(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("Undid 1 change: Pathing: disable it"));
        }

        [Fact]
        public async Task Clean_profile_hero_point_request_installs_marker_module_and_configures_it() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails for Guild Wars 2.",
                                          new[] { "bh.blishhud" }, isPreview: false,
                                          apiPermissions: new[] { "account", "characters" })
                }
            };
            world.SettingsByNamespace["bh.community.pathing"] = FakeWorld.Ok("Pathing",
                new ModuleSettingInfo("ShowTrails", "Show trails", "", "Boolean", "false", null, null));

            // Clean profile: no modules installed
            Assert.Empty(world.Modules);

            // Turn 1: Player asks for hero points. AI checks installed modules (none), checks catalog, proposes install with Gap
            model.WillCallTool("list_installed_modules", "call_list", new Dictionary<string, object>());
            model.WillCallTool("search_catalog", "call_search", new Dictionary<string, object> { ["query"] = "hero point" });
            model.WillCallTool("submit_plan", "call_install_plan",
                               Plan("You have no marker module installed. I propose installing Pathing to show hero point routes.",
                                    InstallAction("bh.community.pathing", "Hero point markers and routes on the map",
                                                  reason: "Pathing shows marker routes")));

            var chat = NewChat(model, world);
            await chat.SendAsync("I want to finish the hero points on this map");

            Assert.NotNull(chat.Review);
            ReviewedAction reviewAction = Assert.Single(chat.Review.Actions);
            Assert.Equal(PlanActionKind.Install, reviewAction.Action.Kind);
            Assert.Equal("Hero point markers and routes on the map", reviewAction.Gap);

            // Turn 2 (Continuation): After apply, AI checks settings of Pathing, proposes enabling trails
            model.WillCallTool("get_module_settings", "call_settings", new Dictionary<string, object> { ["module"] = "bh.community.pathing" });
            model.WillCallTool("submit_plan", "call_cfg_plan",
                               Plan("Pathing is installed and enabled. I suggest enabling trails to follow hero point routes.",
                                    SettingAction("bh.community.pathing", "ShowTrails", "true", reason: "Follow the trails to each hero point")));

            // Player applies the install
            await chat.ApplyAsync(new[] { 0 });

            // Pathing was installed and enabled
            InstalledModuleInfo pathing = Assert.Single(world.Modules);
            Assert.Equal("bh.community.pathing", pathing.Namespace);
            Assert.True(pathing.Enabled);

            // Continuation proposed the setting change
            Assert.NotNull(chat.Review);
            ReviewedAction trailAction = Assert.Single(chat.Review.Actions);
            Assert.Equal(PlanActionKind.SetSetting, trailAction.Action.Kind);
            Assert.Equal("ShowTrails", trailAction.SettingKey);
            Assert.Equal("true", trailAction.ProposedValue);

            // Player applies the setting
            await chat.ApplyAsync(new[] { 0 });

            // Everything completed in one Chat
            Assert.Contains(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("install and enable it"));
            Assert.Contains(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("Show trails"));
        }
    }
}
