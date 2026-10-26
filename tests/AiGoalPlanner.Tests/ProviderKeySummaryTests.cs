using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests what the Provider settings do with the key the Player pastes: what counts as a key on
    /// the clipboard, and what the window is allowed to say about a saved one.
    /// </summary>
    public class ProviderKeySummaryTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\t ")]
        public void An_empty_clipboard_holds_no_key(string clipboardText) {
            Assert.Null(ProviderKeySummary.FromClipboard(clipboardText));
        }

        [Theory]
        [InlineData("sk-the-key", "sk-the-key")]
        [InlineData("  sk-the-key  ", "sk-the-key")]
        [InlineData("sk-the-key\r\n", "sk-the-key")]
        public void A_pasted_key_is_trimmed_and_kept_as_it_is_otherwise(string clipboardText, string expected) {
            Assert.Equal(expected, ProviderKeySummary.FromClipboard(clipboardText));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void No_saved_key_is_reported_as_no_key(string key) {
            Assert.Equal("No key", ProviderKeySummary.Describe(key));
        }

        [Fact]
        public void A_saved_key_is_shown_by_its_last_four_characters_only() {
            string description = ProviderKeySummary.Describe("sk-0123456789-abcd");

            Assert.Equal("Key saved (...abcd)", description);
            Assert.DoesNotContain("0123456789", description);
        }

        [Fact]
        public void The_key_itself_never_fits_in_the_description() {
            const string key = "sk-live-7f3c9a2e5b8d1c4f";

            Assert.DoesNotContain(key, ProviderKeySummary.Describe(key));
        }
    }
}
