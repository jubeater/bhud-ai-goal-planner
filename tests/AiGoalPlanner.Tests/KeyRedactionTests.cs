using jubeater.aigoalplanner.Core;
using Xunit;

namespace jubeater.aigoalplanner.Tests
{
    public class KeyRedactionTests
    {
        [Fact]
        public void A_message_containing_the_key_comes_out_without_it() {
            const string apiKey = "sk-live-secret-1234567890";
            string message = "The Provider returned HTTP 400. Invalid key " + apiKey + " provided.";

            string redacted = KeyRedaction.Redact(message, apiKey);

            Assert.DoesNotContain(apiKey, redacted);
            Assert.Contains("[redacted]", redacted);
            Assert.Equal("The Provider returned HTTP 400. Invalid key [redacted] provided.", redacted);
        }

        [Fact]
        public void A_message_without_the_key_comes_out_unchanged() {
            const string apiKey = "sk-live-secret-1234567890";
            string message = "The Provider returned HTTP 400. Model does not support tool calling.";

            string redacted = KeyRedaction.Redact(message, apiKey);

            Assert.Equal(message, redacted);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void An_empty_or_whitespace_key_leaves_message_unchanged(string emptyKey) {
            string message = "Some error message with no redaction needed.";

            string redacted = KeyRedaction.Redact(message, emptyKey);

            Assert.Equal(message, redacted);
        }

        [Fact]
        public void Every_occurrence_of_the_key_is_replaced() {
            const string apiKey = "sk-secret-123";
            string message = "Key " + apiKey + " failed; retry with " + apiKey + ".";

            string redacted = KeyRedaction.Redact(message, apiKey);

            Assert.DoesNotContain(apiKey, redacted);
            Assert.Equal("Key [redacted] failed; retry with [redacted].", redacted);
        }

        [Fact]
        public void Null_or_empty_text_returns_original_value() {
            Assert.Null(KeyRedaction.Redact(null, "sk-secret"));
            Assert.Equal(string.Empty, KeyRedaction.Redact(string.Empty, "sk-secret"));
        }
    }
}
