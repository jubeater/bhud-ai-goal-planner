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
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world, bool autoApply = false, Action<string> logWarn = null) {
            IBlishHudWorld target = new GameThreadWorld(world, new ImmediateGameThread());
            var plans = new Plans(target, () => autoApply);
            var tools = Lookups.For(target);
            plans.AddTo(tools);

            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            tools, null, logWarn, plans);
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

        private static IDictionary<string, object> DisableAction(string module, string reason = "Disable module", bool sideEffect = false) {
            return new Dictionary<string, object> {
                ["kind"]           = "disable_module",
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
            Assert.Equal("Markers and trails for Guild Wars 2.", action.Description);
            Assert.True(action.PermissionsKnown);
        }

        [Fact]
        public async Task Review_shows_permissions_as_unknown_when_details_fail_and_allows_approval() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", new[] { "bh.blishhud" })
                }
            };
            world.AnswerGetCatalogModuleDetails = (ns, ct) =>
                Task.FromResult(new CatalogModuleDetails(false, ns, problem: "Network timeout."));

            model.WillCallTool("submit_plan", "call_1",
                               Plan("I propose installing Pathing.",
                                    InstallAction("bh.community.pathing", "Markers on map")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Install pathing");

            Assert.NotNull(chat.Review);
            ReviewedAction action = Assert.Single(chat.Review.Actions);
            Assert.True(action.IsAccepted);
            Assert.True(action.CanApply);
            Assert.False(action.PermissionsKnown);
            Assert.Empty(action.ApiPermissions);
            Assert.Contains("Network timeout", action.DetailsProblem);

            // Player can still approve and apply
            model.WillReply("Installed.");
            await chat.ApplyAsync(new[] { 0 });
            Assert.Equal(1, world.InstallCalls);
            Assert.Contains("install bh.community.pathing", world.Writes);
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
        public async Task Continuation_instructs_to_confirm_permissions_and_restart_after_install() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", new[] { "bh.blishhud" })
                }
            };
            world.CatalogDetails["bh.community.pathing"] = new CatalogModuleDetails(
                true, "bh.community.pathing", "Pathing", "3.2.1",
                "Markers and trails.", new[] { "account" }
            );

            model.WillCallTool("submit_plan", "call_1",
                               Plan("I propose installing Pathing.",
                                    InstallAction("bh.community.pathing", "Markers on map")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Install pathing");

            model.WillReply("Please check Pathing permissions.");
            await chat.ApplyAsync(new[] { 0 });

            // The instruction sent to the model for the continuation must include permission instructions
            var lastRequest = model.Requests.Last();
            var continuationMsg = string.Join(" ", lastRequest.Select(m => m.Text));
            Assert.Contains("permissions", continuationMsg, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("restart", continuationMsg, StringComparison.OrdinalIgnoreCase);
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

        [Fact]
        public async Task Existing_module_is_not_sent_to_installer() {
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.dependency", "Dependency", "1.0.0", "A dependency", new[] { "bh.blishhud" })
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.dependency", "Old name", "0.9.0", "", enabled: true));

            var plan = new Plan("Install Dependency", new[] {
                new PlanAction(PlanActionKind.Install, "install", "Dependency", null, null,
                               "Need dependency", isSideEffect: false, gap: "Missing feature")
            });

            var applier = new PlanApplier(world);
            var reviewed = new ReviewedAction(plan.Actions[0], "bh.dependency", "Dependency", null, "Dependency",
                                              "not installed", "installed", null);
            var review = new PlanReview(plan, new[] { reviewed });

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0 }, CancellationToken.None);

            Assert.Equal(0, world.InstallCalls);
            ApplyOutcome outcome = Assert.Single(applied.Outcomes);
            Assert.False(outcome.Succeeded);
            Assert.Contains("already installed", outcome.Problem);
        }

        [Fact]
        public async Task Approved_installs_are_applied_in_dependency_order() {
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers", new[] { "bh.blishhud" }),
                    new CatalogModuleInfo("bh.addon", "Addon Module", "1.0.0", "Addon", new[] { "bh.blishhud", "bh.community.pathing" })
                }
            };

            var plan = new Plan("Install Addon then Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.addon", null, null,
                               "Need addon", isSideEffect: false, gap: "Addon gap"),
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Need pathing", isSideEffect: false, gap: "Pathing gap")
            });

            var applier = new PlanApplier(world);
            var reviewedAddon = new ReviewedAction(plan.Actions[0], "bh.addon", "Addon Module", null, "Addon Module",
                                                   "not installed", "installed", null, dependencies: new[] { "bh.community.pathing" });
            var reviewedPathing = new ReviewedAction(plan.Actions[1], "bh.community.pathing", "Pathing", null, "Pathing",
                                                     "not installed", "installed", null);
            var review = new PlanReview(plan, new[] { reviewedAddon, reviewedPathing });

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0, 1 }, CancellationToken.None);

            Assert.Equal(2, world.InstallCalls);
            // Must have installed dependency (bh.community.pathing) before dependent (bh.addon)
            Assert.Equal("install bh.community.pathing", world.Writes[0]);
            Assert.Equal("install bh.addon", world.Writes[1]);
        }

        [Fact]
        public async Task Unchecking_dependency_must_not_install_dependent() {
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers", new[] { "bh.blishhud" }),
                    new CatalogModuleInfo("bh.addon", "Addon Module", "1.0.0", "Addon", new[] { "bh.blishhud", "bh.community.pathing" })
                }
            };

            var plan = new Plan("Install Pathing and Addon", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Need pathing", isSideEffect: false, gap: "Pathing gap"),
                new PlanAction(PlanActionKind.Install, "install", "bh.addon", null, null,
                               "Need addon", isSideEffect: false, gap: "Addon gap")
            });

            var applier = new PlanApplier(world);
            var reviewedPathing = new ReviewedAction(plan.Actions[0], "bh.community.pathing", "Pathing", null, "Pathing",
                                                     "not installed", "installed", null);
            var reviewedAddon = new ReviewedAction(plan.Actions[1], "bh.addon", "Addon Module", null, "Addon Module",
                                                   "not installed", "installed", null, dependencies: new[] { "bh.community.pathing" });
            var review = new PlanReview(plan, new[] { reviewedPathing, reviewedAddon });

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 1 }, CancellationToken.None);

            Assert.Equal(0, world.InstallCalls);
            ApplyOutcome outcome = Assert.Single(applied.Outcomes);
            Assert.False(outcome.Succeeded);
            Assert.Contains("requires dependency \"bh.community.pathing\"", outcome.Problem);
        }

        [Fact]
        public async Task Failed_dependency_must_not_install_dependent() {
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers", new[] { "bh.blishhud" }),
                    new CatalogModuleInfo("bh.addon", "Addon Module", "1.0.0", "Addon", new[] { "bh.blishhud", "bh.community.pathing" })
                }
            };
            world.AnswerInstallModule = (ns, ct) => {
                if (ns == "bh.community.pathing") {
                    return Task.FromResult(new ModuleInstallChange(false, false, "Pathing", ns, "3.2.1", "Network error"));
                }
                return Task.FromResult(new ModuleInstallChange(true, true, ns, ns, "1.0.0"));
            };

            var plan = new Plan("Install Pathing and Addon", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Need pathing", isSideEffect: false, gap: "Pathing gap"),
                new PlanAction(PlanActionKind.Install, "install", "bh.addon", null, null,
                               "Need addon", isSideEffect: false, gap: "Addon gap")
            });

            var applier = new PlanApplier(world);
            var reviewedPathing = new ReviewedAction(plan.Actions[0], "bh.community.pathing", "Pathing", null, "Pathing",
                                                     "not installed", "installed", null);
            var reviewedAddon = new ReviewedAction(plan.Actions[1], "bh.addon", "Addon Module", null, "Addon Module",
                                                   "not installed", "installed", null, dependencies: new[] { "bh.community.pathing" });
            var review = new PlanReview(plan, new[] { reviewedPathing, reviewedAddon });

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0, 1 }, CancellationToken.None);

            Assert.Equal(1, world.InstallCalls);
            Assert.Equal(2, applied.Outcomes.Count);
            Assert.False(applied.Outcomes[0].Succeeded);
            Assert.False(applied.Outcomes[1].Succeeded);
            Assert.Contains("requires dependency \"bh.community.pathing\"", applied.Outcomes[1].Problem);
        }

        [Fact]
        public async Task Completed_install_is_recorded_when_enable_fails() {
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers", new[] { "bh.blishhud" })
                }
            };
            world.AnswerSetModuleEnabled = (ns, en) => {
                return new ModuleChange(false, false, "Module threw in LoadAsync");
            };

            var plan = new Plan("Install Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Need pathing", isSideEffect: false, gap: "Pathing gap")
            });

            IBlishHudWorld target = new GameThreadWorld(world, new ImmediateGameThread());
            var applier = new PlanApplier(target);
            var reviewed = new ReviewedAction(plan.Actions[0], "bh.community.pathing", "Pathing", null, "Pathing",
                                              "not installed", "installed", null);
            var review = new PlanReview(plan, new[] { reviewed });

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0 }, CancellationToken.None);

            Assert.Equal(1, world.InstallCalls);
            ApplyOutcome outcome = Assert.Single(applied.Outcomes);
            Assert.True(applied.AnyChanged);
            Assert.True(outcome.Changed);
            Assert.False(outcome.Succeeded);
            Assert.Equal("Module threw in LoadAsync", outcome.Problem);
            UndoStep undo = Assert.Single(applied.Undo);
            Assert.Equal(PlanActionKind.Install, undo.Kind);
            Assert.Equal("bh.community.pathing", undo.ModuleNamespace);
        }

        [Fact]
        public async Task Completed_install_is_recorded_when_cancelled_during_download() {
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers", new[] { "bh.blishhud" })
                }
            };

            var cts = new CancellationTokenSource();
            world.AnswerInstallModule = async (ns, ct) => {
                cts.Cancel();
                await Task.Yield();
                world.Modules.Add(new InstalledModuleInfo(ns, "Pathing", "3.2.1", "Markers", enabled: false));
                world.Writes.Add("install " + ns);
                return new ModuleInstallChange(true, true, "Pathing", ns, "3.2.1");
            };

            var plan = new Plan("Install Pathing", new[] {
                new PlanAction(PlanActionKind.Install, "install", "bh.community.pathing", null, null,
                               "Need pathing", isSideEffect: false, gap: "Pathing gap")
            });

            IBlishHudWorld target = new GameThreadWorld(world, new ImmediateGameThread());
            var applier = new PlanApplier(target);
            var reviewed = new ReviewedAction(plan.Actions[0], "bh.community.pathing", "Pathing", null, "Pathing",
                                              "not installed", "installed", null);
            var review = new PlanReview(plan, new[] { reviewed });

            AppliedPlan applied = await applier.ApplyAsync(review, new[] { 0 }, cts.Token);

            Assert.Equal(1, world.InstallCalls);
            Assert.True(applied.Cancelled);
            Assert.True(applied.AnyChanged);
            ApplyOutcome outcome = Assert.Single(applied.Outcomes);
            Assert.True(outcome.Changed);
            UndoStep undo = Assert.Single(applied.Undo);
            Assert.Equal(PlanActionKind.Install, undo.Kind);
            Assert.Equal("bh.community.pathing", undo.ModuleNamespace);
        }

        [Fact]
        public async Task Install_undo_remove_requires_confirmation_calls_delete_once_and_omits_button_for_preexisting() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" })
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.preexisting", "PreExisting", "1.0.0", "Pre-existing module.", enabled: true));
            world.SettingsByNamespace["bh.community.pathing"] = FakeWorld.Ok("Pathing");

            model.WillCallTool("submit_plan", "call_plan",
                               Plan("Install Pathing and disable PreExisting.",
                                    InstallAction("bh.community.pathing", "Markers on map"),
                                    DisableAction("bh.preexisting")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Setup modules");

            model.WillReply("Continuation reply.");
            await chat.ApplyAsync(new[] { 0, 1 });

            Assert.True(world.Modules.Any(m => m.Namespace == "bh.community.pathing"));

            // Undo the apply (both changes undone)
            world.Writes.Clear();
            await chat.UndoAsync();

            ChatMessage undoReport = Assert.Single(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("Undid"));

            // Pre-existing module does NOT get a remove button
            Assert.False(chat.CanRemoveModule("bh.preexisting"));
            Assert.DoesNotContain(undoReport.RemovableModules, rm => rm.Namespace == "bh.preexisting");

            // Installed module DOES get a remove button
            Assert.True(chat.CanRemoveModule("bh.community.pathing"));
            RemovableModuleInfo removable = Assert.Single(undoReport.RemovableModules);
            Assert.Equal("bh.community.pathing", removable.Namespace);
            Assert.Equal("Pathing", removable.Name);

            // First click: asks for confirmation, does not delete yet ("no confirm -> no delete")
            bool removedWithoutConfirm = await chat.RemoveModuleAsync("bh.community.pathing");
            Assert.False(removedWithoutConfirm);
            Assert.Equal(0, world.DeleteModuleCalls);
            Assert.True(chat.IsAwaitingRemoveConfirmation("bh.community.pathing"));

            // Second click: confirmed, calls DeleteModule once ("remove calls DeleteModule once")
            bool removedWithConfirm = await chat.RemoveModuleAsync("bh.community.pathing");
            Assert.True(removedWithConfirm);
            Assert.Equal(1, world.DeleteModuleCalls);
            Assert.Contains("delete bh.community.pathing", world.Writes);
            Assert.False(chat.CanRemoveModule("bh.community.pathing"));

            // The Report says it was removed and cannot be undone
            ChatMessage removeReport = chat.Messages.Last();
            Assert.Equal(ChatMessageAuthor.Report, removeReport.Author);
            Assert.Contains("Removed Pathing", removeReport.Text);
            Assert.Contains("cannot be undone", removeReport.Text);
        }

        [Fact]
        public async Task Remove_module_failure_shows_fixed_message_and_logs_exception_type() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" })
                }
            };
            world.SettingsByNamespace["bh.community.pathing"] = FakeWorld.Ok("Pathing");
            world.AnswerDeleteModule = _ => throw new InvalidOperationException("SecretDiskErrorDetailsThatMustNotLeak");

            var warnings = new List<string>();
            var chat = NewChat(model, world, logWarn: warnings.Add);

            model.WillCallTool("submit_plan", "call_plan",
                               Plan("Install Pathing.",
                                    InstallAction("bh.community.pathing", "Markers on map")));

            await chat.SendAsync("Setup");
            model.WillReply("Applied.");
            await chat.ApplyAsync(new[] { 0 });

            await chat.UndoAsync();
            Assert.True(chat.CanRemoveModule("bh.community.pathing"));

            // First click asks confirm
            await chat.RemoveModuleAsync("bh.community.pathing");

            // Second click attempts remove, fails with InvalidOperationException
            bool removed = await chat.RemoveModuleAsync("bh.community.pathing");
            Assert.False(removed);

            // Shows fixed message to Player
            ChatMessage notice = Assert.Single(chat.Messages, m => m.Author == ChatMessageAuthor.Notice && m.Text.Contains("Removing the module failed."));
            Assert.Equal("Removing the module failed.", notice.Text);

            // Log contains only the exception type, not the exception message or player text
            string warn = Assert.Single(warnings, w => w.Contains("DeleteModule failed:"));
            Assert.Equal("DeleteModule failed: InvalidOperationException", warn);
            Assert.DoesNotContain("SecretDiskErrorDetailsThatMustNotLeak", warn);
        }

        [Fact]
        public async Task New_chat_during_install_must_not_offer_prior_chats_module_for_removal() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                CatalogModules = {
                    new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" })
                }
            };
            world.SettingsByNamespace["bh.community.pathing"] = FakeWorld.Ok("Pathing");

            var installStarted = new TaskCompletionSource<bool>();
            var allowInstallToFinish = new TaskCompletionSource<bool>();

            world.AnswerInstallModule = async (ns, ct) => {
                installStarted.SetResult(true);
                await allowInstallToFinish.Task;
                world.Modules.Add(new InstalledModuleInfo(ns, "Pathing", "3.2.1", "Markers", enabled: true));
                return new ModuleInstallChange(true, true, "Pathing", ns, "3.2.1");
            };

            model.WillCallTool("submit_plan", "call_plan",
                               Plan("Install Pathing.",
                                    InstallAction("bh.community.pathing", "Markers on map")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Setup");

            model.WillReply("Continuation reply.");
            Task applyTask = chat.ApplyAsync(new[] { 0 });

            await installStarted.Task;

            // Player starts a new chat while install is in flight
            chat.StartNewChat();

            // Allow the install to finish
            allowInstallToFinish.SetResult(true);
            await applyTask;

            // Undo the install in the new chat
            await chat.UndoAsync();

            // The module was installed by the previous chat, so it must not be offered for removal in this chat
            Assert.False(chat.CanRemoveModule("bh.community.pathing"));
            ChatMessage undoReport = Assert.Single(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("Undid"));
            Assert.DoesNotContain(undoReport.RemovableModules, rm => rm.Namespace == "bh.community.pathing");
        }
    }
}
