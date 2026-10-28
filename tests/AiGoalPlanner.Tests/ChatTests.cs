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
                                  Assert.Equal(ChatMessageAuthor.Ai, answer.Text != null ? ChatMessageAuthor.Ai : ChatMessageAuthor.Notice);
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
        public async Task A_Notice_the_module_shows_itself_is_read_by_the_Player_and_never_by_the_model() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = NewChat(model);

            chat.ShowNotice("AI Goal Planner failed to load and cannot answer this session.");

            Assert.Equal(ChatMessageAuthor.Notice, Assert.Single(chat.Messages).Author);

            await chat.SendAsync("Hello");

            // The Player reads the Notice, but it is not part of the conversation the model sees.
            Assert.Equal(new[] { "Hello" }, WithoutTheIdentityMessage(model.Requests[0]));
        }

        [Fact]
        public void A_freshly_made_unavailable_Chat_shows_its_reason_from_the_start() {
            var chat = Chat.Unavailable("AI Goal Planner failed to load and cannot answer this session.");

            // The Player sees why the Chat is dead the first time the window opens, not after a
            // first Request or a New chat.
            ChatMessage notice = Assert.Single(chat.Messages);

            Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
            Assert.Equal("AI Goal Planner failed to load and cannot answer this session.", notice.Text);
        }

        [Fact]
        public async Task An_unavailable_Chat_never_reaches_a_Provider_and_answers_every_Request_with_its_reason() {
            // There is no client to call: Chat.Unavailable takes none, so a Provider cannot be
            // reached by construction.
            var chat = Chat.Unavailable("AI Goal Planner failed to load and cannot answer this session.");

            await chat.SendAsync("What modules do I have?");
            await chat.SendAsync("And now?");

            Assert.Collection(chat.Messages,
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("failed to load", notice.Text);
                              },
                              request => {
                                  Assert.Equal(ChatMessageAuthor.Player, request.Author);
                                  Assert.Equal("What modules do I have?", request.Text);
                              },
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("failed to load", notice.Text);
                              },
                              request => {
                                  Assert.Equal(ChatMessageAuthor.Player, request.Author);
                                  Assert.Equal("And now?", request.Text);
                              },
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("failed to load", notice.Text);
                              });
        }

        [Fact]
        public async Task Starting_over_on_an_unavailable_Chat_shows_the_reason_again() {
            var chat = Chat.Unavailable("AI Goal Planner failed to load and cannot answer this session.");

            await chat.SendAsync("Hello");

            chat.StartNewChat();

            // The fresh Chat is empty of conversation, but the failure is not cleared away.
            ChatMessage notice = Assert.Single(chat.Messages);

            Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
            Assert.Equal("AI Goal Planner failed to load and cannot answer this session.", notice.Text);

            Assert.True(chat.IsUnavailable);
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

            Assert.Contains("bh.pathing", (string)result.Result);
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
        public async Task A_cut_off_reply_that_tries_a_Lookup_shows_its_notice_and_runs_nothing() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));

            model.WillCallTool("list_installed_modules", "call_1", finishReason: ChatFinishReason.Length);

            var chat = NewChat(model, world);

            await chat.SendAsync("What modules do I have?");

            // The reply was cut off mid-thought, so its tool call is not run, and no further
            // Request goes out to the model. The Notice is the one a plain cut-off reply gets.
            Assert.Equal(1, model.Requests.Count);
            Assert.Equal(0, world.ListCalls);

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("cut off", notice.Text);
                              });
        }

        [Fact]
        public async Task A_refused_reply_that_tries_a_Lookup_shows_its_notice_and_runs_nothing() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));

            model.WillCallTool("list_installed_modules", "call_1", finishReason: ChatFinishReason.ContentFilter);

            var chat = NewChat(model, world);

            await chat.SendAsync("What modules do I have?");

            Assert.Equal(1, model.Requests.Count);
            Assert.Equal(0, world.ListCalls);

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("refused", notice.Text);
                              });
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

            string answer = (string)result.Result;

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

        [Fact]
        public async Task A_Lookup_waits_for_the_game_thread_and_answers_once_it_has_run() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));

            model.WillCallTool("list_installed_modules", "call_1");
            model.WillReply("You have Pathing installed.");

            var gameThread = new FakeGameThread();
            var chat       = NewChat(model, world, gameThread);

            Task send = chat.SendAsync("What modules do I have?");

            // The read is queued for the game thread instead of being read where the Chat thinks:
            // nothing has been read yet, and the Player only has their own Request to look at.
            await WaitUntil(() => gameThread.WaitingCount == 1);

            Assert.Equal(0, world.ListCalls);
            Assert.Single(chat.Messages);

            gameThread.Pump();
            await send;

            Assert.Equal(1, world.ListCalls);
            Assert.Equal("You have Pathing installed.", chat.Messages.Last().Text);
        }

        [Fact]
        public async Task Cancelling_stops_the_Request_while_a_Lookup_waits_for_the_game_thread() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));
            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok(
                "Pathing",
                new ModuleSettingInfo("trail.size", "Trail size", "", "Int32", "3", "1", "10"));

            // A Settings read is the one that used to run another module's code off the game thread,
            // so it is the one held open here.
            model.WillCallTool("get_module_settings", "call_1",
                                new Dictionary<string, object> { ["namespace"] = "bh.pathing" });
            model.WillReply("Never reached.");

            var gameThread = new FakeGameThread();
            var chat       = NewChat(model, world, gameThread);

            using (var cancellation = new CancellationTokenSource()) {
                Task send = chat.SendAsync("How wide are Pathing's trails?", cancellation.Token);

                await WaitUntil(() => gameThread.WaitingCount == 1);

                cancellation.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);

                // The read never ran, the Player's Request stays and nothing answered it, and the
                // progress that said what was being checked is gone.
                Assert.Equal(0, world.SettingsCalls);
                Assert.Single(chat.Messages);
                Assert.Equal(ChatMessageAuthor.Player, chat.Messages[0].Author);
                Assert.Empty(chat.ProgressLines);
            }
        }

        [Fact]
        public async Task Each_Provider_round_trip_is_logged_with_its_duration_finish_reason_and_tool_calls() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));

            model.WillCallTool("list_installed_modules", "call_1");
            model.WillReply("You have Pathing installed.");

            var log  = new List<string>();
            var chat = NewChat(model, world, log.Add);

            await chat.SendAsync("What modules do I have?");

            // One line per round trip: the one that asked for the Lookup, and the one that answered.
            Assert.Equal(2, log.Count(line => line.Contains("Provider round trip")));

            Assert.Contains(log, line => line.Contains("finish_reason=tool_calls") && line.Contains("tool_calls=1"));
            Assert.Contains(log, line => line.Contains("finish_reason=stop")       && line.Contains("tool_calls=0"));
        }

        [Fact]
        public async Task A_Lookup_is_logged_with_its_name_module_duration_and_size() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));
            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok(
                "Pathing",
                new ModuleSettingInfo("trail.size", "Trail size", "How wide trails are drawn.", "Int32", "3", "1", "10"));

            model.WillCallTool("get_module_settings", "call_1",
                                new Dictionary<string, object> { ["namespace"] = "bh.pathing" });
            model.WillReply("Pathing draws trails three pixels wide.");

            var log  = new List<string>();
            var chat = NewChat(model, world, log.Add);

            await chat.SendAsync("How wide are Pathing's trails?");

            string lookup = Assert.Single(log, line => line.Contains("Lookup get_module_settings"));

            Assert.Contains("ms",               lookup);
            Assert.Contains("module=bh.pathing", lookup);
            Assert.Contains("settings=1",        lookup);
            Assert.Contains("chars",             lookup);
        }

        [Fact]
        public async Task No_Setting_value_reaches_the_log() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.maps", "Maps", "1.0.0", "", enabled: true));
            world.SettingsByNamespace["bh.maps"] = FakeWorld.Ok(
                "Maps",
                new ModuleSettingInfo("api_key",    "API key",    "", "String",  "sk-live-123", null, null),
                new ModuleSettingInfo("trail.size", "Trail size", "", "Int32",   "987654",      "1", "999999"),
                new ModuleSettingInfo("show.grid",  "Show grid",  "", "Boolean", "true",        null, null));

            model.WillCallTool("get_module_settings", "call_1",
                                new Dictionary<string, object> { ["namespace"] = "bh.maps" });
            model.WillReply("The Maps module shows a grid.");

            var log  = new List<string>();
            var chat = NewChat(model, world, log.Add);

            await chat.SendAsync("What is in the Maps module settings?");

            string everything = string.Join("\n", log);

            // The Lookup was logged...
            Assert.Contains("get_module_settings", everything);

            // ...and the settings themselves stayed out of it. The trail size is a value the model
            // is meant to see, so the log must not hold it either; the key never gets that far, and
            // must not appear for the other reason.
            Assert.DoesNotContain("987654",      everything);
            Assert.DoesNotContain("sk-live-123", everything);
            Assert.DoesNotContain("show.grid",   everything);
        }

        [Fact]
        public async Task A_namespace_the_model_made_up_never_reaches_the_log() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.maps", "Maps", "1.0.0", "", enabled: true));
            world.SettingsByNamespace["bh.maps"] = FakeWorld.Ok(
                "Maps",
                new ModuleSettingInfo("trail.size", "Trail size", "", "Int32", "987654", "1", "999999"));

            // The model asks for a module whose namespace is a Setting value it has just been shown.
            // Nothing stops it putting anything at all in that argument, so the log must not repeat
            // what it sent.
            model.WillCallTool("get_module_settings", "call_1",
                                new Dictionary<string, object> { ["namespace"] = "987654" });
            model.WillReply("I could not find a module with that namespace.");

            var log  = new List<string>();
            var chat = NewChat(model, world, log.Add);

            await chat.SendAsync("Try that setting again");

            string everything = string.Join("\n", log);

            Assert.Contains("module=<unknown>", everything);
            Assert.DoesNotContain("987654",      everything);
        }

        [Fact]
        public async Task A_finish_reason_the_Provider_made_up_never_reaches_the_log() {
            var model = new ScriptedChatClient();

            // A finish reason is whatever text the endpoint sends back, and an endpoint that is not
            // really a Provider can put an API key there.
            model.WillStop(new ChatFinishReason("sk-test-1234567890abcdef"), "A reply.");

            var log  = new List<string>();
            var chat = NewChat(model, new FakeWorld(), log.Add);

            await chat.SendAsync("Hello");

            string everything = string.Join("\n", log);

            Assert.Contains("finish_reason=other",          everything);
            Assert.DoesNotContain("sk-test-1234567890abcdef", everything);
        }

        [Fact]
        public async Task A_finish_reason_the_Provider_made_up_never_reaches_the_Player() {
            var model = new ScriptedChatClient();
            model.WillStop(new ChatFinishReason("sk-test-1234567890abcdef"), "A reply.");

            var chat = NewChat(model);

            await chat.SendAsync("Hello");

            // The same text would otherwise reach the Notice: the Player reads that like any other
            // line in the Chat, so it says what happened in words of this module's own.
            ChatMessage notice = Assert.Single(chat.Messages, message => message.Author == ChatMessageAuthor.Notice);

            Assert.DoesNotContain("sk-test-1234567890abcdef", notice.Text);
            Assert.Contains("stopped", notice.Text);
        }

        [Fact]
        public async Task When_a_Lookup_reports_a_missing_permission_a_Notice_is_shown_and_planning_continues() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.MasteryProgressResult = new ProgressResult(ProgressStatus.MissingPermission, ProgressKind.Mastery, missingPermission: "progression");

            var warnings = new List<string>();

            // Round 1: Model calls get_progress(kind="mastery")
            model.WillCallTool("get_progress", "call_1", new Dictionary<string, object> { ["kind"] = "mastery" });
            // Round 2: Model receives unavailable notice and replies with general advice
            model.WillReply("Since progression permission is missing, here is a general Central Tyria plan.");

            var chat = NewChat(model, world, logDebug: null, logWarn: warnings.Add);

            await chat.SendAsync("What masteries should I train?");

            // Chat contains: Player request, Notice from missing permission, and AI reply
            Assert.Collection(chat.Messages,
                              request => {
                                  Assert.Equal(ChatMessageAuthor.Player, request.Author);
                                  Assert.Equal("What masteries should I train?", request.Text);
                              },
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("progression", notice.Text);
                                  Assert.Equal("Your Guild Wars 2 API key lacks the 'progression' permission. Planning continues with available data.", notice.Text);
                              },
                              reply => {
                                  Assert.Equal(ChatMessageAuthor.Ai, reply.Author);
                                  Assert.Equal("Since progression permission is missing, here is a general Central Tyria plan.", reply.Text);
                              });

            // Warning was logged at Warn (not Error)
            string warning = Assert.Single(warnings);
            Assert.Contains("Lookup get_progress", warning);
            Assert.Contains("progression", warning);

            // The model saw the tool result with unavailable status and instruction
            AiChatMessage toolAnswer = model.Requests[1].Single(message => message.Role.Equals(ChatRole.Tool));
            FunctionResultContent result = Assert.Single(toolAnswer.Contents.OfType<FunctionResultContent>());
            string resultJson = (string)result.Result;
            Assert.Contains("\"status\":\"unavailable\"", resultJson);
            Assert.Contains("\"reason\":\"missing_permission\"", resultJson);
            Assert.Contains("\"permission\":\"progression\"", resultJson);
        }

        [Fact]
        public async Task When_get_account_access_reports_a_missing_permission_a_Notice_is_shown_and_planning_continues() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.AccountAccessResult = new AccountAccessResult(AccountAccessStatus.MissingPermission, null, missingPermission: "account");

            var warnings = new List<string>();

            model.WillCallTool("get_account_access", "call_1");
            model.WillReply("Since account permission is missing, assuming Core game access.");

            var chat = NewChat(model, world, logDebug: null, logWarn: warnings.Add);

            await chat.SendAsync("What can I play?");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("account", notice.Text);
                                  Assert.Equal("Your Guild Wars 2 API key lacks the 'account' permission. Planning continues with available data.", notice.Text);
                              },
                              reply => {
                                  Assert.Equal(ChatMessageAuthor.Ai, reply.Author);
                                  Assert.Contains("Core game", reply.Text);
                              });

            string warning = Assert.Single(warnings);
            Assert.Contains("Lookup get_account_access", warning);
            Assert.Contains("account", warning);
        }

        [Fact]
        public async Task When_no_api_access_a_Notice_is_shown_to_restart_blish_hud_and_planning_continues() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.AccountAccessResult = new AccountAccessResult(AccountAccessStatus.NoApiAccess, null);

            var warnings = new List<string>();

            model.WillCallTool("get_account_access", "call_1");
            model.WillReply("Since API access has not been granted, continuing with available data.");

            var chat = NewChat(model, world, logDebug: null, logWarn: warnings.Add);

            await chat.SendAsync("What expansions do I own?");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Equal("Blish HUD hasn't given AI Goal Planner API access yet. Make sure a Guild Wars 2 API key is added in Blish HUD, then restart Blish HUD.", notice.Text);
                              },
                              reply => {
                                  Assert.Equal(ChatMessageAuthor.Ai, reply.Author);
                                  Assert.Contains("continuing", reply.Text);
                              });

            string warning = Assert.Single(warnings);
            Assert.Contains("Lookup get_account_access", warning);
            Assert.Contains("Blish HUD hasn't given AI Goal Planner API access yet", warning);
        }

        [Fact]
        public async Task When_hero_points_lookup_lacks_progression_permission_a_Notice_is_shown_and_planning_continues() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.HeroPointsProgressResult = new ProgressResult(ProgressStatus.MissingPermission, ProgressKind.HeroPoints, missingPermission: "progression");

            var warnings = new List<string>();

            model.WillCallTool("get_progress", "call_1", new Dictionary<string, object> { ["kind"] = "hero_points" });
            model.WillReply("Since progression permission is missing, I cannot read hero challenges.");

            var chat = NewChat(model, world, logDebug: null, logWarn: warnings.Add);

            await chat.SendAsync("What hero points do I need?");

            Assert.Collection(chat.Messages,
                              request => {
                                  Assert.Equal(ChatMessageAuthor.Player, request.Author);
                                  Assert.Equal("What hero points do I need?", request.Text);
                              },
                              notice => {
                                  Assert.Equal(ChatMessageAuthor.Notice, notice.Author);
                                  Assert.Contains("progression", notice.Text);
                                  Assert.Equal("Your Guild Wars 2 API key lacks the 'progression' permission. Planning continues with available data.", notice.Text);
                              },
                              reply => {
                                  Assert.Equal(ChatMessageAuthor.Ai, reply.Author);
                                  Assert.Contains("progression permission is missing", reply.Text);
                              });

            string warning = Assert.Single(warnings);
            Assert.Contains("Lookup get_progress", warning);
            Assert.Contains("progression", warning);
        }

        [Fact]
        public async Task Owned_Content_is_passed_through_to_the_model_and_influences_its_reply() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.AccountAccessResult = new AccountAccessResult(AccountAccessStatus.Ok, new[] { "GuildWars2", "HeartOfThorns", "PathOfFire" });

            model.WillCallTool("get_account_access", "call_1");
            model.WillReply("You own GuildWars2, HeartOfThorns, and PathOfFire. You can unlock mounts in the Crystal Desert.");

            var chat = NewChat(model, world);

            await chat.SendAsync("What content do I own?");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              reply => {
                                  Assert.Equal(ChatMessageAuthor.Ai, reply.Author);
                                  Assert.Contains("PathOfFire", reply.Text);
                              });

            Assert.Equal(1, world.AccountAccessCalls);

            AiChatMessage toolAnswer = model.Requests[1].Single(message => message.Role.Equals(ChatRole.Tool));
            FunctionResultContent result = Assert.Single(toolAnswer.Contents.OfType<FunctionResultContent>());
            string resultJson = (string)result.Result;
            Assert.Contains("\"status\":\"ok\"", resultJson);
            Assert.Contains("\"owned_content\":[\"GuildWars2\",\"HeartOfThorns\",\"PathOfFire\"]", resultJson);
        }

        [Fact]
        public async Task Location_is_passed_through_to_the_model_and_influences_its_reply() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.LocationResult = new PlayerLocationResult(LocationStatus.Ok, new PlayerLocation("Test Character", 1205, "Domain of Vabbi", 10, "Crystal Desert"));

            model.WillCallTool("get_location", "call_1");
            model.WillReply("You are on Domain of Vabbi in the Crystal Desert as Test Character.");

            var chat = NewChat(model, world);

            await chat.SendAsync("Where am I?");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              reply => {
                                  Assert.Equal(ChatMessageAuthor.Ai, reply.Author);
                                  Assert.Contains("Domain of Vabbi", reply.Text);
                              });

            Assert.Equal(1, world.LocationCalls);

            AiChatMessage toolAnswer = model.Requests[1].Single(message => message.Role.Equals(ChatRole.Tool));
            FunctionResultContent result = Assert.Single(toolAnswer.Contents.OfType<FunctionResultContent>());
            string resultJson = (string)result.Result;
            Assert.Contains("\"character\":\"Test Character\"", resultJson);
            Assert.Contains("\"map\":\"Domain of Vabbi\"", resultJson);
            Assert.Contains("\"region\":\"Crystal Desert\"", resultJson);
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
        /// The Chat with Lookups that log, wired the way the module wires them: the same log takes
        /// the Lookup lines and the Provider round trips.
        /// </summary>
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world, Action<string> logDebug) {
            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            Lookups.For(world, logDebug), logDebug);
        }

        /// <summary>
        /// The Chat with Lookups and diagnostic + warning logging.
        /// </summary>
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world, Action<string> logDebug, Action<string> logWarn) {
            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            Lookups.For(world, logDebug), logDebug, logWarn);
        }

        /// <summary>
        /// The Chat with Lookups that read on a game thread the test runs itself.
        /// </summary>
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world, FakeGameThread gameThread) {
            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            Lookups.For(new GameThreadWorld(world, gameThread)));
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

        [Fact]
        public async Task Find_marker_categories_runs_and_informs_the_reply() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));
            world.MarkerCategoriesResult = new MarkerCategoriesResult(
                MarkerCategoriesStatus.Ok, 1210, "Crystal Oasis", new[] {
                    new MarkerCategoryMatch(
                        "Tekkit's Guides",
                        "Tekkit's Guides › Path of Fire › Routes - Hero Points Only",
                        "tw_guides.tw_mc_pof.tw_mc_pof_trails_hp",
                        shown: false,
                        markerCount: 2,
                        trailCount: 1)
                });

            model.WillCallTool("find_marker_categories", "call_1",
                               new Dictionary<string, object> { ["search"] = "hero point routes" });
            model.WillReply("In Pathing, turn on Tekkit's Guides › Path of Fire › Routes - Hero Points Only. It's off right now, and it has 3 routes on Crystal Oasis.");

            var chat = NewChat(model, world);

            await chat.SendAsync("Show me the hero point routes here");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              answer => Assert.Equal("In Pathing, turn on Tekkit's Guides › Path of Fire › Routes - Hero Points Only. It's off right now, and it has 3 routes on Crystal Oasis.", answer.Text));

            Assert.Equal(1, world.MarkerCategoriesCalls);
            AiChatMessage toolAnswer = model.Requests[1][3];
            FunctionResultContent result = Assert.Single(toolAnswer.Contents.OfType<FunctionResultContent>());
            Assert.Contains("Tekkit's Guides", (string)result.Result);
            Assert.Contains("tw_guides.tw_mc_pof.tw_mc_pof_trails_hp", (string)result.Result);
        }

        [Fact]
        public async Task Find_marker_categories_suggests_installing_pathing_when_no_pathing() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerCategoriesResult = MarkerCategoriesResult.NoPathing()
            };

            model.WillCallTool("find_marker_categories", "call_1",
                               new Dictionary<string, object> { ["search"] = "routes" });
            model.WillReply("Pathing is not installed. You can install it from the module catalog.");

            var chat = NewChat(model, world);
            await chat.SendAsync("Show me routes here");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              answer => Assert.Contains("install", answer.Text));
        }

        [Fact]
        public async Task Find_marker_categories_suggests_installing_packs_when_no_packs() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerCategoriesResult = MarkerCategoriesResult.NoPacks()
            };

            model.WillCallTool("find_marker_categories", "call_1",
                               new Dictionary<string, object> { ["search"] = "routes" });
            model.WillReply("No marker packs are installed. You can download Tekkit's Guides.");

            var chat = NewChat(model, world);
            await chat.SendAsync("Show me routes here");

            Assert.Collection(chat.Messages,
                              request => Assert.Equal(ChatMessageAuthor.Player, request.Author),
                              answer => Assert.Contains("Tekkit", answer.Text));
        }

        [Fact]
        public async Task Usage_is_counted_across_a_tool_loop_and_shown_on_the_reply() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            model.WillCallTool("list_installed_modules", "call_1",
                               usage: new UsageDetails { InputTokenCount = 1200, CachedInputTokenCount = 900, OutputTokenCount = 40 });
            model.WillReply("You have no modules installed.",
                            new UsageDetails { InputTokenCount = 1500, CachedInputTokenCount = 1100, OutputTokenCount = 80 });

            var chat = NewChatWithTokens(model, world, TokenBudget.Default, null);

            await chat.SendAsync("What modules do I have?");

            ChatMessage answer = Assert.Single(chat.Messages, message => message.Author == ChatMessageAuthor.Ai);

            Assert.Equal((long?)2700, answer.Usage.InputTokens);
            Assert.Equal((long?)2000, answer.Usage.CachedInputTokens);
            Assert.Equal((long?)120,  answer.Usage.OutputTokens);

            Assert.Equal((long?)2700, chat.TotalUsage.InputTokens);
            Assert.Equal((long?)2000, chat.TotalUsage.CachedInputTokens);
            Assert.Equal((long?)120,  chat.TotalUsage.OutputTokens);
        }

        [Fact]
        public async Task Usage_is_counted_per_Chat_across_Requests() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            model.WillReply("First answer.",  new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 });
            model.WillReply("Second answer.", new UsageDetails { InputTokenCount = 200, OutputTokenCount = 20 });

            var chat = NewChatWithTokens(model, world, TokenBudget.Default, null);

            await chat.SendAsync("First request");
            await chat.SendAsync("Second request");

            Assert.Equal((long?)100, chat.Messages[1].Usage.InputTokens);
            Assert.Equal((long?)200, chat.Messages[3].Usage.InputTokens);

            Assert.Equal((long?)300, chat.TotalUsage.InputTokens);
            Assert.Equal((long?)30,  chat.TotalUsage.OutputTokens);
        }

        [Fact]
        public async Task A_reply_without_reported_usage_carries_no_counts() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            model.WillReply("An answer with no usage behind it.");

            var chat = NewChatWithTokens(model, world, TokenBudget.Default, null);

            await chat.SendAsync("Hello");

            Assert.Null(chat.Messages[1].Usage);
            Assert.True(chat.TotalUsage.IsEmpty);
        }

        [Fact]
        public async Task The_budget_prompt_asks_once_per_Chat() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            model.WillReply("First answer.",  new UsageDetails { InputTokenCount = 60, OutputTokenCount = 10 });
            model.WillReply("Second answer.", new UsageDetails { InputTokenCount = 60, OutputTokenCount = 10 });

            var chat = NewChatWithTokens(model, world, 100, null);

            await chat.SendAsync("First request");
            await chat.SendAsync("Second request");

            List<ChatMessage> prompts = chat.Messages
                                            .Where(message => message.Author == ChatMessageAuthor.Notice
                                                           && message.Text.Contains("Token budget reached"))
                                            .ToList();

            ChatMessage prompt = Assert.Single(prompts);

            Assert.Contains("New chat", prompt.Text);

            // Both answers still landed: the budget asks, it does not take the reply away.
            Assert.Equal(2, chat.Messages.Count(message => message.Author == ChatMessageAuthor.Ai));
        }

        [Fact]
        public async Task After_New_chat_totals_start_over_and_the_budget_asks_again() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            model.WillReply("First answer.",  new UsageDetails { InputTokenCount = 300, OutputTokenCount = 20 });
            model.WillReply("Second answer.", new UsageDetails { InputTokenCount = 300, OutputTokenCount = 20 });

            var chat = NewChatWithTokens(model, world, 100, null);

            await chat.SendAsync("First request");

            chat.StartNewChat();

            Assert.True(chat.TotalUsage.IsEmpty);

            await chat.SendAsync("Second request");

            Assert.Single(chat.Messages, message => message.Author == ChatMessageAuthor.Notice
                                                 && message.Text.Contains("Token budget reached"));
        }

        [Fact]
        public async Task The_reply_carries_its_cost_when_prices_are_entered() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            model.WillReply("An answer.", new UsageDetails { InputTokenCount = 1200, OutputTokenCount = 80 });

            var chat = NewChatWithTokens(model, world, TokenBudget.Default, new TokenPrice(3m, 15m));

            await chat.SendAsync("Hello");

            Assert.Equal(0.0048m, chat.Messages[1].EstimatedCostUsd);
            Assert.Equal(0.0048m, chat.TotalEstimatedCostUsd);
        }

        [Fact]
        public async Task Without_prices_tokens_are_counted_but_no_cost_is_shown() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld();

            model.WillReply("An answer.", new UsageDetails { InputTokenCount = 1200, OutputTokenCount = 80 });

            var chat = NewChatWithTokens(model, world, TokenBudget.Default, null);

            await chat.SendAsync("Hello");

            Assert.Equal((long?)1200, chat.Messages[1].Usage.InputTokens);
            Assert.Null(chat.Messages[1].EstimatedCostUsd);
            Assert.Null(chat.TotalEstimatedCostUsd);
        }

        /// <summary>
        /// The Chat with a Token Budget and prices for the token tests: the scripted model answers,
        /// and the Lookups run against an in-memory world, wired the way the module wires them.
        /// </summary>
        private static Chat NewChatWithTokens(ScriptedChatClient model, FakeWorld world, long budget, TokenPrice prices) {
            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"), Lookups.For(world),
                            logDebug: null, logWarn: null, plans: null, tokenBudget: () => budget, tokenPrices: () => prices);
        }
    }
}
