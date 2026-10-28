using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
#if DEBUG
    /// <summary>
    /// The operation the developer channel is asked to run, as a request file names it. One operation
    /// per file, like the Chat window's buttons.
    /// </summary>
    public enum DevRequestOp
    {
        /// <summary>The Player's words to send to the AI.</summary>
        Send,

        /// <summary>The Actions of the open Review the Player chose to apply.</summary>
        Apply,

        /// <summary>The open Review the Player dismissed.</summary>
        CancelReview,

        /// <summary>The last Apply to put back.</summary>
        Undo,

        /// <summary>The Chat to start over from.</summary>
        NewChat,

        /// <summary>Ask what the state is; change nothing.</summary>
        State
    }

    /// <summary>
    /// A developer request: one operation the channel runs against the Chat, and the parts of it the
    /// operation needs. It is read from a request file, and anything this channel does not accept is a
    /// <see cref="DevErrors.BadRequest"/>.
    /// </summary>
    public sealed class DevRequest
    {
        public DevRequest(string id, DevRequestOp op, string text = null, IReadOnlyList<int> actions = null) {
            Id      = id;
            Op      = op;
            Text    = text;
            Actions = actions;
        }

        /// <summary>The request's own name, as the response file is named after it.</summary>
        public string Id { get; }

        /// <summary>The operation the Player's or the script's check asked for.</summary>
        public DevRequestOp Op { get; }

        /// <summary>What to send; the AI's answer to it, or a Notice when there is none.</summary>
        public string Text { get; }

        /// <summary>The Actions of the open Review to apply, in the order the Player chose.</summary>
        public IReadOnlyList<int> Actions { get; }

        /// <summary>
        /// Reads one request file. The shape is the one the ticket allows — <c>id</c> and <c>op</c>,
        /// plus <c>text</c> for a Send and <c>actions</c> for an Apply — so a file with any other field,
        /// a missing field or an unknown operation is not a Request this channel accepts. The id names
        /// the response file, so it may only hold characters that cannot escape the outbox folder.
        /// </summary>
        public static bool TryParse(string json, out DevRequest request, out string error) {            request = null;
            error   = null;

            if (string.IsNullOrWhiteSpace(json)) {
                error = "empty";
                return false;
            }

            JObject obj;

            try {
                obj = JObject.Parse(json);
            } catch {
                error = "invalid_json";
                return false;
            }

            if (obj == null) {
                error = "invalid_json";
                return false;
            }

            if (!TryReadId(obj, out string id, out error)) return false;
            if (!TryReadOp(obj, out DevRequestOp op, out error)) return false;

            switch (op) {
                case DevRequestOp.Send:
                    if (!HasOnly(obj, "id", "op", "text")) {
                        error = "unknown_fields";
                        return false;
                    }

                    if (!TryReadText(obj, out string text, out error)) return false;

                    request = new DevRequest(id, op, text);
                    return true;

                case DevRequestOp.Apply:
                    if (!HasOnly(obj, "id", "op", "actions")) {
                        error = "unknown_fields";
                        return false;
                    }

                    if (!TryReadActions(obj, out IReadOnlyList<int> actions, out error)) return false;

                    request = new DevRequest(id, op, actions: actions);
                    return true;

                default:
                    if (!HasOnly(obj, "id", "op")) {
                        error = "unknown_fields";
                        return false;
                    }

                    request = new DevRequest(id, op);
                    return true;
            }
        }

        /// <summary>
        /// Whether <paramref name="name"/> can name a request and its response file: letters, digits,
        /// dots, dashes and underscores, and never just dots, so it cannot escape the outbox folder.
        /// </summary>
        public static bool IsValidId(string name) {
            if (string.IsNullOrWhiteSpace(name)) return false;

            foreach (char letter in name) {
                if (char.IsLetterOrDigit(letter) || letter == '.' || letter == '-' || letter == '_') continue;

                return false;
            }

            return name.Trim('.').Length > 0;
        }

        private static bool TryReadId(JObject obj, out string id, out string error) {
            id    = null;
            error = null;

            JToken token = obj["id"];

            if (token == null || token.Type != JTokenType.String || !IsValidId((string)token)) {
                error = "missing_id";
                return false;
            }

            id = (string)token;
            return true;
        }

        private static bool TryReadOp(JObject obj, out DevRequestOp op, out string error) {
            op    = default(DevRequestOp);
            error = null;

            JToken token = obj["op"];

            if (token == null || token.Type != JTokenType.String) {
                error = "missing_op";
                return false;
            }

            switch ((string)token) {
                case "send":          op = DevRequestOp.Send;         return true;
                case "apply":         op = DevRequestOp.Apply;        return true;
                case "cancel_review": op = DevRequestOp.CancelReview; return true;
                case "undo":          op = DevRequestOp.Undo;         return true;
                case "new_chat":      op = DevRequestOp.NewChat;      return true;
                case "state":         op = DevRequestOp.State;        return true;
            }

            error = "unknown_op";
            return false;
        }

        private static bool TryReadText(JObject obj, out string text, out string error) {
            text  = null;
            error = null;

            JToken token = obj["text"];

            if (token == null || token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token)) {
                error = "missing_text";
                return false;
            }

            text = (string)token;
            return true;
        }

        private static bool TryReadActions(JObject obj, out IReadOnlyList<int> actions, out string error) {
            actions = null;
            error   = null;

            JToken token = obj["actions"];

            if (token == null || token.Type != JTokenType.Array) {
                error = "missing_actions";
                return false;
            }

            var picked = new List<int>();

            foreach (JToken member in (IEnumerable<JToken>)token) {
                if (member.Type != JTokenType.Integer) {
                    error = "bad_actions";
                    return false;
                }

                long index;

                try {
                    index = (long)member;
                } catch {
                    error = "bad_actions";
                    return false;
                }

                if (index < int.MinValue || index > int.MaxValue) {
                    error = "bad_actions";
                    return false;
                }

                picked.Add((int)index);
            }

            if (picked.Count == 0) {
                error = "missing_actions";
                return false;
            }

            actions = picked;
            return true;
        }

        private static bool HasOnly(JObject obj, params string[] names) {
            if (obj == null || obj.Count != names.Length) return false;

            foreach (string name in names) {
                if (obj[name] == null) return false;
            }

            return true;
        }
    }
#endif
}
