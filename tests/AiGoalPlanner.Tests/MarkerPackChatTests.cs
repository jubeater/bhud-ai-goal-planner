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
    public class MarkerPackChatTests
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

        private static IDictionary<string, object> DownloadAction(string pack, string reason = "Needed for markers", bool sideEffect = false) {
            return new Dictionary<string, object> {
                ["kind"]           = "download_pack",
                ["pack"]           = pack,
                ["reason"]         = reason,
                ["is_side_effect"] = sideEffect
            };
        }

        [Fact]
        public async Task Unofficial_pack_is_rejected_in_chat() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };

            model.WillCallTool("submit_plan", "call_1",
                               Plan("I propose downloading an unofficial pack.",
                                    DownloadAction("Shady Unofficial Pack")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Get me that pack");

            Assert.NotNull(chat.Review);
            ReviewedAction action = Assert.Single(chat.Review.Actions);
            Assert.False(action.IsAccepted);
            Assert.False(action.CanApply);
            Assert.Contains("not on Pathing's official marker pack list", action.Problem);
            Assert.False(chat.Review.HasSomethingToApply);
        }

        [Fact]
        public async Task Auto_apply_never_downloads_marker_pack() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };

            model.WillCallTool("submit_plan", "call_1",
                               Plan("I propose downloading Tekkit's pack.",
                                    DownloadAction("Tekkit's All-In-One", sideEffect: false)));

            // Auto-apply is ON
            var chat = NewChat(model, world, autoApply: true);
            await chat.SendAsync("Set up hero points");

            // Pack was NOT downloaded by Auto-apply
            Assert.Empty(world.DownloadedPacks);

            // Review is open and waiting for Player approval
            Assert.NotNull(chat.Review);
            ReviewedAction action = Assert.Single(chat.Review.Actions);
            Assert.True(action.IsAccepted);
            Assert.True(action.CanApply);
            Assert.Equal("Pathing: download Tekkit's All-In-One (47.1 MB, Pathing will restart)", action.Summary);
        }

        [Fact]
        public async Task Download_pack_leads_to_continuation_advising_category_to_turn_on() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };

            // Request 1: Model calls list_marker_packs, then proposes downloading Tekkit's pack
            model.WillCallTool("list_marker_packs", "call_lookup", new Dictionary<string, object>());
            model.WillCallTool("submit_plan", "call_plan",
                               Plan("Tekkit's marker pack has routes for this map. I suggest downloading it.",
                                    DownloadAction("Tekkit's All-In-One")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Show me the hero point routes here");

            Assert.NotNull(chat.Review);
            Assert.Empty(world.DownloadedPacks);

            // Configure continuation turn: after download, model calls find_marker_categories and advises Player
            world.MarkerCategoriesResult = new MarkerCategoriesResult(
                MarkerCategoriesStatus.Ok,
                mapId: 1205,
                mapName: "Domain of Vabbi",
                categories: new[] {
                    new MarkerCategoryMatch(
                        "Tekkit's Guides",
                        "Tekkit's Guides › Path of Fire › Routes - Hero Points Only",
                        "tw_guides.tw_mc_pof.tw_mc_pof_trails_hp",
                        shown: false,
                        markerCount: 2,
                        trailCount: 1)
                });

            model.WillCallTool("find_marker_categories", "call_cat",
                               new Dictionary<string, object> { ["search"] = "hero point routes" });
            model.WillReply("In Pathing, turn on Tekkit's Guides › Path of Fire › Routes - Hero Points Only. It's off right now, and it has 3 routes on Domain of Vabbi.");

            // Player applies the download
            await chat.ApplyAsync(new[] { 0 });

            // Pack was downloaded
            Assert.Single(world.DownloadedPacks);
            Assert.Equal("Tekkit's All-In-One", world.DownloadedPacks[0]);

            // Continuation ran and provided advisory category guidance
            Assert.Equal(1, world.MarkerCategoriesCalls);

            // Review the messages in Chat: Player -> AI proposal -> Report -> AI continuation
            Assert.Collection(chat.Messages,
                m1 => Assert.Equal("Show me the hero point routes here", m1.Text),
                m2 => Assert.Equal("Tekkit's marker pack has routes for this map. I suggest downloading it.", m2.Text),
                m3 => Assert.Contains("Applied 1 change", m3.Text),
                m4 => Assert.Equal("In Pathing, turn on Tekkit's Guides › Path of Fire › Routes - Hero Points Only. It's off right now, and it has 3 routes on Domain of Vabbi.", m4.Text));

            // Undo
            await chat.UndoAsync();
            Assert.Empty(world.DownloadedPacks);
            Assert.Contains(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("Undid 1 change"));
        }

        [Fact]
        public async Task Apply_downloads_with_progress_and_cancel_cleans_up() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };

            model.WillCallTool("submit_plan", "call_plan",
                               Plan("Download Tekkit's pack.",
                                    DownloadAction("Tekkit's All-In-One")));

            var chat = NewChat(model, world);
            await chat.SendAsync("Get Tekkit's pack");

            Assert.NotNull(chat.Review);

            var downloadStarted = new TaskCompletionSource<bool>();
            world.AnswerDownloadMarkerPack = async (pack, ct) => {
                downloadStarted.TrySetResult(true);
                var tcs = new TaskCompletionSource<MarkerPackChange>();
                using (ct.Register(() => tcs.TrySetCanceled(ct))) {
                    return await tcs.Task;
                }
            };

            using (var cts = new CancellationTokenSource()) {
                Task apply = chat.ApplyAsync(new[] { 0 }, cts.Token);
                await downloadStarted.Task;

                Assert.Contains(chat.ProgressLines, line => line.Contains("Tekkit"));

                cts.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => apply);

                Assert.Empty(chat.ProgressLines);
                Assert.Empty(world.DownloadedPacks);
                Assert.Contains(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("Nothing was applied"));
            }
        }

        [Fact]
        public async Task Undo_deletes_marker_pack_and_restarts_pathing() {
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

            var chat = NewChat(model, world);
            await chat.SendAsync("Get Tekkit's pack");

            world.MarkerCategoriesResult = new MarkerCategoriesResult(
                MarkerCategoriesStatus.Ok, mapId: 1, mapName: "Lion's Arch", categories: Array.Empty<MarkerCategoryMatch>());
            model.WillCallTool("find_marker_categories", "call_cat", new Dictionary<string, object>());
            model.WillReply("Tekkit's pack is downloaded.");

            world.Writes.Clear();
            await chat.ApplyAsync(new[] { 0 });

            Assert.Single(world.DownloadedPacks);
            Assert.Contains("download Tekkit's All-In-One", world.Writes);
            Assert.Contains("disable bh.community.pathing", world.Writes);
            Assert.Contains("enable bh.community.pathing", world.Writes);

            world.Writes.Clear();
            await chat.UndoAsync();

            Assert.Empty(world.DownloadedPacks);
            Assert.Contains("delete tw_ALL_IN_ONE.taco", world.Writes);
            Assert.Contains("disable bh.community.pathing", world.Writes);
            Assert.Contains("enable bh.community.pathing", world.Writes);
            Assert.Contains(chat.Messages, m => m.Author == ChatMessageAuthor.Report && m.Text.Contains("Undid 1 change"));
        }
    }
}
