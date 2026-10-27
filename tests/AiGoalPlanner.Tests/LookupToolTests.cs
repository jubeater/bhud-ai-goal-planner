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
        [Fact]
        public async Task A_nested_Setting_comes_back_by_its_qualified_key_and_a_nested_secret_does_not_come_back() {
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));
            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok(
                "Pathing",
                ModuleSettingInfo.Subcollection(
                    "markers", "Markers", "Everything about markers.",
                    new[] {
                        new ModuleSettingInfo("api_key", "API key", "", "String", "sk-nested-9", null, null),
                        new ModuleSettingInfo("trail.size", "Trail size", "How wide trails are.", "Int32", "3", "1", "10"),
                    }),
                ModuleSettingInfo.Subcollection(
                    "credentials", "Credentials", "Accounts for map services.",
                    new[] {
                        new ModuleSettingInfo("show.grid", "Show grid", "", "Boolean", "true", null, null),
                    }));

            AIFunction tool = GetModuleSettingsTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = "bh.pathing" }));

            JObject trailSize = (JObject)((JArray)result["settings"]).First(setting => ((string)((JObject)setting)["key"]) == "markers.trail.size");

            Assert.Equal("Trail size", trailSize["label"]);
            Assert.Equal("3",          trailSize["value"]);
            Assert.Equal("1",          trailSize["min"]);
            Assert.Equal("10",         trailSize["max"]);

            string resultText = result.ToString();

            // The nested secret is dropped, and so is the whole subcollection whose own name reads
            // as one, innocent children included.
            Assert.DoesNotContain("sk-nested-9", resultText);
            Assert.DoesNotContain("credentials", resultText);
            Assert.DoesNotContain("show.grid",   resultText);
        }

        [Fact]
        public async Task A_module_with_more_Settings_than_the_limit_returns_the_first_ones_and_says_the_list_was_cut() {
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));

            int total = GetModuleSettingsTool.MaxSettings + 50;

            ModuleSettingInfo[] many =
                Enumerable.Range(1, total)
                          .Select(index => new ModuleSettingInfo("setting." + index, "Setting " + index, "",
                                                                 "Int32", index.ToString(), null, null))
                          .ToArray();

            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok("Pathing", many);

            AIFunction tool = GetModuleSettingsTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = "bh.pathing" }));

            JArray settings = (JArray)result["settings"];

            // The first ones, in the order the module holds them...
            Assert.Equal(GetModuleSettingsTool.MaxSettings, settings.Count);
            Assert.Equal("setting.1",   (string)((JObject)settings[0])["key"]);
            Assert.Equal("setting.200", (string)((JObject)settings[settings.Count - 1])["key"]);

            // ...and a note that the rest exists but was not sent, so one large module cannot flood
            // the Request.
            Assert.Equal(true, result["truncated"]);
            Assert.Equal(total, (int)result["settings_total"]);
            Assert.Contains("cut", ((string)result["note"]).ToLowerInvariant());
        }

        [Fact]
        public async Task A_module_with_fewer_Settings_than_the_limit_is_answered_whole_and_says_nothing_about_cutting() {
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));
            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok(
                "Pathing",
                new ModuleSettingInfo("show.trails", "Show trails", "", "Boolean", "true", null, null),
                new ModuleSettingInfo("trail.size",  "Trail size",  "", "Int32",   "3", "1", "10"));

            AIFunction tool = GetModuleSettingsTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = "bh.pathing" }));

            Assert.Equal(2, ((JArray)result["settings"]).Count);
            Assert.Null(result["truncated"]);
            Assert.Null(result["note"]);
        }

        [Fact]
        public async Task A_settings_Lookup_is_logged_with_the_Installed_Modules_own_namespace() {
            var world = new FakeWorld();
            world.Modules.Add(new InstalledModuleInfo("bh.pathing", "Pathing", "3.2.1", "", enabled: true));
            world.SettingsByNamespace["bh.pathing"] = FakeWorld.Ok(
                "Pathing",
                new ModuleSettingInfo("trail.size", "Trail size", "", "Int32", "3", "1", "10"));

            var log = new List<string>();

            AIFunction tool = GetModuleSettingsTool.Create(world, log.Add);

            // The model spells the namespace its own way; the log names the Module it matched, not
            // the spelling the model sent.
            await tool.InvokeAsync(new AIFunctionArguments { ["namespace"] = "BH.Pathing" });

            string line = Assert.Single(log);

            Assert.Contains("module=bh.pathing", line);
            Assert.DoesNotContain("BH.Pathing", line);
        }

        [Fact]
        public async Task Getting_location_returns_character_map_and_region() {
            var world = new FakeWorld();
            world.LocationResult = new PlayerLocationResult(
                LocationStatus.Ok,
                new PlayerLocation("Elementalist", 1205, "Domain of Vabbi", 10, "Crystal Desert"));

            AIFunction tool = GetLocationTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync());

            Assert.Equal("ok", (string)result["status"]);
            Assert.Equal("Elementalist", (string)result["character"]);
            Assert.Equal(1205, (int)result["map_id"]);
            Assert.Equal("Domain of Vabbi", (string)result["map"]);
            Assert.Equal(10, (int)result["region_id"]);
            Assert.Equal("Crystal Desert", (string)result["region"]);
        }

        [Fact]
        public async Task Getting_location_when_unavailable_returns_reason() {
            var world = new FakeWorld();
            world.LocationResult = new PlayerLocationResult(LocationStatus.Unavailable, null, "Mumble Link is not running.");

            AIFunction tool = GetLocationTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync());

            Assert.Equal("unavailable", (string)result["status"]);
            Assert.Equal("Mumble Link is not running.", (string)result["reason"]);
        }

        [Fact]
        public async Task Getting_account_access_returns_owned_content() {
            var world = new FakeWorld();
            world.AccountAccessResult = new AccountAccessResult(
                AccountAccessStatus.Ok,
                new[] { "GuildWars2", "HeartOfThorns", "PathOfFire", "EndOfDragons" });

            AIFunction tool = GetAccountAccessTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync());

            Assert.Equal("ok", (string)result["status"]);

            JArray content = (JArray)result["owned_content"];
            Assert.Equal(4, content.Count);
            Assert.Contains("HeartOfThorns", content.Select(c => (string)c));
            Assert.Contains("EndOfDragons", content.Select(c => (string)c));
        }

        [Fact]
        public async Task Getting_account_access_when_permission_missing_returns_missing_permission_notice() {
            var world = new FakeWorld();
            world.AccountAccessResult = new AccountAccessResult(
                AccountAccessStatus.MissingPermission,
                new string[0],
                missingPermission: "account");

            AIFunction tool = GetAccountAccessTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync());

            Assert.Equal("unavailable", (string)result["status"]);
            Assert.Equal("missing_permission", (string)result["reason"]);
            Assert.Equal("account", (string)result["permission"]);
            Assert.Contains("account", (string)result["notice"]);
        }

        [Fact]
        public async Task Getting_mastery_progress_returns_regions_points_and_unfinished_tracks() {
            var world = new FakeWorld();
            world.MasteryProgressResult = new ProgressResult(
                ProgressStatus.Ok, ProgressKind.Mastery,
                masteryRegions: new[] {
                    new MasteryRegionProgress("Central Tyria", 49, 54, new[] {
                        new MasteryTrackProgress(1, "Pact Commander", "Central Tyria", 4, 5)
                    }),
                    new MasteryRegionProgress("Heart of Maguuma", 150, 163, new[] {
                        new MasteryTrackProgress(2, "Gliding", "Heart of Maguuma", 5, 6),
                        new MasteryTrackProgress(3, "Itzel Lore", "Heart of Maguuma", 2, 6)
                    })
                });

            AIFunction tool = GetProgressTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["kind"] = "mastery" }));

            Assert.Equal("ok", (string)result["status"]);
            Assert.Equal("mastery", (string)result["kind"]);

            JArray regions = (JArray)result["regions"];
            Assert.Equal(2, regions.Count);

            JObject centralTyria = (JObject)regions[0];
            Assert.Equal("Central Tyria", (string)centralTyria["region"]);
            Assert.Equal(54, (int)centralTyria["points_earned"]);
            Assert.Equal(49, (int)centralTyria["points_spent"]);
            Assert.Equal(5, (int)centralTyria["points_unspent"]);

            JArray unfinished = (JArray)centralTyria["unfinished_tracks"];
            Assert.Single(unfinished);
            Assert.Equal("Pact Commander", (string)((JObject)unfinished[0])["name"]);
            Assert.Equal(4, (int)((JObject)unfinished[0])["level"]);
            Assert.Equal(5, (int)((JObject)unfinished[0])["total_levels"]);
        }

        [Fact]
        public async Task Getting_hero_points_progress_returns_map_and_region_hero_challenges() {
            var world = new FakeWorld();
            world.HeroPointsProgressResult = new ProgressResult(
                ProgressStatus.Ok, ProgressKind.HeroPoints,
                heroChallenges: new HeroChallengeProgress(mapTotal: 14, mapCompleted: 7, regionMissing: 42),
                characterName: "Ranger", mapId: 1205, mapName: "Domain of Vabbi", regionName: "Crystal Desert");

            AIFunction tool = GetProgressTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["kind"] = "hero_points" }));

            Assert.Equal("ok", (string)result["status"]);
            Assert.Equal("hero_points", (string)result["kind"]);
            Assert.Equal("Ranger", (string)result["character"]);
            Assert.Equal("Domain of Vabbi", (string)result["map"]);
            Assert.Equal(1205, (int)result["map_id"]);
            Assert.Equal("Crystal Desert", (string)result["region"]);

            JObject hc = (JObject)result["hero_challenges"];
            Assert.Equal(14, (int)hc["map_total"]);
            Assert.Equal(7, (int)hc["map_completed"]);
            Assert.Equal(7, (int)hc["map_missing"]);
            Assert.Equal(42, (int)hc["region_missing"]);
        }

        [Fact]
        public async Task Getting_progress_when_permission_missing_returns_missing_permission_notice() {
            var world = new FakeWorld();
            world.MasteryProgressResult = new ProgressResult(
                ProgressStatus.MissingPermission, ProgressKind.Mastery,
                missingPermission: "progression");

            AIFunction tool = GetProgressTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["kind"] = "mastery" }));

            Assert.Equal("unavailable", (string)result["status"]);
            Assert.Equal("missing_permission", (string)result["reason"]);
            Assert.Equal("progression", (string)result["permission"]);
            Assert.Contains("progression", (string)result["notice"]);
        }

        [Fact]
        public async Task Getting_progress_with_invalid_kind_returns_instruction() {
            var world = new FakeWorld();
            AIFunction tool = GetProgressTool.Create(world);

            JObject result = Parse(await tool.InvokeAsync(new AIFunctionArguments { ["kind"] = "unknown" }));

            Assert.Equal("unavailable", (string)result["status"]);
            Assert.Contains("mastery", (string)result["instruction"]);
            Assert.Contains("hero_points", (string)result["instruction"]);
        }

        /// <summary>
        /// The tool's JSON. A hand-written Lookup returns its result string as it is, the way the
        /// Chat receives it.
        /// </summary>
        private static JObject Parse(object toolResult) {
            return JObject.Parse(Assert.IsType<string>(toolResult));
        }
    }
}
