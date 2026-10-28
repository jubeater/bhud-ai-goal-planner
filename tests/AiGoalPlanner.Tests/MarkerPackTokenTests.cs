using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Xunit;
using ChatMessage = jubeater.aigoalplanner.Core.ChatMessage;

namespace jubeater.aigoalplanner.Tests
{
    public class MarkerPackTokenTests
    {
        private static Chat NewChatWithTokens(ScriptedChatClient model, FakeWorld world, long budget, TokenPrice prices) {
            IBlishHudWorld target = new GameThreadWorld(world, new ImmediateGameThread());
            var plans = new Plans(target, () => false);
            var tools = Lookups.For(target);
            plans.AddTo(tools);

            return new Chat(() => model, () => SystemPrompt.For("test-model", "https://provider.test/v1"),
                            tools, logDebug: null, logWarn: null, plans: plans,
                            tokenBudget: () => budget, tokenPrices: () => prices);
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
        public async Task Continuation_after_download_records_usage_and_costs() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };

            model.WillCallTool("submit_plan", "call_plan",
                               Plan("I propose downloading Tekkit's pack.",
                                    DownloadAction("Tekkit's All-In-One")),
                               usage: new UsageDetails { InputTokenCount = 100, OutputTokenCount = 20 });

            var prices = new TokenPrice(3m, 15m);
            var chat = NewChatWithTokens(model, world, TokenBudget.Default, prices);
            await chat.SendAsync("Get Tekkit's pack");

            world.MarkerCategoriesResult = new MarkerCategoriesResult(
                MarkerCategoriesStatus.Ok, mapId: 1, mapName: "Lion's Arch", categories: Array.Empty<MarkerCategoryMatch>());
            model.WillCallTool("find_marker_categories", "call_cat", new Dictionary<string, object>(),
                               usage: new UsageDetails { InputTokenCount = 200, OutputTokenCount = 30 });
            model.WillReply("Tekkit's pack is installed.",
                            new UsageDetails { InputTokenCount = 300, OutputTokenCount = 50 });

            await chat.ApplyAsync(new[] { 0 });

            // Continuation reply carries usage from continuation rounds
            ChatMessage reply = chat.Messages.Last();
            Assert.Equal(ChatMessageAuthor.Ai, reply.Author);
            Assert.NotNull(reply.Usage);
            Assert.Equal((long?)500, reply.Usage.InputTokens);
            Assert.Equal((long?)80, reply.Usage.OutputTokens);

            // Total usage across proposal turn and continuation turn
            Assert.Equal((long?)600, chat.TotalUsage.InputTokens);
            Assert.Equal((long?)100, chat.TotalUsage.OutputTokens);
            Assert.NotNull(chat.TotalEstimatedCostUsd);
            Assert.True(chat.TotalEstimatedCostUsd > 0);
        }

        [Fact]
        public async Task Continuation_after_download_stops_if_token_budget_is_reached() {
            var model = new ScriptedChatClient();
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                }
            };

            // Proposal uses 60 tokens (< 100 budget)
            model.WillCallTool("submit_plan", "call_plan",
                               Plan("I propose downloading Tekkit's pack.",
                                    DownloadAction("Tekkit's All-In-One")),
                               usage: new UsageDetails { InputTokenCount = 50, OutputTokenCount = 10 });

            var chat = NewChatWithTokens(model, world, 100, null);
            await chat.SendAsync("Get Tekkit's pack");

            Assert.Single(model.Requests);

            // Continuation round 1 calls find_marker_categories and uses 60 tokens (total 120 >= 100 budget)
            world.MarkerCategoriesResult = new MarkerCategoriesResult(
                MarkerCategoriesStatus.Ok, mapId: 1, mapName: "Lion's Arch", categories: Array.Empty<MarkerCategoryMatch>());
            model.WillCallTool("find_marker_categories", "call_cat", new Dictionary<string, object>(),
                               usage: new UsageDetails { InputTokenCount = 50, OutputTokenCount = 10 });
            model.WillReply("This reply must never be reached.");

            // Apply download: pack downloads, then continuation round 1 executes, and round 2 stops before model call
            await chat.ApplyAsync(new[] { 0 });

            // Exactly 2 model requests were made (proposal + find_marker_categories call; the final reply was stopped)
            Assert.Equal(2, model.Requests.Count);
            Assert.Single(world.DownloadedPacks);

            // Budget notice is present
            Assert.Contains(chat.Messages, m => m.Author == ChatMessageAuthor.Notice && m.Text.Contains("Token budget reached"));
            Assert.DoesNotContain(chat.Messages, m => m.Text == "This reply must never be reached.");
            Assert.Equal((long?)100, chat.TotalUsage.InputTokens);
        }
    }
}
