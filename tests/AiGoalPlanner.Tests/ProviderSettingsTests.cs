using System;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests what the Player types into the Provider settings before it is used: a Provider needs
    /// an endpoint and a model, the key is optional, and nothing that is obviously wrong is kept.
    /// </summary>
    public class ProviderSettingsTests
    {
        [Fact]
        public void What_the_Player_typed_is_trimmed_and_kept_as_it_is_otherwise() {
            ProviderSettings settings = ProviderSettings.Parse("  https://api.deepseek.com  ", " deepseek-chat ", "  sk-the-key  ");

            Assert.Equal("https://api.deepseek.com", settings.Endpoint);
            Assert.Equal("https://api.deepseek.com/", settings.EndpointUri.ToString());
            Assert.Equal("deepseek-chat", settings.Model);
            Assert.Equal("sk-the-key", settings.ApiKey);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void A_local_Provider_works_without_a_key(string key) {
            ProviderSettings settings = ProviderSettings.Parse("http://localhost:11434/v1", "qwen3.5:9b", key);

            Assert.Equal(string.Empty, settings.ApiKey);
        }

        [Theory]
        [InlineData("", "deepseek-chat")]
        [InlineData("   ", "deepseek-chat")]
        [InlineData(null, "deepseek-chat")]
        [InlineData("https://api.deepseek.com", "")]
        [InlineData("https://api.deepseek.com", "  ")]
        [InlineData("https://api.deepseek.com", null)]
        public void A_missing_endpoint_or_model_is_reported_as_not_configured(string endpoint, string model) {
            ProviderException exception = Assert.Throws<ProviderException>(() => ProviderSettings.Parse(endpoint, model, "sk-the-key"));

            Assert.Equal(ProviderFailure.NotConfigured, exception.Failure);
        }

        [Theory]
        [InlineData("api.deepseek.com")]
        [InlineData("localhost:11434/v1")]
        [InlineData("ftp://example.com/v1")]
        [InlineData("not a url")]
        public void An_endpoint_that_is_not_an_http_url_is_reported_as_invalid(string endpoint) {
            ProviderException exception = Assert.Throws<ProviderException>(() => ProviderSettings.Parse(endpoint, "deepseek-chat", null));

            Assert.Equal(ProviderFailure.InvalidEndpoint, exception.Failure);
        }

        [Fact]
        public void An_endpoint_can_be_read_on_its_own_for_the_key_buttons() {
            Assert.Equal("https://api.deepseek.com/v1",
                         ProviderSettings.ParseEndpoint("  https://api.deepseek.com/v1  ").ToString());
        }

        [Theory]
        [InlineData("api.deepseek.com")]
        [InlineData("localhost:11434/v1")]
        [InlineData("ftp://example.com/v1")]
        public void An_endpoint_that_is_not_an_http_url_is_refused_on_its_own_too(string endpoint) {
            ProviderException exception = Assert.Throws<ProviderException>(() => ProviderSettings.ParseEndpoint(endpoint));

            Assert.Equal(ProviderFailure.InvalidEndpoint, exception.Failure);
        }

        [Fact]
        public void A_missing_endpoint_is_refused_on_its_own_too() {
            ProviderException exception = Assert.Throws<ProviderException>(() => ProviderSettings.ParseEndpoint("   "));

            Assert.Equal(ProviderFailure.NotConfigured, exception.Failure);
        }

        [Fact]
        public void The_key_is_never_part_of_how_the_settings_describe_themselves() {
            ProviderSettings settings = ProviderSettings.Parse("https://api.deepseek.com", "deepseek-chat", "sk-the-key");

            Assert.DoesNotContain("sk-the-key", settings.ToString());
            Assert.Contains("deepseek-chat", settings.ToString());
        }
    }
}
