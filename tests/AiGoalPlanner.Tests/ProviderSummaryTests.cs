using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the line under the Chat's input row: it has to say, before the Player sends anything,
    /// whether the Chat knows which Provider to talk to.
    /// </summary>
    public class ProviderSummaryTests
    {
        [Theory]
        [InlineData(null, null)]
        [InlineData("", "deepseek-flash")]
        [InlineData("   ", "deepseek-flash")]
        [InlineData("https://api.deepseek.com", "")]
        [InlineData("https://api.deepseek.com", "   ")]
        public void A_Provider_that_is_not_set_up_yet_is_reported_as_such(string endpoint, string model) {
            Assert.Equal("No Provider yet. Click Provider settings to connect one.",
                         ProviderSummary.Describe(endpoint, model, hasKey: false));
        }

        [Theory]
        [InlineData("api.deepseek.com")]
        [InlineData("localhost:11434/v1")]
        [InlineData("ftp://example.com/v1")]
        public void An_endpoint_that_is_not_an_http_url_is_reported_as_invalid(string endpoint) {
            Assert.Equal("The endpoint URL is not valid. Check it in Provider settings.",
                         ProviderSummary.Describe(endpoint, "deepseek-flash", hasKey: false));
        }

        [Fact]
        public void A_configured_Provider_is_named_with_its_model_and_host() {
            string summary = ProviderSummary.Describe(" https://api.deepseek.com/v1 ", " deepseek-flash ", hasKey: false);

            Assert.Equal("Using deepseek-flash at api.deepseek.com without an API key.", summary);
        }

        [Fact]
        public void A_stored_key_is_reported_as_present_and_never_shown() {
            string summary = ProviderSummary.Describe("https://api.deepseek.com", "deepseek-flash", hasKey: true);

            Assert.Equal("Using deepseek-flash at api.deepseek.com with an API key.", summary);
        }
    }
}
