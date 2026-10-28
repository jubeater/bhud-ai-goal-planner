using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
#if DEBUG
    /// <summary>
    /// The reasons the developer channel answers with, as the response file the script reads back
    /// and as the failure the Chat window would show. Each is a fixed word, so a script can tell what
    /// a response meant without parsing anything the Player or the AI wrote.
    /// </summary>
    public static class DevErrors
    {
        /// <summary>A gate was missing: not a Debug build, Blish HUD started without <c>--debug</c>, or the setting off.</summary>
        public const string ChannelOff = "channel_off";

        /// <summary>The request arrived while another one was running, and waited past the channel's budget.</summary>
        public const string Busy = "busy";

        /// <summary>The request file was not a Request this channel accepts.</summary>
        public const string BadRequest = "bad_request";

        /// <summary>The request ran longer than the channel allows.</summary>
        public const string Timeout = "timeout";
    }

    /// <summary>
    /// What one Provider round trip of a Request did: how long it took, how it finished and how many
    /// Lookups it asked for. It is what the debug log keeps, in the shape the developer channel reports.
    /// </summary>
    public sealed class DevRoundTrip
    {
        public DevRoundTrip(long durationMs, string finishReason, int toolCalls) {
            DurationMs   = durationMs;
            FinishReason = finishReason;
            ToolCalls    = toolCalls;
        }

        /// <summary>How long the round trip took, in milliseconds.</summary>
        public long DurationMs { get; }

        /// <summary>How the Provider round trip ended: "stop", "tool_calls", "length", "content_filter" or "other".</summary>
        public string FinishReason { get; }

        /// <summary>How many Lookups the round trip asked for.</summary>
        public int ToolCalls { get; }
    }

    /// <summary>
    /// What one Lookup of a Request did: its name, how long it took, the same detail the debug log
    /// keeps, and how big the answer was.
    /// </summary>
    public sealed class DevLookup
    {
        public DevLookup(string name, long durationMs, string detail, int chars) {
            Name       = name;
            DurationMs = durationMs;
            Detail     = detail;
            Chars      = chars;
        }

        /// <summary>The Lookup's name, as the model asked for it.</summary>
        public string Name { get; }

        /// <summary>How long the Lookup took, in milliseconds.</summary>
        public long DurationMs { get; }

        /// <summary>What the Lookup read, in names and counts; "ok" when there is nothing to say.</summary>
        public string Detail { get; }

        /// <summary>How big the answer was, in characters.</summary>
        public int Chars { get; }
    }

    /// <summary>
    /// What one developer request produced, for the response file the script reads back: the Chat
    /// messages the operation left, the Review still open, whether Undo is available, what the
    /// Lookups and the Provider round trips of the operation did, and — for an Apply — what it did.
    /// </summary>
    public sealed class DevResponse
    {
        public DevResponse(bool ok, string error, IReadOnlyList<ChatMessage> messages, PlanReview review,
                           bool undoAvailable, IReadOnlyList<DevRoundTrip> roundTrips, IReadOnlyList<DevLookup> lookups,
                           AppliedPlan applied) {
            Ok            = ok;
            Error         = error;
            Messages      = messages;
            Review        = review;
            UndoAvailable = undoAvailable;
            RoundTrips    = roundTrips;
            Lookups       = lookups;
            Applied       = applied;
        }

        /// <summary>The answer to a Request the channel rejected before running it.</summary>
        public static DevResponse Failed(string error) {
            return new DevResponse(false, error, new List<ChatMessage>(), null, false,
                                   new List<DevRoundTrip>(), new List<DevLookup>(), null);
        }

        /// <summary>Whether the operation the request asked for ran and answered.</summary>
        public bool Ok { get; }

        /// <summary>The fixed reason the operation did not run, or null when it did.</summary>
        public string Error { get; }

        /// <summary>The Chat messages and Notices the operation left, oldest first.</summary>
        public IReadOnlyList<ChatMessage> Messages { get; }

        /// <summary>The Plan waiting for the Player's Review after the operation, or null when there is none.</summary>
        public PlanReview Review { get; }

        /// <summary>Whether an Apply is waiting to be undone after the operation.</summary>
        public bool UndoAvailable { get; }

        /// <summary>The Provider round trips the operation went through, oldest first.</summary>
        public IReadOnlyList<DevRoundTrip> RoundTrips { get; }

        /// <summary>The Lookups the operation ran, oldest first.</summary>
        public IReadOnlyList<DevLookup> Lookups { get; }

        /// <summary>The Apply the operation carried out, or null when the operation was not an Apply.</summary>
        public AppliedPlan Applied { get; }

        /// <summary>
        /// The response file: everything the operation produced, in the shape a script reads back.
        /// Authors are lowercase words, so a script can tell a Notice or a Report from an AI reply.
        /// </summary>
        public string ToJson() {
            var json = new JObject {
                ["ok"]    = Ok,
                ["error"] = Error == null ? JValue.CreateNull() : new JValue(Error)
            };

            var messages = new JArray();

            foreach (ChatMessage message in Messages) {
                messages.Add(new JObject {
                    ["author"] = AuthorText(message.Author),
                    ["text"]   = message.Text
                });
            }

            json["messages"] = messages;

            JToken reviewJson = Review == null ? (JToken)JValue.CreateNull() : (JToken)ReviewJson(Review);

            json["review"]        = reviewJson;
            json["undoAvailable"] = UndoAvailable;
            json["lookups"]       = LookupsJson();
            json["roundTrips"]    = RoundTripsJson();

            JToken appliedJson = Applied == null
                                     ? (JToken)JValue.CreateNull()
                                     : (JToken)new JObject { ["describe"] = Applied.Describe() };

            json["applied"] = appliedJson;

            return json.ToString();
        }

        private JObject ReviewJson(PlanReview review) {
            var actions = new JArray();

            foreach (ReviewedAction action in review.Actions) {
                actions.Add(new JObject {
                    ["module"]     = action.ModuleName,
                    ["target"]     = action.TargetName,
                    ["current"]    = action.CurrentValue,
                    ["proposed"]   = action.ProposedValue,
                    ["reason"]     = action.Action.Reason,
                    ["sideEffect"] = action.Action.IsSideEffect,
                    ["accepted"]   = action.IsAccepted,
                    ["problem"]    = action.Problem
                });
            }

            return new JObject { ["actions"] = actions };
        }

        private JArray LookupsJson() {
            var lookups = new JArray();

            foreach (DevLookup lookup in Lookups) {
                lookups.Add(new JObject {
                    ["name"]       = lookup.Name,
                    ["durationMs"] = lookup.DurationMs,
                    ["detail"]     = lookup.Detail,
                    ["chars"]      = lookup.Chars
                });
            }

            return lookups;
        }

        private JArray RoundTripsJson() {
            var roundTrips = new JArray();

            foreach (DevRoundTrip roundTrip in RoundTrips) {
                roundTrips.Add(new JObject {
                    ["durationMs"]   = roundTrip.DurationMs,
                    ["finishReason"] = roundTrip.FinishReason,
                    ["toolCalls"]    = roundTrip.ToolCalls
                });
            }

            return roundTrips;
        }

        private static string AuthorText(ChatMessageAuthor author) {
            switch (author) {
                case ChatMessageAuthor.Player: return "player";
                case ChatMessageAuthor.Ai:     return "ai";
                case ChatMessageAuthor.Report: return "report";
                default:                       return "notice";
            }
        }
    }
}
#endif
