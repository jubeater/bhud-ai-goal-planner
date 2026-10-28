using Xunit;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests
{
#if DEBUG
    /// <summary>
    /// Tests the developer channel's request parsing: one operation per file, exactly the shapes
    /// the ticket allows, so anything else is rejected before it can reach the Chat.
    /// </summary>
    public class DevRequestTests
    {
        [Theory]
        [InlineData("{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"Where can I still get mastery points?\" }", DevRequestOp.Send)]
        [InlineData("{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": [0, 2] }", DevRequestOp.Apply)]
        [InlineData("{ \"id\": \"a3\", \"op\": \"cancel_review\" }", DevRequestOp.CancelReview)]
        [InlineData("{ \"id\": \"a4\", \"op\": \"undo\" }", DevRequestOp.Undo)]
        [InlineData("{ \"id\": \"a5\", \"op\": \"new_chat\" }", DevRequestOp.NewChat)]
        [InlineData("{ \"id\": \"a6\", \"op\": \"state\" }", DevRequestOp.State)]
        public void Each_ticket_example_parses(string json, DevRequestOp op) {
            Assert.True(DevRequest.TryParse(json, out DevRequest request, out string error), "Should parse: " + error);

            Assert.Equal(op, request.Op);
            Assert.False(string.IsNullOrWhiteSpace(request.Id));
        }

        [Fact]
        public void A_send_keeps_its_text() {
            Assert.True(DevRequest.TryParse("{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"Where am I?\" }",
                                            out DevRequest request, out string error), "Should parse: " + error);

            Assert.Equal("a1", request.Id);
            Assert.Equal("Where am I?", request.Text);
        }

        [Fact]
        public void An_apply_keeps_its_actions_in_order() {
            Assert.True(DevRequest.TryParse("{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": [2, 0] }",
                                            out DevRequest request, out string error), "Should parse: " + error);

            Assert.Equal(new[] { 2, 0 }, request.Actions);
        }

        [Theory]
        [InlineData("{ \"id\": \"a1\", \"op\": \"launch\" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"SEND\" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"\" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": 3 }")]
        public void An_unknown_operation_is_rejected(string json) {
            Assert.False(DevRequest.TryParse(json, out DevRequest request, out string error));

            Assert.Null(request);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"Hi\", \"actions\": [0] }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"apply\", \"actions\": [0], \"text\": \"Hi\" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"state\", \"text\": \"Hi\" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"undo\", \"extra\": true }")]
        public void Unknown_fields_are_rejected(string json) {
            Assert.False(DevRequest.TryParse(json, out DevRequest request, out string error));

            Assert.Null(request);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("{ \"op\": \"state\" }")]
        [InlineData("{ \"id\": \"\", \"op\": \"state\" }")]
        [InlineData("{ \"id\": \"  \", \"op\": \"state\" }")]
        [InlineData("{ \"id\": \"a1\" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": null }")]
        public void A_missing_id_or_op_is_rejected(string json) {
            Assert.False(DevRequest.TryParse(json, out DevRequest request, out string error));

            Assert.Null(request);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("{ \"id\": \"a1\", \"op\": \"send\" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"\" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"send\", \"text\": \"   \" }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"send\", \"text\": null }")]
        [InlineData("{ \"id\": \"a1\", \"op\": \"send\", \"text\": 42 }")]
        public void A_send_without_words_is_rejected(string json) {
            Assert.False(DevRequest.TryParse(json, out DevRequest request, out string error));

            Assert.Null(request);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("{ \"id\": \"a2\", \"op\": \"apply\" }")]
        [InlineData("{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": null }")]
        [InlineData("{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": \"0\" }")]
        [InlineData("{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": [] }")]
        [InlineData("{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": [0, \"1\"] }")]
        [InlineData("{ \"id\": \"a2\", \"op\": \"apply\", \"actions\": [0.5] }")]
        public void An_apply_without_integer_actions_is_rejected(string json) {
            Assert.False(DevRequest.TryParse(json, out DevRequest request, out string error));

            Assert.Null(request);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("{ \"id\": \"../a1\", \"op\": \"state\" }")]
        [InlineData("{ \"id\": \"..\", \"op\": \"state\" }")]
        [InlineData("{ \"id\": \"sub/a1\", \"op\": \"state\" }")]
        [InlineData("{ \"id\": \"sub\\\\a1\", \"op\": \"state\" }")]
        public void An_id_that_escapes_the_outbox_is_rejected(string json) {
            Assert.False(DevRequest.TryParse(json, out DevRequest request, out string error));

            Assert.Null(request);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("{ not json")]
        [InlineData("[\"state\"]")]
        public void A_malformed_file_is_rejected(string json) {
            Assert.False(DevRequest.TryParse(json, out DevRequest request, out string error));

            Assert.Null(request);
            Assert.False(string.IsNullOrEmpty(error));
        }

        [Theory]
        [InlineData("a1", true)]
        [InlineData("A-1_2.3", true)]
        [InlineData("../a1", false)]
        [InlineData("a/b", false)]
        [InlineData("a b", false)]
        [InlineData("..", false)]
        [InlineData(".", false)]
        [InlineData("", false)]
        public void Only_a_name_that_cannot_escape_the_outbox_is_an_id(string name, bool valid) {
            Assert.Equal(valid, DevRequest.IsValidId(name));
        }
    }
#endif
}
