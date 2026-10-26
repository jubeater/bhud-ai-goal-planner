using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using jubeater.aigoalplanner.Core;
using jubeater.aigoalplanner.Tests.Fakes;
using Microsoft.Extensions.AI;
using Newtonsoft.Json.Linq;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the first Lookups the way the model sees them: a tool is invoked, and its result is
    /// the JSON the model reads. The world is a <see cref="FakeWorld"/>, so these say nothing about
    /// whether the real Blish HUD host reports the truth — that is what the in-game check is for.
    /// </summary>
    public class LookupToolTests
    {
        [Fact]
        public async Task Listing_modules_includes_disabled_ones_and_their_details() {
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "Shows markers and trails.", enabled: true));
            world.Modules.Add(new InstalledModuleInfo("bh.tacoo", "TacO", "1.0.0", "Old marker overlay.", enabled: false));

            AIFunction tool = ListInstalledModulesTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync());

            JArray modules = (JArray)result["modules"];

            Assert.Equal(2, modules.Count);

            JObject pathing = (JObject)modules[0];

            Assert.Equal("bh.pathing", pathing["namespace"]);
            Assert.Equal("Pathing",    pathing["name"]);
            Assert.Equal("3.2.1",      pathing["version"]);
            Assert.Equal(true,         pathing["enabled"]);
            Assert.Equal("Shows markers and trails.", pathing["description"]);

            Assert.Equal(false, ((JObject)modules[1])["enabled"]);
        }

        [Fact]
        public async Task Reading_settings_returns_label_value_and_range() {
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));
            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok(
                "Pathing",
                new ModuleSettingInfo("trail.size", "Trail size", "How wide trails are drawn.", "Int32", "3", "1", "10"),
                new ModuleSettingInfo("show.trails", "Show trails", null, "Boolean", "true", null, null));

            AIFunction tool = GetModuleSettingsTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = "bh.pathing" }));

            Assert.Equal("Pathing", result["module"]);

            JArray settings = (JArray)result["settings"];

            JObject trailSize = (JObject)settings[0];

            Assert.Equal("trail.size",  trailSize["key"]);
            Assert.Equal("Trail size",  trailSize["label"]);
            Assert.Equal("Int32",       trailSize["type"]);
            Assert.Equal("3",           trailSize["value"]);
            Assert.Equal("1",           trailSize["min"]);
            Assert.Equal("10",          trailSize["max"]);
            Assert.Equal("How wide trails are drawn.", trailSize["description"]);

            JObject showTrails = (JObject)settings[1];

            Assert.Null(showTrails["min"]);
            Assert.Null(showTrails["max"]);
        }

        [Fact]
        public async Task A_disabled_module_is_answered_with_enable_it_first() {
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.tacoo", "TacO", "1.0.0", "", enabled: false));
            world.SettingsByNamespace["bh.tacoo"] = FakeWorld.Disabled("TacO");

            AIFunction tool = GetModuleSettingsTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = "bh.tacoo" }));

            Assert.Equal("TacO",      result["module"]);
            Assert.Equal("disabled",  result["status"]);
            Assert.Contains("enable", ((string)result["instruction"]).ToLowerInvariant());
        }

        [Fact]
        public async Task An_unknown_namespace_is_answered_as_not_installed() {
            var world = new FakeWorld();

            AIFunction tool = GetModuleSettingsTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = "bh.missing" }));

            Assert.Equal("not_installed", result["status"]);
        }

        [Fact]
        public async Task The_settings_of_this_module_are_never_returned() {
            // The world would answer with real settings if it were asked at all — the tool has to
            // refuse before that.
            var world = new FakeWorld();
            world.SettingsByNamespace[GetModuleSettingsTool.ThisModuleNamespace] = FakeWorld.Ok(
                "AI Goal Planner",
                new ModuleSettingInfo("provider.endpoint", "Endpoint URL", "", "String", "https://secret.example/v1", null, null));

            AIFunction tool = GetModuleSettingsTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = GetModuleSettingsTool.ThisModuleNamespace }));

            Assert.Equal("private", result["status"]);
            Assert.Equal(0, world.SettingsCalls);
            Assert.DoesNotContain("secret.example", result.ToString());
        }

        [Fact]
        public async Task Settings_named_like_a_secret_are_removed_from_the_answer() {
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.maps", "Maps", "1.0.0", "", enabled: true));
            world.SettingsByNamespace["bh.maps"] = FakeWorld.Ok(
                "Maps",
                new ModuleSettingInfo("api_key",   "API key", "", "String", "sk-live-123", null, null),
                new ModuleSettingInfo("auth",      "Authentication", "", "String", "bearer 456", null, null),
                new ModuleSettingInfo("hotkey",    "Hotkey", "", "String", "Ctrl+M", null, null),
                new ModuleSettingInfo("show.grid", "Show grid", "", "Boolean", "true", null, null));

            AIFunction tool = GetModuleSettingsTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = "bh.maps" }));

            string resultText = result.ToString();

            Assert.DoesNotContain("sk-live-123", resultText);
            Assert.DoesNotContain("bearer 456",  resultText);
            Assert.Contains("hotkey", resultText);
            Assert.Contains("show.grid", resultText);
        }

        /// <summary>
        /// The tool's JSON. Microsoft.Extensions.AI hands the result back as a JsonElement wrapper
        /// around the string the tool returned, the same way the Chat receives it.
        /// </summary>
        private static JObject Parse(object toolResult) {
            return JObject.Parse(Assert.IsType<JsonElement>(toolResult).GetString());
        }
    }
}
