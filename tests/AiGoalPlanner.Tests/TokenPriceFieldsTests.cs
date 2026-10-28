using System;
using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests what the Provider settings' price fields show: they always describe the endpoint
    /// they belong to.
    /// </summary>
    public class TokenPriceFieldsTests
    {
        [Fact]
        public void A_new_window_starts_with_empty_fields() {
            var fields = new TokenPriceFields();

            Assert.Equal(string.Empty, fields.InputText);
            Assert.Equal(string.Empty, fields.OutputText);
        }

        [Fact]
        public void The_fields_show_the_endpoint_they_belong_to() {
            var fields = new TokenPriceFields();

            fields.EndpointChanged("https://api.deepseek.com", endpoint =>
                endpoint == "https://api.deepseek.com" ? new TokenPrice(0.14m, 0.28m) : null);

            Assert.Equal("0.14", fields.InputText);
            Assert.Equal("0.28", fields.OutputText);
        }

        [Fact]
        public void An_endpoint_without_saved_prices_empties_both_fields() {
            var fields = new TokenPriceFields();
            fields.EndpointChanged("https://api.deepseek.com", endpoint => new TokenPrice(0.14m, 0.28m));

            fields.EndpointChanged("https://unknown.example/v1", endpoint => (TokenPrice)null);

            Assert.Equal(string.Empty, fields.InputText);
            Assert.Equal(string.Empty, fields.OutputText);
        }

        [Fact]
        public void Typing_another_endpoint_replaces_typed_but_unsaved_prices() {
            var fields = new TokenPriceFields();
            fields.EndpointChanged("http://localhost:11434/v1", endpoint => new TokenPrice(0.27m, 1.1m));
            fields.Edited("0.14", "0.28");

            // The typed prices were never saved for the first endpoint; moving to the second
            // endpoint reloads its saved prices rather than carrying the first endpoint's.
            fields.EndpointChanged("https://api.deepseek.com", endpoint =>
                endpoint == "https://api.deepseek.com" ? new TokenPrice(0.14m, 0.28m) : new TokenPrice(0.27m, 1.1m));

            Assert.Equal("0.14", fields.InputText);
            Assert.Equal("0.28", fields.OutputText);
        }

        [Fact]
        public void Typing_an_unknown_endpoint_clears_stale_prices() {
            var fields = new TokenPriceFields();
            fields.EndpointChanged("http://localhost:11434/v1", endpoint => new TokenPrice(0.27m, 1.1m));

            fields.EndpointChanged("https://half-typed.exa", endpoint => (TokenPrice)null);

            Assert.Equal(string.Empty, fields.InputText);
            Assert.Equal(string.Empty, fields.OutputText);
        }

        [Fact]
        public void What_the_player_typed_stays_until_the_endpoint_changes() {
            var fields = new TokenPriceFields();

            fields.Edited("0.14", "0.28");

            Assert.Equal("0.14", fields.InputText);
            Assert.Equal("0.28", fields.OutputText);
        }
    }
}
