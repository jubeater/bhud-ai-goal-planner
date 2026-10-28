using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Providers;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = jubeater.aigoalplanner.Core.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    public class MarkerPackReviewRound6Tests
    {
        private static Chat NewChat(ScriptedChatClient model, FakeWorld world) {
            IBlishHudWorld target = new GameThreadWorld(world, new ImmediateGameThread());
            var plans = new Plans(target);
            var tools = Lookups.For(target);
            plans.AddTo(tools);

            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            tools, null, plans);
        }

        [Fact]
        public async Task Failed_download_gets_generic_continuation_without_mentioning_downloaded_pack() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Official", "official.taco", "Official pack", "Cat", 1.5, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            world.AnswerDownloadMarkerPack = (pack, ct) =>
                Task.FromResult(new MarkerPackChange(false, false, pack, null, 0, problem: "Download failed."));

            // Turn 1: Propose download
            model.WillCallTool("submit_plan", "call_1",
                               new Dictionary<string, object> {
                                   ["reply"] = "I will download Official.",
                                   ["actions"] = new object[] {
                                       new Dictionary<string, object> {
                                           ["kind"] = "download_pack",
                                           ["pack"] = "Official",
                                           ["reason"] = "Needed for markers",
                                           ["is_side_effect"] = false
                                       }
                                   }
                               });

            // Turn 2 (Continuation): Model replies acknowledging failure
            model.WillReply("The download failed, let me know if you would like to retry.");

            var chat = NewChat(model, world);
            await chat.SendAsync("Download Official");

            Assert.NotNull(chat.Review);
            await chat.ApplyAsync(new[] { 0 });

            // Check report message in chat
            var reportMsg = chat.Messages.FirstOrDefault(m => m.Author == ChatMessageAuthor.Report);
            Assert.NotNull(reportMsg);
            Assert.Contains("Nothing was applied", reportMsg.Text);
            Assert.Contains("Download failed.", reportMsg.Text);

            // Check continuation prompt sent to model
            var continuationRequest = model.Requests.FirstOrDefault(req =>
                req.Any(m => m.Role == ChatRole.User && m.Text != null && m.Text.Contains("applied the plan you proposed")));

            Assert.NotNull(continuationRequest);
            var userMsg = continuationRequest.First(m => m.Role == ChatRole.User && m.Text.Contains("applied the plan you proposed"));
            Assert.DoesNotContain("the downloaded pack", userMsg.Text);
            Assert.DoesNotContain("for the downloaded pack", userMsg.Text);
            Assert.DoesNotContain("find_marker_categories", userMsg.Text);
            Assert.Contains("Look up anything you now need to, and tell the Player what you suggest next.", userMsg.Text);
        }

        [Fact]
        public async Task Committed_download_includes_downloaded_pack_instruction_in_continuation() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Official", "official.taco", "Official pack", "Cat", 1.5, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            world.AnswerDownloadMarkerPack = (pack, ct) =>
                Task.FromResult(new MarkerPackChange(true, true, pack, "official.taco", 1.5));

            // Turn 1: Propose download
            model.WillCallTool("submit_plan", "call_1",
                               new Dictionary<string, object> {
                                   ["reply"] = "I will download Official.",
                                   ["actions"] = new object[] {
                                       new Dictionary<string, object> {
                                           ["kind"] = "download_pack",
                                           ["pack"] = "Official",
                                           ["reason"] = "Needed for markers",
                                           ["is_side_effect"] = false
                                       }
                                   }
                               });

            // Turn 2 (Continuation): Model replies
            model.WillReply("Looking up categories for the downloaded pack.");

            var chat = NewChat(model, world);
            await chat.SendAsync("Download Official");

            Assert.NotNull(chat.Review);
            await chat.ApplyAsync(new[] { 0 });

            // Check report message in chat
            var reportMsg = chat.Messages.FirstOrDefault(m => m.Author == ChatMessageAuthor.Report);
            Assert.NotNull(reportMsg);
            Assert.Contains("Applied 1 change", reportMsg.Text);

            // Check continuation prompt sent to model
            var continuationRequest = model.Requests.FirstOrDefault(req =>
                req.Any(m => m.Role == ChatRole.User && m.Text != null && m.Text.Contains("applied the plan you proposed")));

            Assert.NotNull(continuationRequest);
            var userMsg = continuationRequest.First(m => m.Role == ChatRole.User && m.Text.Contains("applied the plan you proposed"));
            Assert.Contains("for the downloaded pack", userMsg.Text);
            Assert.Contains("find_marker_categories", userMsg.Text);
        }

        [Fact]
        public async Task Committed_download_with_restart_failure_includes_downloaded_pack_instruction_in_continuation() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Official", "official.taco", "Official pack", "Cat", 1.5, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            world.AnswerDownloadMarkerPack = (pack, ct) =>
                Task.FromResult(new MarkerPackChange(true, true, pack, "official.taco", 1.5, problem: "Pathing restart failed."));

            // Turn 1: Propose download
            model.WillCallTool("submit_plan", "call_1",
                               new Dictionary<string, object> {
                                   ["reply"] = "I will download Official.",
                                   ["actions"] = new object[] {
                                       new Dictionary<string, object> {
                                           ["kind"] = "download_pack",
                                           ["pack"] = "Official",
                                           ["reason"] = "Needed for markers",
                                           ["is_side_effect"] = false
                                       }
                                   }
                               });

            // Turn 2 (Continuation): Model replies
            model.WillReply("Restart had a problem, but pack is installed.");

            var chat = NewChat(model, world);
            await chat.SendAsync("Download Official");

            Assert.NotNull(chat.Review);
            await chat.ApplyAsync(new[] { 0 });

            // Check report message in chat
            var reportMsg = chat.Messages.FirstOrDefault(m => m.Author == ChatMessageAuthor.Report);
            Assert.NotNull(reportMsg);
            Assert.Contains("Applied 1 change", reportMsg.Text);
            Assert.Contains("Pathing restart failed.", reportMsg.Text);

            // Check continuation prompt sent to model
            var continuationRequest = model.Requests.FirstOrDefault(req =>
                req.Any(m => m.Role == ChatRole.User && m.Text != null && m.Text.Contains("applied the plan you proposed")));

            Assert.NotNull(continuationRequest);
            var userMsg = continuationRequest.First(m => m.Role == ChatRole.User && m.Text.Contains("applied the plan you proposed"));
            Assert.Contains("for the downloaded pack", userMsg.Text);
            Assert.Contains("find_marker_categories", userMsg.Text);
        }
    }
}