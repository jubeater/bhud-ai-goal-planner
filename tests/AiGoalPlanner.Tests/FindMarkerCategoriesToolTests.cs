using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class FindMarkerCategoriesToolTests
    {
        [Fact]
        public void Tool_declaration_is_configured_correctly() {
            var fakeWorld = new FakeWorld();
            AIFunction function = FindMarkerCategoriesTool.Create(fakeWorld);

            Assert.Equal("find_marker_categories", function.Name);
            Assert.False(string.IsNullOrWhiteSpace(function.Description));
            Assert.True(function.JsonSchema.ValueKind == System.Text.Json.JsonValueKind.Object);
        }

        [Fact]
        public void Progress_text_includes_query_when_present() {
            var argsWithQuery = new Dictionary<string, object> { ["search"] = "hero points" };
            Assert.Equal("Searching marker categories for 'hero points'…", FindMarkerCategoriesTool.Progress(argsWithQuery));

            var argsEmpty = new Dictionary<string, object>();
            Assert.Equal("Searching marker categories…", FindMarkerCategoriesTool.Progress(argsEmpty));

            Assert.Equal("Searching marker categories…", FindMarkerCategoriesTool.Progress(null));
        }

        [Fact]
        public async Task Answers_no_pathing_when_pathing_not_installed() {
            var fakeWorld = new FakeWorld {
                MarkerCategoriesResult = MarkerCategoriesResult.NoPathing()
            };

            AIFunction function = FindMarkerCategoriesTool.Create(fakeWorld);
            var args = new AIFunctionArguments { ["search"] = "routes" };

            object raw = await function.InvokeAsync(args, CancellationToken.None);
            string json = Assert.IsType<string>(raw);

            JObject parsed = JObject.Parse(json);
            Assert.Equal("no_pathing", (string)parsed["status"]);
            Assert.Contains("Pathing is not installed", (string)parsed["message"]);
        }

        [Fact]
        public async Task Answers_no_packs_when_no_packs_installed() {
            var fakeWorld = new FakeWorld {
                MarkerCategoriesResult = MarkerCategoriesResult.NoPacks()
            };

            AIFunction function = FindMarkerCategoriesTool.Create(fakeWorld);
            var args = new AIFunctionArguments { ["search"] = "routes" };

            object raw = await function.InvokeAsync(args, CancellationToken.None);
            string json = Assert.IsType<string>(raw);

            JObject parsed = JObject.Parse(json);
            Assert.Equal("no_packs", (string)parsed["status"]);
            Assert.Contains("No marker packs are installed", (string)parsed["message"]);
        }

        [Fact]
        public async Task Answers_matches_with_pack_display_path_shown_and_counts() {
            var matches = new List<MarkerCategoryMatch> {
                new MarkerCategoryMatch(
                    "Tekkit's Guides",
                    "Tekkit's Guides › Path of Fire › Routes - Hero Points Only",
                    "tw_guides.tw_mc_pof.tw_mc_pof_trails_hp",
                    shown: false,
                    markerCount: 2,
                    trailCount: 1)
            };

            var fakeWorld = new FakeWorld {
                MarkerCategoriesResult = new MarkerCategoriesResult(
                    MarkerCategoriesStatus.Ok, 1210, "Crystal Oasis", matches,
                    cut: false, totalMatches: 1)
            };

            AIFunction function = FindMarkerCategoriesTool.Create(fakeWorld);
            var args = new AIFunctionArguments {
                ["search"] = "hero point routes",
                ["map"] = 1210
            };

            object raw = await function.InvokeAsync(args, CancellationToken.None);
            string json = Assert.IsType<string>(raw);

            JObject parsed = JObject.Parse(json);
            Assert.Equal("ok", (string)parsed["status"]);
            Assert.Equal(1210, (int)parsed["map_id"]);
            Assert.Equal("Crystal Oasis", (string)parsed["map_name"]);
            Assert.Equal(1, (int)parsed["total_matches"]);
            Assert.False((bool)parsed["cut"]);

            JArray categories = (JArray)parsed["categories"];
            Assert.Single(categories);
            JObject first = (JObject)categories[0];
            Assert.Equal("Tekkit's Guides", (string)first["pack"]);
            Assert.Equal("Tekkit's Guides › Path of Fire › Routes - Hero Points Only", (string)first["display_path"]);
            Assert.Equal("tw_guides.tw_mc_pof.tw_mc_pof_trails_hp", (string)first["namespace"]);
            Assert.False((bool)first["shown"]);
            Assert.Equal(2, (int)first["markers"]);
            Assert.Equal(1, (int)first["trails"]);
            Assert.Equal(3, (int)first["count"]);
        }
    }
}
