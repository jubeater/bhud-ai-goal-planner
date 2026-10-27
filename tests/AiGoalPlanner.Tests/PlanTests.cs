using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatMessage = jubeater.aigoalplanner.Core.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests Plans from the Player's side of the Chat: the AI proposes, the Review says what each
    /// Action would do and what cannot be done, Apply changes the Player's configuration, Undo puts
    /// it back, and the AI's next turn works from the state that is really there.
    /// </summary>
    public class PlanTests
    {
        [Fact]
        public async Task A_plan_is_shown_as_a_reply_and_a_Review_with_one_row_per_Action() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("I suggest hiding the mount markers.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false",
                                           reason: "You asked for the markers to go.")));

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn off the markers in Mounts");

            Assert.Equal("I suggest hiding the mount markers.",
                         chat.Messages.Last(message => message.Author == ChatMessageAuthor.Ai).Text);

            PlanReview review = Assert.IsType<PlanReview>(chat.Review);
            ReviewedAction action = Assert.Single(review.Actions);

            Assert.True(action.IsAccepted);
            Assert.True(action.CanApply);
            Assert.Equal("Mounts", action.ModuleName);
            Assert.Equal("Show markers", action.TargetName);
            Assert.Equal("true", action.CurrentValue);
            Assert.Equal("false", action.ProposedValue);
            Assert.Equal("You asked for the markers to go.", action.Action.Reason);
            Assert.False(action.Action.IsSideEffect);

            // A Plan is a proposal: nothing in the Player's configuration has changed.
            Assert.Empty(world.Writes);
        }

        [Fact]
        public async Task An_Action_that_enables_a_Module_shows_the_state_it_would_change() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Pathing can do that, but it is disabled.",
                                    Action("enable_module", "bh.pathing", reason: "Pathing draws the trails.")));

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("show me the trail to the hero point");

            ReviewedAction action = Assert.Single(chat.Review.Actions);

            Assert.True(action.CanApply);
            Assert.Equal("disabled", action.CurrentValue);
            Assert.Equal("enabled", action.ProposedValue);
        }

        [Fact]
        public async Task A_module_named_the_way_the_Player_sees_it_is_understood() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Disabling Pathing for you.",
                                    Action("disable_module", "Pathing", reason: "It overlaps with Mounts.")));

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("stop Pathing from drawing anything");

            // Pathing is disabled already, so the change is refused — but the Module was found by its
            // name, which is what this test is about.
            ReviewedAction action = Assert.Single(chat.Review.Actions);

            Assert.Equal("bh.pathing", action.ModuleNamespace);
            Assert.Contains("already disabled", action.Problem);
        }

        [Fact]
        public async Task A_module_the_Player_does_not_have_is_refused_with_a_reason() {
            ReviewedAction action = await ReviewOneAsync(Action("enable_module", "bh.nowhere", reason: "It would help."));

            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("No installed module is called \"bh.nowhere\"", action.Problem);
        }

        [Fact]
        public async Task A_Setting_the_Module_does_not_have_is_refused_with_a_reason() {
            ReviewedAction action = await ReviewOneAsync(Action("set_setting", "bh.mounts", key: "Show all markers",
                                                                value: "false", reason: "You asked for it."));

            Assert.False(action.IsAccepted);
            Assert.Contains("has no setting with the key \"Show all markers\"", action.Problem);
        }

        [Fact]
        public async Task A_value_of_the_wrong_type_is_refused_with_a_reason() {
            ReviewedAction action = await ReviewOneAsync(Action("set_setting", "bh.mounts", key: "Show markers",
                                                                value: "yes", reason: "You asked for it."));

            Assert.False(action.IsAccepted);
            Assert.Contains("it has to be true or false", action.Problem);
        }

        [Fact]
        public async Task A_value_outside_the_Settings_range_is_refused_with_a_reason() {
            ReviewedAction action = await ReviewOneAsync(Action("set_setting", "bh.mounts", key: "Icon size",
                                                                value: "9", reason: "Bigger icons."));

            Assert.False(action.IsAccepted);
            Assert.Contains("the largest allowed value is 5", action.Problem);
        }

        [Fact]
        public async Task A_value_that_is_not_one_of_the_Settings_choices_is_refused_with_a_reason() {
            ReviewedAction action = await ReviewOneAsync(Action("set_setting", "bh.mounts", key: "Marker style",
                                                                value: "Hexagon", reason: "A nicer shape."));

            Assert.False(action.IsAccepted);
            Assert.Contains("it has to be one of: Arrow, Circle, Star", action.Problem);
        }

        [Fact]
        public async Task The_same_Module_proposed_twice_is_refused_the_second_time() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Enabling Pathing now, and turning it off again.",
                                    Action("enable_module", "bh.pathing", reason: "You asked for trails."),
                                    Action("disable_module", "bh.pathing", reason: "It might be too much.")));

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("show the trails");

            PlanReview review = chat.Review;

            Assert.True(review.Actions[0].CanApply);
            Assert.False(review.Actions[1].IsAccepted);
            Assert.Contains("already changed by another action in this plan", review.Actions[1].Problem);
        }

        [Fact]
        public async Task The_same_Setting_proposed_twice_is_refused_the_second_time() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the markers off, then off again.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "First."),
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "true", reason: "Second.")));

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("turn the markers off");

            PlanReview review = chat.Review;

            Assert.True(review.Actions[0].CanApply);
            Assert.False(review.Actions[1].IsAccepted);
            Assert.Contains("\"Show markers\" is already changed", review.Actions[1].Problem);
        }

        [Fact]
        public async Task Two_different_Settings_of_one_Module_are_both_allowed() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the markers off and making the icons smaller.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked."),
                                    Action("set_setting", "bh.mounts", key: "Icon size", value: "1", reason: "You asked.")));

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("turn the markers off and make the icons smaller");

            Assert.All(chat.Review.Actions, action => Assert.True(action.CanApply));
        }

        [Fact]
        public async Task Apply_carries_out_the_approved_Actions_and_the_Chat_reports_them() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("Done — the markers are off. Anything else?");

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn off the mount markers");
            await chat.ApplyAsync(new[] { 0 });

            Assert.Equal(new[] { "set bh.mounts/Show markers=false" }, world.Writes.ToArray());

            ChatMessage report = chat.Messages.Last(message => message.Author == ChatMessageAuthor.Report);

            Assert.Contains("Applied 1 change", report.Text);
            Assert.Contains("Mounts: set \"Show markers\" to false", report.Text);

            // The Review is over, and the AI has carried on from the state the Apply made.
            Assert.Null(chat.Review);
            Assert.Equal(ChatMessageAuthor.Ai, chat.Messages.Last().Author);
            Assert.Equal("Done — the markers are off. Anything else?", chat.Messages.Last().Text);
        }

        [Fact]
        public async Task Apply_leaves_the_Actions_the_Player_did_not_approve_alone() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Two changes.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked."),
                                    Action("set_setting", "bh.mounts", key: "Icon size", value: "1", reason: "You asked.")));
            model.WillReply("Done.");

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn the markers off and make the icons smaller");
            await chat.ApplyAsync(new[] { 1 });

            Assert.Equal(new[] { "set bh.mounts/Icon size=1" }, world.Writes.ToArray());
        }

        [Fact]
        public async Task One_Action_that_fails_does_not_stop_the_rest() {
            var world = PlayerWorld();
            world.AnswerSetSetting = (module, key, value) => key == "Show markers"
                                                                 ? new ModuleSettingChange(false, null, "Mounts refused the change.")
                                                                 : new ModuleSettingChange(true, "3");

            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Two changes.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked."),
                                    Action("set_setting", "bh.mounts", key: "Icon size", value: "1", reason: "You asked.")));
            model.WillReply("Some of that did not take.");

            var chat = NewChat(model, world);

            await chat.SendAsync("turn the markers off and make the icons smaller");
            await chat.ApplyAsync(new[] { 0, 1 });

            // Both were attempted, in the order the Plan had them, and the second one happened.
            Assert.Equal(new[] { "set bh.mounts/Show markers=false", "set bh.mounts/Icon size=1" }, world.Writes.ToArray());

            ChatMessage report = chat.Messages.Last(message => message.Author == ChatMessageAuthor.Report);

            Assert.Contains("Applied 1 change", report.Text);
            Assert.Contains("Not applied", report.Text);
            Assert.Contains("Mounts refused the change", report.Text);
        }

        [Fact]
        public async Task A_Module_that_will_not_change_does_not_stop_the_rest() {
            var world = PlayerWorld();
            world.AnswerSetModuleEnabled = (module, enabled) => module == "bh.pathing"
                                                                   ? new ModuleChange(false, false, "Blish HUD could not load the module.")
                                                                   : new ModuleChange(true, true);

            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Enabling Pathing and hiding the mount markers.",
                                    Action("enable_module", "bh.pathing", reason: "Pathing draws the trails."),
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("Pathing would not load, but the markers are off.");

            var chat = NewChat(model, world);

            await chat.SendAsync("show me the trails without the mount markers");
            await chat.ApplyAsync(new[] { 0, 1 });

            Assert.Equal(new[] { "enable bh.pathing", "set bh.mounts/Show markers=false" }, world.Writes.ToArray());

            ChatMessage report = chat.Messages.Last(message => message.Author == ChatMessageAuthor.Report);

            Assert.Contains("Applied 1 change", report.Text);
            Assert.Contains("Blish HUD could not load the module.", report.Text);
        }

        [Fact]
        public async Task A_rejected_Action_cannot_be_applied_even_if_the_Review_asks_for_it() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning off a setting that does not exist.",
                                    Action("set_setting", "bh.mounts", key: "Show all markers", value: "false", reason: "You asked.")));

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn everything off");
            await chat.ApplyAsync(new[] { 0 });

            Assert.Empty(world.Writes);

            ChatMessage report = chat.Messages.Last(message => message.Author == ChatMessageAuthor.Report);

            Assert.Contains("Nothing was applied", report.Text);

            // Nothing to tell the model about, so it is not asked again.
            Assert.Single(model.Requests);
        }

        [Fact]
        public async Task Cancel_Review_closes_it_changes_nothing_and_tells_the_model() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("All right, nothing changed.");

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn off the mount markers");

            chat.CancelReview();

            Assert.Null(chat.Review);
            Assert.Empty(world.Writes);

            await chat.SendAsync("never mind");

            string history = string.Join(" ", model.Requests.Last().Select(message => message.Text));

            Assert.Contains("dismissed the plan", history);
        }

        [Fact]
        public async Task Apply_runs_on_the_game_thread() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("Done.");

            var world      = PlayerWorld();
            var gameThread = new FakeGameThread();
            var chat       = NewChat(model, world, autoApply: false, gameThread: gameThread);

            await PumpUntil(chat.SendAsync("turn off the mount markers"), gameThread);

            // The Plan is read and checked, and nothing has been written.
            Assert.Empty(world.Writes);

            Task applying = chat.ApplyAsync(new[] { 0 });

            // The write waits its turn on the game thread, which is where Blish HUD expects it.
            await WaitUntil(() => gameThread.WaitingCount > 0);

            Assert.Empty(world.Writes);

            await PumpUntil(applying, gameThread);

            Assert.Equal(new[] { "set bh.mounts/Show markers=false" }, world.Writes.ToArray());
        }

        [Fact]
        public async Task Undo_puts_a_changed_Setting_back() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("Done.");

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn off the mount markers");
            await chat.ApplyAsync(new[] { 0 });

            Assert.True(chat.CanUndo);

            await chat.UndoAsync();

            Assert.Equal(new[] { "set bh.mounts/Show markers=false", "set bh.mounts/Show markers=true" },
                         world.Writes.ToArray());

            Assert.Equal("true", await SettingValueAsync(world, "bh.mounts", "Show markers"));
            Assert.False(chat.CanUndo);

            ChatMessage report = chat.Messages.Last(message => message.Author == ChatMessageAuthor.Report);

            Assert.Contains("Undid 1 change", report.Text);
        }

        [Fact]
        public async Task Undo_puts_a_disabled_Module_back_to_enabled() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Disabling the Event Table.",
                                    Action("disable_module", "bh.eventtable", reason: "You asked.")));
            model.WillReply("Done.");

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn off the event table");
            await chat.ApplyAsync(new[] { 0 });

            Assert.False(Enabled(world, "bh.eventtable"));

            await chat.UndoAsync();

            Assert.True(Enabled(world, "bh.eventtable"));
            Assert.Equal(new[] { "disable bh.eventtable", "enable bh.eventtable" }, world.Writes.ToArray());
        }

        [Fact]
        public async Task Undo_can_only_be_done_once_per_Apply() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("Done.");

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn off the mount markers");
            await chat.ApplyAsync(new[] { 0 });

            await chat.UndoAsync();
            await chat.UndoAsync();

            Assert.Equal(2, world.Writes.Count);
            Assert.Contains("nothing to undo", chat.Messages.Last().Text);
        }

        [Fact]
        public async Task The_journal_keeps_the_last_ten_Applies() {
            var model = new ScriptedChatClient();
            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            for (int round = 0; round < 11; round++) {
                model.WillCallTool(SubmitPlanTool.Name, "call_" + round,
                                   Plan("Change " + round + ".",
                                        Action("set_setting", "bh.mounts", key: "Show markers",
                                               value: round % 2 == 0 ? "false" : "true", reason: "You asked.")));
                model.WillReply("Done.");

                await chat.SendAsync("change " + round);
                await chat.ApplyAsync(new[] { 0 });
            }

            // Eleven Applies, ten of them undoable: the oldest fell out of the journal, so ten Undos
            // work through the rest and the first change stays where it was.
            for (int round = 0; round < 10; round++) {
                await chat.UndoAsync();
            }

            Assert.False(chat.CanUndo);
            Assert.Equal("false", await SettingValueAsync(world, "bh.mounts", "Show markers"));
        }

        [Fact]
        public async Task An_Undo_record_is_never_sent_to_the_model() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("Done.");
            model.WillReply("Anything else?");

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn off the mount markers");
            await chat.ApplyAsync(new[] { 0 });
            await chat.UndoAsync();
            await chat.SendAsync("what now?");

            string history = string.Join(" ", model.Requests.Last().Select(message => message.Text));

            Assert.DoesNotContain("Undid", history);
            Assert.DoesNotContain("nothing to undo", history);
        }

        [Fact]
        public async Task Auto_apply_off_waits_for_the_Player() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("turn off the mount markers");

            Assert.Empty(world.Writes);
            Assert.NotNull(chat.Review);
            Assert.Single(model.Requests);
        }

        [Fact]
        public async Task Auto_apply_applies_a_Setting_change_the_Player_asked_for() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("The markers are off.");

            var world = PlayerWorld();
            var chat  = NewChat(model, world, autoApply: true);

            await chat.SendAsync("turn off the mount markers");

            Assert.Equal(new[] { "set bh.mounts/Show markers=false" }, world.Writes.ToArray());
            Assert.Null(chat.Review);
            Assert.Contains(chat.Messages, message => message.Author == ChatMessageAuthor.Report
                                                   && message.Text.Contains("Applied 1 change"));
        }

        [Fact]
        public async Task Auto_apply_leaves_Module_changes_and_Side_Effects_for_Review() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Enabling Pathing and hiding the mount markers.",
                                    Action("enable_module", "bh.pathing", reason: "Pathing draws the trails."),
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false",
                                           reason: "The two would overlap.", sideEffect: true)));

            var world = PlayerWorld();
            var chat  = NewChat(model, world, autoApply: true);

            await chat.SendAsync("show me the trails");

            Assert.Empty(world.Writes);

            PlanReview review = Assert.IsType<PlanReview>(chat.Review);

            Assert.Equal(2, review.Actions.Count);
            Assert.Single(model.Requests);
        }

        [Fact]
        public async Task Auto_apply_takes_the_Setting_change_and_still_asks_about_the_rest() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Hiding the markers and shrinking the icons.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked."),
                                    Action("set_setting", "bh.mounts", key: "Icon size", value: "1",
                                           reason: "Smaller icons read better.", sideEffect: true)));
            model.WillReply("The markers are off. Shall I shrink the icons too?");

            var world = PlayerWorld();
            var chat  = NewChat(model, world, autoApply: true);

            await chat.SendAsync("hide the mount markers");

            Assert.Equal(new[] { "set bh.mounts/Show markers=false" }, world.Writes.ToArray());

            PlanReview review = Assert.IsType<PlanReview>(chat.Review);
            ReviewedAction left = Assert.Single(review.Actions);

            Assert.Equal("Icon size", left.TargetName);
            Assert.True(left.Action.IsSideEffect);
        }

        [Fact]
        public async Task After_an_Apply_the_model_is_told_what_happened_and_asked_again() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("The markers are off. Anything else?");

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("turn off the mount markers");
            await chat.ApplyAsync(new[] { 0 });

            // The Continuation is a second turn of the same Request, working from what the Apply did.
            Assert.Equal(2, model.Requests.Count);

            string history = string.Join(" ", model.Requests.Last().Select(message => message.Text));

            Assert.Contains("applied the plan you proposed", history);
            Assert.Contains("Mounts: set \"Show markers\" to false", history);
            Assert.Equal("The markers are off. Anything else?", chat.Messages.Last().Text);
        }

        [Fact]
        public async Task After_an_Apply_the_AI_reads_the_state_the_Apply_made() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillCallTool("get_module_settings", "call_2",
                               new Dictionary<string, object> { ["namespace"] = "bh.mounts" });
            model.WillReply("The markers are off now.");

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("turn off the mount markers");
            await chat.ApplyAsync(new[] { 0 });

            AiChatMessage toolAnswer = model.Requests.Last().Last(message => message.Role.Equals(ChatRole.Tool));
            string result = string.Join(" ", toolAnswer.Contents.OfType<FunctionResultContent>()
                                                        .Select(content => content.Result as string));

            Assert.Contains("\"value\":\"false\"", result);
            Assert.Equal("The markers are off now.", chat.Messages.Last().Text);
        }

        [Fact]
        public async Task An_Advice_is_shown_but_is_not_something_to_apply() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("I cannot do that myself.",
                                    Action("advice", reason: "Ask a friend who owns the guild hall.")));

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("get me into the guild hall");

            ReviewedAction advice = Assert.Single(chat.Review.Actions);

            Assert.True(advice.IsAccepted);
            Assert.False(advice.CanApply);
            Assert.Contains("Ask a friend who owns the guild hall.", advice.Summary);
        }

        [Fact]
        public async Task A_kind_this_version_does_not_know_is_refused_with_a_reason() {
            ReviewedAction action = await ReviewOneAsync(Action("install_module", "bh.pathing", reason: "You need it."));

            Assert.False(action.IsAccepted);
            Assert.Contains("install_module", action.Problem);
            Assert.Contains("cannot carry out", action.Problem);
        }

        [Fact]
        public async Task A_field_an_Action_kind_does_not_use_is_never_applied_as_a_Setting() {
            // A real model added "key": "enabled" to an enable_module Action (ticket #6, comment 2).
            var action = Action("enable_module", "bh.pathing", reason: "Pathing draws the trails.");
            action["key"]   = "enabled";
            action["value"] = "true";

            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1", Plan("Enabling Pathing.", action));
            model.WillReply("Enabled.");

            var world = PlayerWorld();
            var chat  = NewChat(model, world);

            await chat.SendAsync("show me the trails");
            await chat.ApplyAsync(new[] { 0 });

            Assert.Equal(new[] { "enable bh.pathing" }, world.Writes.ToArray());
            Assert.Equal(0, world.SettingWrites);
        }

        [Fact]
        public async Task A_new_Chat_keeps_neither_the_Review_nor_the_earlier_plan() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                               Plan("Turning the mount markers off.",
                                    Action("set_setting", "bh.mounts", key: "Show markers", value: "false", reason: "You asked.")));
            model.WillReply("Done.");
            model.WillReply("Fresh start.");

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("turn off the mount markers");
            await chat.ApplyAsync(new[] { 0 });

            chat.StartNewChat();

            await chat.SendAsync("hello");

            Assert.Null(chat.Review);
            Assert.Equal(new[] { "hello" }, WithoutTheIdentityMessage(model.Requests.Last()));

            // Undo is about the Player's configuration, not the conversation, so it survives.
            Assert.True(chat.CanUndo);
        }

        /// <summary>
        /// The world a test plays in: three Modules, one of them disabled, and enough kinds of Setting
        /// to try a boolean, a number with a range and a list of choices.
        /// </summary>
        private static FakeWorld PlayerWorld() {
            var world = new FakeWorld();

            world.Modules.Add(new InstalledModuleInfo("bh.mounts", "Mounts", "1.0.0", "Mount settings.", enabled: true));
            world.Modules.Add(new InstalledModuleInfo("bh.eventtable", "Event Table", "1.1.0", "Event timers.", enabled: true));
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "Markers and trails.", enabled: false));

            world.SettingsByNamespace["bh.mounts"] = FakeWorld.Ok(
                "Mounts",
                new ModuleSettingInfo("Show markers", "Show markers", "Draws the mount markers.", "Boolean", "true", null, null),
                new ModuleSettingInfo("Icon size", "Icon size", "How big the icons are.", "Int32", "3", "1", "5"),
                new ModuleSettingInfo("Marker style", "Marker style", "How the markers look.", "MarkerStyle", "Arrow", null, null,
                                      new[] { "Arrow", "Circle", "Star" }));

            world.SettingsByNamespace["bh.eventtable"] = FakeWorld.Ok(
                "Event Table",
                new ModuleSettingInfo("Show all events", "Show all events", "Lists every event.", "Boolean", "true", null, null));

            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Disabled("Pathing");

            return world;
        }

        /// <summary>
        /// The Chat the way the module builds it: a scripted model, this version's Lookups and the
        /// <c>submit_plan</c> proposal, and the Plan pipeline against the same world.
        /// </summary>
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world, bool autoApply = false,
                                    FakeGameThread gameThread = null) {
            IBlishHudWorld target = gameThread == null
                                        ? (IBlishHudWorld)world
                                        : new GameThreadWorld(world, gameThread);

            var plans = new Plans(target, () => autoApply);
            var tools = Lookups.For(target);

            plans.AddTo(tools);

            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            tools, null, plans);
        }

        /// <summary>The arguments of a <c>submit_plan</c> call, the way a model sends them.</summary>
        private static IDictionary<string, object> Plan(string reply, params IDictionary<string, object>[] actions) {
            return new Dictionary<string, object> {
                ["reply"]   = reply,
                ["actions"] = actions.Cast<object>().ToList()
            };
        }

        /// <summary>One Action of a <c>submit_plan</c> call.</summary>
        private static IDictionary<string, object> Action(string kind, string module = null, string key = null,
                                                          string value = null, string reason = "", bool sideEffect = false) {
            var action = new Dictionary<string, object> {
                ["kind"]         = kind,
                ["reason"]       = reason,
                ["is_side_effect"] = sideEffect
            };

            if (module != null) action["module"] = module;
            if (key != null)    action["key"]    = key;
            if (value != null)  action["value"]  = value;

            return action;
        }

        /// <summary>
        /// Sends one Request whose answer is a Plan with that one Action, and gives back what the
        /// Review made of it.
        /// </summary>
        private static async Task<ReviewedAction> ReviewOneAsync(IDictionary<string, object> action) {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1", Plan("Here is what I suggest.", action));

            var chat = NewChat(model, PlayerWorld());

            await chat.SendAsync("a request");

            return Assert.Single(chat.Review.Actions);
        }

        private static async Task<string> SettingValueAsync(FakeWorld world, string @namespace, string key) {
            ModuleSettingsResult result = await world.GetModuleSettingsAsync(@namespace, default(CancellationToken));

            return SettingWalk.Flatten(result.Settings).Single(setting => setting.Key == key).Value;
        }

        private static bool Enabled(FakeWorld world, string @namespace) {
            return world.Modules.Single(module => module.Namespace == @namespace).Enabled;
        }

        private static IEnumerable<string> WithoutTheIdentityMessage(IReadOnlyList<AiChatMessage> request) {
            return request.Where(message => !message.Role.Equals(ChatRole.System)).Select(message => message.Text);
        }

        /// <summary>
        /// Runs the game thread until <paramref name="task"/> is done, the way a running game turns
        /// its update loop while something waits on it.
        /// </summary>
        private static async Task PumpUntil(Task task, FakeGameThread gameThread) {
            for (int attempt = 0; attempt < 500 && !task.IsCompleted; attempt++) {
                gameThread.Pump();

                await Task.Delay(5);
            }

            await task;
        }

        /// <summary>Waits for a condition elsewhere in the machinery, so a test need not sleep.</summary>
        private static async Task WaitUntil(Func<bool> condition) {
            for (int attempt = 0; attempt < 500 && !condition(); attempt++) {
                await Task.Delay(5);
            }

            Assert.True(condition());
        }
    }
}

