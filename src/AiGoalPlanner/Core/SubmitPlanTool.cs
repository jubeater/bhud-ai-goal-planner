using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The <c>submit_plan</c> proposal: the one tool that delivers a Plan (ADR-0003). The agent loop
    /// never runs it — a proposal goes to Review instead — and this class turns what the model put
    /// in its arguments into a <see cref="Plan"/>.
    /// </summary>
    /// <remarks>
    /// A model calls tools it can see, not a schema it must obey to the letter, so reading the
    /// arguments is forgiving: a kind this module does not know becomes
    /// <see cref="PlanActionKind.Unknown"/> (and the Review explains it), a field a kind does not
    /// use is dropped, and a value sent as a JSON boolean or number is read as its text. Nothing
    /// here decides whether an Action is allowed — that is <see cref="PlanValidator"/>'s job.
    /// </remarks>
    public static class SubmitPlanTool
    {
        public const string Name = "submit_plan";

        private const string Description =
            "Proposes a plan for the Player to review and apply: your reply, and the actions that carry "
          + "it out. Nothing is changed by calling this. Use enable_module or disable_module with the "
          + "module's namespace from list_installed_modules, set_setting with the module's namespace, "
          + "the setting key from get_module_settings and the new value as text, and advice for what "
          + "you cannot do yourself. Give each action a reason, and set is_side_effect to true when it "
          + "changes something the Player did not ask about.";

        private const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{"
          + "\"reply\":{\"type\":\"string\",\"description\":\"What you say to the Player about this plan.\"},"
          + "\"actions\":{\"type\":\"array\",\"description\":\"The changes, in the order they should be applied.\","
          + "\"items\":{\"type\":\"object\",\"properties\":{"
          + "\"kind\":{\"type\":\"string\",\"enum\":[\"enable_module\",\"disable_module\",\"set_setting\",\"advice\"],"
          + "\"description\":\"What this action does.\"},"
          + "\"module\":{\"type\":\"string\",\"description\":\"The module's namespace, for the module and setting kinds.\"},"
          + "\"key\":{\"type\":\"string\",\"description\":\"The setting key, for set_setting.\"},"
          + "\"value\":{\"type\":\"string\",\"description\":\"The setting's new value as text, for set_setting.\"},"
          + "\"reason\":{\"type\":\"string\",\"description\":\"Why you propose this, for the Player to read.\"},"
          + "\"is_side_effect\":{\"type\":\"boolean\",\"description\":\"True when the Player did not ask for this change.\"}"
          + "},\"required\":[\"kind\",\"reason\"]}}"
          + "},\"required\":[\"reply\",\"actions\"]}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        /// <summary>
        /// Builds the declaration the model is offered. It is a proposal, so the Chat delivers the
        /// Plan rather than running the call; a call that reaches it another way is refused instead
        /// of quietly changing something.
        /// </summary>
        public static AIFunction Create() {
            return new Function();
        }

        public static string Progress(IDictionary<string, object> arguments) {
            return "Preparing a plan…";
        }

        /// <summary>
        /// The Plan the model put in <paramref name="arguments"/>. Junk comes out as an empty Plan
        /// rather than an exception: the Chat answers that with a Notice and the Player loses
        /// nothing.
        /// </summary>
        public static Plan ReadPlan(IDictionary<string, object> arguments) {
            if (arguments == null) return new Plan(string.Empty, null);

            string reply = TextOf(Value(arguments, "reply"));

            return new Plan(reply, ReadActions(Value(arguments, "actions")));
        }

        private static IReadOnlyList<PlanAction> ReadActions(object value) {
            var actions = new List<PlanAction>();

            if (!(value is IEnumerable items) || value is string) return actions;

            foreach (object item in items) {
                if (item is IDictionary<string, object> action) actions.Add(ReadAction(action));
            }

            return actions;
        }

        private static PlanAction ReadAction(IDictionary<string, object> action) {
            string kindText = TextOf(Value(action, "kind")).Trim();

            PlanActionKind kind = KindOf(kindText);

            string reason       = TextOf(Value(action, "reason")).Trim();
            bool   isSideEffect = FlagOf(Value(action, "is_side_effect"));

            switch (kind) {
                case PlanActionKind.EnableModule:
                case PlanActionKind.DisableModule:
                    // Only the Module is read: a stray key or value must never be applied as a
                    // Setting the model did not mean to touch.
                    return new PlanAction(kind, kindText, TextOf(Value(action, "module")).Trim(), null, null, reason, isSideEffect);

                case PlanActionKind.SetSetting:
                    return new PlanAction(kind, kindText,
                                          TextOf(Value(action, "module")).Trim(),
                                          TextOf(Value(action, "key")).Trim(),
                                          TextOf(Value(action, "value")).Trim(),
                                          reason, isSideEffect);

                case PlanActionKind.Advice:
                    return new PlanAction(kind, kindText, null, null, null, reason, isSideEffect);

                default:
                    return new PlanAction(PlanActionKind.Unknown, kindText, TextOf(Value(action, "module")).Trim(),
                                          null, null, reason, isSideEffect);
            }
        }

        private static PlanActionKind KindOf(string kindText) {
            switch ((kindText ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_')) {
                case "enable_module":  return PlanActionKind.EnableModule;
                case "disable_module": return PlanActionKind.DisableModule;
                case "set_setting":    return PlanActionKind.SetSetting;
                case "advice":         return PlanActionKind.Advice;
                default:               return PlanActionKind.Unknown;
            }
        }

        private static object Value(IDictionary<string, object> arguments, string key) {
            return arguments.TryGetValue(key, out object value) ? value : null;
        }

        /// <summary>
        /// A value as text, whether the model sent a string or a JSON boolean or number. Text is
        /// what a Setting is compared and applied as, so a <c>value: false</c> and a
        /// <c>value: "false"</c> are the same change.
        /// </summary>
        private static string TextOf(object value) {
            switch (value) {
                case null:          return string.Empty;
                case string text:   return text;
                case bool flag:     return flag ? "true" : "false";
                case float number:  return number.ToString(CultureInfo.InvariantCulture);
                case double number: return number.ToString(CultureInfo.InvariantCulture);
                default:            return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static bool FlagOf(object value) {
            switch (value) {
                case bool flag:   return flag;
                case string text: return bool.TryParse(text.Trim(), out bool parsed) && parsed;
                default:          return false;
            }
        }

        /// <summary>
        /// The declaration itself. It carries a schema for the model to read and refuses to run:
        /// only the Chat delivers a Plan, through validation and Review.
        /// </summary>
        private sealed class Function : AIFunction
        {
            public override string Name => SubmitPlanTool.Name;

            public override string Description => SubmitPlanTool.Description;

            public override JsonElement JsonSchema => Schema.RootElement;

            protected override ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                throw new InvalidOperationException("A plan is delivered to the Player for Review, not run as a tool.");
            }
        }
    }
}

