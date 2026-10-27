using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace jubeater.aigoalplanner.Core
{
    /// <summary>
    /// The <c>get_module_settings</c> Lookup: one Module's Settings, with their labels, values and
    /// ranges, nested ones included, under their qualified keys. What may reach the model is decided
    /// by <see cref="SettingWalk"/>: Settings that look like secrets are removed there, and the
    /// Settings of AI Goal Planner itself are never shared.
    /// </summary>
    public static class GetModuleSettingsTool
    {
        public const string Name = "get_module_settings";

        /// <summary>
        /// How many Settings one answer may carry. A module with more (Pathing, for one) is cut: the
        /// first ones are answered with a note that the list was cut, so a single large module
        /// cannot flood the Request and crowd out what the Player actually asked.
        /// </summary>
        public const int MaxSettings = 200;

        /// <summary>
        /// This module's own namespace. Its Settings configure the Chat and the Provider; they are
        /// no business of the model that runs inside them.
        /// </summary>
        public const string ThisModuleNamespace = "jubeater.aigoalplanner";

        /// <summary>
        /// The one argument the tool takes. The schema offers it under this name, and the function
        /// reads it back out of the call's arguments, so both share the spelling here.
        /// </summary>
        private const string NamespaceArgument = "namespace";

        private const string Description =
            "Reads the settings of one installed Blish HUD module: key, label, type, current value and "
          + "the allowed range. Pass the module's namespace from list_installed_modules. A disabled "
          + "module answers that it has to be enabled first; the settings of AI Goal Planner itself "
          + "are never shared.";

        private const string SchemaJson =
            "{\"type\":\"object\",\"properties\":{\"namespace\":{\"type\":\"string\","
          + "\"description\":\"The namespace of the Module, from list_installed_modules.\"}},"
          + "\"required\":[\"namespace\"]}";

        private static readonly JsonDocument Schema = JsonDocument.Parse(SchemaJson);

        /// <summary>
        /// Builds the tool against <paramref name="world"/>, the host that answers the Lookup. It is
        /// a hand-written <see cref="AIFunction"/> with a literal schema, per ADR-0008: the
        /// serializer-driven factory needs a System.Text.Json newer than the one Blish HUD runs.
        /// <paramref name="logDebug"/> receives one line per call, with counts and no Setting values.
        /// </summary>
        public static AIFunction Create(IBlishHudWorld world, Action<string> logDebug = null) {
            return new Function(world, logDebug);
        }

        /// <summary>
        /// The progress line the Chat shows while this Lookup runs, naming the Module when the call
        /// already says which one it wants.
        /// </summary>
        public static string Progress(IDictionary<string, object> arguments) {
            if (arguments != null && arguments.TryGetValue(NamespaceArgument, out object value)
                                  && value is string moduleNamespace
                                  && moduleNamespace.Length > 0) {
                return "Reading the settings of " + moduleNamespace + "…";
            }

            return "Reading module settings…";
        }

        private static async Task<LookupAnswer> RunAsync(IBlishHudWorld world, string @namespace, CancellationToken cancellationToken) {
            string wanted = (@namespace ?? string.Empty).Trim();

            if (string.Equals(wanted, ThisModuleNamespace, StringComparison.OrdinalIgnoreCase)) {
                JObject refusal = Answer(ModuleSettingsStatus.ThisModule, "AI Goal Planner",
                                         Array.Empty<ModuleSettingInfo>(), totalSettings: 0);

                return new LookupAnswer(refusal.ToString(Formatting.None),
                                        Detail(ModuleSettingsStatus.ThisModule, ThisModuleNamespace, shown: 0, total: 0));
            }

            ModuleSettingsResult result = await world.GetModuleSettingsAsync(@namespace, cancellationToken)
                                                     .ConfigureAwait(false);

            IReadOnlyList<ModuleSettingInfo> settings = result.Status == ModuleSettingsStatus.Ok
                                                            ? SettingWalk.Flatten(result.Settings)
                                                            : Array.Empty<ModuleSettingInfo>();

            int total = settings.Count;

            if (total > MaxSettings) settings = settings.Take(MaxSettings).ToList();

            string answer = Answer(result.Status, result.ModuleName, settings, total).ToString(Formatting.None);

            return new LookupAnswer(answer, Detail(result.Status, result.Namespace, settings.Count, total));
        }

        /// <summary>
        /// What the debug log may say about this Lookup: which Module matched, what came of it, and
        /// how many Settings the answer carries out of how many the module holds. Never a Setting's
        /// value.
        /// </summary>
        /// <remarks>
        /// The Module is named by the namespace the world matched, never by the one the model asked
        /// with: that argument is the model's own text, and the model can put anything in it — a
        /// Setting's value it has just read, or the Player's words. A Lookup that matched nothing
        /// names no Module at all.
        /// </remarks>
        private static string Detail(ModuleSettingsStatus status, string moduleNamespace, int shown, int total) {
            string module = string.IsNullOrEmpty(moduleNamespace) ? "<unknown>" : moduleNamespace;

            string counts = status != ModuleSettingsStatus.Ok
                                ? "settings=0"
                                : shown < total
                                    ? "settings=" + shown + " of " + total
                                    : "settings=" + shown;

            return "module=" + module + ", status=" + StatusText(status) + ", " + counts;
        }

        /// <summary>
        /// The status as both the answer and the log spell it, so the two cannot drift apart.
        /// </summary>
        private static string StatusText(ModuleSettingsStatus status) {
            switch (status) {
                case ModuleSettingsStatus.Ok:                 return "ok";
                case ModuleSettingsStatus.ModuleNotInstalled: return "not_installed";
                case ModuleSettingsStatus.ModuleDisabled:     return "disabled";
                default:                                      return "private";
            }
        }

        /// <summary>
        /// The tool's answer in one shape: what was found, or the status that says why there is
        /// nothing to read. <paramref name="totalSettings"/> is how many Settings the module has
        /// before the cap, so an answer that was cut can say so.
        /// </summary>
        private static JObject Answer(ModuleSettingsStatus status, string moduleName,
                                      IReadOnlyList<ModuleSettingInfo> settings, int totalSettings) {
            var json = new JObject {
                ["module"] = moduleName,
                ["status"] = StatusText(status)
            };

            switch (status) {
                case ModuleSettingsStatus.Ok:
                    json["settings"] = new JArray(settings.Select(ToJson));

                    // The model is told the list was cut, so it can say which Settings it did not
                    // see instead of quietly treating the first ones as the whole module.
                    if (totalSettings > settings.Count) {
                        json["truncated"]      = true;
                        json["settings_total"] = totalSettings;
                        json["note"] = "This module has " + totalSettings + " settings; only the first " + settings.Count
                                     + " are shown. The list was cut to keep the answer small.";
                    }

                    break;

                case ModuleSettingsStatus.ModuleNotInstalled:
                    json["instruction"] =
                        "No installed module has that namespace. Call " + ListInstalledModulesTool.Name +
                        " to see what the Player has installed.";
                    break;

                case ModuleSettingsStatus.ModuleDisabled:
                    json["instruction"] =
                        "This module is disabled. The Player has to enable it in Blish HUD before its "
                      + "settings can be read; suggest enabling it if the request needs it.";
                    break;

                default:
                    json["instruction"] =
                        "These are the settings of AI Goal Planner itself. They are never shared with "
                      + "the model, and they are not part of what can be configured for the Player.";
                    break;
            }

            return json;
        }

        private static JObject ToJson(ModuleSettingInfo setting) {
            var json = new JObject {
                ["key"]         = setting.Key,
                ["label"]       = setting.DisplayName,
                ["description"] = setting.Description,
                ["type"]        = setting.TypeName,
                ["value"]       = setting.Value
            };

            if (setting.Min != null) json["min"] = setting.Min;
            if (setting.Max != null) json["max"] = setting.Max;

            return json;
        }

        private sealed class Function : LookupFunction
        {
            private readonly IBlishHudWorld _world;

            public Function(IBlishHudWorld world, Action<string> logDebug)
                : base(GetModuleSettingsTool.Name, GetModuleSettingsTool.Description, Schema, logDebug) {
                _world = world;
            }

            protected override async Task<LookupAnswer> InvokeLookupAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) {
                string @namespace = arguments != null && arguments.TryGetValue(NamespaceArgument, out object value)
                                        ? value as string
                                        : null;

                return await RunAsync(_world, @namespace, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
