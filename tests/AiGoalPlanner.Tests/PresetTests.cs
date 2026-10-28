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

        [Fact]
        public void Extra_fields_in_a_preset_file_are_ignored() {
            Preset preset = Preset.Parse("{\"name\":\"XP\",\"request\":\"Help me.\",\"hints\":\"Check XP.\",\"season\":\"Janthir\"}");

            Assert.NotNull(preset);
            Assert.Equal("XP", preset.Name);
        }

        [Fact]
        public void The_prompt_section_names_each_preset_and_carries_its_hints() {
            string section = PresetLibrary.SectionFor(PresetLibrary.Load());

            foreach (Preset preset in PresetLibrary.Load()) {
                Assert.Contains(preset.Name, section);
                Assert.Contains(preset.Hints, section);
            }
        }

        [Fact]
        public void The_prompt_section_is_plain_text() {
            string section = PresetLibrary.SectionFor(PresetLibrary.Load());

            Assert.DoesNotContain("**", section);
            Assert.DoesNotContain("```", section);
        }

        [Fact]
        public void No_presets_means_no_prompt_section() {
            Assert.Equal(string.Empty, PresetLibrary.SectionFor(new Preset[0]));
            Assert.Equal(string.Empty, PresetLibrary.SectionFor(null));
        }
    }
}
