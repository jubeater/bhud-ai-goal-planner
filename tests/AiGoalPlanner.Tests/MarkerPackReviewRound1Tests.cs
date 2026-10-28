using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = jubeater.aigoalplanner.Core.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    public class MarkerPackReviewRound1Tests
    {
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world, bool autoApply = false) {
            var plans = new Plans(world, () => autoApply);
            var tools = Lookups.For(world);
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

        private static IDictionary<string, object> DownloadAction(string pack, string reason = "Needed for markers", bool sideEffect = false) {
            return new Dictionary<string, object> {
                ["kind"]           = "download_pack",
                ["pack"]           = pack,
                ["reason"]         = reason,
                ["is_side_effect"] = sideEffect
            };
        }

        [Fact]
        public async Task Cancelled_partial_apply_is_remembered_for_the_next_Request() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false),
                    new MarkerPackInfo("Fast TacO Pack", "fast_TacO_pack.taco", "General", "Farming", 10.0, "url", "1", false)
                }
            };

            model.WillCallTool("submit_plan", "call_plan",
                               Plan("Download both packs.",
                                    DownloadAction("Tekkit's All-In-One"),
                                    DownloadAction("Fast TacO Pack")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Get both packs");

            Assert.NotNull(chat.Review);

            var secondStarted = new TaskCompletionSource<bool>();
            world.AnswerDownloadMarkerPack = async (pack, ct) => {
                if (pack == "Tekkit's All-In-One") {
                    world.DownloadedPacks.Add(pack);
                    return new MarkerPackChange(true, true, pack, "tw_ALL_IN_ONE.taco", 47.1);
                }
                secondStarted.TrySetResult(true);
                var tcs = new TaskCompletionSource<MarkerPackChange>();
                using (ct.Register(() => tcs.TrySetCanceled(ct))) {
                    return await tcs.Task;
                }
            };

            using (var cts = new CancellationTokenSource()) {
                Task apply = chat.ApplyAsync(new[] { 0, 1 }, cts.Token);
                await secondStarted.Task;

                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => apply);
            }

            // Exactly 1 pack was downloaded
            Assert.Single(world.DownloadedPacks);
            Assert.Equal("Tekkit's All-In-One", world.DownloadedPacks[0]);

            // Player sends next request: model history MUST include the 1 applied change
            model.WillReply("You have Tekkit's pack installed.");
            await chat.SendAsync("What next?");

            // Verify that the model's new request includes the applied outcome in history
            var lastRequest = model.Requests.Last();
            Assert.Contains(lastRequest, m => m.Text != null && m.Text.Contains("Applied 1 change"));
        }

        [Fact]
        public async Task Marker_pack_download_and_undo_restarts_pathing_on_game_thread() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            model.WillCallTool("submit_plan", "call_plan",
                               Plan("Download Tekkit's pack.",
                                    DownloadAction("Tekkit's All-In-One")));

            var gameThread = new FakeGameThread();
            IBlishHudWorld target = new GameThreadWorld(world, gameThread);
            var plans = new Plans(target, () => false);
            var tools = Lookups.For(target);
            plans.AddTo(tools);

            var chat = new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                                tools, null, plans);

            await PumpUntil(chat.SendAsync("Get Tekkit's pack"), gameThread);

            world.MarkerCategoriesResult = new MarkerCategoriesResult(
                MarkerCategoriesStatus.Ok, mapId: 1, mapName: "Lion's Arch", categories: Array.Empty<MarkerCategoryMatch>());
            model.WillCallTool("find_marker_categories", "call_cat", new Dictionary<string, object>());
            model.WillReply("Tekkit's pack is downloaded.");

            world.Writes.Clear();
            Task applying = chat.ApplyAsync(new[] { 0 });
            await PumpUntil(applying, gameThread);

            Assert.Single(world.DownloadedPacks);
            Assert.Contains("download Tekkit's All-In-One", world.Writes);
            Assert.Contains("disable bh.community.pathing", world.Writes);
            Assert.Contains("enable bh.community.pathing", world.Writes);

            world.Writes.Clear();
            Task undoing = chat.UndoAsync();
            await PumpUntil(undoing, gameThread);

            Assert.Empty(world.DownloadedPacks);
            Assert.Contains("delete tw_ALL_IN_ONE.taco", world.Writes);
            Assert.Contains("disable bh.community.pathing", world.Writes);
            Assert.Contains("enable bh.community.pathing", world.Writes);
        }

        private static async Task PumpUntil(Task task, FakeGameThread gameThread) {
            for (int attempt = 0; attempt < 500 && !task.IsCompleted; attempt++) {
                gameThread.Pump();
                await Task.Delay(5);
            }

            await task;
        }
    }
}
