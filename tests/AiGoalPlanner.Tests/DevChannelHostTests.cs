using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using jubeater.aigoalplanner;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;

namespace jubeater.aigoalplanner.Tests
{
#if DEBUG
    /// <summary>
    /// Tests the developer channel's host: the watcher runs only while the setting is on and Blish
    /// HUD was started with <c>--debug</c>, and stops watching when either gate closes.
    /// </summary>
    public class DevChannelHostTests
    {
        [Fact]
        public async Task Without_the_debug_flag_the_watcher_reads_nothing() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"));

            var inbox = new FakeDevInbox();
            inbox.AddFile("gate.json", "{ \"id\": \"gate\", \"op\": \"state\" }");

            var host = new DevChannelHost();
            host.Start(chat, settingOn: () => true, blishDebug: () => false, logDebug: null, inbox: inbox);

            try {
                await Task.Delay(600);

                Assert.Equal(new[] { "gate.json" }, inbox.Waiting);
                Assert.Empty(inbox.Responses);
                Assert.Empty(model.Requests);
            } finally {
                host.Stop();
            }
        }

        [Fact]
        public async Task With_both_gates_the_watcher_handles_files() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"));

            var inbox = new FakeDevInbox();
            inbox.AddFile("gate.json", "{ \"id\": \"gate\", \"op\": \"state\" }");

            var host = new DevChannelHost();
            host.Start(chat, settingOn: () => true, blishDebug: () => true, logDebug: null, inbox: inbox);

            try {
                for (int attempt = 0; attempt < 100 && inbox.Responses.Count == 0; attempt++) {
                    await Task.Delay(50);
                }

                Assert.True((bool)Newtonsoft.Json.Linq.JObject.Parse(inbox.Responses["gate"])["ok"]);
                Assert.Empty(inbox.Waiting);
            } finally {
                host.Stop();
            }
        }

        [Fact]
        public async Task Turning_the_setting_off_stops_the_watcher() {
            var model = new ScriptedChatClient();
            model.WillReply("An answer.");

            var chat = new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"));

            var inbox = new FakeDevInbox();
            inbox.AddFile("first.json", "{ \"id\": \"first\", \"op\": \"state\" }");

            bool settingOn = true;

            var host = new DevChannelHost();
            host.Start(chat, settingOn: () => settingOn, blishDebug: () => true, logDebug: null, inbox: inbox);

            try {
                for (int attempt = 0; attempt < 100 && inbox.Responses.Count == 0; attempt++) {
                    await Task.Delay(50);
                }

                Assert.True(inbox.Responses.ContainsKey("first"));

                settingOn = false;
                inbox.AddFile("second.json", "{ \"id\": \"second\", \"op\": \"state\" }");

                await Task.Delay(600);

                Assert.Equal(new[] { "second.json" }, inbox.Waiting);
                Assert.False(inbox.Responses.ContainsKey("second"));
            } finally {
                host.Stop();
            }
        }
    }
#endif
}
