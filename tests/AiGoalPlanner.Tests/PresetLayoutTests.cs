using System.Collections.Generic;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests how Preset buttons share their row: every preset keeps its natural width while the
    /// row fits, a preset that would overflow takes what is left when that is still usable, and
    /// only what cannot be shown usefully is left out — so a preset added as data still shows up.
    /// </summary>
    public class PresetLayoutTests
    {
        [Fact]
        public void Presets_that_fit_keep_their_natural_widths() {
            Assert.Equal(new[] { 120, 130, 90 },
                         PresetLayout.FitWidths(new[] { 120, 130, 90 }, availableWidth: 470, gap: 6, minWidth: 60));
        }

        [Fact]
        public void A_preset_that_overflows_takes_what_is_left_while_it_is_usable() {
            Assert.Equal(new[] { 120, 74 },
                         PresetLayout.FitWidths(new[] { 120, 130, 90 }, availableWidth: 200, gap: 6, minWidth: 60));
        }

        [Fact]
        public void Nothing_fits_in_a_row_with_no_usable_room() {
            Assert.Empty(PresetLayout.FitWidths(new[] { 120 }, availableWidth: 50, gap: 6, minWidth: 60));
        }

        [Fact]
        public void No_presets_need_no_widths() {
            Assert.Empty(PresetLayout.FitWidths(new int[0], availableWidth: 470, gap: 6, minWidth: 60));
        }
    }
}
