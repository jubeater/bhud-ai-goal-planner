using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// The Chat the way the module builds it: a scripted model, and the identity message that
        /// says which product the model is part of.
        /// </summary>
        private static Chat NewChat(ScriptedChatClient model) {
            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"));
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
    }
}
