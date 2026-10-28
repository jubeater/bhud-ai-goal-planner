using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;

namespace jubeater.aigoalplanner.Tests
{
#if DEBUG
    /// <summary>
    /// Tests the developer channel against the real Chat: request files in a fake inbox run the same
    /// Chat methods the window's buttons call, and the response files report what happened.
    /// </summary>
    public class DevChannelTests
    {
        [Fact]
        public async Task Send_runs_the_same_request_the_button_sends() {
            var model = new ScriptedChatClient();
            model.WillReply("Pathing draws the trails.");

            var world = PlayerWorld();
            var chat  = NewChannelChat(model, world);

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"What do the trails need?\" }");

            string handled = await PollAsync(chat, inbox);

            Assert.Equal("a1", handled);
            Assert.Equal(new[] { "a1.json" }, inbox.Removed);

            JObject json = Response(inbox, "a1");

            Assert.True((bool)json["ok"]);
            Assert.Equal(new[] { "player", "ai" },
                         json["messages"].Select(message => (string)message["author"]));
            Assert.Equal("What do the trails need?", (string)json["messages"][0]["text"]);
            Assert.Equal("Pathing draws the trails.", (string)json["messages"][1]["text"]);
        }

        [Fact]
        public async Task State_changes_nothing_but_reports_everything() {
            var model = new ScriptedChatClient();
            model.WillCallTool("list_installed_modules", "call_1");
            model.WillReply("You have Pathing installed.");

            var world = PlayerWorld();
            var chat  = NewChannelChat(model, world);

            await chat.SendAsync("What modules do I have?");
            int messages = chat.Messages.Count;

            var inbox = new FakeDevInbox();
            inbox.AddFile("a6.json", "{ \"id\": \"a6\", \"op\": \"state\" }");

            await PollAsync(chat, inbox);

            Assert.Equal(messages, chat.Messages.Count);

            JObject json = Response(inbox, "a6");

            Assert.True((bool)json["ok"]);
            Assert.Equal(messages, json["messages"].Count());
            Assert.Equal("list_installed_modules", (string)json["lookups"][0]["name"]);
            Assert.Equal(2, json["roundTrips"].Count());
            Assert.False((bool)json["undoAvailable"]);
        }

        [Fact]
        public async Task Lookup_and_round_trip_details_match_the_debug_log() {
            var model = new ScriptedChatClient();
            model.WillCallTool("get_module_settings", "call_1",
                                new Dictionary<string, object> { ["namespace"] = "bh.mounts" });
            model.WillReply("Mounts shows markers.");

            var world = PlayerWorld();
            var log   = new List<string>();
            var chat  = NewChannelChat(model, world, log);

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"What is in the Mounts settings?\" }");

            await PollAsync(chat, inbox, log);

            JObject json = Response(inbox, "a1");
            string everything = string.Join("\n", log);

            Assert.Equal(2, json["roundTrips"].Count());
            Assert.Equal(2, Occurrences(everything, "Provider round trip"));

            foreach (JToken roundTrip in json["roundTrips"]) {
                Assert.Contains("finish_reason=" + (string)roundTrip["finishReason"], everything);
                Assert.Contains("tool_calls=" + (string)roundTrip["toolCalls"], everything);
            }

            string name   = (string)json["lookups"][0]["name"];
            string detail = (string)json["lookups"][0]["detail"];

            Assert.Equal("get_module_settings", name);
            Assert.Contains("Lookup " + name + ":", everything);
            Assert.Contains(detail, everything);
            Assert.True((int)json["lookups"][0]["chars"] > 0);
        }

        [Fact]
        public async Task Apply_applies_the_named_actions_like_the_button() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                                Plan("I suggest hiding the mount markers.",
                                     Action("set_setting", "bh.mounts", key: "Show markers", value: "false",
                                            reason: "You asked for the markers to go."),
                                     Action("disable_module", "bh.pathing",
                                            reason: "It overlaps with Mounts.", sideEffect: true)));
            model.WillReply("Anything else?");

            var world = PlayerWorld();
            var chat  = NewChannelChat(model, world);

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"turn off the markers in Mounts\" }");
            inbox.AddFile("a2.json", "{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": [0] }");

            await PollAsync(chat, inbox);
            await PollAsync(chat, inbox);

            Assert.Equal(new[] { "a1.json", "a2.json" }, inbox.Removed);
            Assert.Equal("false", await SettingValueAsync(world, "bh.mounts", "Show markers"));

            JObject json = Response(inbox, "a2");

            Assert.True((bool)json["ok"]);
            Assert.Contains("Show markers", (string)json["applied"]["describe"]);
            Assert.True((bool)json["undoAvailable"]);
        }

        [Fact]
        public async Task Cancel_review_dismisses_the_open_review() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                                Plan("I suggest hiding the mount markers.",
                                     Action("set_setting", "bh.mounts", key: "Show markers", value: "false",
                                            reason: "You asked for the markers to go.")));

            var chat = NewChannelChat(model, PlayerWorld());

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"turn off the markers in Mounts\" }");
            inbox.AddFile("a3.json", "{ \"id\": \"a3\", \"op\": \"cancel_review\" }");

            await PollAsync(chat, inbox);

            Assert.NotNull(chat.Review);

            await PollAsync(chat, inbox);

            Assert.Null(chat.Review);
            Assert.True((bool)Response(inbox, "a3")["ok"]);
        }

        [Fact]
        public async Task Undo_puts_the_apply_back() {
            var model = new ScriptedChatClient();
            model.WillCallTool(SubmitPlanTool.Name, "call_1",
                                Plan("I suggest hiding the mount markers.",
                                     Action("set_setting", "bh.mounts", key: "Show markers", value: "false",
                                            reason: "You asked for the markers to go.")));
            model.WillReply("Anything else?");

            var world = PlayerWorld();
            var chat  = NewChannelChat(model, world);

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"turn off the markers in Mounts\" }");
            inbox.AddFile("a2.json", "{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": [0] }");
            inbox.AddFile("a4.json", "{ \"id\": \"a4\", \"op\": \"undo\" }");

            await PollAsync(chat, inbox);
            await PollAsync(chat, inbox);

            Assert.Equal("false", await SettingValueAsync(world, "bh.mounts", "Show markers"));

            await PollAsync(chat, inbox);

            Assert.Equal("true", await SettingValueAsync(world, "bh.mounts", "Show markers"));
            Assert.True((bool)Response(inbox, "a4")["ok"]);
        }

        [Fact]
        public async Task New_chat_starts_over() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = NewChannelChat(model, PlayerWorld());

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"Hello\" }");
            inbox.AddFile("a5.json", "{ \"id\": \"a5\", \"op\": \"new_chat\" }");

            await PollAsync(chat, inbox);
            Assert.NotEmpty(chat.Messages);

            await PollAsync(chat, inbox);

            Assert.Empty(chat.Messages);
            Assert.True((bool)Response(inbox, "a5")["ok"]);
            Assert.Empty(Response(inbox, "a5")["messages"]);
        }

        [Fact]
        public async Task With_a_gate_missing_nothing_is_read_from_the_inbox() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = NewChannelChat(model, PlayerWorld());

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"Hello\" }");

            var channel = new DevChannel(chat, isOpen: () => false, logDebug: null, inbox: inbox);

            Assert.Null(await channel.PollAsync());

            Assert.Equal(new[] { "a1.json" }, inbox.Waiting);
            Assert.Empty(inbox.Responses);
            Assert.Empty(model.Requests);
        }

        [Fact]
        public async Task A_direct_request_with_a_gate_missing_answers_channel_off() {
            var model = new ScriptedChatClient();

            var chat = NewChannelChat(model, PlayerWorld());

            var channel = new DevChannel(chat, isOpen: () => false, logDebug: null,
                                         inbox: new FakeDevInbox());

            DevResponse response = await channel.HandleAsync(new DevRequest("a1", DevRequestOp.State));

            Assert.False(response.Ok);
            Assert.Equal(DevErrors.ChannelOff, response.Error);
            Assert.Empty(model.Requests);
        }

        [Fact]
        public async Task A_too_big_file_is_rejected_without_running() {
            var model = new ScriptedChatClient();

            var chat = NewChannelChat(model, PlayerWorld());

            string padding = new string(' ', 17 * 1024);

            var inbox = new FakeDevInbox();
            inbox.AddFile("big.json", "{ \"id\": \"big\", \"op\": \"state\" }" + padding);

            await PollAsync(chat, inbox);

            Assert.Equal(DevErrors.BadRequest, (string)Response(inbox, "big")["error"]);
            Assert.Equal(new[] { "big.json" }, inbox.Removed);
            Assert.Empty(model.Requests);
        }

        [Fact]
        public async Task A_malformed_file_is_rejected_without_running() {
            var model = new ScriptedChatClient();

            var chat = NewChannelChat(model, PlayerWorld());

            var inbox = new FakeDevInbox();
            inbox.AddFile("a9.json", "{ this is not a request file");

            await PollAsync(chat, inbox);

            Assert.Equal(DevErrors.BadRequest, (string)Response(inbox, "a9")["error"]);
            Assert.Equal(new[] { "a9.json" }, inbox.Removed);
            Assert.Empty(model.Requests);
        }

        [Fact]
        public async Task A_file_whose_name_is_not_an_id_is_left_alone() {
            var model = new ScriptedChatClient();

            var chat = NewChannelChat(model, PlayerWorld());

            var inbox = new FakeDevInbox();
            inbox.AddFile("not a request.json", "{ \"id\": \"a1\", \"op\": \"state\" }");

            Assert.Null(await PollAsync(chat, inbox));

            Assert.Equal(new[] { "not a request.json" }, inbox.Waiting);
            Assert.Empty(inbox.Responses);
            Assert.Empty(model.Requests);
        }

        [Fact]
        public async Task The_response_file_is_named_after_the_request_id() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = NewChannelChat(model, PlayerWorld());

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"b2\", \"op\": \"send\", \"text\": \"Hello\" }");

            Assert.Equal("b2", await PollAsync(chat, inbox));
            Assert.True((bool)Response(inbox, "b2")["ok"]);
            Assert.Equal(new[] { "a1.json" }, inbox.Removed);
        }

        [Fact]
        public async Task Files_are_handled_in_order() {
            var model = new ScriptedChatClient();
            model.WillReply("First answer.");
            model.WillReply("Second answer.");

            var chat = NewChannelChat(model, PlayerWorld());

            var inbox = new FakeDevInbox();
            inbox.AddFile("b1.json", "{ \"id\": \"b1\", \"op\": \"send\", \"text\": \"Second file\" }");
            inbox.AddFile("a2.json", "{ \"id\": \"a2\", \"op\": \"send\", \"text\": \"First file\" }");

            Assert.Equal("a2", await PollAsync(chat, inbox));
            Assert.Equal("b1", await PollAsync(chat, inbox));

            Assert.Equal("First file", (string)Response(inbox, "a2")["messages"][0]["text"]);

            JArray both = (JArray)Response(inbox, "b1")["messages"];

            Assert.Equal(4, both.Count);
            Assert.Equal("Second file", (string)both[2]["text"]);
        }

        [Fact]
        public async Task Requests_run_one_at_a_time() {
            var model = new ScriptedChatClient();
            model.WillHold();
            model.WillReply("An answer.");

            var chat = NewChannelChat(model, PlayerWorld());
            var log  = new List<string>();
            var busy = new DevChannel(chat, isOpen: () => true, logDebug: log.Add, inbox: new FakeDevInbox(),
                                      busyTimeout: TimeSpan.FromMilliseconds(50));

            Task<DevResponse> first = busy.HandleAsync(new DevRequest("b1", DevRequestOp.Send, "Hello"));

            await WaitUntil(() => model.Requests.Count == 1);

            DevResponse second = await busy.HandleAsync(new DevRequest("b2", DevRequestOp.State));

            Assert.False(second.Ok);
            Assert.Equal(DevErrors.Busy, second.Error);

            model.Release();

            Assert.True((await first).Ok);
            Assert.Contains(log, line => line.Contains("b2") && line.Contains(DevErrors.Busy));
        }

        [Fact]
        public async Task A_request_that_outlasts_its_budget_times_out() {
            var model = new ScriptedChatClient();
            model.WillCallTool("get_module_settings", "call_1",
                                new Dictionary<string, object> { ["namespace"] = "bh.mounts" });
            model.WillReply("Never reached.");

            var world = PlayerWorld();
            var held  = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            world.AnswerGetModuleSettings = (@namespace, cancellationToken) =>
                HoldUntil(held.Task, world, @namespace, cancellationToken);

            var chat = NewChannelChat(model, world);
            var slow = new DevChannel(chat, isOpen: () => true, logDebug: null, inbox: new FakeDevInbox(),
                                      operationTimeout: TimeSpan.FromMilliseconds(200));

            DevResponse response = await slow.HandleAsync(new DevRequest("b9", DevRequestOp.Send, "Settings please"));

            Assert.False(response.Ok);
            Assert.Equal(DevErrors.Timeout, response.Error);

            held.TrySetResult(true);
        }

        [Fact]
        public async Task The_log_gets_one_line_per_request() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = NewChannelChat(model, PlayerWorld());
            var log  = new List<string>();

            var inbox = new FakeDevInbox();
            inbox.AddFile("a1.json", "{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"Hello\" }");

            await PollAsync(chat, inbox, log);

            string line = Assert.Single(log, text => text.StartsWith("dev request "));

            Assert.Matches(@"^dev request a1: send, ok, \d+ ms$", line);
        }

        private static async Task<string> PollAsync(Chat chat, FakeDevInbox inbox, List<string> log = null) {
            var channel = new DevChannel(chat, isOpen: () => true,
                                         logDebug: log == null ? (Action<string>)null : log.Add,
                                         inbox: inbox);

            return await channel.PollAsync();
        }

        private static JObject Response(FakeDevInbox inbox, string id) {
            return JObject.Parse(inbox.Responses[id]);
        }

        /// <summary>
        /// The Chat the way the module builds it: a scripted model, this version's Lookups and the
        /// <c>submit_plan</c> proposal, and the Plan pipeline against the same world.
        /// </summary>
        private static Chat NewChannelChat(ScriptedChatClient model, FakeWorld world, List<string> log = null) {
            var plans = new Plans(world, () => false);
            ToolRegistry tools = log == null ? Lookups.For(world) : Lookups.For(world, log.Add);

            plans.AddTo(tools);

            Action<string> logDebug = log == null ? (Action<string>)null : log.Add;

            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            tools, logDebug, plans);
        }

        private static FakeWorld PlayerWorld() {
            var world = new FakeWorld();

            world.Modules.Add(new InstalledModuleInfo("bh.mounts", "Mounts", "1.0.0", "Mount settings.", enabled: true));
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "Markers and trails.", enabled: false));

            world.SettingsByNamespace["bh.mounts"] = FakeWorld.Ok(
                "Mounts",
                new ModuleSettingInfo("Show markers", "Show markers", "Draws the mount markers.", "Boolean", "true", null, null));

            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Disabled("Pathing");

            return world;
        }

        private static IDictionary<string, object> Plan(string reply, params IDictionary<string, object>[] actions) {
            return new Dictionary<string, object> {
                ["reply"]   = reply,
                ["actions"] = actions.Cast<object>().ToList()
            };
        }

        private static IDictionary<string, object> Action(string kind, string module, string key = null,
                                                          string value = null, string reason = "",
                                                          bool sideEffect = false) {
            var action = new Dictionary<string, object> {
                ["kind"]           = kind,
                ["reason"]         = reason,
                ["is_side_effect"] = sideEffect
            };

            if (module != null) action["module"] = module;
            if (key != null)    action["key"]    = key;
            if (value != null)  action["value"]  = value;

            return action;
        }

        private static async Task<string> SettingValueAsync(FakeWorld world, string @namespace, string key) {
            ModuleSettingsResult result = await world.GetModuleSettingsAsync(@namespace, default(CancellationToken));

            return SettingWalk.Flatten(result.Settings).Single(setting => setting.Key == key).Value;
        }

        /// <summary>
        /// A settings Lookup that waits for <paramref name="release"/> and is cut short by
        /// cancellation, so a test holds the Chat in the middle of a Lookup.
        /// </summary>
        private static async Task<ModuleSettingsResult> HoldUntil(Task<bool> release, FakeWorld world, string @namespace,
                                                                  CancellationToken cancellationToken) {
            await Task.WhenAny(release, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            return world.SettingsByNamespace[@namespace];
        }

        private static async Task WaitUntil(Func<bool> condition) {
            for (int attempt = 0; attempt < 500 && !condition(); attempt++) {
                await Task.Delay(10);
            }

            Assert.True(condition());
        }

        private static int Occurrences(string text, string needle) {
            int count = 0;

            for (int at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
                 at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) {
                count++;
            }

            return count;
        }
    }
#endif
}
