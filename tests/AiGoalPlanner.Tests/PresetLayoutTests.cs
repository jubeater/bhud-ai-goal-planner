using System.Collections.Generic;
using System.Linq;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests how Preset buttons share their rows in the Chat window: each preset keeps its natural
    /// width while the row fits, one that would overflow takes what is left while that is still
    /// usable, and the rest wrap onto further rows — so adding a preset as data can never remove
    /// an existing Preset's button.
    /// </summary>
    public class PresetLayoutTests
    {
        [Fact]
        public void Presets_that_fit_share_one_row_at_their_natural_widths() {
            AssertRows(new[] { new[] { 120, 130, 90 } },
                       PresetLayout.FitRows(new[] { 120, 130, 90 }, availableWidth: 470, gap: 6, minWidth: 60));
        }

        [Fact]
        public void A_full_row_wraps_the_rest_onto_further_rows() {
            IReadOnlyList<IReadOnlyList<int>> rows =
                PresetLayout.FitRows(new[] { 120, 130, 220, 90 }, availableWidth: 466, gap: 6, minWidth: 60);

            AssertRows(new[] { new[] { 120, 130, 204 }, new[] { 90 } }, rows);
            Assert.Equal(4, rows.Sum(row => row.Count));
        }

        [Fact]
        public void A_preset_that_overflows_takes_what_is_left_while_it_is_usable() {
            AssertRows(new[] { new[] { 120, 74 }, new[] { 90 } },
                       PresetLayout.FitRows(new[] { 120, 130, 90 }, availableWidth: 200, gap: 6, minWidth: 60));
        }

        [Fact]
        public void A_preset_wider_than_the_row_is_narrowed_not_dropped() {
            AssertRows(new[] { new[] { 466 } },
                       PresetLayout.FitRows(new[] { 500 }, availableWidth: 466, gap: 6, minWidth: 60));
        }

        [Fact]
        public void No_presets_need_no_rows() {
            Assert.Empty(PresetLayout.FitRows(new int[0], availableWidth: 466, gap: 6, minWidth: 60));
        }

        private static void AssertRows(IReadOnlyList<IReadOnlyList<int>> expected,
                                       IReadOnlyList<IReadOnlyList<int>> actual) {
            Assert.Equal(expected.Count, actual.Count);

            for (int index = 0; index < expected.Count; index++) {
                Assert.Equal(expected[index], actual[index].ToArray());
            }
        }
    }
}
