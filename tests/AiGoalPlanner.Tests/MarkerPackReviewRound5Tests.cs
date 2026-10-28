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
    public class MarkerPackReviewRound5Tests
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
        public void FormatPackSize_formats_below_1mb_as_kb_and_above_as_mb() {
            Assert.Equal("33 KB", PlanValidator.FormatPackSize(0.033));
            Assert.Equal("900 KB", PlanValidator.FormatPackSize(0.9));
            Assert.Equal("46.8 MB", PlanValidator.FormatPackSize(46.8));

            Assert.Equal("33 KB", PlanValidator.FormatPackSize(0.033086777));
            Assert.Equal("1 KB", PlanValidator.FormatPackSize(0.0001));
            Assert.Equal(string.Empty, PlanValidator.FormatPackSize(0));
            Assert.Equal(string.Empty, PlanValidator.FormatPackSize(-1));
        }

        [Fact]
        public async Task Review_download_pack_shows_kb_for_sub_megabyte_packs() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Small Pack", "small.taco", "Desc", "Cat", 0.033086777, "url", "1", false)
                }
            };
            var plan = new Plan("Download", new[] {
                new PlanAction(PlanActionKind.DownloadPack, "download_pack", null, null, null,
                               "Needed for markers", isSideEffect: false, pack: "Small Pack")
            });

            PlanReview review = await PlanValidator.ReviewAsync(plan, world, CancellationToken.None);
            ReviewedAction action = Assert.Single(review.Actions);

            Assert.True(action.CanApply);
            Assert.Contains("33 KB", action.Summary);
            Assert.Contains("Pathing will restart", action.Summary);
            Assert.DoesNotContain("0 MB", action.Summary);
        }

        [Fact]
        public async Task Continuation_with_skipped_pack_guides_player_to_pathing_menu_without_wait_or_restart_advice() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Malformed Pack", "malformed.taco", "Desc", "Cat", 0.033, "url", "1", false)
                }
            };
            world.Modules.Add(new InstalledModuleInfo("bh.community.pathing", "Pathing", "1.13.2", "", enabled: true));

            world.AnswerDownloadMarkerPack = (pack, ct) => {
                world.DownloadedPacks.Add(pack);
                return Task.FromResult(new MarkerPackChange(true, true, pack, "malformed.taco", 0.033));
            };

            world.AnswerFindMarkerCategories = (search, mapId, ct) => {
                return Task.FromResult(new MarkerCategoriesResult(
                    MarkerCategoriesStatus.Ok, 15, "Queensdale", Array.Empty<MarkerCategoryMatch>(),
                    skippedPacks: new[] { "Malformed Pack" }));
            };

            // Turn 1: Propose download
            model.WillCallTool("submit_plan", "call_1",
                               new Dictionary<string, object> {
                                   ["reply"] = "I will download Malformed Pack.",
                                   ["actions"] = new object[] {
                                       new Dictionary<string, object> {
                                           ["kind"] = "download_pack",
                                           ["pack"] = "Malformed Pack",
                                           ["reason"] = "Needed for markers",
                                           ["is_side_effect"] = false
                                       }
                                   }
                               });

            // Turn 2 (Continuation round 1): Model calls find_marker_categories
            model.WillCallTool("find_marker_categories", "call_2",
                               new Dictionary<string, object> {
                                   ["search"] = "Malformed Pack"
                               });

            // Turn 3 (Continuation round 2): Model replies with category guidance
            model.WillReply("The category list for Malformed Pack could not be read. You can find it in Pathing's menu under Malformed Pack.");

            var chat = NewChat(model, world);
            await chat.SendAsync("Get Malformed Pack");

            Assert.NotNull(chat.Review);
            await chat.ApplyAsync(new[] { 0 });

            // Check that the continuation prompt sent to the model instructed it about skipped packs
            var continuationRequest = model.Requests.FirstOrDefault(req =>
                req.Any(m => m.Role == ChatRole.User && m.Text != null && m.Text.Contains("applied the plan you proposed")));

            Assert.NotNull(continuationRequest);
            var userMsg = continuationRequest.First(m => m.Role == ChatRole.User && m.Text.Contains("applied the plan you proposed"));
            Assert.Contains("skipped_packs", userMsg.Text);
            Assert.Contains("Pathing's menu under the pack's name", userMsg.Text);
            Assert.Contains("do not suggest", userMsg.Text);

            // Check that the model's reply reached the Chat messages
            var lastReply = chat.Messages.LastOrDefault(m => m.Author == ChatMessageAuthor.Ai)?.Text;
            Assert.NotNull(lastReply);
            Assert.Contains("could not be read", lastReply);
            Assert.Contains("Pathing's menu under Malformed Pack", lastReply);
            Assert.DoesNotContain("indexing", lastReply, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("restart", lastReply, StringComparison.OrdinalIgnoreCase);
        }
    }
}
