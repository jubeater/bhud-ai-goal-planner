using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ChatMessage = jubeater.aigoalplanner.Core.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the seam the rest of the module is built on: the Chat. Everything here is driven the
    /// way a Player drives it and checked the way a Player would read it back.
    /// </summary>
    public class ChatTests
    {
        [Fact]
        public async Task Sending_a_Request_shows_it_and_the_models_reply_in_the_Chat() {
            var model = new ScriptedChatClient();
            model.WillReply("Try the Pathing module for hero point trails.");

            var chat = NewChat(model);

            await chat.SendAsync("I want to finish the hero points on this map");

            Assert.Collection(chat.Messages,
                              request => {
                                  Assert.Equal(ChatMessageAuthor.Player, request.Author);
                                  Assert.Equal("I want to finish the hero points on this map", request.Text);
                              },
                              answer => {
                                  Assert.Equal(ChatMessageAuthor.Ai, answer.Author);
                                  Assert.Equal("Try the Pathing module for hero point trails.", answer.Text);
                              });
        }

        [Fact]
        public async Task The_model_sees_the_whole_Chat_so_far_and_nothing_is_replaced() {
            var model = new ScriptedChatClient();
            model.WillReply("First answer.");
            model.WillReply("Second answer.");

            var chat = NewChat(model);

            await chat.SendAsync("First request");
            await chat.SendAsync("Second request");

            Assert.Equal(4, chat.Messages.Count);
            Assert.Equal("First request",  chat.Messages[0].Text);
            Assert.Equal("First answer.",  chat.Messages[1].Text);
            Assert.Equal("Second request", chat.Messages[2].Text);
            Assert.Equal("Second answer.", chat.Messages[3].Text);

            Assert.Equal(new[] { "First request" },
                         WithoutTheIdentityMessage(model.Requests[0]));

            Assert.Equal(new[] { "First request", "First answer.", "Second request" },
                         WithoutTheIdentityMessage(model.Requests[1]));
        }

        [Fact]
        public async Task A_cut_off_reply_is_reported_as_a_notice_not_as_an_ai_reply() {
            var model = new ScriptedChatClient();
            model.WillStop(ChatFinishReason.Length, "The reply was cut");

            var chat = NewChat(model);

            await chat.SendAsync("Tell me everything");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("cut off", notice.Text);
                              });
        }

        [Fact]
        public async Task A_refused_reply_is_reported_as_a_notice_not_as_an_ai_reply() {
            var model = new ScriptedChatClient();
            model.WillStop(ChatFinishReason.ContentFilter, "I can't help with that.");

            var chat = NewChat(model);

            await chat.SendAsync("Something the model refuses");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("refused", notice.Text);
                              });
        }

        [Fact]
        public async Task Rate_limiting_is_explained_with_a_retry_hint() {
            var model = new ScriptedChatClient();
            model.WillFail(ProviderFailure.RateLimited);

            var chat = NewChat(model);

            await chat.SendAsync("Hello");

            ChatMessage notice = Assert.Single(chat.Messages, message => message.Author == ChatMessageAuthor.Notice);
            Assert.Contains("Wait", notice.Text);
            Assert.Contains("again", notice.Text);
        }

        [Fact]
        public async Task A_Request_after_a_failure_keeps_going_and_notices_are_not_sent_to_the_model() {
            var model = new ScriptedChatClient();
            model.WillFail(ProviderFailure.RateLimited);
            model.WillReply("Answered now.");

            var chat = NewChat(model);

            await chat.SendAsync("First request");
            await chat.SendAsync("Second request");

            ChatMessage last = chat.Messages.Last();
            Assert.Equal(ChatMessageAuthor.Ai, last.Author);
            Assert.Equal("Answered now.", last.Text);

            Assert.Equal(new[] { "First request", "Second request" },
                         WithoutTheIdentityMessage(model.Requests[1]));
        }

        [Fact]
        public async Task New_chat_clears_what_the_Player_said_before() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = NewChat(model);
            await chat.SendAsync("Show only world bosses in the event table");

            chat.StartNewChat();

            Assert.Empty(chat.Messages);
        }

        [Fact]
        public async Task A_Request_after_New_chat_starts_a_fresh_Chat() {
            var model = new ScriptedChatClient();
            model.WillReply("Old answer.");
            model.WillReply("New answer.");

            var chat = NewChat(model);
            await chat.SendAsync("Finish the hero points on this map");

            chat.StartNewChat();
            await chat.SendAsync("Level an alt");

            Assert.Equal(2, chat.Messages.Count);
            Assert.Equal(ChatMessageAuthor.Player, chat.Messages[0].Author);
            Assert.Equal("Level an alt",           chat.Messages[0].Text);
            Assert.Single(WithoutTheIdentityMessage(model.Requests[1]));
        }

        [Fact]
        public async Task A_Request_queued_before_New_chat_is_dropped_instead_of_landing_in_the_new_Chat() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer for the old Chat.");
            model.WillReply("An answer for the new Chat.");
            model.WillHold();

            var chat = NewChat(model);

            // The Player sends, and the model takes its time with this Request.
            Task first = chat.SendAsync("Finish the hero points on this map");

            await WaitUntilTheModelWasAsked(model, times: 1);

            // The Player sends again, which queues a second Request behind the first, and then
            // starts over before either one comes back.
            Task second = chat.SendAsync("Level an alt");

            chat.StartNewChat();

            model.Release();

            await first;
            await second;

            // Neither the old reply nor the queued Request belongs to the new Chat, and the model
            // never saw the queued Request at all.
            Assert.Empty(chat.Messages);
            Assert.Single(model.Requests);
        }

        [Fact]
        public async Task A_reply_that_is_ready_when_the_Player_starts_over_lands_nowhere() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer for the old Chat.");
            model.WillReply("An answer for the new Chat.");

            var chat = NewChat(model);

            // The model has its reply in hand, and the Player starts over before the Chat is given
            // it: the reply belongs to the old Chat, and joining the new one would put it in front
            // of the model on the next Request.
            model.WillRunWhenTheReplyIsReady(chat.StartNewChat);

            await chat.SendAsync("Finish the hero points on this map");

            Assert.Empty(chat.Messages);

            await chat.SendAsync("Level an alt");

            // The new Chat is what it looks like: the Request the Player just sent and its answer.
            Assert.Equal(2, chat.Messages.Count);
            Assert.Equal("Level an alt", chat.Messages[0].Text);
            Assert.Equal("An answer for the new Chat.", chat.Messages[1].Text);

            // And the old reply was not sent to the model with it.
            Assert.Equal(new[] { "Level an alt" }, WithoutTheIdentityMessage(model.Requests[1]));
        }

        [Fact]
        public async Task A_failure_that_is_ready_when_the_Player_starts_over_leaves_no_Notice() {
            var model = new ScriptedChatClient();
            model.WillFail(ProviderFailure.Network);

            var chat = NewChat(model);
            model.WillRunWhenTheReplyIsReady(chat.StartNewChat);

            await chat.SendAsync("Finish the hero points on this map");

            // The Request and the Notice about it both belong to the Chat the Player left.
            Assert.Empty(chat.Messages);
        }

        [Fact]
        public async Task Sending_nothing_adds_nothing_to_the_Chat() {
            var model = new ScriptedChatClient();
            var chat  = NewChat(model);

            await chat.SendAsync(string.Empty);
            await chat.SendAsync("   ");

            Assert.Empty(chat.Messages);
            Assert.Empty(model.Requests);
        }

        [Theory]
        [InlineData(ProviderFailure.NotConfigured,   "Connect a Provider")]
        [InlineData(ProviderFailure.InvalidEndpoint, "endpoint URL is not valid")]
        [InlineData(ProviderFailure.InvalidKey,      "API key")]
        [InlineData(ProviderFailure.UnknownModel,    "model")]
        [InlineData(ProviderFailure.RateLimited,     "Wait a moment")]
        [InlineData(ProviderFailure.Network,         "internet connection")]
        [InlineData(ProviderFailure.ToolCallingNotSupported, "cannot call tools")]
        [InlineData(ProviderFailure.Unexpected,      "went wrong")]
        public async Task Each_kind_of_Provider_failure_is_explained_in_the_Chat(ProviderFailure failure, string expected) {
            var model = new ScriptedChatClient();
            model.WillFail(failure);

            var chat = NewChat(model);

            await chat.SendAsync("Hello");

            ChatMessage notice = Assert.Single(chat.Messages, message => message.Author == ChatMessageAuthor.Notice);
            Assert.Contains(expected, notice.Text);
        }

        [Fact]
        public async Task A_Request_is_shown_without_surrounding_whitespace() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = NewChat(model);

            await chat.SendAsync("  Get mastery points  ");

            Assert.Equal("Get mastery points", chat.Messages[0].Text);
        }

        [Fact]
        public async Task The_Chat_window_is_told_every_time_the_Chat_changes() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat  = NewChat(model);
            int raised = 0;
            chat.MessagesChanged += (sender, args) => raised++;

            await chat.SendAsync("Hello");

            Assert.Equal(2, raised);
        }

        [Fact]
        public async Task The_model_is_told_what_it_is_before_it_is_told_what_the_Player_wants() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            string identity = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");
            var chat = new Chat(() => model, () => identity);

            await chat.SendAsync("Hello");

            AiChatMessage first = model.Requests[0][0];

            Assert.Equal(ChatRole.System, first.Role);
            Assert.Equal(identity,        first.Text);
        }

        [Fact]
        public async Task The_model_is_told_what_it_is_once_and_first_on_every_Request() {
            var model = new ScriptedChatClient();
            model.WillReply("First answer.");
            model.WillReply("Second answer.");

            var chat = NewChat(model);

            await chat.SendAsync("First request");
            await chat.SendAsync("Second request");

            foreach (IReadOnlyList<AiChatMessage> request in model.Requests) {
                Assert.Equal(ChatRole.System, request[0].Role);
                Assert.Single(request, message => message.Role.Equals(ChatRole.System));
            }
        }

        [Fact]
        public async Task The_identity_message_is_not_part_of_what_the_Player_reads() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            string identity = SystemPrompt.For("beam:cloud", "https://ollama.com/v1");
            var chat = new Chat(() => model, () => identity);

            await chat.SendAsync("Hello");

            Assert.Equal(2, chat.Messages.Count);
            Assert.DoesNotContain(chat.Messages, message => message.Text.Contains("AI Goal Planner"));
        }


        [Fact]
        public async Task A_fake_model_that_passes_the_tool_calling_test_is_accepted_in_the_Chat() {
            var model = new ScriptedChatClient();
            model.WillCallTool("test_tool");
            model.WillReply("I can help with hero points.");

            var store = new ToolCallingCapabilityStore();
            var chat = new Chat(() => new ToolCallingChatClient(model, "https://ollama.com/v1", "beam:cloud", store),
                                () => SystemPrompt.For("beam:cloud", "https://ollama.com/v1"));

            await chat.SendAsync("Hero points please");

            Assert.Collection(chat.Messages,
                              request => {
                                  Assert.Equal(ChatMessageAuthor.Player, request.Author);
                                  Assert.Equal("Hero points please", request.Text);
                              },
                              reply => {
                                  Assert.Equal(ChatMessageAuthor.Ai, reply.Author);
                                  Assert.Equal("I can help with hero points.", reply.Text);
                              });
            Assert.Equal(2, model.Requests.Count);
        }

        [Fact]
        public async Task A_fake_model_that_fails_the_tool_calling_test_is_refused_with_a_Notice() {
            var model = new ScriptedChatClient();
            model.WillReply("I cannot call tools.");

            var store = new ToolCallingCapabilityStore();
            var chat = new Chat(() => new ToolCallingChatClient(model, "https://ollama.com/v1", "beam:cloud", store),
                                () => SystemPrompt.For("beam:cloud", "https://ollama.com/v1"));

            await chat.SendAsync("Hero points please");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("cannot call tools", notice.Text);
                              });
        }

        [Fact]
        public async Task A_model_refused_for_tool_calling_is_refused_immediately_on_the_next_Request() {
            var model = new ScriptedChatClient();
            model.WillReply("I cannot call tools.");

            var store = new ToolCallingCapabilityStore();
            var chat = new Chat(() => new ToolCallingChatClient(model, "https://ollama.com/v1", "beam:cloud", store),
                                () => SystemPrompt.For("beam:cloud", "https://ollama.com/v1"));

            await chat.SendAsync("First request");
            await chat.SendAsync("Second request");

            Assert.Equal(4, chat.Messages.Count);
            Assert.Equal(ChatMessageAuthor.Notice, chat.Messages[1].Author);
            Assert.Equal(ChatMessageAuthor.Notice, chat.Messages[3].Author);
            Assert.Single(model.Requests);
        }

        [Fact]
        public async Task Subsequent_Requests_to_an_accepted_model_do_not_retest_tool_calling() {
            var model = new ScriptedChatClient();
            model.WillCallTool("test_tool");
            model.WillReply("First answer.");
            model.WillReply("Second answer.");

            var store = new ToolCallingCapabilityStore();
            var chat = new Chat(() => new ToolCallingChatClient(model, "https://ollama.com/v1", "beam:cloud", store),
                                () => SystemPrompt.For("beam:cloud", "https://ollama.com/v1"));

            await chat.SendAsync("First request");
            await chat.SendAsync("Second request");

            Assert.Equal(3, model.Requests.Count);
            Assert.Equal("Second answer.", chat.Messages.Last().Text);
        }

        [Fact]
        public async Task A_Lookup_runs_and_its_result_goes_back_to_the_model_before_the_reply() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "Shows markers and trails.", enabled: true));

            model.WillCallTool("list_installed_modules", "call_1");
            model.WillReply("You have Pathing installed.");

            var chat = NewChat(model, world);

            await chat.SendAsync("What modules do I have?");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              answer => Assert.Equal("You have Pathing installed.", answer.Text));

            // The model saw its own tool call and the Lookup's answer before it was asked again.
            Assert.Equal(4, model.Requests[1].Count);
            Assert.True(model.Requests[1][2].Contents.OfType<FunctionCallContent>()
                            .Any(call => call.Name == "list_installed_modules"));

            AiChatMessage toolAnswer = model.Requests[1][3];

            Assert.True(toolAnswer.Role.Equals(ChatRole.Tool));

            FunctionResultContent result = Assert.Single(toolAnswer.Contents.OfType<FunctionResultContent>());

            Assert.Contains("bh.pathing", ((JsonElement)result.Result).GetString());
        }

        [Fact]
        public async Task All_the_results_of_one_round_go_back_to_the_model_together() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));

            model.WillCallTools(new FunctionCallContent("call_1", "list_installed_modules"),
                                new FunctionCallContent("call_2", "list_installed_modules"));
            model.WillReply("Checked twice, one module.");

            var chat = NewChat(model, world);

            await chat.SendAsync("What modules do I have?");

            Assert.Equal("Checked twice, one module.", chat.Messages.Last().Text);

            AiChatMessage toolAnswer = model.Requests[1].Single(message => message.Role.Equals(ChatRole.Tool));
            List<FunctionResultContent> results = toolAnswer.Contents.OfType<FunctionResultContent>().ToList();

            Assert.Equal(new[] { "call_1", "call_2" }, results.Select(result => result.CallId).ToArray());
            Assert.Equal(2, world.ListCalls);
        }

        [Fact]
        public async Task A_call_to_an_unknown_tool_tells_the_model_so_instead_of_crashing_the_Chat() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            model.WillCallTool("search_module_catalog", "call_1");
            model.WillReply("I cannot search the catalog in this version.");

            var chat = NewChat(model, world);

            await chat.SendAsync("Search the catalog for markers");

            Assert.Equal("I cannot search the catalog in this version.", chat.Messages.Last().Text);

            FunctionResultContent result = Assert.Single(
                model.Requests[1].Single(message => message.Role.Equals(ChatRole.Tool)).Contents.OfType<FunctionResultContent>());

            Assert.Null(result.Result);
            Assert.Contains("search_module_catalog", result.Exception.Message);
        }

        [Fact]
        public async Task A_runaway_tool_loop_stops_at_the_limit_with_a_clear_message() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            for (int round = 0; round < 20; round++) {
                model.WillCallTool("list_installed_modules", "call_" + round);
            }

            var chat = NewChat(model, world);

            await chat.SendAsync("What modules do I have?");

            // 15 Lookups ran; the model was asked once more, saw that the next one would be the
            // 16th, and the Chat stopped it there instead of answering again.
            Assert.Equal(16, model.Requests.Count);

            ChatMessage notice = Assert.Single(chat.Messages, message => message.Author == ChatMessageAuthor.Notice);

            Assert.Contains("15", notice.Text);
            Assert.Contains("Lookup", notice.Text);
        }

        [Fact]
        public async Task A_secret_Setting_never_reaches_the_model_even_when_it_is_asked_for_by_name() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.maps", "Maps", "1.0.0", "", enabled: true));
            world.SettingsByNamespace["bh.maps"] = FakeWorld.Ok(
                "Maps",
                new ModuleSettingInfo("api_key",   "API key",   "", "String",  "sk-live-123", null, null),
                new ModuleSettingInfo("show.grid", "Show grid", "", "Boolean", "true",       null, null));

            model.WillCallTool("get_module_settings", "call_1",
                               new Dictionary<string, object> { ["namespace"] = "bh.maps" });
            model.WillReply("The Maps module shows a grid.");

            var chat = NewChat(model, world);

            await chat.SendAsync("What is in the Maps module settings?");

            FunctionResultContent result = Assert.Single(
                model.Requests[1].Single(message => message.Role.Equals(ChatRole.Tool)).Contents.OfType<FunctionResultContent>());

            string answer = ((JsonElement)result.Result).GetString();

            Assert.DoesNotContain("sk-live-123", answer);
            Assert.Contains("show.grid", answer);
        }

        [Fact]
        public async Task Progress_lines_show_while_a_Lookup_runs_and_are_gone_when_the_reply_lands() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));
            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok(
                "Pathing",
                new ModuleSettingInfo("show.trails", "Show trails", "", "Boolean", "true", null, null));

            model.WillCallTool("get_module_settings", "call_1",
                               new Dictionary<string, object> { ["namespace"] = "bh.pathing" });
            model.WillReply("Pathing shows trails by default.");

            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            world.AnswerGetModuleSettings = (@namespace, cancellationToken) => HoldUntil(release.Task, world, @namespace, cancellationToken);

            var chat = NewChat(model, world);

            Task send = chat.SendAsync("What does Pathing's trail setting do?");

            await WaitUntil(() => world.SettingsCalls == 1);

            Assert.Contains(chat.ProgressLines, line => line.Contains("bh.pathing"));

            release.TrySetResult(true);
            await send;

            Assert.Equal("Pathing shows trails by default.", chat.Messages.Last().Text);
            Assert.Empty(chat.ProgressLines);
        }

        [Fact]
        public async Task Cancelling_stops_the_Request_while_a_Lookup_runs() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));
            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok(
                "Pathing",
                new ModuleSettingInfo("show.trails", "Show trails", "", "Boolean", "true", null, null));

            model.WillCallTool("get_module_settings", "call_1",
                               new Dictionary<string, object> { ["namespace"] = "bh.pathing" });
            model.WillReply("Never reached.");

            var neverReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            world.AnswerGetModuleSettings = (@namespace, cancellationToken) => HoldUntil(neverReleased.Task, world, @namespace, cancellationToken);

            var chat = NewChat(model, world);

            using (var cancellation = new CancellationTokenSource()) {
                Task send = chat.SendAsync("What does Pathing's trail setting do?", cancellation.Token);

                await WaitUntil(() => world.SettingsCalls == 1);

                cancellation.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);

                // The Player's Request stays in the Chat, nothing answered it, and no further round
                // was asked of the model. The progress is gone with it.
                Assert.Equal(1, chat.Messages.Count);
                Assert.Equal(ChatMessageAuthor.Player, chat.Messages[0].Author);
                Assert.Equal(1, model.Requests.Count);
                Assert.Empty(chat.ProgressLines);
            }
        }

        /// <summary>
        /// A settings Lookup that waits for <paramref name="release"/> and is cut short by
        /// cancellation, so a test holds the Chat in the middle of a Lookup.
        /// </summary>
        private static async Task<ModuleSettingsResult> HoldUntil(Task<bool> release, FakeWorld world, string @namespace,
                                                                  CancellationToken cancellationToken) {
            // The task that is awaited is the one the token cancels: cancelling must end the Lookup
            // the Chat is waiting for, not a side task nobody watches.
            await Task.WhenAny(release, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            return world.SettingsByNamespace[@namespace];
        }

        /// <summary>
        /// The Chat the way the module builds it: a scripted model, and the identity message that
        /// says which product the model is part of.
        /// </summary>
        private static Chat NewChat(ScriptedChatClient model) {
            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"));
        }

        /// <summary>
        /// The Chat with Lookups: the scripted model and this version's Lookups against an in-memory
        /// world, wired the way the module wires them.
        /// </summary>
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world) {
            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"), Lookups.For(world));
        }

        /// <summary>
        /// What the model was told apart from the identity message it is always sent first.
        /// </summary>
        private static IEnumerable<string> WithoutTheIdentityMessage(IReadOnlyList<AiChatMessage> request) {
            return request.Where(message => !message.Role.Equals(ChatRole.System))
                          .Select(message => message.Text);
        }

        /// <summary>
        /// Waits until the scripted model has been asked, so a test that sends something while
        /// another Request is in flight does not depend on how quickly that thread gets there.
        /// </summary>
        private static async Task WaitUntilTheModelWasAsked(ScriptedChatClient model, int times) {
            for (int attempt = 0; attempt < 200 && model.Requests.Count < times; attempt++) {
                await Task.Delay(10);
            }

            Assert.Equal(times, model.Requests.Count);
        }

        /// <summary>
        /// Waits for a condition somewhere else in the machinery, so a test can look at the Chat in
        /// the middle of a Request without guessing how fast the threads get there.
        /// </summary>
        private static async Task WaitUntil(Func<bool> condition) {
            for (int attempt = 0; attempt < 500 && !condition(); attempt++) {
                await Task.Delay(10);
            }

            Assert.True(condition());
        }
    }
}
