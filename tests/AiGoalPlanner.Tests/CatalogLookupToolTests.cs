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
    public class CatalogLookupToolTests
    {
        [Fact]
        public async Task It_lists_catalog_modules_with_all_required_fields() {
            var world = new FakeWorld();
            world.CatalogModules.Add(new CatalogModuleInfo(
                "bh.community.pathing",
                "Pathing",
                "3.2.1",
                "Markers and trails for Guild Wars 2.",
                new[] { "bh.blishhud" },
                isPreview: false,
                apiPermissions: new[] { "account", "characters" }
            ));

            AIFunction tool = SearchCatalogTool.Create(world);
            object raw = await tool.InvokeAsync();

            JObject json = JObject.Parse(Assert.IsType<string>(raw));
            Assert.Equal("ok", (string)json["status"]);
            Assert.Equal(1, (int)json["total_modules"]);

            JArray modules = (JArray)json["modules"];
            Assert.Single(modules);

            JObject module = (JObject)modules[0];
            Assert.Equal("bh.community.pathing", (string)module["namespace"]);
            Assert.Equal("Pathing", (string)module["name"]);
            Assert.Equal("3.2.1", (string)module["version"]);
            Assert.Equal("Markers and trails for Guild Wars 2.", (string)module["description"]);
            Assert.False((bool)module["is_preview"]);

            JArray deps = (JArray)module["dependencies"];
            Assert.Single(deps);
            Assert.Equal("bh.blishhud", (string)deps[0]);

            JArray perms = (JArray)module["api_permissions"];
            Assert.Equal(2, perms.Count);
            Assert.Equal("account", (string)perms[0]);
            Assert.Equal("characters", (string)perms[1]);
        }

        [Fact]
        public async Task It_filters_catalog_modules_by_query() {
            var world = new FakeWorld();
            world.CatalogModules.Add(new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" }));
            world.CatalogModules.Add(new CatalogModuleInfo("bh.mounts", "Mounts", "1.0.0", "Mount wheel and keybinds.", new[] { "bh.blishhud" }));
            world.CatalogModules.Add(new CatalogModuleInfo("bh.eventtable", "Event Table", "2.0.0", "World boss timers.", new[] { "bh.blishhud" }));

            AIFunction tool = SearchCatalogTool.Create(world);

            var args = new AIFunctionArguments { ["query"] = "trail" };
            object raw = await tool.InvokeAsync(args);

            JObject json = JObject.Parse(Assert.IsType<string>(raw));
            Assert.Equal(1, (int)json["total_modules"]);
            JArray modules = (JArray)json["modules"];
            Assert.Equal("bh.community.pathing", (string)modules[0]["namespace"]);
        }

        [Fact]
        public async Task It_returns_empty_module_list_when_no_modules_match_query() {
            var world = new FakeWorld();
            world.CatalogModules.Add(new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" }));

            AIFunction tool = SearchCatalogTool.Create(world);
            var args = new AIFunctionArguments { ["query"] = "nonexistent" };
            object raw = await tool.InvokeAsync(args);

            JObject json = JObject.Parse(Assert.IsType<string>(raw));
            Assert.Equal(0, (int)json["total_modules"]);
            Assert.Empty((JArray)json["modules"]);
        }

        [Fact]
        public void Progress_shows_query_text_when_provided() {
            string progressWithQuery = SearchCatalogTool.Progress(new Dictionary<string, object> { ["query"] = "markers" });
            Assert.Equal("Searching module catalog for 'markers'…", progressWithQuery);

            string progressWithoutQuery = SearchCatalogTool.Progress(new Dictionary<string, object>());
            Assert.Equal("Checking module catalog…", progressWithoutQuery);
        }

        [Fact]
        public async Task It_logs_call_diagnostics() {
            var log = new List<string>();
            var world = new FakeWorld();
            world.CatalogModules.Add(new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails."));

            AIFunction tool = SearchCatalogTool.Create(world, log.Add);
            await tool.InvokeAsync();

            Assert.Single(log);
            Assert.Contains("search_catalog", log[0]);
            Assert.Contains("modules=1", log[0]);
        }

        [Fact]
        public void Tool_is_registered_as_read_only_with_correct_name() {
            var world = new FakeWorld();
            var registry = Lookups.For(world);

            Assert.True(registry.TryGet(SearchCatalogTool.Name, out RegisteredTool tool));
            Assert.Equal(ToolAccess.ReadOnly, tool.Access);
        }
    }
}
