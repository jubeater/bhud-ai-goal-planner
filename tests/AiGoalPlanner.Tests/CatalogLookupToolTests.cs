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
            Assert.Null(module["description"]);
            Assert.Null(module["api_permissions"]);
            Assert.False((bool)module["is_preview"]);

            JArray deps = (JArray)module["dependencies"];
            Assert.Single(deps);
            Assert.Equal("bh.blishhud", (string)deps[0]);
        }

        [Fact]
        public void Tool_description_does_not_mention_description() {
            var world = new FakeWorld();
            AIFunction tool = SearchCatalogTool.Create(world);
            Assert.DoesNotContain("description", tool.Description, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Tool_schema_query_description_does_not_mention_module_description() {
            var world = new FakeWorld();
            AIFunction tool = SearchCatalogTool.Create(world);
            string schema = tool.JsonSchema.GetRawText();
            Assert.DoesNotContain("or description", schema, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task It_filters_catalog_modules_by_query_matching_name_or_namespace_only() {
            var world = new FakeWorld();
            world.CatalogModules.Add(new CatalogModuleInfo("bh.community.pathing", "Pathing", "3.2.1", "Markers and trails.", new[] { "bh.blishhud" }));
            world.CatalogModules.Add(new CatalogModuleInfo("bh.mounts", "Mounts", "1.0.0", "Mount wheel and keybinds.", new[] { "bh.blishhud" }));
            world.CatalogModules.Add(new CatalogModuleInfo("bh.eventtable", "Event Table", "2.0.0", "World boss timers.", new[] { "bh.blishhud" }));

            AIFunction tool = SearchCatalogTool.Create(world);

            var args = new AIFunctionArguments { ["query"] = "trail" };
            object raw = await tool.InvokeAsync(args);

            JObject json = JObject.Parse(Assert.IsType<string>(raw));
            // Should not match "trail" because it's only in the description, not name or namespace
            Assert.Equal(0, (int)json["total_modules"]);
            Assert.Empty((JArray)json["modules"]);

            var argsName = new AIFunctionArguments { ["query"] = "mounts" };
            object rawName = await tool.InvokeAsync(argsName);
            JObject jsonName = JObject.Parse(Assert.IsType<string>(rawName));
            Assert.Equal(1, (int)jsonName["total_modules"]);
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
