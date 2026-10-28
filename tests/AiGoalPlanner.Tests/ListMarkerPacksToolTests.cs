using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class ListMarkerPacksToolTests
    {
        [Fact]
        public async Task Returns_all_packs_when_no_search_specified() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General content", "Hero Points", 47.1, "https://example.com/tw.taco", "1", false),
                    new MarkerPackInfo("Fast TacO", "fast_TacO_pack.taco", "Farming", "Farming", 1.5, "https://example.com/fast.taco", "1", true)
                }
            };

            AIFunction tool = ListMarkerPacksTool.Create(world);
            object rawAnswer = await tool.InvokeAsync();

            string json = Assert.IsType<string>(rawAnswer);
            JObject obj = JObject.Parse(json);

            Assert.Equal("ok", (string)obj["status"]);
            Assert.True((bool)obj["pathing_installed"]);
            Assert.Equal(2, (int)obj["total_packs"]);
            Assert.Equal(1, (int)obj["installed_count"]);

            JArray packs = (JArray)obj["packs"];
            Assert.Equal(2, packs.Count);
            Assert.Equal("Tekkit's All-In-One", (string)packs[0]["name"]);
            Assert.False((bool)packs[0]["installed"]);
            Assert.Equal("Fast TacO", (string)packs[1]["name"]);
            Assert.True((bool)packs[1]["installed"]);
        }

        [Fact]
        public async Task Filters_packs_by_search_query() {
            var world = new FakeWorld {
                MarkerPacks = {
                    new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General content", "Hero Points", 47.1, "https://example.com/tw.taco", "1", false),
                    new MarkerPackInfo("Fast TacO", "fast_TacO_pack.taco", "Farming", "Farming", 1.5, "https://example.com/fast.taco", "1", true)
                }
            };

            AIFunction tool = ListMarkerPacksTool.Create(world);
            object rawAnswer = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object> {
                ["search"] = "hero"
            }));

            string json = Assert.IsType<string>(rawAnswer);
            JObject obj = JObject.Parse(json);

            JArray packs = (JArray)obj["packs"];
            Assert.Single(packs);
            Assert.Equal("Tekkit's All-In-One", (string)packs[0]["name"]);
        }

        [Fact]
        public async Task Reports_when_pathing_is_not_installed() {
            var world = new FakeWorld {
                MarkerPacksResult = new MarkerPacksResult(
                    MarkerPacksStatus.NoPathing,
                    new[] {
                        new MarkerPackInfo("Tekkit's All-In-One", "tw_ALL_IN_ONE.taco", "General", "Hero Points", 47.1, "url", "1", false)
                    },
                    pathingInstalled: false,
                    message: "Pathing is not installed. You can install it from the module catalog.")
            };

            AIFunction tool = ListMarkerPacksTool.Create(world);
            object rawAnswer = await tool.InvokeAsync();

            string json = Assert.IsType<string>(rawAnswer);
            JObject obj = JObject.Parse(json);

            Assert.False((bool)obj["pathing_installed"]);
            Assert.Contains("Pathing is not installed", (string)obj["message"]);
        }

        [Fact]
        public void Progress_text_formats_with_or_without_search() {
            string empty = ListMarkerPacksTool.Progress(new Dictionary<string, object>());
            Assert.Equal("Checking marker packs…", empty);

            string withSearch = ListMarkerPacksTool.Progress(new Dictionary<string, object> {
                ["search"] = "tekkit"
            });
            Assert.Equal("Checking marker packs for 'tekkit'…", withSearch);
        }
    }
}
