using jubeater.aigoalplanner.Core;
using Microsoft.Extensions.AI;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    /// <summary>
    /// Tests the token counting the Chat shows: adding usage up, reading it back, and the budget
    /// question and price estimates built on it.
    /// </summary>
    public class TokenUsageTests
    {
        [Fact]
        public void Adding_usage_sums_each_part() {
            var first  = new TokenUsage(1200, 900, 80);
            var second = new TokenUsage(300, 100, 20);

            TokenUsage total = first.Add(second);

            Assert.Equal((long?)1500, total.InputTokens);
            Assert.Equal((long?)1000, total.CachedInputTokens);
            Assert.Equal((long?)100,  total.OutputTokens);
        }

        [Fact]
        public void Adding_usage_keeps_what_only_one_side_reported() {
            TokenUsage total = new TokenUsage(1200, null, 80).Add(new TokenUsage(null, 900, null));

            Assert.Equal((long?)1200, total.InputTokens);
            Assert.Equal((long?)900,  total.CachedInputTokens);
            Assert.Equal((long?)80,   total.OutputTokens);
        }

        [Fact]
        public void The_total_counts_input_and_output_but_not_cached_twice() {
            Assert.Equal(1280, new TokenUsage(1200, 900, 80).Total);
        }

        [Fact]
        public void A_missing_usage_block_is_no_usage() {
            Assert.Null(TokenUsage.FromResponse(null));
        }

        [Fact]
        public void A_usage_block_with_no_counts_is_no_usage() {
            Assert.Null(TokenUsage.FromResponse(new UsageDetails()));
        }

        [Fact]
        public void A_usage_block_maps_to_counts_with_its_cached_split() {
            TokenUsage usage = TokenUsage.FromResponse(new UsageDetails {
                InputTokenCount       = 1200,
                CachedInputTokenCount = 900,
                OutputTokenCount      = 80,
                TotalTokenCount       = 1280
            });

            Assert.Equal((long?)1200, usage.InputTokens);
            Assert.Equal((long?)900,  usage.CachedInputTokens);
            Assert.Equal((long?)80,   usage.OutputTokens);
        }

        [Fact]
        public void Usage_describes_exact_counts_with_the_cached_split() {
            Assert.Equal("1,200 in · 900 cached · 80 out", new TokenUsage(1200, 900, 80).Describe());
        }

        [Fact]
        public void Usage_without_a_cached_split_describes_without_one() {
            Assert.Equal("1,200 in · 80 out", new TokenUsage(1200, null, 80).Describe());
        }

        [Fact]
        public void Empty_usage_describes_as_nothing() {
            Assert.Equal(string.Empty, TokenUsage.Empty.Describe());
            Assert.Equal(string.Empty, TokenUsage.Empty.DescribeWithCost(0.01m));
        }

        [Fact]
        public void The_cost_needs_both_prices() {
            var usage = new TokenUsage(1200, 900, 80);

            Assert.Null(usage.EstimateCost(null));
            Assert.Null(usage.EstimateCost(new TokenPrice(null, null)));
            Assert.Null(usage.EstimateCost(new TokenPrice(3m, null)));
            Assert.Null(usage.EstimateCost(new TokenPrice(null, 15m)));
            Assert.Null(TokenUsage.Empty.EstimateCost(new TokenPrice(3m, 15m)));
        }

        [Fact]
        public void The_cost_is_estimated_from_input_and_output_per_million() {
            var usage = new TokenUsage(1200, 900, 80);

            // (1200 * 3 + 80 * 15) / 1M: cached input is billed as input.
            Assert.Equal(0.0048m, usage.EstimateCost(new TokenPrice(3m, 15m)));
        }

        [Fact]
        public void A_small_cost_shows_fractions_of_a_cent() {
            Assert.Equal("$0.0048", TokenUsage.DescribeCost(0.0048m));
            Assert.Equal("$0.02",   TokenUsage.DescribeCost(0.0234m));
        }

        [Fact]
        public void Usage_with_a_cost_describes_both() {
            Assert.Equal("1,200 in · 900 cached · 80 out · ~$0.0048",
                         new TokenUsage(1200, 900, 80).DescribeWithCost(0.0048m));
        }

        [Fact]
        public void The_default_budget_is_about_300k() {
            Assert.Equal(300000, TokenBudget.Default);
        }

        [Fact]
        public void The_budget_is_reached_at_the_limit_but_never_without_one() {
            Assert.False(TokenBudget.IsReached(299999, 300000));
            Assert.True(TokenBudget.IsReached(300000, 300000));
            Assert.True(TokenBudget.IsReached(312400, 300000));
            Assert.False(TokenBudget.IsReached(999999999, 0));
            Assert.False(TokenBudget.IsReached(999999999, -1));
        }

        [Fact]
        public void The_budget_prompt_names_the_counts_and_how_to_go_on() {
            string prompt = TokenBudget.PromptFor(312400, 300000);

            Assert.Contains("312,400",    prompt);
            Assert.Contains("300,000",    prompt);
            Assert.Contains("New chat",   prompt);
        }

        [Fact]
        public void Prices_are_remembered_per_endpoint() {
            var prices = new TokenPrices();
            prices.SetPrice("https://api.deepseek.com", new TokenPrice(0.27m, 1.1m));
            prices.SetPrice("http://localhost:11434/v1", new TokenPrice(0m, 0m));

            Assert.Equal(0.27m, prices.GetPrice("https://api.deepseek.com").InputPerMillion);
            Assert.Equal(1.1m,  prices.GetPrice("https://api.deepseek.com").OutputPerMillion);
            Assert.Equal(0m,    prices.GetPrice("http://localhost:11434/v1").InputPerMillion);
            Assert.Null(prices.GetPrice("https://unknown.example/v1"));
        }

        [Fact]
        public void An_emptied_price_forgets_the_endpoint() {
            var prices = new TokenPrices();
            prices.SetPrice("https://api.deepseek.com", new TokenPrice(0.27m, 1.1m));
            prices.SetPrice("https://api.deepseek.com", new TokenPrice(null, null));

            Assert.Null(prices.GetPrice("https://api.deepseek.com"));
        }

        [Fact]
        public void Prices_survive_a_save_and_load_round_trip() {
            var prices = new TokenPrices();
            prices.SetPrice("https://api.deepseek.com", new TokenPrice(0.27m, 1.1m));

            TokenPrices loaded = TokenPrices.FromJson(prices.ToJson());

            Assert.Equal(0.27m, loaded.GetPrice("https://api.deepseek.com").InputPerMillion);
            Assert.Equal(1.1m,  loaded.GetPrice("https://api.deepseek.com").OutputPerMillion);
        }

        [Fact]
        public void Malformed_saved_prices_load_as_empty() {
            Assert.Null(TokenPrices.FromJson("not json at all").GetPrice("https://api.deepseek.com"));
            Assert.Null(TokenPrices.FromJson(string.Empty).GetPrice("https://api.deepseek.com"));
        }
    }
}
