using System;
using System.Collections.Generic;
using System.Linq;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the Presets the Chat window offers: ready-made Requests for common goals, defined
    /// as data files bundled with the module, so adding one needs no code change.
    /// </summary>
    public class PresetTests
    {
        [Fact]
        public void The_module_bundles_a_preset_for_each_common_goal() {
            IReadOnlyList<Preset> presets = PresetLibrary.Load();

            Assert.Equal(new[] { "Hero points", "Mastery points", "XP" },
                         presets.Select(preset => preset.Name).ToArray());
        }

        [Fact]
        public void Every_bundled_preset_has_a_request_and_hints() {
            foreach (Preset preset in PresetLibrary.Load()) {
                Assert.False(string.IsNullOrWhiteSpace(preset.Request), preset.Name + " has no starter Request.");
                Assert.False(string.IsNullOrWhiteSpace(preset.Hints), preset.Name + " has no hints.");
            }
        }

        [Fact]
        public void One_unreadable_preset_file_is_skipped_while_the_others_still_load() {
            var log = new List<string>();

            IReadOnlyList<Preset> presets = PresetLibrary.LoadFrom(
                new[] { "a.json", "b.json", "c.json" },
                name => name == "b.json"
                            ? throw new InvalidCastException("bad field")
                            : "{\"name\":\"" + name + "\",\"request\":\"Help me.\",\"hints\":\"Check progress.\"}",
                log.Add);

            Assert.Equal(new[] { "a.json", "c.json" },
                         presets.Select(preset => preset.Name).ToArray());
            Assert.Single(log);
            Assert.Contains("InvalidCastException", log[0]);
        }

        [Fact]
        public void A_valid_preset_file_parses() {
            Preset preset = Preset.Parse("{\"name\":\"Hero points\",\"request\":\"Help me.\",\"hints\":\"Check progress.\"}");

            Assert.NotNull(preset);
            Assert.Equal("Hero points", preset.Name);
            Assert.Equal("Help me.", preset.Request);
            Assert.Equal("Check progress.", preset.Hints);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not json")]
        [InlineData("{\"name\":\"\",\"request\":\"Help me.\",\"hints\":\"Check progress.\"}")]
        [InlineData("{\"name\":\"Hero points\",\"request\":\"  \",\"hints\":\"Check progress.\"}")]
        [InlineData("{\"name\":\"Hero points\",\"request\":\"Help me.\",\"hints\":\"\"}")]
        [InlineData("{\"name\":\"Hero points\",\"request\":\"Help me.\"}")]
        [InlineData("[\"Hero points\"]")]
        public void A_preset_file_with_missing_or_blank_fields_is_skipped(string json) {
            Assert.Null(Preset.Parse(json));
        }

        [Theory]
        [InlineData("{\"name\":{},\"request\":\"Help me.\",\"hints\":\"Check progress.\"}")]
        [InlineData("{\"name\":[\"Hero points\"],\"request\":\"Help me.\",\"hints\":\"Check progress.\"}")]
        [InlineData("{\"name\":\"Hero points\",\"request\":[\"Help me.\"],\"hints\":\"Check progress.\"}")]
        [InlineData("{\"name\":\"Hero points\",\"request\":\"Help me.\",\"hints\":{\"text\":\"Check progress.\"}}")]
        [InlineData("{\"name\":42,\"request\":\"Help me.\",\"hints\":\"Check progress.\"}")]
        public void A_preset_file_with_wrongly_typed_fields_is_skipped(string json) {
            Assert.Null(Preset.Parse(json));
        }

        [Fact]
        public void The_hero_hints_enable_an_installed_Pathing_and_keep_a_hand_install_as_fallback() {
            Preset hero = PresetLibrary.Load().Single(candidate => candidate.Name == "Hero points");

            Assert.Contains("enable_module", hero.Hints);
            Assert.Contains("Catalog", hero.Hints);
            Assert.DoesNotContain("enable it by hand", hero.Hints);
        }

        [Fact]
        public void Every_preset_relying_on_a_module_states_the_same_install_rule() {
            foreach (Preset preset in PresetLibrary.Load()) {
                Assert.Contains("enable_module", preset.Hints);
                Assert.Contains("Catalog", preset.Hints);
            }
        }
        [Fact]
        public void Extra_fields_in_a_preset_file_are_ignored() {
            Preset preset = Preset.Parse("{\"name\":\"XP\",\"request\":\"Help me.\",\"hints\":\"Check XP.\",\"notes\":\"For the author.\"}");

            Assert.NotNull(preset);
            Assert.Equal("XP", preset.Name);
        }

        [Fact]
        public void The_prompt_section_names_each_preset_and_carries_its_hints() {
            string section = PresetLibrary.SystemPromptSectionFor(PresetLibrary.Load());

            foreach (Preset preset in PresetLibrary.Load()) {
                Assert.Contains(preset.Name, section);
                Assert.Contains(preset.Hints, section);
            }
        }

        [Fact]
        public void The_prompt_section_is_plain_text() {
            string section = PresetLibrary.SystemPromptSectionFor(PresetLibrary.Load());

            Assert.DoesNotContain("**", section);
            Assert.DoesNotContain("```", section);
        }

        [Fact]
        public void No_presets_means_no_prompt_section() {
            Assert.Equal(string.Empty, PresetLibrary.SystemPromptSectionFor(new Preset[0]));
            Assert.Equal(string.Empty, PresetLibrary.SystemPromptSectionFor(null));
        }
    }
}
