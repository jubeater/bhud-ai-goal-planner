using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The tokens one Provider round trip, one reply or one whole Chat used: input, cached input
    /// and output, from the Provider's usage data. A Provider that reports no usage leaves an
    /// empty usage; one that reports no caching leaves the cached count empty, and the Chat shows
    /// no cached split. Cached tokens are part of the input, never added on top.
    /// </summary>
    public sealed class TokenUsage
    {
        public static readonly TokenUsage Empty = new TokenUsage(null, null, null);

        public TokenUsage(long? inputTokens, long? cachedInputTokens, long? outputTokens) {
            InputTokens       = inputTokens;
            CachedInputTokens = cachedInputTokens;
            OutputTokens      = outputTokens;
        }

        public long? InputTokens { get; }

        public long? CachedInputTokens { get; }

        public long? OutputTokens { get; }

        public bool IsEmpty => !InputTokens.HasValue && !CachedInputTokens.HasValue && !OutputTokens.HasValue;

        /// <summary>
        /// Input plus output, in tokens. Cached tokens are part of the input, so they are not
        /// counted again.
        /// </summary>
        public long Total => (InputTokens ?? 0) + (OutputTokens ?? 0);

        /// <summary>
        /// Reads the Provider's usage block the way the client keeps it: no block, or a block with
        /// no counts the Player could read, is no usage rather than zeros.
        /// </summary>
        public static TokenUsage FromResponse(UsageDetails usage) {
            if (usage == null) return null;

            if (!usage.InputTokenCount.HasValue && !usage.CachedInputTokenCount.HasValue && !usage.OutputTokenCount.HasValue) {
                return null;
            }

            return new TokenUsage(usage.InputTokenCount, usage.CachedInputTokenCount, usage.OutputTokenCount);
        }

        public TokenUsage Add(TokenUsage other) {
            if (other == null) throw new ArgumentNullException(nameof(other));

            return new TokenUsage(AddCounts(InputTokens, other.InputTokens),
                                  AddCounts(CachedInputTokens, other.CachedInputTokens),
                                  AddCounts(OutputTokens, other.OutputTokens));
        }

        private static long? AddCounts(long? first, long? second) {
            if (!first.HasValue) return second;
            if (!second.HasValue) return first;

            return first.Value + second.Value;
        }

        /// <summary>
        /// What the Player reads: exact counts, with the cached split only when the Provider
        /// reported it. Empty usage describes as nothing, so the Chat shows nothing.
        /// </summary>
        public string Describe() {
            var parts = new List<string>();

            if (InputTokens.HasValue) parts.Add(Format(InputTokens.Value) + " in");
            if (CachedInputTokens.HasValue) parts.Add(Format(CachedInputTokens.Value) + " cached");
            if (OutputTokens.HasValue) parts.Add(Format(OutputTokens.Value) + " out");

            return string.Join(" · ", parts);
        }

        /// <summary>
        /// The usage with its estimated cost, or the usage alone when the Player entered no
        /// prices to estimate it from.
        /// </summary>
        public string DescribeWithCost(decimal? costUsd) {
            string text = Describe();

            if (costUsd.HasValue && text.Length > 0) text += " · ~" + DescribeCost(costUsd.Value);

            return text;
        }

        /// <summary>
        /// What this usage costs at <paramref name="prices"/>, or null when there is nothing to
        /// estimate from: no prices, only half of them, or no usage at all. Both prices are
        /// needed, because a guess from half the bill is worse than no estimate.
        /// </summary>
        public decimal? EstimateCost(TokenPrice prices) {
            if (prices == null || !prices.InputPerMillion.HasValue || !prices.OutputPerMillion.HasValue) return null;
            if (IsEmpty) return null;

            return ((InputTokens ?? 0) * prices.InputPerMillion.Value + (OutputTokens ?? 0) * prices.OutputPerMillion.Value) / 1000000m;
        }

        /// <summary>
        /// A cost the Player can read at a glance: cents with two decimals, fractions of a cent
        /// with four, so a small Chat does not round down to nothing.
        /// </summary>
        public static string DescribeCost(decimal costUsd) {
            return costUsd < 0.01m ? "$" + costUsd.ToString("0.0000", CultureInfo.InvariantCulture)
                                   : "$" + costUsd.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Format(long count) {
            return count.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
