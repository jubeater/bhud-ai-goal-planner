using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;
using jubeater.aigoalplanner.Core;

namespace jubeater.aigoalplanner.Tests
{
#if DEBUG
    /// <summary>
    /// Tests the developer channel's response files: everything one operation produced, in the shape
    /// a script reads back. Durations are canned here; the channel tests pin them against the real Chat.
    /// </summary>
    public class DevResponseTests
    {
        [Fact]
        public void Messages_keep_their_author_and_text() {
            var response = new DevResponse(
                true, null,
                new List<ChatMessage> {
                    new ChatMessage(ChatMessageAuthor.Player, "Where am I?"),
                    new ChatMessage(ChatMessageAuthor.Ai, "In the Domain of Vabbi."),
                    new ChatMessage(ChatMessageAuthor.Notice, "The Provider was slow."),
                    new ChatMessage(ChatMessageAuthor.Report, "Applied 1 change: Mounts: set \"Show markers\" to false."),
                },
                review: null, undoAvailable: false,
                roundTrips: new List<DevRoundTrip>(), lookups: new List<DevLookup>(), applied: null);

            JObject json = JObject.Parse(response.ToJson());

            Assert.True((bool)json["ok"]);
            Assert.Equal(JTokenType.Null, json["error"].Type);
            Assert.Equal(new[] { "player", "ai", "notice", "report" },
                         json["messages"].Select(message => (string)message["author"]));
            Assert.Equal("In the Domain of Vabbi.", (string)json["messages"][1]["text"]);
        }

        [Fact]
        public void The_open_Review_lists_each_Action_with_what_the_Player_decides_on() {
            PlanReview review = new PlanReview(
                new Plan("Here is what I suggest.", new List<PlanAction>()),
                new List<ReviewedAction> {
                    new ReviewedAction(
                        new PlanAction(PlanActionKind.SetSetting, "set_setting", "Mounts", "Show markers", "false",
                                       "You asked for the markers to go.", isSideEffect: false),
                        "bh.mounts", "Mounts", "Show markers", "Show markers", "true", "false", problem: string.Empty),
                    new ReviewedAction(
                        new PlanAction(PlanActionKind.DisableModule, "disable_module", "Pathing", null, null,
                                       "It overlaps with Mounts.", isSideEffect: true),
                        "bh.pathing", "Pathing", string.Empty, string.Empty, "enabled", "disabled", problem: string.Empty),
                    new ReviewedAction(
                        new PlanAction(PlanActionKind.SetSetting, "set_setting", "Nowhere", "Nope", "x",
                                       "It would help.", isSideEffect: false),
                        string.Empty, "Nowhere", string.Empty, string.Empty, string.Empty, string.Empty,
                        problem: "No installed module is called \"Nowhere\"."),
                });

            var response = new DevResponse(
                true, null, new List<ChatMessage>(), review, undoAvailable: false,
                roundTrips: new List<DevRoundTrip>(), lookups: new List<DevLookup>(), applied: null);

            JArray actions = (JArray)JObject.Parse(response.ToJson())["review"]["actions"];

            Assert.Equal(3, actions.Count);

            Assert.Equal("true", (string)actions[0]["current"]);
            Assert.Equal("false", (string)actions[0]["proposed"]);
            Assert.Equal("You asked for the markers to go.", (string)actions[0]["reason"]);
            Assert.False((bool)actions[0]["sideEffect"]);
            Assert.True((bool)actions[0]["accepted"]);

            Assert.True((bool)actions[1]["sideEffect"]);
            Assert.True((bool)actions[1]["accepted"]);

            Assert.False((bool)actions[2]["accepted"]);
            Assert.Contains("Nowhere", (string)actions[2]["problem"]);
        }

        [Fact]
        public void Without_an_open_Review_the_response_says_so() {
            var response = new DevResponse(
                true, null, new List<ChatMessage>(), review: null, undoAvailable: true,
                roundTrips: new List<DevRoundTrip>(), lookups: new List<DevLookup>(), applied: null);

            JObject json = JObject.Parse(response.ToJson());

            Assert.Equal(JTokenType.Null, json["review"].Type);
            Assert.True((bool)json["undoAvailable"]);
        }

        [Fact]
        public void Lookups_and_round_trips_keep_what_the_debug_log_keeps() {
            var response = new DevResponse(
                true, null, new List<ChatMessage>(), review: null, undoAvailable: false,
                roundTrips: new List<DevRoundTrip> {
                    new DevRoundTrip(120, "tool_calls", 1),
                    new DevRoundTrip(340, "stop", 0),
                },
                lookups: new List<DevLookup> {
                    new DevLookup("get_location", 12, "character=Test Character, map=Domain of Vabbi", 96),
                },
                applied: null);

            JObject json = JObject.Parse(response.ToJson());

            Assert.Equal("tool_calls", (string)json["roundTrips"][0]["finishReason"]);
            Assert.Equal(1, (int)json["roundTrips"][0]["toolCalls"]);
            Assert.Equal("get_location", (string)json["lookups"][0]["name"]);
            Assert.Equal("character=Test Character, map=Domain of Vabbi", (string)json["lookups"][0]["detail"]);
            Assert.Equal(96, (int)json["lookups"][0]["chars"]);
        }

        [Fact]
        public void An_apply_reports_what_it_did_in_the_modules_own_words() {
            var action = new ReviewedAction(
                new PlanAction(PlanActionKind.SetSetting, "set_setting", "Mounts", "Show markers", "false",
                               "You asked for the markers to go.", isSideEffect: false),
                "bh.mounts", "Mounts", "Show markers", "Show markers", "true", "false", problem: string.Empty);

            var applied = new AppliedPlan(
                new List<ApplyOutcome> { new ApplyOutcome(action, succeeded: true) },
                new List<UndoStep>());

            var response = new DevResponse(
                true, null, new List<ChatMessage>(), review: null, undoAvailable: true,
                roundTrips: new List<DevRoundTrip>(), lookups: new List<DevLookup>(), applied: applied);

            JObject json = JObject.Parse(response.ToJson());

            Assert.Contains("Show markers", (string)json["applied"]["describe"]);
            Assert.True((bool)json["undoAvailable"]);
        }

        [Fact]
        public void A_rejection_is_an_error_with_nothing_collected() {
            DevResponse response = DevResponse.Failed(DevErrors.BadRequest);

            JObject json = JObject.Parse(response.ToJson());

            Assert.False((bool)json["ok"]);
            Assert.Equal(DevErrors.BadRequest, (string)json["error"]);
            Assert.Empty(json["messages"]);
            Assert.Empty(json["lookups"]);
            Assert.Empty(json["roundTrips"]);
            Assert.False((bool)json["undoAvailable"]);
        }
    }
#endif
}
